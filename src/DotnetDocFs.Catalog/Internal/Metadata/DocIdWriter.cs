using System.Reflection;
using System.Text;

namespace DotnetDocFs.Catalog.Internal.Metadata;

/// <summary>
/// Writes the ECMA-335 documentation id of a member: the key the compiler used when it wrote the
/// XML file, and so the only way to find the prose belonging to one particular overload.
/// </summary>
internal static class DocIdWriter
{
    /// <summary>The id of <paramref name="member"/>, including its kind prefix.</summary>
    internal static string For(MemberInfo member) => member switch
    {
        ConstructorInfo constructor => "M:" + Qualify(constructor.DeclaringType!) + ".#ctor" + Parameters(constructor),
        MethodInfo method => "M:" + Qualify(method.DeclaringType!) + "." + Escape(method.Name)
            + Arity(method) + Parameters(method),
        PropertyInfo property => "P:" + Qualify(property.DeclaringType!) + "." + Escape(property.Name)
            + IndexerParameters(property),
        FieldInfo field => "F:" + Qualify(field.DeclaringType!) + "." + Escape(field.Name),
        EventInfo declared => "E:" + Qualify(declared.DeclaringType!) + "." + Escape(declared.Name),
        Type type => "T:" + Qualify(type),
        _ => "M:" + Qualify(member.DeclaringType!) + "." + Escape(member.Name),
    };

    /// <summary>
    /// The name of a declaring type inside an id: nesting written with dots, arity kept.
    /// </summary>
    private static string Qualify(Type type)
    {
        if (!type.IsNested)
        {
            return type.FullName?.Replace('+', '.') ?? type.Name;
        }

        return Qualify(type.DeclaringType!) + "." + type.Name;
    }

    /// <summary>A documentation id writes the dots of an explicit implementation as a hash.</summary>
    private static string Escape(string name) => name.Replace('.', '#');

    private static string Arity(MethodInfo method) =>
        method.IsGenericMethodDefinition ? "``" + method.GetGenericArguments().Length : string.Empty;

    private static string Parameters(MethodBase method) => Parameters(method.GetParameters());

    private static string IndexerParameters(PropertyInfo property) => Parameters(property.GetIndexParameters());

    private static string Parameters(ParameterInfo[] parameters) => parameters.Length == 0
        ? string.Empty
        : "(" + string.Join(",", parameters.Select(parameter => Reference(parameter.ParameterType))) + ")";

    /// <summary>
    /// A parameter type as an id spells it: full names throughout, generic arguments in braces,
    /// a generic parameter by its position, and a by-reference parameter with a trailing at sign.
    /// </summary>
    private static string Reference(Type type)
    {
        if (type.IsByRef)
        {
            return Reference(type.GetElementType()!) + "@";
        }

        if (type.IsArray)
        {
            int rank = type.GetArrayRank();
            string dimensions = rank == 1 ? "[]" : "[" + string.Join(",", Enumerable.Repeat("0:", rank)) + "]";

            return Reference(type.GetElementType()!) + dimensions;
        }

        if (type.IsPointer)
        {
            return Reference(type.GetElementType()!) + "*";
        }

        if (type.IsGenericParameter)
        {
            // A parameter of the method is written with two backticks, one of the type with one.
            return (type.DeclaringMethod is null ? "`" : "``") + type.GenericParameterPosition;
        }

        if (type.IsConstructedGenericType)
        {
            return Constructed(type);
        }

        return Qualify(type);
    }

    /// <summary>
    /// A constructed generic type as an id spells it: arity stripped and the arguments written in
    /// braces after <em>the segment that declares them</em>.
    /// </summary>
    /// <remarks>
    /// The distinction only shows on a generic type nested in another, where the compiler writes
    /// <c>Dictionary{`0,`1}.AlternateLookup{``0}</c> — two brace groups, each holding the
    /// parameters its own segment introduced. Stripping arity once, at the last backtick of the
    /// qualified name, leaves the outer arity in place and gathers every argument into one group
    /// at the end, which is a key the compiler never wrote. The page then renders with no prose
    /// and reads as an undocumented member rather than as a miss.
    /// </remarks>
    private static string Constructed(Type type)
    {
        Type[] arguments = type.GetGenericArguments();

        var chain = new List<Type>();

        for (Type? at = type.GetGenericTypeDefinition(); at is not null; at = at.DeclaringType)
        {
            chain.Insert(0, at);
        }

        var builder = new StringBuilder();
        int taken = 0;

        for (int at = 0; at < chain.Count; at++)
        {
            // The outermost segment carries the namespace; the rest are bare names.
            string name = at == 0 ? Qualify(chain[at]) : chain[at].Name;
            int tick = name.LastIndexOf('`');

            builder.Append(at == 0 ? string.Empty : ".").Append(tick < 0 ? name : name[..tick]);

            // Arity in a name counts only the parameters that segment introduced, so the ones
            // this segment owns are whatever the enclosing segments have not already claimed.
            int own = chain[at].GetGenericArguments().Length - taken;

            if (own <= 0)
            {
                continue;
            }

            builder.Append('{')
                .Append(string.Join(",", arguments.Skip(taken).Take(own).Select(Reference)))
                .Append('}');

            taken += own;
        }

        return builder.ToString();
    }
}
