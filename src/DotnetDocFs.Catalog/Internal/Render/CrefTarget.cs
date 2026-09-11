using DotnetDocFs.Catalog.Internal.Naming;

namespace DotnetDocFs.Catalog.Internal.Render;

/// <summary>
/// A resolved cross-reference: where it points, and what the link should read as.
/// </summary>
/// <param name="Path">The page in this tree.</param>
/// <param name="Label">The name as C# spells it, for a reference that carried no text of its own.</param>
internal sealed record CrefTarget(TreePath Path, string Label);
