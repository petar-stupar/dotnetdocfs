using System.Text;

namespace DotnetDocFs.Catalog.Internal.Naming;

/// <summary>
/// Turns a .NET name into one path element. The rules are few and are stated on the root
/// <c>index.md</c> as well, because an agent that knows them can construct a path instead of
/// walking to find it.
/// </summary>
internal static class PathName
{
    /// <summary>
    /// The longest name this tree hands out, in <em>bytes</em>, leaving room for the <c>.md</c> a
    /// page adds. <c>Tstatfs</c> advertises 255, which is what every filesystem this can be
    /// re-exported on allows, and a name over it is refused by the client rather than by anything
    /// here. Bytes and not characters: a name of 250 non-ASCII characters is up to a kilobyte on
    /// the wire, and obfuscated assemblies — which is what <c>/ctl</c> is pointed at — routinely
    /// use identifiers that are not ASCII at all.
    /// </summary>
    private const int MaxNameBytes = 250;

    /// <summary>Names Windows refuses whatever the extension, and this tree may be mounted there.</summary>
    private static readonly HashSet<string> ReservedOnWindows = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// The directory name for a type. Metadata spells arity with a backtick — <c>List`1</c> —
    /// which is awkward in every shell, so the tree spells it <c>List-1</c>.
    /// </summary>
    internal static string ForType(string metadataName)
    {
        int tick = metadataName.LastIndexOf('`');
        string name = tick < 0
            ? metadataName
            : string.Concat(metadataName.AsSpan(0, tick), "-", metadataName.AsSpan(tick + 1));

        return Sanitize(name);
    }

    /// <summary>
    /// The file stem for a member name, covering every overload that shares it. The two names
    /// metadata spells with a leading dot would be hidden files, so they are spelled out.
    /// </summary>
    /// <remarks>
    /// <c>index</c> is taken. Every directory of this tree serves its own <c>index.md</c>, so a
    /// member of that name would be a second entry called <c>index.md</c> beside it: a readdir
    /// would list the name twice and a lookup would resolve to whichever won the name map, which
    /// is the member page — leaving the type's own index unreachable. The match ignores case
    /// because this tree is mounted over SMB on macOS and Windows, where <c>Index.md</c> and
    /// <c>index.md</c> are one file.
    /// </remarks>
    internal static string ForMember(string metadataName) => Sanitize(metadataName switch
    {
        // Metadata spells these with a leading dot and a documentation id with a leading hash;
        // both arrive here, and both would be a hidden file on a Unix mount.
        ".ctor" or "#ctor" => "constructors",
        ".cctor" or "#cctor" => "static-constructor",
        _ when metadataName.Equals("index", StringComparison.OrdinalIgnoreCase) => metadataName + "_",
        _ => metadataName,
    });

    /// <summary>
    /// Replaces what a filesystem cannot carry. Explicit interface implementations arrive as
    /// <c>System.IDisposable.Dispose</c>, whose dots are fine; separators and control bytes are
    /// not, and a name that collides with a Windows device gets a trailing underscore.
    /// </summary>
    /// <remarks>
    /// Nothing in the base class library reaches the last three rules, and every one of them is
    /// reachable through <c>/ctl</c>, which takes a path to an assembly this program did not
    /// build. A type called <c>..</c> is legal metadata and an illegal directory entry: a client
    /// that walked into it would climb out of the tree. Trailing dots and spaces are silently
    /// dropped by Windows, which turns two distinct members into one file. And a name may be any
    /// length metadata allows, where <c>Tstatfs</c> here promises 255 bytes.
    /// </remarks>
    /// <summary>
    /// Brings a name under the byte limit, cutting on a character boundary and replacing what was
    /// cut with a digest of the whole name — truncation alone would merge two long names that
    /// share a prefix.
    /// </summary>
    private static string Shorten(string name)
    {
        string digest = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..8];

        int room = MaxNameBytes - digest.Length - 1;
        int kept = 0;
        int bytes = 0;

        // Rune by rune, so a multi-byte character is never cut in half.
        foreach (System.Text.Rune rune in name.EnumerateRunes())
        {
            int width = rune.Utf8SequenceLength;

            if (bytes + width > room)
            {
                break;
            }

            bytes += width;
            kept += rune.Utf16SequenceLength;
        }

        return string.Concat(name.AsSpan(0, kept), "-", digest);
    }

    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (char c in name)
        {
            builder.Append(c switch
            {
                '/' or '\\' => '.',
                ':' or '*' or '?' or '"' or '<' or '>' or '|' => '_',
                _ when char.IsControl(c) => '_',
                _ => c,
            });
        }

        // "." and ".." are the directory's own entries on every filesystem there is.
        if (builder.Length > 0 && builder.ToString().Trim('.').Length == 0)
        {
            builder.Append('_');
        }

        // Windows drops these on the way to disk, silently merging two names into one.
        while (builder.Length > 0 && builder[^1] is '.' or ' ')
        {
            builder.Length--;
        }

        if (builder.Length == 0)
        {
            return "_";
        }

        string result = builder.ToString();

        if (Encoding.UTF8.GetByteCount(result) > MaxNameBytes)
        {
            result = Shorten(result);
        }


        return ReservedOnWindows.Contains(result) ? result + "_" : result;
    }
}
