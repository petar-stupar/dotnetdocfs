using System.Runtime.InteropServices;

namespace DotnetDocFs.Catalog.Internal.Sources;

/// <summary>
/// Finds the .NET installation on this machine and the reference packs inside it.
/// </summary>
internal static class FrameworkLocator
{
    /// <summary>The pack ids worth serving, in the order their namespaces should win a tie.</summary>
    private static readonly string[] PackIds =
    [
        "Microsoft.NETCore.App.Ref",
        "Microsoft.AspNetCore.App.Ref",
        "Microsoft.WindowsDesktop.App.Ref",
    ];

    /// <summary>
    /// The .NET root: the directory holding <c>packs</c> and <c>shared</c>. Explicit beats the
    /// environment, which beats the host this process is running on, which beats the
    /// per-platform default install location.
    /// </summary>
    internal static string? FindDotnetRoot(string? configured)
    {
        foreach (string? candidate in Candidates(configured))
        {
            if (!string.IsNullOrEmpty(candidate) && Directory.Exists(Path.Combine(candidate, "packs")))
            {
                return candidate;
            }
        }

        return null;

        static IEnumerable<string?> Candidates(string? configured)
        {
            yield return configured;
            yield return Environment.GetEnvironmentVariable("DOTNET_ROOT");
            yield return FromRunningHost();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                yield return "/usr/local/share/dotnet";
                yield return "/opt/homebrew/share/dotnet";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                yield return @"C:\Program Files\dotnet";
            }
            else
            {
                yield return "/usr/share/dotnet";
                yield return "/usr/lib/dotnet";
            }
        }
    }

    /// <summary>
    /// The root inferred from where this process's own runtime was loaded from:
    /// <c>&lt;root&gt;/shared/Microsoft.NETCore.App/&lt;version&gt;</c>. Null when the host is
    /// self-contained or single-file, which have no shared framework to point at.
    /// </summary>
    private static string? FromRunningHost()
    {
        // Empty in a single-file app, and meaningless in a self-contained one: neither runs out of
        // a shared framework, so there is no installation here to infer. Returning null hands the
        // question to the platform defaults below, which is the right answer for a published
        // binary and leaves this useful for the ordinary framework-dependent case.
#pragma warning disable IL3000 // Deliberate: the empty string is handled as "no shared framework".
        string location = typeof(object).Assembly.Location;
#pragma warning restore IL3000

        if (location.Length == 0)
        {
            return null;
        }

        string? runtime = Path.GetDirectoryName(location);
        string? shared = Path.GetDirectoryName(Path.GetDirectoryName(runtime));

        return Path.GetFileName(shared) == "shared" ? Path.GetDirectoryName(shared) : null;
    }

    /// <summary>
    /// The reference pack to serve. With no <paramref name="targetFramework"/> the newest moniker
    /// installed wins; with one, the pack carrying that moniker does.
    /// </summary>
    /// <remarks>
    /// Three pack ids contribute to one moniker and they are separate products, patched on their
    /// own schedules: <c>Microsoft.AspNetCore.App.Ref</c> is routinely a patch ahead of
    /// <c>Microsoft.NETCore.App.Ref</c>. So the newest version is chosen <em>per pack id</em> and
    /// the chosen ones are merged. Picking one version across all three and letting a newer pack
    /// replace what an older one contributed threw away the entire base class library whenever
    /// the numbers differed: no <c>System</c> namespace, no <c>System.Runtime</c> for the reader
    /// to resolve against, and so every page in the tree rendered with no members and no message.
    /// </remarks>
    internal static FrameworkPack? FindPack(string dotnetRoot, string? targetFramework)
    {
        // moniker -> pack id -> the newest version of that pack carrying that moniker
        var byMoniker = new Dictionary<string, Dictionary<string, PackVersion>>(StringComparer.OrdinalIgnoreCase);

        foreach (string packId in PackIds)
        {
            string packRoot = Path.Combine(dotnetRoot, "packs", packId);

            if (!Directory.Exists(packRoot))
            {
                continue;
            }

            foreach (string versionDir in Directory.EnumerateDirectories(packRoot))
            {
                string refDir = Path.Combine(versionDir, "ref");

                if (!Directory.Exists(refDir) || !Version.TryParse(Path.GetFileName(versionDir), out Version? version))
                {
                    continue;
                }

                foreach (string monikerDir in Directory.EnumerateDirectories(refDir))
                {
                    string moniker = Path.GetFileName(monikerDir);

                    if (targetFramework is not null
                        && !moniker.Equals(targetFramework, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!byMoniker.TryGetValue(moniker, out Dictionary<string, PackVersion>? packs))
                    {
                        byMoniker[moniker] = packs = new Dictionary<string, PackVersion>(StringComparer.Ordinal);
                    }

                    if (packs.TryGetValue(packId, out PackVersion? held) && held.Version >= version)
                    {
                        continue;
                    }

                    packs[packId] = new PackVersion(version, Path.GetFileName(versionDir), monikerDir);
                }
            }
        }

        if (byMoniker.Count == 0)
        {
            return null;
        }

        // Newest moniker wins: net10.0 over net8.0. The monikers sort by their version, not by
        // their text, or net10.0 would lose to net8.0.
        KeyValuePair<string, Dictionary<string, PackVersion>> chosen =
            byMoniker.MaxBy(pair => MonikerOrder(pair.Key));

        var assemblies = new List<DocAssembly>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // PackIds order decides a tie: the base pack is visited first, so where two packs ship an
        // assembly of one name the base class library's copy is the one served.
        foreach (string packId in PackIds)
        {
            if (!chosen.Value.TryGetValue(packId, out PackVersion? pack))
            {
                continue;
            }

            foreach (string dll in Directory.EnumerateFiles(pack.Directory, "*.dll"))
            {
                if (seen.Add(Path.GetFileNameWithoutExtension(dll)))
                {
                    assemblies.Add(DocAssembly.Describe(dll));
                }
            }
        }

        // The version reported is the base pack's, which is what "the .NET on this machine"
        // means to a reader; a targeting pack's own patch number would be a different question's
        // answer.
        string reported = chosen.Value.TryGetValue(PackIds[0], out PackVersion? basePack)
            ? basePack.Raw
            : chosen.Value.Values.OrderByDescending(pack => pack.Version).First().Raw;

        return new FrameworkPack(chosen.Key, reported, assemblies);
    }

    /// <summary>One version directory of one pack id, carrying one moniker.</summary>
    private sealed record PackVersion(Version Version, string Raw, string Directory);

    /// <summary>Orders <c>net8.0</c> before <c>net10.0</c>, which a text sort gets backwards.</summary>
    /// <remarks>
    /// The prefix is removed by length and not with <c>TrimStart("net")</c>, which trims the
    /// <em>characters</em> n, e and t in any order and for as long as they last: it is right for
    /// every moniker there is today and wrong for the first one that starts with another of those
    /// letters.
    /// </remarks>
    private static Version MonikerOrder(string moniker)
    {
        if (!moniker.StartsWith("net", StringComparison.Ordinal))
        {
            return new Version(0, 0);
        }

        return Version.TryParse(moniker.AsSpan(3), out Version? version) ? version : new Version(0, 0);
    }
}
