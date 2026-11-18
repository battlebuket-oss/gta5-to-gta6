using System;
using System.Collections.Generic;
using System.Linq;

namespace GtaTransformationHub.Services;

/// <summary>
/// Compares parser-produced ecological inventories. This is a semantic comparison
/// engine, not an RPF extractor or proof that an inventory came from the game.
/// Callers must only trust snapshots produced by a separately verified exact-build
/// extraction pipeline. The package preflight remains fail-closed until that pipeline
/// and its provenance checks are integrated.
/// </summary>
public static class EcologyInventoryDiffService
{
    public static EcologyInventoryDiff Compare(
        EcologyInventorySnapshot baseline,
        EcologyInventorySnapshot candidate)
    {
        var errors = new List<string>();
        var addedSpecies = new List<EcologySpeciesIdentity>();

        ValidateSnapshot(baseline, "Baseline", errors);
        ValidateSnapshot(candidate, "Candidate", errors);

        var baselineSpeciesItems = baseline.Species ?? Array.Empty<EcologySpeciesIdentity>();
        var candidateSpeciesItems = candidate.Species ?? Array.Empty<EcologySpeciesIdentity>();
        var baselineHabitatItems = baseline.Habitats ?? Array.Empty<EcologyHabitatIdentity>();
        var candidateHabitatItems = candidate.Habitats ?? Array.Empty<EcologyHabitatIdentity>();
        var baselinePopulationItems = baseline.PopulationEntries ?? Array.Empty<EcologyPopulationEntry>();
        var candidatePopulationItems = candidate.PopulationEntries ?? Array.Empty<EcologyPopulationEntry>();
        var baselineWaterItems = baseline.WaterBodies ?? Array.Empty<EcologyWaterBodyIdentity>();
        var candidateWaterItems = candidate.WaterBodies ?? Array.Empty<EcologyWaterBodyIdentity>();

        if (baseline.SchemaVersion != candidate.SchemaVersion)
            errors.Add("Inventory schema versions do not match.");
        if (!Same(baseline.GameBuild, candidate.GameBuild))
            errors.Add("The candidate inventory targets a different GTA V game build.");
        if (!SameHash(baseline.GameExecutableSha256, candidate.GameExecutableSha256))
            errors.Add("The GTA5.exe fingerprint does not match the vanilla baseline.");

        var baselineSpecies = IndexById(baselineSpeciesItems, item => item.Id, "baseline species", errors);
        var candidateSpecies = IndexById(candidateSpeciesItems, item => item.Id, "candidate species", errors);
        var baselineHabitats = IndexById(baselineHabitatItems, item => item.Id, "baseline habitats", errors);
        var candidateHabitats = IndexById(candidateHabitatItems, item => item.Id, "candidate habitats", errors);
        var baselinePopulation = IndexById(baselinePopulationItems, item => item.Id, "baseline population entries", errors);
        var candidatePopulation = IndexById(candidatePopulationItems, item => item.Id, "candidate population entries", errors);
        var baselineWater = IndexById(baselineWaterItems, item => item.Id, "baseline water bodies", errors);
        var candidateWater = IndexById(candidateWaterItems, item => item.Id, "candidate water bodies", errors);

        ValidateReferences(baselineSpeciesItems, baselineHabitatItems, baselinePopulationItems,
            baselineWaterItems, "Baseline", errors);
        ValidateReferences(candidateSpeciesItems, candidateHabitatItems, candidatePopulationItems,
            candidateWaterItems, "Candidate", errors);

        foreach (var original in baselineSpecies.Values)
        {
            if (!candidateSpecies.TryGetValue(original.Id, out var current))
            {
                errors.Add($"Vanilla species '{original.Id}' was removed.");
                continue;
            }

            if (!SpeciesIdentityEquals(original, current))
                errors.Add($"Vanilla species '{original.Id}' changed its model, domain or behaviour identity.");
        }

        foreach (var original in baselineHabitats.Values)
        {
            if (!candidateHabitats.TryGetValue(original.Id, out var current))
                errors.Add($"Vanilla habitat '{original.Id}' was removed.");
            else if (!HabitatEquals(original, current))
                errors.Add($"Vanilla habitat '{original.Id}' was changed.");
        }

        foreach (var original in baselinePopulation.Values)
        {
            if (!candidatePopulation.TryGetValue(original.Id, out var current))
                errors.Add($"Vanilla population entry '{original.Id}' was removed.");
            else if (!PopulationEquals(original, current))
                errors.Add($"Vanilla population entry '{original.Id}' was changed.");
        }

        foreach (var original in baselineWater.Values)
        {
            if (!candidateWater.TryGetValue(original.Id, out var current))
                errors.Add($"Vanilla water body '{original.Id}' was removed.");
            else if (!WaterIdentityEquals(original, current))
                errors.Add($"Water body '{original.Id}' changed its type, simulation or interaction identity.");
            // RenderingProfileId is intentionally allowed to change so visual water
            // improvements can be compared without silently replacing water behaviour.
        }

        var baselineModelIds = baselineSpeciesItems
            .Where(item => item is not null)
            .SelectMany(item => item.ModelIdentifiers ?? Array.Empty<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = candidateSpecies.Values
            .Where(item => !baselineSpecies.ContainsKey(item.Id))
            .ToArray();

        foreach (var species in added)
        {
            addedSpecies.Add(species);
            if (string.IsNullOrWhiteSpace(species.Domain) ||
                !AllowedDomains.Contains(species.Domain))
                errors.Add($"New species '{species.Id}' has an unsupported ecological domain.");

            if (species.ModelIdentifiers is null || species.ModelIdentifiers.Count == 0)
                errors.Add($"New species '{species.Id}' has no model identifiers.");

            foreach (var modelId in species.ModelIdentifiers ?? Array.Empty<string>())
            {
                if (baselineModelIds.Contains(modelId))
                    errors.Add($"New species '{species.Id}' reuses a vanilla model identifier '{modelId}'.");
            }

            var entries = candidatePopulationItems
                .Where(item => item is not null && Same(item.SpeciesId, species.Id))
                .ToArray();
            if (entries.Length == 0)
                errors.Add($"New species '{species.Id}' has no additive population or distribution entry.");

            foreach (var habitatId in species.HabitatIds ?? Array.Empty<string>())
            {
                if (!candidateHabitats.ContainsKey(habitatId))
                    errors.Add($"New species '{species.Id}' references missing habitat '{habitatId}'.");
            }
        }

        foreach (var entry in candidatePopulation.Values.Where(item => !baselinePopulation.ContainsKey(item.Id)))
        {
            if (!added.Any(item => Same(item.Id, entry.SpeciesId)))
                errors.Add($"New population entry '{entry.Id}' modifies or adds population data for a vanilla species.");
            if (!candidateHabitats.ContainsKey(entry.HabitatId))
                errors.Add($"Population entry '{entry.Id}' references missing habitat '{entry.HabitatId}'.");
        }

        // Vanilla species may intentionally share an original model alias. Only
        // newly introduced species must have model identifiers unique among themselves.
        ValidateGlobalModelUniqueness(added, errors);
        return new EcologyInventoryDiff(
            errors.Count == 0,
            errors,
            added);
    }

    private static readonly HashSet<string> AllowedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "TerrestrialAnimal",
        "AquaticAnimal",
        "TerrestrialPlant",
        "AquaticPlant"
    };

