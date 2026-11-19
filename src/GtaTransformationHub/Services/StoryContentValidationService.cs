using System.Text.RegularExpressions;
using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

/// <summary>
/// Validates metadata for community-authored GTA V Story Mode campaigns and mission packs.
/// It does not create missions or claim compatibility with Rockstar's GTA VI campaign.
/// </summary>
public static class StoryContentValidationService
{
    private static readonly HashSet<string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Campaign", "MissionPack", "SideMission", "CharacterArc", "ActivityPack", "AmbientEvent"
    };

    private static readonly HashSet<string> SaveCompatibilityModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SeparateNamespace", "NoPersistentData", "UsesVanillaSave"
    };

    private static readonly Regex LanguageTagPattern = new(
        "^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<string> Validate(PackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var descriptor = manifest.StoryContent;
        var errors = new List<string>();

        var declaresStoryImpact = manifest.ImpactAreas?.Any(area =>
            string.Equals(area, "story", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(area, "campaign", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(area, "missions", StringComparison.OrdinalIgnoreCase)) == true;

        if (descriptor is null)
        {
            if (declaresStoryImpact)
                errors.Add(manifest.Name + " declares Story Mode impact but has no StoryContent descriptor.");
            return errors;
        }

        if (!ContentTypes.Contains(descriptor.ContentType ?? string.Empty))
            errors.Add(manifest.Name + " has an unsupported Story Mode content type.");

        if (descriptor.MissionIds is null || descriptor.MissionIds.Count == 0)
        {
            if (!string.Equals(descriptor.ContentType, "ActivityPack", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(descriptor.ContentType, "AmbientEvent", StringComparison.OrdinalIgnoreCase))
                errors.Add(manifest.Name + " must declare at least one mission ID.");
        }
        else
        {
            ValidateIdentifiers(descriptor.MissionIds, manifest.Name + " mission IDs", errors);
        }

        if (descriptor.ProtagonistRequirements is null || descriptor.ProtagonistRequirements.Count == 0)
            errors.Add(manifest.Name + " must declare protagonist requirements (use 'Any' when unrestricted).");
        else
            ValidateLabels(descriptor.ProtagonistRequirements, manifest.Name + " protagonist requirements", errors);

        if (descriptor.StartLocations is null || descriptor.StartLocations.Count == 0)
            errors.Add(manifest.Name + " must declare at least one mission/activity start location.");
        else
            ValidateLabels(descriptor.StartLocations, manifest.Name + " start locations", errors);

        if (!PackageIdValidator.IsValid(descriptor.ProgressionNamespace))
            errors.Add(manifest.Name + " has an invalid progression namespace.");

        if (!SaveCompatibilityModes.Contains(descriptor.SaveCompatibility ?? string.Empty))
            errors.Add(manifest.Name + " has an unsupported Story Mode save-compatibility mode.");

        if (string.Equals(descriptor.SaveCompatibility, "UsesVanillaSave", StringComparison.OrdinalIgnoreCase) &&
            !descriptor.RequiresSaveBackup)
            errors.Add(manifest.Name + " uses the vanilla save and must require a save backup before activation.");

        if (!descriptor.PreservesOriginalCampaign)
            errors.Add(manifest.Name + " does not declare preservation of the original GTA V campaign.");

        ValidateLanguages(descriptor.VoiceLanguages, manifest.Name + " voice languages", errors);
        ValidateLanguages(descriptor.SubtitleLanguages, manifest.Name + " subtitle languages", errors);

        if (!string.IsNullOrWhiteSpace(descriptor.TestEvidenceUri) &&
            (!Uri.TryCreate(descriptor.TestEvidenceUri, UriKind.Absolute, out var evidence) ||
             evidence.Scheme != Uri.UriSchemeHttps))
            errors.Add(manifest.Name + " has a non-HTTPS Story Mode test-evidence reference.");

        if (!declaresStoryImpact)
            errors.Add(manifest.Name + " contains StoryContent metadata but does not declare the 'story' impact area.");

        return errors;
    }

    private static void ValidateIdentifiers(IReadOnlyList<string> values, string label, ICollection<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (!PackageIdValidator.IsValid(value) || !seen.Add(value))
                errors.Add(label + " contain an invalid or duplicate identifier.");
        }
    }

    private static void ValidateLabels(IReadOnlyList<string> values, string label, ICollection<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 120 || !seen.Add(value.Trim()))
                errors.Add(label + " contain an empty, overlong or duplicate value.");
        }
    }

    private static void ValidateLanguages(IReadOnlyList<string> values, string label, ICollection<string> errors)
    {
        if (values is null)
        {
            errors.Add(label + " must be declared, even when the list is empty.");
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || !LanguageTagPattern.IsMatch(value) || !seen.Add(value))
                errors.Add(label + " contain an invalid or duplicate BCP-47 language tag.");
        }
    }
}
