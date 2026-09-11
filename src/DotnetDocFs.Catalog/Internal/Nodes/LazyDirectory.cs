namespace DotnetDocFs.Catalog.Internal.Nodes;

/// <summary>
/// A directory whose entries are built the first time something asks for them, and then kept
/// until the catalog's <see cref="Generation"/> moves. Every level of this tree is one of these:
/// the root is cheap, and the cost of a namespace is paid by the caller who walks into it.
/// </summary>
internal sealed class LazyDirectory(
    string name,
    DocNodeKind kind,
    string key,
    Func<IReadOnlyList<DocNode>> children,
    Generation? generation = null) : DocDirectory(name, kind)
{
    private readonly Lock _gate = new();
    private IReadOnlyList<DocNode>? _children;
    private Dictionary<string, DocNode>? _byName;
    private int _builtUnder = -1;

    /// <inheritdoc />
    public override string Key { get; } = key;

    /// <inheritdoc />
    public override IReadOnlyList<DocNode> Children => Materialize().Children;

    /// <inheritdoc />
    public override DocNode? Find(string childName) => Materialize().ByName.GetValueOrDefault(childName);

    private (IReadOnlyList<DocNode> Children, Dictionary<string, DocNode> ByName) Materialize()
    {
        lock (_gate)
        {
            int now = generation?.Current ?? 0;

            if (_children is null || _builtUnder != now)
            {
                _children = children();
                _builtUnder = now;
                _byName = new Dictionary<string, DocNode>(_children.Count, StringComparer.Ordinal);

                foreach (DocNode child in _children)
                {
                    _byName[child.Name] = child;
                }
            }

            return (_children, _byName!);
        }
    }
}
