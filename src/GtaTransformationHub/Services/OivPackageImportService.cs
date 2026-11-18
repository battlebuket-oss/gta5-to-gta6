using System.IO.Compression;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

/// <summary>
/// Imports the safe loose-file subset of OpenIV OIV 2.2 packages.
/// Only direct add operations targeting loose files are supported. RPF archive
/// edits, delete operations, patches and script operations are rejected.
/// </summary>
public sealed class OivPackageImportService
{
    private const long MaximumArchiveBytes = 2L * 1024 * 1024 * 1024;
    private const long MaximumExpandedBytes = 4L * 1024 * 1024 * 1024;
    private const long MaximumFileBytes = 512L * 1024 * 1024;
    private const int MaximumEntries = 10000;
    private const int MaximumAssemblyBytes = 1024 * 1024;
    private readonly string _packagesRoot;

    public OivPackageImportService(string? packagesRoot = null)
    {
        _packagesRoot = Path.GetFullPath(packagesRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "packages"));
    }

    public async Task<ImportedPackage> ImportAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(archivePath);
        var info = new FileInfo(fullPath);
        if (!info.Exists || info.Length <= 0 || info.Length > MaximumArchiveBytes)
            throw new InvalidDataException("The OIV file is missing, empty or larger than 2 GB.");

        await using var archiveStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var archiveHash = Convert.ToHexString(await SHA256.HashDataAsync(archiveStream, cancellationToken));
        archiveStream.Position = 0;
        using var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count == 0 || zip.Entries.Count > MaximumEntries)
            throw new InvalidDataException("The OIV archive has an invalid entry count.");

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        long expandedBytes = 0;
        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                              entry.FullName.EndsWith("\\", StringComparison.Ordinal);
            var safeName = PackageArchiveService.ValidateArchivePath(entry.FullName, isDirectory);
            RejectLinkEntry(entry);
            if (isDirectory)
                continue;
            if (entry.Length < 0 || entry.Length > MaximumFileBytes)
                throw new InvalidDataException("An OIV entry exceeds the per-file limit.");
            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > MaximumExpandedBytes)
                throw new InvalidDataException("The OIV archive exceeds the expanded-size limit.");
            if (!entries.TryAdd(safeName.Replace('\\', '/'), entry))
                throw new InvalidDataException("The OIV archive contains duplicate paths.");
        }

        if (!entries.TryGetValue("assembly.xml", out var assemblyEntry) ||
            assemblyEntry.Length <= 0 || assemblyEntry.Length > MaximumAssemblyBytes)
            throw new InvalidDataException("The OIV archive must contain a root assembly.xml under 1 MB.");

        var assembly = LoadSecureXml(await ReadBoundedAsync(assemblyEntry, MaximumAssemblyBytes, cancellationToken));
        var package = assembly.Root;
        if (package is null ||
            !package.Name.LocalName.Equals("package", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals((string?)package.Attribute("version"), "2.2", StringComparison.Ordinal) ||
            !string.Equals((string?)package.Attribute("target"), "Five", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only OpenIV OIV format 2.2 packages targeting Grand Theft Auto V are supported.");

        var metadata = package.Element("metadata") ?? throw new InvalidDataException("The OIV package is missing metadata.");
        var content = package.Element("content") ?? throw new InvalidDataException("The OIV package is missing content instructions.");
        var commands = content.Elements().ToArray();
        if (commands.Length == 0 || commands.Any(element =>
                !element.Name.LocalName.Equals("add", StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("This OIV uses RPF archive edits, delete, patch or other operations. Only direct loose-file add operations are supported; no partial installation was performed.");

        var name = RequiredText(metadata, "name", "OIV package name");
        var authorNode = metadata.Element("author") ?? throw new InvalidDataException("The OIV package is missing its author.");
        var author = RequiredText(authorNode, "displayName", "OIV author");
        var versionNode = metadata.Element("version") ?? throw new InvalidDataException("The OIV package is missing its version.");
        if (!int.TryParse(versionNode.Element("major")?.Value, out var major) ||
            !int.TryParse(versionNode.Element("minor")?.Value, out var minor) || major < 0 || minor < 0)
            throw new InvalidDataException("The OIV package has an invalid numeric version.");
        var version = $"{major}.{minor}.0";
        var licenceText = metadata.Element("licence")?.Value.Trim();
        var licence = string.IsNullOrWhiteSpace(licenceText)
            ? "Not declared in the OIV package; author permission must be checked before installation."
            : licenceText.Length > 500 ? licenceText[..500] : licenceText;
        var sourceUri = GetHttpsAuthorUri(authorNode) ?? new Uri("https://openiv.com/");
        var rawId = (string?)package.Attribute("id") ?? name;
        var guidBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawId));
        var packageGuid = Guid.TryParse(rawId, out var parsed) ? parsed : new Guid(guidBytes.AsSpan(0, 16));
        var packageId = "oiv-" + packageGuid.ToString("N");

        var deployable = new List<(ZipArchiveEntry Entry, string DestinationPath)>();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (command.HasElements)
                throw new InvalidDataException("An OIV add instruction contains nested XML elements.");
            var sourceAttribute = (string?)command.Attribute("source");
            if (string.IsNullOrWhiteSpace(sourceAttribute))
                throw new InvalidDataException("An OIV add instruction is missing its source attribute.");
            var source = PackageArchiveService.ValidateArchivePath(sourceAttribute, false).Replace('\\', '/');
            var destination = PackageArchiveService.ValidateArchivePath(command.Value.Trim(), false).Replace('\\', '/');
            ProtectedGamePathPolicy.ValidateDestination(destination);
            if (!destinations.Add(destination))
                throw new InvalidDataException("The OIV package writes more than one payload to destination " + destination);
            var candidates = new[] { "content/" + source, source };
            var matches = candidates.Where(entries.ContainsKey).Select(key => entries[key]).Distinct().ToArray();
            if (matches.Length != 1)
            {
                var suffix = "/" + source;
                matches = entries.Where(pair => pair.Key.StartsWith("content/", StringComparison.OrdinalIgnoreCase) &&
                    pair.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Value).ToArray();
            }
            if (matches.Length != 1)
                throw new InvalidDataException("An OIV add instruction references a missing or ambiguous payload: " + source);
            deployable.Add((matches[0], destination));
        }

        var packageRoot = Path.Combine(_packagesRoot, packageId, version, archiveHash[..16].ToLowerInvariant());
        if (Directory.Exists(packageRoot))
        {
            var existingManifestPath = Path.Combine(packageRoot, PackageArchiveService.ManifestFileName);
            var existingManifest = JsonSerializer.Deserialize<PackageManifest>(await File.ReadAllTextAsync(existingManifestPath, cancellationToken))
                ?? throw new InvalidDataException("An existing imported OIV manifest is invalid.");
            return new ImportedPackage(existingManifest, packageRoot, archiveHash, DateTimeOffset.UtcNow,
                "Unsigned OIV (publisher identity not authenticated)");
        }

        Directory.CreateDirectory(_packagesRoot);
        var stagingRoot = Path.Combine(_packagesRoot, ".staging-oiv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        try
        {
            var manifestFiles = new List<PackageManifestFile>();
            for (var index = 0; index < deployable.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = deployable[index];
                var storedRelative = Path.Combine("payload", index.ToString("D4") + "-" + Path.GetFileName(item.DestinationPath));
                var outputPath = Path.GetFullPath(Path.Combine(stagingRoot, storedRelative));
                var prefix = Path.TrimEndingDirectorySeparator(stagingRoot) + Path.DirectorySeparatorChar;
                if (!outputPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("An OIV payload path escaped the import directory.");
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await CopyEntryAsync(item.Entry, outputPath, cancellationToken);
                var length = new FileInfo(outputPath).Length;
                await using var verify = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(verify, cancellationToken));
                manifestFiles.Add(new PackageManifestFile(storedRelative, item.DestinationPath, hash, length));
            }

            var manifest = new PackageManifest(
                1, packageId, name, author, sourceUri.AbsoluteUri, licence, version,
                "GTA V Legacy", "0.0.0.0", "99999.0.0.0",
                Array.Empty<string>(), Array.Empty<string>(), manifestFiles,
                ImpactAreas: Array.Empty<string>(), CompatibilityVerified: false, PackageFormat: "OIV-2.2");

            await File.WriteAllTextAsync(
                Path.Combine(stagingRoot, PackageArchiveService.ManifestFileName),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(packageRoot)!);
            Directory.Move(stagingRoot, packageRoot);
            return new ImportedPackage(manifest, packageRoot, archiveHash, DateTimeOffset.UtcNow,
                "Unsigned OIV (publisher identity not authenticated; compatibility not verified)");
        }
        catch
        {
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
            throw;
        }
    }

    private static XDocument LoadSecureXml(byte[] bytes)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MaximumAssemblyBytes, IgnoreComments = true
        };
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static string RequiredText(XElement parent, string elementName, string description)
    {
        var value = parent.Element(elementName)?.Value.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 500)
            throw new InvalidDataException("The " + description + " is missing or too long.");
        return value;
    }

    private static Uri? GetHttpsAuthorUri(XElement author)
    {
        foreach (var name in new[] { "web", "actionLink" })
        {
            if (Uri.TryCreate(author.Element(name)?.Value.Trim(), UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps)
                return uri;
        }
        return null;
    }

    private static void RejectLinkEntry(ZipArchiveEntry entry)
    {
        var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
        var dosAttributes = entry.ExternalAttributes & 0xFFFF;
        if (unixType == 0xA000 || (dosAttributes & (int)FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Symbolic links and reparse-point entries are not allowed in OIV packages.");
    }

    private static async Task<byte[]> ReadBoundedAsync(ZipArchiveEntry entry, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var input = entry.Open();
        using var output = new MemoryStream((int)entry.Length);
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > maximumBytes)
                throw new InvalidDataException("The OIV assembly.xml expanded beyond its size limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static async Task CopyEntryAsync(ZipArchiveEntry entry, string destination, CancellationToken cancellationToken)
    {
        if (entry.Length < 0 || entry.Length > MaximumFileBytes)
            throw new InvalidDataException("The OIV payload exceeds the per-file size limit.");
        await using var input = entry.Open();
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
        var buffer = new byte[81920];
        long copied = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) break;
            copied = checked(copied + read);
            if (copied > entry.Length || copied > MaximumFileBytes)
                throw new InvalidDataException("An OIV payload expanded beyond its declared size.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (copied != entry.Length)
            throw new InvalidDataException("An OIV payload size did not match the ZIP directory entry.");
        await output.FlushAsync(cancellationToken);
    }
}
