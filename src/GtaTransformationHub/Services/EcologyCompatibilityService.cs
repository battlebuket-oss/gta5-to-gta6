using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

public static class EcologyCompatibilityService
{
    private static readonly HashSet<string> SensitiveAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ydr", ".ydd", ".yft", ".ytd", ".ymap", ".ytyp", ".ymt", ".ybn", ".ycd", ".meta"
    };

    private static readonly string[] SensitivePathTokens =
    {
        "wildlife", "animal", "fauna", "flora", "vegetation", "foliage",
        "plant", "tree", "species", "popcycle", "popgroups",
        "pedmodelsets", "ambientpedmodelsets", "scenario",
        "aquatic", "underwater", "marine", "fish", "fishing",
        "seagrass", "kelp", "coral", "reef", "water", "ocean",
        "river", "lake", "pond", "coast", "shore", "submerged"
    };

    public static bool RequiresEcologyReview(PackageManifest manifest)
    {
        if (manifest.ImpactAreas?.Any(IsEcologyArea) == true)
            return true;
        return manifest.Files?.Any(file =>
        {
            var destination = (file.DestinationPath ?? string.Empty).Replace('\\', '/');
            return SensitivePathTokens.Any(token => destination.Contains(token, StringComparison.OrdinalIgnoreCase)) ||
                   SensitiveAssetExtensions.Contains(Path.GetExtension(destination));
        }) == true;
    }

    public static IReadOnlyList<string> Validate(PackageManifest manifest)
    {
        if (!RequiresEcologyReview(manifest))
            return Array.Empty<string>();
        var errors = new List<string>();
        var contract = manifest.Ecology;
        if (contract is null)
        {
            errors.Add(manifest.Name + " touches ecology-sensitive assets but has no ecology preservation declaration. Installation is blocked.");
            return errors;
        }
        if (!string.Equals(contract.ReviewStatus, "Approved", StringComparison.OrdinalIgnoreCase))
            errors.Add(manifest.Name + " has not passed an ecological identity review.");
        var allowedImpact = new[] { "None", "VisualOnly", "DensityOnly", "AudioOnly", "BehaviourOnly", "AdditiveBiodiversity" };
        if (contract.ImpactType is null || !allowedImpact.Contains(contract.ImpactType, StringComparer.OrdinalIgnoreCase))
            errors.Add(manifest.Name + " declares an unsupported ecological change type.");
        if (!contract.PreservesVanillaAnimalSpecies || !contract.PreservesVanillaPlantSpecies ||
            !contract.PreservesVanillaVegetationSpecies || !contract.PreservesVanillaModelIdentifiers)
            errors.Add(manifest.Name + " does not guarantee preservation of every vanilla animal, plant, vegetation species and model identifier.");
        if (contract.AllowsSpeciesReplacement || contract.AllowsSpeciesRemoval || contract.ChangesSpeciesSpawnTables)
            errors.Add(manifest.Name + " replaces/removes vanilla species or changes existing species population/spawn definitions. Only strictly additive species entries are permitted.");
        if (contract.VanillaIdentityInventorySha256 is null || contract.VanillaIdentityInventorySha256.Length != 64 ||
            !contract.VanillaIdentityInventorySha256.All(Uri.IsHexDigit))
            errors.Add(manifest.Name + " has no valid SHA-256 fingerprint for its vanilla identity comparison inventory.");
        if (!Uri.TryCreate(contract.EvidenceUri, UriKind.Absolute, out var evidence) || evidence.Scheme != Uri.UriSchemeHttps)
            errors.Add(manifest.Name + " has no HTTPS ecological review evidence.");

        // Deliberately fail closed: this build does not yet parse every supported
        // GTA V asset format or perform a semantic species/model diff. A manifest
        // declaration, inventory hash and evidence URL are not proof by themselves.
        errors.Add(manifest.Name + " is blocked because exact-build semantic verification of terrestrial and aquatic animal, plant, vegetation, water and model identities is not implemented yet. Manifest claims cannot self-approve the package.");
        return errors;
    }

    private static bool IsEcologyArea(string value) =>
        new[]
        {
            "ecology", "wildlife", "flora", "fauna", "vegetation", "environment",
            "population", "aquatic", "underwater", "marine", "water"
        }.Contains(value, StringComparer.OrdinalIgnoreCase);
}
