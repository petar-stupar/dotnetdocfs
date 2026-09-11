using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using DotnetDocFs.Catalog.Internal;
using DotnetDocFs.Catalog.Internal.Metadata;
using DotnetDocFs.Catalog.Internal.Naming;
using DotnetDocFs.Catalog.Internal.Nodes;
using DotnetDocFs.Catalog.Internal.Render;
using DotnetDocFs.Catalog.Internal.Sources;

namespace DotnetDocFs.Catalog;

/// <summary>
/// The tree dotnetdocfs serves: the reference assemblies of the installed .NET, the packages in
/// the NuGet cache, and whatever <c>/ctl</c> has been told to ingest.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is shipped, vendored or downloaded. Every page is rendered, when it is read, from
/// files that already belong to the machine this runs on, so there is no cache on disk and no
/// version of anything to go stale.
/// </para>
/// <para>
/// What is held in memory is a cache, and a bounded one: the parsed documentation files and the
/// member lists a cross-reference consults are kept to a fixed number of entries, and a rendered
/// page's bytes are held only while something is reading them. A directory that has been listed
/// keeps its entries, which is what makes a walked path stable; <c>refresh</c> is how a caller
/// says the machine has changed underneath it.
/// </para>
/// </remarks>
public sealed class DocCatalog : IDisposable
{
    private readonly ConcurrentDictionary<string, DocArea> _areas = new(StringComparer.Ordinal);
    private readonly DocArea? _framework;
    private readonly MutableDirectory _ingested;
    private readonly CatalogOptions _options;
    private readonly string? _packageRoot;
    private readonly Lock _gate = new();
    private readonly List<DocNode> _ingestedNodes = [];
    private readonly LazyDirectory _root;
    private readonly Generation _generation = new();

    /// <summary>
    /// When this catalog was built, and so the <c>timestamp</c> of the pages that describe the
    /// tree itself. A clock read during rendering would stop at whenever the page was first read
    /// and stand still after that, and would break the one thing that lets a page's bytes be
    /// dropped and re-made: that a second render is byte-for-byte the first.
    /// </summary>
    private readonly DateTimeOffset _builtAt = DateTimeOffset.UtcNow;

    private bool _disposed;

    private DocCatalog(CatalogOptions options)
    {
        _options = options;

        string? dotnetRoot = FrameworkLocator.FindDotnetRoot(options.DotnetRoot);
        FrameworkPack? pack = dotnetRoot is null
            ? null
            : FrameworkLocator.FindPack(dotnetRoot, options.TargetFramework);

        FrameworkAssemblies = pack?.Assemblies ?? [];
        TargetFramework = pack?.TargetFramework;
        FrameworkVersion = pack?.Version;

        _packageRoot = PackageLocator.FindRoot(options.PackageRoot);

        if (pack is not null)
        {
            var links = new AreaLinks();

            _framework = new DocArea(
                "framework",
                DocNodeKind.Area,
                TreePath.Root.Add("docs", "framework"),
                TypeIndex.Build(pack.Assemblies),
                FrameworkReader(pack),
                links,
                [new KeyValuePair<string, string>("framework", pack.TargetFramework)],
                $".NET {pack.TargetFramework}",
                $"The public API of the .NET {pack.TargetFramework} reference assemblies, "
                    + $"from reference pack {pack.Version} on this machine.",
                _generation);

            links.Own = _framework;
            links.Framework = _framework;
            _areas["/docs/framework"] = _framework;
        }

        _ingested = new MutableDirectory("ingested", DocNodeKind.Area, "/docs/ingested");
        PublishIngested();

        _root = BuildRoot();
    }

    /// <summary>The root of the served tree.</summary>
    public DocDirectory Root => _root;

    /// <summary>
    /// How many times this catalog has been refreshed. A 9P layer puts it in the qid version, so
    /// that a client caching on the qid — which is what <c>cache=loose</c> does — is told that
    /// what it holds for a path is no longer what the path holds. Not called Version, which
    /// inside this class would shadow <see cref="System.Version"/>.
    /// </summary>
    public uint Revision => (uint)_generation.Current;

    /// <summary>The moniker being served, or null when no reference pack was found.</summary>
    public string? TargetFramework { get; }

    /// <summary>The reference pack version being served, or null.</summary>
    public string? FrameworkVersion { get; }

    /// <summary>How many reference assemblies the framework area covers.</summary>
    public int FrameworkAssemblyCount => FrameworkAssemblies.Count;

    private IReadOnlyList<DocAssembly> FrameworkAssemblies { get; }

