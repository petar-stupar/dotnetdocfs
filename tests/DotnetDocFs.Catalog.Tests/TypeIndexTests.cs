using System.Collections.Immutable;
using DotnetDocFs.Catalog.Internal.Metadata;
using DotnetDocFs.Catalog.Internal.Sources;
using Xunit;

namespace DotnetDocFs.Catalog.Tests;

/// <summary>
/// Reads the reference pack of whichever .NET is installed here. These assertions name types that
/// have been in the base class library since .NET Framework 1.0 and are not going to move, so the
/// suite is pinned to the shape of the index rather than to one SDK version.
/// </summary>
public class TypeIndexTests
{
    private static readonly Lazy<TypeIndex?> Index = new(() =>
    {
        string? root = FrameworkLocator.FindDotnetRoot(configured: null);

        FrameworkPack? pack = root is null ? null : FrameworkLocator.FindPack(root, targetFramework: null);

        return pack is null ? null : TypeIndex.Build(pack.Assemblies);
    });

    private static TypeIndex Built =>
        Index.Value ?? throw new InvalidOperationException("no .NET reference pack is installed on this machine");

    [Fact]
    public void AReferencePackIsFoundOnAMachineWithTheSdkInstalled() =>
        Assert.NotNull(Index.Value);

    [Fact]
    public void TheSystemNamespaceIsIndexed() =>
        Assert.Contains("System", Built.Namespaces);

    [Fact]
    public void ATypeIsFoundByTheNameItsDocumentationIdUses() =>
        Assert.NotNull(Built.Find("System.String"));

    [Fact]
    public void AGenericTypeKeepsItsArityInMetadataAndLosesTheBacktickInThePath()
    {
        TypeRecord list = Assert.IsType<TypeRecord>(Built.Find("System.Collections.Generic.List`1"));

        Assert.Equal("List`1", list.MetadataName);
        Assert.Equal("List-1", list.PathName);
        Assert.Equal("List<T>", list.DisplayName);
        Assert.Equal("T:System.Collections.Generic.List`1", list.DocId);
    }

    // The shape is named as text because TypeShape is internal to the catalog and this method is
    // public, which the compiler will not have.
    [Theory]
    [InlineData("System.String", "Class")]
    [InlineData("System.Int32", "Struct")]
    [InlineData("System.IDisposable", "Interface")]
    [InlineData("System.DayOfWeek", "Enum")]
    [InlineData("System.Action", "Delegate")]
    public void TheShapeOfATypeIsReadFromItsBaseType(string fullName, string expected) =>
        Assert.Equal(expected, Assert.IsType<TypeRecord>(Built.Find(fullName)).Kind.ToString());

    [Fact]
    public void ANestedTypeIsReachedThroughItsDeclaringTypeAndKeepsThatTypesNamespace()
    {
        TypeRecord nested = Assert.IsType<TypeRecord>(Built.Find("System.Environment.SpecialFolder"));

        Assert.Equal("System", nested.Namespace);
        Assert.Equal("System.Environment", nested.DeclaringFullName);
        Assert.Contains(Built.Find("System.Environment")!.Nested, type => type.MetadataName == "SpecialFolder");
    }

    [Fact]
    public void ANestedTypeIsNotListedAsIfItWereTopLevelInItsNamespace() =>
        Assert.DoesNotContain(Built.TypesIn("System"), type => type.MetadataName == "SpecialFolder");

    [Fact]
    public void ANamespaceListsItsTypesInAStableOrder()
    {
        ImmutableArray<TypeRecord> types = Built.TypesIn("System.Text");

        Assert.Contains(types, type => type.MetadataName == "StringBuilder");
        Assert.Equal(types.OrderBy(type => type.PathName, StringComparer.Ordinal), types);
    }

    [Fact]
    public void TheDocumentationFileBesideAnAssemblyIsFound()
    {
        TypeRecord type = Assert.IsType<TypeRecord>(Built.Find("System.String"));

        Assert.NotNull(type.Assembly.XmlPath);
        Assert.True(File.Exists(type.Assembly.XmlPath));
    }
}
