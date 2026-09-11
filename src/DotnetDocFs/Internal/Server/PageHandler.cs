using DotnetDocFs.Catalog;
using NineP.Protocol;
using NineP.Server;

namespace DotnetDocFs.Internal.Server;

/// <summary>
/// Serves one <see cref="DocPage"/> as a read-only file. The page renders on the first call that
/// needs it — a stat for the size, or the first read — and remembers the size it produced, so the
/// size a client stats and the bytes it reads cannot disagree however long the two are apart.
/// </summary>
internal sealed class PageHandler(DocPage page, DocTree tree) : IFileHandler
{
    /// <inheritdoc />
    public Qid Qid { get; } = new(QidType.QTFILE, tree.QidVersion, tree.QidPathOf(page));

    /// <inheritdoc />
    public async ValueTask<Attr> GetAttrAsync(CancellationToken cancellationToken = default)
    {
        ulong size = await page.SizeAsync(cancellationToken).ConfigureAwait(false);

        return DocAttributes.Of(Qid, FileKind.File, DocAttributes.PageMode, size, tree.Started);
    }

    /// <summary>
    /// Renders the page and holds its bytes for as long as this open lasts.
    /// </summary>
    /// <remarks>
    /// The bytes have to be held by the open, not looked up per read. A page bigger than one
    /// <c>msize</c> arrives as several reads, and the page itself holds its bytes only weakly —
    /// so with nothing else referencing them, a collection between two chunks dropped them and
    /// the next chunk re-rendered the page. That is the design working as intended on the memory
    /// side and wrong on this one: what a client is reading is exactly what should be held.
    /// </remarks>
    public async ValueTask<IOpenFile> OpenAsync(
        OpenMode mode,
        OpenFlags flags,
        CancellationToken cancellationToken = default) =>
        new Open(await page.ContentAsync(cancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public ValueTask SetAttrAsync(SetAttr update, CancellationToken cancellationToken = default) =>
        throw new NinePException(NinePError.FromErrno(Errno.EROFS));

    /// <inheritdoc />
    public ValueTask ClunkAsync(bool wasOpen, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask FsyncAsync(bool dataOnly, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    /// <summary>One open of one page: the rendered bytes, served from memory.</summary>
    private sealed class Open(ReadOnlyMemory<byte> content) : IOpenFile
    {
        /// <inheritdoc />
        public ValueTask<int> ReadAsync(
            ulong offset,
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (offset >= (ulong)content.Length)
            {
                return ValueTask.FromResult(0);
            }

            int at = (int)offset;
            int count = Math.Min(buffer.Length, content.Length - at);

            content.Slice(at, count).CopyTo(buffer);

            return ValueTask.FromResult(count);
        }

        /// <inheritdoc />
        public ValueTask<int> WriteAsync(
            ulong offset,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default) =>
            throw new NinePException(NinePError.FromErrno(Errno.EROFS));

        /// <inheritdoc />
        public ValueTask<ulong> GetSizeAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult((ulong)content.Length);

        /// <inheritdoc />
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
