using System.Security.Cryptography;
using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

public sealed record PackagePreflightReport(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    int FilesToDeploy);

/// <summary>
/// Resolves and verifies a complete set of user-imported packages before passing
/// their declared data files to the transactional copy engine.
/// </summary>
public sealed class PackageDeploymentCoordinator
{
    private readonly TransactionalDeploymentService _deploymentService;
    private readonly string _stagingRoot;

    public PackageDeploymentCoordinator(
        TransactionalDeploymentService? deploymentService = null,
        string? stagingRoot = null)
    {
        _deploymentService = deploymentService ?? new TransactionalDeploymentService();
        _stagingRoot = Path.GetFullPath(stagingRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "deployment-staging"));
    }

    public async Task<PackagePreflightReport> ValidateAsync(
        GameInstallation installation,
        IReadOnlyCollection<ImportedPackage> selectedPackages,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var packageMap = new Dictionary<string, ImportedPackage>(StringComparer.OrdinalIgnoreCase);
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filesToDeploy = 0;

        if (selectedPackages is null || selectedPackages.Count == 0)
            errors.Add("Select at least one imported package.");

        foreach (var imported in selectedPackages ?? Array.Empty<ImportedPackage>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = imported.Manifest;

            if (!PackageIdValidator.IsValid(manifest.Id))
            {
                errors.Add("An imported package has an invalid ID.");
                continue;
            }

            if (!packageMap.TryAdd(manifest.Id, imported))
            {
                errors.Add("The selection contains more than one version of package " + manifest.Id + ".");
                continue;
            }

            if (!string.Equals(manifest.GameTarget, "GTA V Legacy", StringComparison.Ordinal))
                errors.Add(manifest.Name + " does not target GTA V Legacy.");

            if (!Version.TryParse(manifest.MinimumGameBuild, out var minimum) ||
                !Version.TryParse(manifest.MaximumGameBuild, out var maximum) ||
                !Version.TryParse(installation.BuildVersion, out var current))
            {
                errors.Add(manifest.Name + " has an invalid game-build compatibility range.");
            }
            else if (current < minimum || current > maximum)
            {
                errors.Add(manifest.Name + " is not compatible with GTA V build " + installation.BuildVersion + ".");
            }

            foreach (var file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relativeSource;
                string relativeDestination;
                try
                {
                    relativeSource = PackageArchiveService.ValidateArchivePath(file.SourcePath, isDirectory: false);
                    relativeDestination = PackageArchiveService.ValidateArchivePath(file.DestinationPath, isDirectory: false);
                }
                catch (InvalidDataException exception)
                {
                    errors.Add(manifest.Name + ": " + exception.Message);
                    continue;
                }

                if (!destinations.Add(relativeDestination))
                    errors.Add("Multiple selected packages write to the same destination: " + relativeDestination);

                if (Path.GetFileName(relativeDestination).Equals("GTA5.exe", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetExtension(relativeDestination).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                    errors.Add(manifest.Name + " attempts to replace an executable. Executable replacement is blocked.");

                if (string.IsNullOrWhiteSpace(file.Sha256) ||
                    file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                {
                    errors.Add(manifest.Name + " contains a file with an invalid SHA-256 digest.");
                    continue;
                }

                var packageRoot = Path.GetFullPath(imported.PackageRoot);
                var sourcePath = Path.GetFullPath(Path.Combine(packageRoot, relativeSource));
                var rootPrefix = Path.TrimEndingDirectorySeparator(packageRoot) + Path.DirectorySeparatorChar;
                if (!sourcePath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(manifest.Name + " contains a file path outside its package directory.");
                    continue;
                }

                if (!File.Exists(sourcePath))
                {
                    errors.Add(manifest.Name + " is missing an imported file: " + relativeSource);
                    continue;
                }

                var fileInfo = new FileInfo(sourcePath);
                if (fileInfo.Length != file.SizeBytes)
                {
                    errors.Add(manifest.Name + " has an imported file with an unexpected size: " + relativeSource);
                    continue;
                }

                await using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    errors.Add(manifest.Name + " has an imported file that failed SHA-256 verification: " + relativeSource);
                else
                    filesToDeploy++;
            }

            if (string.IsNullOrWhiteSpace(manifest.Licence) ||
                manifest.Licence.Contains("unknown", StringComparison.OrdinalIgnoreCase))
                warnings.Add(manifest.Name + " has an unclear licence statement. Confirm permission before installation.");
        }

        foreach (var imported in packageMap.Values)
        {
            var manifest = imported.Manifest;
            foreach (var dependency in manifest.Dependencies)
            {
                if (!packageMap.ContainsKey(dependency))
                    errors.Add(manifest.Name + " requires package " + dependency + ".");
            }

            foreach (var conflict in manifest.Conflicts)
            {
                if (packageMap.ContainsKey(conflict))
                    errors.Add(manifest.Name + " conflicts with package " + conflict + ".");
            }
        }

        if (filesToDeploy == 0)
            errors.Add("The validated selection contains no deployable files.");

        return new PackagePreflightReport(errors.Count == 0, errors, warnings, filesToDeploy);
    }

    public async Task<DeploymentResult> ApplyAsync(
        GameInstallation installation,
        IReadOnlyCollection<ImportedPackage> selectedPackages,
        CancellationToken cancellationToken = default)
    {
        var report = await ValidateAsync(installation, selectedPackages, cancellationToken);
        if (!report.IsValid)
            throw new InvalidOperationException(
                "Package preflight failed: " + string.Join(" ", report.Errors));

        var stagePath = Path.Combine(_stagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagePath);

        try
        {
            var deploymentFiles = new List<DeploymentFile>();
            foreach (var imported in selectedPackages)
            {
                foreach (var file in imported.Manifest.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sourceRelative = PackageArchiveService.ValidateArchivePath(
                        file.SourcePath, isDirectory: false);
                    var sourceRoot = Path.GetFullPath(imported.PackageRoot);
                    var sourcePath = Path.GetFullPath(Path.Combine(sourceRoot, sourceRelative));
                    var packagePrefix = Path.TrimEndingDirectorySeparator(sourceRoot) + Path.DirectorySeparatorChar;
                    if (!sourcePath.StartsWith(packagePrefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("A package source path escaped its import directory.");

                    var stagedRelative = Path.Combine(imported.Manifest.Id, sourceRelative);
                    var stagedPath = Path.GetFullPath(Path.Combine(stagePath, stagedRelative));
                    var stagingPrefix = Path.TrimEndingDirectorySeparator(stagePath) + Path.DirectorySeparatorChar;
                    if (!stagedPath.StartsWith(stagingPrefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("A package source path escaped the deployment staging directory.");

                    Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
                    File.Copy(sourcePath, stagedPath, overwrite: false);

                    deploymentFiles.Add(new DeploymentFile(
                        imported.Manifest.Id,
                        stagedRelative,
                        file.DestinationPath,
                        file.Sha256));
                }
            }

            return await _deploymentService.ApplyAsync(
                installation.RootPath,
                stagePath,
                deploymentFiles,
                cancellationToken);
        }
        finally
        {
            if (Directory.Exists(stagePath))
                Directory.Delete(stagePath, recursive: true);
        }
    }
}
