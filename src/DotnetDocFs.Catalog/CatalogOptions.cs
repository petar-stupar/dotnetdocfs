namespace DotnetDocFs.Catalog;

/// <summary>
/// Where a <see cref="DocCatalog"/> looks for the assemblies and documentation it renders.
/// Every path here belongs to the machine dotnetdocfs runs on; the repository ships none of it.
/// </summary>
public sealed record CatalogOptions
{
    /// <summary>
    /// The .NET installation whose reference packs become <c>/docs/framework</c>. When null the
    /// catalog discovers it from <c>DOTNET_ROOT</c>, then from the running host.
    /// </summary>
    public string? DotnetRoot { get; init; }

    /// <summary>
    /// The target framework moniker to serve, such as <c>net10.0</c>. When null the newest
    /// reference pack installed is used.
    /// </summary>
    public string? TargetFramework { get; init; }

    /// <summary>
    /// The NuGet global package folder that becomes <c>/docs/packages</c>. When null it is taken
    /// from <c>NUGET_PACKAGES</c>, then <c>~/.nuget/packages</c>. An absent folder is not an
    /// error: the area is simply empty.
    /// </summary>
    public string? PackageRoot { get; init; }

    /// <summary>
    /// Where this tree can be read from, or null when nobody has said.
    /// </summary>
    /// <remarks>
    /// A caption and nothing more: the served skill prints it in place of a placeholder, because
    /// that page is the one meant to be copied out of the tree and followed from outside it.
    /// Nothing in here acts on it, and null rather than a default because a path nobody stated is
    /// a guess, and a skill naming a directory that is not there is worse than one that asks to
    /// be filled in.
    /// </remarks>
    public string? MountPath { get; init; }

    /// <summary>
    /// Whether <c>/ctl</c> accepts writes. False serves a strictly read-only tree.
    /// </summary>
    public bool AllowIngest { get; init; } = true;

    /// <summary>
    /// The largest assembly <c>/ctl</c> will read, in bytes. A file larger than this is refused
    /// before it is opened.
    /// </summary>
    public long MaxIngestBytes { get; init; } = 256L * 1024 * 1024;
}
