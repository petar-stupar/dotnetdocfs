using System.Text;
using DotnetDocFs.Catalog.Internal.Naming;
using Xunit;

namespace DotnetDocFs.Catalog.Tests;

/// <summary>
/// The catalog over whatever .NET this machine has, exercised through the public surface a
/// mounted client sees. Each of these pins a promise the README makes.
/// </summary>
public class CatalogTests : IDisposable
{
    private readonly DocCatalog _catalog = DocCatalog.Create(new CatalogOptions());

    /// <inheritdoc />
    public void Dispose()
    {
        _catalog.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TheRootCarriesAnIndexAndTheDocumentationTree()
    {
        Assert.NotNull(_catalog.Root.Find("index.md"));
        Assert.NotNull(_catalog.Root.Find("docs"));
        Assert.NotNull(_catalog.Root.Find("ctl"));
    }

    /// <summary>
    /// The headline promise: every relative link a page writes resolves to a page that is there.
    /// </summary>
    /// <remarks>
    /// Listing the whole framework area is cheap — it reads names out of the metadata tables and
    /// opens no documentation file — so the set of pages that exist is the real one. Rendering is
    /// the expensive half, so only a slice of it is rendered; a cref from that slice may point
    /// anywhere, which is the case that matters and the one a slice-against-slice check would
    /// throw away.
    /// </remarks>
    [Fact]
    public async Task EveryInternalLinkResolvesToAPageThatExists()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        DocDirectory framework = Framework();

        var exists = new HashSet<string>(StringComparer.Ordinal);
        List(framework, "/docs/framework", exists);

        // The catalog's own pages, three levels down: enough for everything /index.md,
        // /docs/index.md and /skills link to, and shallow enough not to walk the NuGet cache,
        // which would build an area per package version to answer a question about link targets.
        List(_catalog.Root, string.Empty, exists, depth: 3);

        Assert.NotEmpty(exists);

        // A sample of namespaces that is not the first twelve in ordinal order — on a machine
        // with the ASP.NET pack those are all Microsoft.AspNetCore.* and none of them is System.
        var broken = new List<string>();
        int checked_ = 0;

        var rendered = new Dictionary<string, string>(StringComparer.Ordinal);
        await Collect(_catalog.Root, string.Empty, rendered, token, pagesOnly: true);

        foreach (DocDirectory area in Sample(framework))
        {
            rendered.Clear();
            await Collect(area, "/docs/framework/" + area.Name, rendered, token);

            foreach ((string from, string text) in rendered)
            {
                foreach (string href in Links(text))
                {
                    checked_++;

                    string at = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(from)!, href))
                        .Replace('\\', '/');

                    if (!exists.Contains(at))
                    {
                        broken.Add($"{from} -> {href}");
                    }
                }
            }
        }

        Assert.True(checked_ > 100, $"only {checked_} links were checked; the crawl found nothing");
        Assert.Empty(broken);
    }

    /// <summary>
    /// A cross-reference to a member the tree has no page for is not written as a link.
    /// <c>AssemblyBuilderAccess.Save</c> is the case that exists in the shipped documentation of
    /// .NET 10: the type is there, the member was removed, and the prose still names it.
    /// </summary>
    [Fact]
    public void ATypeThatResolvesDoesNotMakeEveryMemberOfItResolve()
    {
        DocDirectory? type = Walk(Framework(), "System.Reflection.Emit", "AssemblyBuilderAccess");

        Assert.NotNull(type);
        Assert.NotNull(type.Find("index.md"));
        Assert.Null(type.Find("Save.md"));
    }

    /// <summary>
    /// The compiler's backing field for an enum is not a page. It is documented nowhere and is on
    /// every enum in the tree, so it is one empty file per enum for a reader to open.
    /// </summary>
    [Fact]
    public void TheStorageFieldOfAnEnumIsNotServed()
    {
        DocDirectory? type = Walk(Framework(), "System", "DayOfWeek");

        Assert.NotNull(type);
        Assert.NotNull(type.Find("Monday.md"));
        Assert.Null(type.Find("value__.md"));
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("ingest")]
    [InlineData("ingest /no/such/file.dll")]
    public async Task ACommandTheControlFileCannotCarryOutIsRefusedWithItsReason(string command)
    {
        var control = (DocControl)_catalog.Root.Find("ctl")!;

        CatalogException refused = await Assert.ThrowsAsync<CatalogException>(
            async () => await control.ExecuteAsync(command, TestContext.Current.CancellationToken));

        Assert.Equal(CatalogErrno.InvalidArgument, refused.Errno);
        Assert.NotEmpty(refused.Message);
    }

    /// <summary>
    /// A file that is not a managed assembly is refused the same way. Everything below
    /// <c>ingest</c> goes through <c>PEReader</c>, which throws rather than returning, and an
    /// exception that is not a <see cref="CatalogException"/> never reaches the client as a
    /// reason at all.
    /// </summary>
    [Fact]
    public async Task IngestingSomethingThatIsNotAnAssemblyIsRefusedWithItsReason()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        var control = (DocControl)_catalog.Root.Find("ctl")!;

        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".dll");
        await File.WriteAllTextAsync(path, "this is not a portable executable", token);

        try
        {
            CatalogException refused = await Assert.ThrowsAsync<CatalogException>(
                async () => await control.ExecuteAsync($"ingest {path}", token));

            Assert.Equal(CatalogErrno.InvalidArgument, refused.Errno);
            Assert.Contains(path, refused.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A page renders once and keeps the size it reported. A client stats before it reads, and a
    /// second render that produced a different length would make the two disagree.
    /// </summary>
    [Fact]
    public async Task ThePageSizeAClientStatsIsTheLengthItThenReads()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        var page = (DocPage)Framework().Find("index.md")!;

        ulong size = await page.SizeAsync(token);

        Assert.Equal(size, (ulong)(await page.ContentAsync(token)).Length);
        Assert.Equal(size, await page.SizeAsync(token));
    }

    /// <summary>Every page opens with the frontmatter block, which is what makes this OKF.</summary>
    [Fact]
    public async Task EveryPageOpensWithFrontmatterNamingItsType()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        var pages = new Dictionary<string, string>(StringComparer.Ordinal);

        await Collect(Framework().Children.OfType<DocDirectory>().First(), "/x", pages, token);

        Assert.All(pages.Values, text =>
        {
            Assert.StartsWith("---\n", text, StringComparison.Ordinal);
            Assert.Contains("\ntype: ", text, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// The framework area, skipping the test when this machine has no reference pack. A
    /// contributor without the SDK should see a skip rather than a failure that names nothing
    /// they did.
    /// </summary>
    private DocDirectory Framework()
    {
        var docs = (DocDirectory)_catalog.Root.Find("docs")!;

        Assert.SkipWhen(docs.Find("framework") is null, "no .NET reference pack is installed here");

        return (DocDirectory)docs.Find("framework")!;
    }

    /// <summary>
    /// A spread of namespaces rather than the first few: every distinct top-level prefix, so that
    /// <c>System</c> is always in it whatever else the machine has installed.
    /// </summary>
    private static IEnumerable<DocDirectory> Sample(DocDirectory framework) =>
        framework.Children.OfType<DocDirectory>()
            .GroupBy(area => area.Name.Split('.')[0], StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(12);

    /// <summary>Records the path of every page under <paramref name="directory"/>.</summary>
    private static void List(DocDirectory directory, string path, HashSet<string> into, int depth = int.MaxValue)
    {
        foreach (DocNode child in directory.Children)
        {
            if (child is DocDirectory sub)
            {
                if (depth > 1)
                {
                    List(sub, path + "/" + sub.Name, into, depth - 1);
                }
            }
            else
            {
                into.Add(path + "/" + child.Name);
            }
        }
    }

    private static DocDirectory? Walk(DocDirectory from, params string[] names)
    {
        DocDirectory? at = from;

        foreach (string name in names)
        {
            at = at?.Find(name) as DocDirectory;
        }

        return at;
    }

    private static async Task Collect(
        DocDirectory directory,
        string path,
        Dictionary<string, string> into,
        CancellationToken token,
        bool pagesOnly = false)
    {
        foreach (DocNode child in directory.Children)
        {
            switch (child)
            {
                case DocPage page:
                    into[path + "/" + page.Name] = Encoding.UTF8.GetString((await page.ContentAsync(token)).Span);
                    break;

                case DocDirectory sub when !pagesOnly:
                    await Collect(sub, path + "/" + sub.Name, into, token);
                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>Every markdown link destination that points inside this tree.</summary>
    private static IEnumerable<string> Links(string text)
    {
        for (int at = text.IndexOf("](", StringComparison.Ordinal); at >= 0;
            at = text.IndexOf("](", at + 2, StringComparison.Ordinal))
        {
            int end = text.IndexOf(')', at + 2);

            if (end < 0)
            {
                yield break;
            }

            string href = text[(at + 2)..end];

            if (!href.Contains("://", StringComparison.Ordinal))
            {
                yield return href;
            }
        }
    }
}

/// <summary>
/// Names that would collide with something the tree already serves, or that a filesystem cannot
/// carry. Nothing in the base class library reaches these; <c>/ctl</c> takes assemblies no
/// compiler here built.
/// </summary>
public class ReservedNameTests
{
    [Fact]
    public void AMemberCalledIndexDoesNotTakeTheDirectorysOwnIndexPage() =>
        Assert.NotEqual("index", PathName.ForMember("index"));

    [Theory]
    [InlineData("Index")]
    [InlineData("INDEX")]
    public void TheReservationHoldsOnACaseInsensitiveMount(string name) =>
        Assert.NotEqual("index", PathName.ForMember(name).ToLowerInvariant());

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public void ANameMadeOnlyOfDotsIsNotADirectorysOwnEntry(string name)
    {
        Assert.NotEqual(name, PathName.ForType(name));
        Assert.NotEqual(name, PathName.ForMember(name));
    }

    [Theory]
    [InlineData("Trailing.")]
    [InlineData("Trailing ")]
    public void ANameWindowsWouldSilentlyTrimIsTrimmedHere(string name)
    {
        string mangled = PathName.ForMember(name);

        Assert.False(mangled[^1] is '.' or ' ', $"'{mangled}' still ends in a dot or a space");
    }

    [Fact]
    public void AVeryLongNameIsBroughtUnderTheLimitStatFsAdvertises()
    {
        string mangled = PathName.ForMember(new string('a', 4000));

        Assert.True(mangled.Length <= 250, $"{mangled.Length} bytes is over what Tstatfs promises");
    }

    [Fact]
    public void TwoLongNamesSharingAPrefixStayTwoNames()
    {
        string first = PathName.ForMember(new string('a', 4000) + "One");
        string second = PathName.ForMember(new string('a', 4000) + "Two");

        Assert.NotEqual(first, second);
    }
}
