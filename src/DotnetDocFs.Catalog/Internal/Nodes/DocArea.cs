using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using DotnetDocFs.Catalog.Internal;
using DotnetDocFs.Catalog.Internal.Metadata;
using DotnetDocFs.Catalog.Internal.Naming;
using DotnetDocFs.Catalog.Internal.Render;
using DotnetDocFs.Catalog.Internal.Sources;
using DotnetDocFs.Catalog.Internal.Xml;

namespace DotnetDocFs.Catalog.Internal.Nodes;

/// <summary>
/// One body of assemblies served as a subtree: the framework reference pack, one version of one
/// NuGet package, or one assembly handed to <c>/ctl</c>. An area owns the index of what is in it,
/// the reader that describes a type's members, and the documentation files beside its assemblies.
/// </summary>
internal sealed class DocArea : IDisposable
{
    /// <summary>The directory a type with no namespace is served under.</summary>
    private const string GlobalNamespace = "global";

    /// <summary>
    /// How many parsed documentation files are held at once. A reader works through one namespace
    /// at a time and a namespace's types come from one assembly, so a handful covers any real
    /// read pattern; what this buys is that a crawl of the whole tree costs a bounded amount of
    /// memory instead of every XML file the tree has ever touched.
    /// </summary>
    private const int OpenDocumentationFiles = 8;

    /// <summary>
    /// How many types' member lists are held at once. Describing a type's members is the
    /// expensive half of a page and the answer is wanted three times in quick succession — for
    /// the directory entries, for the type's index, and again for each member page — so they are
    /// worth keeping. Keeping <em>all</em> of them is what is not: the declaration and
    /// documentation id of every member of the framework is the single largest thing this tree
    /// can retain, and a crawl asks for all of them.
    /// </summary>
    private const int RememberedMemberLists = 512;

    private readonly Bounded<string, XmlDocFile> _docs =
        new(OpenDocumentationFiles, StringComparer.Ordinal);

    private readonly Bounded<string, IReadOnlyList<MemberGroup>> _members =
        new(RememberedMemberLists, StringComparer.Ordinal);

    private readonly ILinkResolver _links;
    private readonly AssemblySetReader? _reader;
    private readonly Generation _generation;

    /// <summary>
    /// When this area entered the tree, and so the <c>timestamp</c> of every page in it.
    /// </summary>
    /// <remarks>
    /// Not the moment of rendering. A page is rendered on the first read and the bytes are then
    /// fixed, so a rendering clock stops at whenever that first read happened and reports an
    /// arbitrary stale time forever after — and a page that re-renders would change length under
    /// a client that had already stated its size. One time for the area is both stable and true:
    /// it is when this body of documentation was taken from the machine.
    /// </remarks>
    private readonly DateTimeOffset _builtAt = DateTimeOffset.UtcNow;

    internal DocArea(
        string name,
        DocNodeKind kind,
        TreePath root,
        TypeIndex index,
        AssemblySetReader? reader,
        ILinkResolver links,
        IReadOnlyList<KeyValuePair<string, string>> frontmatter,
        string title,
        string description,
        Generation generation)
    {
        _generation = generation;
        Name = name;
        Kind = kind;
        Root = root;
        Index = index;
        Title = title;
        Description = description;
        Frontmatter = frontmatter;
        _reader = reader;
        _links = links;
    }

    /// <summary>The single path element this area is served under.</summary>
    internal string Name { get; }

    /// <summary>What this area is, for the OKF <c>type</c> of its own index page.</summary>
    internal DocNodeKind Kind { get; }

    /// <summary>Where this area sits in the tree.</summary>
    internal TreePath Root { get; }

    /// <summary>Every public type in this area.</summary>
    internal TypeIndex Index { get; }

    /// <summary>The heading of this area's index page.</summary>
    internal string Title { get; }

    /// <summary>The one-line description of this area.</summary>
    internal string Description { get; }

    /// <summary>Fields added to the frontmatter of every page in this area.</summary>
    internal IReadOnlyList<KeyValuePair<string, string>> Frontmatter { get; }

