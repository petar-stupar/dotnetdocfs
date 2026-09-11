namespace DotnetDocFs.Catalog.Internal.Sources;

/// <summary>
/// The reference assemblies of one target framework, which become <c>/docs/framework</c>.
/// Reference assemblies are the right source: they carry exactly the public surface, so a type
/// the index lists is a type a consumer can actually name.
/// </summary>
/// <param name="TargetFramework">The moniker, such as <c>net10.0</c>.</param>
/// <param name="Version">The pack version, such as <c>10.0.3</c>.</param>
/// <param name="Assemblies">Every reference assembly in the pack, across all its pack ids.</param>
internal sealed record FrameworkPack(
    string TargetFramework,
    string Version,
    IReadOnlyList<DocAssembly> Assemblies);
