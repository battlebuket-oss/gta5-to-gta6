using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GtaTransformationHub.Models;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class ProfileActivationServiceTests
{
    [Fact]
    public async Task Activate_RejectsResearchOnlyPackagesBeforeTouchingGameFiles()
    {
        var package = new ModPackage(
            "candidate-pack", "Candidate pack", "graphics", "Test author",
            new Uri("https://example.com/candidate-pack"), "Unknown", "1.0",
            "1.0.0.0", "2.0.0.0", new string('a', 64),
            Array.Empty<string>(), Array.Empty<string>(), CatalogueStatus.Researching,
            "No adapter", "Journal rollback", "Low", "Research candidate");

        var catalogue = new CatalogueDocument(
            1, "GTA V Legacy", "test",
            new[] { new CatalogueCategory("graphics", "Graphics") },
            new[] { package }, Array.Empty<CatalogueProfile>(), "test");
        var service = new ProfileActivationService();

        var result = await service.ActivateAsync(
            "unused-game-root", "1.5.0.0", "unused-library",
            catalogue, new[] { package.Id },
            new Dictionary<string, IReadOnlyList<DeploymentFile>>());

        Assert.False(result.Success);
        Assert.Null(result.Deployment);
        Assert.Contains(result.Errors, error => error.Contains("not approved for installation"));
    }

    [Fact]
    public async Task Activate_RejectsEmptyProfiles()
    {
        var catalogue = new CatalogueDocument(
            1, "GTA V Legacy", "test",
            new[] { new CatalogueCategory("graphics", "Graphics") },
            Array.Empty<ModPackage>(), Array.Empty<CatalogueProfile>(), "test");
        var service = new ProfileActivationService();

        var result = await service.ActivateAsync(
            "unused-game-root", "1.5.0.0", "unused-library",
            catalogue, Array.Empty<string>(),
            new Dictionary<string, IReadOnlyList<DeploymentFile>>());

        Assert.False(result.Success);
        Assert.Null(result.Deployment);
        Assert.Contains(result.Errors, error => error.Contains("profile is empty"));
    }
}