    /// <summary>The directory node for this whole area.</summary>
    internal DocDirectory Node() => new LazyDirectory(Name, Kind, Root.ToString(), BuildNamespaces, _generation);

    /// <summary>
    /// The page of <paramref name="type"/>, walking out through its declaring types so a nested
    /// type is served below the type that declares it.
    /// </summary>
    internal TreePath PathOfType(TypeRecord type)
    {
        var chain = new List<string>();

        for (TypeRecord? current = type; current is not null;)
        {
            chain.Insert(0, current.PathName);
            current = current.DeclaringFullName is null ? null : Index.Find(current.DeclaringFullName);
        }

        TreePath at = Root.Add(type.Namespace.Length == 0 ? GlobalNamespace : type.Namespace);

        foreach (string segment in chain)
        {
            at = at.Add(segment);
        }

        return at;
    }

    private List<DocNode> BuildNamespaces()
    {
        var children = new List<DocNode>
        {
            new TextPage("index.md", Kind, Root.Add("index.md").ToString(), RenderAreaIndex),
        };

        foreach (string namespaceName in Index.Namespaces)
        {
            string directory = namespaceName.Length == 0 ? GlobalNamespace : namespaceName;
            TreePath at = Root.Add(directory);

            children.Add(new LazyDirectory(
                directory,
                DocNodeKind.Namespace,
                at.ToString(),
                () => BuildTypes(namespaceName, at),
                _generation));
        }

        return children;
    }

    private List<DocNode> BuildTypes(string namespaceName, TreePath at)
    {
        var children = new List<DocNode>
        {
            new TextPage(
                "index.md",
                DocNodeKind.Namespace,
                at.Add("index.md").ToString(),
                () => RenderNamespaceIndex(namespaceName, at.Add("index.md"))),
        };

        foreach (TypeRecord type in Index.TypesIn(namespaceName))
        {
            children.Add(TypeNode(type));
        }

        return children;
    }

    private LazyDirectory TypeNode(TypeRecord type)
    {
        TreePath at = PathOfType(type);

        return new LazyDirectory(
            type.PathName,
            DocNodeKind.Type,
            at.ToString(),
            () => BuildMembers(type, at),
            _generation);
    }

    /// <summary>
    /// The entries under a type: its index, a page per member name, and a directory per nested
    /// type.
    /// </summary>
    /// <remarks>
    /// The member list is read to know what the entries are and then let go. Holding it in the
    /// render closures instead would keep every member's declaration and documentation id alive
    /// for as long as the directory is listed, and a crawl lists all of them: that is the largest
    /// thing this tree can retain, and none of it is needed until a page is actually read.
    /// </remarks>
    private List<DocNode> BuildMembers(TypeRecord type, TreePath at)
    {
        IReadOnlyList<MemberGroup> members = MembersOf(type);

        var children = new List<DocNode>
        {
            new TextPage(
                "index.md",
                DocNodeKind.Type,
                at.Add("index.md").ToString(),
                () => RenderTypeIndex(type, MembersOf(type), at.Add("index.md"))),
        };

        // A member page and a nested type could both want one name. The nested type is a
        // directory and the member a .md file, so the two never collide on a filesystem.
        foreach (MemberGroup group in members)
        {
            string file = group.PathName + ".md";
            string stem = group.PathName;

            children.Add(new TextPage(
                file,
                DocNodeKind.Member,
                at.Add(file).ToString(),
                () => RenderMemberPage(type, stem, at.Add(file))));
        }

        foreach (TypeRecord nested in type.Nested)
        {
            children.Add(TypeNode(nested));
        }

        return children;
    }

    /// <summary>The documentation file beside <paramref name="assembly"/>, parsed and held.</summary>
    private XmlDocFile DocsOf(DocAssembly assembly) => assembly.XmlPath is null
        ? XmlDocFile.Empty
        : _docs.Get(assembly.XmlPath, XmlDocFile.Load);

