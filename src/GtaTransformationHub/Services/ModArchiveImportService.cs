using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

/// <summary>
/// Safely imports user-provided ZIP/OIV containers into a quarantined local package
/// directory. Import never approves or installs a mod and never executes package code.
/// </summary>
public sealed class ModArchiveImportService
{
    private readonly ModArchiveImportLimits _limits;

    public ModArchiveImportService(ModArchiveImportLimits? limits = null)
    {
        _limits = limits ?? new ModArchiveImportLimits();
        if (_limits.MaximumEntries < 1 ||
            _limits.MaximumEntryBytes < 1 ||
            _limits.MaximumTotalBytes < 1 ||
            _limits.MaximumCompressionRatio < 1)
            throw new ArgumentOutOfRangeException(nameof(limits), "Archive limits must be positive.");
    }

    public async Task<ModArchiveImportResult> ImportAsync(
        string archivePath,
        string importRoot,
        ModArchiveMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ValidateMetadata(metadata);
        var source = Path.GetFullPath(archivePath);
        if (!File.Exists(source))
            throw new FileNotFoundException("The selected mod archive was not found.", source);
        var extension = Path.GetExtension(source);
        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".oiv", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only ZIP and OpenIV OIV containers can be imported.");

        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Reparse-point archive files are not accepted.");

        var root = Path.GetFullPath(importRoot);
        Directory.CreateDirectory(root);
        EnsureNoReparsePointAncestors(root);

        await using var archiveStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        var archiveHash = Convert.ToHexString(
            await SHA256.HashDataAsync(archiveStream, cancellationToken));
        archiveStream.Position = 0;
        var packageDirectoryName = metadata.PackageId + "-" + archiveHash[..12].ToLowerInvariant();
        var destination = Path.Combine(root, packageDirectoryName);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("This exact archive has already been imported for this package ID.");

        var staging = Path.Combine(root, ".import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        try
        {
            var files = new List<ImportedModFile>();
            using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true))
            {
                if (archive.Entries.Count == 0 || archive.Entries.Count > _limits.MaximumEntries)
                    throw new InvalidDataException("The archive is empty or exceeds the configured entry limit.");

                var nodes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                long declaredTotal = 0;
                var fileEntries = new List<(ZipArchiveEntry Entry, string RelativePath)>();

                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                                      entry.FullName.EndsWith("\\", StringComparison.Ordinal);
                    var relativePath = NormalizeArchivePath(entry.FullName, isDirectory);
                    ValidateArchiveEntryType(entry, isDirectory);
                    if (string.Equals(relativePath, "package-manifest.json", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The archive uses the reserved package-manifest.json filename.");
                    if (nodes.ContainsKey(relativePath))
                        throw new InvalidDataException("The archive contains duplicate or case-colliding paths: " + relativePath);

                    var parent = Path.GetDirectoryName(relativePath);
                    while (!string.IsNullOrEmpty(parent))
                    {
                        if (nodes.TryGetValue(parent, out var parentIsDirectory) && !parentIsDirectory)
                            throw new InvalidDataException("A file entry is also used as a parent directory: " + parent);
                        parent = Path.GetDirectoryName(parent);
                    }

                    if (!isDirectory)
                    {
                        if (nodes.Keys.Any(existing => existing.StartsWith(relativePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                            throw new InvalidDataException("A file entry conflicts with an existing child path: " + relativePath);

                        if (entry.Length < 0 || entry.Length > _limits.MaximumEntryBytes)
                            throw new InvalidDataException("An archive entry exceeds the per-file size limit: " + relativePath);
                        declaredTotal = checked(declaredTotal + entry.Length);
                        if (declaredTotal > _limits.MaximumTotalBytes)
                            throw new InvalidDataException("The archive exceeds the total uncompressed size limit.");

                        if (entry.Length > 10 * 1024 * 1024 &&
                            (entry.CompressedLength == 0 ||
                             entry.Length / (double)entry.CompressedLength > _limits.MaximumCompressionRatio))
                            throw new InvalidDataException("The archive contains an excessive compression ratio: " + relativePath);

                        fileEntries.Add((entry, relativePath));
                    }

                    nodes.Add(relativePath, isDirectory);
                }

                if (fileEntries.Count == 0)
                    throw new InvalidDataException("The archive contains no files.");

                long actualTotal = 0;
                foreach (var (entry, relativePath) in fileEntries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var target = ResolveContainedPath(staging, relativePath);
                    var parentDirectory = Path.GetDirectoryName(target)!;
                    Directory.CreateDirectory(parentDirectory);
                    EnsureNoReparsePointAncestors(parentDirectory);

                    var digest = await ExtractAndHashAsync(
                        entry, target, _limits.MaximumEntryBytes,
                        _limits.MaximumTotalBytes - actualTotal, cancellationToken);
                    var size = new FileInfo(target).Length;
                    actualTotal = checked(actualTotal + size);
                    if (actualTotal > _limits.MaximumTotalBytes)
                        throw new InvalidDataException("The archive exceeded the total extraction limit.");

                    files.Add(new ImportedModFile(
                        relativePath.Replace(Path.DirectorySeparatorChar, '/'),
                        size,
                        digest));
                }
            }

            var manifest = new ImportedModManifest(
                1,
                metadata,
                archiveHash,
                DateTimeOffset.UtcNow,
                CatalogueStatus.Researching,
                files);

            var manifestPath = Path.Combine(staging, "package-manifest.json");
            await using (var manifestStream = new FileStream(
                manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(
                    manifestStream, manifest,
                    new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter() }
                },
                    cancellationToken);
                await manifestStream.FlushAsync(cancellationToken);
            }

            Directory.Move(staging, destination);
            return new ModArchiveImportResult(
                metadata.PackageId, destination,
                Path.Combine(destination, "package-manifest.json"),
                archiveHash, files);
        }
        catch
        {
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
            catch
            {
                // Preserve the original import exception; an orphan staging directory
                // can be inspected and removed manually.
            }
            throw;
        }
    }

    public static void ValidateMetadata(ModArchiveMetadata metadata)
    {
        if (metadata is null)
            throw new ArgumentNullException(nameof(metadata));
        if (!PackageIdValidator.IsValid(metadata.PackageId))
            throw new InvalidDataException("A valid package ID is required.");
        RequireText(metadata.Name, 120, "Package name");
        RequireText(metadata.Author, 120, "Author");
        RequireText(metadata.Licence, 500, "Licence");
        RequireText(metadata.Version, 80, "Version");
        if (metadata.SourceUri is null || metadata.SourceUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("A source URL using HTTPS is required.");
    }

    private static void RequireText(string? value, int maxLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            throw new InvalidDataException(label + " is required and must be no longer than " + maxLength + " characters.");
    }

    private static string NormalizeArchivePath(string rawPath, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(rawPath) || rawPath.Length > 240 ||
            rawPath.StartsWith("/", StringComparison.Ordinal) ||
            rawPath.StartsWith("\\\\", StringComparison.Ordinal) ||
            Path.IsPathRooted(rawPath) ||
            rawPath.IndexOf((char)0) >= 0)
            throw new InvalidDataException("The archive contains an absolute, empty, or overlong path.");

        var normalized = rawPath.Replace('\\', '/');
        if (isDirectory)
            normalized = normalized[..^1];
        var segments = normalized.Split('/');
        if (segments.Length == 0)
            throw new InvalidDataException("The archive contains an empty path.");

        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment is "." or ".." ||
                segment.EndsWith(" ", StringComparison.Ordinal) ||
                segment.EndsWith(".", StringComparison.Ordinal) ||
                segment.IndexOfAny(new[] { '<', '>', ':', '"', '|', '?', '*' }) >= 0 ||
                segment.Any(char.IsControl) ||
                Path.GetInvalidFileNameChars().Any(invalidCharacter => segment.Contains(invalidCharacter)))
                throw new InvalidDataException("The archive contains an unsafe path segment: " + segment);

            var baseName = segment.Split('.')[0];
            if (baseName.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                Enumerable.Range(1, 9).Any(i =>
                    baseName.Equals("COM" + i, StringComparison.OrdinalIgnoreCase) ||
                    baseName.Equals("LPT" + i, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("The archive contains a reserved Windows filename.");
        }

        return Path.Combine(segments);
    }

    private static void ValidateArchiveEntryType(ZipArchiveEntry entry, bool isDirectory)
    {
        var attributes = entry.ExternalAttributes;
        var unixType = (attributes >> 16) & 0xF000;
        if ((attributes & 0x400) != 0 || unixType == 0xA000)
            throw new InvalidDataException("Symbolic links and reparse-point entries are not accepted.");

        if (isDirectory)
        {
            if (entry.Length != 0 || (unixType != 0 && unixType != 0x4000))
                throw new InvalidDataException("The archive contains an invalid directory entry.");
        }
        else if (unixType != 0 && unixType != 0x8000)
        {
            throw new InvalidDataException("The archive contains a non-regular file entry.");
        }
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An archive entry escapes the import staging directory.");
        return fullPath;
    }

    private static void EnsureNoReparsePointAncestors(string path)
    {
        for (DirectoryInfo? current = new DirectoryInfo(Path.GetFullPath(path));
             current is not null;
             current = current.Parent)
        {
            if (!current.Exists)
                continue;
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Import through a reparse-point directory is blocked.");
        }
    }

    private static async Task<string> ExtractAndHashAsync(
        ZipArchiveEntry entry,
        string destination,
        long maximumEntryBytes,
        long remainingTotalBytes,
        CancellationToken cancellationToken)
    {
        await using var input = entry.Open();
        await using var output = new FileStream(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
                break;
            total = checked(total + read);
            if (total > maximumEntryBytes || total > remainingTotalBytes || total > entry.Length)
                throw new InvalidDataException("Extracted data exceeded the archive's declared or configured size.");
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (total != entry.Length)
            throw new InvalidDataException("An archive entry's extracted size did not match its declared size.");
        await output.FlushAsync(cancellationToken);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

}

public sealed record ModArchiveMetadata(
    string PackageId,
    string Name,
    string Author,
    Uri SourceUri,
    string Licence,
    string Version);

public sealed record ImportedModFile(string RelativePath, long SizeBytes, string Sha256);

public sealed record ModArchiveImportResult(
    string PackageId,
    string PackageDirectory,
    string ManifestPath,
    string ArchiveSha256,
    IReadOnlyList<ImportedModFile> Files);

public sealed record ImportedModManifest(
    int SchemaVersion,
    ModArchiveMetadata Metadata,
    string ArchiveSha256,
    DateTimeOffset ImportedAt,
    CatalogueStatus Status,
    IReadOnlyList<ImportedModFile> Files);

public sealed record ModArchiveImportLimits(
    int MaximumEntries = 2500,
    long MaximumEntryBytes = 268435456,
    long MaximumTotalBytes = 1073741824,
    double MaximumCompressionRatio = 250);
