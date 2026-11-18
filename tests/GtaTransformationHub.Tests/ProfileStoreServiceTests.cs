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
