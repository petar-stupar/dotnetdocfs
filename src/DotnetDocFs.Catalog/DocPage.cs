namespace DotnetDocFs.Catalog;

/// <summary>
/// A readable file of the served tree: one markdown document with OKF frontmatter.
/// </summary>
public abstract class DocPage : DocNode
{
    private protected DocPage(string name, DocNodeKind kind) : base(name, kind)
    {
    }

    /// <summary>
    /// The rendered document, as the bytes a read returns. Rendering is deferred to this call, and
    /// the bytes are held for as long as the caller keeps them: a stat followed by a read renders
    /// once.
    /// </summary>
    public abstract ValueTask<ReadOnlyMemory<byte>> ContentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// How many bytes <see cref="ContentAsync"/> returns. A page that has been rendered once
    /// answers from what it remembers, so a stat costs nothing and cannot disagree with a read.
    /// </summary>
    public virtual async ValueTask<ulong> SizeAsync(CancellationToken cancellationToken = default) =>
        (ulong)(await ContentAsync(cancellationToken).ConfigureAwait(false)).Length;
}
