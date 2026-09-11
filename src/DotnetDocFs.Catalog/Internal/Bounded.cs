namespace DotnetDocFs.Catalog.Internal;

/// <summary>
/// A cache that forgets. Holds at most <c>capacity</c> entries and drops the least recently used
/// one to make room, telling <c>onEvict</c> about it.
/// </summary>
/// <remarks>
/// <para>
/// The catalog renders on demand and keeps nothing on disk, which makes every in-memory map it
/// holds a cache with no upper bound unless something gives it one. The three that matter are the
/// parsed documentation files — <c>System.Runtime.xml</c> is seven megabytes on disk and several
/// times that as an element tree, and a reference pack ships a hundred of them — the member lists
/// a cross-reference has to consult to know whether it has a page to point at, and the open
/// metadata contexts, which hold a file handle each. A process that serves a mount lives for days
/// and a single <c>grep -r</c> touches all of them.
/// </para>
/// <para>
/// Recency is the right policy rather than a guess: a reader works through one namespace at a
/// time, and every type in a namespace comes out of the same documentation file.
/// </para>
/// </remarks>
internal sealed class Bounded<TKey, TValue>(
    int capacity,
    IEqualityComparer<TKey>? comparer = null,
    Action<TKey, TValue>? onEvict = null)
    where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _byKey = new(capacity, comparer);
    private readonly LinkedList<Entry> _order = new();
    private readonly Lock _gate = new();

    /// <summary>
    /// The value for <paramref name="key"/>, produced by <paramref name="create"/> if it is not
    /// held.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing is produced under the lock — not on a miss, and not on a hit that lands on an
    /// entry another thread is still producing. Evaluating inside the lock made every caller of
    /// the cache, for every key, wait behind whichever seven-megabyte parse or member enumeration
    /// happened to be in flight; the 9P library dispatches requests concurrently, so that was the
    /// server's whole throughput under a crawl.
    /// </para>
    /// <para>
    /// A failed production is not kept. A <see cref="Lazy{T}"/> built for execution and
    /// publication caches the exception it threw, so one unlucky assembly would answer the same
    /// error for the life of the entry rather than being retried.
    /// </para>
    /// </remarks>
    internal TValue Get(TKey key, Func<TKey, TValue> create)
    {
        Lazy<TValue> value;
        List<Entry> evicted = [];

        lock (_gate)
        {
            if (_byKey.TryGetValue(key, out LinkedListNode<Entry>? found))
            {
                _order.Remove(found);
                _order.AddFirst(found);

                value = found.Value.Value;
            }
            else
            {
                value = new Lazy<TValue>(() => create(key), LazyThreadSafetyMode.ExecutionAndPublication);
                _byKey[key] = _order.AddFirst(new Entry(key, value));

                while (_order.Count > capacity)
                {
                    LinkedListNode<Entry> oldest = _order.Last!;

                    _order.RemoveLast();
                    _byKey.Remove(oldest.Value.Key);
                    evicted.Add(oldest.Value);
                }
            }
        }

        // Outside the lock: a hook that closes a file handle has no business holding up every
        // other reader of this cache.
        foreach (Entry entry in evicted)
        {
            Evict(entry);
        }

        try
        {
            return value.Value;
        }
        catch
        {
            Forget(key, value);

            throw;
        }
    }

    /// <summary>Drops every entry, telling <c>onEvict</c> about each.</summary>
    internal void Clear()
    {
        List<Entry> dropped;

        lock (_gate)
        {
            dropped = [.. _order];
            _order.Clear();
            _byKey.Clear();
        }

        foreach (Entry entry in dropped)
        {
            Evict(entry);
        }
    }

    private void Evict(Entry entry)
    {
        if (onEvict is null || !entry.Value.IsValueCreated)
        {
            return;
        }

        onEvict(entry.Key, entry.Value.Value);
    }

    /// <summary>Removes an entry, but only if it is still the one that failed.</summary>
    private void Forget(TKey key, Lazy<TValue> failed)
    {
        lock (_gate)
        {
            if (_byKey.TryGetValue(key, out LinkedListNode<Entry>? found)
                && ReferenceEquals(found.Value.Value, failed))
            {
                _order.Remove(found);
                _byKey.Remove(key);
            }
        }
    }

    private readonly record struct Entry(TKey Key, Lazy<TValue> Value);
}
