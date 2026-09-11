using Xunit;
using DotnetDocFs.Catalog.Internal.Naming;

namespace DotnetDocFs.Catalog.Tests;

/// <summary>
/// The links between pages are the whole point of laying the catalog out as files: an agent
/// follows one with an ordinary read. A link that resolves anywhere but the target is worse than
/// no link, so the path arithmetic is pinned here.
/// </summary>
public class TreePathTests
{
    private static readonly TreePath Docs = TreePath.Root.Add("docs", "framework");

    [Fact]
    public void ASiblingTypeIsReachedThroughTheNamespaceDirectory()
    {
        TreePath page = Docs.Add("System", "String", "Compare.md");
        TreePath target = Docs.Add("System", "StringComparison", "index.md");

        Assert.Equal("../StringComparison/index.md", target.RelativeToPageIn(page));
    }

    [Fact]
    public void AMemberLinksToItsOwnTypeWithoutLeavingTheDirectory()
    {
        TreePath page = Docs.Add("System", "String", "Compare.md");
        TreePath target = Docs.Add("System", "String", "index.md");

        Assert.Equal("index.md", target.RelativeToPageIn(page));
    }

    [Fact]
    public void ATypeInAnotherNamespaceWalksUpToTheFrameworkAndBackDown()
    {
        TreePath page = Docs.Add("System", "String", "index.md");
        TreePath target = Docs.Add("System.Text", "StringBuilder", "index.md");

        Assert.Equal("../../System.Text/StringBuilder/index.md", target.RelativeToPageIn(page));
    }

    [Fact]
    public void AnIndexLinksDownToItsChildren()
    {
        TreePath page = Docs.Add("System", "index.md");
        TreePath target = Docs.Add("System", "String", "index.md");

        Assert.Equal("String/index.md", target.RelativeToPageIn(page));
    }

    [Fact]
    public void APageThatLinksToItselfNamesItsOwnFile()
    {
        // Its own file name resolves to itself from its own directory, so this link is not
        // broken. The renderer still prefers plain code over a self-link; this pins that the
        // arithmetic does not produce the empty string, which no reader would follow.
        TreePath page = Docs.Add("System", "String", "index.md");

        Assert.Equal("index.md", page.RelativeToPageIn(page));
    }

    [Fact]
    public void ALinkAcrossAreasReachesTheRoot()
    {
        TreePath page = Docs.Add("System", "String", "index.md");
        TreePath target = TreePath.Root.Add("docs", "packages", "index.md");

        Assert.Equal("../../../packages/index.md", target.RelativeToPageIn(page));
    }
}
