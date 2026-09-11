using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetDocFs.Catalog.Internal.Naming;
using DotnetDocFs.Catalog.Internal.Sources;

namespace DotnetDocFs.Catalog.Internal.Metadata;

/// <summary>
/// Every public type of a set of assemblies, grouped by namespace. Built straight from the
/// metadata tables, which reads names and nothing else: no assembly is resolved, loaded or run to
/// produce it. That is what keeps it cheap enough to build on demand rather than cache on disk —
/// the whole .NET reference pack is a few tens of milliseconds.
/// </summary>
internal sealed class TypeIndex
{
    /// <summary>How far a chain of enclosing types is followed before it is called malformed.</summary>
    private const int MaxNestingDepth = 64;

    private readonly Dictionary<string, TypeRecord> _byFullName;
    private readonly Dictionary<string, ImmutableArray<TypeRecord>> _byNamespace;

    private TypeIndex(
        Dictionary<string, TypeRecord> byFullName,
        Dictionary<string, ImmutableArray<TypeRecord>> byNamespace,
        ImmutableArray<string> namespaces)
    {
        _byFullName = byFullName;
        _byNamespace = byNamespace;
        Namespaces = namespaces;
    }

    /// <summary>Every namespace holding at least one public type, ordered.</summary>
    internal ImmutableArray<string> Namespaces { get; }

    /// <summary>An empty index, for a source that resolved to no assemblies at all.</summary>
    internal static TypeIndex Empty { get; } = new([], [], []);

    /// <summary>The top-level public types of <paramref name="namespaceName"/>, ordered.</summary>
    internal ImmutableArray<TypeRecord> TypesIn(string namespaceName) =>
        _byNamespace.TryGetValue(namespaceName, out ImmutableArray<TypeRecord> types) ? types : [];

    /// <summary>
    /// The type whose documentation id is <paramref name="fullName"/>, or null. This is how a
    /// <c>see cref</c> becomes a link: a cref that resolves here gets a relative path, and one
    /// that does not is rendered as plain code rather than as a link to nothing.
    /// </summary>
    internal TypeRecord? Find(string fullName) =>
        _byFullName.GetValueOrDefault(fullName);

    /// <summary>Reads <paramref name="assemblies"/> and indexes what is public in them.</summary>
    /// <param name="assemblies">The assemblies to read.</param>
    /// <param name="unreadable">Receives the path of every assembly that could not be read.</param>
    internal static TypeIndex Build(IReadOnlyList<DocAssembly> assemblies, ICollection<string>? unreadable = null)
    {
        var byFullName = new Dictionary<string, TypeRecord>(StringComparer.Ordinal);
        var nested = new List<TypeRecord>();

        foreach (DocAssembly assembly in assemblies)
        {
            try
            {
                ReadAssembly(assembly, byFullName, nested);
            }
            catch (Exception exception) when (exception is BadImageFormatException
                or IOException
                or UnauthorizedAccessException)
            {
                // A reference pack or a NuGet cache is a directory of files this program did not
                // write: one truncated download in it should cost that assembly its pages, not
                // take down the whole catalogue with a stack trace. Ingest is the exception and
                // reports the reason, because there the caller named the one file.
                unreadable?.Add(assembly.Path);
            }
        }

        AttachNested(byFullName, nested);

        var byNamespace = byFullName.Values
            .Where(type => type.DeclaringFullName is null)
            .GroupBy(type => type.Namespace, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => Uniquify([.. group.OrderBy(type => type.MetadataName, StringComparer.Ordinal)]),
                StringComparer.Ordinal);

        return new TypeIndex(
            byFullName,
            byNamespace,
            [.. byNamespace.Keys.OrderBy(name => name, StringComparer.Ordinal)]);
    }

