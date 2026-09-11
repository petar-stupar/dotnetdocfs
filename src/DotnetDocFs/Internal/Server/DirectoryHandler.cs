using DotnetDocFs.Catalog;
using NineP.Protocol;
using NineP.Server;

namespace DotnetDocFs.Internal.Server;

/// <summary>
/// Serves one <see cref="DocDirectory"/>. Every refusal below is the same refusal: this tree is
/// generated from the machine's own assemblies, so there is nothing here a client could
/// meaningfully create, delete or rename.
/// </summary>
internal sealed class DirectoryHandler(DocDirectory directory, DocTree tree)
    : IDirectoryHandler, IStatFsCapability
{
    /// <summary>
    /// The entries this listing is walking, taken once when it began.
    /// </summary>
    /// <remarks>
    /// The cursor is an index, so the list it indexes has to hold still. Most of this tree does —
    /// a materialized directory never changes — but <c>/docs/ingested</c> is replaced wholesale
    /// by <c>/ctl</c>, and an ingest between two pages of one readdir would shift every entry
    /// after it: the client silently skips a name or sees one twice. A handler is per-fid, which
    /// is exactly the lifetime of one listing.
    /// </remarks>
    private IReadOnlyList<DocNode>? _listing;

    /// <inheritdoc />
    public Qid Qid { get; } = new(QidType.QTDIR, tree.QidVersion, tree.QidPathOf(directory));

    /// <inheritdoc />
    public ValueTask<Attr> GetAttrAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(DocAttributes.Of(Qid, FileKind.Directory, DocAttributes.DirectoryMode, 0, tree.Started));

    /// <inheritdoc />
    public ValueTask<IHandler?> LookupAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DocNode? child = directory.Find(name);

        return ValueTask.FromResult(child is null ? null : tree.HandlerFor(child));
    }

    /// <inheritdoc />
    public ValueTask<DirectoryListing> ReadDirAsync(
        ulong cursor,
        int max,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The cursor is the count already delivered, so a listing resumed after a clunk and a
        // re-walk lands in the same place as one read straight through. Starting over takes a
        // fresh snapshot; continuing keeps the one the listing began with.
        IReadOnlyList<DocNode> children = cursor == 0
            ? _listing = directory.Children
            : _listing ??= directory.Children;

        int at = cursor > (ulong)children.Count ? children.Count : (int)cursor;
        var page = new List<DirEntry>(Math.Min(max, children.Count - at));

        while (at < children.Count && page.Count < max)
        {
            DocNode child = children[at];
            at++;

            page.Add(new DirEntry(
                child.Name,
                tree.QidOf(child),
                child is DocDirectory ? FileKind.Directory : FileKind.File,
                (ulong)at));
        }

        return ValueTask.FromResult(new DirectoryListing(page, (ulong)at, at >= children.Count));
    }

    /// <inheritdoc />
    public ValueTask SetAttrAsync(SetAttr update, CancellationToken cancellationToken = default) =>
        throw new NinePException(NinePError.FromErrno(Errno.EROFS));

    /// <inheritdoc />
    public ValueTask<IHandler> CreateAsync(CreateRequest request, CancellationToken cancellationToken = default) =>
        throw new NinePException(NinePError.FromErrno(Errno.EROFS));

    /// <inheritdoc />
    public ValueTask RemoveAsync(string name, FileKind kind, CancellationToken cancellationToken = default) =>
        throw new NinePException(NinePError.FromErrno(Errno.EROFS));

    /// <inheritdoc />
    public ValueTask RenameAsync(
        string oldName,
        IDirectoryHandler newParent,
        string newName,
        CancellationToken cancellationToken = default) =>
        throw new NinePException(NinePError.FromErrno(Errno.EROFS));

    /// <summary>
    /// Answers <c>Tstatfs</c>. This is not decoration: a tree that cannot say how much space it
    /// has cannot be re-exported. Samba calls <c>disk_free</c> when a client connects to a share,
    /// and a 9P mount that answers EOPNOTSUPP makes it fail the connect, which reaches macOS as
    /// "Operation not supported" and no mount at all.
    /// </summary>
    /// <remarks>
    /// The numbers describe what this tree is rather than pretending to a disk: the pages are
    /// generated on read, so there is no used space to report and no free space to offer. It
    /// claims one nominal block, none of it free, which is the truthful shape of a read-only
    /// synthetic filesystem and keeps <c>df</c> from showing an invented capacity.
    /// </remarks>
    public ValueTask<StatFs> StatFsAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new StatFs(
            Type: StatFs.V9fsMagic,
            BlockSize: 4096,
            Blocks: 1,
            BlocksFree: 0,
            BlocksAvailable: 0,
            Files: 1,
            FilesFree: 0,
            FsId: 0,
            NameLength: 255));

    /// <inheritdoc />
    public ValueTask ClunkAsync(bool wasOpen, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask FsyncAsync(bool dataOnly, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
