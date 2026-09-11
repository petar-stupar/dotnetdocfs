namespace DotnetDocFs.Catalog;

/// <summary>
/// A command this catalog refused, carrying both the reason a reader should see and the POSIX
/// error number it amounts to.
/// </summary>
/// <remarks>
/// Both are needed because the dialects carry different things. 9P2000 and 9P2000.u carry an
/// error string, so a caller sees the sentence; 9P2000.L carries a number and nothing else, so on
/// a Linux mount the number is the entire message. A refusal that reported only one of them would
/// be mute on half the clients that can reach this tree.
/// </remarks>
public sealed class CatalogException : Exception
{
    /// <summary>Creates an exception carrying <paramref name="message"/> and its error number.</summary>
    public CatalogException(string message, int errno) : base(message) => Errno = errno;

    /// <summary>Creates an exception whose refusal is an invalid argument.</summary>
    public CatalogException(string message) : this(message, CatalogErrno.InvalidArgument)
    {
    }

    /// <summary>Creates an exception carrying <paramref name="message"/> over an inner cause.</summary>
    public CatalogException(string message, Exception innerException) : base(message, innerException) =>
        Errno = CatalogErrno.InvalidArgument;

    /// <summary>Creates an exception with no message of its own.</summary>
    public CatalogException() => Errno = CatalogErrno.InvalidArgument;

    /// <summary>The POSIX error number this refusal amounts to.</summary>
    public int Errno { get; }
}
