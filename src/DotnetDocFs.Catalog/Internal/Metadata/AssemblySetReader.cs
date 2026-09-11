using System.Reflection;
using DotnetDocFs.Catalog.Internal;
using DotnetDocFs.Catalog.Internal.Naming;
using DotnetDocFs.Catalog.Internal.Sources;

namespace DotnetDocFs.Catalog.Internal.Metadata;

/// <summary>
/// Reads the members of a type through <see cref="MetadataLoadContext"/>, which resolves and
/// describes an assembly without ever making it runnable: no static constructor, no module
/// initializer and no code of the inspected assembly runs in this process. That property is what
/// makes it safe to point this at a path a caller handed to <c>/ctl</c>.
/// </summary>
internal sealed class AssemblySetReader : IDisposable
{
    /// <summary>
    /// How many readers keep their <see cref="MetadataLoadContext"/> open at once.
    /// </summary>
    /// <remarks>
    /// A context holds a memory-mapped handle on each of its assemblies, and there is one reader
    /// per package version. A recursive read over <c>/docs/packages</c> — <c>grep -r</c> over the
    /// mount, which is the README's own example — walks into every version a developer has ever
    /// restored, which on a working machine is thousands. Keeping them all open ends at the
    /// process's file limit.
    /// </remarks>
    private const int OpenContexts = 24;

    /// <summary>The readers whose context is open, least recently used first to be closed.</summary>
    private static readonly Bounded<AssemblySetReader, bool> Open =
        new(OpenContexts, comparer: null, onEvict: (reader, _) => reader.Close());

    private readonly PathAssemblyResolver _resolver;
    private readonly string _core;
    private readonly Lock _gate = new();
    private MetadataLoadContext? _context;
    private bool _disposed;

    private AssemblySetReader(PathAssemblyResolver resolver, string core)
    {
        _resolver = resolver;
        _core = core;
    }

    /// <summary>
    /// Opens <paramref name="assemblies"/> for inspection. <paramref name="fallback"/> is added to
    /// the resolver without being served: a NuGet assembly refers to framework types it does not
    /// carry, and without somewhere to resolve them its members cannot be described at all.
    /// </summary>
    /// <param name="assemblies">The assemblies whose members are to be described.</param>
    /// <param name="fallback">Assemblies to resolve against but not to serve.</param>
    /// <param name="core">
    /// The assembly chosen as the core library, or null when there was none and no reader could
    /// be made. A caller that wants to say why an area has no members needs this.
    /// </param>
    internal static AssemblySetReader? Create(
        IReadOnlyList<DocAssembly> assemblies,
        IReadOnlyList<DocAssembly> fallback,
        out string? core)
    {
        core = null;

        if (assemblies.Count == 0)
        {
            return null;
        }

        // A duplicate simple name makes PathAssemblyResolver throw, and the fallback set overlaps
        // the served one whenever the served one is the framework itself.
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (DocAssembly assembly in assemblies.Concat(fallback))
        {
            paths.TryAdd(assembly.SimpleName, assembly.Path);
        }

        var resolver = new PathAssemblyResolver(paths.Values);

        // Without a core assembly every resolution fails, MembersOf returns nothing through its
        // catch, and the type pages render with no members and no error anywhere — the quietest
        // way this can be wrong. Saying so is the difference between "this machine has no
        // reference pack" and "this tool is broken".
        bool resolvable = paths.ContainsKey("System.Runtime") || paths.ContainsKey("mscorlib");

        if (!resolvable)
        {
            core = null;

            return null;
        }

        core = paths.ContainsKey("System.Runtime") ? "System.Runtime" : "mscorlib";

        return new AssemblySetReader(resolver, core);
    }

    /// <summary>
    /// Closes the context, keeping the reader usable: the next call opens a new one.
    /// </summary>
    internal void Release() => Close();

    /// <summary>
    /// Closes the context, keeping the reader usable: the next call opens a new one.
    /// </summary>
    /// <remarks>
    /// Nothing from a context escapes this class — <see cref="MemberGroup"/> holds strings, and
    /// every one of them is produced inside <see cref="MembersOf"/> while its lock is held — so
    /// closing between calls cannot invalidate anything a caller is holding. That is what makes
    /// the handles boundable without any node of the tree going stale.
    /// </remarks>
    private void Close()
    {
        lock (_gate)
        {
            _context?.Dispose();
            _context = null;
        }
    }