    private static void ReadAssembly(
        DocAssembly assembly,
        Dictionary<string, TypeRecord> byFullName,
        List<TypeRecord> nested)
    {
        using FileStream stream = File.OpenRead(assembly.Path);
        using var pe = new PEReader(stream);

        if (!pe.HasMetadata)
        {
            return;
        }

        MetadataReader md = pe.GetMetadataReader();

        foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
        {
            TypeDefinition type = md.GetTypeDefinition(handle);

            if (!IsVisible(type.Attributes))
            {
                continue;
            }

            string metadataName = md.GetString(type.Name);

            // <Module> and the compiler's own types are not surface.
            if (metadataName.StartsWith('<'))
            {
                continue;
            }

            string? declaringFullName = null;
            string? declaringMetadataName = null;
            string namespaceName;

            if (type.Attributes.HasFlag(TypeAttributes.NestedPublic)
                || (type.Attributes & TypeAttributes.VisibilityMask) is TypeAttributes.NestedFamily
                    or TypeAttributes.NestedFamORAssem)
            {
                if (!TryDescribeEnclosing(md, type, out namespaceName, out declaringFullName, out declaringMetadataName))
                {
                    continue;
                }
            }
            else
            {
                namespaceName = md.GetString(type.Namespace);
            }

            string fullName = declaringFullName is null
                ? Join(namespaceName, metadataName)
                : declaringFullName + "." + metadataName;

            string metadataFullName = declaringMetadataName is null
                ? Join(namespaceName, metadataName)
                : declaringMetadataName + "+" + metadataName;

            var record = new TypeRecord(
                namespaceName,
                metadataName,
                DisplayName(md, type, metadataName),
                PathName.ForType(metadataName),
                fullName,
                metadataFullName,
                assembly,
                ShapeOf(md, type),
                declaringFullName);

            // A type present in more than one assembly of the set — a type forward, or the same
            // name in two packs — is kept once. The first assembly read wins, which the pack
            // ordering in FrameworkLocator makes deterministic.
            if (byFullName.TryAdd(fullName, record) && declaringFullName is not null)
            {
                nested.Add(record);
            }
        }
    }

    /// <summary>
    /// Walks out through the enclosing types to the namespace, which metadata stores only on the
    /// outermost one. Returns false when any link in the chain is not itself visible, since a
    /// public type nested in an internal one is not reachable.
    /// </summary>
    private static bool TryDescribeEnclosing(
        MetadataReader md,
        TypeDefinition type,
        out string namespaceName,
        out string? declaringFullName,
        out string? declaringMetadataName)
    {
        namespaceName = string.Empty;
        declaringFullName = null;
        declaringMetadataName = null;

        var names = new List<string>();
        TypeDefinition current = type;

        // MetadataReader does not check the NestedClass table for cycles, and a type that
        // declares itself would walk this loop forever while growing the list. Nothing a compiler
        // emits nests anywhere near this deep; /ctl accepts assemblies no compiler wrote.
        for (int depth = 0; !current.GetDeclaringType().IsNil; depth++)
        {
            if (depth == MaxNestingDepth)
            {
                return false;
            }

            current = md.GetTypeDefinition(current.GetDeclaringType());

            if (!IsVisible(current.Attributes))
            {
                return false;
            }

            names.Insert(0, md.GetString(current.Name));
        }

        namespaceName = md.GetString(current.Namespace);
        declaringFullName = Join(namespaceName, string.Join('.', names));
        declaringMetadataName = Join(namespaceName, string.Join('+', names));

        return true;
    }

    private static void AttachNested(Dictionary<string, TypeRecord> byFullName, List<TypeRecord> nested)
    {
        foreach (IGrouping<string, TypeRecord> group in nested.GroupBy(type => type.DeclaringFullName!, StringComparer.Ordinal))
        {
            if (byFullName.TryGetValue(group.Key, out TypeRecord? parent))
            {
                parent.Nested = Uniquify([.. group.OrderBy(type => type.MetadataName, StringComparer.Ordinal)]);
            }
        }
    }

