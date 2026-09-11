using DotnetDocFs.Catalog.Internal;
using DotnetDocFs.Catalog.Internal.Sources;
using Xunit;

namespace DotnetDocFs.Catalog.Tests;

/// <summary>
/// What <c>refresh</c> means to a client that is already holding a directory. A 9P client keeps a
/// fid per directory it has walked into, so "the next walk from the root sees it" is not the
/// question; "the directory this client is holding sees it" is.
/// </summary>
public sealed class RefreshTests : IDisposable
{
    private readonly string _cache = Path.Combine(
        Path.GetTempPath(),
        "dotnetdocfs-tests-" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_cache, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Never created, because the test skipped.
        }
    }

    [Fact]
    public async Task ADirectoryAClientIsAlreadyHoldingSeesWhatRefreshPickedUp()
    {
        CancellationToken token = TestContext.Current.CancellationToken;

        AddPackage("aaa");

        using DocCatalog catalog = DocCatalog.Create(new CatalogOptions { PackageRoot = _cache });

        var docs = (DocDirectory)catalog.Root.Find("docs")!;
        var packages = (DocDirectory)docs.Find("packages")!;

        Assert.Contains("aaa", packages.Children.Select(child => child.Name));
        Assert.DoesNotContain("bbb", packages.Children.Select(child => child.Name));

        AddPackage("bbb");
        await ((DocControl)catalog.Root.Find("ctl")!).ExecuteAsync("refresh", token);

        // The objects held from before the refresh, not a fresh walk from the root.
        Assert.Contains("bbb", packages.Children.Select(child => child.Name));
        Assert.Contains("bbb", ((DocDirectory)docs.Find("packages")!).Children.Select(child => child.Name));
    }

    [Fact]
    public async Task RefreshMovesTheRevisionSoAQidCanSayTheContentChanged()
    {
        CancellationToken token = TestContext.Current.CancellationToken;

        AddPackage("aaa");

        using DocCatalog catalog = DocCatalog.Create(new CatalogOptions { PackageRoot = _cache });

        uint before = catalog.Revision;
        await ((DocControl)catalog.Root.Find("ctl")!).ExecuteAsync("refresh", token);

        Assert.NotEqual(before, catalog.Revision);
    }

    /// <summary>
    /// An area released by a refresh keeps serving what a held fid walked into. It is released —
    /// its file handles given up — rather than disposed, because the directories a client is
    /// holding were built by closures that captured it.
    /// </summary>
    [Fact]
    public async Task ATypeHeldAcrossARefreshStillListsItsMembers()
    {
        CancellationToken token = TestContext.Current.CancellationToken;

        AddPackage("aaa");

        using DocCatalog catalog = DocCatalog.Create(new CatalogOptions { PackageRoot = _cache });

        var packages = (DocDirectory)((DocDirectory)catalog.Root.Find("docs")!).Find("packages")!;
        var version = (DocDirectory)((DocDirectory)packages.Find("aaa")!).Find("1.0.0")!;

        DocDirectory? type = version.Children.OfType<DocDirectory>()
            .SelectMany(space => space.Children.OfType<DocDirectory>())
            .FirstOrDefault(candidate => candidate.Children.Count > 1);

        Assert.SkipWhen(type is null, "the sample package produced no type with members");

        int before = type!.Children.Count;

        await ((DocControl)catalog.Root.Find("ctl")!).ExecuteAsync("refresh", token);

        Assert.Equal(before, type.Children.Count);
    }

    private void AddPackage(string id)
    {
        string? root = FrameworkLocator.FindDotnetRoot(configured: null);

        Assert.SkipWhen(root is null, "no .NET installation to take a sample assembly from");

        string? source = Directory
            .EnumerateDirectories(Path.Combine(root!, "packs", "Microsoft.NETCore.App.Ref"))
            .SelectMany(pack => Directory.EnumerateDirectories(Path.Combine(pack, "ref")))
            .FirstOrDefault();

        Assert.SkipWhen(source is null, "no reference pack to take a sample assembly from");

        string lib = Path.Combine(_cache, id, "1.0.0", "lib", "net10.0");
        Directory.CreateDirectory(lib);

        File.Copy(
            Path.Combine(source!, "System.Linq.dll"),
            Path.Combine(lib, id + ".dll"),
            overwrite: true);
    }
}

/// <summary>
/// The cache that forgets. Its policy is load-bearing — it is the only thing keeping the memory
/// and the open file handles of a long-lived mount bounded.
/// </summary>
public class BoundedTests
{
    [Fact]
    public void TheLeastRecentlyUsedEntryIsTheOneDropped()
    {
        var evicted = new List<int>();
        var cache = new Bounded<int, int>(2, comparer: null, onEvict: (key, _) => evicted.Add(key));

        cache.Get(1, key => key);
        cache.Get(2, key => key);
        cache.Get(1, key => key);   // 1 is now the most recent, so 2 is next out
        cache.Get(3, key => key);

        Assert.Equal([2], evicted);
    }

    [Fact]
    public void AValueIsProducedOnceAndThenHeld()
    {
        int made = 0;
        var cache = new Bounded<string, string>(4);

        for (int at = 0; at < 5; at++)
        {
            Assert.Equal("value", cache.Get("key", _ =>
            {
                made++;

                return "value";
            }));
        }

        Assert.Equal(1, made);
    }

    /// <summary>
    /// A failed production is not remembered. A <c>Lazy</c> built for execution and publication
    /// caches the exception it threw, which would answer the same error for the life of the entry
    /// rather than trying again.
    /// </summary>
    [Fact]
    public void AFailedProductionIsRetriedRatherThanRemembered()
    {
        var cache = new Bounded<string, string>(4);
        int attempts = 0;

        Assert.Throws<InvalidOperationException>(() => cache.Get("key", _ =>
        {
            attempts++;

            throw new InvalidOperationException("no");
        }));

        Assert.Equal("second time lucky", cache.Get("key", _ =>
        {
            attempts++;

            return "second time lucky";
        }));

        Assert.Equal(2, attempts);
    }

    [Fact]
    public void ClearingTellsTheHookAboutEveryEntryItHeld()
    {
        var evicted = new List<int>();
        var cache = new Bounded<int, int>(8, comparer: null, onEvict: (key, _) => evicted.Add(key));

        cache.Get(1, key => key);
        cache.Get(2, key => key);
        cache.Clear();

        Assert.Equal([1, 2], [.. evicted.Order()]);
    }
}
