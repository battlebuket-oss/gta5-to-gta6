using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

public sealed class CatalogueService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public CatalogueDocument Load(string cataloguePath)
    {
        var json = File.ReadAllText(cataloguePath);
        var catalogue = JsonSerializer.Deserialize<CatalogueDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("The mod catalogue is empty or invalid.");

        ValidateCatalogueStructure(catalogue);
        return catalogue;
    }

    public ProfileValidationResult ValidateSelection(
        CatalogueDocument catalogue,
        IEnumerable<string> selectedPackageIds,
        string installedGameBuild)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var selected = new HashSet<string>(selectedPackageIds, StringComparer.OrdinalIgnoreCase);
        var packages = catalogue.Packages.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

        if (!Version.TryParse(installedGameBuild, out var currentBuild))
            errors.Add("The installed GTA V build could not be parsed.");

        foreach (var id in selected)
        {
            if (!packages.TryGetValue(id, out var package))
            {
                errors.Add("Unknown package ID: " + id);
                continue;
            }

            if (package.Status != CatalogueStatus.Tested)
                errors.Add(package.Name + " is not approved for installation. Current status: " + package.Status + ".");

            if (string.IsNullOrWhiteSpace(package.Sha256) ||
                package.Sha256.Length != 64 ||
                !package.Sha256.All(Uri.IsHexDigit))
                errors.Add(package.Name + " has no valid package SHA-256 digest.");

            if (package.SourceUri.Scheme != Uri.UriSchemeHttps)
                errors.Add(package.Name + " does not use a secure HTTPS source.");

            if (!Version.TryParse(package.MinimumGameBuild, out var minimum) ||
                !Version.TryParse(package.MaximumGameBuild, out var maximum))
            {
                errors.Add(package.Name + " has no exact, machine-readable supported game-build range.");
            }
            else if (currentBuild is not null &&
                     (currentBuild < minimum || currentBuild > maximum))
            {
                errors.Add(package.Name + " does not support installed game build " + installedGameBuild + ".");
            }

            foreach (var dependency in package.Dependencies)
            {
                if (!selected.Contains(dependency))
                    errors.Add(package.Name + " requires package " + dependency + ".");
            }

            foreach (var conflict in package.Conflicts)
            {
                if (selected.Contains(conflict))
                    errors.Add(package.Name + " conflicts with package " + conflict + ".");
            }

            if (package.ExternalRequirements is { Count: > 0 })
                warnings.Add(package.Name + " requires external tools/frameworks: " +
                              string.Join(", ", package.ExternalRequirements) + ".");
        }

        var duplicateGroups = selected
            .Where(packages.ContainsKey)
            .Select(id => packages[id])
            .Where(package => !string.IsNullOrWhiteSpace(package.ExclusiveGroup))
            .GroupBy(package => package.ExclusiveGroup!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);

        foreach (var group in duplicateGroups)
            errors.Add("Only one package may be enabled in the '" + group.Key + "' group.");

        return new ProfileValidationResult(errors.Count == 0, errors, warnings);
    }

    private static void ValidateCatalogueStructure(CatalogueDocument catalogue)
    {
        if (catalogue.SchemaVersion != 1)
            throw new InvalidDataException("Unsupported catalogue schema version.");
        if (!string.Equals(catalogue.GameTarget, "GTA V Legacy", StringComparison.Ordinal))
            throw new InvalidDataException("This catalogue does not target GTA V Legacy.");

        EnsureUnique(catalogue.Categories.Select(x => x.Id), "category");
        EnsureUnique(catalogue.Packages.Select(x => x.Id), "package");
        EnsureUnique(catalogue.Profiles.Select(x => x.Id), "profile");

        var categoryIds = new HashSet<string>(catalogue.Categories.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var package in catalogue.Packages)
        {
            if (!PackageIdValidator.IsValid(package.Id))
                throw new InvalidDataException("Invalid package ID in catalogue: " + package.Id);
            if (!categoryIds.Contains(package.Category))
                throw new InvalidDataException("Unknown category for package " + package.Id + ".");
            if (package.SourceUri is null || package.SourceUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("Every package must have an HTTPS author/source URI.");
            if (package.Dependencies.Any(x => !PackageIdValidator.IsValid(x)) ||
                package.Conflicts.Any(x => !PackageIdValidator.IsValid(x)))
                throw new InvalidDataException("A package contains an invalid dependency or conflict ID.");
        }
    }

    private static void EnsureUnique(IEnumerable<string> values, string kind)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (!PackageIdValidator.IsValid(value) || !ids.Add(value))
                throw new InvalidDataException("The catalogue contains an invalid or duplicate " + kind + " ID.");
        }
    }
}
