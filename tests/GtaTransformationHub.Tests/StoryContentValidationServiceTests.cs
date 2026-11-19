using GtaTransformationHub.Models;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class StoryContentValidationServiceTests
{
    [Fact]
    public void Validate_AcceptsCompleteCommunityMissionPackDescriptor()
    {
        var errors = StoryContentValidationService.Validate(CreateManifest());
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsDuplicateMissionIdentifiers()
    {
        var manifest = CreateManifest() with
        {
            StoryContent = CreateDescriptor() with { MissionIds = new[] { "mission-01", "mission-01" } }
        };
        var errors = StoryContentValidationService.Validate(manifest);
        Assert.Contains(errors, error => error.Contains("invalid or duplicate identifier"));
    }

    [Fact]
    public void Validate_RejectsUnsupportedContentType()
    {
        var manifest = CreateManifest() with
        {
            StoryContent = CreateDescriptor() with { ContentType = "GTA6CampaignReplica" }
        };
        var errors = StoryContentValidationService.Validate(manifest);
        Assert.Contains(errors, error => error.Contains("unsupported Story Mode content type"));
    }

    [Fact]
    public void Validate_RequiresBackupWhenUsingVanillaSave()
    {
        var manifest = CreateManifest() with
        {
            StoryContent = CreateDescriptor() with
            {
                SaveCompatibility = "UsesVanillaSave",
                RequiresSaveBackup = false
            }
        };
        var errors = StoryContentValidationService.Validate(manifest);
        Assert.Contains(errors, error => error.Contains("must require a save backup"));
    }

    [Fact]
    public void Validate_RejectsPackagesThatDoNotPreserveOriginalCampaign()
    {
        var manifest = CreateManifest() with
        {
            StoryContent = CreateDescriptor() with { PreservesOriginalCampaign = false }
        };
        var errors = StoryContentValidationService.Validate(manifest);
        Assert.Contains(errors, error => error.Contains("does not declare preservation"));
    }

    [Fact]
    public void Validate_RequiresStoryImpactClassification()
    {
        var manifest = CreateManifest() with { ImpactAreas = new[] { "gameplay" } };
        var errors = StoryContentValidationService.Validate(manifest);
        Assert.Contains(errors, error => error.Contains("does not declare the 'story' impact area"));
    }

    [Fact]
    public void Validate_RejectsInvalidLanguageTags()
    {
        var manifest = CreateManifest() with
        {
            StoryContent = CreateDescriptor() with { SubtitleLanguages = new[] { "not a locale" } }
        };
        var errors = StoryContentValidationService.Validate(manifest);
        Assert.Contains(errors, error => error.Contains("BCP-47 language tag"));
    }

    private static StoryContentDescriptor CreateDescriptor() => new(
        "MissionPack",
        new[] { "mission-01", "mission-02" },
        new[] { "Any" },
        new[] { "downtown-bank" },
        "community-story-progression",
        "SeparateNamespace",
        true,
        true,
        new[] { "en-US" },
        new[] { "en-US", "en-GB" },
        "https://example.com/test-evidence");

    private static PackageManifest CreateManifest() => new(
        1,
        "community-story-pack",
        "Community Story Pack",
        "Test Author",
        "https://example.com/story-pack",
        "Test-only fixture licence",
        "1.0.0",
        "GTA V Legacy",
        "1.0.1000.0",
        "1.0.9999.0",
        Array.Empty<string>(),
        Array.Empty<string>(),
        new[] { new PackageManifestFile("scripts/story.dll", "scripts/story.dll", new string('A', 64), 128) })
    {
        ImpactAreas = new[] { "story" },
        StoryContent = CreateDescriptor()
    };
}
