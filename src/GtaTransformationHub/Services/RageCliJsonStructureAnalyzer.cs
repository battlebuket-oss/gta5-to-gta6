using System.Text.Json;

namespace GtaTransformationHub.Services;

public sealed record RageCliJsonStructureSummary(
    int ObjectCount,
    int ArrayCount,
    int PropertyCount,
    int StringValueCount,
    int NumberValueCount,
    int BooleanValueCount,
    int NullValueCount,
    int MaximumDepth,
    int TruncatedPropertyNameCount,
    bool PropertyNameLimitReached,
    bool NodeLimitReached,
    IReadOnlyDictionary<string, int> PropertyNameFrequency);

/// <summary>
/// Produces a bounded structural index of a rage-cli JSON dump. It deliberately
/// records JSON shape and property-name frequency only; it does not infer species,
/// habitat, population or water semantics from untrusted field names.
/// </summary>
public static class RageCliJsonStructureAnalyzer
{
    public const int MaximumDepth = 128;
    public const int MaximumNodes = 5_000_000;
    public const int MaximumUniquePropertyNames = 4096;
    public const int MaximumPropertyNameLength = 256;

    public static RageCliJsonStructureSummary Analyze(JsonElement root)
    {
        var objectCount = 0;
        var arrayCount = 0;
        var propertyCount = 0;
        var stringCount = 0;
        var numberCount = 0;
        var booleanCount = 0;
        var nullCount = 0;
        var maximumDepth = 0;
        var truncatedPropertyNameCount = 0;
        var propertyNameLimitReached = false;
        var nodeLimitReached = false;
        var nodeCount = 0;
        var propertyNames = new Dictionary<string, int>(StringComparer.Ordinal);

        void Visit(JsonElement element, int depth)
        {
            if (depth > MaximumDepth)
                throw new InvalidDataException("The metadata JSON structure exceeds the maximum depth of 128.");
            if (++nodeCount > MaximumNodes)
            {
                nodeLimitReached = true;
                throw new InvalidDataException("The metadata JSON structure exceeds the maximum node limit of 5,000,000.");
            }

            maximumDepth = Math.Max(maximumDepth, depth);
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    objectCount++;
                    foreach (var property in element.EnumerateObject())
                    {
                        propertyCount++;
                        var propertyName = property.Name;
                        if (propertyName.Length > MaximumPropertyNameLength)
                        {
                            truncatedPropertyNameCount++;
                            propertyName = propertyName[..MaximumPropertyNameLength] + "...[truncated]";
                        }

                        if (propertyNames.TryGetValue(propertyName, out var count))
                            propertyNames[propertyName] = checked(count + 1);
                        else if (propertyNames.Count < MaximumUniquePropertyNames)
                            propertyNames.Add(propertyName, 1);
                        else
                            propertyNameLimitReached = true;

                        Visit(property.Value, depth + 1);
                    }
                    break;

                case JsonValueKind.Array:
                    arrayCount++;
                    foreach (var item in element.EnumerateArray())
                        Visit(item, depth + 1);
                    break;

                case JsonValueKind.String:
                    stringCount++;
                    break;

                case JsonValueKind.Number:
                    numberCount++;
                    break;

                case JsonValueKind.True:
                case JsonValueKind.False:
                    booleanCount++;
                    break;

                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    nullCount++;
                    break;
            }
        }

        Visit(root, 0);
        return new RageCliJsonStructureSummary(
            objectCount,
            arrayCount,
            propertyCount,
            stringCount,
            numberCount,
            booleanCount,
            nullCount,
            maximumDepth,
            truncatedPropertyNameCount,
            propertyNameLimitReached,
            nodeLimitReached,
            propertyNames);
    }
}
