using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

/// <summary>
/// Activates a saved profile only when every package is explicitly Tested in the
/// curated catalogue, its imported archive and every inventoried file verify,
/// and an adapter supplies a bounded deployment plan. Unknown package formats
/// remain blocked; this service never guesses archive destinations.
/// </summary>
public sealed class ProfileActivationService
{
    private readonly CatalogueService _catalogueService;
    private readonly ImportedModLibraryService _libraryService;
    private readonly TransactionalDeploymentService _deploymentService;

    public ProfileActivationService(
        CatalogueService? catalogueService = null,
        ImportedModLibraryService? libraryService = null,
        TransactionalDeploymentService? deploymentService = null)
    {
        _catalogueService = catalogueService ?? new CatalogueService();
        _libraryService = libraryService ?? new ImportedModLibraryService();
        _deploymentService = deploymentService ?? new TransactionalDeploymentService();
    }

    public async Task<ProfileActivationResult> ActivateAsync(
        string gameRoot,
        string installedGameBuild,
        string packageLibraryRoot,
        CatalogueDocument catalogue,
        IEnumerable<string> selectedPackageIds,
        IReadOnlyDictionary<string, IReadOnlyList<DeploymentFile>> adapterPlans,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(selectedPackageIds);
        ArgumentNullException.ThrowIfNull(adapterPlans);

        var selected = selectedPackageIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selected.Length == 0)
            return Block("The profile is empty. Select at least one tested package.");

        var readiness = _catalogueService.ValidateSelection(
            catalogue, selected, installedGameBuild);
        if (!readiness.IsValid)
            return new ProfileActivationResult(false, null, readiness.Errors, readiness.Warnings);

        var libraryRoot = Path.GetFullPath(packageLibraryRoot);
        if (!Directory.Exists(libraryRoot))
            return Block("The local package library does not exist.");

        var packages = catalogue.Packages.ToDictionary(
            package => package.Id, StringComparer.OrdinalIgnoreCase);
        var imported = await _libraryService.LoadAsync(libraryRoot, cancellationToken);
        var importedById = imported
            .GroupBy(entry => entry.PackageId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(),
                StringComparer.OrdinalIgnoreCase);

        var deploymentFiles = new List<DeploymentFile>();
        var errors = new List<string>();
        var warnings = new List<string>();

        foreach (var packageId in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!packages.TryGetValue(packageId, out var package))
            {
                errors.Add("Unknown package: " + packageId);
                continue;
            }

            if (package.Status != CatalogueStatus.Tested)
            {
                errors.Add(package.Name + " is not marked Tested in the curated catalogue.");
                continue;
            }

            if (!importedById.TryGetValue(package.Id, out var localPackage))
            {
                errors.Add(package.Name + " is not present exactly once in the local package library.");
                continue;
            }

            if (!localPackage.ManifestValid ||
                !string.Equals(localPackage.OriginalArchiveSha256, package.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(package.Name + " does not match its approved source archive or has an invalid local inventory.");
                continue;
            }

            var verification = await _libraryService.VerifyAsync(
                libraryRoot, localPackage.PackageDirectory, cancellationToken);
            if (!verification.IsValid)
            {
                errors.AddRange(verification.Errors.Select(error => package.Name + ": " + error));
                continue;
            }

            if (!adapterPlans.TryGetValue(package.Id, out var plan) || plan.Count == 0)
            {
                errors.Add(package.Name + " has no explicit deployment adapter plan. No destination paths will be guessed.");
                continue;
            }

            var packageDirectory = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(localPackage.PackageDirectory));
            var packagePrefix = packageDirectory + Path.DirectorySeparatorChar;

            foreach (var file in plan)
            {
                if (!string.Equals(file.PackageId, package.Id, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(package.Name + " has an adapter entry assigned to a different package.");
                    continue;
                }

                var source = Path.GetFullPath(Path.Combine(libraryRoot, file.SourcePath));
                if (!source.StartsWith(packagePrefix, StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(source))
                {
                    errors.Add(package.Name + " has a missing or out-of-package adapter source: " + file.SourcePath);
                    continue;
                }

                deploymentFiles.Add(file);
            }
        }

        if (errors.Count > 0)
            return new ProfileActivationResult(false, null, errors, warnings);

        try
        {
            var deployment = await _deploymentService.ApplyAsync(
                gameRoot, libraryRoot, deploymentFiles, cancellationToken);
            return new ProfileActivationResult(
                true, deployment, Array.Empty<string>(), warnings);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException)
        {
            return Block("Profile activation failed safely: " + exception.Message);
        }
    }

    private static ProfileActivationResult Block(string error) =>
        new(false, null, new[] { error }, Array.Empty<string>());
}

public sealed record ProfileActivationResult(
    bool Success,
    DeploymentResult? Deployment,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