    /// <summary>
    /// The reader for the framework area, saying so if there is none.
    /// </summary>
    /// <remarks>
    /// Without a core assembly every resolution fails and every page in the tree renders with no
    /// members — the quietest way this can be wrong, and indistinguishable from a machine whose
    /// types genuinely have none. It is said once, at startup, because it is a property of the
    /// machine rather than of any one page.
    /// </remarks>
    private static AssemblySetReader? FrameworkReader(FrameworkPack pack)
    {
        AssemblySetReader? reader = AssemblySetReader.Create(pack.Assemblies, [], out string? core);

        if (core is null)
        {
            Diagnostics.Report(
                $"the {pack.TargetFramework} reference pack carries no System.Runtime or mscorlib, "
                + "so no type's members can be described; every page will list none");
        }

        return reader;
    }

    /// <summary>The reader for one non-framework area, saying so if there is none.</summary>
    private AssemblySetReader? Reader(IReadOnlyList<DocAssembly> assemblies, string name)
    {
        AssemblySetReader? reader = AssemblySetReader.Create(assemblies, FrameworkAssemblies, out string? core);

        if (core is null)
        {
            Diagnostics.Report(
                $"{name} has no reference pack to resolve against, so its types will list no "
                + "members; install the .NET SDK or pass --dotnet-root");
        }

        return reader;
    }

    /// <summary>Builds a catalog from what <paramref name="options"/> points at.</summary>
    public static DocCatalog Create(CatalogOptions options) => new(options);

    private LazyDirectory BuildRoot() => new LazyDirectory("", DocNodeKind.Catalog, "/", () =>
    {
        var children = new List<DocNode>
        {
            new TextPage("index.md", DocNodeKind.Catalog, "/index.md", RenderRootIndex),
            new LazyDirectory("docs", DocNodeKind.Area, "/docs", BuildDocs, _generation),
            Skills.Directory(_builtAt),
        };

        if (_options.AllowIngest)
        {
            children.Add(new ControlFile(ExecuteAsync));
        }

        return children;
    }, _generation);

    private List<DocNode> BuildDocs()
    {
        var children = new List<DocNode>
        {
            new TextPage("index.md", DocNodeKind.Area, "/docs/index.md", RenderDocsIndex),
        };

        if (_framework is not null)
        {
            children.Add(_framework.Node());
        }

        if (_packageRoot is not null)
        {
            children.Add(new LazyDirectory(
                "packages",
                DocNodeKind.Area,
                "/docs/packages",
                BuildPackages,
                _generation));
        }

        children.Add(_ingested);

        return children;
    }

    private List<DocNode> BuildPackages()
    {
        var children = new List<DocNode>
        {
            new TextPage("index.md", DocNodeKind.Area, "/docs/packages/index.md", RenderPackagesIndex),
        };

        foreach (string directory in Directories(_packageRoot!).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            string id = Path.GetFileName(directory);

            children.Add(new LazyDirectory(
                id,
                DocNodeKind.Package,
                "/docs/packages/" + id,
                () => BuildPackageVersions(id, directory),
                _generation));
        }

        return children;
    }

    /// <summary>
    /// The subdirectories of <paramref name="path"/>, or none when it cannot be read.
    /// </summary>
    /// <remarks>
    /// A NuGet cache is a directory this program did not make and does not own. One entry in it
    /// that the current user cannot read turned an <c>ls</c> of the whole packages area into an
    /// <c>EIO</c> with no explanation anywhere; reporting it once and listing the rest is the
    /// answer that leaves the tree usable.
    /// </remarks>
    private static IEnumerable<string> Directories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Diagnostics.Report($"listing {path}", exception);

