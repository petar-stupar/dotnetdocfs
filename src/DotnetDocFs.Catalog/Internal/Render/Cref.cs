using DotnetDocFs.Catalog.Internal.Naming;

namespace DotnetDocFs.Catalog.Internal.Render;

/// <summary>
/// Resolves the <c>cref</c> of a documentation comment to a page of this tree.
/// </summary>
/// <remarks>
/// A documentation id is <c>&lt;kind&gt;:&lt;name&gt;</c>: <c>T:System.String</c>,
/// <c>M:System.String.Compare(System.String,System.String)</c>,
/// <c>M:System.Array.System#Collections#IList#Add(System.Object)</c> for an explicit interface
/// implementation, <c>M:System.String.#ctor(System.Char[])</c> for a constructor.
/// </remarks>
internal static class Cref
{
    /// <summary>
    /// The page <paramref name="cref"/> names and the label it should read as, or null when
    /// nothing in this tree carries it. Null is the honest answer for a type this machine does
    /// not have, and the renderer turns it into code rather than a broken link.
    /// </summary>
    internal static CrefTarget? Resolve(string? cref, ILinkResolver links)
    {
        if (string.IsNullOrEmpty(cref) || cref.Length < 3 || cref[1] != ':')
        {
            return null;
        }

        char kind = cref[0];
        string name = cref[2..];

        if (kind == 'T')
        {
            return links.TypePage(name) is { } page
                ? new CrefTarget(page, links.TypeLabel(name) ?? ShortName(name))
                : null;
        }

        if (kind is not ('M' or 'P' or 'F' or 'E'))
        {
            // N: is a namespace and !: is a cref the compiler could not bind. Neither is a page.
            return null;
        }

        // The parameter list distinguishes overloads, which share one page, so it is dropped.
        int parenthesis = name.IndexOf('(', StringComparison.Ordinal);

        if (parenthesis >= 0)
        {
            name = name[..parenthesis];
        }

        // A generic method writes its arity as ``2 after the name.
        int arity = name.IndexOf("``", StringComparison.Ordinal);

        if (arity >= 0)
        {
            name = name[..arity];
        }

        return SplitTypeAndMember(name, links);
    }

    /// <summary>
    /// Finds the boundary between the type and the member by trying the longest type name first.
    /// Guessing at the last dot is wrong for a member of a nested type, and the name of an
    /// explicit interface implementation carries no dots at all — it writes them as <c>#</c> —
    /// so the dots that remain are all candidate boundaries.
    /// </summary>
    private static CrefTarget? SplitTypeAndMember(string name, ILinkResolver links)
    {
        for (int dot = name.LastIndexOf('.'); dot > 0; dot = name.LastIndexOf('.', dot - 1))
        {
            string typeName = name[..dot];

            if (links.TypePage(typeName) is null)
            {
                continue;
            }

            // A documentation id writes the dots of an explicit interface implementation as '#';
            // metadata writes them as dots, which is what the page is named after.
            string member = name[(dot + 1)..].Replace('#', '.');

            if (links.MemberPage(typeName, member) is not { } page)
            {
                return null;
            }

            // The hashes are already dots by now, so ".ctor" is the only spelling that arrives.
            string label = member == ".ctor"
                ? links.TypeLabel(typeName) ?? ShortName(typeName)
                : $"{links.TypeLabel(typeName) ?? ShortName(typeName)}.{member}";

            return new CrefTarget(page, label);
        }

        return null;
    }

    /// <summary>The last dotted segment of a name, for a type this tree cannot label properly.</summary>
    private static string ShortName(string name)
    {
        int dot = name.LastIndexOf('.');
        string last = dot < 0 ? name : name[(dot + 1)..];
        int tick = last.LastIndexOf('`');

        return tick < 0 ? last : last[..tick];
    }
}
