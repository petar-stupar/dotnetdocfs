using System.Text;
using DotnetDocFs.Catalog;
using NineP.Protocol;
using NineP.Server;

namespace DotnetDocFs.Internal.Server;

/// <summary>
/// One open of <c>/ctl</c>. Bytes written are accumulated and every complete line is run as a
/// command as soon as it arrives, so a command that fails fails the write that completed it;
/// waiting for close to report would put the reason somewhere a shell never prints. Reading
/// returns the command list.
/// </summary>
internal sealed class ControlChannel(DocControl control, byte[] help) : IOpenFile
{
    /// <summary>A command longer than this is refused rather than buffered without end.</summary>
    private const int MaxPending = 8 * 1024;

    private readonly StringBuilder _pending = new();

    /// <inheritdoc />
    public ValueTask<int> ReadAsync(
        ulong offset,
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (offset >= (ulong)help.Length)
        {
            return ValueTask.FromResult(0);
        }

        int at = (int)offset;
        int count = Math.Min(buffer.Length, help.Length - at);

        help.AsMemory(at, count).CopyTo(buffer);

        return ValueTask.FromResult(count);
    }

    /// <inheritdoc />
    public async ValueTask<int> WriteAsync(
        ulong offset,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        // A control file is a command channel, not a byte range: offsets mean nothing and are
        // ignored rather than honoured, which is the Plan 9 convention.
        _pending.Append(Encoding.UTF8.GetString(data.Span));

        if (_pending.Length > MaxPending)
        {
            _pending.Clear();

            throw new NinePException(new NinePError($"command longer than {MaxPending} bytes", Errno.EFBIG));
        }

        while (Take() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                await control.ExecuteAsync(line, cancellationToken).ConfigureAwait(false);
            }
            catch (CatalogException exception)
            {
                // Both halves travel: 9P2000 and .u carry the sentence, .L carries the number.
                throw new NinePException(new NinePError(exception.Message, exception.Errno));
            }
        }

        return data.Length;
    }

    /// <summary>The next complete line, or null while none has arrived.</summary>
    private string? Take()
    {
        for (int at = 0; at < _pending.Length; at++)
        {
            if (_pending[at] != '\n')
            {
                continue;
            }

            string line = _pending.ToString(0, at).Trim();
            _pending.Remove(0, at + 1);

            return line;
        }

        return null;
    }

    /// <inheritdoc />
    public ValueTask<ulong> GetSizeAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult((ulong)help.Length);

    /// <summary>
    /// Runs a last command that arrived without a trailing newline, which is what
    /// <c>printf 'refresh' &gt; ctl</c> leaves behind.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        string last = _pending.ToString().Trim();
        _pending.Clear();

        if (last.Length > 0)
        {
            try
            {
                await control.ExecuteAsync(last, CancellationToken.None).ConfigureAwait(false);
            }
            catch (CatalogException exception)
            {
                // The write that carried it was answered before this line was known to be
                // complete, so there is nowhere left to report it to the client: 9P has no error
                // on a clunk. It goes to the server's own output instead of nowhere at all, which
                // is why `printf 'refresh' > ctl` is worth avoiding — `echo` sends the newline
                // that makes a command fail its own write.
                Console.Error.WriteLine(
                    $"dotnetdoc: the trailing '{last}' written to /ctl: {exception.Message}");
            }
        }
    }
}
