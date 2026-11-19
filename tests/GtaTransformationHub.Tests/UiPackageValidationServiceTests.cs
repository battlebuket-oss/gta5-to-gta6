using GtaTransformationHub.Models;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class UiPackageValidationServiceTests
{
    [Fact]
    public void Validate_AcceptsCompleteInGameUiDescriptor()
    {
        Assert.Empty(UiPackageValidationService.Validate(CreateManifest()));
    }

    [Fact]
    public void Validate_RejectsUiImpactWithoutDescriptor()
    {
        var errors = UiPackageValidationService.Validate(CreateManifest() with
        {
            Ui = null
        });

        Assert.Contains(errors, error => error.Contains("no Ui descriptor", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsCompanionOnlyAsAnInGameReplacement()
    {
        var manifest = CreateManifest() with
        {
            Ui = CreateDescriptor() with { ImplementationType = "CompanionOnly" }
        };

        Assert.Contains(UiPackageValidationService.Validate(manifest),
            error => error.Contains("Companion-only overlays", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsUiThatDoesNotPreserveNativeActions()
    {
        var manifest = CreateManifest() with
        {
            Ui = CreateDescriptor() with { NativeActionsPreserved = false }
        };

        Assert.Contains(UiPackageValidationService.Validate(manifest),
            error => error.Contains("native GTA V actions", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsNonHttpsEvidence()
    {
        var manifest = CreateManifest() with
        {
            Ui = CreateDescriptor() with { RollbackEvidenceUri = "http://example.com/rollback" }
        };

        Assert.Contains(UiPackageValidationService.Validate(manifest),
            error => error.Contains("HTTPS rollback evidence", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateSelection_RejectsCompetingReplacementsForSameSurface()
    {
        var first = CreatePackage("hud-stack-a", "HUD A", "hud");
        var second = CreatePackage("hud-stack-b", "HUD B", "hud");

        var errors = UiPackageValidationService.ValidateSelection(new[] { first, second });

        Assert.Contains(errors, error => error.Contains("Select one implementation per surface", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateSelection_AllowsIndependentUiSurfaces()
    {
        var hud = CreatePackage("hud-stack", "HUD", "hud");
        var map = CreatePackage("map-stack", "Map", "map");

        Assert.Empty(UiPackageValidationService.ValidateSelection(new[] { hud, map }));
    }

    private static ImportedPackage CreatePackage(string id, string name, string surface)
    {
        var manifest = CreateManifest() with
        {
            Id = id,
            Name = name,
            Ui = CreateDescriptor() with { Surfaces = new[] { surface } }
        };

        return new ImportedPackage(manifest, Path.GetTempPath(), new string('A', 64), DateTimeOffset.UtcNow);
    }

    private static PackageManifest CreateManifest() => new(
        1, "ui-test-package", "UI Test Package", "Test Author",
        "https://example.com/ui-package", "Test-only licence", "1.0.0",
        "GTA V Legacy", "1.0.1000.0", "1.0.9999.0",
        Array.Empty<string>(), Array.Empty<string>(),
        new[] { new PackageManifestFile("payload/ui.dat", "mods/ui.dat", new string('A', 64), 12) })
    {
        ImpactAreas = new[] { "interface" },
        Ui = CreateDescriptor()
    };

    private static UiPackageDescriptor CreateDescriptor() => new(
        new[] { "hud", "radial" },
        "GameScript",
        new[] { "KeyboardMouse", "Controller" },
        new[] { "1920x1080", "2560x1440" },
        new[] { "KeyboardNavigation", "ControllerNavigation", "VisibleFocus", "HighContrast", "SubtitleSafeArea" },
        true, true, true, true, true,
        "https://example.com/compatibility",
        "https://example.com/rollback");
}
