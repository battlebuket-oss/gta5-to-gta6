using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GtaTransformationHub.Services;

public sealed record PedMetadataInventoryEntry(
    int CandidateIndex,
    int EntryIndex,
    string JsonPointer,
    string ResourceName,
    string InternalArchivePath,
    string ModelName,
    string PropsName,
    string ClipDictionaryName,
    string PedType,
    string MovementClipSet,
    string CreatureMetadataName,
    string AnimalAudioObject,
    string AbilityType,
    string ThermalBehaviour,
    string DecisionMakerName,
    string Personality,
    string CombatInfo,
    string PedCapsuleName,
    string PedLayoutName,
    string PedComponentSetName,
    string NavCapabilitiesName,
    string PerceptionInfo,
    string DefaultTaskDataSetName,
    string ScenarioPopStreamingSlot,
    string DefaultSpawningPreference,
    bool IsStreamedGfx,
    bool IsAnimalCandidate,
    string? AnimalDomainCandidate,
    string ClassificationEvidence,
    string BehaviourMetadataSha256,
    string PedDefinitionSha256,
    string SourceArchivePath,
    string SourceArchiveSha256,
    string DumpJsonSha256);

public sealed record PedRegistryParseResult(
    bool Recognized,
    bool Complete,
    int DeclaredEntryCount,
    IReadOnlyList<PedMetadataInventoryEntry> Entries,
    IReadOnlyList<string> Warnings);

public sealed record WaterQuadGeometryDefinition(
    string Id,
    int CandidateIndex,
    int EntryIndex,
    double MinX,
    double MaxX,
    double MinY,
    double MaxY,
    int TypeCode,
    bool IsInvisible,
    bool HasLimitedDepth,
    double Z,
    IReadOnlyList<double> Alpha,
    bool NoStencil);

public sealed record WaterCalmingQuadDefinition(
    string Id,
    int CandidateIndex,
    int EntryIndex,
    double MinX,
    double MaxX,
    double MinY,
    double MaxY,
    double Dampening);

public sealed record WaterWaveQuadDefinition(
    string Id,
    int CandidateIndex,
    int EntryIndex,
    double MinX,
    double MaxX,
    double MinY,
    double MaxY,
    double Amplitude,
    double XDirection,
    double YDirection);

public sealed record WaterGeometryFileDefinition(
    int CandidateIndex,
    string ResourceName,
    string InternalArchivePath,
    string SourceArchivePath,
    string SourceArchiveSha256,
    string DumpJsonSha256,
    bool StructuredParseComplete,
    IReadOnlyList<WaterQuadGeometryDefinition> WaterQuads,
    IReadOnlyList<WaterCalmingQuadDefinition> CalmingQuads,
    IReadOnlyList<WaterWaveQuadDefinition> WaveQuads);

