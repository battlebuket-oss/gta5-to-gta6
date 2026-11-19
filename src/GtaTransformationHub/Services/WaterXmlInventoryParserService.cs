using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace GtaTransformationHub.Services;

public sealed record WaterQuadInventoryItem(
    float MinX,
    float MaxX,
    float MinY,
    float MaxY,
    int Type,
    bool IsInvisible,
    bool HasLimitedDepth,
    float Z,
    float Alpha1,
    float Alpha2,
    float Alpha3,
    float Alpha4,
    bool NoStencil);

public sealed record WaterCalmingQuadInventoryItem(
    float MinX,
    float MaxX,
    float MinY,
    float MaxY,
    float Dampening);

public sealed record WaterWaveQuadInventoryItem(
    float MinX,
    float MaxX,
    float MinY,
    float MaxY,
    float Amplitude,
    float XDirection,
    float YDirection);

/// <summary>
/// Semantic inventory of the water surface XML consumed by GTA V.
/// The records preserve water surfaces, depth flags, corner alpha, calming and wave definitions.
/// </summary>
public sealed record WaterXmlInventory(
    string FileName,
    bool HasWaterQuadsSection,
    bool HasCalmingQuadsSection,
    bool HasWaveQuadsSection,
    IReadOnlyList<WaterQuadInventoryItem> WaterQuads,
    IReadOnlyList<WaterCalmingQuadInventoryItem> CalmingQuads,
    IReadOnlyList<WaterWaveQuadInventoryItem> WaveQuads)
{
    public bool HasAllRequiredSections =>
        HasWaterQuadsSection && HasCalmingQuadsSection && HasWaveQuadsSection;
}

public sealed record WaterXmlInventoryComparison(
    bool VanillaWaterDefinitionsPreserved,
    IReadOnlyList<string> Errors,
    int BaselineFiles,
    int CandidateFiles,
    int BaselineWaterQuads,
    int CandidateWaterQuads,
    int BaselineCalmingQuads,
    int CandidateCalmingQuads,
    int BaselineWaveQuads,
    int CandidateWaveQuads);

/// <summary>
/// Strict parser for the native GTA V water.xml / water_heistisland.xml format.
/// It mirrors the published RAGE WaterData schema: WaterQuads, CalmingQuads and WaveQuads.
/// Any missing/unparseable required field rejects the file rather than silently substituting zero.
/// </summary>
public static class WaterXmlInventoryParserService
{
    public const long MaximumXmlCharacters = 64L * 1024 * 1024;

    private static readonly string[] RequiredFiles =
    [
        "water.xml",
        "water_heistisland.xml"
    ];

    public static IReadOnlyList<string> RequiredWaterFileNames => RequiredFiles;

    public static WaterXmlInventory ParseFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The water XML file was not found.", fullPath);
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("A reparse-point water XML file cannot be parsed.");

        var info = new FileInfo(fullPath);
        if (info.Length <= 0 || info.Length > MaximumXmlCharacters)
            throw new InvalidDataException("The water XML file is empty or exceeds the 64 MB parse limit.");

