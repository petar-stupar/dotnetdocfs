using System.Text;
using DotnetDocFs.Catalog;
using NineP.Protocol;
using NineP.Server;

namespace DotnetDocFs.Internal.Server;

/// <summary>Serves <c>/ctl</c>: commands in, the command list out.</summary>
internal sealed class ControlHandler(DocControl control, DocTree tree) : IFileHandler
{
    private readonly byte[] _help = Encoding.UTF8.GetBytes(control.Help);

    /// <inheritdoc />
    public Qid Qid { get; } = new(QidType.QTFILE, tree.QidVersion, tree.QidPathOf(control));

    /// <inheritdoc />
    public ValueTask<Attr> GetAttrAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(DocAttributes.Of(
            Qid, FileKind.File, DocAttributes.ControlMode, (ulong)_help.Length, tree.Started));

    /// <inheritdoc />
    public ValueTask<IOpenFile> OpenAsync(
        OpenMode mode,
        OpenFlags flags,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IOpenFile>(new ControlChannel(control, _help));

    /// <summary>
    /// Accepts the truncation a shell redirect performs, and refuses everything else.
    /// </summary>
    /// <remarks>
    /// <c>echo cmd &gt; ctl</c> opens with <c>O_TRUNC</c>, which v9fs sends as a <c>Tsetattr</c>
    /// setting size to zero. Refusing it fails the open, so the documented way to use this file
    /// did not work on a mount at all. A command channel has no length to truncate, so the
    /// request is accepted and does nothing. A request that would change what the file *is* —
    /// its name, mode, owner or flags — is still refused.
    /// </remarks>
    public ValueTask SetAttrAsync(SetAttr update, CancellationToken cancellationToken = default)
    {
        if (update.Name is not null
            || update.Perm is not null
            || update.Flags is not null
            || update.Uid is not null
            || update.Gid is not null
            || update.GroupName is not null)
        {
            throw new NinePException(NinePError.FromErrno(Errno.EROFS));
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask ClunkAsync(bool wasOpen, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask FsyncAsync(bool dataOnly, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
