namespace DotnetDocFs.Catalog.Internal.Metadata;

/// <summary>
/// One public type, as the index knows it. This is deliberately shallow: it carries what a
/// listing and a link need, and nothing that would require reading the type's members. Members
/// are read only when a page for the type is actually rendered.
/// </summary>
/// <param name="Namespace">The namespace, taken from the outermost type for a nested one.</param>
/// <param name="MetadataName">The name as metadata spells it, arity included: <c>List`1</c>.</param>
/// <param name="DisplayName">The name as C# spells it: <c>List&lt;T&gt;</c>.</param>
/// <param name="PathName">
/// The name as this tree spells it: <c>List-1</c>. Settable, because two type names can sanitize
/// to one path element and the index gives the second one a different spelling once it knows what
/// else is in the same directory.
/// </param>
/// <param name="FullName">Namespace and nesting joined by dots, as a documentation id spells it.</param>
/// <param name="MetadataFullName">The same name with nesting joined by <c>+</c>, as reflection wants it.</param>
/// <param name="Assembly">The assembly the type was read from.</param>
/// <param name="Kind">Class, struct, interface, enum or delegate.</param>
/// <param name="DeclaringFullName">The enclosing type's <paramref name="FullName"/>, or null.</param>
internal sealed record TypeRecord(
    string Namespace,
    string MetadataName,
    string DisplayName,
    string PathName,
    string FullName,
    string MetadataFullName,
    Sources.DocAssembly Assembly,
    TypeShape Kind,
    string? DeclaringFullName)
{
    /// <summary>The name as this tree spells it, after any collision has been resolved.</summary>
    internal string PathName { get; set; } = PathName;

    /// <summary>The ECMA-335 documentation id, which is how the XML file keys this type.</summary>
    internal string DocId => "T:" + FullName;

    /// <summary>Types declared inside this one, public or protected. Empty for most types.</summary>
    internal IReadOnlyList<TypeRecord> Nested { get; set; } = [];
}
