using System.Buffers;
using System.Text;
using System.Xml.Linq;
using DotnetDocFs.Catalog.Internal.Naming;

namespace DotnetDocFs.Catalog.Internal.Render;

/// <summary>
/// Renders the elements of a documentation comment as markdown.
/// </summary>
/// <remarks>
/// The link rules are the reason this exists rather than a pass-through:
/// <list type="bullet">
/// <item>a <c>cref</c> that names something in this tree becomes a relative markdown link, so an
/// agent follows it with an ordinary file read;</item>
/// <item>a <c>cref</c> that names anything else becomes inline code, never a link, because a link
/// that resolves to nothing costs a reader a wasted read to discover;</item>
/// <item>an <c>href</c> that points out of this tree is left exactly as written — a link to the
/// web is the author's, and rewriting it would be a lie — while one that is merely relative
/// resolves to nothing here and is named rather than linked, like an unresolvable cref.</item>
/// </list>
/// </remarks>
internal sealed class DocMarkdown(ILinkResolver links, TreePath page, bool plain = false)
{
    /// <summary>The characters that would end a markdown link before its destination does.</summary>
    private static readonly SearchValues<char> BreaksALink = SearchValues.Create("()  \t\n".AsSpan());

    /// <summary>
    /// The same text with no markup at all, for a YAML frontmatter field. A description is
    /// metadata: a relative link inside it resolves against nothing and a backtick is noise.
    /// </summary>
    internal string Plain(XElement? element) => new DocMarkdown(links, page, plain: true).Inline(element);

