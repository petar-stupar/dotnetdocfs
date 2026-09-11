namespace DotnetDocFs.Catalog;

/// <summary>
/// What a <see cref="DocNode"/> stands for. The value is also the OKF <c>type</c> field of the
/// page the node renders, spelled by <see cref="DocNodeKindText.ToOkfType"/>.
/// </summary>
public enum DocNodeKind
{
    /// <summary>The root of the served tree.</summary>
    Catalog,

    /// <summary>A group of assemblies: the framework, the package area, the ingested area.</summary>
    Area,

    /// <summary>One NuGet package, holding its versions.</summary>
    Package,

    /// <summary>One version of one NuGet package.</summary>
    PackageVersion,

    /// <summary>A namespace, holding types.</summary>
    Namespace,

    /// <summary>A type, holding its members and nested types.</summary>
    Type,

    /// <summary>One member name of a type, covering all of its overloads.</summary>
    Member,

    /// <summary>An agent skill for navigating this tree.</summary>
    Skill,
}
