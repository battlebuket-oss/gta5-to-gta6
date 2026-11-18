using GtaTransformationHub.Models;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class ProfileStoreServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GtaHubProfileTests", Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public ProfileStoreServiceTests()
    {
        Directory.CreateDirectory(_root);
        _path = Path.Combine(_root, "profiles.json");
    }

    [Fact]
    public async Task SaveAndLoad_PersistsProfileAcrossServiceInstances()
    {
        var profile = new ModProfile(
            "balanced-profile",
            "Balanced Profile",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "test-package" },
            DateTimeOffset.UtcNow);

        await new ProfileStoreService(_path).SaveAsync(profile);
        var loaded = await new ProfileStoreService(_path).LoadAsync();

        var result = Assert.Single(loaded);
        Assert.Equal(profile.Id, result.Id);
        Assert.Equal(profile.Name, result.Name);
        Assert.Contains("test-package", result.EnabledPackageIds);
    }


    [Fact]
    public async Task SaveAndLoad_PreservesExactPackageVersions()
    {
        var profile = new ModProfile(
            "pinned-profile",
            "Pinned Profile",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "visual-pack", "police-pack" },
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["visual-pack"] = "2.1.0",
                ["police-pack"] = "1.4.2"
            });

        await new ProfileStoreService(_path).SaveAsync(profile);
        var loaded = await new ProfileStoreService(_path).LoadAsync();

        var result = Assert.Single(loaded);
        Assert.NotNull(result.PackageVersions);
        Assert.Equal("2.1.0", result.PackageVersions["visual-pack"]);
        Assert.Equal("1.4.2", result.PackageVersions["police-pack"]);
    }

    [Fact]
    public async Task Load_LegacyProfileWithoutPackageVersions_RemainsCompatible()
    {
        var legacyJson = """
        {
          "SchemaVersion": 1,
          "Profiles": [
            {
              "Id": "legacy-profile",
              "Name": "Legacy Profile",
              "EnabledPackageIds": [ "old-package" ],
              "UpdatedAt": "2026-11-18T12:00:00+00:00"
            }
          ]
        }
        """;
        await File.WriteAllTextAsync(_path, legacyJson);

        var loaded = await new ProfileStoreService(_path).LoadAsync();

        var result = Assert.Single(loaded);
        Assert.Contains("old-package", result.EnabledPackageIds);
        Assert.Null(result.PackageVersions);
    }

    [Fact]
    public async Task Save_RejectsPartialVersionPins()
    {
        var profile = new ModProfile(
            "partial-profile",
            "Partial Profile",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "visual-pack", "police-pack" },
            DateTimeOffset.UtcNow,
            new Dictionary<string, string> { ["visual-pack"] = "1.0.0" });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ProfileStoreService(_path).SaveAsync(profile));
    }

    [Fact]
    public async Task Delete_RemovesOnlyTheRequestedProfile()
    {
        var store = new ProfileStoreService(_path);
        await store.SaveAsync(new ModProfile("first-profile", "First", new HashSet<string>(), DateTimeOffset.UtcNow));
        await store.SaveAsync(new ModProfile("second-profile", "Second", new HashSet<string>(), DateTimeOffset.UtcNow));

        await store.DeleteAsync("first-profile");
        var remaining = await store.LoadAsync();

        Assert.Single(remaining);
        Assert.Equal("second-profile", remaining[0].Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
