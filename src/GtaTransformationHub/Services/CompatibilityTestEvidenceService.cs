using System.Text.Json;
using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

public enum CompatibilityTestOutcome
{
    NotRun,
    Passed,
    Failed,
    Blocked
}

public sealed class CompatibilityTestPackage
{
    public string PackageId { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = string.Empty;
    public string ArchiveSha256 { get; set; } = string.Empty;
    public string SignatureStatus { get; set; } = string.Empty;
}

public sealed class CompatibilityTestScenarioResult
{
    public string ScenarioId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CompatibilityTestOutcome Outcome { get; set; } = CompatibilityTestOutcome.NotRun;
    public string Notes { get; set; } = string.Empty;
}

public sealed record CompatibilityPerformanceMetrics(
    bool WarmupCompleted,
    int BenchmarkRepeats,
    double? AverageFps,
    double? OnePercentLowFps,
    double? FrameTimeP95Ms,
    double? FrameTimeP99Ms,
    long? PeakSystemRamMb,
    long? PeakVramMb,
    double? PeakGpuTemperatureC,
    double? PeakCpuTemperatureC,
    int SoakDurationMinutes);

public sealed record CompatibilityTestEvidenceRecord(
    string RecordId,
    string GameBuild,
    string GameExecutableSha256,
    string ProfileName,
    DateTimeOffset RecordedAtUtc,
    string TestEnvironment,
    string Summary,
    IReadOnlyList<CompatibilityTestPackage> Packages,
    CompatibilityPerformanceMetrics Performance,
    IReadOnlyList<CompatibilityTestScenarioResult> Scenarios);

public sealed record CompatibilityTestEvidenceSummary(
    int Passed,
    int Failed,
    int Blocked,
    int NotRun,
    int Total,
    bool IsCompletePass);

/// <summary>
/// Stores manual compatibility, gameplay, performance and rollback test evidence,
/// pinned to an exact GTA5.exe digest and exact package archive/version stack.
/// This is evidence capture only: it never marks a package compatible or unlocks it.
/// </summary>
public sealed class CompatibilityTestEvidenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly CompatibilityTestScenarioDefinition[] RequiredScenarios =
    [
        new("cold-launch", "Cold launch and launcher hand-off", "Launch from the supported Steam/Rockstar launcher path and check for missing loaders or dependencies."),
        new("story-load-save", "Story Mode load, save and reload", "Load a disposable Story Mode save, save, exit and reload it."),
        new("character-switch", "Character switching", "Switch protagonists and verify camera, animations and mission state."),
        new("dense-city-driving", "Dense city driving", "Exercise traffic, pedestrians, vehicle collisions and streaming in a dense area."),
        new("rural-wildlife", "Countryside and wildlife", "Check terrestrial vegetation, ambient populations, wildlife and world streaming."),
        new("weather-day-night", "Weather and day/night", "Check weather transitions, lighting and time-of-day changes."),
        new("water-shoreline", "Water surface and shoreline", "Check waves, shoreline interaction, water continuity and weather response."),
        new("underwater-ecosystem", "Underwater ecosystem", "Check underwater visibility, depth response, lighting, vegetation and aquatic species."),
        new("animal-population", "Animal and population behaviour", "Check original species, spawn groups, population entries and habitat behaviour."),
        new("police-combat", "Police response and combat", "Check wanted levels, dispatch, pursuits, combat and weapon wheel."),
        new("mission-cutscene-subtitles", "Mission and cutscene transitions", "Check mission start/finish, checkpoints, cutscenes and subtitles."),
        new("pause-map-settings", "Pause, map and settings", "Check pause actions, map pan/zoom/waypoints and settings persistence."),
        new("keyboard-mouse", "Keyboard and mouse", "Check focus, input mapping, menu navigation and exit behaviour."),
        new("controller", "Controller", "Check controller focus, bindings, navigation and exit behaviour."),
        new("exit-relaunch", "Exit and relaunch", "Check no orphaned process, locked transaction or incomplete file operation remains."),
        new("crash-recovery", "Interruption and crash recovery", "Test a disposable install after forced interruption and verify recovery without save loss."),
        new("uninstall-rollback", "Uninstall and rollback", "Remove the tested stack and verify managed files restore without overwriting user changes."),
        new("performance-benchmark", "Repeatable performance benchmark", "Warm up, then repeat the same route at least three times and record frame-time, FPS, RAM and VRAM."),
        new("sixty-minute-soak", "60-minute stability soak", "Run at least 60 minutes of driving, streaming, weather and water scenarios without crash or corruption.")
    ];

    private readonly string _evidenceDirectory;

