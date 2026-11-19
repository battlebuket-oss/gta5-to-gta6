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
        if (HasReparsePointInPath(root, root))
            return Array.Empty<ImportedPackage>();

        var prefix = root + Path.DirectorySeparatorChar;
        var packages = new List<ImportedPackage>();

        try
        {
            foreach (var manifestPath in Directory.EnumerateFiles(
                         root, PackageArchiveService.ManifestFileName, SearchOption.AllDirectories))
            {
                try
                {
                    var fullManifestPath = Path.GetFullPath(manifestPath);
                    if (!fullManifestPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var packageRoot = Path.GetDirectoryName(fullManifestPath)!;
                    if (HasReparsePointInPath(root, packageRoot))
                        continue;

                    var info = new FileInfo(fullManifestPath);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                        info.Length <= 0 || info.Length > 1024 * 1024)
                        continue;

                    var manifest = JsonSerializer.Deserialize<PackageManifest>(
                        File.ReadAllText(fullManifestPath), JsonOptions);

                    if (manifest is null ||
                        !PackageIdValidator.IsValid(manifest.Id) ||
                        string.IsNullOrWhiteSpace(manifest.Name) ||
                        string.IsNullOrWhiteSpace(manifest.Author) ||
                        manifest.Files is null ||
                        manifest.Dependencies is null ||
                        manifest.Conflicts is null)
                        continue;

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
                        new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)));
                }
                catch (Exception exception) when (
                    exception is IOException ||
                    exception is UnauthorizedAccessException ||
                    exception is JsonException ||
                    exception is ArgumentException)
                {
                    // A partial, inaccessible or malformed package is omitted from the UI.
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException || exception is UnauthorizedAccessException)
        {
            // Keep any packages already enumerated; inaccessible folders are not trusted.
        }

        return packages
            .OrderBy(package => package.Manifest.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.Manifest.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool HasReparsePointInPath(string root, string path)
    {
        var rootFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var currentPath = Path.GetFullPath(path);

        while (currentPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(currentPath) &&
                (File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0)
                return true;

            if (currentPath.Equals(rootFullPath, StringComparison.OrdinalIgnoreCase))
                return false;

            var parent = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrEmpty(parent))
                return true;

            currentPath = parent;
        }

        return true;
    }
}
