namespace DotnetDocFs.Catalog.Internal.Metadata;

/// <summary>What kind of type a <see cref="TypeRecord"/> describes, as C# names them.</summary>
internal enum TypeShape
{
    /// <summary>A reference type.</summary>
    Class,

    /// <summary>A value type that is not an enumeration.</summary>
    Struct,

    /// <summary>An interface.</summary>
    Interface,

    /// <summary>An enumeration.</summary>
    Enum,

    /// <summary>A delegate.</summary>
    Delegate,
}
