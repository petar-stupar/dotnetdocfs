using System.Xml;
using System.Xml.Linq;

namespace DotnetDocFs.Catalog.Internal.Xml;

/// <summary>
/// One compiler-produced XML documentation file, indexed by documentation id. Loading is the
/// expensive half of rendering a page — <c>System.Runtime.xml</c> alone is over seven megabytes —
/// which is the reason nothing here happens until a page is actually read.
/// </summary>
internal sealed class XmlDocFile
{
    private readonly Dictionary<string, XElement> _members;

    private XmlDocFile(Dictionary<string, XElement> members) => _members = members;

    /// <summary>A file that documents nothing, for an assembly that shipped without one.</summary>
    internal static XmlDocFile Empty { get; } = new([]);

    /// <summary>
    /// Reads <paramref name="path"/>. A file that is malformed or unreadable yields
    /// <see cref="Empty"/> rather than throwing: a broken documentation file beside an assembly
    /// should cost that assembly its prose, not take the whole tree down.
    /// </summary>
    internal static XmlDocFile Load(string path)
    {
        try
        {
            // DtdProcessing.Prohibit is the point of going through XmlReader rather than
            // XDocument.Load: these files come from the machine, but an assembly handed to /ctl
            // can bring any .xml the caller likes beside it, and an external entity must never be
            // fetched or expanded.
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = false,

                // The largest documentation file .NET ships is about seven megabytes; this is two
                // orders of magnitude over that. An assembly handed to /ctl brings whatever .xml
                // its caller put beside it, and a file with no ceiling is parsed into memory in
                // full before anything here looks at it.
                MaxCharactersInDocument = 512L * 1024 * 1024,
            };

            using XmlReader reader = XmlReader.Create(path, settings);
            XDocument document = XDocument.Load(reader, LoadOptions.None);

            var members = new Dictionary<string, XElement>(StringComparer.Ordinal);

            foreach (XElement member in document.Root?.Element("members")?.Elements("member") ?? [])
            {
                string? name = member.Attribute("name")?.Value;

                if (!string.IsNullOrEmpty(name))
                {
                    members[name] = member;
                }
            }

            return new XmlDocFile(members);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or XmlException
            or NotSupportedException)
        {
            return Empty;
        }
    }

    /// <summary>The documentation for <paramref name="docId"/>, or null when it has none.</summary>
    internal XElement? Find(string docId) => _members.GetValueOrDefault(docId);
}
