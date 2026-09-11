namespace DotnetDocFs.Catalog;

/// <summary>
/// Where the catalog reports a failure it recovered from.
/// </summary>
/// <remarks>
/// These are the failures with nowhere else to go. A command refused by <c>/ctl</c> travels back
/// to the caller as a <see cref="CatalogException"/>; a type whose members could not be described
/// has no such channel — the page renders, it is just emptier than it should be, and a reader
/// cannot tell that from a type that genuinely has no members. Without somewhere to say so, the
/// whole class of failure is invisible on both sides of the mount.
/// </remarks>
public static class Diagnostics
{
    private static Action<string>? sink;

    /// <summary>Sends every report to <paramref name="to"/>. Null silences them again.</summary>
    public static void To(Action<string>? to) => sink = to;

    /// <summary>Reports a recovered failure, naming what was being done when it happened.</summary>
    internal static void Report(string doing, Exception exception) =>
        sink?.Invoke($"dotnetdoc: {doing}: {exception.GetType().Name}: {exception.Message}");

    /// <summary>Reports something the catalog noticed rather than caught.</summary>
    internal static void Report(string message) => sink?.Invoke("dotnetdoc: " + message);
}
