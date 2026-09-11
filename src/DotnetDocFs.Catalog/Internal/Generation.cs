namespace DotnetDocFs.Catalog.Internal;

/// <summary>
/// A counter every materialized directory records when it builds, so that one increment
/// invalidates the whole tree at once.
/// </summary>
/// <remarks>
/// <para>
/// The alternative — walking the tree and clearing each directory — reaches only the directories
/// the walk can find, and those are not the ones that matter. A 9P client holds a fid per
/// directory it has walked into, and a kernel mount with <c>cache=loose</c> holds them for as long
/// as its dentry cache does. Each fid is a handler holding the <see cref="DocDirectory"/> object
/// it was walked to, so invalidating from the root left every client that had already looked at
/// <c>/docs/packages</c> looking at the old entries: <c>echo refresh &gt; ctl</c> did nothing a
/// mounted reader could see, which is the one operational promise the README makes about it.
/// </para>
/// <para>
/// A counter reaches them because the object checks it rather than being told. Node keys do not
/// change across a generation, so a client that re-walks the same path gets the same qid.
/// </para>
/// </remarks>
internal sealed class Generation
{
    private int _current;

    /// <summary>The generation directories should be built under now.</summary>
    internal int Current => Volatile.Read(ref _current);

    /// <summary>Moves to the next generation, making every built directory stale.</summary>
    internal void Advance() => Interlocked.Increment(ref _current);
}
