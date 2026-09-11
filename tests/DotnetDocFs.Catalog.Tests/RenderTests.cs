using System.Reflection;
using System.Xml.Linq;
using DotnetDocFs.Catalog.Internal.Metadata;
using DotnetDocFs.Catalog.Internal.Naming;
using DotnetDocFs.Catalog.Internal.Render;
using Xunit;

namespace DotnetDocFs.Catalog.Tests;

/// <summary>
/// The documentation id is the key the compiler wrote, and a page whose id is wrong finds no
/// prose and reads as an undocumented member rather than as a miss. These compare what this
/// writes against what the compiler actually put in the XML file.
/// </summary>
public class DocIdWriterTests
{
    [Fact]
    public void AGenericTypeNestedInAGenericTypeGetsItsArgumentsPerSegment()
    {
        MethodInfo method = typeof(Dictionary<,>)
            .GetMethods()
            .First(candidate => candidate.Name == "TryGetAlternateLookup");

        // The compiler writes Dictionary{`0,`1}.AlternateLookup{``0}: each segment carries the
        // parameters it introduced. Stripping arity once, at the last backtick, leaves the outer
        // arity behind and gathers every argument into one group at the end.
        Assert.Equal(
            "M:System.Collections.Generic.Dictionary`2.TryGetAlternateLookup``1"
                + "(System.Collections.Generic.Dictionary{`0,`1}.AlternateLookup{``0}@)",
            DocIdWriter.For(method));
    }

    [Fact]
    public void AConstructorIsWrittenAsTheCompilerSpellsIt()
    {
        ConstructorInfo constructor = typeof(string).GetConstructor([typeof(char[])])!;

        Assert.Equal("M:System.String.#ctor(System.Char[])", DocIdWriter.For(constructor));
    }

    [Fact]
    public void AConstructedGenericParameterKeepsItsArgumentsInBraces()
    {
        MethodInfo method = typeof(List<>).GetMethod("AddRange")!;

        Assert.Equal(
            "M:System.Collections.Generic.List`1.AddRange(System.Collections.Generic.IEnumerable{`0})",
            DocIdWriter.For(method));
    }
}

/// <summary>
/// The frontmatter is the part of a page a consumer parses rather than reads, so a value that
/// YAML reads as something other than text is worse than a clumsy one.
/// </summary>
public class FrontmatterTests
{
    [Theory]
    [InlineData("true")]
    [InlineData("no")]
    [InlineData("1.0")]
    [InlineData("# not a comment")]
    [InlineData("value: with a colon")]
    public void AValueYamlWouldNotReadAsTextIsQuoted(string value)
    {
        string block = new Frontmatter("reference").Add("title", value).ToString();

        Assert.Contains($"title: \"{value}\"", block, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("JsonSerializer")]
    [InlineData("System.Text.Json")]
    [InlineData("Dictionary{`0,`1}")]
    public void AnOrdinaryNameIsLeftAlone(string value)
    {
        string block = new Frontmatter("reference").Add("title", value).ToString();

        Assert.Contains($"title: {value}\n", block, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTypeFieldOkfRequiresIsAlwaysFirst() =>
        Assert.StartsWith("---\ntype: reference\n", new Frontmatter("reference").ToString(), StringComparison.Ordinal);
}

/// <summary>
/// What the renderer turns a <c>see</c> into. The rule it exists for is that a link in this tree
/// always lands somewhere, so anything that cannot be resolved is named instead of linked.
/// </summary>
public class DocMarkdownTests
{
    private static string Render(string xml) =>
        new DocMarkdown(new NothingResolves(), TreePath.Root.Add("docs", "index.md"))
            .Block(XElement.Parse("<summary>" + xml + "</summary>"));

    [Theory]
    [InlineData("https://learn.microsoft.com/dotnet")]
    [InlineData("http://example.com/a(b)c")]
    [InlineData("//example.com/protocol-relative")]
    [InlineData("#same-page")]
    public void AnHrefThatPointsOutOfTheTreeIsStillALink(string href) =>
        Assert.Contains("](", Render($"""<see href="{href}">text</see>"""), StringComparison.Ordinal);

    /// <summary>
    /// Every <c>href</c> in the .NET 10 reference packs that is not a URL is an authoring slip —
    /// <c>&lt;see href="P:ModelBindingContext.Result"/&gt;</c>, where <c>cref</c> was meant.
    /// Relative to this tree it resolves to nothing, and writing it as a link spends a reader a
    /// file read to discover that.
    /// </summary>
    [Theory]
    [InlineData("P:ModelBindingContext.Result")]
    [InlineData("../guide.md")]
    [InlineData("SomeType.SomeMember")]
    public void AnHrefThatIsMerelyRelativeIsNamedRatherThanLinked(string href)
    {
        string rendered = Render($"""<see href="{href}">text</see>""");

        Assert.DoesNotContain("](", rendered, StringComparison.Ordinal);
        Assert.Contains("text", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ACrefToSomethingThisTreeDoesNotCarryIsNamedRatherThanLinked()
    {
        string rendered = Render("""<see cref="T:Some.Missing.Type"/>""");

        Assert.DoesNotContain("](", rendered, StringComparison.Ordinal);
        Assert.Contains("Type", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ABacktickInsideACodeSpanDoesNotBreakOutOfIt()
    {
        string rendered = Render("<c>a ` b</c>");

        Assert.StartsWith("``", rendered, StringComparison.Ordinal);
        Assert.EndsWith("``", rendered, StringComparison.Ordinal);
    }

    /// <summary>A resolver for which nothing is in the tree, so every cref takes the code path.</summary>
    private sealed class NothingResolves : ILinkResolver
    {
        public TreePath? TypePage(string typeFullName) => null;

        public TreePath? MemberPage(string typeFullName, string memberName) => null;

        public string? TypeLabel(string typeFullName) => null;
    }
}
