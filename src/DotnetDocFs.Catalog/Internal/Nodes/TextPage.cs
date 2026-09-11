using System.Text;

namespace DotnetDocFs.Catalog.Internal.Nodes;

/// <summary>
/// A page rendered by a delegate the first time it is needed.
/// </summary>
/// <remarks>
/// <para>
/// A client stats a file before it reads it and both answers must agree, so the size of the first
/// render is kept for good: a read past the stated size would be a short read for no reason. The
/// bytes themselves are held only weakly. A stat-then-read pair sees one render because the read
/// holds the reference the stat produced, and a crawl that walks away from a page lets it go —
/// which is the difference between a server whose memory tracks what is being read and one whose
/// memory tracks everything that was ever read.
/// </para>
/// <para>
/// This is only safe because rendering is deterministic: nothing in a page comes from a clock or
/// from anything else that moves, so a second render of the same page is byte-for-byte the first.
/// The <c>timestamp</c> in the frontmatter is the time its area entered the tree, not the time of
/// the render, for exactly this reason.
/// </para>
/// </remarks>
internal sealed class TextPage(string name, DocNodeKind kind, string key, Func<string> render)
    : DocPage(name, kind)
{
    private readonly Lock _gate = new();
    private WeakReference<byte[]>? _content;
    private int _size = -1;

    /// <inheritdoc />
    public override string Key { get; } = key;

    /// <inheritdoc />
    public override ValueTask<ReadOnlyMemory<byte>> ContentAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_content is not null && _content.TryGetTarget(out byte[]? held))
            {
                return ValueTask.FromResult<ReadOnlyMemory<byte>>(held);
            }

            byte[] rendered = Encoding.UTF8.GetBytes(render());

            _content = new WeakReference<byte[]>(rendered);
            _size = rendered.Length;

            return ValueTask.FromResult<ReadOnlyMemory<byte>>(rendered);
        }
    }

    /// <inheritdoc />
    public override async ValueTask<ulong> SizeAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_size >= 0)
            {
                return (ulong)_size;
            }
        }

        return (ulong)(await ContentAsync(cancellationToken).ConfigureAwait(false)).Length;
    }
}
