namespace DotnetDocFs.Catalog;

/// <summary>
/// The control file at <c>/ctl</c>. Writing a command to it changes what the tree serves; reading
/// it returns the commands it takes.
/// </summary>
/// <remarks>
/// Plan 9 would make this write-only, and it was, until a mount proved that unworkable. A file
/// with no read bit cannot be opened through anything that re-exports the tree — Samba asks for
/// extended attributes when it opens a file, that ask needs read permission, and the refusal
/// arrives at the user as "permission denied" on a shell redirect. Reading it now answers with
/// the command list, which is more useful than an empty file in any case.
/// </remarks>
public abstract class DocControl : DocNode
{
    private protected DocControl(string name) : base(name, DocNodeKind.Area)
    {
    }

    /// <summary>The commands this file accepts, as the text a read of it returns.</summary>
    public abstract string Help { get; }

    /// <summary>
    /// Runs one command. A command that cannot be carried out throws
    /// <see cref="CatalogException"/>, whose message the server returns as the error of the write:
    /// the caller sees why it failed on the write itself rather than having to look elsewhere.
    /// </summary>
    public abstract ValueTask ExecuteAsync(string command, CancellationToken cancellationToken = default);
}
