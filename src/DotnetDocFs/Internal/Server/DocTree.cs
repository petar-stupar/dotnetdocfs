using DotnetDocFs.Catalog;
using NineP.Protocol;
using NineP.Protocol.Auth;
using NineP.Server;

namespace DotnetDocFs.Internal.Server;

/// <summary>
/// The catalog as a 9P filesystem. It owns the mapping from a catalog node to a handler, which is
/// the whole of the 9P layer: the catalog knows nothing about the protocol and this knows nothing
/// about .NET metadata.
/// </summary>
internal sealed class DocTree : IFilesystem
{
    private readonly DocCatalog _catalog;
    private readonly QidPaths _qids = new();

    internal DocTree(DocCatalog catalog)
    {
        _catalog = catalog;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Started = new TimeSpec(now.ToUnixTimeSeconds(), 0);
    }

    /// <summary>
    /// The time every file in this tree reports. It is when the server started: nothing here
    /// changes on its own, and a tree whose mtime moved on every stat would defeat any client
    /// that caches on it.
    /// </summary>
    internal TimeSpec Started { get; }

    /// <inheritdoc />
    public ValueTask<IDirectoryHandler> AttachAsync(
        Identity identity,
        string aname,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IDirectoryHandler>(new DirectoryHandler(_catalog.Root, this));

    /// <summary>The qid path of <paramref name="node"/>, stable for as long as the server runs.</summary>
    internal ulong QidPathOf(DocNode node) => _qids.Of(node.Key);

    /// <summary>
    /// What every qid of this tree carries as its version. A path keeps its qid path across a
    /// <c>refresh</c> — it is still the same place — but what is at it may have changed, and the
    /// version is the only field that can say so. Left at zero, a client caching on the qid went
    /// on serving the bytes it had, which is exactly what <c>refresh</c> is asked to undo.
    /// </summary>
    internal uint QidVersion => _catalog.Revision;

    /// <summary>The qid of <paramref name="node"/>, for a directory entry.</summary>
    internal Qid QidOf(DocNode node) => new(
        node is DocDirectory ? QidType.QTDIR : QidType.QTFILE,
        QidVersion,
        QidPathOf(node));

    /// <summary>The handler for <paramref name="node"/>, whichever kind it is.</summary>
    internal IHandler HandlerFor(DocNode node) => node switch
    {
        DocDirectory directory => new DirectoryHandler(directory, this),
        DocControl control => new ControlHandler(control, this),
        DocPage page => new PageHandler(page, this),
        _ => throw new NinePException(NinePError.FromErrno(Errno.EIO)),
    };
}
