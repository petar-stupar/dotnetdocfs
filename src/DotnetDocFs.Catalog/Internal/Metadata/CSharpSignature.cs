using System.Reflection;
using System.Text;

namespace DotnetDocFs.Catalog.Internal.Metadata;

/// <summary>
/// Writes a member the way C# does, and the way a documentation id does. Both spellings come from
/// the same reflection objects and are produced together, because a page needs one to be read and
/// the other to be matched against the prose in the XML file.
/// </summary>
internal static class CSharpSignature
{
    private static readonly Dictionary<string, string> Keywords = new(StringComparer.Ordinal)
    {
        ["System.Boolean"] = "bool",
        ["System.Byte"] = "byte",
        ["System.SByte"] = "sbyte",
        ["System.Char"] = "char",
        ["System.Decimal"] = "decimal",
        ["System.Double"] = "double",
        ["System.Single"] = "float",
        ["System.Int32"] = "int",
        ["System.UInt32"] = "uint",
        ["System.Int64"] = "long",
        ["System.UInt64"] = "ulong",
        ["System.Int16"] = "short",
        ["System.UInt16"] = "ushort",
        ["System.Object"] = "object",
        ["System.String"] = "string",
        ["System.Void"] = "void",
        ["System.IntPtr"] = "nint",
        ["System.UIntPtr"] = "nuint",
    };

    /// <summary>The type as C# names it: <c>int</c>, <c>string[]</c>, <c>List&lt;int&gt;</c>.</summary>
    internal static string TypeName(Type type)
    {
        if (type.IsByRef)
        {
            return TypeName(type.GetElementType()!);
        }

        if (type.IsArray)
        {
            int rank = type.GetArrayRank();

            return TypeName(type.GetElementType()!) + "[" + new string(',', rank - 1) + "]";
        }

        if (type.IsPointer)
        {
            return TypeName(type.GetElementType()!) + "*";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (type.IsConstructedGenericType || type.IsGenericTypeDefinition)
        {
            Type[] arguments = type.GetGenericArguments();

            if (Nullable(type) is { } inner)
            {
                return TypeName(inner) + "?";
            }

            string bare = Strip(type.Name);
            string prefix = type.IsNested ? TypeName(type.DeclaringType!) + "." : string.Empty;

            return $"{prefix}{bare}<{string.Join(", ", arguments.Select(TypeName))}>";
        }

        string full = type.FullName?.Replace('+', '.') ?? type.Name;

        if (Keywords.TryGetValue(full, out string? keyword))
        {
            return keyword;
        }

        // Namespaces are noise in a signature when the reader is already inside the namespace's
        // own directory; the short name is what C# source would say after a using.
        return type.IsNested ? TypeName(type.DeclaringType!) + "." + type.Name : type.Name;
    }

    /// <summary>The declaration of <paramref name="member"/>, without a body.</summary>
    internal static string Declaration(MemberInfo member) => member switch
    {
        ConstructorInfo constructor => Method(constructor, constructor.DeclaringType!.Name, returns: null),
        MethodInfo method => Method(method, method.Name, method.ReturnType),
        PropertyInfo property => Property(property),
        FieldInfo field => Field(field),
        EventInfo declared => $"event {TypeName(declared.EventHandlerType!)} {declared.Name}",
        _ => member.Name,
    };

    private static string Method(MethodBase method, string name, Type? returns)
    {
        var builder = new StringBuilder();

        builder.Append(Modifiers(method));

        if (returns is not null)
        {
            builder.Append(TypeName(returns)).Append(' ');
        }

        builder.Append(Strip(name));

        if (method is MethodInfo { IsGenericMethodDefinition: true } generic)
        {
            builder.Append('<').Append(string.Join(", ", generic.GetGenericArguments().Select(TypeName))).Append('>');
        }

        builder.Append('(')
            .Append(string.Join(", ", method.GetParameters().Select(Parameter)))
            .Append(')');

        return builder.ToString();
    }

    private static string Parameter(ParameterInfo parameter)
    {
        string modifier = string.Empty;

        if (parameter.ParameterType.IsByRef)
        {
            modifier = parameter.IsOut ? "out " : parameter.IsIn ? "in " : "ref ";
        }
        else if (parameter.GetCustomAttributesData()
            .Any(attribute => attribute.AttributeType.Name == "ParamArrayAttribute"))
        {
            modifier = "params ";
        }

        return $"{modifier}{TypeName(parameter.ParameterType)} {parameter.Name}";
    }

    private static string Property(PropertyInfo property)
    {
        MethodInfo? getter = property.GetGetMethod(nonPublic: true);
        MethodInfo? setter = property.GetSetMethod(nonPublic: true);

        string accessors = (Visible(getter), Visible(setter)) switch
        {
            (true, true) => " { get; set; }",
            (true, false) => " { get; }",
            (false, true) => " { set; }",
            _ => " { }",
        };

        ParameterInfo[] indexer = property.GetIndexParameters();

        string name = indexer.Length == 0
            ? property.Name
            : $"this[{string.Join(", ", indexer.Select(Parameter))}]";

        return Modifiers(getter ?? setter) + TypeName(property.PropertyType) + " " + name + accessors;
    }

    private static string Field(FieldInfo field)
    {
        string modifiers = field.IsPublic ? "public " : "protected ";

        if (field.IsLiteral)
        {
            modifiers += "const ";
        }
        else if (field.IsStatic)
        {
            modifiers += "static ";
        }

        if (field.IsInitOnly)
        {
            modifiers += "readonly ";
        }

        return $"{modifiers}{TypeName(field.FieldType)} {field.Name}";
    }

    private static string Modifiers(MethodBase? method)
    {
        if (method is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        builder.Append(method.IsPublic ? "public " : "protected ");

        if (method.IsStatic)
        {
            builder.Append("static ");
        }
        else if (method.IsAbstract && method.DeclaringType?.IsInterface != true)
        {
            builder.Append("abstract ");
        }
        else if (method.IsVirtual && !method.IsFinal)
        {
            builder.Append("virtual ");
        }

        return builder.ToString();
    }

    private static bool Visible(MethodInfo? accessor) =>
        accessor is not null && (accessor.IsPublic || accessor.IsFamily || accessor.IsFamilyOrAssembly);

    /// <summary>The argument of <c>Nullable&lt;T&gt;</c>, or null for anything else.</summary>
    private static Type? Nullable(Type type) =>
        type.IsConstructedGenericType
        && type.GetGenericTypeDefinition().FullName == "System.Nullable`1"
            ? type.GetGenericArguments()[0]
            : null;

    /// <summary>Drops the arity a generic name carries in metadata.</summary>
    private static string Strip(string name)
    {
        int tick = name.LastIndexOf('`');

        return tick < 0 ? name : name[..tick];
    }
}
