namespace DotnetDocFs.Catalog;

/// <summary>
/// The POSIX error numbers a <see cref="CatalogException"/> reports. They are stated here rather
/// than taken from the 9P library so that the catalog keeps knowing nothing about the protocol.
/// </summary>
public static class CatalogErrno
{
    /// <summary>
    /// Invalid argument: a command that is not one of the commands, or one whose argument names
    /// something that is not there.
    /// </summary>
    /// <remarks>
    /// A refusal from <c>/ctl</c> is about the command that was written, not about a path being
    /// walked, and <c>write(2)</c> has no <c>ENOENT</c> to return. Naming a file that does not
    /// exist makes the argument invalid, which is what the caller needs to know.
    /// </remarks>
    public const int InvalidArgument = 22;

    /// <summary>File too large: an assembly over the ingest limit.</summary>
    public const int TooLarge = 27;
}
