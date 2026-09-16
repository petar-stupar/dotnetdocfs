using System.Net.Sockets;
using System.Runtime.InteropServices;
using DotnetDocFs.Catalog;
using DotnetDocFs.Internal.Mount;
using DotnetDocFs.Internal.Server;
using NineP.Protocol;
using NineP.Protocol.Transports;
using NineP.Server;

namespace DotnetDocFs;

/// <summary>The <c>dotnetdoc</c> command.</summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAsync(args).ConfigureAwait(false);
        }
        catch (CliUsageException exception)
        {
            await Console.Error.WriteLineAsync("dotnetdoc: " + exception.Message).ConfigureAwait(false);
            await Console.Error.WriteLineAsync(CliOptions.Usage).ConfigureAwait(false);

            return 2;
        }
        catch (Exception exception) when (exception is MountException or CatalogException)
        {
            await Console.Error.WriteLineAsync("dotnetdoc: " + exception.Message).ConfigureAwait(false);

            return 1;
        }
        // SocketException is not an IOException, so the catch below still gets it.
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Building the catalog walks the reference packs and the NuGet cache, neither of
            // which this program owns: one unreadable directory is a sentence, not a stack trace.
            await Console.Error.WriteLineAsync("dotnetdoc: " + exception.Message).ConfigureAwait(false);

            return 1;
        }
        catch (SocketException exception)
        {
            // A port already taken is the ordinary way this fails, and a stack trace is the wrong
            // way to say so.
            await Console.Error.WriteLineAsync("dotnetdoc: cannot listen: " + exception.Message)
                .ConfigureAwait(false);

            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        // Everything the catalog recovers from goes here. Without it a type whose members could
        // not be described renders as a type with no members, which is what a type with no
        // members also looks like.
        Diagnostics.To(Console.Error.WriteLine);

        CliOptions options = CliOptions.Parse(args);

        if (options.Help)
        {
            Console.WriteLine(CliOptions.Usage);

            return 0;
        }

        // Before Validated(), as --help is: asking what this binary is cannot be refused for a
        // combination of flags that has nothing to do with the answer.
        if (options.Version)
        {
            Console.WriteLine(Versions.Report);

            return 0;
        }

        options = options.Validated();

        if (options.Unmount)
        {
            await Mounter.UnmountAsync(options.MountSettings).ConfigureAwait(false);

            return 0;
        }

        if (options.RestartContainer)
        {
            Console.WriteLine("taking the existing bridge down");
            await Mounter.UnmountAsync(options.MountSettings).ConfigureAwait(false);
        }

        return await ServeAsync(options).ConfigureAwait(false);
    }

    private static async Task<int> ServeAsync(CliOptions options)
    {
        bool mounting = options.Mount || options.RestartContainer;
        MountSettings mount = options.MountSettings;

        // Loopback by default, including for the container bridge: Docker Desktop forwards
        // host.docker.internal to the host's loopback, so the bridge reaches a server bound here
        // and nothing off this machine does. --listen can widen it, and CliOptions.Validated has
        // already refused the combinations where that would be a surprise rather than a choice.
        string listen = options.ListenAddress;

        bool allowIngest = options.AllowIngest;

        using DocCatalog catalog = DocCatalog.Create(new CatalogOptions
        {
            TargetFramework = options.Framework,
            DotnetRoot = options.DotnetRoot,
            PackageRoot = options.PackageRoot,
            AllowIngest = allowIngest,
        });

        if (catalog.TargetFramework is null)
        {
            await Console.Error.WriteLineAsync(
                "dotnetdoc: no .NET reference pack found; /docs/framework will be absent")
                .ConfigureAwait(false);
        }

        var trace = options.LogRequests ? new RequestTrace() : null;

        await using var server = new NinePServer(new ServerOptions
        {
            Listen = [NinePAddress.Parse(listen)],
            RequestLog = trace,
            Logger = new StandardErrorLogger(),
        });

        Task serving = server.ServeAsync(new DocTree(catalog));
        await server.ListeningAsync().ConfigureAwait(false);

        foreach (NinePAddress address in server.Endpoints)
        {
            Console.WriteLine($"listening {address} framework={catalog.TargetFramework ?? "none"} ctl={(allowIngest ? "rw" : "off")}");
        }

        // The signals are hooked before anything is mounted, not after. Mounting can run for
        // minutes — a five-minute image build, thirty polls waiting for Samba, a sudo prompt — and
        // a Ctrl-C in that window used to take the default action and kill the process outright,
        // leaving the container running and a half-made mount attached with nothing behind it.
        // That is the exact outcome the signal handling was added to prevent.
        using var shutdown = new Shutdown();

        if (mounting)
        {
            try
            {
                await Mounter.MountAsync(mount, shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await Console.Error.WriteLineAsync("dotnetdoc: interrupted while mounting")
                    .ConfigureAwait(false);
            }
        }

        // Whether the mount finished, failed or was interrupted, the same cleanup runs: it asks
        // the mount table what is actually there and is quiet when the answer is nothing.
        bool faulted = !shutdown.Token.IsCancellationRequested
            && await Task.WhenAny(serving, shutdown.Requested).ConfigureAwait(false) == serving;

        if (mounting)
        {
            await Mounter.UnmountAsync(mount, CancellationToken.None).ConfigureAwait(false);
        }

        if (trace is not null)
        {
            await Console.Error.WriteLineAsync(trace.Report()).ConfigureAwait(false);
        }

        await server.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // Awaiting the serve task rethrows whatever stopped it, which is the only place the
        // reason exists.
        await serving.ConfigureAwait(false);

        if (faulted)
        {
            await Console.Error.WriteLineAsync("dotnetdoc: the server stopped on its own")
                .ConfigureAwait(false);
        }

        return faulted ? 1 : 0;
    }

    /// <summary>
    /// The signals that mean stop, hooked for the life of the run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SIGTERM</c> is the one that matters and was missing. Hooking only
    /// <c>Console.CancelKeyPress</c> catches Ctrl-C and nothing else, so an ordinary <c>kill</c>,
    /// a logout, or a supervisor stopping this process left the mount attached and the bridge
    /// container running, with no server behind them.
    /// </para>
    /// <para>
    /// <c>SIGKILL</c> cannot be caught by anything, so an orphaned mount is still possible.
    /// <c>--unmount</c> exists to clear one up and is safe to run when nothing is mounted.
    /// </para>
    /// </remarks>
    private sealed class Shutdown : IDisposable
    {
        private readonly CancellationTokenSource _stopping = new();
        private readonly List<PosixSignalRegistration> _signals;

        internal Shutdown()
        {
            _signals =
            [
                PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop),
                PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop),
            ];

            if (!OperatingSystem.IsWindows())
            {
                _signals.Add(PosixSignalRegistration.Create(PosixSignal.SIGHUP, Stop));
            }
        }

        /// <summary>Cancelled when a stop signal arrives.</summary>
        internal CancellationToken Token => _stopping.Token;

        /// <summary>Completes when a stop signal arrives.</summary>
        internal Task Requested => Task.Delay(Timeout.InfiniteTimeSpan, _stopping.Token)
            .ContinueWith(static _ => { }, TaskScheduler.Default);

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (PosixSignalRegistration signal in _signals)
            {
                signal.Dispose();
            }

            _stopping.Dispose();
        }

        private void Stop(PosixSignalContext context)
        {
            // Take responsibility for the signal: without this the runtime terminates the process
            // where it stands and the cleanup never runs.
            context.Cancel = true;
            _stopping.Cancel();
        }
    }
}