    private static void ValidateSnapshot(
        EcologyInventorySnapshot snapshot,
        string label,
        List<string> errors)
    {
        if (snapshot.SchemaVersion != 1)
            errors.Add($"{label} inventory schema version is unsupported.");
        if (string.IsNullOrWhiteSpace(snapshot.GameBuild))
            errors.Add($"{label} inventory has no game build.");
        if (!IsSha256(snapshot.GameExecutableSha256))
            errors.Add($"{label} inventory has no valid GTA5.exe SHA-256.");
        if (string.IsNullOrWhiteSpace(snapshot.ExtractorId) ||
            string.IsNullOrWhiteSpace(snapshot.ExtractorVersion))
            errors.Add($"{label} inventory has no extractor identity/version.");
        if (snapshot.SourceArchiveSha256 is null || snapshot.SourceArchiveSha256.Count == 0 ||
            snapshot.SourceArchiveSha256.Any(hash => !IsSha256(hash)))
            errors.Add($"{label} inventory has incomplete source archive hashes.");

        if (snapshot.Species is null || snapshot.Habitats is null ||
            snapshot.PopulationEntries is null || snapshot.WaterBodies is null)
            errors.Add($"{label} inventory has a missing inventory collection.");
    }

    private static Dictionary<string, T> IndexById<T>(
        IEnumerable<T> values,
        Func<T, string> keySelector,
        string description,
        List<string> errors)
    {
        var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (value is null)
            {
                errors.Add($"The {description} inventory contains a null record.");
                continue;
            }
            var key = keySelector(value);
            if (string.IsNullOrWhiteSpace(key))
            {
                errors.Add($"A {description} record has an empty identifier.");
                continue;
            }
            if (!result.TryAdd(key, value))
                errors.Add($"The {description} inventory contains duplicate identifier '{key}'.");
        }
        return result;
    }

    private static void ValidateReferences(
        IReadOnlyList<EcologySpeciesIdentity> species,
        IReadOnlyList<EcologyHabitatIdentity> habitats,
        IReadOnlyList<EcologyPopulationEntry> populationEntries,
        IReadOnlyList<EcologyWaterBodyIdentity> waterBodies,
        string label,
        List<string> errors)
    {
        var speciesById = species.Where(item => item is not null)
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var habitatIds = habitats.Where(item => item is not null)
            .Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var waterIds = waterBodies.Where(item => item is not null)
            .Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in species.Where(item => item is not null))
        {
            if (item.ModelIdentifiers is null || item.ModelIdentifiers.Count == 0)
                errors.Add($"{label} species '{item.Id}' has no model identifiers.");
            if (string.IsNullOrWhiteSpace(item.BehaviourProfileId) ||
                !IsSha256(item.BehaviourDefinitionSha256))
                errors.Add($"{label} species '{item.Id}' has an incomplete behaviour profile fingerprint.");
            if (item.HabitatIds is null)
            {
                errors.Add($"{label} species '{item.Id}' has no habitat list.");
                continue;
            }
            foreach (var habitatId in item.HabitatIds)
                if (!habitatIds.Contains(habitatId))
                    errors.Add($"{label} species '{item.Id}' references missing habitat '{habitatId}'.");
        }

        foreach (var habitat in habitats.Where(item => item is not null))
        {
            if (!string.IsNullOrWhiteSpace(habitat.WaterBodyId) && !waterIds.Contains(habitat.WaterBodyId))
                errors.Add($"{label} habitat '{habitat.Id}' references missing water body '{habitat.WaterBodyId}'.");
            if (string.IsNullOrWhiteSpace(habitat.HabitatType) || !IsSha256(habitat.DefinitionSha256))
                errors.Add($"{label} habitat '{habitat.Id}' has an incomplete habitat definition fingerprint.");
        }

        foreach (var entry in populationEntries.Where(item => item is not null))
        {
            if (!speciesById.TryGetValue(entry.SpeciesId, out var ownerSpecies))
            {
                errors.Add($"{label} population entry '{entry.Id}' references missing species '{entry.SpeciesId}'.");
            }
            else if (ownerSpecies.HabitatIds is null ||
                     !ownerSpecies.HabitatIds.Contains(entry.HabitatId, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"{label} population entry '{entry.Id}' is assigned to a habitat not declared by species '{entry.SpeciesId}'.");
            }
            if (!habitatIds.Contains(entry.HabitatId))
                errors.Add($"{label} population entry '{entry.Id}' references missing habitat '{entry.HabitatId}'.");
            if (!IsSha256(entry.DefinitionSha256))
                errors.Add($"{label} population entry '{entry.Id}' has an invalid definition fingerprint.");
        }

        foreach (var water in waterBodies.Where(item => item is not null))
        {
            if (string.IsNullOrWhiteSpace(water.WaterType) ||
                string.IsNullOrWhiteSpace(water.SimulationProfileId) ||
                string.IsNullOrWhiteSpace(water.InteractionProfileId) ||
                string.IsNullOrWhiteSpace(water.RenderingProfileId) ||
                !IsSha256(water.SimulationDefinitionSha256) ||
                !IsSha256(water.InteractionDefinitionSha256) ||
                !IsSha256(water.RenderingDefinitionSha256))
                errors.Add($"{label} water body '{water.Id}' has incomplete or invalid water profile fingerprints.");
        }
    }

    private static void ValidateGlobalModelUniqueness(
        IReadOnlyList<EcologySpeciesIdentity> species,
        List<string> errors)
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in species)
        {
            foreach (var model in item.ModelIdentifiers ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(model))
                {
                    errors.Add($"Species '{item.Id}' contains an empty model identifier.");
                    continue;
                }

                if (owners.TryGetValue(model, out var owner) && !Same(owner, item.Id))
                    errors.Add($"Model identifier '{model}' is shared by species '{owner}' and '{item.Id}'.");
                else
                    owners[model] = item.Id;
            }
        }
    }

    private static bool SpeciesIdentityEquals(EcologySpeciesIdentity left, EcologySpeciesIdentity right) =>
        Same(left.Id, right.Id) &&
        Same(left.Domain, right.Domain) &&
        Same(left.BehaviourProfileId, right.BehaviourProfileId) &&
        SameHash(left.BehaviourDefinitionSha256, right.BehaviourDefinitionSha256) &&
        SetEquals(left.ModelIdentifiers, right.ModelIdentifiers) &&
        SetEquals(left.HabitatIds, right.HabitatIds);

    private static bool HabitatEquals(EcologyHabitatIdentity left, EcologyHabitatIdentity right) =>
        Same(left.Id, right.Id) &&
        Same(left.HabitatType, right.HabitatType) &&
        Same(left.WaterBodyId, right.WaterBodyId) &&
        SameHash(left.DefinitionSha256, right.DefinitionSha256);

    private static bool PopulationEquals(EcologyPopulationEntry left, EcologyPopulationEntry right) =>
        Same(left.Id, right.Id) &&
        Same(left.SpeciesId, right.SpeciesId) &&
        Same(left.HabitatId, right.HabitatId) &&
        SameHash(left.DefinitionSha256, right.DefinitionSha256);

    private static bool WaterIdentityEquals(EcologyWaterBodyIdentity left, EcologyWaterBodyIdentity right) =>
        Same(left.Id, right.Id) &&
        Same(left.WaterType, right.WaterType) &&
        Same(left.SimulationProfileId, right.SimulationProfileId) &&
        Same(left.InteractionProfileId, right.InteractionProfileId) &&
        SameHash(left.SimulationDefinitionSha256, right.SimulationDefinitionSha256) &&
        SameHash(left.InteractionDefinitionSha256, right.InteractionDefinitionSha256);

    private static bool SetEquals(IReadOnlyList<string>? left, IReadOnlyList<string>? right) =>
        left is not null && right is not null &&
        left.ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(right);

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool SameHash(string? left, string? right) =>
        IsSha256(left) && IsSha256(right) &&
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

