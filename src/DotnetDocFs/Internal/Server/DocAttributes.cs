using NineP.Protocol;

namespace DotnetDocFs.Internal.Server;

/// <summary>
/// The attributes every node of this tree reports. The tree is generated and read-only apart from
/// <c>/ctl</c>, so the permission bits are the same for every file of a kind and say so plainly:
/// a client that tries to write to a page is refused by the mode before it reaches a handler.
/// </summary>
internal static class DocAttributes
{
    /// <summary>0555: a directory anyone may list and walk, and nobody may write.</summary>
    internal const FilePermissions DirectoryMode =
        FilePermissions.OwnerReadExecute | FilePermissions.GroupReadExecute | FilePermissions.OtherReadExecute;

    /// <summary>0444: a page anyone may read and nobody may write.</summary>
    internal const FilePermissions PageMode = FilePermissions.AllRead;

    /// <summary>
    /// 0666: a control file that takes commands and describes itself.
    /// </summary>
    /// <remarks>
    /// The read bit is not decoration. Txattrwalk requires read permission before the server even
    /// looks for an extended-attribute handler, and Samba asks for extended attributes when it
    /// opens a file, so a write-only control file cannot be opened at all through the SMB bridge:
    /// the refusal surfaces as "permission denied" on <c>echo … &gt; ctl</c>. The write bits are
    /// open to all because the bridge writes as <c>nobody</c>, not as the tree's owner.
    /// </remarks>
    internal const FilePermissions ControlMode = FilePermissions.AllRead | FilePermissions.AllWrite;

    /// <summary>
    /// The attributes of one node.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three timestamps are the moment the server started, not the moment of the call: a tree
    /// whose mtime moved on every stat would defeat every client and editor that caches on it, and
    /// nothing here changes on its own.
    /// </para>
    /// <para>
    /// The owner is stated rather than left unknown. <c>Attr.Uid</c> defaults to <c>NONUNAME</c>
    /// (<c>0xFFFFFFFF</c>), which is the right sentinel for 9P2000 and 9P2000.u, where ownership
    /// travels as a name. In 9P2000.L it is a number the client must map, and Linux cannot map
    /// that one: it substitutes the overflow uid, shows every file as <c>nobody</c>, and answers
    /// <c>EOVERFLOW</c> — "Value too large for data type" — to any operation that needs the real
    /// owner. An <c>rm</c> on this tree failed that way, locally, without a message ever reaching
    /// the server to be refused honestly. Root owns a generated system tree that nobody may write,
    /// and 0 is mappable everywhere.
    /// </para>
    /// </remarks>
    internal static Attr Of(Qid qid, FileKind kind, FilePermissions mode, ulong size, TimeSpec when) => new()
    {
        Qid = qid,
        Kind = kind,
        Perm = mode,
        Size = size,
        Uid = 0,
        Gid = 0,
        UserName = "root",
        GroupName = "root",
        ModifierUid = 0,
        ModifierName = "root",
        ATime = when,
        MTime = when,
        CTime = when,
        BTime = when,
    };
}
