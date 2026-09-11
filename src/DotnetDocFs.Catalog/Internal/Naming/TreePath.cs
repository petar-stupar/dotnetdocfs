namespace DotnetDocFs.Catalog.Internal.Naming;

/// <summary>
/// An absolute path inside the served tree, held as its segments. Its reason for existing is
/// <see cref="RelativeToPageIn"/>: every link this catalog writes between two of its own pages is
/// a relative markdown link that resolves on a real filesystem, so an agent can follow it with an
/// ordinary file read instead of knowing anything about 9P or about where the tree is mounted.
/// </summary>
internal sealed class TreePath
{
    private readonly string[] _segments;

    private TreePath(string[] segments) => _segments = segments;

    /// <summary>The root of the served tree.</summary>
    internal static TreePath Root { get; } = new([]);

    /// <summary>This path with <paramref name="segment"/> appended.</summary>
    internal TreePath Add(string segment) => new([.. _segments, segment]);

    /// <summary>This path with each of <paramref name="segments"/> appended in order.</summary>
    internal TreePath Add(params ReadOnlySpan<string> segments)
    {
        var combined = new string[_segments.Length + segments.Length];
        _segments.CopyTo(combined, 0);
        segments.CopyTo(combined.AsSpan(_segments.Length));

        return new TreePath(combined);
    }

    /// <summary>The absolute path, as a 9P client walking from the root would spell it.</summary>
    public override string ToString() => "/" + string.Join('/', _segments);

    /// <summary>
    /// This path written as a markdown link target relative to a page that lives at
    /// <paramref name="page"/>. Both are absolute paths to files; the link is resolved against the
    /// directory holding <paramref name="page"/>, which is what a markdown reader does.
    /// </summary>
    /// <remarks>
    /// A link to the page itself is <c>#</c> rather than the empty string, which no reader follows.
    /// </remarks>
    internal string RelativeToPageIn(TreePath page)
    {
        string[] from = page._segments;
        int fromDirLength = from.Length == 0 ? 0 : from.Length - 1;

        int shared = 0;
        while (shared < fromDirLength
            && shared < _segments.Length - 1
            && string.Equals(from[shared], _segments[shared], StringComparison.Ordinal))
        {
            shared++;
        }

        int up = fromDirLength - shared;
        var parts = new List<string>(up + _segments.Length - shared);

        for (int i = 0; i < up; i++)
        {
            parts.Add("..");
        }

        for (int i = shared; i < _segments.Length; i++)
        {
            parts.Add(_segments[i]);
        }

        string link = string.Join('/', parts.Select(Encode));

        return link.Length == 0 ? "#" : link;
    }

    /// <summary>
    /// Percent-encodes the few characters that would end a markdown link target early. Type and
    /// member names are already sanitized by <see cref="PathName"/>, so this is a backstop rather
    /// than the main line of defence.
    /// </summary>
    internal static string Encode(string segment) => segment
        .Replace("%", "%25", StringComparison.Ordinal)
        .Replace(" ", "%20", StringComparison.Ordinal)
        .Replace("(", "%28", StringComparison.Ordinal)
        .Replace(")", "%29", StringComparison.Ordinal)

        // A markdown reader ends a link target at "#" and treats "[" and "]" as the start of the
        // next construct. PathName does not strip any of the three, and all three are legal in a
        // .NET member name, so an assembly handed to /ctl can put them here.
        .Replace("#", "%23", StringComparison.Ordinal)
        .Replace("[", "%5B", StringComparison.Ordinal)
        .Replace("]", "%5D", StringComparison.Ordinal);
}
