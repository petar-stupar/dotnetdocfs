using System.Text;
using Xunit;

namespace DotnetDocFs.Catalog.Tests;

/// <summary>
/// The served skill, which is the one page in this tree meant to be copied out of it and followed
/// from outside. That is why it alone carries a placeholder for where the tree is, and why it is
/// the only page that needs telling.
/// </summary>
public class SkillTests
{
    private static async Task<string> Skill(DocCatalog catalog) =>
        await Read(catalog, "SKILL.md");

    private static async Task<string> Index(DocCatalog catalog) =>
        await Read(catalog, "index.md");

    private static async Task<string> Read(DocCatalog catalog, string name)
    {
        var skills = (DocDirectory)catalog.Root.Find("skills")!;
        var skill = (DocDirectory)skills.Find("dotnet-api-docs")!;
        var page = (DocPage)skill.Find(name)!;

        return Encoding.UTF8.GetString(
            (await page.ContentAsync(TestContext.Current.CancellationToken)).Span);
    }

    /// <summary>
    /// Nobody said where the tree is, so the placeholder stays. A path this server guessed at
    /// would send an agent to a directory that is not there, which is worse than one that asks to
    /// be filled in.
    /// </summary>
    [Fact]
    public async Task TheSkillKeepsItsPlaceholderWhenNobodyHasSaidWhereTheTreeIs()
    {
        using DocCatalog catalog = DocCatalog.Create(new CatalogOptions());

        Assert.Contains("<mount>/docs/framework", await Skill(catalog), StringComparison.Ordinal);

        Assert.Contains(
            "replace that with where this tree is",
            await Index(catalog),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSkillNamesTheMountpointWhenOneIsKnown()
    {
        using DocCatalog catalog = DocCatalog.Create(
            new CatalogOptions { MountPath = "/mnt/docs" });

        string text = await Skill(catalog);

        Assert.Contains("/mnt/docs/docs/framework", text, StringComparison.Ordinal);
        Assert.Contains("> /mnt/docs/ctl", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<mount>", text, StringComparison.Ordinal);

        // A blank line before it, or the sentence joins the bullet above and renders inside it.
        Assert.Contains(
            "from the mount.\n\nThe paths in it are already",
            await Index(catalog),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Only <c>&lt;mount&gt;</c> is substituted. The other angle-bracketed words are placeholders
    /// a reader is meant to fill in themselves, and a general template pass would eat them.
    /// </summary>
    [Fact]
    public async Task TheSkillLeavesItsOtherPlaceholdersAlone()
    {
        using DocCatalog catalog = DocCatalog.Create(
            new CatalogOptions { MountPath = "/mnt/docs" });

        string text = await Skill(catalog);

        Assert.Contains("/mnt/docs/docs/framework/<namespace>/<Type>/index.md", text, StringComparison.Ordinal);
        Assert.Contains("<package-id-lowercased>/<version>", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATrailingSeparatorOnTheMountpointDoesNotDoubleUp()
    {
        using DocCatalog catalog = DocCatalog.Create(
            new CatalogOptions { MountPath = "/mnt/docs/" });

        string text = await Skill(catalog);

        Assert.Contains("/mnt/docs/docs/framework", text, StringComparison.Ordinal);
        Assert.DoesNotContain("//docs", text, StringComparison.Ordinal);
    }
}
