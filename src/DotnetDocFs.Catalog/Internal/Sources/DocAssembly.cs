namespace DotnetDocFs.Catalog.Internal.Sources;

/// <summary>
/// One assembly on this machine, with the XML documentation file that sits beside it when the
/// producer shipped one. Both paths belong to the machine; dotnetdocfs copies neither.
/// </summary>
/// <param name="Path">The assembly.</param>
/// <param name="XmlPath">Its documentation file, or null when it has none.</param>
/// <param name="SimpleName">The assembly's file name without the extension.</param>
internal sealed record DocAssembly(string Path, string? XmlPath, string SimpleName)
{
    /// <summary>
    /// Describes <paramref name="path"/>, pairing it with the <c>.xml</c> beside it when one
    /// exists. Reference packs and NuGet's <c>lib</c> folders both use that convention, which is
    /// why no separate documentation lookup is needed.
    /// </summary>
    internal static DocAssembly Describe(string path)
    {
        string xml = System.IO.Path.ChangeExtension(path, ".xml");

        return new DocAssembly(
            path,
            File.Exists(xml) ? xml : null,
            System.IO.Path.GetFileNameWithoutExtension(path));
    }
}