    /// <summary>
    /// Whether this area serves a page named <paramref name="file"/> under
    /// <paramref name="type"/>. A cross-reference asks before it is written as a link.
    /// </summary>
    /// <remarks>
    /// A type resolving is not enough to know that one of its members does. A cref survives the
    /// API it names: <c>AssemblyBuilderAccess.Save</c> is still described in prose that ships with
    /// .NET 10, where the member was removed years ago. Non-public members, property and event
    /// accessors, and every member of a type whose dependencies would not resolve are all in the
    /// same position — the type page exists, the member page does not.
    /// </remarks>
    internal bool HasMemberPage(TypeRecord type, string file) =>
        MembersOf(type).Any(group => group.PathName + ".md" == file);

    private Frontmatter Begin(string okfType, string title, string description)
    {
        var frontmatter = new Frontmatter(okfType).Add("title", title).Add("description", description);

        foreach ((string name, string value) in Frontmatter)
        {
            frontmatter.Add(name, value);
        }

        return frontmatter.AddTimestamp(_builtAt);
    }

    private string RenderAreaIndex()
    {
        TreePath page = Root.Add("index.md");
        var builder = new StringBuilder();

        builder.Append(Begin(OkfType.Of(Kind), Title, Description)
            .AddList("tags", ["dotnet", Name])
            .ToString());

        builder.Append("# ").Append(Title).Append("\n\n").Append(Description).Append("\n\n");
        builder.Append("## Namespaces\n\n");

        foreach (string namespaceName in Index.Namespaces)
        {
            string directory = namespaceName.Length == 0 ? GlobalNamespace : namespaceName;
            int count = Index.TypesIn(namespaceName).Length;

            builder.Append("- [").Append(namespaceName.Length == 0 ? "(global namespace)" : namespaceName)
                .Append("](").Append(Root.Add(directory, "index.md").RelativeToPageIn(page)).Append(") — ")
                .Append(count).Append(count == 1 ? " type\n" : " types\n");
        }

        return builder.ToString();
    }