public sealed record EcologyInventorySnapshot(
    int SchemaVersion,
    string GameBuild,
    string GameExecutableSha256,
    string ExtractorId,
    string ExtractorVersion,
    IReadOnlyList<string> SourceArchiveSha256,
    IReadOnlyList<EcologySpeciesIdentity> Species,
    IReadOnlyList<EcologyHabitatIdentity> Habitats,
    IReadOnlyList<EcologyPopulationEntry> PopulationEntries,
    IReadOnlyList<EcologyWaterBodyIdentity> WaterBodies);

public sealed record EcologySpeciesIdentity(
    string Id,
    string Domain,
    IReadOnlyList<string> ModelIdentifiers,
    IReadOnlyList<string> HabitatIds,
    string BehaviourProfileId,
    string BehaviourDefinitionSha256 = "");

public sealed record EcologyHabitatIdentity(
    string Id,
    string HabitatType,
    string WaterBodyId,
    string DefinitionSha256 = "");

public sealed record EcologyPopulationEntry(
    string Id,
    string SpeciesId,
    string HabitatId,
    string DefinitionSha256);

public sealed record EcologyWaterBodyIdentity(
    string Id,
    string WaterType,
    string SimulationProfileId,
    string InteractionProfileId,
    string RenderingProfileId,
    string SimulationDefinitionSha256,
    string InteractionDefinitionSha256,
    string RenderingDefinitionSha256);

public sealed record EcologyInventoryDiff(
    bool VanillaPreserved,
    IReadOnlyList<string> Errors,
    IReadOnlyList<EcologySpeciesIdentity> AddedSpecies);
