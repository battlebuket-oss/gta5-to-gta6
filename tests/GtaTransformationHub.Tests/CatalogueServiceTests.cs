using System;
using System.IO;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class CatalogueServiceTests
{
    [Fact]
    public void Load_ParsesResearchCatalogue_WithoutApprovingCandidates()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/GtaTransformationHub/Catalogue/catalogue.json"));

        var catalogue = new CatalogueService().Load(path);

        Assert.Equal("GTA V Legacy", catalogue.GameTarget);
        Assert.NotEmpty(catalogue.Packages);
        Assert.All(catalogue.Packages, package => Assert.Equal(
            GtaTransformationHub.Models.CatalogueStatus.Researching, package.Status));
    }


    [Fact]
    public void Load_RejectsNoDependenciesOrProfilesThatReferenceUnknownPackages()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/GtaTransformationHub/Catalogue/catalogue.json"));
        var catalogue = new CatalogueService().Load(path);
        var packageIds = catalogue.Packages.Select(package => package.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(catalogue.Packages.SelectMany(package => package.Dependencies),
            dependency => Assert.Contains(dependency, packageIds));
        Assert.All(catalogue.Profiles.SelectMany(profile => profile.EnabledPackageIds),
            packageId => Assert.Contains(packageId, packageIds));
    }

    [Fact]
    public void ValidateSelection_RejectsResearchOnlyPackages()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/GtaTransformationHub/Catalogue/catalogue.json"));
        var catalogue = new CatalogueService().Load(path);

        var result = new CatalogueService().ValidateSelection(
            catalogue,
            new[] { "naturalvision-evolved-legacy" },
            "1.0.3889.0");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("not approved for installation"));
        Assert.Contains(result.Errors, error => error.Contains("SHA-256"));
    }
}
