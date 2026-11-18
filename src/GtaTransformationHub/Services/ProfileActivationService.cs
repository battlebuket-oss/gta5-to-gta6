using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

public sealed record ProfileActivationResult(
    int FilesRemoved,
    int FilesApplied,
    int PackagesAlreadyActive,
    IReadOnlyList<string> TransactionIds);

/// <summary>
/// Reconciles a desired package selection against the Hub's active transaction history.
/// Stale packages are removed first; if the new deployment fails, the removal is rolled back.
/// </summary>
public sealed class ProfileActivationService
{
    private readonly PackageDeploymentCoordinator _deploymentCoordinator;
    private readonly TransactionalRemovalService _removalService;
    private readonly TransactionalDeploymentService _deploymentService;

    public ProfileActivationService(
        PackageDeploymentCoordinator deploymentCoordinator,
        TransactionalRemovalService removalService,
        TransactionalDeploymentService deploymentService)
    {
        _deploymentCoordinator = deploymentCoordinator;
        _removalService = removalService;
        _deploymentService = deploymentService;
    }

    public async Task<ProfileActivationResult> ActivateAsync(
        GameInstallation installation,
        IReadOnlyCollection<ImportedPackage> packageLibrary,
        IReadOnlyCollection<ImportedPackage> desiredPackages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentNullException.ThrowIfNull(packageLibrary);
        ArgumentNullException.ThrowIfNull(desiredPackages);

        var libraryByKey = new Dictionary<string, ImportedPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packageLibrary)
        {
            var key = GetPackageKey(package);
            if (!libraryByKey.TryAdd(key, package))
                throw new InvalidOperationException(
                    "The local library contains multiple archives for the same package ID and version. Remove the duplicate archive or publish it as a new version.");
        }

        var desiredByKey = new Dictionary<string, ImportedPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in desiredPackages)
        {
            var key = GetPackageKey(package);
            if (!libraryByKey.TryGetValue(key, out var stored) ||
                !stored.PackageRoot.Equals(package.PackageRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "The selected profile references a package that is not in the local package library: " + package.Manifest.Id);

            if (!desiredByKey.TryAdd(key, package))
                throw new InvalidOperationException(
                    "The selected profile contains duplicate package ID/version entries.");
        }

        if (desiredPackages.Count > 0)
        {
            var preflight = await _deploymentCoordinator.ValidateAsync(
                installation, desiredPackages, cancellationToken);
            if (!preflight.IsValid)
                throw new InvalidOperationException(
                    "Profile preflight failed: " + string.Join(" ", preflight.Errors));
        }

        var activeKeys = await _removalService.GetActivePackageIdentitiesAsync(cancellationToken);
        var missingActivePackages = activeKeys
            .Where(key => !libraryByKey.ContainsKey(key))
            .ToArray();

        if (missingActivePackages.Length > 0)
            throw new InvalidOperationException(
                "The Hub has active packages whose exact versions are not present in the local library. Re-import those exact archives before changing profiles: " +
                string.Join(", ", missingActivePackages.Select(DescribePackageKey)));

        var desiredKeys = new HashSet<string>(desiredByKey.Keys, StringComparer.OrdinalIgnoreCase);
        var stalePackages = packageLibrary
            .Where(package => activeKeys.Contains(GetPackageKey(package)) &&
                              !desiredKeys.Contains(GetPackageKey(package)))
            .ToArray();

        var desiredAlreadyActive = desiredPackages
            .Where(package => activeKeys.Contains(GetPackageKey(package)))
            .ToArray();

        var packagesToDeploy = desiredPackages
            .Where(package => !activeKeys.Contains(GetPackageKey(package)))
            .ToDictionary(GetPackageKey, package => package, StringComparer.OrdinalIgnoreCase);

        foreach (var package in desiredAlreadyActive)
        {
            var intact = await _removalService.IsPackageInstalledAndIntactAsync(
                installation, package, cancellationToken);
            if (!intact)
                packagesToDeploy[GetPackageKey(package)] = package;
        }

        // If a stale package shares a destination with a desired package, redeploy the
        // desired package after removal so the selected profile owns the final file.
        var staleDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in stalePackages)
        {
            foreach (var file in stale.Manifest.Files)
            {
                staleDestinations.Add(PackageArchiveService.ValidateArchivePath(
                    file.DestinationPath, isDirectory: false));
            }
        }

        foreach (var desired in desiredPackages)
        {
            if (desired.Manifest.Files.Any(file =>
                    staleDestinations.Contains(PackageArchiveService.ValidateArchivePath(
                        file.DestinationPath, isDirectory: false))))
                packagesToDeploy[GetPackageKey(desired)] = desired;
        }

        DeploymentResult? removalResult = null;
        if (stalePackages.Length > 0)
        {
            removalResult = await _removalService.RemovePackagesAsync(
                installation, stalePackages, cancellationToken);
        }

        DeploymentResult? deploymentResult = null;
        try
        {
            var deploymentSubset = packagesToDeploy.Values.ToArray();
            if (deploymentSubset.Length > 0)
            {
                deploymentResult = await _deploymentCoordinator.ApplyAsync(
                    installation,
                    desiredPackages,
                    deploymentSubset,
                    cancellationToken);
            }
        }
        catch (Exception deploymentError)
        {
            if (removalResult is not null && !string.IsNullOrWhiteSpace(removalResult.TransactionId))
            {
                try
                {
                    await _deploymentService.RollbackLatestAsync(
                        removalResult.TransactionId, CancellationToken.None);
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException(
                        "Profile activation failed and the previous profile could not be fully restored.",
                        deploymentError,
                        rollbackError);
                }
            }

            throw;
        }

        var transactions = new List<string>();
        if (removalResult is not null && !string.IsNullOrWhiteSpace(removalResult.TransactionId))
            transactions.Add(removalResult.TransactionId);
        if (deploymentResult is not null && !string.IsNullOrWhiteSpace(deploymentResult.TransactionId))
            transactions.Add(deploymentResult.TransactionId);

        return new ProfileActivationResult(
            removalResult?.FilesApplied ?? 0,
            deploymentResult?.FilesApplied ?? 0,
            desiredAlreadyActive.Length - packagesToDeploy.Keys.Count(key => desiredKeys.Contains(key)),
            transactions);
    }

    private static string GetPackageKey(ImportedPackage package) =>
        package.Manifest.Id + "\u001f" + package.Manifest.Version;

    private static string DescribePackageKey(string key)
    {
        var parts = key.Split('\u001f', 2);
        return parts.Length == 2 ? parts[0] + "@" + parts[1] : key;
    }
}
