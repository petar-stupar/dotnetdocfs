using System.Net;
using DotnetDocFs.Internal.Mount;

namespace DotnetDocFs;

/// <summary>The command line, parsed.</summary>
internal sealed record CliOptions
{
    /// <summary>The 9P address to listen on.</summary>
    internal string Listen { get; init; } = "";

    /// <summary>Where the tree should appear on this machine.</summary>
    internal string MountPath { get; init; } = MountSettings.DefaultMountPath;

    /// <summary>Whether to mount after starting the server.</summary>
    internal bool Mount { get; init; }

    /// <summary>Whether the container bridge was asked for explicitly.</summary>
    internal bool Docker { get; init; }

    /// <summary>Unmount and stop the bridge, without starting a server.</summary>
    internal bool Unmount { get; init; }

    /// <summary>Recreate the bridge container and mount again.</summary>
    internal bool RestartContainer { get; init; }

    /// <summary>The moniker to serve, or null for the newest installed.</summary>
    internal string? Framework { get; init; }

    /// <summary>An explicit .NET root.</summary>
    internal string? DotnetRoot { get; init; }

    /// <summary>An explicit NuGet global package folder.</summary>
    internal string? PackageRoot { get; init; }

    /// <summary>Whether <c>/ctl</c> accepts writes.</summary>
    internal bool AllowIngest { get; init; } = true;

    /// <summary>The port the 9P server listens on.</summary>
    internal int NinePPort { get; init; } = 15640;

    /// <summary>The host port the bridge publishes its share on.</summary>
    internal int SmbPort { get; init; } = 14450;

    /// <summary>Report each kind of request the first time it arrives, and each refusal.</summary>
    internal bool LogRequests { get; init; }

    /// <summary>Print usage and stop.</summary>
    internal bool Help { get; init; }

    /// <summary>How this run should mount, given what was asked for and what the machine is.</summary>
    /// <remarks>
    /// The path is made absolute here and nowhere else. The mount table records absolute paths, so
    /// a relative <c>--path</c> would mount correctly and then fail its own ownership check on the
    /// way back out: <c>--unmount --path ./x</c> would refuse to detach the mount it had just made.
    /// </remarks>
    internal MountSettings MountSettings => new(
        Path.GetFullPath(MountPath),
        MountSettings.StrategyFor(Docker),
        ListenPort ?? NinePPort,
        SmbPort);

    /// <summary>The address the server binds: <c>--listen</c> if given, otherwise loopback.</summary>
    internal string ListenAddress => Listen.Length > 0 ? Listen : $"tcp://127.0.0.1:{NinePPort}";

    /// <summary>
    /// Checks the combinations that would otherwise fail somewhere far from their cause, and
    /// returns the options unchanged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>--listen</c> and <c>--port</c> describe the same thing and can disagree. A mount is
    /// told a port, so <c>--listen tcp://127.0.0.1:9999 --mount</c> served on 9999 and mounted
    /// 15640: at best a mount that fails, at worst one that silently attaches to a different
    /// <c>dotnetdoc</c> still running on the default port. The listen address wins, because it is
    /// the more specific of the two, and saying so is better than either of those outcomes.
    /// </para>
    /// <para>
    /// Binding off loopback is the other one. This tree has no authentication — 9P offers one and
    /// nothing here uses it — so whoever can open the socket gets the root, and with <c>/ctl</c>
    /// open, a way to make this process read files by name. On loopback that is the machine's own
    /// users; on any other address it is the network.
    /// </para>
    /// </remarks>
    internal CliOptions Validated()
    {
        if (Listen.Length == 0)
        {
            return this;
        }

        if (!Uri.TryCreate(Listen, UriKind.Absolute, out Uri? address))
        {
            throw new CliUsageException($"--listen: '{Listen}' is not an address");
        }

        bool loopback = address.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
            && IPAddress.TryParse(address.Host, out IPAddress? ip)
            && IPAddress.IsLoopback(ip);

        loopback = loopback || address.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);

        // A unix socket has no host to be loopback or not: it is a path on this machine, reachable
        // only by something already on it, which is more local than loopback rather than less.
        bool unix = address.Scheme.Equals("unix", StringComparison.OrdinalIgnoreCase);

        if (!loopback && !unix && AllowIngest)
        {
            throw new CliUsageException(
                $"--listen {Listen} would serve this machine's documentation, and a control file "
                + "that makes it read other files, to anything that can reach that address. "
                + "Add --no-ctl to serve a read-only tree there, or bind loopback.");
        }

