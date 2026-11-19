using System.Text.RegularExpressions;
using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

/// <summary>
/// Validates the technical claims required for actual in-game UI packages and prevents
/// multiple selected packages from replacing the same native UI surface.
/// This validates declarations; it does not prove that the evidence is true.
/// </summary>
public static class UiPackageValidationService
{
    private static readonly HashSet<string> SupportedSurfaces = new(StringComparer.OrdinalIgnoreCase)
    {
        "title", "frontend", "pause", "map", "settings", "hud", "phone", "radial", "loading", "accessibility"
    };

    private static readonly HashSet<string> ImplementationTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Scaleform", "GameScript", "AssetReplacement"
    };

    private static readonly HashSet<string> InputMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "KeyboardMouse", "Controller"
    };

    private static readonly HashSet<string> ResolutionTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "1920x1080", "2560x1440", "3840x2160", "3440x1440"
    };

    private static readonly HashSet<string> AccessibilityFeatures = new(StringComparer.OrdinalIgnoreCase)
    {
        "KeyboardNavigation", "ControllerNavigation", "VisibleFocus", "HighContrast",
        "ScalableText", "ReducedMotion", "SubtitleSafeArea", "ColorIndependentStatus"
    };

    private static readonly Regex SurfaceIdPattern = new(
        "^[a-z][a-z0-9-]{1,31}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool DeclaresUiImpact(PackageManifest manifest) =>
        manifest.ImpactAreas?.Any(IsUiImpactArea) == true;

    public static IReadOnlyList<string> Validate(PackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var errors = new List<string>();
        var declaresUiImpact = DeclaresUiImpact(manifest);
        var descriptor = manifest.Ui;

        if (descriptor is null)
        {
            if (declaresUiImpact)
                errors.Add(manifest.Name + " declares UI/interface impact but has no Ui descriptor.");
            return errors;
        }

        if (!declaresUiImpact)
            errors.Add(manifest.Name + " contains Ui metadata but does not declare an 'interface' impact area.");

        ValidateSet(descriptor.Surfaces, "UI surfaces", SupportedSurfaces, errors, requireAtLeastOne: true);
        ValidateSet(descriptor.InputMethods, "UI input methods", InputMethods, errors, requireAtLeastOne: true);
        ValidateSet(descriptor.ResolutionTargets, "UI resolution targets", ResolutionTargets, errors, requireAtLeastOne: true);
        ValidateSet(descriptor.AccessibilityFeatures, "UI accessibility features", AccessibilityFeatures, errors, requireAtLeastOne: true);

        if (!ImplementationTypes.Contains(descriptor.ImplementationType ?? string.Empty))
            errors.Add(manifest.Name + " uses an unsupported UI implementation type. Companion-only overlays are not in-game replacements.");

        if (!descriptor.NativeActionsPreserved)
            errors.Add(manifest.Name + " does not prove that native GTA V actions are preserved.");
        if (!descriptor.MissionTransitionsPreserved)
            errors.Add(manifest.Name + " does not declare mission-transition preservation.");
        if (!descriptor.CutscenesPreserved)
            errors.Add(manifest.Name + " does not declare cutscene preservation.");
        if (!descriptor.SubtitlesPreserved)
            errors.Add(manifest.Name + " does not declare subtitle preservation.");
        if (!descriptor.OriginalCampaignPreserved)
            errors.Add(manifest.Name + " does not declare original-campaign preservation.");

        ValidateEvidenceUri(descriptor.CompatibilityEvidenceUri, "compatibility evidence", manifest.Name, errors);
        ValidateEvidenceUri(descriptor.RollbackEvidenceUri, "rollback evidence", manifest.Name, errors);

        foreach (var surface in descriptor.Surfaces ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(surface) || !SurfaceIdPattern.IsMatch(surface))
                errors.Add(manifest.Name + " contains an invalid UI surface identifier.");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateSelection(IReadOnlyCollection<ImportedPackage> selectedPackages)
    {
        ArgumentNullException.ThrowIfNull(selectedPackages);
        var errors = new List<string>();
        var surfaceOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var package in selectedPackages)
        {
            if (package?.Manifest?.Ui is not { } ui)
                continue;

            foreach (var surface in ui.Surfaces ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(surface))
                    continue;

                if (surfaceOwners.TryGetValue(surface, out var existing))
                    errors.Add("UI surface '" + surface + "' is replaced by both " + existing +
                               " and " + package.Manifest.Name + ". Select one implementation per surface.");
                else
                    surfaceOwners.Add(surface, package.Manifest.Name);
            }
        }

        return errors;
    }

    private static bool IsUiImpactArea(string area) =>
        string.Equals(area, "interface", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(area, "ui", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(area, "hud", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(area, "map", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(area, "frontend", StringComparison.OrdinalIgnoreCase);

    private static void ValidateSet(
        IReadOnlyList<string>? values,
        string label,
        HashSet<string> allowed,
        ICollection<string> errors,
        bool requireAtLeastOne)
    {
        if (values is null || (requireAtLeastOne && values.Count == 0))
        {
            errors.Add(label + " must be declared.");
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || !allowed.Contains(value) || !seen.Add(value))
                errors.Add(label + " contain an unsupported or duplicate value.");
        }
    }

    private static void ValidateEvidenceUri(
        string? value,
        string label,
        string packageName,
        ICollection<string> errors)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            errors.Add(packageName + " must provide an HTTPS " + label + " reference.");
    }
}