    /// <summary>
    /// The members of <paramref name="type"/>, grouped by name so that every overload of one name
    /// shares one page. Empty when the type cannot be resolved, which a missing dependency of a
    /// third-party assembly can cause and which is not worth failing a directory listing over.
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// The area this reader belongs to has been replaced by <c>refresh</c>, <c>forget</c> or a
    /// second <c>ingest</c>. The handlers turn this into <c>ESTALE</c>, which tells a client
    /// holding a fid across that change to walk the path again — where it finds the new area.
    /// Answering with an empty member list instead produced a type page with no members and a
    /// directory with no member files, silently and for good.
    /// </exception>
    internal IReadOnlyList<MemberGroup> MembersOf(TypeRecord type)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            try
            {
                MetadataLoadContext context = _context ??= new MetadataLoadContext(_resolver, _core);
                Open.Get(this, _ => true);

                Assembly assembly = context.LoadFromAssemblyPath(type.Assembly.Path);
                Type? resolved = assembly.GetType(type.MetadataFullName, throwOnError: false);

                return resolved is null ? [] : Group(resolved);
            }
            catch (Exception exception) when (exception is not ObjectDisposedException)
            {
                // Anything at all, and deliberately so. This resolves and formats types from
                // assemblies /ctl accepts from a caller: function-pointer signatures, modreq and
                // modopt, and malformed generic instantiations surface out of
                // MetadataLoadContext as NotSupportedException or InvalidOperationException, none
                // of which a named list would have caught. Letting one escape makes the whole
                // directory an unexplained EIO; a type with no members and a line on stderr is
                // the smaller loss.
                Catalog.Diagnostics.Report($"describing {type.FullName} from {type.Assembly.SimpleName}", exception);

                return [];
            }
        }
    }

    private static List<MemberGroup> Group(Type type)
    {
        const BindingFlags Flags = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        var groups = new Dictionary<(MemberSort Kind, string Name), List<MemberSignature>>();
        var order = new List<(MemberSort Kind, string Name)>();

        bool isEnum = type.IsEnum;
        bool isDelegate = IsDelegate(type);

        foreach (MemberInfo member in type.GetMembers(Flags))
        {
            if (Sort(member) is not { } kind || !IsVisible(member) || member.Name.StartsWith('<'))
            {
                continue;
            }

            if (IsPlumbing(member, isEnum, isDelegate))
            {
                continue;
            }

            var key = (kind, member.Name);

            if (!groups.TryGetValue(key, out List<MemberSignature>? signatures))
            {
                groups[key] = signatures = [];
                order.Add(key);
            }

            signatures.Add(new MemberSignature(CSharpSignature.Declaration(member), DocIdWriter.For(member)));
        }

        return Uniquify(
        [
            .. order
                .OrderBy(key => key.Kind)
                .ThenBy(key => key.Name, StringComparer.Ordinal)
                .Select(key => new MemberGroup(
                    key.Name,
                    PathName.ForMember(key.Name),
                    key.Kind,
                    groups[key])),
        ]);
    }

    /// <summary>
    /// Gives every group a file stem no other group of this type has taken.
    /// </summary>
    /// <remarks>
    /// Two members can want one file. A name is mangled on the way to a path — a colon and an
    /// asterisk both become an underscore — and IL allows a field and a method of one name where
    /// C# does not. The comparison ignores case because this tree is re-exported over SMB, where
    /// <c>Item.md</c> and <c>item.md</c> are one file. Without this the second page wins the name
    /// map and the first is listed but unreachable, which is worse than either name.
    /// </remarks>
    private static List<MemberGroup> Uniquify(List<MemberGroup> members)
    {
        var taken = new HashSet<string>(members.Count, StringComparer.OrdinalIgnoreCase);

        for (int at = 0; at < members.Count; at++)
        {
            string stem = members[at].PathName;

            if (taken.Add(stem))
            {
                continue;
            }

            string unique = stem;

            for (int suffix = 2; !taken.Add(unique); suffix++)
            {
                unique = $"{stem}-{suffix}";
            }

            members[at] = members[at] with { PathName = unique };
        }

        return members;
    }

    /// <summary>
    /// What sort of member this is, or null for one that is not surface of its own: the accessors
    /// a property or an event generates are reached through those, never on their own page.
    /// </summary>
    private static MemberSort? Sort(MemberInfo member) => member switch
    {
        ConstructorInfo => MemberSort.Constructor,
        MethodInfo method => IsAccessor(method) ? null : MemberSort.Method,
        PropertyInfo => MemberSort.Property,
        FieldInfo => MemberSort.Field,
        EventInfo => MemberSort.Event,
        _ => null,
    };

    /// <summary>
    /// Whether this member is something the compiler put there rather than something an author
    /// wrote. Every enum carries a <c>value__</c> field holding its storage, and every delegate
    /// carries a constructor and <c>Invoke</c>/<c>BeginInvoke</c>/<c>EndInvoke</c>; none of the
    /// five is ever documented, so each is an empty page for a reader to find and open. That is
    /// one wasted file per enum and four per delegate, in a tree whose whole claim is that
    /// nothing is spent on a page that says nothing.
    /// </summary>
    private static bool IsPlumbing(MemberInfo member, bool isEnum, bool isDelegate) =>
        (isEnum && member is FieldInfo { Name: "value__" })
        || (isDelegate && member.Name is "Invoke" or "BeginInvoke" or "EndInvoke" or ".ctor");

    private static bool IsDelegate(Type type)
    {
        for (Type? at = type.BaseType; at is not null; at = at.BaseType)
        {
            if (at.FullName is "System.MulticastDelegate" or "System.Delegate")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAccessor(MethodInfo method) =>
        method.IsSpecialName
        && (method.Name.StartsWith("get_", StringComparison.Ordinal)
            || method.Name.StartsWith("set_", StringComparison.Ordinal)
            || method.Name.StartsWith("add_", StringComparison.Ordinal)
            || method.Name.StartsWith("remove_", StringComparison.Ordinal)
            || method.Name.StartsWith("raise_", StringComparison.Ordinal));

    /// <summary>
    /// Public and protected are the surface a consumer can reach; everything else is not
    /// documentation, whatever the XML file happens to carry about it.
    /// </summary>
    private static bool IsVisible(MemberInfo member) => member switch
    {
        MethodBase method => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly,
        FieldInfo field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly,
        PropertyInfo property => property.GetAccessors(nonPublic: true).Any(IsVisible),
        EventInfo declared => declared.GetAddMethod(nonPublic: true) is { } add && IsVisible(add),
        _ => false,
    };

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _context?.Dispose();
            _context = null;
        }
    }
}
