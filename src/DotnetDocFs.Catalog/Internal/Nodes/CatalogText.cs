using System.Globalization;
using System.Text;
using DotnetDocFs.Catalog.Internal.Render;

namespace DotnetDocFs.Catalog.Internal.Nodes;

/// <summary>
/// The pages that describe the tree rather than any one type. The root index matters most: it is
/// the first thing an agent reads, and an agent that has read it can construct a path to a type
/// instead of walking the tree to find one.
/// </summary>
internal static class CatalogText
{
    /// <summary>The page at <c>/index.md</c>.</summary>
    internal static string RootIndex(DocCatalog catalog, string? packageRoot, bool allowIngest, DateTimeOffset builtAt)
    {
        var builder = new StringBuilder();

        builder.Append(new Frontmatter(OkfType.Of(DocNodeKind.Catalog))
            .Add("title", ".NET API documentation")
            .Add("description", "The .NET and NuGet API surface of this machine, as files.")
            .Add("framework", catalog.TargetFramework)
            .AddList("tags", ["dotnet", "api", "reference"])
            .AddTimestamp(builtAt)
            .ToString());

        builder.Append("""
            # .NET API documentation

            Every page here is generated, when you read it, from the assemblies and XML
            documentation files already installed on this machine. Nothing was downloaded and
            nothing is cached on disk, so this tree describes exactly what this machine has.

            ## Layout

            ```text
            /docs/framework/<namespace>/<Type>/index.md      the .NET base class library
            /docs/framework/<namespace>/<Type>/<Member>.md   one page per member name
            /docs/packages/<package>/<version>/...           the NuGet cache, same shape
            /docs/ingested/<assembly>/...                    whatever /ctl was told about
            /skills/                                         how to use this tree
            ```

            Every directory has an `index.md`. Every page carries YAML frontmatter whose `type`
            says what it describes.

            ## How to find something

            The path of a type is its namespace, then its name:

            ```text
            /docs/framework/System.Text.Json/JsonSerializer/index.md
            /docs/framework/System.Text.Json/JsonSerializer/Serialize.md
            ```

            Two spellings differ from C#, because a filesystem cannot carry the originals:

            - **Generic types** put arity after a dash, not a backtick: `List<T>` is `List-1`,
              `Dictionary<TKey, TValue>` is `Dictionary-2`.
            - **Constructors** are `constructors.md`, because `.ctor` would be a hidden file.

            A namespace is one directory with its full dotted name; `System.Text.Json` is a
            single directory, not three nested ones. A nested type is a directory inside the type
            that declares it.

            **All overloads of one name share one page.** `Serialize.md` holds every overload of
            `JsonSerializer.Serialize`, with a section per signature.

            ## Links

            Links between pages are relative paths that resolve on this filesystem, so following
            one is an ordinary file read. A cross-reference to something this machine does not
            have is written as code rather than as a link, so a link here always leads somewhere.
            Links to the web are the documentation author's own and are left untouched.


            """);

        builder.Append("## What is here\n\n");

        if (catalog.TargetFramework is not null)
        {
            builder.Append("- [.NET ").Append(catalog.TargetFramework).Append("](docs/framework/index.md) — ")
                .Append(catalog.FrameworkAssemblyCount.ToString(CultureInfo.InvariantCulture))
                .Append(" reference assemblies from pack ").Append(catalog.FrameworkVersion).Append(".\n");
        }
        else
        {
            builder.Append("- No .NET reference pack was found on this machine, so there is no framework area.\n");
        }

        if (packageRoot is not null)
        {
            builder.Append("- [NuGet packages](docs/packages/index.md) — the restored packages in `")
                .Append(packageRoot).Append("`.\n");
        }

        builder.Append("- [Ingested assemblies](docs/ingested/index.md) — assemblies added through `/ctl`.\n");
        builder.Append("- [Skills](skills/index.md) — how an agent should navigate this tree.\n\n");

        if (allowIngest)
        {
            builder.Append("""
                ## /ctl

                One command per write; reading it returns this list.

                ```text
                echo 'ingest /path/to/Your.Library.dll' > /ctl
                echo 'forget Your.Library'              > /ctl
                echo 'refresh'                          > /ctl
                ```

                An ingested assembly is inspected, never loaded: no code from it runs. Its
                documentation comes from the `.xml` file beside it, if the build produced one. A
                command that fails fails the write, with the reason as the error.

                `refresh` drops every ingested assembly and everything this tree has already
                listed, so run it after restoring a package.

                """);
        }

        return builder.ToString();
    }