        using var stream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.SequentialScan);
        return Parse(Path.GetFileName(fullPath), stream);
    }

    public static WaterXmlInventory Parse(string fileName, string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(xml);
        if (xml.Length == 0 || xml.Length > MaximumXmlCharacters)
            throw new InvalidDataException("The water XML input is empty or exceeds the 64 MB parse limit.");

        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, CreateReaderSettings());
        var document = XDocument.Load(reader, LoadOptions.None);
        return ParseDocument(fileName, document);
    }

    public static WaterXmlInventory Parse(string fileName, Stream xmlStream)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(xmlStream);
        using var reader = XmlReader.Create(xmlStream, CreateReaderSettings());
        var document = XDocument.Load(reader, LoadOptions.None);
        return ParseDocument(fileName, document);
    }

    public static WaterXmlInventoryComparison CompareRequiredWaterFiles(
        IReadOnlyCollection<WaterXmlInventory> baseline,
        IReadOnlyCollection<WaterXmlInventory> candidate)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        var errors = new List<string>();

        var baselineByName = IndexRequiredFiles(baseline, "Baseline", errors);
        var candidateByName = IndexRequiredFiles(candidate, "Candidate", errors);

        foreach (var requiredName in RequiredFiles)
        {
            if (!baselineByName.TryGetValue(requiredName, out var original) ||
                !candidateByName.TryGetValue(requiredName, out var current))
                continue;

            if (!original.HasAllRequiredSections || !current.HasAllRequiredSections)
                errors.Add(requiredName + " is missing one or more water definition sections.");

            if (!original.WaterQuads.SequenceEqual(current.WaterQuads))
                errors.Add(requiredName + " changed one or more water surface quads, depths, flags, heights or corner alpha values.");
            if (!original.CalmingQuads.SequenceEqual(current.CalmingQuads))
                errors.Add(requiredName + " changed one or more wave-calming regions or dampening values.");
            if (!original.WaveQuads.SequenceEqual(current.WaveQuads))
                errors.Add(requiredName + " changed one or more wave regions, amplitudes or directions.");
        }

        var baselineWater = baseline.Sum(item => item.WaterQuads.Count);
        var candidateWater = candidate.Sum(item => item.WaterQuads.Count);
        var baselineCalming = baseline.Sum(item => item.CalmingQuads.Count);
        var candidateCalming = candidate.Sum(item => item.CalmingQuads.Count);
        var baselineWaves = baseline.Sum(item => item.WaveQuads.Count);
        var candidateWaves = candidate.Sum(item => item.WaveQuads.Count);

        return new WaterXmlInventoryComparison(
            errors.Count == 0 &&
            baselineByName.Count == RequiredFiles.Length &&
            candidateByName.Count == RequiredFiles.Length,
            errors,
            baseline.Count,
            candidate.Count,
            baselineWater,
            candidateWater,
            baselineCalming,
            candidateCalming,
            baselineWaves,
            candidateWaves);
    }

    private static Dictionary<string, WaterXmlInventory> IndexRequiredFiles(
        IReadOnlyCollection<WaterXmlInventory> inventories,
        string label,
        ICollection<string> errors)
    {
        var result = new Dictionary<string, WaterXmlInventory>(StringComparer.OrdinalIgnoreCase);
        foreach (var inventory in inventories)
        {
            if (inventory is null ||
                !RequiredFiles.Contains(inventory.FileName, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(label + " water inventory contains an unexpected file.");
                continue;
            }

            if (!result.TryAdd(inventory.FileName, inventory))
                errors.Add(label + " water inventory contains multiple records for " + inventory.FileName + ".");
        }

        foreach (var requiredName in RequiredFiles)
        {
            if (!result.ContainsKey(requiredName))
                errors.Add(label + " water inventory is missing " + requiredName + ".");
        }

        return result;
    }

    private static WaterXmlInventory ParseDocument(string fileName, XDocument document)
    {
        if (!RequiredFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Only water.xml and water_heistisland.xml are accepted by this inventory parser.");

        var root = document.Root;
        if (root is null || !root.Name.LocalName.Equals("WaterData", StringComparison.Ordinal))
            throw new InvalidDataException("The water XML document root must be WaterData.");

        var waterSection = root.Elements().SingleOrDefault(item => item.Name.LocalName == "WaterQuads");
        var calmingSection = root.Elements().SingleOrDefault(item => item.Name.LocalName == "CalmingQuads");
        var waveSection = root.Elements().SingleOrDefault(item => item.Name.LocalName == "WaveQuads");

        if (root.Elements().Count(item => item.Name.LocalName == "WaterQuads") > 1 ||
            root.Elements().Count(item => item.Name.LocalName == "CalmingQuads") > 1 ||
            root.Elements().Count(item => item.Name.LocalName == "WaveQuads") > 1)
            throw new InvalidDataException("The water XML document contains duplicate quad list sections.");

        var water = waterSection?.Elements()
            .Where(item => item.Name.LocalName == "Item")
            .Select(ReadWaterQuad)
            .ToArray() ?? Array.Empty<WaterQuadInventoryItem>();

        var calming = calmingSection?.Elements()
            .Where(item => item.Name.LocalName == "Item")
            .Select(ReadCalmingQuad)
            .ToArray() ?? Array.Empty<WaterCalmingQuadInventoryItem>();

        var waves = waveSection?.Elements()
            .Where(item => item.Name.LocalName == "Item")
            .Select(ReadWaveQuad)
            .ToArray() ?? Array.Empty<WaterWaveQuadInventoryItem>();

        return new WaterXmlInventory(
            fileName,
            waterSection is not null,
            calmingSection is not null,
            waveSection is not null,
            water,
            calming,
            waves);
    }

    private static WaterQuadInventoryItem ReadWaterQuad(XElement item)
    {
        var result = new WaterQuadInventoryItem(
            ReadFloat(item, "minX"),
            ReadFloat(item, "maxX"),
            ReadFloat(item, "minY"),
            ReadFloat(item, "maxY"),
            ReadInt(item, "Type"),
            ReadBool(item, "IsInvisible"),
            ReadBool(item, "HasLimitedDepth"),
            ReadFloat(item, "z"),
            ReadFloat(item, "a1"),
            ReadFloat(item, "a2"),
            ReadFloat(item, "a3"),
            ReadFloat(item, "a4"),
            ReadBool(item, "NoStencil"));
        ValidateBounds(result.MinX, result.MaxX, result.MinY, result.MaxY, "WaterQuads");
        return result;
    }

    private static WaterCalmingQuadInventoryItem ReadCalmingQuad(XElement item)
    {
        var result = new WaterCalmingQuadInventoryItem(
            ReadFloat(item, "minX"),
            ReadFloat(item, "maxX"),
            ReadFloat(item, "minY"),
            ReadFloat(item, "maxY"),
            ReadFloat(item, "fDampening"));
        ValidateBounds(result.MinX, result.MaxX, result.MinY, result.MaxY, "CalmingQuads");
        return result;
    }

    private static WaterWaveQuadInventoryItem ReadWaveQuad(XElement item)
    {
        var result = new WaterWaveQuadInventoryItem(
            ReadFloat(item, "minX"),
            ReadFloat(item, "maxX"),
            ReadFloat(item, "minY"),
            ReadFloat(item, "maxY"),
            ReadFloat(item, "Amplitude"),
            ReadFloat(item, "XDirection"),
            ReadFloat(item, "YDirection"));
        ValidateBounds(result.MinX, result.MaxX, result.MinY, result.MaxY, "WaveQuads");
        return result;
    }

    private static float ReadFloat(XElement item, string name)
    {
        var raw = ReadRequiredValue(item, name);
        if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !float.IsFinite(value))
            throw new InvalidDataException("Water XML field '" + name + "' is not a finite floating-point value.");
        return value;
    }

    private static int ReadInt(XElement item, string name)
    {
        var raw = ReadRequiredValue(item, name);
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException("Water XML field '" + name + "' is not a valid integer.");
        return value;
    }

    private static bool ReadBool(XElement item, string name)
    {
        var raw = ReadRequiredValue(item, name);
        if (!bool.TryParse(raw, out var value))
            throw new InvalidDataException("Water XML field '" + name + "' is not a valid Boolean.");
        return value;
    }

    private static string ReadRequiredValue(XElement item, string name)
    {
        var element = item.Elements().SingleOrDefault(child => child.Name.LocalName == name);
        var raw = (string?)element?.Attribute("value");
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidDataException("Water XML Item is missing required field '" + name + " value'.");
        return raw;
    }

    private static void ValidateBounds(float minX, float maxX, float minY, float maxY, string section)
    {
        if (minX > maxX || minY > maxY)
            throw new InvalidDataException(section + " contains an inverted rectangle.");
    }

    private static XmlReaderSettings CreateReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaximumXmlCharacters,
        MaxCharactersFromEntities = 0,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        CloseInput = false
    };
}
