using System;
using System.IO;
using System.Threading.Tasks;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class ModProfileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "GtaHubProfileTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LoadMissingProfile_ReturnsEmptySelection()
    {
        var store = new ModProfileStore(Path.Combine(_root, "active-profile.json"));

        var selected = await store.LoadEnabledPackageIdsAsync();

        Assert.Empty(selected);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsSelectionCaseInsensitively()
    {
        var store = new ModProfileStore(Path.Combine(_root, "active-profile.json"));
        await store.SaveEnabledPackageIdsAsync(new[]
        {
            "sea-life-v", "gta-vi-inspired-hud-single-player", "SEA-LIFE-V"
        });

        var selected = await store.LoadEnabledPackageIdsAsync();

        Assert.Equal(2, selected.Count);
        Assert.Contains("sea-life-v", selected);
        Assert.Contains("gta-vi-inspired-hud-single-player", selected);
    }

    [Fact]
    public async Task LoadInvalidProfile_FailsClosedAndPreservesFile()
    {
        var path = Path.Combine(_root, "active-profile.json");
        Directory.CreateDirectory(_root);
        const string invalid = """{"schemaVersion":99,"profileId":"default","name":"bad","enabledPackageIds":[],"updatedAt":"2026-01-01T00:00:00+00:00"}""";
        await File.WriteAllTextAsync(path, invalid);
        var store = new ModProfileStore(path);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadEnabledPackageIdsAsync());

        Assert.Equal(invalid, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task SaveRejectsInvalidPackageId()
    {
        var store = new ModProfileStore(Path.Combine(_root, "active-profile.json"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveEnabledPackageIdsAsync(new[] { "../unsafe" }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
