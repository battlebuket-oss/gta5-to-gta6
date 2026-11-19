using GtaTransformationHub.Models;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class ProfilePackageResolverTests
{
    [Fact]
    public void Resolve_UsesExplicitlyPinnedVersion()
    {
        var v1 = CreatePackage("graphics-pack", "1.0.0", "/packages/graphics-1");
        var v2 = CreatePackage("graphics-pack", "2.0.0", "/packages/graphics-2");
        var profile = new ModProfile(
            "visual-profile",
            "Visual Profile",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "graphics-pack" },
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["graphics-pack"] = "1.0.0"
            });

        var result = ProfilePackageResolver.Resolve(profile, new[] { v2, v1 });

        Assert.Same(v1, Assert.Single(result.SelectedPackages));
        Assert.Empty(result.MissingPackages);
        Assert.Empty(result.AmbiguousPackages);
    }

    [Fact]
    public void Resolve_ReportsAmbiguousLegacyProfileInsteadOfChoosingArbitrarily()
    {
        var v1 = CreatePackage("graphics-pack", "1.0.0", "/packages/graphics-1");
        var v2 = CreatePackage("graphics-pack", "2.0.0", "/packages/graphics-2");
        var profile = new ModProfile(
            "legacy-profile",
            "Legacy Profile",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "graphics-pack" },
            DateTimeOffset.UtcNow);

        var result = ProfilePackageResolver.Resolve(profile, new[] { v1, v2 });

        Assert.Empty(result.SelectedPackages);
        Assert.Empty(result.MissingPackages);
        Assert.Contains("graphics-pack", result.AmbiguousPackages);
    }

    [Fact]
    public void Resolve_ReportsMissingPinnedVersion()
    {
        var v2 = CreatePackage("graphics-pack", "2.0.0", "/packages/graphics-2");
        var profile = new ModProfile(
            "pinned-profile",
            "Pinned Profile",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "graphics-pack" },
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>
            {
                ["graphics-pack"] = "1.0.0"
            });

        var result = ProfilePackageResolver.Resolve(profile, new[] { v2 });

        Assert.Empty(result.SelectedPackages);
        Assert.Contains("graphics-pack@1.0.0", result.MissingPackages);
    }

    private static ImportedPackage CreatePackage(string id, string version, string root)
    {
        var manifest = new PackageManifest(
            1,
            id,
            id,
            "Test Author",
            "https://example.com/" + id,
            "Test licence",
            version,
            "GTA V Legacy",
            "1.0.0.0",
            "99.0.0.0",
            Array.Empty<string>(),
            Array.Empty<string>(),
            new[]
            {
                new PackageManifestFile(
                    "mods/sample.dat",
                    "mods/sample.dat",
                    new string('a', 64),
                    1)
            });

        return new ImportedPackage(manifest, root, string.Empty, DateTimeOffset.UtcNow);
    }
}
