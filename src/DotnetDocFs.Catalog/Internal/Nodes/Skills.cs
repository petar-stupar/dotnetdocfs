using System.Text;
using DotnetDocFs.Catalog.Internal.Render;

namespace DotnetDocFs.Catalog.Internal.Nodes;

/// <summary>
/// <c>/skills</c>: read-only agent skills for navigating this tree, meant to be copied into a
/// project rather than run from here.
/// </summary>
internal static class Skills
{
    /// <summary>The <c>/skills</c> directory.</summary>
    /// <param name="builtAt">When the tree was built, for the pages' frontmatter.</param>
    /// <param name="mountPath">Where this tree can be read from, or null when nobody has said.</param>
    internal static LazyDirectory Directory(DateTimeOffset builtAt, string? mountPath) =>
        new LazyDirectory("skills", DocNodeKind.Skill, "/skills", () =>
        [
            new TextPage("index.md", DocNodeKind.Skill, "/skills/index.md", () => Index(builtAt)),
            new LazyDirectory("dotnet-api-docs", DocNodeKind.Skill, "/skills/dotnet-api-docs", () =>
            [
                // Every directory of this tree carries an index.md, this one included: an agent
                // descending the tree reads index.md and would otherwise find nothing here.
                new TextPage(
                    "index.md",
                    DocNodeKind.Skill,
                    "/skills/dotnet-api-docs/index.md",
                    () => SkillIndex(builtAt, mountPath)),
                new TextPage(
                    "SKILL.md",
                    DocNodeKind.Skill,
                    "/skills/dotnet-api-docs/SKILL.md",
                    () => Navigation(mountPath)),
            ]),
        ]);

    private static string SkillIndex(DateTimeOffset builtAt, string? mountPath)
    {
        var builder = new StringBuilder();

        builder.Append(new Frontmatter(OkfType.Of(DocNodeKind.Skill))
            .Add("title", "dotnet-api-docs")
            .Add("description", "A skill for finding a .NET type or member in this tree.")
            .AddList("tags", ["agent", "skill"])
            .AddTimestamp(builtAt)
            .ToString());

        builder.Append("""
            # dotnet-api-docs

            An agent skill for looking up .NET and NuGet API documentation in this tree instead of
            searching the web.

            - [SKILL.md](SKILL.md) — the skill itself. Copy it into your project's skills
              directory; nothing here runs from the mount.

            """);

        // A blank line first: a raw string literal drops the last line break before its closing
        // delimiter, so the one above leaves this sentence abutting the bullet it follows.
        builder.Append(mountPath is null
            ? "\nThe paths in it are written `<mount>`; replace that with where this tree is\nmounted.\n"
            : "\nThe paths in it are already the ones on this machine, so copy it as it is.\n");

        return builder.ToString();
    }

    private static string Index(DateTimeOffset builtAt)
    {
        var builder = new StringBuilder();

        builder.Append(new Frontmatter(OkfType.Of(DocNodeKind.Skill))
            .Add("title", "Skills")
            .Add("description", "Agent skills for navigating this documentation tree.")
            .AddList("tags", ["agent", "skill"])
            .AddTimestamp(builtAt)
            .ToString());

        builder.Append("""
            # Skills

            Read-only skills for working with this tree. Copy one into your project's skills
            directory; nothing here runs from the mount.

            - [dotnet-api-docs](dotnet-api-docs/SKILL.md) — finding a .NET type or member in this
              tree without searching the web.
            """);

        return builder.ToString();
    }

    /// <summary>
    /// The skill an agent harness reads, with <paramref name="mountPath"/> written into it where
    /// it is known.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one page in the tree meant to be copied *out* of it and followed from outside,
    /// which is why it alone is written with a placeholder. Every other page is read through the
    /// mount, where a path relative to the tree is the right way to name a position in it.
    /// </para>
    /// <para>
    /// The substitution is <c>&lt;mount&gt;</c> and nothing else. <c>&lt;namespace&gt;</c>,
    /// <c>&lt;Type&gt;</c>, <c>&lt;version&gt;</c> and the rest are placeholders a reader is meant
    /// to fill in themselves, and a general template pass would eat them.
    /// </para>
    /// </remarks>
    private static string Navigation(string? mountPath)
    {
        var builder = new StringBuilder();

        // A skill file's frontmatter is the agent harness's, not OKF's: name and description are
        // what a harness matches on, and inventing extra fields there would be noise.
        builder.Append("""
            ---
            name: dotnet-api-docs
            description: >-
              Look up .NET and NuGet API documentation from a mounted dotnetdocfs tree instead of
              searching the web. Use when you need a type's members, a method's overloads,
              parameters, exceptions or remarks for anything in the .NET base class library or a
              restored NuGet package.
            ---

            # Reading .NET API documentation from the filesystem

            This tree holds the API surface of the .NET installed on this machine and of every
            NuGet package restored into its cache, one markdown page per namespace, type and
            member name. It is generated on read, so it matches this machine exactly.

            ## Find a type

            Construct the path; do not search for it.

            ```text
            <mount>/docs/framework/<namespace>/<Type>/index.md
            ```

            `System.Text.Json.JsonSerializer` is
            `docs/framework/System.Text.Json/JsonSerializer/index.md`. The namespace is one
            directory with its full dotted name.

            Two spellings differ from C#:

            - generic arity is a dash: `List<T>` is `List-1`, `Dictionary<K,V>` is `Dictionary-2`
            - constructors are `constructors.md`

            A nested type is a directory inside its declaring type.

            ## Find a member

            One page per member **name**, holding every overload:

            ```text
            docs/framework/System.Text.Json/JsonSerializer/Serialize.md
            ```

            Read the type's `index.md` first when you do not know the member name: it lists every
            member with a one-line summary and a link.

            ## When you do not know the namespace

            List it rather than guessing:

            ```text
            ls <mount>/docs/framework/ | grep -i json
            ```

            Namespace directories are the full dotted names, so this finds the namespace in one
            step. Then list that directory to see its types.

            ## NuGet packages

            ```text
            <mount>/docs/packages/<package-id-lowercased>/<version>/<namespace>/<Type>/index.md
            ```

            List `docs/packages/` to see what this machine has restored. Package ids are
            lowercased, the way NuGet stores them.

            ## Rules worth knowing

            - Links between pages are relative paths that work as file reads; follow them.
            - A cross-reference written as code rather than a link means this machine does not
              have that type. Do not guess a path for it.
            - Every page starts with YAML frontmatter. `type` says what the page describes, and
              `uid` on a type page is its full .NET name.
            - The tree is read-only apart from `/ctl`.

            ## Adding a library that is not here

            If a library is not in the cache, point `/ctl` at its assembly:

            ```text
            echo 'ingest /path/to/Your.Library.dll' > <mount>/ctl
            ```

            It then appears under `docs/ingested/Your.Library/`. The assembly is inspected, never
            executed.
            """);

        return mountPath is null
            ? builder.ToString()
            : builder.Replace("<mount>", At(mountPath)).ToString();
    }

    /// <summary>
    /// A mount path as it is written into the skill: one trailing separator taken off, so
    /// <c>&lt;mount&gt;/docs</c> cannot become <c>//docs</c>.
    /// </summary>
    /// <remarks>
    /// The separators themselves are left as they came. Translating them would be guessing which
    /// side of a namespace boundary the reader is on, which is the mistake this substitution
    /// exists to avoid.
    /// </remarks>
    private static string At(string mountPath) =>
        mountPath.TrimEnd('/', '\\') is { Length: > 0 } trimmed ? trimmed : mountPath;
}
