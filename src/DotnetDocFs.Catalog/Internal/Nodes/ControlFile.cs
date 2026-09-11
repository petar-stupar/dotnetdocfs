namespace DotnetDocFs.Catalog.Internal.Nodes;

/// <summary>
/// <c>/ctl</c>: one command per write.
/// </summary>
/// <remarks>
/// The commands are <c>ingest &lt;path-to-assembly&gt;</c>, <c>forget &lt;name&gt;</c> and
/// <c>refresh</c>. A command that fails fails the write, carrying its reason, which is how a
/// caller learns what went wrong without a matching read: <c>echo ingest /x.dll &gt; ctl</c>
/// prints the error itself.
/// </remarks>
internal sealed class ControlFile(Func<string, CancellationToken, ValueTask> execute) : DocControl("ctl")
{
    /// <inheritdoc />
    public override string Key => "/ctl";

    /// <inheritdoc />
    public override string Help { get; } = """
        # /ctl — one command per line

        ingest <path-to-assembly.dll>   read an assembly and serve it under docs/ingested/
        forget <name>                   drop an ingested assembly
        refresh                         drop every ingested assembly, and everything
                                        this tree has already listed, so that the next
                                        walk reads the machine again

        An ingested assembly is inspected, never loaded: no code from it runs. Its prose comes
        from the .xml file beside it, when the build produced one.

        A command that fails fails the write that carried it, with the reason as the error.

            echo 'ingest /path/to/Your.Library.dll' > ctl

        """;

    /// <inheritdoc />
    public override ValueTask ExecuteAsync(string command, CancellationToken cancellationToken = default) =>
        execute(command, cancellationToken);
}