            return [];
        }
    }

    /// <summary>
    /// Orders version directories by version rather than by text, so <c>9.0.0</c> comes before
    /// <c>10.0.0</c>. A pre-release suffix sorts after the release it belongs to, which is the
    /// wrong way round for semantic versioning but keeps the listing stable and grouped; the
    /// question here is only what order <c>ls</c> shows.
    /// </summary>
    private static (Version Version, string Name) VersionOrder(string directory)
    {
        string name = Path.GetFileName(directory);
        int dash = name.IndexOf('-', StringComparison.Ordinal);
        ReadOnlySpan<char> numbers = dash < 0 ? name : name.AsSpan(0, dash);

        return (Version.TryParse(numbers, out Version? version) ? version : new Version(0, 0), name);
    }

    /// <summary>
    /// The versions of one package, as directories that describe themselves only once something
    /// walks into them.
    /// </summary>
    /// <remarks>
    /// Listing a package must not build an area for every version of it. An area owns a
    /// <c>MetadataLoadContext</c> with an open handle on each of its assemblies, and a developer's
    /// NuGet cache holds thousands of package-versions: an <c>ls -R</c> over <c>/docs/packages</c>
    /// would open one context per version and hold every one of them until the process exited,
    /// which ends at the open-file limit. What a listing needs is the name and whether there is
    /// anything under it, and both come from the directory.
    /// </remarks>
    private List<DocNode> BuildPackageVersions(string id, string directory)
    {
        var children = new List<DocNode>();

        foreach (string versionDirectory in Directories(directory).OrderBy(VersionOrder))
        {
            string version = Path.GetFileName(versionDirectory);
            TreePath at = TreePath.Root.Add("docs", "packages", id, version);

            if (!PackageLocator.HasAssemblies(versionDirectory))
            {
                // An analyzer or a tools-only package carries no library to describe.
                continue;
            }

            children.Add(new LazyDirectory(
                version,
                DocNodeKind.PackageVersion,
                at.ToString(),
                () => Area(
                        at.ToString(),
                        version,
                        DocNodeKind.PackageVersion,
                        at,
                        PackageLocator.AssembliesIn(versionDirectory),
                        [
                            new KeyValuePair<string, string>("package", id),
                            new KeyValuePair<string, string>("package_version", version),
                        ],
                        $"{id} {version}",
                        $"The public API of {id} {version}, from the NuGet cache on this machine.")
                    .Node()
                    .Children,
                _generation));
        }

        return children;
    }

    /// <summary>
    /// Creates an area under <paramref name="key"/>, disposing whatever was registered there
    /// before it.
    /// </summary>
    /// <remarks>
    /// The key is what makes an area's lifetime end before the process does. Re-ingesting an
    /// assembly, forgetting one, or refreshing all of them each replaces an area, and the one
    /// being replaced holds a <c>MetadataLoadContext</c> with the assembly still open — on
    /// Windows, still locked, so the file cannot even be rebuilt while this is running.
    /// </remarks>
    private DocArea Area(
        string key,
        string name,
        DocNodeKind kind,
        TreePath root,
        IReadOnlyList<DocAssembly> assemblies,
        IReadOnlyList<KeyValuePair<string, string>> frontmatter,
        string title,
        string description,
        TypeIndex? index = null)
    {
        var links = new AreaLinks { Framework = _framework };

        var area = new DocArea(
            name,
            kind,
            root,
            index ?? TypeIndex.Build(assemblies),
            Reader(assemblies, name),
            links,
            frontmatter,
            title,
            description,
            _generation);

        links.Own = area;

        // Under the same lock that guards the node list. Registering the area and publishing the
        // node it belongs to have to be one step: two ingests of one name racing here could
        // otherwise leave the surviving node pointing at the area the other one disposed, which
        // is a directory that lists nothing and never recovers.
        lock (_gate)
        {
            if (_areas.TryRemove(key, out DocArea? displaced))
            {
                displaced.Release();
            }

            _areas[key] = area;
        }

        return area;
    }

    /// <summary>Runs one <c>/ctl</c> command.</summary>
    private ValueTask ExecuteAsync(string command, CancellationToken cancellationToken)
    {
        // Any whitespace separates, not just a space: "ingest<tab>/x.dll" is what a shell gives
        // you after tab completion, and reading it as an unknown command is a poor joke.
        string[] words = command.Split(
            [' ', '\t'],
            2,
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)
        {
            throw new CatalogException(
                "empty command; expected 'ingest <path>', 'forget <name>' or 'refresh'",
                CatalogErrno.InvalidArgument);
        }

        switch (words[0])
        {
            case "ingest" when words.Length == 2:
                Ingest(words[1]);
                break;

            case "forget" when words.Length == 2:
                Forget(words[1]);
                break;

            case "refresh":
                Refresh();
                break;

            default:
                throw new CatalogException(
                    $"unknown command '{words[0]}'; expected 'ingest <path>', 'forget <name>' or 'refresh'",
                    CatalogErrno.InvalidArgument);
        }

        return ValueTask.CompletedTask;
    }

    private void Ingest(string path)
    {
        string full = Path.GetFullPath(path);

        if (!File.Exists(full))
        {
            throw new CatalogException($"no file at {full}", CatalogErrno.InvalidArgument);
        }

        var info = new FileInfo(full);

        // A character device reports length 0 and would sail past the size check into a read that
        // never ends. Only an ordinary file on disk can be an assembly.
        //
        // A symbolic link is fine: Homebrew and most build outputs are one, and refusing the last
        // component while accepting a linked directory anywhere earlier in the path bought
        // nothing at all. What matters is what it points at, which is what FileInfo reports
        // anyway.
        if ((info.Attributes & FileAttributes.Directory) != 0 || info.Length == 0)
        {
            throw new CatalogException(
                $"{full} is not an ordinary file",
                CatalogErrno.InvalidArgument);
        }

        if (info.Length > _options.MaxIngestBytes)
        {
            throw new CatalogException(
                $"{full} is {info.Length} bytes, over the {_options.MaxIngestBytes} byte limit",
                CatalogErrno.TooLarge);
        }

        DocAssembly assembly = DocAssembly.Describe(full);

        // The whole point of CatalogException is that a refusal reaches the client as a sentence
        // and a number. PEReader throws BadImageFormatException on anything that is not a managed
        // assembly — which is the likeliest thing a caller gets wrong — so without this the
        // careful message below is unreachable and what arrives instead is whatever the 9P layer
        // does with an exception it did not expect.
        var unreadable = new List<string>();
        TypeIndex index = TypeIndex.Build([assembly], unreadable);

        if (unreadable.Count > 0)
        {
            throw new CatalogException(
                $"{full} could not be read as a managed assembly",
                CatalogErrno.InvalidArgument);
        }

        if (index.Namespaces.Length == 0)
        {
            throw new CatalogException(
                $"{full} holds no public types",
                CatalogErrno.InvalidArgument);
        }

        TreePath root = TreePath.Root.Add("docs", "ingested", assembly.SimpleName);

        // The index just built is handed on rather than rebuilt: reading the metadata tables of
        // the same file twice in one command is the sort of thing nobody notices until the file
        // is large.
        DocArea area = Area(
            root.ToString(),
            assembly.SimpleName,
            DocNodeKind.Area,
            root,
            [assembly],
            [new KeyValuePair<string, string>("source", full)],
            assembly.SimpleName,
            $"The public API of {assembly.SimpleName}, ingested from {full}.",
            index);

        lock (_gate)
        {
            _ingestedNodes.RemoveAll(node => node.Name == assembly.SimpleName);
            _ingestedNodes.Add(area.Node());
            PublishIngested();
        }
    }

    private void Forget(string name)
    {
        lock (_gate)
        {
            if (_ingestedNodes.RemoveAll(node => node.Name == name) == 0)
            {
                throw new CatalogException(
                    $"nothing ingested under the name '{name}'",
                    CatalogErrno.InvalidArgument);
            }

            PublishIngested();
        }

        // Dropping the node is not enough: the area behind it still holds the assembly open, and
        // on Windows still locks it, for the life of the process. "forget" has to mean it. The
        // area is released rather than disposed so that a client still holding a fid into it
        // keeps reading what it walked into instead of an empty directory; see DocArea.Release.
        if (_areas.TryRemove("/docs/ingested/" + name, out DocArea? area))
        {
            area.Release();
        }
    }

    /// <summary>
    /// Drops everything read from the machine so the next walk reads it again.
    /// </summary>
    /// <remarks>
    /// A directory keeps its entries once something has listed it, which is what makes a walked
    /// path stable — so restoring a package does not appear under <c>/docs/packages</c> until
    /// this is run. Rebuilding the root is what makes the promise good; the framework area is
    /// kept, because the reference pack it reads does not change under a running process.
    /// </remarks>
    private void Refresh()
    {
        // One increment reaches every directory, including the ones a client already holds a fid
        // to. Invalidating from the root alone reached only what a fresh walk would rebuild
        // anyway, which is not what a mounted reader is looking at: a kernel client holds a fid
        // per directory it has visited, for as long as its dentry cache does, and every one of
        // them kept listing the old entries. The keys the factories produce are the paths,
        // unchanged, so a re-walk keeps the same qid.
        _generation.Advance();

        lock (_gate)
        {
            _ingestedNodes.Clear();
            PublishIngested();
        }

        foreach (KeyValuePair<string, DocArea> entry in _areas)
        {
            if (!ReferenceEquals(entry.Value, _framework) && _areas.TryRemove(entry.Key, out DocArea? area))
            {
                area.Release();
            }
        }
    }

    /// <summary>
    /// Republishes <c>/docs/ingested</c>, index page first.
    /// </summary>
    /// <remarks>
    /// The index is rebuilt rather than kept, because it lists what is ingested and a page's
    /// bytes are fixed once rendered. Its key does not change, so a client that has walked to it
    /// keeps the same qid.
    /// </remarks>
    private void PublishIngested()
    {
        string[] names = [.. _ingestedNodes.Select(node => node.Name)];

        _ingested.Replace(
        [
            new TextPage(
                "index.md",
                DocNodeKind.Area,
                "/docs/ingested/index.md",
                () => CatalogText.IngestedIndex(names, _builtAt, _options.AllowIngest)),
            .. _ingestedNodes,
        ]);
    }

    private string RenderRootIndex() => CatalogText.RootIndex(this, _packageRoot, _options.AllowIngest, _builtAt);

    private string RenderDocsIndex() => CatalogText.DocsIndex(this, _packageRoot, _builtAt);

    private string RenderPackagesIndex() => CatalogText.PackagesIndex(_packageRoot!, _builtAt);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (DocArea area in _areas.Values)
        {
            area.Dispose();
        }

        _areas.Clear();
    }
}
