namespace DotnetDocFs.Catalog.Internal.Nodes;

/// <summary>
/// The OKF <c>type</c> field for each kind of node. It is the format's one required field, so it
/// is spelled in exactly one place.
/// </summary>
internal static class OkfType
{
    /// <summary>The <c>type</c> a page of the given <paramref name="kind"/> declares.</summary>
    internal static string Of(DocNodeKind kind) => kind switch
    {
        DocNodeKind.Catalog => ".NET API Catalog",
        DocNodeKind.Area => ".NET API Area",
        DocNodeKind.Package => "NuGet Package",
        DocNodeKind.PackageVersion => "NuGet Package Version",
        DocNodeKind.Namespace => ".NET Namespace",
        DocNodeKind.Type => ".NET Type",
        DocNodeKind.Member => ".NET Member",
        _ => "Agent Skill",
    };
}