        if (Mount || RestartContainer)
        {
            if (!address.Scheme.Equals("tcp", StringComparison.OrdinalIgnoreCase))
            {
                throw new CliUsageException(
                    $"--listen {Listen} cannot be mounted from here: the mount is made with "
                    + "trans=tcp, so the server has to be listening on TCP.");
            }

            if (!loopback)
            {
                throw new CliUsageException(
                    $"--listen {Listen} cannot be mounted from here: both the direct 9P mount and "
                    + "the bridge container reach this server on 127.0.0.1.");
            }
        }

        return this;
    }

    /// <summary>The TCP port of <c>--listen</c>, or null when it names no TCP port.</summary>
    private int? ListenPort =>
        Listen.Length > 0
        && Uri.TryCreate(Listen, UriKind.Absolute, out Uri? address)
        && address.Port > 0
            ? address.Port
            : null;

    /// <summary>Parses <paramref name="args"/>, or throws on a spelling it does not know.</summary>
    internal static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();

        for (int at = 0; at < args.Length; at++)
        {
            string argument = args[at];
            string name = argument;
            string? inline = null;

            int equals = argument.IndexOf('=', StringComparison.Ordinal);

            if (argument.StartsWith("--", StringComparison.Ordinal) && equals > 0)
            {
                name = argument[..equals];
                inline = argument[(equals + 1)..];
            }

            string Value()
            {
                // "--path=" parses as an inline value that happens to be empty, and an empty path
                // throws out of Path.GetFullPath long after anything is left to say about it.
                if (inline is not null)
                {
                    return inline.Length > 0 ? inline : throw new CliUsageException($"{name} needs a value");
                }

                if (at + 1 >= args.Length)
                {
                    throw new CliUsageException($"{name} needs a value");
                }

                return args[++at].Length > 0
                    ? args[at]
                    : throw new CliUsageException($"{name} needs a value");
            }

            int Number()
            {
                string text = Value();

                return int.TryParse(text, out int port) && port is > 0 and < 65536
                    ? port
                    : throw new CliUsageException($"{name}: '{text}' is not a port");
            }

            options = name switch
            {
                "--help" or "-h" => options with { Help = true },
                "--listen" => options with { Listen = Value() },
                "--mount" => options with { Mount = true },
                "--mount-docker" => options with { Mount = true, Docker = true },
                "--unmount" => options with { Unmount = true },
                "--restart-docker-container" => options with { RestartContainer = true, Docker = true },
                "--path" => options with { MountPath = Value() },
                "--framework" => options with { Framework = Value() },
                "--dotnet-root" => options with { DotnetRoot = Value() },
                "--package-root" => options with { PackageRoot = Value() },
                "--port" => options with { NinePPort = Number() },
                "--smb-port" => options with { SmbPort = Number() },
                "--log-requests" => options with { LogRequests = true },
                "--no-ctl" => options with { AllowIngest = false },
                _ => throw new CliUsageException($"unknown option '{argument}'"),
            };
        }

        return options;
    }

    /// <summary>How to use the command.</summary>
    internal const string Usage = """
        usage: dotnetdoc [--listen <url>] [--framework <tfm>] [--port <n>]
                         [--mount | --mount-docker] [--path <dir>] [--smb-port <n>]
                         [--unmount] [--restart-docker-container] [--no-ctl]

          (no flags)                  serve the tree over 9P and print the address
          --mount                     serve, then mount it; Linux mounts 9P directly and
                                      macOS goes through a container that re-exports SMB
          --mount-docker              serve, then mount through the container everywhere
          --path <dir>                where to mount; ~/mnt/dotnetdocfs by default
          --unmount                   unmount and remove the bridge, without serving
          --restart-docker-container  recreate the bridge container and mount again
          --listen <url>              9P address; tcp://127.0.0.1:<port> by default. An
                                      address off loopback serves the tree to the network
                                      and is refused unless --no-ctl is given too; it
                                      cannot be mounted from here either
          --port <n>                  the 9P port, 15640 by default
          --smb-port <n>              the host port the bridge publishes, 14450 by default
          --framework <tfm>           the moniker to serve, newest installed by default
          --dotnet-root <dir>         the .NET installation to read
          --package-root <dir>        the NuGet global package folder to read
          --no-ctl                    refuse writes to /ctl, serving a wholly read-only
                                      tree; /ctl is open by default
          --log-requests              report each kind of 9P request the first time it
                                      arrives, and every kind of refusal
          --help, -h                  print this and stop

        environment, read only when the matching flag is absent:

          DOTNET_ROOT                 the .NET installation whose reference packs are served
          NUGET_PACKAGES              the NuGet package folder served under docs/packages
        """;
}