    public CompatibilityTestEvidenceService(string? evidenceDirectory = null)
    {
        _evidenceDirectory = Path.GetFullPath(evidenceDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "compatibility-test-evidence"));
    }

    public string EvidenceDirectory => _evidenceDirectory;

    public static IReadOnlyList<CompatibilityTestScenarioDefinition> GetRequiredScenarios() =>
        RequiredScenarios.ToArray();

    public static IReadOnlyList<CompatibilityTestScenarioResult> CreateScenarioResults() =>
        RequiredScenarios.Select(scenario => new CompatibilityTestScenarioResult
        {
            ScenarioId = scenario.Id,
            Name = scenario.Name,
            Outcome = CompatibilityTestOutcome.NotRun,
            Notes = string.Empty
        }).ToArray();

    public static IReadOnlyList<string> Validate(CompatibilityTestEvidenceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var errors = new List<string>();

        if (!Guid.TryParseExact(record.RecordId, "N", out _))
            errors.Add("The compatibility evidence record ID is invalid.");
        if (!Version.TryParse(record.GameBuild, out _))
            errors.Add("The compatibility evidence must include the numeric GTA V Legacy build.");
        if (!IsSha256(record.GameExecutableSha256))
            errors.Add("The compatibility evidence must include the exact GTA5.exe SHA-256.");
        if (string.IsNullOrWhiteSpace(record.ProfileName) || record.ProfileName.Length > 160)
            errors.Add("A profile name or 'Vanilla baseline' label is required (160 characters maximum).");
        if (record.RecordedAtUtc == default)
            errors.Add("The compatibility evidence record has no timestamp.");
        if (string.IsNullOrWhiteSpace(record.TestEnvironment) || record.TestEnvironment.Length > 6000)
            errors.Add("The test environment is required and must be 6,000 characters or fewer.");
        if ((record.Summary?.Length ?? 0) > 8000)
            errors.Add("The compatibility test summary must be 8,000 characters or fewer.");

        ValidatePackages(record.Packages, errors);
        ValidatePerformance(record.Performance, record.Scenarios, errors);
        ValidateScenarios(record.Scenarios, errors);

        return errors;
    }

    public static CompatibilityTestEvidenceSummary Summarize(CompatibilityTestEvidenceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var scenarios = record.Scenarios ?? Array.Empty<CompatibilityTestScenarioResult>();
        var passed = scenarios.Count(item => item.Outcome == CompatibilityTestOutcome.Passed);
        var failed = scenarios.Count(item => item.Outcome == CompatibilityTestOutcome.Failed);
        var blocked = scenarios.Count(item => item.Outcome == CompatibilityTestOutcome.Blocked);
        var notRun = scenarios.Count(item => item.Outcome == CompatibilityTestOutcome.NotRun);
        var expected = RequiredScenarios.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var seen = scenarios.Where(item => item is not null)
            .Select(item => item.ScenarioId)
            .ToHashSet(StringComparer.Ordinal);
        var completePass = Validate(record).Count == 0 &&
                           expected.SetEquals(seen) &&
                           passed == RequiredScenarios.Length &&
                           failed == 0 && blocked == 0 && notRun == 0;
        return new CompatibilityTestEvidenceSummary(
            passed, failed, blocked, notRun, RequiredScenarios.Length, completePass);
    }

    public async Task SaveAsync(
        CompatibilityTestEvidenceRecord record,
        CancellationToken cancellationToken = default)
    {
        var errors = Validate(record);
        if (errors.Count > 0)
            throw new InvalidDataException("Compatibility test evidence is invalid: " + string.Join(" ", errors));

        Directory.CreateDirectory(_evidenceDirectory);
        var destination = GetRecordPath(record.RecordId);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, record, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public async Task<IReadOnlyList<CompatibilityTestEvidenceRecord>> ListAsync(
        string gameExecutableSha256,
        CancellationToken cancellationToken = default)
    {
        if (!IsSha256(gameExecutableSha256))
            throw new ArgumentException("A valid GTA5.exe SHA-256 fingerprint is required.", nameof(gameExecutableSha256));
        if (!Directory.Exists(_evidenceDirectory))
            return Array.Empty<CompatibilityTestEvidenceRecord>();

        var records = new List<CompatibilityTestEvidenceRecord>();
        foreach (var path in Directory.EnumerateFiles(_evidenceDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var record = await JsonSerializer.DeserializeAsync<CompatibilityTestEvidenceRecord>(
                stream, JsonOptions, cancellationToken);
            if (record is null || Validate(record).Count > 0 ||
                !record.GameExecutableSha256.Equals(gameExecutableSha256, StringComparison.OrdinalIgnoreCase))
                continue;
            records.Add(record);
        }

        return records.OrderByDescending(record => record.RecordedAtUtc).ToArray();
    }

    private static void ValidatePackages(
        IReadOnlyList<CompatibilityTestPackage>? packages,
        ICollection<string> errors)
    {
        if (packages is null)
        {
            errors.Add("The package stack declaration is missing.");
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
        {
            if (package is null)
            {
                errors.Add("The package stack contains an empty entry.");
                continue;
            }
            if (!PackageIdValidator.IsValid(package.PackageId) ||
                !seen.Add(package.PackageId))
                errors.Add("The package stack contains an invalid or duplicate package ID.");
            if (string.IsNullOrWhiteSpace(package.PackageName) || package.PackageName.Length > 160)
                errors.Add("A package stack entry has no valid display name.");
            if (!Version.TryParse(package.PackageVersion, out _))
                errors.Add("A package stack entry has an invalid numeric version.");
            if (!IsSha256(package.ArchiveSha256))
                errors.Add("A package stack entry has no valid imported archive SHA-256.");
            if (string.IsNullOrWhiteSpace(package.SignatureStatus) ||
                package.SignatureStatus.Length > 80)
                errors.Add("A package stack entry has no signature status.");
        }
    }

    private static void ValidatePerformance(
        CompatibilityPerformanceMetrics? metrics,
        IReadOnlyList<CompatibilityTestScenarioResult>? scenarios,
        ICollection<string> errors)
    {
        if (metrics is null)
        {
            errors.Add("Performance metrics are missing.");
            return;
        }

        if (metrics.BenchmarkRepeats is < 0 or > 100)
            errors.Add("Benchmark repeat count must be between 0 and 100.");
        if (metrics.SoakDurationMinutes is < 0 or > 100000)
            errors.Add("Soak duration is outside the supported range.");

        ValidateOptionalPositive(metrics.AverageFps, "Average FPS", errors);
        ValidateOptionalPositive(metrics.OnePercentLowFps, "1% low FPS", errors);
        ValidateOptionalPositive(metrics.FrameTimeP95Ms, "P95 frame time", errors);
        ValidateOptionalPositive(metrics.FrameTimeP99Ms, "P99 frame time", errors);
        ValidateOptionalPositive(metrics.PeakGpuTemperatureC, "Peak GPU temperature", errors);
        ValidateOptionalPositive(metrics.PeakCpuTemperatureC, "Peak CPU temperature", errors);
        if (metrics.PeakSystemRamMb is < 0 || metrics.PeakVramMb is < 0)
            errors.Add("Peak RAM and VRAM values cannot be negative.");

        if (scenarios is null)
            return;

        if (scenarios.Any(item => item is not null &&
            item.ScenarioId == "performance-benchmark" && item.Outcome == CompatibilityTestOutcome.Passed))
        {
            if (!metrics.WarmupCompleted || metrics.BenchmarkRepeats < 3 ||
                metrics.AverageFps is null || metrics.OnePercentLowFps is null ||
                metrics.FrameTimeP95Ms is null || metrics.FrameTimeP99Ms is null ||
                metrics.PeakSystemRamMb is null or <= 0 || metrics.PeakVramMb is null or <= 0)
            {
                errors.Add("A Passed performance benchmark requires a warm-up, at least 3 repeats and FPS/frame-time/RAM/VRAM measurements.");
            }
        }

        if (scenarios.Any(item => item is not null &&
            item.ScenarioId == "sixty-minute-soak" && item.Outcome == CompatibilityTestOutcome.Passed) &&
            metrics.SoakDurationMinutes < 60)
        {
            errors.Add("A Passed 60-minute soak requires at least 60 recorded minutes.");
        }
    }

    private static void ValidateScenarios(
        IReadOnlyList<CompatibilityTestScenarioResult>? scenarios,
        ICollection<string> errors)
    {
        if (scenarios is null)
        {
            errors.Add("The required compatibility test matrix is missing.");
            return;
        }

        var expected = RequiredScenarios.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scenario in scenarios)
        {
            if (scenario is null)
            {
                errors.Add("The compatibility test matrix contains an empty scenario.");
                continue;
            }
            if (!expected.Contains(scenario.ScenarioId) || !seen.Add(scenario.ScenarioId))
                errors.Add("The compatibility test matrix contains an unsupported or duplicate scenario.");
            if (!Enum.IsDefined(scenario.Outcome))
                errors.Add("A compatibility test scenario has an unsupported outcome.");
            if ((scenario.Notes?.Length ?? 0) > 4096)
                errors.Add("A compatibility test scenario note exceeds 4,096 characters.");
        }

        if (!expected.SetEquals(seen))
            errors.Add("The compatibility test matrix must contain exactly one result for every required scenario.");
    }

    private static void ValidateOptionalPositive(double? value, string label, ICollection<string> errors)
    {
        if (value is not null && (!double.IsFinite(value.Value) || value.Value <= 0))
            errors.Add(label + " must be a finite positive value when supplied.");
    }

    private string GetRecordPath(string recordId)
    {
        if (!Guid.TryParseExact(recordId, "N", out _))
            throw new ArgumentException("Invalid compatibility evidence record ID.", nameof(recordId));
        return Path.Combine(_evidenceDirectory, recordId + ".json");
    }

    private static bool IsSha256(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length == 64 && value.All(Uri.IsHexDigit);
}

public sealed record CompatibilityTestScenarioDefinition(string Id, string Name, string Description);
