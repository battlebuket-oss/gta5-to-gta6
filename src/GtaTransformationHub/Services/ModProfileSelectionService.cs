using System;
using System.Collections.Generic;
using System.Linq;
using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

public sealed record ProfileSelectionResult(
    bool Success,
    IReadOnlySet<string> SelectedPackageIds,
    string? Error,
    IReadOnlyList<string> AddedDependencies,
    IReadOnlyList<string> RemovedDependents);

/// <summary>
/// Applies ON/OFF changes to the desired profile while keeping declared dependencies
/// and conflicts coherent. This service never deploys files into GTA V.
/// </summary>
public sealed class ModProfileSelectionService
{
    public ProfileSelectionResult SetEnabled(
        CatalogueDocument catalogue,
        IEnumerable<string> currentSelection,
        string packageId,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(currentSelection);

        var packages = catalogue.Packages.ToDictionary(
            package => package.Id, StringComparer.OrdinalIgnoreCase);
        var selected = new HashSet<string>(
            currentSelection.Where(packages.ContainsKey),
            StringComparer.OrdinalIgnoreCase);

        if (!packages.TryGetValue(packageId, out var target))
            return Failure(selected, "The selected package is not present in the current catalogue.");

        if (!enabled)
        {
            selected.Remove(target.Id);
            var removedDependents = new List<string>();
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var selectedId in selected.ToArray())
                {
                    var selectedPackage = packages[selectedId];
                    if (selectedPackage.Dependencies.Any(
                        dependency => !selected.Contains(dependency)))
                    {
                        selected.Remove(selectedId);
                        removedDependents.Add(selectedId);
                        changed = true;
                    }
                }
            }

            return new ProfileSelectionResult(
                true, selected, null, Array.Empty<string>(), removedDependents);
        }

        if (target.Status is CatalogueStatus.Blocked or CatalogueStatus.Deprecated)
            return Failure(selected,
                target.Name + " is " + target.Status + " and cannot be selected.");

        var original = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var added = new List<string>();

        if (!TryAddWithDependencies(target, packages, selected, visiting, added, out var error))
            return Failure(original, error!);

        return new ProfileSelectionResult(
            true, selected, null, added, Array.Empty<string>());
    }

    private static bool TryAddWithDependencies(
        ModPackage package,
        IReadOnlyDictionary<string, ModPackage> packages,
        HashSet<string> selected,
        HashSet<string> visiting,
        List<string> added,
        out string? error)
    {
        error = null;
        if (selected.Contains(package.Id))
            return true;

        if (package.Status is CatalogueStatus.Blocked or CatalogueStatus.Deprecated)
        {
            error = package.Name + " is " + package.Status + " and cannot be selected.";
            return false;
        }

        if (!visiting.Add(package.Id))
        {
            error = "A dependency cycle was detected at " + package.Name + ".";
            return false;
        }

        foreach (var dependencyId in package.Dependencies)
        {
            if (!packages.TryGetValue(dependencyId, out var dependency))
            {
                error = package.Name + " requires missing package " + dependencyId + ".";
                visiting.Remove(package.Id);
                return false;
            }

            if (!TryAddWithDependencies(dependency, packages, selected, visiting, added, out error))
            {
                visiting.Remove(package.Id);
                return false;
            }
        }

        foreach (var selectedId in selected)
        {
            var existing = packages[selectedId];
            if (Conflicts(package, existing))
            {
                error = package.Name + " conflicts with " + existing.Name +
                        ". Disable one of these packages before continuing.";
                visiting.Remove(package.Id);
                return false;
            }
        }

        selected.Add(package.Id);
        added.Add(package.Id);
        visiting.Remove(package.Id);
        return true;
    }

    private static bool Conflicts(ModPackage left, ModPackage right)
    {
        if (left.Conflicts.Contains(right.Id, StringComparer.OrdinalIgnoreCase) ||
            right.Conflicts.Contains(left.Id, StringComparer.OrdinalIgnoreCase))
            return true;

        return !string.IsNullOrWhiteSpace(left.ExclusiveGroup) &&
               string.Equals(left.ExclusiveGroup, right.ExclusiveGroup,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static ProfileSelectionResult Failure(HashSet<string> selection, string error) =>
        new(false, selection, error, Array.Empty<string>(), Array.Empty<string>());
}
