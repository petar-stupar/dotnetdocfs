using DotnetDocFs.Catalog.Internal.Naming;

namespace DotnetDocFs.Catalog.Internal.Render;

/// <summary>
/// Turns a .NET name into a place in this tree, or says that it has none. The renderer asks
/// before it writes a link, which is how a <c>cref</c> to something outside the served tree
/// becomes plain code instead of a path that leads nowhere.
/// </summary>
internal interface ILinkResolver
{
    /// <summary>The page of the type named <paramref name="typeFullName"/>, or null.</summary>
    TreePath? TypePage(string typeFullName);

    /// <summary>
    /// The page covering <paramref name="memberName"/> on <paramref name="typeFullName"/>, or
    /// null when the type itself is not in the tree.
    /// </summary>
    TreePath? MemberPage(string typeFullName, string memberName);

    /// <summary>
    /// The type as C# spells it — <c>IAsyncEnumerable&lt;T&gt;</c>, not
    /// <c>System.Collections.Generic.IAsyncEnumerable`1</c> — or null when it is not in the tree.
    /// A documentation id is the right key and the wrong label.
    /// </summary>
    string? TypeLabel(string typeFullName);
}
