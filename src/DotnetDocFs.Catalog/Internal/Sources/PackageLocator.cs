namespace DotnetDocFs.Catalog.Internal.Sources;

/// <summary>
/// Finds the NuGet global package folder and the assemblies inside one package version. This is
/// where a third-party library's documentation already lives on a developer's machine: NuGet
/// unpacks the <c>.xml</c> beside the <c>.dll</c>, so restoring a package is all the ingestion
/// most libraries ever need.
/// </summary>
internal static class PackageLocator
{
    /// <summary>The folders under <c>lib</c> worth preferring, best first.</summary>
    private static readonly string[] Preference =
    [
        "net10.0", "net9.0", "net8.0", "net7.0", "net6.0",
        "netstandard2.1", "netstandard2.0",
    ];

    /// <summary>
    /// The global package folder: explicit, then <c>NUGET_PACKAGES</c>, then the default under the
    /// user's profile. Null when none of them exists — an empty area, not an error.
    /// </summary>
    internal static string? FindRoot(string? configured)
    {
        foreach (string? candidate in Candidates(configured))
        {
            if (!string.IsNullOrEmpty(candidate) && Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;

        static IEnumerable<string?> Candidates(string? configured)
        {
            yield return configured;
            yield return Environment.GetEnvironmentVariable("NUGET_PACKAGES");
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
        }
    }

    /// <summary>
    /// Whether this package version carries any library at all, answered from the directory alone
    /// so that listing a package does not have to open one.
    /// </summary>
    internal static bool HasAssemblies(string versionDirectory)
    {
        string lib = Path.Combine(versionDirectory, "lib");

        return Directory.Exists(lib)
            && Directory.EnumerateFiles(lib, "*.dll", SearchOption.AllDirectories).Any();
    }

    /// <summary>
    /// The assemblies of one package version, taken from the single best target framework folder
    /// it offers. Serving every folder would list the same type five times under monikers that
    /// make no difference to its documentation.
    /// </summary>
    internal static IReadOnlyList<DocAssembly> AssembliesIn(string versionDirectory)
    {
        string lib = Path.Combine(versionDirectory, "lib");

        if (!Directory.Exists(lib))
        {
            return [];
        }

        // The name breaks a tie between two unknown monikers. Ranking them equal left the choice
        // between net462, netcoreapp3.1 and net8.0-windows to directory enumeration order, which
        // differs between filesystems: the same package served a different API on two machines.
        string? best = Directory.EnumerateDirectories(lib)
            .OrderBy(directory => Rank(Path.GetFileName(directory)))
            .ThenByDescending(directory => Path.GetFileName(directory), StringComparer.Ordinal)
            .FirstOrDefault();

        if (best is null)
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(best, "*.dll").Select(DocAssembly.Describe)];
    }

    /// <summary>
    /// How much a target framework folder is wanted, lower being better. An unknown moniker sorts
    /// last but is still usable, which is what keeps an old package from serving nothing at all.
    /// </summary>
    private static int Rank(string moniker)
    {
        int index = Array.FindIndex(Preference, name => name.Equals(moniker, StringComparison.OrdinalIgnoreCase));

        return index < 0 ? Preference.Length : index;
    }
}
