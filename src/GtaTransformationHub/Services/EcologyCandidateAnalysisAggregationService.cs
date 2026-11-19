using System.Text.Json;

namespace GtaTransformationHub.Services;

public sealed record EcologyBatchCoverageResult(
    bool EligibleMetadataResourceCoverageComplete,
    int ExpectedCandidateCount,
    int UniqueCandidateIndexes,
    IReadOnlyList<int> MissingCandidateIndexes,
    IReadOnlyList<int> DuplicateCandidateIndexes,
    IReadOnlyList<string> Warnings);

public sealed record EcologyCandidateAggregationResult(
    string ReportPath,
    string ReportSha256,
    string GameBuild,
    string GameExecutableSha256,
    int InputAnalysisReportCount,
    int DumpBatchCount,
    int PayloadsVerified,
    int CandidateCount,
    IReadOnlyDictionary<string, int> CategoryCounts,
    EcologyBatchCoverageResult BatchCoverage,
    bool CompletenessVerified,
    bool PackageEligibilityUnlocked,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<EcologyMetadataCandidate> Candidates);

/// <summary>
/// Combines heuristic candidate-analysis reports produced from separate 50-resource
/// metadata batches. Before combining, it re-verifies the report, dump-index, discovery
/// report, source RPF and dumped JSON hashes against one exact GTA V Legacy executable.
/// It never turns heuristic metadata matches into verified ecology identities and
/// never unlocks ecology-sensitive package deployment.
/// </summary>
public sealed class EcologyCandidateAnalysisAggregationService
{
    public const int MaximumInputReports = 200;
    public const int MaximumAggregateCandidates = 250_000;
    public const long MaximumAggregateReportBytes = 128L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = RageCliJsonStructureAnalyzer.MaximumDepth
    };

    private readonly string _dataRoot;
    private readonly string _evidenceRoot;

    private sealed record CandidateAnalysisDocument(
        int SchemaVersion,
        string GameBuild,
        string GameExecutableSha256,
        string ToolVersion,
        string ToolSha256,
        string DumpIndexFileName,
        string DumpIndexSha256,
        string DiscoveryReportFileName,
        string DiscoveryReportSha256,
        int PayloadsReviewed,
        int CandidateCount,
        bool InputIndexWasComplete,
        bool CompletenessVerified,
        bool PackageEligibilityUnlocked,
        IReadOnlyDictionary<string, int> CategoryCounts,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<EcologyMetadataCandidate> Candidates);

    private sealed record DumpIndexDocument(
        int SchemaVersion,
        string ToolVersion,
        string ToolSha256,
        string GameBuild,
        string GameExecutableSha256,
        string DiscoveryReportPath,
        string DiscoveryReportSha256,
        int CandidateCount,
        int StartingCandidateIndex,
        int NextCandidateIndex,
        int SelectedCandidateCount,
        bool MoreBatchesAvailable,
        bool BatchComplete,
        IReadOnlyList<string> Errors,
        IReadOnlyList<RageCliMetadataPayload> Payloads);

    private sealed record DiscoveryDocument(
        int SchemaVersion,
        string ToolVersion,
        string ToolSha256,
        string GameBuild,
        string GameExecutableSha256,
        IReadOnlyList<RageCliMetadataPatternResult> PatternResults,
        IReadOnlyList<string> Errors);

    private sealed record BatchProvenance(
        string DumpIndexFileName,
        string DumpIndexSha256,
        int StartingCandidateIndex,
        int NextCandidateIndex,
        int SelectedCandidateCount,
        bool BatchComplete);

    private sealed record AggregatedCandidateReport(
        int SchemaVersion,
        DateTimeOffset CreatedAtUtc,
        string GameBuild,
        string GameExecutableSha256,
        string ToolVersion,
        string ToolSha256,
        string DiscoveryReportFileName,
        string DiscoveryReportSha256,
        IReadOnlyList<string> InputAnalysisReportFiles,
        IReadOnlyList<string> InputAnalysisReportSha256,
        IReadOnlyList<BatchProvenance> Batches,
        int ExpectedEligibleMetadataResourceCount,
        int UniqueBatchCandidateIndexes,
        int PayloadsVerified,
        int CandidateCount,
        IReadOnlyDictionary<string, int> CategoryCounts,
        bool EligibleMetadataResourceCoverageComplete,
        bool CompletenessVerified,
        bool PackageEligibilityUnlocked,
        IReadOnlyList<string> Limitations,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<EcologyMetadataCandidate> Candidates);

    public EcologyCandidateAnalysisAggregationService(
        string? dataRoot = null,
        string? evidenceRoot = null)
    {
        _dataRoot = Path.GetFullPath(dataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "rage-cli"));
        _evidenceRoot = Path.GetFullPath(evidenceRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "ecology-evidence", "candidate-analysis"));
    }

    public async Task<EcologyCandidateAggregationResult> AggregateAsync(
        GameInstallation installation,
        IEnumerable<string> analysisReportPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentNullException.ThrowIfNull(analysisReportPaths);

        var (gameExecutable, gameRoot) = ValidateInstallation(installation);
        var gameHash = await RageCliToolchainService.ComputeSha256Async(gameExecutable, cancellationToken);
        var buildDirectory = RageCliToolchainService.BuildDirectoryNameFromExecutableHash(gameHash);
        var reportRoot = Path.Combine(_evidenceRoot, "aggregated");
        var metadataRoot = Path.Combine(_dataRoot, "metadata-dumps", buildDirectory);
        var catalogueRoot = Path.Combine(_dataRoot, "catalogue", buildDirectory);

        var selectedPaths = analysisReportPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (selectedPaths.Length < 2)
            throw new InvalidDataException("Select at least two candidate-analysis reports from separate metadata batches.");
        if (selectedPaths.Length > MaximumInputReports)
            throw new InvalidDataException("The aggregation input limit is 200 candidate-analysis reports.");

        Directory.CreateDirectory(_evidenceRoot);
        EnsureDirectoryIsNotReparsePoint(_evidenceRoot);
        Directory.CreateDirectory(reportRoot);
        EnsureDirectoryIsNotReparsePoint(reportRoot);
        EnsureDirectoryIsNotReparsePoint(metadataRoot);
        EnsureDirectoryIsNotReparsePoint(catalogueRoot);

        var reports = new List<CandidateAnalysisDocument>();
        var reportNames = new List<string>();
        var reportHashes = new List<string>();
        var warnings = new List<string>();
        var batches = new List<BatchProvenance>();
        var batchIndexes = new List<int>();
        var candidatePayloadFingerprints = new Dictionary<int, string>();
        var allCandidates = new List<EcologyMetadataCandidate>();
        var verifiedPayloads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string? sharedToolVersion = null;
        string? sharedToolSha256 = null;
        string? sharedDiscoveryName = null;
        string? sharedDiscoverySha256 = null;
        int? sharedCandidateCount = null;

        foreach (var selectedPath in selectedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reportPath = ValidateOwnedFile(_evidenceRoot, selectedPath, 64L * 1024 * 1024,
                "candidate-analysis report");
            var reportHash = await RageCliToolchainService.ComputeSha256Async(reportPath, cancellationToken);
            var report = await ReadJsonAsync<CandidateAnalysisDocument>(reportPath, cancellationToken);

            if (report.SchemaVersion != 1 ||
                !EqualsIgnoreCase(report.GameBuild, installation.BuildVersion) ||
                !EqualsIgnoreCase(report.GameExecutableSha256, gameHash) ||
                !IsSha256(report.ToolSha256) ||
                report.CompletenessVerified ||
                report.PackageEligibilityUnlocked ||
                report.Candidates is null ||
                report.Candidates.Count > RageCliEcologyCandidateAnalysisService.MaximumCandidates)
            {
                throw new InvalidDataException(
                    "A candidate-analysis report is malformed, belongs to another game build, or contains an invalid approval state: " +
                    Path.GetFileName(reportPath));
            }

            if (!IsSha256(report.DumpIndexSha256) || !IsSha256(report.DiscoveryReportSha256))
                throw new InvalidDataException("A candidate-analysis report has invalid provenance hashes.");

            if (sharedToolVersion is null)
            {
                sharedToolVersion = report.ToolVersion;
                sharedToolSha256 = report.ToolSha256;
                sharedDiscoveryName = report.DiscoveryReportFileName;
                sharedDiscoverySha256 = report.DiscoveryReportSha256;
            }
            else if (!EqualsIgnoreCase(sharedToolVersion, report.ToolVersion) ||
                     !EqualsIgnoreCase(sharedToolSha256, report.ToolSha256) ||
                     !EqualsIgnoreCase(sharedDiscoveryName, report.DiscoveryReportFileName) ||
                     !EqualsIgnoreCase(sharedDiscoverySha256, report.DiscoveryReportSha256))
            {
                throw new InvalidDataException(
                    "All candidate reports must come from the same pinned rage-cli build and the same discovery report.");
            }

            var dumpIndexPath = ValidateOwnedFile(
                metadataRoot,
                ResolveLeafFileName(metadataRoot, report.DumpIndexFileName, "metadata dump index"),
                RageCliEcologyCandidateAnalysisService.MaximumIndexBytes,
                "metadata dump index");
            var dumpIndexHash = await RageCliToolchainService.ComputeSha256Async(dumpIndexPath, cancellationToken);
            if (!EqualsIgnoreCase(dumpIndexHash, report.DumpIndexSha256))
                throw new IOException("A metadata dump index changed after candidate analysis.");

            var index = await ReadJsonAsync<DumpIndexDocument>(dumpIndexPath, cancellationToken);
            if (index.SchemaVersion != 3 ||
                !EqualsIgnoreCase(index.GameBuild, installation.BuildVersion) ||
                !EqualsIgnoreCase(index.GameExecutableSha256, gameHash) ||
                !EqualsIgnoreCase(index.ToolVersion, report.ToolVersion) ||
                !EqualsIgnoreCase(index.ToolSha256, report.ToolSha256) ||
                !EqualsIgnoreCase(Path.GetFileName(index.DiscoveryReportPath), report.DiscoveryReportFileName) ||
                !EqualsIgnoreCase(index.DiscoveryReportSha256, report.DiscoveryReportSha256) ||
                index.Payloads is null ||
                index.Payloads.Count > RageCliMetadataPayloadService.MaximumCandidatesToDump)
            {
                throw new InvalidDataException("A metadata dump index does not match its candidate-analysis report provenance.");
            }

            if (sharedCandidateCount is null)
                sharedCandidateCount = index.CandidateCount;
            else if (sharedCandidateCount.Value != index.CandidateCount)
                throw new InvalidDataException("Metadata dump batches disagree on the discovery candidate count.");

            var batchValidation = RageCliEcologyCandidateAnalysisService.ValidateDumpBatchContract(
                index.SchemaVersion,
                index.CandidateCount,
                index.StartingCandidateIndex,
                index.NextCandidateIndex,
                index.SelectedCandidateCount,
                index.MoreBatchesAvailable,
                index.BatchComplete,
                index.Payloads.Select(payload => payload.CandidateIndex).ToArray(),
                index.Errors);
            if (batchValidation.Count > 0)
                throw new InvalidDataException("A metadata dump batch has an invalid cursor: " +
                                                string.Join(" ", batchValidation));

            var discoveryPath = ValidateOwnedFile(
                catalogueRoot,
                ResolveLeafFileName(catalogueRoot, Path.GetFileName(index.DiscoveryReportPath), "discovery report"),
                64L * 1024 * 1024,
                "metadata discovery report");
            var discoveryHash = await RageCliToolchainService.ComputeSha256Async(discoveryPath, cancellationToken);
            if (!EqualsIgnoreCase(discoveryHash, index.DiscoveryReportSha256))
                throw new IOException("The metadata discovery report changed after the dump index was created.");

            var discovery = await ReadJsonAsync<DiscoveryDocument>(discoveryPath, cancellationToken);
            if (discovery.SchemaVersion != 1 ||
                !EqualsIgnoreCase(discovery.GameBuild, installation.BuildVersion) ||
                !EqualsIgnoreCase(discovery.GameExecutableSha256, gameHash) ||
                !EqualsIgnoreCase(discovery.ToolVersion, index.ToolVersion) ||
                !EqualsIgnoreCase(discovery.ToolSha256, index.ToolSha256))
                throw new InvalidDataException("The discovery report provenance no longer matches this exact game/tool.");

            foreach (var payload in index.Payloads)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (payload is null || !IsSha256(payload.SourceArchiveSha256) ||
                    !IsSha256(payload.DumpJsonSha256))
                    throw new InvalidDataException("A metadata payload is missing a source or JSON digest.");

                var sourceArchive = ValidateSourceArchive(gameRoot, payload.SourceArchivePath);
                var dumpJsonPath = ValidateOwnedFile(
                    metadataRoot,
                    payload.DumpJsonPath,
                    RageCliMetadataPayloadService.MaximumDumpJsonBytes,
                    "metadata JSON payload");
                var info = new FileInfo(dumpJsonPath);
                if (info.Length != payload.DumpJsonBytes)
                    throw new IOException("A metadata JSON payload changed size after candidate analysis.");

                var sourceHash = await RageCliToolchainService.ComputeSha256Async(sourceArchive, cancellationToken);
                if (!EqualsIgnoreCase(sourceHash, payload.SourceArchiveSha256))
                    throw new IOException("A source RPF changed after candidate analysis: " + payload.InternalPath);

                var dumpHash = await RageCliToolchainService.ComputeSha256Async(dumpJsonPath, cancellationToken);
                if (!EqualsIgnoreCase(dumpHash, payload.DumpJsonSha256))
                    throw new IOException("A metadata JSON payload changed after candidate analysis: " + payload.InternalPath);

                var payloadKey = payload.CandidateIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                                 "|" + payload.DumpJsonSha256;
                var payloadFingerprint = payload.SourceArchiveSha256 + "|" +
                                         payload.DumpJsonSha256 + "|" + payload.InternalPath;
                if (candidatePayloadFingerprints.TryGetValue(payload.CandidateIndex, out var existingFingerprint) &&
                    !EqualsIgnoreCase(existingFingerprint, payloadFingerprint))
                {
                    throw new InvalidDataException(
                        "A retried candidate index resolves to different source or JSON content: " +
                        payload.CandidateIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                candidatePayloadFingerprints[payload.CandidateIndex] = payloadFingerprint;
                verifiedPayloads.Add(payloadKey);
                batchIndexes.Add(payload.CandidateIndex);
            }

            var expectedPayloadKeys = index.Payloads.Select(payload =>
                payload.CandidateIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "|" + payload.DumpJsonSha256).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in report.Candidates)
            {
                if (candidate is null ||
                    !expectedPayloadKeys.Contains(
                        candidate.JsonPath is null ? string.Empty :
                        index.Payloads.FirstOrDefault(payload =>
                            EqualsIgnoreCase(payload.DumpJsonSha256, candidate.DumpJsonSha256) &&
                            EqualsIgnoreCase(payload.SourceArchiveSha256, candidate.SourceArchiveSha256) &&
                            EqualsIgnoreCase(payload.InternalPath, candidate.InternalArchivePath)) is { } match
                                ? match.CandidateIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                                  "|" + match.DumpJsonSha256
                                : string.Empty))
                {
                    throw new InvalidDataException(
                        "A candidate row cannot be matched to an exact payload hash in its metadata dump index.");
                }

                if (string.IsNullOrWhiteSpace(candidate.Category) ||
                    string.IsNullOrWhiteSpace(candidate.ResourceName) ||
                    string.IsNullOrWhiteSpace(candidate.FieldName) ||
                    string.IsNullOrWhiteSpace(candidate.CandidateValue) ||
                    !IsSha256(candidate.SourceArchiveSha256) ||
                    !IsSha256(candidate.DumpJsonSha256))
                    throw new InvalidDataException("A candidate row is missing required identity or provenance fields.");
            }

            if (allCandidates.Count + report.Candidates.Count > MaximumAggregateCandidates)
                throw new InvalidDataException("The combined ecology candidate report would exceed 250,000 heuristic candidate rows.");

            allCandidates.AddRange(report.Candidates);
            reports.Add(report);
            reportNames.Add(Path.GetFileName(reportPath));
            reportHashes.Add(reportHash);
            batches.Add(new BatchProvenance(
                Path.GetFileName(dumpIndexPath),
                dumpIndexHash,
                index.StartingCandidateIndex,
                index.NextCandidateIndex,
                index.SelectedCandidateCount,
                index.BatchComplete));
            warnings.AddRange(report.Warnings?.Take(100) ?? Array.Empty<string>());
            warnings.AddRange(index.Errors?.Take(100) ?? Array.Empty<string>());

            var discoveryComplete = discovery.Errors is { Count: 0 } &&
                                    discovery.PatternResults is { Count: > 0 } &&
                                    discovery.PatternResults.All(item =>
                                        !item.HitLimit && !item.OutputTruncated && !item.HasArchiveErrors);
            if (!discoveryComplete)
                warnings.Add("The original metadata discovery contains errors, hit caps, truncated output or archive failures.");
            if (!index.BatchComplete)
                warnings.Add("At least one metadata dump batch is incomplete.");
        }

        if (reports.Count != selectedPaths.Length || sharedCandidateCount is null ||
            sharedToolVersion is null || sharedToolSha256 is null ||
            sharedDiscoveryName is null || sharedDiscoverySha256 is null)
            throw new InvalidDataException("The selected candidate-analysis reports could not be reconciled.");

        if (allCandidates.Count > MaximumAggregateCandidates)
            throw new InvalidDataException("The combined candidate report would exceed 250,000 heuristic candidate rows.");

        var uniqueCandidates = allCandidates
            .DistinctBy(candidate => string.Join("|",
                candidate.Category,
                candidate.InternalArchivePath,
                candidate.JsonPath,
                candidate.FieldName,
                candidate.CandidateValue,
                candidate.SourceArchiveSha256,
                candidate.DumpJsonSha256), StringComparer.OrdinalIgnoreCase)
            .OrderBy(candidate => candidate.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.ResourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.InternalArchivePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.JsonPath, StringComparer.Ordinal)
            .ToArray();

        var categoryCounts = uniqueCandidates
            .GroupBy(candidate => candidate.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var discoveryFile = Path.Combine(catalogueRoot, sharedDiscoveryName);
        var discoveryDocument = await ReadJsonAsync<DiscoveryDocument>(discoveryFile, cancellationToken);
        var isDiscoveryComplete = discoveryDocument.Errors is { Count: 0 } &&
                                  discoveryDocument.PatternResults is { Count: > 0 } &&
                                  discoveryDocument.PatternResults.All(item =>
                                      !item.HitLimit && !item.OutputTruncated && !item.HasArchiveErrors);

        var coverage = EvaluateBatchCoverage(
            sharedCandidateCount.Value,
            batchIndexes.Distinct(),
            isDiscoveryComplete,
            batches.All(batch => batch.BatchComplete));

        var distinctIndexes = batchIndexes.Distinct().Count();
        if (coverage.Warnings.Count > 0)
            warnings.AddRange(coverage.Warnings);
        if (distinctIndexes != batchIndexes.Count)
            warnings.Add("Some candidate indexes occur in more than one selected batch. Duplicate indexes were not counted twice.");
        if (!coverage.EligibleMetadataResourceCoverageComplete)
            warnings.Add("The combined reports do not yet cover every eligible metadata candidate from the discovery report.");

        var limitation = new[]
        {
            "All candidate values remain heuristic field/path matches; they are not independently verified species, model, habitat, population or water identities.",
            "A complete set of discovered metadata resource dumps does not prove the discovery patterns cover every ecological definition in GTA V.",
            "CompletenessVerified is always false and PackageEligibilityUnlocked is always false. Ecology-sensitive packages remain blocked."
        };

        var output = new AggregatedCandidateReport(
            SchemaVersion: 1,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            GameBuild: installation.BuildVersion,
            GameExecutableSha256: gameHash,
            ToolVersion: sharedToolVersion,
            ToolSha256: sharedToolSha256,
            DiscoveryReportFileName: sharedDiscoveryName,
            DiscoveryReportSha256: sharedDiscoverySha256,
            InputAnalysisReportFiles: reportNames,
            InputAnalysisReportSha256: reportHashes,
            Batches: batches,
            ExpectedEligibleMetadataResourceCount: sharedCandidateCount.Value,
            UniqueBatchCandidateIndexes: distinctIndexes,
            PayloadsVerified: verifiedPayloads.Count,
            CandidateCount: uniqueCandidates.Length,
            CategoryCounts: categoryCounts,
            EligibleMetadataResourceCoverageComplete: coverage.EligibleMetadataResourceCoverageComplete,
            CompletenessVerified: false,
            PackageEligibilityUnlocked: false,
            Limitations: limitation,
            Warnings: warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Candidates: uniqueCandidates);

        var outputPath = Path.Combine(
            reportRoot,
            "ecology-candidate-aggregate-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") +
            "-" + Guid.NewGuid().ToString("N") + ".json");
        var temporaryPath = outputPath + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    output,
                    new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase },
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            var temporaryInfo = new FileInfo(temporaryPath);
            if (temporaryInfo.Length <= 0 || temporaryInfo.Length > MaximumAggregateReportBytes)
                throw new InvalidDataException("The combined ecology candidate report is empty or exceeds 128 MB.");
            File.Move(temporaryPath, outputPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }

        var outputHash = await RageCliToolchainService.ComputeSha256Async(outputPath, cancellationToken);
        return new EcologyCandidateAggregationResult(
            outputPath,
            outputHash,
            installation.BuildVersion,
            gameHash,
            reports.Count,
            batches.Count,
            verifiedPayloads.Count,
            uniqueCandidates.Length,
            categoryCounts,
            coverage,
            CompletenessVerified: false,
            PackageEligibilityUnlocked: false,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            uniqueCandidates);
    }

    public static EcologyBatchCoverageResult EvaluateBatchCoverage(
        int expectedCandidateCount,
        IEnumerable<int> candidateIndexes,
        bool discoveryComplete,
        bool allBatchesComplete)
    {
        ArgumentNullException.ThrowIfNull(candidateIndexes);
        if (expectedCandidateCount is < 1 or > RageCliEcologyCandidateAnalysisService.MaximumDiscoveredCandidates)
            throw new ArgumentOutOfRangeException(nameof(expectedCandidateCount));

        var indexes = candidateIndexes.ToArray();
        var duplicates = indexes
            .GroupBy(value => value)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(value => value)
            .ToArray();
        var unique = indexes.Distinct().ToHashSet();
        var invalid = indexes.Where(value => value < 0 || value >= expectedCandidateCount).Distinct().ToArray();
        var missing = Enumerable.Range(0, expectedCandidateCount)
            .Where(value => !unique.Contains(value))
            .ToArray();
        var warnings = new List<string>();

        if (invalid.Length > 0)
            warnings.Add("One or more batch candidate indexes fall outside the discovery report's eligible candidate range.");
        if (duplicates.Length > 0)
            warnings.Add("One or more candidate indexes were supplied more than once.");
        if (missing.Length > 0)
            warnings.Add(missing.Length + " eligible metadata candidates do not yet have a verified JSON dump.");
        if (!discoveryComplete)
            warnings.Add("The metadata discovery itself is incomplete, so full ecological metadata coverage cannot be asserted.");
        if (!allBatchesComplete)
            warnings.Add("One or more dump batches contain extraction errors.");

        var complete = discoveryComplete && allBatchesComplete &&
                       invalid.Length == 0 && duplicates.Length == 0 && missing.Length == 0;
        return new EcologyBatchCoverageResult(
            complete,
            expectedCandidateCount,
            unique.Count,
            missing,
            duplicates,
            warnings);
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
               ?? throw new InvalidDataException("A required ecology candidate provenance JSON file is empty.");
    }

    private static (string Executable, string Root) ValidateInstallation(GameInstallation installation)
    {
        var executable = Path.GetFullPath(installation.ExecutablePath);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation.RootPath));
        if (!File.Exists(executable) ||
            !Path.GetFileName(executable).Equals("GTA5.exe", StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(root) ||
            !Path.GetDirectoryName(executable)!.Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected GTA5.exe and installation root do not match.");
        return (executable, root);
    }

    private static string ValidateSourceArchive(string gameRoot, string archivePath)
    {
        var fullPath = Path.GetFullPath(archivePath);
        EnsureContained(gameRoot, fullPath);
        if (!fullPath.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            throw new InvalidDataException("A source archive is missing or outside the selected game folder.");
        EnsureNoReparsePoints(gameRoot, fullPath);
        return fullPath;
    }

    private static string ResolveLeafFileName(string root, string fileName, string description)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            !Path.GetFileName(fileName).Equals(fileName, StringComparison.Ordinal) ||
            fileName is "." or "..")
            throw new InvalidDataException("The " + description + " name is not a safe leaf filename.");
        return Path.Combine(root, fileName);
    }

    private static string ValidateOwnedFile(string root, string path, long maximumBytes, string description)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        EnsureContained(fullRoot, fullPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The " + description + " was not found.", fullPath);
        EnsureNoReparsePoints(fullRoot, fullPath);
        var info = new FileInfo(fullPath);
        if (info.Length <= 0 || info.Length > maximumBytes)
            throw new InvalidDataException("The " + description + " is empty or exceeds its size limit.");
        return fullPath;
    }

    private static void EnsureContained(string root, string path)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A provenance path escapes its expected directory.");
    }

    private static void EnsureNoReparsePoints(string root, string path)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if ((File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A provenance root directory is a reparse point.");

        var fullPath = Path.GetFullPath(path);
        EnsureContained(fullRoot, fullPath);
        var current = fullRoot;
        var relative = Path.GetRelativePath(fullRoot, fullPath);
        var segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            current = Path.Combine(current, segments[index]);
            if (!File.Exists(current) && !Directory.Exists(current))
                throw new FileNotFoundException("A provenance path component is missing.", current);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A provenance path traverses a reparse point.");
        }
    }

    private static void EnsureDirectoryIsNotReparsePoint(string path)
    {
        if (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("An ecology evidence directory is missing or redirected.");
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool EqualsIgnoreCase(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
