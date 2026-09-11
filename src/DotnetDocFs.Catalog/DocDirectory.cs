namespace DotnetDocFs.Catalog;

/// <summary>
/// A directory of the served tree. Children are produced when they are first asked for, which is
/// what keeps the catalog cheap: listing a namespace reads type names out of metadata, and
/// nothing reads a documentation file until a page below it is actually opened.
/// </summary>
public abstract class DocDirectory : DocNode
{
    private protected DocDirectory(string name, DocNodeKind kind) : base(name, kind)
    {
    }

    /// <summary>The entries of this directory, in the order they should be listed.</summary>
    public abstract IReadOnlyList<DocNode> Children { get; }

    /// <summary>
    /// The child named <paramref name="name"/>, or null. Names are matched exactly: 9P is
    /// case-sensitive, and a tree that guessed would hand a caller a different type than the one
    /// it walked to.
    /// </summary>
    public virtual DocNode? Find(string name)
    {
        foreach (DocNode child in Children)
        {
            if (string.Equals(child.Name, name, StringComparison.Ordinal))
            {
                return child;
            }
        }

        return null;
    }
}
