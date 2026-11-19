using System.Text.Json;

namespace GtaTransformationHub.Services;

public sealed record HubAccessibilityPreferences(
    bool HighContrast,
    double TextScale,
    bool ReduceMotion)
{
    public static HubAccessibilityPreferences Default { get; } = new(
        HighContrast: false,
        TextScale: 1.0,
        ReduceMotion: true);
}

/// <summary>
/// Stores Hub-only accessibility preferences locally. These preferences never write
/// settings into GTA V and do not claim to enable unsupported game-side accessibility.
/// </summary>
public sealed class AccessibilityPreferencesService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public AccessibilityPreferencesService(string? settingsPath = null)
    {
        _settingsPath = Path.GetFullPath(settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "accessibility.json"));
    }

    public async Task<HubAccessibilityPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
            return HubAccessibilityPreferences.Default;

        await using var stream = new FileStream(
            _settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var preferences = await JsonSerializer.DeserializeAsync<HubAccessibilityPreferences>(
            stream, JsonOptions, cancellationToken);

        if (preferences is null || !double.IsFinite(preferences.TextScale) ||
            preferences.TextScale is < 1.0 or > 1.5)
            throw new InvalidDataException("Saved accessibility preferences are invalid.");

        return preferences;
    }

    public async Task SaveAsync(
        HubAccessibilityPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (!double.IsFinite(preferences.TextScale) || preferences.TextScale is < 1.0 or > 1.5)
            throw new ArgumentOutOfRangeException(nameof(preferences), "Hub text scale must be between 100% and 150%.");

        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporary = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, preferences, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
