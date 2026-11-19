using System.Security.Cryptography;
using System.Text.Json;

namespace GtaTransformationHub.Services;

public sealed record EcologyInventoryComparisonReport(
    int SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    string GameBuild,
    string GameExecutableSha256,
    string ExtractorId,
    string ExtractorVersion,
    string BaselineFileName,
    string BaselineFileSha256,
    string CandidateFileName,
    string CandidateFileSha256,
    bool SemanticComparisonPassed,
    bool CompletenessVerified,
    bool PackageEligibilityUnlocked,
    string Limitation,
    int AddedSpeciesCount,
    IReadOnlyList<string> AddedSpeciesIds,
    IReadOnlyList<string> Errors);

public sealed record EcologyInventoryEvidenceResult(
    string ReportPath,
    string ReportSha256,
    string BaselineSha256,
    string CandidateSha256,
    EcologyInventoryDiff Diff,
    bool CompletenessVerified,
    bool PackageEligibilityUnlocked,
    string Limitation);

/// <summary>
/// Imports two normalized semantic ecology snapshots, checks their provenance against
/// the currently selected GTA5.exe fingerprint/build, runs the identity comparator,
/// and writes an auditable local comparison report.
///
/// Important: this service does not parse raw rage-cli JSON dumps into species identities
/// and does not certify scan completeness. A clean semantic diff for two supplied
/// snapshots is not proof that those snapshots enumerate every GTA V resource. It
/// never unlocks or approves ecology-sensitive packages.
/// </summary>
public sealed class EcologyInventoryEvidenceService
{
    public const long MaximumSnapshotBytes = 64L * 1024 * 1024;

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 64
    };

    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 64
    };

    private readonly string _evidenceRoot;

    public EcologyInventoryEvidenceService(string? evidenceRoot = null)
    {
        _evidenceRoot = Path.GetFullPath(evidenceRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "ecology-evidence"));
    }

    public string EvidenceRoot => _evidenceRoot;

    public async Task<EcologyInventoryEvidenceResult> CompareAndRecordAsync(
        string baselinePath,
        string candidatePath,
        GameInstallation installation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var baselineFullPath = ValidateSnapshotFile(baselinePath, "baseline");
        var candidateFullPath = ValidateSnapshotFile(candidatePath, "candidate");

        var currentBuild = installation.BuildVersion;
        if (!Version.TryParse(currentBuild, out _))
            throw new InvalidDataException("The selected GTA V Legacy build is not numeric.");

        var executableSha256 = await GameExecutableFingerprintService.ComputeSha256Async(
            installation.ExecutablePath, cancellationToken);

        var baselineHash = await ComputeSha256Async(baselineFullPath, cancellationToken);
        var candidateHash = await ComputeSha256Async(candidateFullPath, cancellationToken);

        var baseline = await ReadSnapshotAsync(baselineFullPath, cancellationToken);
        var candidate = await ReadSnapshotAsync(candidateFullPath, cancellationToken);

        ValidateExactInstallation(baseline, "Baseline", currentBuild, executableSha256);
        ValidateExactInstallation(candidate, "Candidate", currentBuild, executableSha256);

        var diff = EcologyInventoryDiffService.Compare(baseline, candidate);
        var limitation =
            "This report compares only the semantic records supplied in these two JSON snapshots. " +
            "The current rage-cli workflow discovers a bounded set of metadata candidates and dumps at most " +
            RageCliMetadataPayloadService.MaximumCandidatesToDump +
            " resources; it does not yet enumerate every terrestrial/aquatic species, plant, population, habitat " +
            "or water-system definition. Completeness is therefore unverified and ecology-sensitive packages remain blocked.";

        var report = new EcologyInventoryComparisonReport(
            SchemaVersion: 1,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            GameBuild: currentBuild,
            GameExecutableSha256: executableSha256,
            ExtractorId: baseline.ExtractorId,
            ExtractorVersion: baseline.ExtractorVersion,
            BaselineFileName: Path.GetFileName(baselineFullPath),
            BaselineFileSha256: baselineHash,
            CandidateFileName: Path.GetFileName(candidateFullPath),
            CandidateFileSha256: candidateHash,
            SemanticComparisonPassed: diff.VanillaPreserved,
            CompletenessVerified: false,
            PackageEligibilityUnlocked: false,
            Limitation: limitation,
            AddedSpeciesCount: diff.AddedSpecies.Count,
            AddedSpeciesIds: diff.AddedSpecies.Select(item => item.Id).ToArray(),
            Errors: diff.Errors.ToArray());

        Directory.CreateDirectory(_evidenceRoot);
        EnsureDirectoryIsNotReparsePoint(_evidenceRoot);

        var reportPath = Path.Combine(
            _evidenceRoot,
            "ecology-comparison-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") +
            "-" + Guid.NewGuid().ToString("N") + ".json");
        var temporaryPath = reportPath + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, report, ReportJsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, reportPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }

        var reportHash = await ComputeSha256Async(reportPath, cancellationToken);
        return new EcologyInventoryEvidenceResult(
            reportPath,
            reportHash,
            baselineHash,
            candidateHash,
            diff,
            CompletenessVerified: false,
            PackageEligibilityUnlocked: false,
            limitation);
    }

    private static string ValidateSnapshotFile(string? path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Select a " + label + " ecology inventory JSON file.");

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The " + label + " ecology inventory file was not found.", fullPath);
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The " + label + " ecology inventory cannot be a symbolic link or reparse point.");

        var info = new FileInfo(fullPath);
        if (info.Length <= 0 || info.Length > MaximumSnapshotBytes)
            throw new InvalidDataException("The " + label + " ecology inventory must be between 1 byte and 64 MB.");

        if (!Path.GetExtension(fullPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The " + label + " ecology inventory must be a JSON file.");

        return fullPath;
    }

    private static async Task<EcologyInventorySnapshot> ReadSnapshotAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var snapshot = await JsonSerializer.DeserializeAsync<EcologyInventorySnapshot>(
                stream, SnapshotJsonOptions, cancellationToken);

            return snapshot ?? throw new InvalidDataException(
                "The ecology inventory JSON did not contain a snapshot object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The ecology inventory JSON is malformed or does not match the supported semantic snapshot schema.",
                exception);
        }
    }

    private static void ValidateExactInstallation(
        EcologyInventorySnapshot snapshot,
        string label,
        string expectedBuild,
        string expectedExecutableSha256)
    {
        if (!Version.TryParse(snapshot.GameBuild, out _))
            throw new InvalidDataException(label + " snapshot must declare a numeric GTA V Legacy build.");
        if (!snapshot.GameBuild.Equals(expectedBuild, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                label + " snapshot targets GTA V build " + snapshot.GameBuild +
                ", but the selected installation is build " + expectedBuild + ".");
        if (!snapshot.GameExecutableSha256.Equals(expectedExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                label + " snapshot GTA5.exe SHA-256 does not match the selected game executable.");
        if (string.IsNullOrWhiteSpace(snapshot.ExtractorId) ||
            string.IsNullOrWhiteSpace(snapshot.ExtractorVersion))
            throw new InvalidDataException(label + " snapshot has no extractor identity and version.");
        if (snapshot.SourceArchiveSha256 is null || snapshot.SourceArchiveSha256.Count == 0 ||
            snapshot.SourceArchiveSha256.Any(hash =>
                string.IsNullOrWhiteSpace(hash) || hash.Length != 64 || !hash.All(Uri.IsHexDigit)))
            throw new InvalidDataException(label + " snapshot does not include valid source RPF/archive SHA-256 fingerprints.");
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void EnsureDirectoryIsNotReparsePoint(string directory)
    {
        var info = new DirectoryInfo(directory);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The ecology evidence directory cannot be a symbolic link or reparse point.");
    }
}
