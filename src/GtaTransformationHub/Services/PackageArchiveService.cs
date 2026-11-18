using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

/// <summary>
/// Imports ZIP packages into an isolated local store. Only files explicitly listed
/// in gta-hub-manifest.json are extracted. No archive executable or installer is run.
/// </summary>
public sealed class PackageArchiveService
{
    public const string ManifestFileName = "gta-hub-manifest.json";
    private const long MaximumArchiveBytes = 2L * 1024 * 1024 * 1024;
    private const long MaximumExpandedBytes = 4L * 1024 * 1024 * 1024;
    private const long MaximumFileBytes = 512L * 1024 * 1024;
    private const int MaximumEntries = 10000;
    private const int MaximumManifestBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _packagesRoot;
    private readonly PackageSignatureService _signatureService;

    public PackageArchiveService(
        string? packagesRoot = null,
        PackageSignatureService? signatureService = null)
    {
        _signatureService = signatureService ?? new PackageSignatureService();
        _packagesRoot = Path.GetFullPath(packagesRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "packages"));
    }

    public async Task<ImportedPackage> ImportAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
            throw new ArgumentException("Select a package ZIP archive.", nameof(archivePath));

        var fullArchivePath = Path.GetFullPath(archivePath);
        var archiveInfo = new FileInfo(fullArchivePath);
        if (!archiveInfo.Exists)
            throw new FileNotFoundException("The selected package archive was not found.", fullArchivePath);
        if (archiveInfo.Length <= 0 || archiveInfo.Length > MaximumArchiveBytes)
            throw new InvalidDataException("The package archive is empty or exceeds the 2 GB import limit.");

        await using var archiveStream = new FileStream(
            fullArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var archiveHash = Convert.ToHexString(
            await SHA256.HashDataAsync(archiveStream, cancellationToken));
        archiveStream.Position = 0;

        using var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count == 0 || zip.Entries.Count > MaximumEntries)
            throw new InvalidDataException("The package archive has no entries or exceeds the entry-count limit.");

        var filesByPath = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        long expandedBytes = 0;
        ZipArchiveEntry? manifestEntry = null;

        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                              entry.FullName.EndsWith("\\", StringComparison.Ordinal);
            var safePath = ValidateArchivePath(entry.FullName, isDirectory);
            RejectLinkEntries(entry);

            if (isDirectory)
                continue;

            if (entry.Length < 0 || entry.Length > MaximumFileBytes)
                throw new InvalidDataException("A package entry exceeds the 512 MB per-file limit.");
            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > MaximumExpandedBytes)
                throw new InvalidDataException("The package archive exceeds the 4 GB expanded-size limit.");

            if (!filesByPath.TryAdd(safePath, entry))
                throw new InvalidDataException("The package archive contains duplicate paths: " + safePath);

            if (safePath.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase))
                manifestEntry = entry;
        }

        if (manifestEntry is null || manifestEntry.Length <= 0 ||
            manifestEntry.Length > MaximumManifestBytes)
            throw new InvalidDataException("The archive must contain a root-level gta-hub-manifest.json under 1 MB.");

        var manifestBytes = await ReadEntryBoundedAsync(
            manifestEntry, MaximumManifestBytes, cancellationToken);
        var manifest = JsonSerializer.Deserialize<PackageManifest>(manifestBytes, JsonOptions)
            ?? throw new InvalidDataException("The package manifest is empty or invalid.");

        ValidateManifest(manifest);
        var signatureVerification = await _signatureService.VerifyAsync(manifest, cancellationToken);
        if (signatureVerification.Status == PackageSignatureStatus.Invalid)
            throw new InvalidDataException(signatureVerification.Message);
        var signatureStatus = signatureVerification.Status.ToString();

        var listedFiles = new Dictionary<string, PackageManifestFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            var sourcePath = ValidateArchivePath(file.SourcePath, isDirectory: false);
            _ = ValidateArchivePath(file.DestinationPath, isDirectory: false);

            if (sourcePath.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The package manifest cannot list itself as a deployed file.");
            if (!listedFiles.TryAdd(sourcePath, file))
                throw new InvalidDataException("The manifest contains duplicate source paths.");
            if (!filesByPath.ContainsKey(sourcePath))
                throw new InvalidDataException("A manifest file is missing from the archive: " + sourcePath);

            var archiveEntry = filesByPath[sourcePath];
            if (archiveEntry.Length != file.SizeBytes)
                throw new InvalidDataException(
                    "The manifest file size does not match the ZIP directory entry: " + sourcePath);
        }

        var unlisted = filesByPath.Keys.Where(path =>
            !path.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase) &&
            !listedFiles.ContainsKey(path)).ToArray();
        if (unlisted.Length > 0)
            throw new InvalidDataException(
                "The archive contains files that are not declared in its manifest: " + string.Join(", ", unlisted.Take(5)));

        var packageHashKey = archiveHash[..16].ToLowerInvariant();
        var packageVersion = NormalizePathSegment(manifest.Version);
        var packageDestination = Path.Combine(
            _packagesRoot, manifest.Id, packageVersion, packageHashKey);

        if (Directory.Exists(packageDestination))
            return new ImportedPackage(manifest, packageDestination, archiveHash, DateTimeOffset.UtcNow, signatureStatus);

        Directory.CreateDirectory(_packagesRoot);
        var stagingPath = Path.Combine(_packagesRoot, ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingPath);

        try
        {
            foreach (var item in listedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = ResolveContainedPath(stagingPath, item.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                await CopyEntryBoundedAsync(
                    filesByPath[item.Key], destination, item.Value.SizeBytes, cancellationToken);

                var info = new FileInfo(destination);
                if (info.Length != item.Value.SizeBytes)
                    throw new InvalidDataException("A package file has an unexpected size: " + item.Key);

                await using var verifyStream = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read);
                var actualHash = Convert.ToHexString(
                    await SHA256.HashDataAsync(verifyStream, cancellationToken));
                if (!actualHash.Equals(item.Value.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("A package file failed SHA-256 verification: " + item.Key);
            }

            var manifestPath = Path.Combine(stagingPath, ManifestFileName);
            await using (var stream = new FileStream(
                manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(packageDestination)!);
            Directory.Move(stagingPath, packageDestination);
            return new ImportedPackage(manifest, packageDestination, archiveHash, DateTimeOffset.UtcNow);
        }
        catch
        {
            if (Directory.Exists(stagingPath))
                Directory.Delete(stagingPath, recursive: true);
            throw;
        }
    }


    private static async Task<byte[]> ReadEntryBoundedAsync(
        ZipArchiveEntry entry,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (entry.Length < 0 || entry.Length > maximumBytes)
            throw new InvalidDataException("The manifest exceeds its decompression limit.");

        await using var input = entry.Open();
        using var output = new MemoryStream((int)entry.Length);
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
                break;

            if (output.Length + read > maximumBytes)
                throw new InvalidDataException("The manifest expanded beyond its decompression limit.");

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static async Task CopyEntryBoundedAsync(
        ZipArchiveEntry entry,
        string destination,
        long expectedSize,
        CancellationToken cancellationToken)
    {
        if (expectedSize < 0 || expectedSize > MaximumFileBytes || entry.Length != expectedSize)
            throw new InvalidDataException("A package entry has an invalid or unexpected expanded size.");

        await using var input = entry.Open();
        await using var output = new FileStream(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.WriteThrough);

        var buffer = new byte[81920];
        long copied = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
                break;

            copied = checked(copied + read);
            if (copied > expectedSize || copied > MaximumFileBytes)
                throw new InvalidDataException("A package entry expanded beyond its declared size.");

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (copied != expectedSize)
            throw new InvalidDataException("A package entry expanded to a size different from its declaration.");

        await output.FlushAsync(cancellationToken);
    }

    private static void ValidateManifest(PackageManifest manifest)
    {
        if (manifest.SchemaVersion != 1)
            throw new InvalidDataException("Unsupported package manifest schema.");
        if (!PackageIdValidator.IsValid(manifest.Id))
            throw new InvalidDataException("The package manifest has an invalid package ID.");
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 160 ||
            string.IsNullOrWhiteSpace(manifest.Author) || manifest.Author.Length > 160)
            throw new InvalidDataException("The package name and author are required.");
        if (!Uri.TryCreate(manifest.SourceUri, UriKind.Absolute, out var source) ||
            source.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("The package source must be an absolute HTTPS URL.");
        if (string.IsNullOrWhiteSpace(manifest.Licence) || manifest.Licence.Length > 500)
            throw new InvalidDataException("The package licence/permission statement is required.");
        if (!string.Equals(manifest.GameTarget, "GTA V Legacy", StringComparison.Ordinal))
            throw new InvalidDataException("Only GTA V Legacy packages can be imported.");
        if (!Version.TryParse(manifest.Version, out _))
            throw new InvalidDataException("The package version must be numeric and machine-readable.");
        if (!Version.TryParse(manifest.MinimumGameBuild, out var minimum) ||
            !Version.TryParse(manifest.MaximumGameBuild, out var maximum) || minimum > maximum)
            throw new InvalidDataException("The package must declare a valid supported game-build range.");
        if (manifest.Files is null || manifest.Files.Count == 0)
            throw new InvalidDataException("The package manifest must declare at least one file.");
        if (manifest.Dependencies is null || manifest.Conflicts is null)
            throw new InvalidDataException("Dependencies and conflicts must be declared, even when empty.");

        foreach (var id in manifest.Dependencies.Concat(manifest.Conflicts))
        {
            if (!PackageIdValidator.IsValid(id))
                throw new InvalidDataException("The package manifest contains an invalid dependency or conflict ID.");
        }

        foreach (var file in manifest.Files)
        {
            if (file.SizeBytes < 0 || file.SizeBytes > MaximumFileBytes)
                throw new InvalidDataException("A manifest file size is outside the supported limit.");
            if (file.Sha256 is null || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Every manifest file must declare a SHA-256 digest.");
        }
    }

    private static void RejectLinkEntries(ZipArchiveEntry entry)
    {
        var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
        var dosAttributes = entry.ExternalAttributes & 0xFFFF;
        if (unixType == 0xA000 ||
            (dosAttributes & (int)FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Symbolic links and reparse-point entries are not allowed in packages.");
    }

    internal static string ValidateArchivePath(string path, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0)
            throw new InvalidDataException("The archive contains an empty or invalid path.");

        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.StartsWith("//", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalized) ||
            (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':'))
            throw new InvalidDataException("Absolute archive paths are not allowed.");

        var segments = normalized.Split('/');
        var effectiveSegments = isDirectory ? segments[..^1] : segments;
        if (effectiveSegments.Length == 0 ||
            effectiveSegments.Any(segment =>
                segment.Length == 0 || segment == "." || segment == ".." ||
                segment.Contains(':') || segment.EndsWith(".", StringComparison.Ordinal) ||
                segment.EndsWith(" ", StringComparison.Ordinal) ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                IsReservedWindowsName(segment)))
            throw new InvalidDataException("The archive contains an unsafe path segment.");

        return string.Join(Path.DirectorySeparatorChar, effectiveSegments);
    }

    private static bool IsReservedWindowsName(string segment)
    {
        var name = segment.Split('.')[0];
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               (name.Length == 4 &&
                (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                 name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                name[3] is >= '1' and <= '9');
    }

    private static string NormalizePathSegment(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0 || normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            normalized.Contains('/') || normalized.Contains('\\') ||
            normalized == "." || normalized == "..")
            throw new InvalidDataException("The package version cannot be used as a storage directory name.");
        return normalized;
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An archive path escapes its staging directory.");
        return full;
    }
}
