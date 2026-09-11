using DotnetDocFs.Catalog.Internal.Metadata;
using DotnetDocFs.Catalog.Internal.Naming;
using DotnetDocFs.Catalog.Internal.Nodes;

namespace DotnetDocFs.Catalog.Internal.Render;

/// <summary>
/// Resolves a name for pages of one area: its own types first, then the framework's.
/// </summary>
/// <remarks>
/// The order is what a reader expects. A package that declares <c>JsonConvert</c> means its own,
/// and a cref to <c>System.String</c> from inside that package means the framework's. Anything
/// neither area carries resolves to null, and the renderer writes code rather than a link —
/// nothing in this tree ever links to a page that is not there.
/// </remarks>
internal sealed class AreaLinks : ILinkResolver
{
    /// <summary>The area whose pages are being rendered. Set once the area exists.</summary>
    internal DocArea? Own { get; set; }

    /// <summary>The framework area, searched second. Null when no reference pack was found.</summary>
    internal DocArea? Framework { get; set; }

    /// <inheritdoc />
    public TreePath? TypePage(string typeFullName) =>
        Locate(typeFullName) is var (area, type) && area is not null && type is not null
            ? area.PathOfType(type).Add("index.md")
            : null;

    /// <inheritdoc />
    public TreePath? MemberPage(string typeFullName, string memberName)
    {
        (DocArea? area, TypeRecord? type) = Locate(typeFullName);

        if (area is null || type is null)
        {
            return null;
        }

        // Composing a path from the member's name would answer "yes" for every member of a type
        // this tree carries, including the ones it has no page for: a cref outliving the API it
        // names, a member that is not public, a property accessor, or anything at all on a type
        // whose dependencies would not resolve. The area is asked instead, because it knows.
        string file = PathName.ForMember(memberName) + ".md";

        return area.HasMemberPage(type, file) ? area.PathOfType(type).Add(file) : null;
    }

    /// <inheritdoc />
    public string? TypeLabel(string typeFullName) => Locate(typeFullName).Type?.DisplayName;

    private (DocArea? Area, TypeRecord? Type) Locate(string typeFullName)
    {
        if (Own?.Index.Find(typeFullName) is { } mine)
        {
            return (Own, mine);
        }

        if (Framework?.Index.Find(typeFullName) is { } framework)
        {
            return (Framework, framework);
        }

        return (null, null);
    }
}
