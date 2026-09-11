namespace DotnetDocFs.Catalog.Internal.Metadata;

/// <summary>The kinds of member a type page groups its children under.</summary>
internal enum MemberSort
{
    /// <summary>A constructor.</summary>
    Constructor,

    /// <summary>A method, including an operator.</summary>
    Method,

    /// <summary>A property or an indexer.</summary>
    Property,

    /// <summary>A field, including an enumeration's members.</summary>
    Field,

    /// <summary>An event.</summary>
    Event,
}
