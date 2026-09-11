namespace DotnetDocFs.Catalog.Internal.Nodes;

/// <summary>
/// A directory whose entries change while the server runs, which is what <c>/ctl</c> needs.
/// Entries are replaced as one list rather than mutated in place, so a listing in flight sees
/// either the old set or the new one and never a half-built one.
/// </summary>
internal sealed class MutableDirectory(string name, DocNodeKind kind, string key) : DocDirectory(name, kind)
{
    private volatile IReadOnlyList<DocNode> _children = [];

    /// <inheritdoc />
    public override string Key { get; } = key;

    /// <inheritdoc />
    public override IReadOnlyList<DocNode> Children => _children;

    /// <summary>Replaces every entry.</summary>
    internal void Replace(IReadOnlyList<DocNode> children) => _children = children;
}
