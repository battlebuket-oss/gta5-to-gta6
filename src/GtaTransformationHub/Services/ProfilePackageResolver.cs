using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

public sealed record ProfilePackageResolution(
    IReadOnlyList<ImportedPackage> SelectedPackages,
    IReadOnlyList<string> MissingPackages,
    IReadOnlyList<string> AmbiguousPackages);

public static class ProfilePackageResolver
{
    public static ProfilePackageResolution Resolve(
        ModProfile profile,
        IReadOnlyCollection<ImportedPackage> packageLibrary)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(packageLibrary);

        var selected = new List<ImportedPackage>();
        var missing = new List<string>();
        var ambiguous = new List<string>();

        foreach (var packageId in profile.EnabledPackageIds.OrderBy(
                     id => id, StringComparer.OrdinalIgnoreCase))
        {
            var matches = packageLibrary
                .Where(package => package.Manifest.Id.Equals(
                    packageId, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (profile.PackageVersions is not null &&
                profile.PackageVersions.TryGetValue(packageId, out var pinnedVersion))
            {
                var exact = matches.FirstOrDefault(package =>
                    package.Manifest.Version.Equals(pinnedVersion, StringComparison.OrdinalIgnoreCase));

                if (exact is null)
                    missing.Add(packageId + "@" + pinnedVersion);
                else
                    selected.Add(exact);

                continue;
            }

            if (matches.Length == 0)
                missing.Add(packageId);
            else if (matches.Length == 1)
                selected.Add(matches[0]);
            else
                ambiguous.Add(packageId);
        }

        return new ProfilePackageResolution(selected, missing, ambiguous);
    }
}
