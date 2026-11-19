using System.Text.Json;
using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

public enum StoryTestOutcome
{
    NotRun,
    Passed,
    Failed,
    Blocked
}

public sealed class StoryTestScenarioResult
{
    public string ScenarioId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public StoryTestOutcome Outcome { get; set; } = StoryTestOutcome.NotRun;
    public string Notes { get; set; } = string.Empty;
}

public sealed record StoryTestEvidenceRecord(
    string RecordId,
    string PackageId,
    string PackageVersion,
    string PackageArchiveSha256,
    string GameBuild,
    string GameExecutableSha256,
    DateTimeOffset RecordedAtUtc,
    string TestEnvironment,
    string Summary,
    IReadOnlyList<StoryTestScenarioResult> Scenarios);

public sealed record StoryTestEvidenceSummary(
    int Passed,
    int Failed,
    int Blocked,
    int NotRun,
    int Total,
    bool IsCompletePass);

/// <summary>
/// Stores tester-authored Story Mode test evidence, pinned to the exact package archive,
/// GTA5.exe SHA-256 and game build. Saving evidence never promotes a package automatically.
/// </summary>
public sealed class StoryTestEvidenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly StoryTestScenarioDefinition[] RequiredScenarios =
    [
        new("mission-discovery", "Mission discovery", "The mission appears at its documented start location and can be discovered."),
        new("mission-start", "Mission start", "The mission starts and enters its expected first state."),
        new("checkpoint", "Checkpoint", "Checkpoints can be reached and resumed correctly."),
        new("failure-retry", "Failure and retry", "Failure, retry and mission reset behave correctly."),
        new("completion", "Mission completion", "Completion triggers the expected outcome without soft-locking the campaign."),
        new("save-reload", "Save and reload", "Save, exit, reload and progression persistence behave as declared."),
        new("protagonist-switching", "Protagonist switching", "Character switching and protagonist-specific requirements remain valid."),
        new("cutscene-transitions", "Cutscene transitions", "Cutscenes, control hand-off and mission transitions remain functional."),
        new("map-markers", "Map markers", "Start markers, route markers and mission blips behave correctly."),
        new("keyboard-mouse", "Keyboard and mouse", "All declared keyboard/mouse interactions work."),
        new("controller", "Controller", "All declared controller interactions work."),
        new("conflict-combinations", "Conflict combinations", "The package works with its declared compatible package combinations."),
        new("crash-recovery", "Crash recovery", "A forced interruption does not corrupt game files, saves or Hub transactions."),
        new("uninstall-rollback", "Uninstall and rollback", "Removal and rollback restore the pre-test game state without deleting user-modified files.")
    ];

    private readonly string _evidenceDirectory;

    public StoryTestEvidenceService(string? evidenceDirectory = null)
    {
        _evidenceDirectory = Path.GetFullPath(evidenceDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "story-test-evidence"));
    }

    public string EvidenceDirectory => _evidenceDirectory;

    public static IReadOnlyList<StoryTestScenarioDefinition> GetRequiredScenarios() =>
        RequiredScenarios.ToArray();

    public static IReadOnlyList<StoryTestScenarioResult> CreateScenarioResults() =>
        RequiredScenarios.Select(scenario => new StoryTestScenarioResult
        {
            ScenarioId = scenario.Id,
            Name = scenario.Name,
            Outcome = StoryTestOutcome.NotRun,
            Notes = string.Empty
        }).ToArray();

    public static IReadOnlyList<string> Validate(StoryTestEvidenceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var errors = new List<string>();

        if (!Guid.TryParseExact(record.RecordId, "N", out _))
            errors.Add("The story test evidence record ID is invalid.");
        if (!PackageIdValidator.IsValid(record.PackageId))
            errors.Add("The story test evidence package ID is invalid.");
        if (!Version.TryParse(record.PackageVersion, out _))
            errors.Add("The story test evidence package version is invalid.");
        if (!IsSha256(record.PackageArchiveSha256))
            errors.Add("The evidence must include the imported package archive SHA-256.");
        if (!Version.TryParse(record.GameBuild, out _))
            errors.Add("The evidence must include a numeric GTA V Legacy game build.");
        if (!IsSha256(record.GameExecutableSha256))
            errors.Add("The evidence must include the GTA5.exe SHA-256 fingerprint.");
        if (record.RecordedAtUtc == default)
            errors.Add("The evidence record has no timestamp.");
        if (string.IsNullOrWhiteSpace(record.TestEnvironment) || record.TestEnvironment.Length > 4000)
            errors.Add("The test environment is required and must be 4,000 characters or fewer.");
        if ((record.Summary?.Length ?? 0) > 8000)
            errors.Add("The test summary must be 8,000 characters or fewer.");
        if (record.Scenarios is null)
        {
            errors.Add("The evidence must include every required Story Mode test scenario.");
            return errors;
        }

        var expected = RequiredScenarios.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scenario in record.Scenarios)
        {
            if (scenario is null)
            {
                errors.Add("The evidence contains an empty test scenario.");
                continue;
            }

            if (!expected.Contains(scenario.ScenarioId) || !seen.Add(scenario.ScenarioId))
                errors.Add("The evidence contains an unsupported or duplicate test scenario.");
            if (!Enum.IsDefined(scenario.Outcome))
                errors.Add("A test scenario has an unsupported outcome.");
            if ((scenario.Notes?.Length ?? 0) > 4096)
                errors.Add("A test scenario note exceeds 4,096 characters.");
        }

        if (!expected.SetEquals(seen))
            errors.Add("The evidence must contain exactly one result for each required Story Mode test scenario.");

        return errors;
    }

    public static StoryTestEvidenceSummary Summarize(StoryTestEvidenceRecord record)
    {
        var scenarios = record.Scenarios ?? Array.Empty<StoryTestScenarioResult>();
        var passed = scenarios.Count(item => item.Outcome == StoryTestOutcome.Passed);
        var failed = scenarios.Count(item => item.Outcome == StoryTestOutcome.Failed);
        var blocked = scenarios.Count(item => item.Outcome == StoryTestOutcome.Blocked);
        var notRun = scenarios.Count(item => item.Outcome == StoryTestOutcome.NotRun);
        var allRequiredPresent = RequiredScenarios.All(required =>
            scenarios.Count(item => string.Equals(item.ScenarioId, required.Id, StringComparison.Ordinal)) == 1);
        var complete = errorsFree(record) && allRequiredPresent &&
                       passed == RequiredScenarios.Length && failed == 0 && blocked == 0 && notRun == 0;
        return new StoryTestEvidenceSummary(passed, failed, blocked, notRun, RequiredScenarios.Length, complete);

        static bool errorsFree(StoryTestEvidenceRecord value) => Validate(value).Count == 0;
    }

    public async Task SaveAsync(StoryTestEvidenceRecord record, CancellationToken cancellationToken = default)
    {
        var errors = Validate(record);
        if (errors.Count > 0)
            throw new InvalidDataException("Story test evidence is invalid: " + string.Join(" ", errors));

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

    public async Task<IReadOnlyList<StoryTestEvidenceRecord>> ListAsync(
        string packageId,
        string packageVersion,
        string? gameExecutableSha256 = null,
        CancellationToken cancellationToken = default)
    {
        if (!PackageIdValidator.IsValid(packageId))
            throw new ArgumentException("A valid package ID is required.", nameof(packageId));
        if (!Version.TryParse(packageVersion, out _))
            throw new ArgumentException("A numeric package version is required.", nameof(packageVersion));
        if (!Directory.Exists(_evidenceDirectory))
            return Array.Empty<StoryTestEvidenceRecord>();

        var records = new List<StoryTestEvidenceRecord>();
        foreach (var path in Directory.EnumerateFiles(_evidenceDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var record = await JsonSerializer.DeserializeAsync<StoryTestEvidenceRecord>(stream, JsonOptions, cancellationToken);
            if (record is null || Validate(record).Count > 0)
                continue;
            if (!record.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase) ||
                !record.PackageVersion.Equals(packageVersion, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrWhiteSpace(gameExecutableSha256) &&
                !record.GameExecutableSha256.Equals(gameExecutableSha256, StringComparison.OrdinalIgnoreCase))
                continue;
            records.Add(record);
        }

        return records.OrderByDescending(record => record.RecordedAtUtc).ToArray();
    }

    private string GetRecordPath(string recordId)
    {
        if (!Guid.TryParseExact(recordId, "N", out _))
            throw new ArgumentException("Invalid evidence record ID.", nameof(recordId));
        return Path.Combine(_evidenceDirectory, recordId + ".json");
    }

    private static bool IsSha256(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length == 64 && value.All(Uri.IsHexDigit);
}

public sealed record StoryTestScenarioDefinition(string Id, string Name, string Description);
