using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class StoryTestEvidenceServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "GtaHubStoryEvidenceTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Validate_RequiresEveryScenarioAndExactBuildFingerprints()
    {
        var record = CreateRecord();
        Assert.Empty(StoryTestEvidenceService.Validate(record));

        var missing = record with
        {
            Scenarios = record.Scenarios.Skip(1).ToArray()
        };
        Assert.Contains(StoryTestEvidenceService.Validate(missing),
            error => error.Contains("exactly one result", StringComparison.Ordinal));

        var badFingerprint = record with { GameExecutableSha256 = "not-a-hash" };
        Assert.Contains(StoryTestEvidenceService.Validate(badFingerprint),
            error => error.Contains("GTA5.exe SHA-256", StringComparison.Ordinal));
    }

    [Fact]
    public void Summarize_RequiresAllScenariosToPass()
    {
        var passing = CreateRecord() with
        {
            Scenarios = StoryTestEvidenceService.CreateScenarioResults()
                .Select(item => new StoryTestScenarioResult
                {
                    ScenarioId = item.ScenarioId,
                    Name = item.Name,
                    Outcome = StoryTestOutcome.Passed
                }).ToArray()
        };
        var passSummary = StoryTestEvidenceService.Summarize(passing);
        Assert.True(passSummary.IsCompletePass);
        Assert.Equal(passSummary.Total, passSummary.Passed);

        var failed = passing with
        {
            Scenarios = passing.Scenarios.Select((item, index) => index == 0
                ? new StoryTestScenarioResult
                {
                    ScenarioId = item.ScenarioId,
                    Name = item.Name,
                    Outcome = StoryTestOutcome.Failed,
                    Notes = "Reproducible mission start failure"
                }
                : item).ToArray()
        };
        Assert.False(StoryTestEvidenceService.Summarize(failed).IsCompletePass);
    }

    [Fact]
    public async Task SaveAsync_StoresEvidenceAndListFiltersByPackageAndExecutableFingerprint()
    {
        var service = new StoryTestEvidenceService(_root);
        var record = CreateRecord();

        await service.SaveAsync(record);

        var matching = await service.ListAsync(record.PackageId, record.PackageVersion, record.GameExecutableSha256);
        Assert.Single(matching);
        Assert.Equal(record.RecordId, matching[0].RecordId);

        var otherExecutable = await service.ListAsync(
            record.PackageId, record.PackageVersion, new string('C', 64));
        Assert.Empty(otherExecutable);

        var otherPackage = await service.ListAsync("other-story-pack", record.PackageVersion);
        Assert.Empty(otherPackage);
    }

    [Fact]
    public void Summarize_MalformedScenarioListNeverThrowsOrPasses()
    {
        var scenarios = StoryTestEvidenceService.CreateScenarioResults()
            .Cast<StoryTestScenarioResult?>()
            .Append(null)
            .Select(item => item!)
            .ToArray();
        var record = CreateRecord() with { Scenarios = scenarios };

        var summary = StoryTestEvidenceService.Summarize(record);

        Assert.False(summary.IsCompletePass);
        Assert.Equal(StoryTestEvidenceService.GetRequiredScenarios().Count, summary.Total);
        Assert.Contains(StoryTestEvidenceService.Validate(record),
            error => error.Contains("empty test scenario", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaveAsync_RejectsMalformedEvidenceBeforeWriting()
    {
        var service = new StoryTestEvidenceService(_root);
        var invalid = CreateRecord() with { PackageArchiveSha256 = "invalid" };

        await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsync(invalid));
        Assert.False(Directory.Exists(_root));
    }

    private static StoryTestEvidenceRecord CreateRecord() => new(
        Guid.NewGuid().ToString("N"),
        "community-story-pack",
        "1.0.0",
        new string('A', 64),
        "1.0.1000.0",
        new string('B', 64),
        DateTimeOffset.UtcNow,
        "Windows 11; RTX GPU; 32 GB RAM; Steam; 1920x1080",
        "Disposable copy and save profile used.",
        StoryTestEvidenceService.CreateScenarioResults());

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