public sealed record WaterGeometryParseResult(
    bool Recognized,
    bool Complete,
    WaterGeometryFileDefinition? File,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Typed readers for two well-defined vanilla GTA V metadata formats.
/// The ped registry reader follows rage-formats' CPedModelInfo__InitDataList
/// field names. The water reader follows the WaterData XML structure:
/// WaterQuads, CalmingQuads and WaveQuads. These outputs are exact metadata
/// records, not guesses made from filenames. Animal-domain labelling remains
/// a candidate classification; peds.ymt alone does not prove habitat or spawn
/// distribution. Water XML alone does not contain the full shader, interaction
/// or simulation implementation.
/// </summary>
public static class RageCliTypedEcologyMetadataParser
{
    private const string PedRegistryRoot = "CPedModelInfo__InitDataList";
    private const string WaterRoot = "WaterData";

    private static readonly string[] AquaticAnimalNameTokens =
    [
        "FISH", "SHARK", "DOLPHIN", "WHALE", "STINGRAY",
        "OCTOPUS", "SQUID", "TURTLE", "SEAL"
    ];

    public static PedRegistryParseResult ParsePedRegistry(
        JsonElement root,
        int candidateIndex,
        string resourceName,
        string internalArchivePath,
        string sourceArchivePath,
        string sourceArchiveSha256,
        string dumpJsonSha256)
    {
        ValidateProvenance(
            candidateIndex, resourceName, internalArchivePath, sourceArchivePath,
            sourceArchiveSha256, dumpJsonSha256);

        if (root.ValueKind != JsonValueKind.Object ||
            !TryGetProperty(root, "$type", out var rootType) ||
            rootType.ValueKind != JsonValueKind.String ||
            !string.Equals(rootType.GetString(), PedRegistryRoot, StringComparison.Ordinal))
        {
            return new PedRegistryParseResult(
                Recognized: false,
                Complete: false,
                DeclaredEntryCount: 0,
                Entries: Array.Empty<PedMetadataInventoryEntry>(),
                Warnings: Array.Empty<string>());
        }

        var warnings = new List<string>();
        if (!TryGetProperty(root, "InitDatas", out var initDatas) ||
            initDatas.ValueKind != JsonValueKind.Array)
        {
            return new PedRegistryParseResult(
                true, false, 0, Array.Empty<PedMetadataInventoryEntry>(),
                new[] { "The ped registry root has no InitDatas array." });
        }

        var entries = new List<PedMetadataInventoryEntry>();
        var entryIndex = 0;
        foreach (var item in initDatas.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                warnings.Add("Ped registry entry " + entryIndex + " is not a JSON object.");
                entryIndex++;
                continue;
            }

            var name = ReadString(item, "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                warnings.Add("Ped registry entry " + entryIndex + " has no Name identifier.");
                entryIndex++;
                continue;
            }

            var propsName = ReadString(item, "PropsName");
            var clipDictionary = ReadString(item, "ClipDictionaryName");
            var pedType = ReadString(item, "Pedtype");
            var movementClipSet = ReadString(item, "MovementClipSet");
            var creatureMetadata = ReadString(item, "CreatureMetadataName");
            var animalAudio = ReadString(item, "AnimalAudioObject");
            var abilityType = ReadString(item, "AbilityType");
            var thermalBehaviour = ReadString(item, "ThermalBehaviour");
            var decisionMaker = ReadString(item, "DecisionMakerName");
            var personality = ReadString(item, "Personality");
            var combatInfo = ReadString(item, "CombatInfo");
            var capsule = ReadString(item, "PedCapsuleName");
            var layout = ReadString(item, "PedLayoutName");
            var componentSet = ReadString(item, "PedComponentSetName");
            var navCapabilities = ReadString(item, "NavCapabilitiesName");
            var perception = ReadString(item, "PerceptionInfo");
            var defaultTaskDataSet = ReadString(item, "DefaultTaskDataSetName");
            var scenarioSlot = ReadString(item, "ScenarioPopStreamingSlot");
            var spawningPreference = ReadString(item, "DefaultSpawningPreference");
            var isStreamed = ReadBoolean(item, "IsStreamedGfx");

            var animalEvidence = new List<string>();
            if (name.StartsWith("A_C_", StringComparison.OrdinalIgnoreCase))
                animalEvidence.Add("A_C_ model-name prefix");
            if (ContainsAny(pedType, "ANIMAL", "FISH", "MARINE"))
                animalEvidence.Add("Pedtype=" + pedType);
            if (!string.IsNullOrWhiteSpace(creatureMetadata))
                animalEvidence.Add("CreatureMetadataName is populated");
            if (!string.IsNullOrWhiteSpace(animalAudio))
                animalEvidence.Add("AnimalAudioObject is populated");

            var isAnimal = animalEvidence.Count > 0;
            string? domainCandidate = null;
            if (isAnimal)
            {
                domainCandidate = IsAquaticAnimalCandidate(name, pedType)
                    ? "AquaticAnimal"
                    : "TerrestrialAnimal";
            }

            var behaviourFingerprint = HashText(string.Join("\n", new[]
            {
                pedType,
                movementClipSet,
                creatureMetadata,
                animalAudio,
                abilityType,
                thermalBehaviour,
                decisionMaker,
                personality,
                combatInfo,
                capsule,
                layout,
                componentSet,
                navCapabilities,
                perception,
                defaultTaskDataSet,
                scenarioSlot,
                spawningPreference
            }));

            var definitionHash = HashText(item.GetRawText());
            entries.Add(new PedMetadataInventoryEntry(
                candidateIndex,
                entryIndex,
                "$/InitDatas/" + entryIndex.ToString(CultureInfo.InvariantCulture),
                resourceName,
                internalArchivePath,
                name,
                propsName,
                clipDictionary,
                pedType,
                movementClipSet,
                creatureMetadata,
                animalAudio,
                abilityType,
                thermalBehaviour,
                decisionMaker,
                personality,
                combatInfo,
                capsule,
                layout,
                componentSet,
                navCapabilities,
                perception,
                defaultTaskDataSet,
                scenarioSlot,
                spawningPreference,
                isStreamed,
                isAnimal,
                domainCandidate,
                animalEvidence.Count == 0
                    ? "No animal-specific fields or A_C_ model-name prefix were detected."
                    : string.Join("; ", animalEvidence),
                behaviourFingerprint,
                definitionHash,
                sourceArchivePath,
                sourceArchiveSha256,
                dumpJsonSha256));

            entryIndex++;
        }

        if (entries.Count == 0)
            warnings.Add("The ped registry contained no readable InitDatas records.");

        return new PedRegistryParseResult(
            true,
            warnings.Count == 0 && entries.Count == initDatas.GetArrayLength(),
            initDatas.GetArrayLength(),
            entries,
            warnings);
    }

    public static WaterGeometryParseResult ParseWaterGeometry(
        JsonElement root,
        int candidateIndex,
        string resourceName,
        string internalArchivePath,
        string sourceArchivePath,
        string sourceArchiveSha256,
        string dumpJsonSha256)
    {
        ValidateProvenance(
            candidateIndex, resourceName, internalArchivePath, sourceArchivePath,
            sourceArchiveSha256, dumpJsonSha256);

        if (root.ValueKind != JsonValueKind.Object ||
            !TryGetProperty(root, "$type", out var rootType) ||
            rootType.ValueKind != JsonValueKind.String ||
            !string.Equals(rootType.GetString(), WaterRoot, StringComparison.Ordinal))
        {
            return new WaterGeometryParseResult(
                false, false, null, Array.Empty<string>());
        }

        var warnings = new List<string>();
        var waterQuads = new List<WaterQuadGeometryDefinition>();
        var calmingQuads = new List<WaterCalmingQuadDefinition>();
        var waveQuads = new List<WaterWaveQuadDefinition>();

        ParseArray("WaterQuads", item =>
        {
            if (!TryReadBounds(item, out var minX, out var maxX, out var minY, out var maxY, out var boundsError))
            {
                warnings.Add("WaterQuads item " + item.Index + ": " + boundsError);
                return;
            }

            if (!TryReadDouble(item.Value, "z", out var z) ||
                !TryReadInt(item.Value, "Type", out var typeCode) ||
                !TryReadBoolean(item.Value, "IsInvisible", out var invisible) ||
                !TryReadBoolean(item.Value, "HasLimitedDepth", out var limitedDepth) ||
                !TryReadBoolean(item.Value, "NoStencil", out var noStencil) ||
                !TryReadDouble(item.Value, "a1", out var a1) ||
                !TryReadDouble(item.Value, "a2", out var a2) ||
                !TryReadDouble(item.Value, "a3", out var a3) ||
                !TryReadDouble(item.Value, "a4", out var a4))
            {
                warnings.Add("WaterQuads item " + item.Index + " is missing one or more typed water fields.");
                return;
            }

            waterQuads.Add(new WaterQuadGeometryDefinition(
                resourceName + ":WaterQuads:" + item.Index.ToString("D6", CultureInfo.InvariantCulture),
                candidateIndex, item.Index,
                minX, maxX, minY, maxY, typeCode, invisible, limitedDepth, z,
                new[] { a1, a2, a3, a4 }, noStencil));
        });

        ParseArray("CalmingQuads", item =>
        {
            if (!TryReadBounds(item, out var minX, out var maxX, out var minY, out var maxY, out var boundsError) ||
                !TryReadDouble(item.Value, "fDampening", out var dampening))
            {
                warnings.Add("CalmingQuads item " + item.Index + ": " +
                             (boundsError ?? "fDampening is missing or invalid."));
                return;
            }

            calmingQuads.Add(new WaterCalmingQuadDefinition(
                resourceName + ":CalmingQuads:" + item.Index.ToString("D6", CultureInfo.InvariantCulture),
                candidateIndex, item.Index, minX, maxX, minY, maxY, dampening));
        });

        ParseArray("WaveQuads", item =>
        {
            if (!TryReadBounds(item, out var minX, out var maxX, out var minY, out var maxY, out var boundsError) ||
                !TryReadDouble(item.Value, "Amplitude", out var amplitude) ||
                !TryReadDouble(item.Value, "XDirection", out var xDirection) ||
                !TryReadDouble(item.Value, "YDirection", out var yDirection))
            {
                warnings.Add("WaveQuads item " + item.Index + ": " +
                             (boundsError ?? "Amplitude or wave-direction values are missing or invalid."));
                return;
            }

            waveQuads.Add(new WaterWaveQuadDefinition(
                resourceName + ":WaveQuads:" + item.Index.ToString("D6", CultureInfo.InvariantCulture),
                candidateIndex, item.Index, minX, maxX, minY, maxY,
                amplitude, xDirection, yDirection));
        });

        if (!waterQuads.Any())
            warnings.Add("The WaterData document contained no valid WaterQuads entries.");

        var file = new WaterGeometryFileDefinition(
            candidateIndex,
            resourceName,
            internalArchivePath,
            sourceArchivePath,
            sourceArchiveSha256,
            dumpJsonSha256,
            warnings.Count == 0,
            waterQuads,
            calmingQuads,
            waveQuads);

        return new WaterGeometryParseResult(
            true,
            warnings.Count == 0,
            file,
            warnings);

        void ParseArray(string propertyName, Action<(JsonElement Value, int Index)> consume)
        {
            if (!TryGetProperty(root, propertyName, out var values) ||
                values.ValueKind != JsonValueKind.Array)
            {
                warnings.Add("The WaterData document is missing its " + propertyName + " array.");
                return;
            }

            var index = 0;
            foreach (var item in values.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    warnings.Add(propertyName + " item " + index + " is not an object.");
                else
                    consume((item, index));
                index++;
            }
        }
    }

    public static bool IsAquaticAnimalCandidate(string? modelName, string? pedType)
    {
        var name = (modelName ?? string.Empty).ToUpperInvariant();
        var type = (pedType ?? string.Empty).ToUpperInvariant();
        return ContainsAny(type, "FISH", "AQUATIC", "MARINE") ||
               (name.StartsWith("A_C_", StringComparison.Ordinal) &&
                AquaticAnimalNameTokens.Any(token =>
                    name.Contains(token, StringComparison.Ordinal)));
    }

    private static bool TryReadBounds(
        (JsonElement Value, int Index) item,
        out double minX,
        out double maxX,
        out double minY,
        out double maxY,
        out string? error)
    {
        minX = maxX = minY = maxY = 0;
        error = null;

        if (!TryReadDouble(item.Value, "minX", out minX) ||
            !TryReadDouble(item.Value, "maxX", out maxX) ||
            !TryReadDouble(item.Value, "minY", out minY) ||
            !TryReadDouble(item.Value, "maxY", out maxY))
        {
            error = "one or more bounds are missing or not numeric.";
            return false;
        }

        if (minX > maxX || minY > maxY)
        {
            error = "the water bounds are inverted.";
            return false;
        }

        return true;
    }

    private static bool TryReadDouble(JsonElement source, string name, out double result)
    {
        result = 0;
        if (!TryGetProperty(source, name, out var value))
            return false;
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (!TryGetProperty(value, "value", out var nested))
                return false;
            value = nested;
        }

        if (value.ValueKind == JsonValueKind.Number)
            return value.TryGetDouble(out result) && double.IsFinite(result);
        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            return double.IsFinite(result);
        return false;
    }

    private static bool TryReadInt(JsonElement source, string name, out int result)
    {
        result = 0;
        if (!TryGetProperty(source, name, out var value))
            return false;
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (!TryGetProperty(value, "value", out var nested))
                return false;
            value = nested;
        }

        if (value.ValueKind == JsonValueKind.Number)
            return value.TryGetInt32(out result);
        return value.ValueKind == JsonValueKind.String &&
               int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryReadBoolean(JsonElement source, string name, out bool result)
    {
        result = false;
        if (!TryGetProperty(source, name, out var value))
            return false;
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (!TryGetProperty(value, "value", out var nested))
                return false;
            value = nested;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            result = value.GetBoolean();
            return true;
        }
        if (value.ValueKind == JsonValueKind.String &&
            bool.TryParse(value.GetString(), out result))
            return true;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numeric))
        {
            result = numeric != 0;
            return true;
        }
        return false;
    }

    private static string ReadString(JsonElement source, string name)
    {
        if (!TryGetProperty(source, name, out var value))
            return string.Empty;
        if (value.ValueKind == JsonValueKind.Object &&
            TryGetProperty(value, "value", out var nested))
            value = nested;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static bool ReadBoolean(JsonElement source, string name) =>
        TryReadBoolean(source, name, out var result) && result;

    private static bool TryGetProperty(JsonElement source, string name, out JsonElement value)
    {
        if (source.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in source.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static bool ContainsAny(string value, params string[] tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static void ValidateProvenance(
        int candidateIndex,
        string? resourceName,
        string? internalArchivePath,
        string? sourceArchivePath,
        string? sourceArchiveSha256,
        string? dumpJsonSha256)
    {
        if (candidateIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(candidateIndex));
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(internalArchivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceArchivePath);
        if (!IsSha256(sourceArchiveSha256) || !IsSha256(dumpJsonSha256))
            throw new ArgumentException("Valid source archive and dumped JSON SHA-256 values are required.");
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