    private string RenderNamespaceIndex(string namespaceName, TreePath page)
    {
        ImmutableArray<TypeRecord> types = Index.TypesIn(namespaceName);
        string title = namespaceName.Length == 0 ? "(global namespace)" : namespaceName;

        var builder = new StringBuilder();

        builder.Append(Begin(
                OkfType.Of(DocNodeKind.Namespace),
                title,
                $"{types.Length} public types in the {title} namespace.")
            .Add("namespace", namespaceName)
            .AddList("tags", ["dotnet", "namespace"])
            .ToString());

        builder.Append("# ").Append(title).Append("\n\n");

        var markdown = new DocMarkdown(_links, page);

        foreach (IGrouping<TypeShape, TypeRecord> group in types.GroupBy(type => type.Kind).OrderBy(group => group.Key))
        {
            builder.Append("## ").Append(Plural(group.Key)).Append("\n\n");

            foreach (TypeRecord type in group)
            {
                builder.Append("- [").Append(type.DisplayName).Append("](")
                    .Append(PathOfType(type).Add("index.md").RelativeToPageIn(page)).Append(')');

                string summary = markdown.Inline(DocsOf(type.Assembly).Find(type.DocId)?.Element("summary"));

                if (summary.Length > 0)
                {
                    builder.Append(" — ").Append(summary);
                }

                builder.Append('\n');
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    private string RenderTypeIndex(TypeRecord type, IReadOnlyList<MemberGroup> members, TreePath page)
    {
        XmlDocFile docs = DocsOf(type.Assembly);
        XElement? documentation = docs.Find(type.DocId);
        var markdown = new DocMarkdown(_links, page);

        string summary = markdown.Block(documentation?.Element("summary"));

        var builder = new StringBuilder();

        builder.Append(Begin(
                OkfType.Of(DocNodeKind.Type),
                type.DisplayName,
                markdown.Plain(documentation?.Element("summary")))
            .Add("namespace", type.Namespace)
            .Add("assembly", type.Assembly.SimpleName)
            .Add("kind", type.Kind.ToString().ToLowerInvariant())
            .Add("uid", type.FullName)
            .AddList("tags", ["dotnet", type.Kind.ToString().ToLowerInvariant()])
            .ToString());

        builder.Append("# ").Append(type.DisplayName).Append(' ')
            .Append(type.Kind.ToString().ToLowerInvariant()).Append("\n\n");

        if (type.Namespace.Length > 0)
        {
            builder.Append("```csharp\nnamespace ").Append(type.Namespace).Append(";\n```\n\n");
        }

        if (summary.Length > 0)
        {
            builder.Append(summary).Append("\n\n");
        }

        AppendSection(builder, markdown, documentation, "remarks", "Remarks");
        AppendSection(builder, markdown, documentation, "example", "Example");

        foreach (IGrouping<MemberSort, MemberGroup> group in members.GroupBy(member => member.Kind))
        {
            builder.Append("## ").Append(Plural(group.Key)).Append("\n\n");

            foreach (MemberGroup member in group)
            {
                // A member page is a sibling of this index, so its file name is the whole link —
                // encoded like every other link, which this one was not.
                builder.Append("- [").Append(MemberLabel(member.Name, type))
                    .Append("](").Append(TreePath.Encode(member.PathName + ".md")).Append(')');

                string first = markdown.Inline(docs.Find(member.Signatures[0].DocId)?.Element("summary"));

                if (first.Length > 0)
                {
                    builder.Append(" — ").Append(first);
                }

                if (member.Signatures.Count > 1)
                {
                    builder.Append(" (").Append(member.Signatures.Count).Append(" overloads)");
                }

                builder.Append('\n');
            }

            builder.Append('\n');
        }

        if (type.Nested.Count > 0)
        {
            builder.Append("## Nested types\n\n");

            foreach (TypeRecord nested in type.Nested)
            {
                builder.Append("- [").Append(nested.DisplayName).Append("](")
                    .Append(PathOfType(nested).Add("index.md").RelativeToPageIn(page)).Append(")\n");
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>The members of <paramref name="type"/>, described once and then held for a while.</summary>
    private IReadOnlyList<MemberGroup> MembersOf(TypeRecord type) =>
        _members.Get(type.FullName, _ => _reader?.MembersOf(type) ?? []);

    private string RenderMemberPage(TypeRecord type, string stem, TreePath page)
    {
        MemberGroup? group = MembersOf(type).FirstOrDefault(member => member.PathName == stem);

        if (group is null)
        {
            // The only way here is a member that was there when the directory was listed and is
            // not there now, which means the assembly changed underneath a running server.
            return Begin(OkfType.Of(DocNodeKind.Member), stem, "This member is no longer present.")
                + $"# {stem}\n\nThis page was listed from {type.Assembly.SimpleName}, which no longer "
                + "describes a member of this name.\n";
        }

        XmlDocFile docs = DocsOf(type.Assembly);
        var markdown = new DocMarkdown(_links, page);

        string heading = group.Name == ".ctor" ? type.DisplayName + " constructors" : $"{type.DisplayName}.{group.Name}";

        XElement? first = docs.Find(group.Signatures[0].DocId);

        var builder = new StringBuilder();

        builder.Append(Begin(OkfType.Of(DocNodeKind.Member), heading, markdown.Plain(first?.Element("summary")))
            .Add("namespace", type.Namespace)
            .Add("assembly", type.Assembly.SimpleName)
            .Add("declaring_type", type.FullName)
            .Add("member_kind", group.Kind.ToString().ToLowerInvariant())
            .Add("overloads", group.Signatures.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .AddList("tags", ["dotnet", group.Kind.ToString().ToLowerInvariant()])
            .ToString());

        builder.Append("# ").Append(heading).Append("\n\n");

        builder.Append("Declared on [").Append(type.DisplayName).Append("](")
            .Append(PathOfType(type).Add("index.md").RelativeToPageIn(page)).Append(").\n\n");

        foreach (MemberSignature signature in group.Signatures)
        {
            XElement? documentation = docs.Find(signature.DocId);

            builder.Append("## `").Append(signature.Declaration).Append("`\n\n");

            string summary = markdown.Block(documentation?.Element("summary"));

            if (summary.Length > 0)
            {
                builder.Append(summary).Append("\n\n");
            }

            AppendParameters(builder, markdown, documentation, "param", "Parameters");
            AppendParameters(builder, markdown, documentation, "typeparam", "Type parameters");
            AppendSection(builder, markdown, documentation, "returns", "Returns");
            AppendSection(builder, markdown, documentation, "value", "Value");
            AppendExceptions(builder, markdown, documentation);
            AppendSection(builder, markdown, documentation, "remarks", "Remarks");
            AppendSection(builder, markdown, documentation, "example", "Example");
        }

        return builder.ToString();
    }

    private static void AppendSection(
        StringBuilder builder,
        DocMarkdown markdown,
        XElement? documentation,
        string element,
        string heading)
    {
        string text = markdown.Block(documentation?.Element(element));

        if (text.Length > 0)
        {
            builder.Append("### ").Append(heading).Append("\n\n").Append(text).Append("\n\n");
        }
    }

    private static void AppendParameters(
        StringBuilder builder,
        DocMarkdown markdown,
        XElement? documentation,
        string element,
        string heading)
    {
        XElement[] parameters = [.. documentation?.Elements(element) ?? []];

        if (parameters.Length == 0)
        {
            return;
        }

        builder.Append("### ").Append(heading).Append("\n\n");

        foreach (XElement parameter in parameters)
        {
            builder.Append("- `").Append(parameter.Attribute("name")?.Value ?? "?").Append("` — ")
                .Append(markdown.Inline(parameter)).Append('\n');
        }

        builder.Append('\n');
    }

    private static void AppendExceptions(StringBuilder builder, DocMarkdown markdown, XElement? documentation)
    {
        XElement[] exceptions = [.. documentation?.Elements("exception") ?? []];

        if (exceptions.Length == 0)
        {
            return;
        }

        builder.Append("### Exceptions\n\n");

        foreach (XElement exception in exceptions)
        {
            string name = exception.Attribute("cref")?.Value ?? string.Empty;
            int colon = name.IndexOf(':', StringComparison.Ordinal);

            builder.Append("- `").Append(colon >= 0 ? name[(colon + 1)..] : name).Append("` — ")
                .Append(markdown.Inline(exception)).Append('\n');
        }

        builder.Append('\n');
    }

    /// <summary>
    /// How a member's name reads in a list. The two names metadata spells with a leading dot are
    /// not names a reader recognises: a constructor is the type, and a static constructor is
    /// worth saying in words.
    /// </summary>
    private static string MemberLabel(string name, TypeRecord type) => name switch
    {
        ".ctor" => type.DisplayName,
        ".cctor" => "static constructor",
        _ => name,
    };

    private static string Plural(TypeShape shape) => shape switch
    {
        TypeShape.Class => "Classes",
        TypeShape.Struct => "Structs",
        TypeShape.Interface => "Interfaces",
        TypeShape.Enum => "Enums",
        _ => "Delegates",
    };

    private static string Plural(MemberSort sort) => sort switch
    {
        MemberSort.Constructor => "Constructors",
        MemberSort.Method => "Methods",
        MemberSort.Property => "Properties",
        MemberSort.Field => "Fields",
        _ => "Events",
    };

    /// <summary>
    /// Gives up this area's file handles without breaking it.
    /// </summary>
    /// <remarks>
    /// This is what <c>refresh</c>, <c>forget</c> and a second <c>ingest</c> do to the area they
    /// displace, and the distinction from <see cref="Dispose"/> is the whole point. A 9P client
    /// holds a fid per directory it has walked into, and those directories are built by closures
    /// that captured <em>this</em> area: disposing it left every one of them describing a type
    /// with no members and listing a directory with no member pages, silently and for good.
    /// Closing the metadata context releases the handles — which is the only reason to do
    /// anything here, since an unreferenced area is ordinary garbage — and a held directory that
    /// rebuilds simply opens it again and serves what it was walked into. A client that walks the
    /// path afresh gets the new area.
    /// </remarks>
    internal void Release()
    {
        _reader?.Release();
        _docs.Clear();
        _members.Clear();
    }

    /// <inheritdoc />
    public void Dispose() => _reader?.Dispose();
}
