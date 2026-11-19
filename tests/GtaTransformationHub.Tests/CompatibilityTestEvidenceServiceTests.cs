using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class CompatibilityTestEvidenceServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "GtaHubCompatibilityEvidenceTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Validate_AllowsVanillaBaselineAndIncompleteScenarioResults()
    {
        var record = CreateRecord();

        Assert.Empty(CompatibilityTestEvidenceService.Validate(record));
        var summary = CompatibilityTestEvidenceService.Summarize(record);
        Assert.False(summary.IsCompletePass);
        Assert.Equal(CompatibilityTestEvidenceService.GetRequiredScenarios().Count, summary.NotRun);
    }

    [Fact]
    public void Summarize_RequiresEveryScenarioAndMeasuredPerformanceToPass()
    {
        var scenarios = CompatibilityTestEvidenceService.CreateScenarioResults()
            .Select(item => new CompatibilityTestScenarioResult
            {
                ScenarioId = item.ScenarioId,
                Name = item.Name,
                Outcome = CompatibilityTestOutcome.Passed
            }).ToArray();

        var passing = CreateRecord() with
        {
            Scenarios = scenarios,
            Performance = new CompatibilityPerformanceMetrics(
                WarmupCompleted: true,
                BenchmarkRepeats: 3,
                AverageFps: 91.2,
                OnePercentLowFps: 63.1,
                FrameTimeP95Ms: 18.4,
                FrameTimeP99Ms: 25.5,
                PeakSystemRamMb: 14500,
                PeakVramMb: 5700,
                PeakGpuTemperatureC: 72,
                PeakCpuTemperatureC: 83,
                SoakDurationMinutes: 60)
        };

        var summary = CompatibilityTestEvidenceService.Summarize(passing);
        Assert.True(summary.IsCompletePass);
        Assert.Equal(summary.Total, summary.Passed);

        var incompleteSoak = passing with
        {
            Performance = passing.Performance with { SoakDurationMinutes = 59 }
        };
        Assert.False(CompatibilityTestEvidenceService.Summarize(incompleteSoak).IsCompletePass);
    }

    [Fact]
    public void Validate_RejectsPassedBenchmarkWithoutWarmupRepeatsAndMetrics()
    {
        var scenarios = CompatibilityTestEvidenceService.CreateScenarioResults()
            .Select(item => new CompatibilityTestScenarioResult
            {
                ScenarioId = item.ScenarioId,
                Name = item.Name,
                Outcome = item.ScenarioId == "performance-benchmark"
                    ? CompatibilityTestOutcome.Passed
                    : CompatibilityTestOutcome.NotRun
            }).ToArray();
        var record = CreateRecord() with
        {
            Scenarios = scenarios,
            Performance = new CompatibilityPerformanceMetrics(
                WarmupCompleted: false,
                BenchmarkRepeats: 1,
                AverageFps: null,
                OnePercentLowFps: null,
                FrameTimeP95Ms: null,
                FrameTimeP99Ms: null,
                PeakSystemRamMb: null,
                PeakVramMb: null,
                PeakGpuTemperatureC: null,
                PeakCpuTemperatureC: null,
                SoakDurationMinutes: 0)
        };

        var errors = CompatibilityTestEvidenceService.Validate(record);
        Assert.Contains(errors, error => error.Contains("at least 3 repeats", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsInvalidOrDuplicatePackageStackEntries()
    {
        var package = new CompatibilityTestPackage
        {
            PackageId = "test-mod",
            PackageName = "Test Mod",
            PackageVersion = "1.0.0",
            ArchiveSha256 = new string('A', 64),
            SignatureStatus = "TrustedPublisher"
        };
        var record = CreateRecord() with
        {
            Packages = new[] { package, package }
        };

        Assert.Contains(CompatibilityTestEvidenceService.Validate(record),
            error => error.Contains("invalid or duplicate package ID", StringComparison.Ordinal));
    }

    [Fact]
    public void Summarize_MalformedScenarioListNeverThrowsOrPasses()
    {
        var scenarios = CompatibilityTestEvidenceService.CreateScenarioResults()
            .Select(item => new CompatibilityTestScenarioResult
            {
                ScenarioId = item.ScenarioId,
                Name = item.Name,
                Outcome = CompatibilityTestOutcome.Passed
            })
            .Cast<CompatibilityTestScenarioResult?>()
            .Append(null)
            .Select(item => item!)
            .ToArray();

        var record = CreateRecord() with { Scenarios = scenarios };

        var summary = CompatibilityTestEvidenceService.Summarize(record);

        Assert.False(summary.IsCompletePass);
        Assert.Equal(summary.Total, summary.Passed);
        Assert.Contains(CompatibilityTestEvidenceService.Validate(record),
            error => error.Contains("empty scenario", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaveAndList_FiltersEvidenceByExactGta5ExecutableFingerprint()
    {
        var service = new CompatibilityTestEvidenceService(_root);
        var record = CreateRecord();

        await service.SaveAsync(record);

        var matching = await service.ListAsync(record.GameExecutableSha256);
        Assert.Single(matching);
        Assert.Equal(record.RecordId, matching[0].RecordId);

        var otherBuild = await service.ListAsync(new string('C', 64));
        Assert.Empty(otherBuild);
    }

    [Fact]
    public async Task SaveAsync_RejectsMalformedEvidenceBeforeWriting()
    {
        var service = new CompatibilityTestEvidenceService(_root);
        var invalid = CreateRecord() with { GameExecutableSha256 = "bad-hash" };

        await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsync(invalid));
        Assert.False(Directory.Exists(_root));
    }

    private static CompatibilityTestEvidenceRecord CreateRecord() => new(
        Guid.NewGuid().ToString("N"),
        "1.0.3889.0",
        new string('B', 64),
        "Vanilla baseline",
        DateTimeOffset.UtcNow,
        "Windows 11; CPU and GPU details pending; 32 GB RAM; 1920x1080; Steam",
        "Tester-authored validation record.",
        Array.Empty<CompatibilityTestPackage>(),
        new CompatibilityPerformanceMetrics(
            false, 0, null, null, null, null, null, null, null, null, 0),
        CompatibilityTestEvidenceService.CreateScenarioResults());

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
