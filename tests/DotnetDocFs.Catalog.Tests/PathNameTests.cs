using Xunit;
using DotnetDocFs.Catalog.Internal.Naming;

namespace DotnetDocFs.Catalog.Tests;

public class PathNameTests
{
    [Theory]
    [InlineData("String", "String")]
    [InlineData("List`1", "List-1")]
    [InlineData("Dictionary`2", "Dictionary-2")]
    public void AnArityBacktickBecomesADash(string metadata, string expected) =>
        Assert.Equal(expected, PathName.ForType(metadata));

    [Theory]
    [InlineData(".ctor", "constructors")]
    [InlineData(".cctor", "static-constructor")]
    [InlineData("WriteLine", "WriteLine")]
    [InlineData("op_Addition", "op_Addition")]
    [InlineData("System.IDisposable.Dispose", "System.IDisposable.Dispose")]
    public void AMemberNameKeepsItsSpellingUnlessItWouldHide(string metadata, string expected) =>
        Assert.Equal(expected, PathName.ForMember(metadata));

    [Fact]
    public void APathSeparatorNeverSurvivesIntoAName() =>
        Assert.DoesNotContain('/', PathName.ForMember("a/b"));

    [Fact]
    public void ANameWindowsReservesIsMadeSafe() =>
        Assert.Equal("CON_", PathName.ForType("CON"));
}
