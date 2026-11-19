using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

/// <summary>
/// Lists locally imported, research-only packages and recomputes every inventoried file's SHA-256,
/// including the retained original ZIP/OIV source archive. It never loads or executes package code.
/// </summary>
public sealed class ImportedModLibraryService
{
    private const int MaximumPackages = 500;
    private const int MaximumManifestBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<ImportedModLibraryEntry>> LoadAsync(
        string packageRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        var root = Path.GetFullPath(packageRoot);
        if (!Directory.Exists(root))
            return Array.Empty<ImportedModLibraryEntry>();

        EnsureNoReparsePointAncestors(root);
        var directories = Directory.EnumerateDirectories(root)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumPackages + 1)
            .ToArray();

        if (directories.Length > MaximumPackages)
            throw new InvalidDataException("The local package library exceeds the 500-package display limit.");

        var entries = new List<ImportedModLibraryEntry>(directories.Length);
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(await LoadOneAsync(root, directory, cancellationToken));
        }

        return entries
            .OrderByDescending(entry => entry.ImportedAt)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<ImportedModVerificationResult> VerifyAsync(
        string packageRoot,
        string packageDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);

        var root = Path.GetFullPath(packageRoot);
        var directory = Path.GetFullPath(packageDirectory);
        if (!Directory.Exists(root) || !Directory.Exists(directory))
            return new ImportedModVerificationResult(false, 0, new[] { "The package library or selected package directory is missing." });

        try
        {
            EnsureContainedDirectory(root, directory);
            EnsureNoReparsePointAncestors(directory);
            var manifestPath = Path.Combine(directory, "package-manifest.json");
            if (!File.Exists(manifestPath) ||
                (File.GetAttributes(manifestPath) & FileAttributes.ReparsePoint) != 0)
                return new ImportedModVerificationResult(false, 0, new[] { "The package manifest is missing or is a reparse point." });

            var manifestInfo = new FileInfo(manifestPath);
            if (manifestInfo.Length <= 0 || manifestInfo.Length > MaximumManifestBytes)
                return new ImportedModVerificationResult(false, 0, new[] { "The package manifest is empty or exceeds the size limit." });

            await using var stream = new FileStream(
                manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
            var manifest = await JsonSerializer.DeserializeAsync<ImportedModManifest>(
                stream, JsonOptions, cancellationToken);
            if (manifest is null)
                return new ImportedModVerificationResult(false, 0, new[] { "The package manifest could not be parsed." });

            var errors = new List<string>();
            if (manifest.SchemaVersion != 1)
                errors.Add("Unsupported package-manifest schema version.");
            try
            {
                ModArchiveImportService.ValidateMetadata(manifest.Metadata);
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
            {
                errors.Add("Provenance metadata is invalid: " + exception.Message);
            }

            if (!IsSha256(manifest.ArchiveSha256))
                errors.Add("Archive SHA-256 value is invalid.");
            else if (manifest.Metadata is not null)
            {
                var expectedDirectory = manifest.Metadata.PackageId + "-" +
                                        manifest.ArchiveSha256[..12].ToLowerInvariant();
                if (!string.Equals(Path.GetFileName(directory), expectedDirectory, StringComparison.OrdinalIgnoreCase))
                    errors.Add("Package folder name does not match its package ID and archive digest.");
            }

            if (manifest.Files is null || manifest.Files.Count == 0 || manifest.Files.Count > 2500)
                errors.Add("File inventory is empty or outside the permitted entry range.");

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var checkedFiles = 0;
            if (manifest.Files is not null)
            {
                foreach (var file in manifest.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (file is null || !IsSha256(file.Sha256) || file.SizeBytes < 0)
                    {
                        errors.Add("A file inventory record has invalid size or SHA-256 metadata.");
                        continue;
                    }

                    string target;
                    try
                    {
                        target = ResolveContainedFilePath(directory, file.RelativePath);
                    }
                    catch (InvalidDataException exception)
                    {
                        errors.Add("Unsafe inventory path: " + exception.Message);
                        continue;
                    }

                    if (!paths.Add(file.RelativePath))
                    {
                        errors.Add("Duplicate or case-colliding inventory path: " + file.RelativePath);
                        continue;
                    }

                    if (!File.Exists(target))
                    {
                        errors.Add("Imported file is missing: " + file.RelativePath);
                        continue;
                    }

                    if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                    {
                        errors.Add("Imported file is a reparse point: " + file.RelativePath);
                        continue;
                    }

                    var fileInfo = new FileInfo(target);
                    if (fileInfo.Length != file.SizeBytes)
                    {
                        errors.Add("Imported file size differs from the manifest: " + file.RelativePath);
                        continue;
                    }

                    await using var fileStream = new FileStream(
                        target, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
                    var actualHash = Convert.ToHexString(
                        await System.Security.Cryptography.SHA256.HashDataAsync(fileStream, cancellationToken));
                    checkedFiles++;
                    if (!actualHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                        errors.Add("Imported file SHA-256 does not match the manifest: " + file.RelativePath);
                }
            }

            errors.AddRange(FindUntrackedFileWarnings(directory, paths, cancellationToken));
            return new ImportedModVerificationResult(errors.Count == 0, checkedFiles, errors);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or NotSupportedException or InvalidOperationException)
        {
            return new ImportedModVerificationResult(false, 0, new[] { "Verification failed: " + exception.Message });
        }
    }

    private static async Task<ImportedModLibraryEntry> LoadOneAsync(
        string root,
        string directory,
        CancellationToken cancellationToken)
    {
        var fallbackName = Path.GetFileName(directory);
        try
        {
            var fullDirectory = Path.GetFullPath(directory);
            EnsureContainedDirectory(root, fullDirectory);
            EnsureNoReparsePointAncestors(fullDirectory);

            var manifestPath = Path.Combine(fullDirectory, "package-manifest.json");
            if (!File.Exists(manifestPath))
                return Invalid(fallbackName, fullDirectory, "Package manifest is missing.");

            if ((File.GetAttributes(manifestPath) & FileAttributes.ReparsePoint) != 0)
                return Invalid(fallbackName, fullDirectory, "Package manifest is a reparse point.");

            var manifestInfo = new FileInfo(manifestPath);
            if (manifestInfo.Length <= 0 || manifestInfo.Length > MaximumManifestBytes)
                return Invalid(fallbackName, fullDirectory, "Package manifest is empty or exceeds the size limit.");

            await using var stream = new FileStream(
                manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
            var manifest = await JsonSerializer.DeserializeAsync<ImportedModManifest>(
                stream, JsonOptions, cancellationToken);

            if (manifest is null)
                return Invalid(fallbackName, fullDirectory, "Package manifest could not be parsed.");

            var warnings = new List<string>();
            if (manifest.SchemaVersion != 1)
                warnings.Add("Unsupported package-manifest schema version.");
            if (manifest.Metadata is null)
                warnings.Add("Package provenance metadata is missing.");
            else
            {
                try
                {
                    ModArchiveImportService.ValidateMetadata(manifest.Metadata);
                }
                catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
                {
                    warnings.Add("Provenance metadata is invalid: " + exception.Message);
                }
            }

            if (!IsSha256(manifest.ArchiveSha256))
                warnings.Add("Archive SHA-256 value is invalid.");

            if (manifest.Files is null || manifest.Files.Count == 0 || manifest.Files.Count > 2500)
                warnings.Add("File inventory is empty or outside the permitted entry range.");

            if (manifest.Status != CatalogueStatus.Researching)
                warnings.Add("Imported package status was changed from Researching; the claimed status is not trusted.");

            if (manifest.Metadata is not null && IsSha256(manifest.ArchiveSha256))
            {
                var expectedDirectory = manifest.Metadata.PackageId + "-" +
                                        manifest.ArchiveSha256[..12].ToLowerInvariant();
                if (!string.Equals(Path.GetFileName(fullDirectory), expectedDirectory, StringComparison.OrdinalIgnoreCase))
                    warnings.Add("Package folder name does not match its package ID and archive digest.");
            }

            var checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long verifiedTotalBytes = 0;
            if (manifest.Files is not null)
            {
                foreach (var file in manifest.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (file is null || !IsSha256(file.Sha256) || file.SizeBytes < 0)
                    {
                        warnings.Add("A file inventory record has invalid size or SHA-256 metadata.");
                        continue;
                    }

                    string target;
                    try
                    {
                        target = ResolveContainedFilePath(fullDirectory, file.RelativePath);
                    }
                    catch (InvalidDataException exception)
                    {
                        warnings.Add("Unsafe inventory path: " + exception.Message);
                        continue;
                    }

                    if (!checkedPaths.Add(file.RelativePath))
                    {
                        warnings.Add("Duplicate or case-colliding inventory path: " + file.RelativePath);
                        continue;
                    }

                    if (!File.Exists(target))
                    {
                        warnings.Add("Imported file is missing: " + file.RelativePath);
                        continue;
                    }

                    if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                    {
                        warnings.Add("Imported file is a reparse point: " + file.RelativePath);
                        continue;
                    }

                    if (file.SizeBytes > 268435456)
                    {
                        warnings.Add("Imported file exceeds the per-file verification limit: " + file.RelativePath);
                        continue;
                    }

                    verifiedTotalBytes = checked(verifiedTotalBytes + file.SizeBytes);
                    if (verifiedTotalBytes > 1073741824)
                    {
                        warnings.Add("Package inventory exceeds the 1 GiB total verification limit.");
                        break;
                    }

                    var actualLength = new FileInfo(target).Length;
                    if (actualLength != file.SizeBytes)
                    {
                        warnings.Add("Imported file size differs from the manifest: " + file.RelativePath);
                        continue;
                    }

                    await using var fileStream = new FileStream(
                        target, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
                    var actualHash = Convert.ToHexString(
                        await SHA256.HashDataAsync(fileStream, cancellationToken));
                    if (!string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                        warnings.Add("Imported file SHA-256 does not match the manifest: " + file.RelativePath);
                }
            }

            warnings.AddRange(FindUntrackedFileWarnings(fullDirectory, checkedPaths, cancellationToken));

            var metadata = manifest.Metadata;
            var packageName = metadata?.Name;
            if (string.IsNullOrWhiteSpace(packageName))
                packageName = fallbackName;

            string? originalArchivePath = null;
            if (!string.IsNullOrWhiteSpace(manifest.OriginalArchiveRelativePath))
            {
                try
                {
                    var resolvedArchivePath = ResolveContainedFilePath(fullDirectory, manifest.OriginalArchiveRelativePath);
                    if (!File.Exists(resolvedArchivePath))
                        warnings.Add("The retained original source archive is missing.");
                    else if ((File.GetAttributes(resolvedArchivePath) & FileAttributes.ReparsePoint) != 0)
                        warnings.Add("The retained original source archive is a reparse point.");
                    else if (warnings.Count == 0)
                        originalArchivePath = resolvedArchivePath;
                }
                catch (InvalidDataException exception)
                {
                    warnings.Add("The retained source archive path is invalid: " + exception.Message);
                }
            }

            var verificationNote = warnings.Count == 0
                ? "Per-file SHA-256 values were re-computed and compared with the import manifest, including the retained source archive."
                : "The local inventory is not fully verified. Packages without a retained source archive must be re-imported before external installation.";

            return new ImportedModLibraryEntry(
                metadata?.PackageId ?? "unknown",
                packageName,
                metadata?.Author ?? string.Empty,
                metadata?.Version ?? string.Empty,
                "Researching",
                manifest.ImportedAt,
                manifest.Files?.Count ?? 0,
                fullDirectory,
                warnings.Count == 0,
                verificationNote,
                warnings,
                originalArchivePath,
                manifest.OivInspection);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or NotSupportedException or InvalidOperationException)
        {
            return Invalid(fallbackName, directory, "Package could not be read: " + exception.Message);
        }
    }

    private static IReadOnlyList<string> FindUntrackedFileWarnings(
        string packageDirectory,
        HashSet<string> inventoryPaths,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(packageDirectory);
        var inspectedEntries = 0;

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentDirectory = pendingDirectories.Pop();
            foreach (var entryPath in Directory.EnumerateFileSystemEntries(currentDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                inspectedEntries++;
                if (inspectedEntries > 10000)
                    throw new InvalidDataException("The package directory exceeds the 10,000-entry verification limit.");

                var attributes = File.GetAttributes(entryPath);
                var relativePath = Path.GetRelativePath(packageDirectory, entryPath)
                    .Replace(Path.DirectorySeparatorChar, '/');

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    warnings.Add("A reparse-point file or directory is present in the package: " + relativePath);
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pendingDirectories.Push(entryPath);
                    continue;
                }

                if (string.Equals(relativePath, "package-manifest.json", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!inventoryPaths.Contains(relativePath))
                    warnings.Add("An untracked file is present outside the import manifest: " + relativePath);
            }
        }

        return warnings;
    }

    private static ImportedModLibraryEntry Invalid(string name, string directory, string warning) =>
        new(
            "unknown",
            name,
            string.Empty,
            string.Empty,
            "Unreadable",
            DateTimeOffset.MinValue,
            0,
            directory,
            false,
            "Inventory verification unavailable.",
            new[] { warning });

    private static string ResolveContainedFilePath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Length > 240 ||
            relativePath.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath) ||
            relativePath.IndexOf((char)0) >= 0)
            throw new InvalidDataException("The path is empty, rooted or overlong.");

        var normalized = relativePath.Replace('\\', '/');
        var segments = normalized.Split('/');
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment is "." or ".." ||
                segment.EndsWith(" ", StringComparison.Ordinal) ||
                segment.EndsWith(".", StringComparison.Ordinal) ||
                segment.IndexOfAny(new[] { '<', '>', ':', '"', '|', '?', '*' }) >= 0 ||
                segment.Any(char.IsControl) ||
                Path.GetInvalidFileNameChars().Any(character => segment.Contains(character)))
                throw new InvalidDataException("The path contains an unsafe segment.");
        }

        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, Path.Combine(segments)));
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The path escapes the package directory.");

        var current = fullRoot;
        foreach (var segment in segments.Take(segments.Length - 1))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("A parent directory is a reparse point.");
        }

        return fullPath;
    }

    private static void EnsureContainedDirectory(string root, string directory)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        if (!directory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A package directory escapes the library root.");
    }

    private static void EnsureNoReparsePointAncestors(string path)
    {
        for (DirectoryInfo? current = new DirectoryInfo(Path.GetFullPath(path));
             current is not null;
             current = current.Parent)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The package library contains a reparse-point directory.");
        }
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

public sealed record ImportedModLibraryEntry(
    string PackageId,
    string Name,
    string Author,
    string Version,
    string Status,
    DateTimeOffset ImportedAt,
    int FileCount,
    string PackageDirectory,
    bool ManifestValid,
    string VerificationNote,
    IReadOnlyList<string> Warnings,
    string? OriginalArchivePath = null,
    OivManifestInspectionResult? OivInspection = null);

public sealed record ImportedModVerificationResult(
    bool IsValid,
    int FilesChecked,
    IReadOnlyList<string> Errors);
