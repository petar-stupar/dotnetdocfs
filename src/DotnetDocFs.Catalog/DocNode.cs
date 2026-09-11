namespace DotnetDocFs.Catalog;

/// <summary>
/// One entry in the served tree. A node is either a <see cref="DocDirectory"/> or a
/// <see cref="DocPage"/>; nothing else implements it.
/// </summary>
public abstract class DocNode
{
    private protected DocNode(string name, DocNodeKind kind)
    {
        Name = name;
        Kind = kind;
    }

    /// <summary>The single path element naming this node, already mangled for a filesystem.</summary>
    public string Name { get; }

    /// <summary>What this node stands for.</summary>
    public DocNodeKind Kind { get; }

    /// <summary>
    /// A stable identifier for this node, unique within one catalog, from which the 9P layer
    /// derives a qid path. Stability across a <c>refresh</c> is what lets a mounted client keep a
    /// walked fid meaningful.
    /// </summary>
    public abstract string Key { get; }
}
