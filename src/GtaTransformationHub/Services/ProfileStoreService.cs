using System.Text.Json;
using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

public sealed class ProfileStoreService
{
    private sealed record ProfileStoreDocument(int SchemaVersion, IReadOnlyList<StoredProfile> Profiles);
    private sealed record StoredProfile(string Id, string Name, IReadOnlyList<string> EnabledPackageIds, DateTimeOffset UpdatedAt);

    private readonly string _storePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ProfileStoreService(string? storePath = null)
    {
        _storePath = Path.GetFullPath(storePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "profiles.json"));
    }

    public async Task<IReadOnlyList<ModProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadWithoutLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(ModProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!PackageIdValidator.IsValid(profile.Id) ||
            string.IsNullOrWhiteSpace(profile.Name) ||
            profile.Name.Length > 100 ||
            profile.EnabledPackageIds is null)
            throw new InvalidDataException("A profile must have a valid ID, name and package list.");

        var packageIds = profile.EnabledPackageIds.ToArray();
        if (packageIds.Any(id => !PackageIdValidator.IsValid(id)) ||
            packageIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != packageIds.Length)
            throw new InvalidDataException("A profile contains invalid or duplicate package IDs.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var existing = (await LoadWithoutLockAsync(cancellationToken)).ToList();
            existing.RemoveAll(item => item.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
            existing.Add(profile with { UpdatedAt = DateTimeOffset.UtcNow });
            await WriteWithoutLockAsync(existing, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string profileId, CancellationToken cancellationToken = default)
    {
        if (!PackageIdValidator.IsValid(profileId))
            throw new ArgumentException("Invalid profile ID.", nameof(profileId));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var profiles = await LoadWithoutLockAsync(cancellationToken);
            var remaining = profiles.Where(profile =>
                !profile.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (remaining.Length == profiles.Count)
                return;

            await WriteWithoutLockAsync(remaining, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<ModProfile>> LoadWithoutLockAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_storePath))
            return Array.Empty<ModProfile>();

        await using var stream = new FileStream(_storePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var document = await JsonSerializer.DeserializeAsync<ProfileStoreDocument>(
            stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The saved profile store is empty or invalid.");

        if (document.SchemaVersion != 1 || document.Profiles is null)
            throw new InvalidDataException("The saved profile store has an unsupported schema.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var profiles = new List<ModProfile>();
        foreach (var stored in document.Profiles)
        {
            if (stored is null ||
                !PackageIdValidator.IsValid(stored.Id) ||
                string.IsNullOrWhiteSpace(stored.Name) ||
                stored.Name.Length > 100 ||
                stored.EnabledPackageIds is null ||
                !ids.Add(stored.Id))
                throw new InvalidDataException("The saved profile store contains an invalid or duplicate profile.");

            var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var packageId in stored.EnabledPackageIds)
            {
                if (!PackageIdValidator.IsValid(packageId) || !packageIds.Add(packageId))
                    throw new InvalidDataException("A saved profile contains an invalid or duplicate package ID.");
            }

            profiles.Add(new ModProfile(stored.Id, stored.Name, packageIds, stored.UpdatedAt));
        }

        return profiles;
    }

    private async Task WriteWithoutLockAsync(
        IReadOnlyCollection<ModProfile> profiles,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_storePath)!;
        Directory.CreateDirectory(directory);

        var stored = profiles.Select(profile => new StoredProfile(
            profile.Id,
            profile.Name,
            profile.EnabledPackageIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray(),
            profile.UpdatedAt)).ToArray();

        var tempPath = _storePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream, new ProfileStoreDocument(1, stored), JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, _storePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
