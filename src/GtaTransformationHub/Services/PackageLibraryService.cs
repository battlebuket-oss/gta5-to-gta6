using System.Text.Json;
using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

/// <summary>
/// Enumerates locally imported package manifests. Contents are not trusted merely
/// because they appear in this library; preflight re-hashes every declared file.
/// </summary>
public sealed class PackageLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _packagesRoot;

    public PackageLibraryService(string? packagesRoot = null)
    {
        _packagesRoot = Path.GetFullPath(packagesRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "packages"));
    }

    public IReadOnlyList<ImportedPackage> ListImported()
    {
        if (!Directory.Exists(_packagesRoot))
            return Array.Empty<ImportedPackage>();

        var root = Path.TrimEndingDirectorySeparator(_packagesRoot);
        var prefix = root + Path.DirectorySeparatorChar;
        var packages = new List<ImportedPackage>();

        foreach (var manifestPath in Directory.EnumerateFiles(
                     root, PackageArchiveService.ManifestFileName, SearchOption.AllDirectories))
        {
            var fullManifestPath = Path.GetFullPath(manifestPath);
            if (!fullManifestPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var info = new FileInfo(fullManifestPath);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                info.Length <= 0 || info.Length > 1024 * 1024)
                continue;

            try
            {
                var manifest = JsonSerializer.Deserialize<PackageManifest>(
                    File.ReadAllText(fullManifestPath), JsonOptions);
                if (manifest is null || !PackageIdValidator.IsValid(manifest.Id))
                    continue;

                var packageRoot = Path.GetDirectoryName(fullManifestPath)!;
                var packageDirectory = new DirectoryInfo(packageRoot);
                var versionDirectory = packageDirectory.Parent;
                var idDirectory = versionDirectory?.Parent;
                if (idDirectory is null ||
                    !idDirectory.Name.Equals(manifest.Id, StringComparison.OrdinalIgnoreCase))
                    continue;

                packages.Add(new ImportedPackage(
                    manifest,
                    packageRoot,
                    string.Empty,
                    info.LastWriteTimeUtc));
            }
            catch (IOException)
            {
                // A partially written or inaccessible package is omitted from the UI.
            }
            catch (JsonException)
            {
                // A malformed local manifest is not surfaced as an installed package.
            }
        }

        return packages
            .OrderBy(package => package.Manifest.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.Manifest.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
