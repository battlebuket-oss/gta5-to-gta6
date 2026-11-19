using System.Security.Cryptography;
using System.Text.Json;
using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

public sealed class EcologyCurationCandidateRow
{
    public int CandidateIndex { get; set; }
    public string Category { get; set; } = string.Empty;
    public string CandidateValue { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string InternalArchivePath { get; set; } = string.Empty;
    public string JsonPointer { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string SourceArchivePath { get; set; } = string.Empty;
    public string SourceArchiveSha256 { get; set; } = string.Empty;
    public string DumpJsonSha256 { get; set; } = string.Empty;
    public string DumpJsonFilePath { get; set; } = string.Empty;
    public string EvidenceNote { get; set; } = string.Empty;
    public string ReviewDecision { get; set; } = "Unreviewed";
    public string CanonicalId { get; set; } = string.Empty;
    public string ReviewerNotes { get; set; } = string.Empty;
}

public sealed class EcologySpeciesCurationRow
{
    public string Id { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string ModelIdentifiers { get; set; } = string.Empty;
    public string HabitatIds { get; set; } = string.Empty;
    public string BehaviourProfileId { get; set; } = string.Empty;
    public string BehaviourDefinitionJson { get; set; } = string.Empty;
    public string EvidenceCandidateIndexes { get; set; } = string.Empty;
}

public sealed class EcologyHabitatCurationRow
{
    public string Id { get; set; } = string.Empty;
    public string HabitatType { get; set; } = string.Empty;
    public string WaterBodyId { get; set; } = string.Empty;
    public string DefinitionJson { get; set; } = string.Empty;
    public string EvidenceCandidateIndexes { get; set; } = string.Empty;
}

public sealed class EcologyPopulationCurationRow
{
    public string Id { get; set; } = string.Empty;
    public string SpeciesId { get; set; } = string.Empty;
    public string HabitatId { get; set; } = string.Empty;
    public string DefinitionJson { get; set; } = string.Empty;
    public string EvidenceCandidateIndexes { get; set; } = string.Empty;
}

public sealed class EcologyWaterBodyCurationRow
{
    public string Id { get; set; } = string.Empty;
    public string WaterType { get; set; } = string.Empty;
    public string SimulationProfileId { get; set; } = string.Empty;
    public string InteractionProfileId { get; set; } = string.Empty;
    public string RenderingProfileId { get; set; } = string.Empty;
    public string SimulationDefinitionJson { get; set; } = string.Empty;
    public string InteractionDefinitionJson { get; set; } = string.Empty;
    public string RenderingDefinitionJson { get; set; } = string.Empty;
    public string EvidenceCandidateIndexes { get; set; } = string.Empty;
}

/// <summary>
/// Editable user curation workspace. Candidate fields are review leads only; the
/// normalized records below are explicit human-authored identity definitions.
/// </summary>
public sealed class EcologyInventoryCurationWorkspace
{
    public int SchemaVersion { get; set; } = 1;
    public string GameBuild { get; set; } = string.Empty;
    public string GameExecutableSha256 { get; set; } = string.Empty;
    public string ExtractorId { get; set; } = "gta-v-legacy-ecology-human-curation";
    public string ExtractorVersion { get; set; } = "1.0.0";
    public string CandidateAnalysisReportFileName { get; set; } = string.Empty;
    public string CandidateAnalysisReportSha256 { get; set; } = string.Empty;
    public bool InputCandidateSearchWasComplete { get; set; }
    public List<string> SourceArchiveSha256 { get; set; } = new();
    public bool HumanReviewed { get; set; }
    public string ReviewNotes { get; set; } = string.Empty;
    public List<EcologyCurationCandidateRow> Candidates { get; set; } = new();
    public List<EcologySpeciesCurationRow> Species { get; set; } = new();
    public List<EcologyHabitatCurationRow> Habitats { get; set; } = new();
    public List<EcologyPopulationCurationRow> PopulationEntries { get; set; } = new();
    public List<EcologyWaterBodyCurationRow> WaterBodies { get; set; } = new();
}

public sealed record EcologyCurationValidationResult(
    EcologyInventorySnapshot? Snapshot,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record EcologyCurationExportEvidence(
    int SchemaVersion,
    DateTimeOffset ExportedAtUtc,
    string GameBuild,
    string GameExecutableSha256,
    string CandidateAnalysisReportFileName,
    string CandidateAnalysisReportSha256,
    string SnapshotFileName,
    string SnapshotSha256,
    int CandidateRows,
    int ConfirmedCandidateRows,
    int RejectedCandidateRows,
    int UnreviewedCandidateRows,
    int SpeciesCount,
    int HabitatCount,
    int PopulationEntryCount,
    int WaterBodyCount,
    bool HumanReviewed,
    bool CompletenessVerified,
    bool PackageEligibilityUnlocked,
    string Limitation,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Creates an exact-build curation workspace from the Hub's heuristic analysis
/// report, rechecking its discovery/index files, GTA5.exe, original source RPFs and
/// metadata JSON payloads. It then exports a normalized semantic snapshot from
/// explicit human-authored rows. A curation export is never a completeness proof
/// and can never unlock ecology-sensitive deployment.
/// </summary>
public sealed class EcologyInventoryCurationService
{
    private const long MaximumAnalysisReportBytes = 64L * 1024 * 1024;
    private const long MaximumWorkspaceBytes = 256L * 1024 * 1024;
    private static readonly string[] AllowedDomains =
    [
        "TerrestrialAnimal", "AquaticAnimal", "TerrestrialPlant", "AquaticPlant"
    ];
    private static readonly HashSet<string> ReviewDecisions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unreviewed", "Confirmed", "Rejected"
    };
    private static readonly JsonSerializerOptions ReadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 64
    };
    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 64
    };

    private readonly string _dataRoot;
    private readonly string _evidenceRoot;
    private readonly string _draftRoot;

    private sealed record CandidateAnalysisReport(
        int SchemaVersion,
        DateTimeOffset CreatedAtUtc,
        string GameBuild,
        string GameExecutableSha256,
        string ToolVersion,
        string ToolSha256,
        IReadOnlyList<string> DumpIndexFileNames,
        IReadOnlyList<string> DumpIndexSha256,
        string DiscoveryReportFileName,
        string DiscoveryReportSha256,
        int PayloadsReviewed,
        int CandidateCount,
        bool InputIndexWasComplete,
        bool CompletenessVerified,
        bool PackageEligibilityUnlocked,
        string Limitation,
        IReadOnlyDictionary<string, int> CategoryCounts,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<EcologyMetadataCandidate> Candidates);

    private sealed record MetadataDumpIndexReport(
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

    public EcologyInventoryCurationService(
        string? dataRoot = null,
        string? evidenceRoot = null,
        string? draftRoot = null)
    {
        _dataRoot = Path.GetFullPath(dataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "rage-cli"));
        _evidenceRoot = Path.GetFullPath(evidenceRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "ecology-evidence", "candidate-analysis"));
        _draftRoot = Path.GetFullPath(draftRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "ecology-evidence", "curation-drafts"));
    }

