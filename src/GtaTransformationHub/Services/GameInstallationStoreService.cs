using System.Text.Json;

namespace GtaTransformationHub.Services;

public sealed record SavedGameInstallation(
    string ExecutablePath,
    string ExecutableSha256,
    string BuildVersion,
    DateTimeOffset SavedAtUtc);

public sealed record GameInstallationRestoreResult(
    bool Restored,
    GameInstallation? Installation,
    string Message);

/// <summary>
/// Remembers the selected GTA V Legacy executable locally. The saved path is never
/// trusted by itself: every restore re-runs executable identity/build validation and
/// compares the current executable SHA-256 with the saved fingerprint.
/// </summary>
public sealed class GameInstallationStoreService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public GameInstallationStoreService(string? settingsPath = null)
    {
        _settingsPath = Path.GetFullPath(settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "game-installation.json"));
    }

    public async Task SaveAsync(
        GameInstallation installation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        if (!Version.TryParse(installation.BuildVersion, out _))
            throw new InvalidDataException("The selected GTA V Legacy build version is not numeric.");

        var executablePath = Path.GetFullPath(installation.ExecutablePath);
        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation.RootPath));
        if (!Path.GetDirectoryName(executablePath)!.Equals(rootPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected GTA5.exe does not belong to the declared game installation root.");

        var fingerprint = await GameExecutableFingerprintService.ComputeSha256Async(
            executablePath, cancellationToken);

        var record = new SavedGameInstallation(
            executablePath,
            fingerprint,
            installation.BuildVersion,
            DateTimeOffset.UtcNow);

        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporary = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, record, JsonOptions, cancellationToken);
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

    public async Task<GameInstallationRestoreResult> RestoreAsync(
        GameInstallationService validator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validator);
        if (!File.Exists(_settingsPath))
            return new GameInstallationRestoreResult(false, null, "No previously validated game installation is saved.");

        try
        {
            await using var stream = new FileStream(
                _settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var saved = await JsonSerializer.DeserializeAsync<SavedGameInstallation>(
                stream, JsonOptions, cancellationToken);

            if (saved is null || string.IsNullOrWhiteSpace(saved.ExecutablePath) ||
                !Version.TryParse(saved.BuildVersion, out _) || !IsSha256(saved.ExecutableSha256))
                return new GameInstallationRestoreResult(false, null,
                    "The saved installation record is malformed. Select GTA5.exe again.");

            var installation = validator.ValidateExecutable(saved.ExecutablePath);
            if (!installation.BuildVersion.Equals(saved.BuildVersion, StringComparison.OrdinalIgnoreCase))
                return new GameInstallationRestoreResult(false, null,
                    "The saved GTA V build changed since the last session. Revalidate GTA5.exe before using packages.");

            var currentHash = await GameExecutableFingerprintService.ComputeSha256Async(
                installation.ExecutablePath, cancellationToken);
            if (!currentHash.Equals(saved.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
                return new GameInstallationRestoreResult(false, null,
                    "The selected GTA5.exe changed since the last session. The saved installation was not trusted; revalidate it.");

            return new GameInstallationRestoreResult(
                true,
                installation,
                "Restored and revalidated GTA V Legacy build " + installation.BuildVersion + ".");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException)
        {
            return new GameInstallationRestoreResult(
                false, null,
                "The saved GTA V installation could not be revalidated: " + exception.Message);
        }
    }

    private static bool IsSha256(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length == 64 && value.All(Uri.IsHexDigit);
}
