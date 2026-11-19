using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GtaTransformationHub.Services;

/// <summary>
/// Persists the user's desired mod selection outside the GTA V installation.
/// A saved selection is not an installation approval and never changes game files.
/// </summary>
public sealed class ModProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _profilePath;

    public ModProfileStore(string? profilePath = null)
    {
        _profilePath = Path.GetFullPath(profilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "Profiles", "active-profile.json"));
    }

    public string ProfilePath => _profilePath;

    public async Task<IReadOnlySet<string>> LoadEnabledPackageIdsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_profilePath))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var stream = new FileStream(
            _profilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

        var profile = await JsonSerializer.DeserializeAsync<PersistedModProfile>(
            stream, JsonOptions, cancellationToken);

        if (profile is null || profile.SchemaVersion != 1 ||
            !string.Equals(profile.ProfileId, "default", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The saved mod profile is invalid or uses an unsupported schema. The file was left unchanged.");
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var packageId in profile.EnabledPackageIds ?? Array.Empty<string>())
        {
            if (!PackageIdValidator.IsValid(packageId) || !result.Add(packageId))
            {
                throw new InvalidDataException(
                    "The saved mod profile contains an invalid or duplicate package ID. The file was left unchanged.");
            }
        }

        return result;
    }

    public async Task SaveEnabledPackageIdsAsync(
        IEnumerable<string> enabledPackageIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(enabledPackageIds);

        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var packageId in enabledPackageIds)
        {
            if (!PackageIdValidator.IsValid(packageId))
                throw new InvalidDataException("The profile contains an invalid package ID.");
            selected.Add(packageId);
        }

        var directory = Path.GetDirectoryName(_profilePath)
            ?? throw new InvalidOperationException("The profile path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _profilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var profile = new PersistedModProfile(
            1,
            "default",
            "My GTA V Transformation",
            selected.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            DateTimeOffset.UtcNow);

        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _profilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private sealed record PersistedModProfile(
        int SchemaVersion,
        string ProfileId,
        string Name,
        string[] EnabledPackageIds,
        DateTimeOffset UpdatedAt);
}