    /// <summary>
    /// Gives every type in one directory a name no other type there has taken.
    /// </summary>
    /// <remarks>
    /// Sanitizing maps several characters to <c>_</c>, so <c>A:B</c> and <c>A*B</c> both want
    /// <c>A_B</c>, and a case-insensitive re-export makes <c>Foo</c> and <c>foo</c> one directory.
    /// Nothing a C# compiler emits does either, and an assembly handed to <c>/ctl</c> may do both.
    /// Without this the second type is listed and unreachable, which is worse than an odd name.
    /// The ordering is by metadata name so the result does not depend on what collided.
    /// </remarks>
    private static ImmutableArray<TypeRecord> Uniquify(List<TypeRecord> types)
    {
        var taken = new HashSet<string>(types.Count, StringComparer.OrdinalIgnoreCase);

        foreach (TypeRecord type in types)
        {
            if (taken.Add(type.PathName))
            {
                continue;
            }

            string stem = type.PathName;
            string unique = stem;

            for (int suffix = 2; !taken.Add(unique); suffix++)
            {
                unique = $"{stem}-{suffix}";
            }

            type.PathName = unique;
        }

        return [.. types.OrderBy(type => type.PathName, StringComparer.Ordinal)];
    }

    private static bool IsVisible(TypeAttributes attributes) =>
        (attributes & TypeAttributes.VisibilityMask) is TypeAttributes.Public
            or TypeAttributes.NestedPublic
            or TypeAttributes.NestedFamily
            or TypeAttributes.NestedFamORAssem;

    private static string Join(string namespaceName, string name) =>
        namespaceName.Length == 0 ? name : namespaceName + "." + name;

    /// <summary>
    /// The C# spelling: <c>List`1</c> becomes <c>List&lt;T&gt;</c>. Only the parameters this type
    /// declares are shown; a nested type inherits its enclosing type's, and repeating them here
    /// would not match how the member pages spell it.
    /// </summary>
    private static string DisplayName(MetadataReader md, TypeDefinition type, string metadataName)
    {
        int tick = metadataName.LastIndexOf('`');

        if (tick < 0)
        {
            return metadataName;
        }

        GenericParameterHandleCollection parameters = type.GetGenericParameters();

        if (!int.TryParse(metadataName.AsSpan(tick + 1), out int own) || own <= 0)
        {
            return metadataName[..tick];
        }

        IEnumerable<string> names = parameters
            .Skip(Math.Max(0, parameters.Count - own))
            .Select(handle => md.GetString(md.GetGenericParameter(handle).Name));

        return $"{metadataName[..tick]}<{string.Join(", ", names)}>";
    }

    private static TypeShape ShapeOf(MetadataReader md, TypeDefinition type)
    {
        if (type.Attributes.HasFlag(TypeAttributes.Interface))
        {
            return TypeShape.Interface;
        }

        return BaseTypeName(md, type) switch
        {
            "System.Enum" => TypeShape.Enum,
            "System.ValueType" => TypeShape.Struct,
            "System.MulticastDelegate" or "System.Delegate" => TypeShape.Delegate,
            _ => TypeShape.Class,
        };
    }

    /// <summary>
    /// The full name of the base type, whichever table it lives in. A base type in another
    /// assembly is a <c>TypeReference</c>, which is the usual case in a reference pack; one in
    /// this assembly is a <c>TypeDefinition</c>. Anything else — a generic instantiation through
    /// a <c>TypeSpecification</c> — is not one of the four names this is asked about.
    /// </summary>
    /// <remarks>
    /// The nil check is not defensive padding. <c>System.Object</c> and every interface have no
    /// base type, and a nil handle carries token 0, which decodes as <c>TypeDefinition</c> row 0:
    /// reading it throws <c>BadImageFormatException</c> on a perfectly good assembly.
    /// </remarks>
    private static string? BaseTypeName(MetadataReader md, TypeDefinition type) => type.BaseType.IsNil ? null : type.BaseType.Kind switch
    {
        HandleKind.TypeReference => NameOf(md, md.GetTypeReference((TypeReferenceHandle)type.BaseType)),
        HandleKind.TypeDefinition => NameOf(md, md.GetTypeDefinition((TypeDefinitionHandle)type.BaseType)),
        _ => null,
    };

    private static string NameOf(MetadataReader md, TypeReference reference) =>
        Join(reference.Namespace.IsNil ? string.Empty : md.GetString(reference.Namespace), md.GetString(reference.Name));

    private static string NameOf(MetadataReader md, TypeDefinition definition) =>
        Join(md.GetString(definition.Namespace), md.GetString(definition.Name));
}