    /// <summary>The text of <paramref name="element"/> as a markdown block.</summary>
    internal string Block(XElement? element)
    {
        if (element is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        Write(element, builder);

        return Tidy(builder.ToString());
    }

    /// <summary>
    /// The text of <paramref name="element"/> as a single line, for a table cell or a
    /// description field. Newlines would break both.
    /// </summary>
    internal string Inline(XElement? element)
    {
        string block = Block(element);

        // A fenced code block cannot survive being folded onto one line: the fence would end up
        // mid-sentence and everything after it would be read as code. The lines between the
        // fences are kept and the fences dropped, so the sample is still there as prose.
        string[] lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join(' ', lines.Where(line => !line.StartsWith("```", StringComparison.Ordinal)));
    }

    private void Write(XNode node, StringBuilder builder)
    {
        switch (node)
        {
            case XText text:
                AppendText(builder, Collapse(text.Value));
                break;

            case XElement element:
                WriteElement(element, builder);
                break;

            default:
                break;
        }
    }

    private void WriteElement(XElement element, StringBuilder builder)
    {
        switch (element.Name.LocalName)
        {
            case "see" or "seealso":
                WriteReference(element, builder);
                break;

            case "paramref" or "typeparamref":
                Code(builder, element.Attribute("name")?.Value ?? string.Empty);
                break;

            case "c":
                Code(builder, Collapse(element.Value));
                break;

            case "code":
                WriteCodeBlock(element, builder);
                break;

            case "para":
                builder.Append("\n\n");
                WriteChildren(element, builder);
                builder.Append("\n\n");
                break;

            case "list":
                WriteList(element, builder);
                break;

            case "br":
                builder.Append('\n');
                break;

            default:
                WriteChildren(element, builder);
                break;
        }
    }

    private void WriteChildren(XElement element, StringBuilder builder)
    {
        foreach (XNode child in element.Nodes())
        {
            Write(child, builder);
        }
    }

    /// <summary>
    /// A <c>see</c> or <c>seealso</c>, in the three forms they take: a cref into the API, an href
    /// out to the web, and a langword naming a C# keyword.
    /// </summary>
    private void WriteReference(XElement element, StringBuilder builder)
    {
        if (element.Attribute("href")?.Value is { Length: > 0 } href)
        {
            string text = Collapse(element.Value);

            if (!IsLinkOut(href))
            {
                // Not a link out to anywhere: it is a relative target, and relative to this tree
                // it resolves to nothing. Every one in the .NET 10 reference packs is an authoring
                // slip — `<see href="P:ModelBindingContext.Result"/>` where cref was meant — and
                // writing it as a link spends a reader a whole file read to find that out. The
                // rule is the cref rule: what cannot be resolved is named, not linked.
                Code(builder, text.Length == 0 ? href : text);
                return;
            }

            // The destination is left as the author wrote it; only the characters that would end
            // the link early are percent-encoded, which is the same URL.
            builder.Append('[').Append(text.Length == 0 ? href : text).Append("](").Append(Href(href)).Append(')');
            return;
        }

        if (element.Attribute("langword")?.Value is { Length: > 0 } langword)
        {
            Code(builder, langword);
            return;
        }

        string? cref = element.Attribute("cref")?.Value;

        if (cref is null)
        {
            WriteChildren(element, builder);
            return;
        }

        string label = Collapse(element.Value).Trim();

        CrefTarget? target = Cref.Resolve(cref, links);

        if (target is null)
        {
            // Nothing in this tree carries it, so it is named rather than linked.
            Code(builder, label.Length == 0 ? ShortName(cref) : label);
            return;
        }

        string caption = label.Length == 0 ? target.Label : label;

        if (plain)
        {
            builder.Append(caption);
            return;
        }

        builder.Append('[').Append(caption).Append("](").Append(target.Path.RelativeToPageIn(page)).Append(')');
    }

    private static void WriteCodeBlock(XElement element, StringBuilder builder)
    {
        string language = element.Attribute("language")?.Value ?? "csharp";

        builder.Append("\n\n```").Append(language).Append('\n')
            .Append(Dedent(element.Value).TrimEnd())
            .Append("\n```\n\n");
    }

    private void WriteList(XElement element, StringBuilder builder)
    {
        string kind = element.Attribute("type")?.Value ?? "bullet";
        int number = 1;

        builder.Append('\n');

        foreach (XElement item in element.Elements("item"))
        {
            XElement? description = item.Element("description");

            var text = new StringBuilder();

            if (item.Element("term") is { } term)
            {
                var heading = new StringBuilder();
                WriteChildren(term, heading);

                text.Append("**").Append(heading.ToString().Trim()).Append("** — ");
            }

            WriteChildren(description ?? item, text);

            // A list item is one line, so a fenced block inside one cannot stay fenced: the
            // closing fence would land mid-sentence and everything after it would read as code.
            // The sample is kept as inline code instead of being thrown away.
            builder.Append(kind == "number" ? $"{number++}. " : "- ")
                .Append(Collapse(Unfence(text.ToString())))
                .Append('\n');
        }

        builder.Append('\n');
    }

    /// <summary>
    /// Whether an <c>href</c> points somewhere a reader can actually go: an absolute URL, a
    /// protocol-relative one, or an anchor within the same page.
    /// </summary>
    private static bool IsLinkOut(string href) =>
        href.StartsWith("//", StringComparison.Ordinal)
        || href.StartsWith('#')
        || Uri.TryCreate(href, UriKind.Absolute, out _);

    /// <summary>
    /// The destination of a link, with only the characters markdown would read as the end of one
    /// escaped. Nothing else is touched: an href points outside this tree and rewriting it would
    /// be a lie about where the author sent the reader.
    /// </summary>
    private static string Href(string href)
    {
        if (!href.AsSpan().ContainsAny(BreaksALink))
        {
            return href;
        }

        var builder = new StringBuilder(href.Length + 6);

        foreach (char c in href)
        {
            builder.Append(c switch
            {
                '(' => "%28",
                ')' => "%29",
                ' ' => "%20",
                '\t' => "%09",
                '\n' => "%0A",
                _ => c.ToString(),
            });
        }

        return builder.ToString();
    }

    /// <summary>
    /// The same text with any fenced code block turned into inline code spans, for a place that
    /// holds one line.
    /// </summary>
    private string Unfence(string text)
    {
        if (!text.Contains("```", StringComparison.Ordinal))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        bool inside = false;

        foreach (string line in text.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inside = !inside;
                continue;
            }

            if (inside && line.Trim().Length > 0)
            {
                Code(builder, line.Trim());
                builder.Append(' ');
            }
            else
            {
                builder.Append(line).Append(' ');
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Inline code, or bare text where markup is not wanted. A span is fenced with one more
    /// backtick than the longest run inside it, which is how markdown quotes a backtick at all.
    /// </summary>
    private void Code(StringBuilder builder, string text)
    {
        if (plain)
        {
            builder.Append(text);
            return;
        }

        int longest = 0;
        int run = 0;

        foreach (char c in text)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        if (longest == 0)
        {
            builder.Append('`').Append(text).Append('`');
            return;
        }

        string fence = new('`', longest + 1);

        // A span whose text starts or ends with a backtick needs a space inside the fence, which
        // the reader strips again.
        builder.Append(fence)
            .Append(text.StartsWith('`') ? " " : string.Empty)
            .Append(text)
            .Append(text.EndsWith('`') ? " " : string.Empty)
            .Append(fence);
    }

    /// <summary>The last segment of a documentation id, for a reference that carried no text.</summary>
    private static string ShortName(string cref)
    {
        string name = cref.Length > 2 && cref[1] == ':' ? cref[2..] : cref;

        int parenthesis = name.IndexOf('(', StringComparison.Ordinal);

        if (parenthesis >= 0)
        {
            name = name[..parenthesis];
        }

        return name.Replace('#', '.');
    }

    /// <summary>
    /// Documentation comments are indented by however deep the source was. Every line of a code
    /// block loses the common indent so the fence holds code rather than a quote.
    /// </summary>
    private static string Dedent(string text)
    {
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        int common = lines
            .Where(line => line.Trim().Length > 0)
            .Select(line => line.Length - line.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        return string.Join('\n', lines.Select(line => line.Length >= common ? line[common..] : line.TrimStart()))
            .Trim('\n');
    }

    /// <summary>
    /// Runs of whitespace in the source become single spaces, as in any markup. The space at
    /// either edge is kept: a documentation comment separates its prose from a <c>see</c> with
    /// exactly that whitespace, and dropping it runs the link into the next word.
    /// </summary>
    private static string Collapse(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool space = false;

        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
            }

            space = false;
            builder.Append(c);
        }

        if (space)
        {
            builder.Append(' ');
        }

        return builder.ToString();
    }

    /// <summary>Appends collapsed text without doubling a space or starting a line with one.</summary>
    private static void AppendText(StringBuilder builder, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        bool atEdge = builder.Length == 0 || builder[^1] is ' ' or '\n';

        builder.Append(atEdge && text[0] == ' ' ? text.AsSpan(1) : text.AsSpan());
    }

    /// <summary>Collapses the blank lines the element handlers leave behind.</summary>
    private static string Tidy(string text)
    {
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var kept = new List<string>(lines.Length);
        bool blank = false;

        foreach (string line in lines)
        {
            string trimmed = line.TrimEnd();

            if (trimmed.Length == 0)
            {
                blank = kept.Count > 0;
                continue;
            }

            if (blank)
            {
                kept.Add(string.Empty);
            }

            blank = false;
            kept.Add(trimmed);
        }

        return string.Join('\n', kept).Trim();
    }
}
