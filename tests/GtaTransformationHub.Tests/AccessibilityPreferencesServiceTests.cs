using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class AccessibilityPreferencesServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "GtaHubAccessibilityTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoad_PersistsLocalAccessibilityPreferences()
    {
        var path = Path.Combine(_root, "accessibility.json");
        var service = new AccessibilityPreferencesService(path);
        var preferences = new HubAccessibilityPreferences(
            HighContrast: true,
            TextScale: 1.3,
            ReduceMotion: true);

        await service.SaveAsync(preferences);
        var loaded = await service.LoadAsync();

        Assert.Equal(preferences, loaded);
    }

    [Fact]
    public async Task Load_ReturnsDefaultsWhenNoSettingsExist()
    {
        var service = new AccessibilityPreferencesService(Path.Combine(_root, "missing.json"));
        Assert.Equal(HubAccessibilityPreferences.Default, await service.LoadAsync());
    }

    [Theory]
    [InlineData(0.99)]
    [InlineData(1.51)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task Save_RejectsUnsupportedTextScale(double scale)
    {
        var service = new AccessibilityPreferencesService(Path.Combine(_root, "invalid.json"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.SaveAsync(new HubAccessibilityPreferences(false, scale, false)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