    public async Task<EcologyInventoryCurationWorkspace> CreateWorkspaceFromAnalysisReportAsync(
        GameInstallation installation,
        string candidateAnalysisReportPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var (gameExecutable, gameRoot) = ValidateInstallation(installation);
        var gameHash = await GameExecutableFingerprintService.ComputeSha256Async(
            gameExecutable, cancellationToken);
        var buildDirectory = RageCliToolchainService.BuildDirectoryNameFromExecutableHash(gameHash);

        var reportPath = ValidateOwnedFile(
            _evidenceRoot, candidateAnalysisReportPath, MaximumAnalysisReportBytes,
            "ecology candidate analysis report");
        var reportHash = await ComputeSha256Async(reportPath, cancellationToken);
        var report = await ReadJsonAsync<CandidateAnalysisReport>(reportPath, cancellationToken);

        if (report.SchemaVersion != 1 ||
            !string.Equals(report.GameBuild, installation.BuildVersion, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(report.GameExecutableSha256, gameHash, StringComparison.OrdinalIgnoreCase) ||
            report.CompletenessVerified ||
            report.PackageEligibilityUnlocked ||
            report.Candidates is null ||
            report.Candidates.Count == 0 ||
            report.Candidates.Count > RageCliEcologyCandidateAnalysisService.MaximumCandidates)
        {
            throw new InvalidDataException(
                "The candidate report is not valid for this exact GTA5.exe build, or it makes unsupported completeness/approval claims.");
        }

        if (report.DumpIndexFileNames is null || report.DumpIndexSha256 is null ||
            report.DumpIndexFileNames.Count == 0 ||
            report.DumpIndexFileNames.Count != report.DumpIndexSha256.Count ||
            report.DumpIndexFileNames.Count > 3000)
            throw new InvalidDataException("The candidate report has invalid metadata dump-index provenance.");

        var metadataDirectory = Path.Combine(_dataRoot, "metadata-dumps", buildDirectory);
        var payloadsByIndex = new Dictionary<int, RageCliMetadataPayload>();

        for (var index = 0; index < report.DumpIndexFileNames.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var indexName = report.DumpIndexFileNames[index];
            if (string.IsNullOrWhiteSpace(indexName) ||
                !Path.GetFileName(indexName).Equals(indexName, StringComparison.Ordinal))
                throw new InvalidDataException("A metadata dump index filename is invalid.");

            var indexPath = ValidateOwnedFile(
                metadataDirectory, Path.Combine(metadataDirectory, indexName), MaximumWorkspaceBytes,
                "metadata dump index");
            var indexHash = await ComputeSha256Async(indexPath, cancellationToken);
            if (!indexHash.Equals(report.DumpIndexSha256[index], StringComparison.OrdinalIgnoreCase))
                throw new IOException("A metadata dump index changed after candidate analysis.");

            var dumpIndex = await ReadJsonAsync<MetadataDumpIndexReport>(indexPath, cancellationToken);
            if (dumpIndex.SchemaVersion != 3 ||
                dumpIndex.Payloads is null ||
                !string.Equals(dumpIndex.GameBuild, installation.BuildVersion, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(dumpIndex.GameExecutableSha256, gameHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A metadata dump index does not match the selected GTA5.exe build and hash.");

            foreach (var payload in dumpIndex.Payloads)
            {
                if (payload is null)
                    throw new InvalidDataException("A metadata dump index contains an empty payload.");
                if (payloadsByIndex.TryGetValue(payload.CandidateIndex, out var existing))
                {
                    if (!existing.DumpJsonSha256.Equals(payload.DumpJsonSha256, StringComparison.OrdinalIgnoreCase) ||
                        !existing.SourceArchiveSha256.Equals(payload.SourceArchiveSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Overlapping metadata batches contain conflicting source or dump hashes.");
                    continue;
                }
                payloadsByIndex.Add(payload.CandidateIndex, payload);
            }
        }

        var discoveryDirectory = Path.Combine(_dataRoot, "catalogue", buildDirectory);
        var discoveryName = report.DiscoveryReportFileName;
        if (string.IsNullOrWhiteSpace(discoveryName) ||
            !Path.GetFileName(discoveryName).Equals(discoveryName, StringComparison.Ordinal))
            throw new InvalidDataException("The metadata discovery filename is invalid.");
        var discoveryPath = ValidateOwnedFile(
            discoveryDirectory, Path.Combine(discoveryDirectory, discoveryName),
            MaximumAnalysisReportBytes, "metadata discovery report");
        var discoveryHash = await ComputeSha256Async(discoveryPath, cancellationToken);
        if (!discoveryHash.Equals(report.DiscoveryReportSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The metadata discovery report changed after candidate analysis.");

        var verifiedArchiveHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var candidateRows = new List<EcologyCurationCandidateRow>(report.Candidates.Count);
        foreach (var candidate in report.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate is null ||
                !payloadsByIndex.TryGetValue(candidate.CandidateIndex, out var payload) ||
                !IsSha256(candidate.SourceArchiveSha256) ||
                !IsSha256(candidate.DumpJsonSha256) ||
                !candidate.DumpJsonSha256.Equals(payload.DumpJsonSha256, StringComparison.OrdinalIgnoreCase) ||
                !candidate.SourceArchiveSha256.Equals(payload.SourceArchiveSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A heuristic candidate does not match its indexed payload evidence.");

            var archivePath = ValidateSourceArchive(gameRoot, candidate.SourceArchivePath);
            if (!verifiedArchiveHashes.TryGetValue(archivePath, out var archiveHash))
            {
                archiveHash = await ComputeSha256Async(archivePath, cancellationToken);
                verifiedArchiveHashes.Add(archivePath, archiveHash);
            }
            if (!archiveHash.Equals(candidate.SourceArchiveSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("A source RPF changed after candidate analysis: " + candidate.InternalArchivePath);

            var dumpPath = ValidateOwnedFile(
                metadataDirectory, payload.DumpJsonPath,
                RageCliMetadataPayloadService.MaximumDumpJsonBytes, "metadata JSON payload");
            var dumpInfo = new FileInfo(dumpPath);
            if (dumpInfo.Length != payload.DumpJsonBytes)
                throw new InvalidDataException("A metadata JSON payload size changed after candidate analysis.");
            var actualDumpHash = await ComputeSha256Async(dumpPath, cancellationToken);
            if (!actualDumpHash.Equals(candidate.DumpJsonSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("A metadata JSON payload changed after candidate analysis: " + candidate.InternalArchivePath);

            candidateRows.Add(new EcologyCurationCandidateRow
            {
                CandidateIndex = candidate.CandidateIndex,
                Category = candidate.Category,
                CandidateValue = candidate.CandidateValue,
                ResourceName = candidate.ResourceName,
                InternalArchivePath = candidate.InternalArchivePath,
                JsonPointer = candidate.JsonPath,
                FieldName = candidate.FieldName,
                SourceArchivePath = archivePath,
                SourceArchiveSha256 = candidate.SourceArchiveSha256,
                DumpJsonSha256 = candidate.DumpJsonSha256,
                DumpJsonFilePath = dumpPath,
                EvidenceNote = candidate.EvidenceNote
            });
        }

        var sourceArchiveHashes = verifiedArchiveHashes.Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new EcologyInventoryCurationWorkspace
        {
            GameBuild = installation.BuildVersion,
            GameExecutableSha256 = gameHash,
            CandidateAnalysisReportFileName = Path.GetFileName(reportPath),
            CandidateAnalysisReportSha256 = reportHash,
            InputCandidateSearchWasComplete = report.InputIndexWasComplete,
            SourceArchiveSha256 = sourceArchiveHashes,
            Candidates = candidateRows
        };
    }

    public async Task<string> SaveDraftAsync(
        EcologyInventoryCurationWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ValidateWorkspaceIdentity(workspace);
        EnsureDirectoryTreeNoReparse(_draftRoot);
        var path = Path.Combine(
            _draftRoot,
            "ecology-curation-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") +
            "-" + Guid.NewGuid().ToString("N") + ".json");
        await WriteJsonAtomicAsync(path, workspace, MaximumWorkspaceBytes, cancellationToken);
        return path;
    }

    public EcologyCurationValidationResult BuildNormalizedSnapshot(
        EcologyInventoryCurationWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var errors = new List<string>();
        var warnings = new List<string>();

        try
        {
            ValidateWorkspaceIdentity(workspace);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            errors.Add(exception.Message);
        }

        if (!workspace.HumanReviewed)
            errors.Add("Confirm that you reviewed the candidate evidence before exporting a normalized inventory snapshot.");
        if (string.IsNullOrWhiteSpace(workspace.ReviewNotes) || workspace.ReviewNotes.Trim().Length < 20)
            errors.Add("Record at least 20 characters describing the curation review and source assumptions.");

        var candidateMap = new Dictionary<int, List<EcologyCurationCandidateRow>>();
        foreach (var candidate in workspace.Candidates ?? new List<EcologyCurationCandidateRow>())
        {
            if (candidate is null)
            {
                errors.Add("The candidate review table contains an empty row.");
                continue;
            }
            if (!ReviewDecisions.Contains(candidate.ReviewDecision ?? string.Empty))
            {
                errors.Add("Candidate " + candidate.CandidateIndex + " has an unsupported review decision.");
                continue;
            }
            if (candidate.ReviewDecision.Equals("Confirmed", StringComparison.OrdinalIgnoreCase) &&
                !IsValidId(candidate.CanonicalId))
                errors.Add("Confirmed candidate " + candidate.CandidateIndex + " must map to a stable canonical ID.");
            if (!candidateMap.TryGetValue(candidate.CandidateIndex, out var list))
            {
                list = new List<EcologyCurationCandidateRow>();
                candidateMap.Add(candidate.CandidateIndex, list);
            }
            list.Add(candidate);
        }

        var unreviewedCount = workspace.Candidates.Count(candidate =>
            candidate is not null &&
            candidate.ReviewDecision.Equals("Unreviewed", StringComparison.OrdinalIgnoreCase));
        if (unreviewedCount > 0)
            warnings.Add(unreviewedCount + " heuristic candidate row(s) remain unreviewed. This snapshot is only a partial semantic curation.");

        var sourceHashes = (workspace.SourceArchiveSha256 ?? new List<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourceHashes.Length == 0 || sourceHashes.Any(hash => !IsSha256(hash)))
            errors.Add("The snapshot must declare one or more valid source RPF SHA-256 fingerprints.");

        var species = new List<EcologySpeciesIdentity>();
        foreach (var row in workspace.Species ?? new List<EcologySpeciesCurationRow>())
        {
            if (row is null)
            {
                errors.Add("The species table contains an empty row.");
                continue;
            }

            if (!IsValidId(row.Id) || !AllowedDomains.Contains(row.Domain, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add("Species entries require a valid ID and one of the four supported terrestrial/aquatic domains.");
                continue;
            }
            var modelIds = ParseDelimitedIds(row.ModelIdentifiers, "species model identifiers", errors);
            var habitatIds = ParseDelimitedIds(row.HabitatIds, "species habitat IDs", errors);
            if (!IsValidId(row.BehaviourProfileId))
                errors.Add("Species '" + row.Id + "' has an invalid behaviour profile ID.");
            var behaviourHash = ComputeDefinitionHash(row.BehaviourDefinitionJson, "species '" + row.Id + "' behaviour", errors);
            ValidateEvidenceLinks(row.Id, row.EvidenceCandidateIndexes, candidateMap,
                row.Domain, errors);
            species.Add(new EcologySpeciesIdentity(
                row.Id, row.Domain, modelIds, habitatIds,
                row.BehaviourProfileId, behaviourHash));
        }

        var habitats = new List<EcologyHabitatIdentity>();
        foreach (var row in workspace.Habitats ?? new List<EcologyHabitatCurationRow>())
        {
            if (row is null)
            {
                errors.Add("The habitat table contains an empty row.");
                continue;
            }
            if (!IsValidId(row.Id) || string.IsNullOrWhiteSpace(row.HabitatType))
                errors.Add("Habitat entries require a stable ID and habitat type.");
            if (!string.IsNullOrWhiteSpace(row.WaterBodyId) && !IsValidId(row.WaterBodyId))
                errors.Add("Habitat '" + row.Id + "' has an invalid water-body ID.");
            var definitionHash = ComputeDefinitionHash(row.DefinitionJson, "habitat '" + row.Id + "'", errors);
            ValidateEvidenceLinks(row.Id, row.EvidenceCandidateIndexes, candidateMap, "Habitat", errors);
            habitats.Add(new EcologyHabitatIdentity(
                row.Id, row.HabitatType, row.WaterBodyId ?? string.Empty, definitionHash));
        }

        var population = new List<EcologyPopulationEntry>();
        foreach (var row in workspace.PopulationEntries ?? new List<EcologyPopulationCurationRow>())
        {
            if (row is null)
            {
                errors.Add("The population table contains an empty row.");
                continue;
            }
            if (!IsValidId(row.Id) || !IsValidId(row.SpeciesId) || !IsValidId(row.HabitatId))
                errors.Add("Population entries require valid entry, species and habitat IDs.");
            var definitionHash = ComputeDefinitionHash(row.DefinitionJson, "population entry '" + row.Id + "'", errors);
            ValidateEvidenceLinks(row.Id, row.EvidenceCandidateIndexes, candidateMap, "Population", errors);
            population.Add(new EcologyPopulationEntry(
                row.Id, row.SpeciesId, row.HabitatId, definitionHash));
        }

        var waterBodies = new List<EcologyWaterBodyIdentity>();
        foreach (var row in workspace.WaterBodies ?? new List<EcologyWaterBodyCurationRow>())
        {
            if (row is null)
            {
                errors.Add("The water-body table contains an empty row.");
                continue;
            }
            if (!IsValidId(row.Id) || string.IsNullOrWhiteSpace(row.WaterType) ||
                !IsValidId(row.SimulationProfileId) ||
                !IsValidId(row.InteractionProfileId) ||
                !IsValidId(row.RenderingProfileId))
                errors.Add("Water bodies require stable IDs, a water type and simulation/interaction/rendering profile IDs.");
            var simulationHash = ComputeDefinitionHash(row.SimulationDefinitionJson, "water body '" + row.Id + "' simulation", errors);
            var interactionHash = ComputeDefinitionHash(row.InteractionDefinitionJson, "water body '" + row.Id + "' interaction", errors);
            var renderingHash = ComputeDefinitionHash(row.RenderingDefinitionJson, "water body '" + row.Id + "' rendering", errors);
            ValidateEvidenceLinks(row.Id, row.EvidenceCandidateIndexes, candidateMap, "Water", errors);
            waterBodies.Add(new EcologyWaterBodyIdentity(
                row.Id, row.WaterType, row.SimulationProfileId, row.InteractionProfileId,
                row.RenderingProfileId, simulationHash, interactionHash, renderingHash));
        }

        if (!AllowedDomains.All(domain => species.Any(item =>
                string.Equals(item.Domain, domain, StringComparison.OrdinalIgnoreCase))))
            errors.Add("The draft must include at least one reviewed species in each terrestrial/aquatic animal and plant domain.");
        if (habitats.Count == 0 || population.Count == 0 || waterBodies.Count == 0)
            errors.Add("The draft must include habitat, population and water-body definitions before it can be exported.");

        EcologyInventorySnapshot? snapshot = null;
        if (errors.Count == 0)
        {
            snapshot = new EcologyInventorySnapshot(
                1,
                workspace.GameBuild,
                workspace.GameExecutableSha256,
                workspace.ExtractorId,
                workspace.ExtractorVersion,
                sourceHashes,
                species,
                habitats,
                population,
                waterBodies);

            var semanticValidation = EcologyInventoryDiffService.Compare(snapshot, snapshot);
            if (!semanticValidation.VanillaPreserved)
            {
                errors.AddRange(semanticValidation.Errors);
                snapshot = null;
            }
        }

        if (errors.Count == 0)
            warnings.Add("This is a human-curated normalized snapshot, not proof of exhaustive coverage. The Hub still reports completeness=false and packageEligibilityUnlocked=false.");

        return new EcologyCurationValidationResult(snapshot, errors, warnings);
    }

    public async Task<(string SnapshotPath, string EvidencePath, string SnapshotSha256)> ExportNormalizedSnapshotAsync(
        EcologyInventoryCurationWorkspace workspace,
        string snapshotPath,
        CancellationToken cancellationToken = default)
    {
        var validation = BuildNormalizedSnapshot(workspace);
        if (validation.Errors.Count > 0 || validation.Snapshot is null)
            throw new InvalidDataException("The normalized ecology snapshot is invalid: " + string.Join(" ", validation.Errors));

        var fullSnapshotPath = Path.GetFullPath(snapshotPath);
        if (!Path.GetExtension(fullSnapshotPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The normalized snapshot output must use a .json extension.");
        var snapshotHash = await WriteJsonAtomicAsync(
            fullSnapshotPath, validation.Snapshot, EcologyInventoryEvidenceService.MaximumSnapshotBytes,
            cancellationToken);

        var evidence = new EcologyCurationExportEvidence(
            SchemaVersion: 1,
            ExportedAtUtc: DateTimeOffset.UtcNow,
            GameBuild: workspace.GameBuild,
            GameExecutableSha256: workspace.GameExecutableSha256,
            CandidateAnalysisReportFileName: workspace.CandidateAnalysisReportFileName,
            CandidateAnalysisReportSha256: workspace.CandidateAnalysisReportSha256,
            SnapshotFileName: Path.GetFileName(fullSnapshotPath),
            SnapshotSha256: snapshotHash,
            CandidateRows: workspace.Candidates.Count,
            ConfirmedCandidateRows: workspace.Candidates.Count(item => item.ReviewDecision.Equals("Confirmed", StringComparison.OrdinalIgnoreCase)),
            RejectedCandidateRows: workspace.Candidates.Count(item => item.ReviewDecision.Equals("Rejected", StringComparison.OrdinalIgnoreCase)),
            UnreviewedCandidateRows: workspace.Candidates.Count(item => item.ReviewDecision.Equals("Unreviewed", StringComparison.OrdinalIgnoreCase)),
            SpeciesCount: validation.Snapshot.Species.Count,
            HabitatCount: validation.Snapshot.Habitats.Count,
            PopulationEntryCount: validation.Snapshot.PopulationEntries.Count,
            WaterBodyCount: validation.Snapshot.WaterBodies.Count,
            HumanReviewed: workspace.HumanReviewed,
            CompletenessVerified: false,
            PackageEligibilityUnlocked: false,
            Limitation: "Source archive hashes in this snapshot are the source RPFs represented in the selected candidate report, not a certified exhaustive archive list. Candidate fields and normalized definitions are human-reviewed and may still omit unsupported resources or species.",
            Warnings: validation.Warnings);

        var evidencePath = fullSnapshotPath + ".curation-evidence.json";
        await WriteJsonAtomicAsync(
            evidencePath, evidence, EcologyInventoryEvidenceService.MaximumSnapshotBytes, cancellationToken);

        return (fullSnapshotPath, evidencePath, snapshotHash);
    }

    private static void ValidateEvidenceLinks(
        string canonicalId,
        string candidateIndexes,
        IReadOnlyDictionary<int, List<EcologyCurationCandidateRow>> candidates,
        string requiredCategory,
        ICollection<string> errors)
    {
        var indexes = ParseIndexes(candidateIndexes, errors);
        if (indexes.Count == 0)
        {
            errors.Add("Curation record '" + canonicalId + "' must link at least one confirmed candidate index.");
            return;
        }

        foreach (var index in indexes)
        {
            if (!candidates.TryGetValue(index, out var rows) ||
                !rows.Any(row =>
                    row.ReviewDecision.Equals("Confirmed", StringComparison.OrdinalIgnoreCase) &&
                    row.CanonicalId.Equals(canonicalId, StringComparison.OrdinalIgnoreCase) &&
                    (row.Category.Equals(requiredCategory, StringComparison.OrdinalIgnoreCase) ||
                     row.Category.Equals("ModelIdentifier", StringComparison.OrdinalIgnoreCase))))
            {
                errors.Add("Curation record '" + canonicalId + "' has no confirmed matching candidate evidence for index " + index + ".");
            }
        }
    }

    private static List<string> ParseDelimitedIds(string value, string label, ICollection<string> errors)
    {
        var values = (value ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (values.Count == 0 || values.Any(item => !IsValidId(item)))
            errors.Add("The " + label + " field must contain one or more semicolon-separated stable IDs.");
        if (values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count)
            errors.Add("The " + label + " field contains duplicate IDs.");
        return values;
    }

    private static List<int> ParseIndexes(string value, ICollection<string> errors)
    {
        var result = new List<int>();
        foreach (var part in (value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var index) || index < 0)
            {
                errors.Add("Candidate evidence indexes must be non-negative semicolon-separated integers.");
                continue;
            }
            if (!result.Contains(index))
                result.Add(index);
        }
        return result;
    }

    private static string ComputeDefinitionHash(string json, string label, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            errors.Add(label + " must include the reviewed source JSON definition.");
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                throw new JsonException("A definition must be a JSON object or array.");
            var canonicalBytes = JsonSerializer.SerializeToUtf8Bytes(document.RootElement);
            return Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant();
        }
        catch (JsonException exception)
        {
            errors.Add(label + " contains invalid definition JSON: " + exception.Message);
            return string.Empty;
        }
    }

    private static bool IsValidId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 64 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-') &&
        char.IsAsciiLetter(value[0]) &&
        !value.Contains("--", StringComparison.Ordinal) &&
        !value.EndsWith('-', StringComparison.Ordinal);

    private void ValidateWorkspaceIdentity(EcologyInventoryCurationWorkspace workspace)
    {
        if (workspace.SchemaVersion != 1 ||
            !Version.TryParse(workspace.GameBuild, out _) ||
            !IsSha256(workspace.GameExecutableSha256) ||
            !IsSha256(workspace.CandidateAnalysisReportSha256) ||
            string.IsNullOrWhiteSpace(workspace.CandidateAnalysisReportFileName) ||
            !Path.GetFileName(workspace.CandidateAnalysisReportFileName).Equals(
                workspace.CandidateAnalysisReportFileName, StringComparison.Ordinal) ||
            !string.Equals(workspace.ExtractorId, "gta-v-legacy-ecology-human-curation", StringComparison.Ordinal) ||
            !Version.TryParse(workspace.ExtractorVersion, out _))
            throw new InvalidDataException("The curation workspace has invalid game, extractor or candidate-report provenance.");

        if (workspace.Candidates is null || workspace.Candidates.Count == 0 ||
            workspace.Candidates.Count > RageCliEcologyCandidateAnalysisService.MaximumCandidates)
            throw new InvalidDataException("The curation workspace has no valid candidate review rows.");

        if (workspace.SourceArchiveSha256 is null || workspace.SourceArchiveSha256.Count == 0 ||
            workspace.SourceArchiveSha256.Any(hash => !IsSha256(hash)))
            throw new InvalidDataException("The curation workspace has no valid source archive fingerprint set.");
    }

    private async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var value = await JsonSerializer.DeserializeAsync<T>(
            stream, ReadJsonOptions, cancellationToken);
        return value ?? throw new InvalidDataException("A required curation provenance JSON document is empty.");
    }

    private static async Task<string> WriteJsonAtomicAsync<T>(
        string path,
        T value,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("The curation output path has no parent folder.");
        EnsureDirectoryTreeNoReparse(directory);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, WriteJsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            var info = new FileInfo(temporary);
            if (info.Length <= 0 || info.Length > maximumBytes)
                throw new InvalidDataException("The curation JSON file is empty or exceeds its size limit.");
            File.Move(temporary, fullPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }

        return await ComputeSha256Async(fullPath, cancellationToken);
    }

    private string ValidateOwnedFile(
        string root,
        string path,
        long maximumBytes,
        string description)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(path);
        EnsureContained(fullRoot, full);
        if (!File.Exists(full) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The " + description + " is missing or redirected.");
        EnsureContainedDirectoriesNoReparse(fullRoot, Path.GetDirectoryName(full)!);
        var info = new FileInfo(full);
        if (info.Length <= 0 || info.Length > maximumBytes)
            throw new InvalidDataException("The " + description + " is empty or exceeds its size limit.");
        return full;
    }

    private static string ValidateSourceArchive(string gameRoot, string archivePath)
    {
        var full = Path.GetFullPath(archivePath);
        EnsureContained(gameRoot, full);
        if (!full.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(full) ||
            (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("A candidate source archive is missing or is not an RPF inside the selected GTA V installation.");
        EnsureContainedDirectoriesNoReparse(gameRoot, Path.GetDirectoryName(full)!);
        return full;
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

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static void EnsureContained(string root, string path)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(path);
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A curation input/output path escapes its expected directory.");
    }

    private static void EnsureContainedDirectoriesNoReparse(string root, string directory)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullDirectory = Path.GetFullPath(directory);
        EnsureContained(fullRoot, fullDirectory);
        if ((File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A curation evidence directory is a reparse point.");

        var current = fullRoot;
        var relative = Path.GetRelativePath(fullRoot, fullDirectory);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) || (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A curation evidence directory is missing or redirected.");
        }
    }

    private static void EnsureDirectoryTreeNoReparse(string leaf)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(leaf));
        var root = Path.GetPathRoot(full)
            ?? throw new IOException("The curation output has no filesystem root.");
        var current = root;
        var relative = Path.GetRelativePath(root, full);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            Directory.CreateDirectory(current);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A curation output directory is a reparse point.");
        }
    }
}
