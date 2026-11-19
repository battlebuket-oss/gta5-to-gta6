using System;
using System.Collections.Generic;
using GtaTransformationHub.Models;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class ModProfileSelectionServiceTests
{
    private readonly ModProfileSelectionService _service = new();

    [Fact]
    public void EnablingPackage_AutomaticallySelectsDependencies()
    {
        var basePackage = Package("script-loader", "Script loader");
        var feature = Package("feature-pack", "Feature pack", dependencies: new[] { "script-loader" });

        var result = _service.SetEnabled(Catalogue(basePackage, feature),
            Array.Empty<string>(), "feature-pack", enabled: true);

        Assert.True(result.Success);
        Assert.Contains("script-loader", result.SelectedPackageIds);
        Assert.Contains("feature-pack", result.SelectedPackageIds);
        Assert.Contains("script-loader", result.AddedDependencies);
    }

    [Fact]
    public void DisablingDependency_AlsoDisablesDependentPackages()
    {
        var basePackage = Package("script-loader", "Script loader");
        var feature = Package("feature-pack", "Feature pack", dependencies: new[] { "script-loader" });

        var result = _service.SetEnabled(Catalogue(basePackage, feature),
            new[] { "script-loader", "feature-pack" }, "script-loader", enabled: false);

        Assert.True(result.Success);
        Assert.Empty(result.SelectedPackageIds);
        Assert.Contains("feature-pack", result.RemovedDependents);
    }

    [Fact]
    public void EnablingMutuallyExclusivePackage_IsRejectedWithoutChangingSelection()
    {
        var first = Package("visual-pack-a", "Visual pack A", exclusiveGroup: "visual-overhaul");
        var second = Package("visual-pack-b", "Visual pack B", exclusiveGroup: "visual-overhaul");

        var result = _service.SetEnabled(Catalogue(first, second),
            new[] { "visual-pack-a" }, "visual-pack-b", enabled: true);

        Assert.False(result.Success);
        Assert.Contains("visual-pack-a", result.SelectedPackageIds);
        Assert.DoesNotContain("visual-pack-b", result.SelectedPackageIds);
    }

    [Fact]
    public void EnablingBlockedPackage_IsRejected()
    {
        var blocked = Package("blocked-pack", "Blocked pack") with
        {
            Status = CatalogueStatus.Blocked
        };

        var result = _service.SetEnabled(Catalogue(blocked),
            Array.Empty<string>(), "blocked-pack", enabled: true);

        Assert.False(result.Success);
        Assert.Empty(result.SelectedPackageIds);
    }

    private static CatalogueDocument Catalogue(params ModPackage[] packages) =>
        new(1, "GTA V Legacy", "test",
            new[] { new CatalogueCategory("graphics", "Graphics") },
            packages, Array.Empty<CatalogueProfile>(), "test");

    private static ModPackage Package(
        string id,
        string name,
        IReadOnlyList<string>? dependencies = null,
        IReadOnlyList<string>? conflicts = null,
        string? exclusiveGroup = null) =>
        new(id, name, "graphics", "test author",
            new Uri("https://example.com/" + id),
            "test licence", "1.0", "1.0.0.0", "2.0.0.0", null,
            dependencies ?? Array.Empty<string>(),
            conflicts ?? Array.Empty<string>(),
            CatalogueStatus.Researching,
            "test adapter", "test restore", "Low", "test package",
            Array.Empty<string>(), exclusiveGroup);
}
