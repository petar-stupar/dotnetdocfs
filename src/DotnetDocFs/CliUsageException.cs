namespace DotnetDocFs;

/// <summary>A command line this program does not understand.</summary>
internal sealed class CliUsageException : Exception
{
    /// <summary>Creates an exception carrying <paramref name="message"/>.</summary>
    internal CliUsageException(string message) : base(message)
    {
    }

    /// <summary>Creates an exception carrying <paramref name="message"/> over an inner cause.</summary>
    internal CliUsageException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