    /// <summary>The page at <c>/docs/index.md</c>.</summary>
    internal static string DocsIndex(DocCatalog catalog, string? packageRoot, DateTimeOffset builtAt)
    {
        var builder = new StringBuilder();

        builder.Append(new Frontmatter(OkfType.Of(DocNodeKind.Area))
            .Add("title", "Documentation areas")
            .Add("description", "The bodies of API documentation this machine can serve.")
            .AddList("tags", ["dotnet", "api"])
            .AddTimestamp(builtAt)
            .ToString());

        builder.Append("# Documentation areas\n\n");

        if (catalog.TargetFramework is not null)
        {
            builder.Append("- [framework](framework/index.md) — the .NET ")
                .Append(catalog.TargetFramework).Append(" reference assemblies.\n");
        }

        if (packageRoot is not null)
        {
            builder.Append("- [packages](packages/index.md) — the NuGet cache.\n");
        }

        builder.Append("- [ingested](ingested/index.md) — assemblies added through `/ctl`.\n");

        return builder.ToString();
    }

    /// <summary>
    /// The page at <c>/docs/ingested/index.md</c>.
    /// </summary>
    /// <remarks>
    /// The root page links here, and the root page is the first thing an agent reads and the one
    /// that states that a link in this tree always lands somewhere. A directory whose contents
    /// start empty still owes that link a page.
    /// </remarks>
    internal static string IngestedIndex(IReadOnlyList<string> names, DateTimeOffset builtAt, bool allowIngest)
    {
        var builder = new StringBuilder();

        builder.Append(new Frontmatter(OkfType.Of(DocNodeKind.Area))
            .Add("title", "Ingested assemblies")
            .Add("description", "Assemblies added to this tree through the control file.")
            .AddList("tags", ["dotnet", "ingested"])
            .AddTimestamp(builtAt)
            .ToString());

        builder.Append("# Ingested assemblies\n\n");

        if (names.Count == 0)
        {
            builder.Append("Nothing has been ingested. ");
        }
        else
        {
            builder.Append("Assemblies named through `/ctl`, each serving the same shape as the ")
                .Append("framework area:\n\n");

            foreach (string name in names)
            {
                builder.Append("- [").Append(name).Append("](").Append(name).Append("/index.md)\n");
            }

            builder.Append('\n');
        }

        builder.Append(allowIngest
            ? "Add one with `echo 'ingest /path/to/Your.Library.dll' > /ctl`, drop one with "
                + "`forget <name>`, and drop all of them with `refresh`. The path is resolved by "
                + "the server, on the machine running dotnetdoc.\n"
            : "This server was started with `--no-ctl`, so nothing can be added here.\n");

        return builder.ToString();
    }

    /// <summary>The page at <c>/docs/packages/index.md</c>.</summary>
    internal static string PackagesIndex(string packageRoot, DateTimeOffset builtAt)
    {
        var builder = new StringBuilder();

        builder.Append(new Frontmatter(OkfType.Of(DocNodeKind.Area))
            .Add("title", "NuGet packages")
            .Add("description", "The packages restored into this machine's NuGet cache.")
            .Add("resource", packageRoot)
            .AddList("tags", ["dotnet", "nuget"])
            .AddTimestamp(builtAt)
            .ToString());

        builder.Append("# NuGet packages\n\n")
            .Append("Every package restored into `").Append(packageRoot).Append("`.\n\n")
            .Append("A package is a directory of versions; each version holds the namespaces of ")
            .Append("its best target framework. A package with no library — an analyzer, a tools ")
            .Append("package — has no versions listed.\n\n")
            .Append("Listing this directory shows every package id. `ls` it rather than reading ")
            .Append("this page, which does not enumerate them.\n");

        return builder.ToString();
    }
}
