using System.Security.Cryptography;
using System.Text.Json;

namespace GtaTransformationHub.Services;

public sealed record EcologyMetadataCandidate(
    string Category,
    string ResourceName,
    string InternalArchivePath,
    string JsonPath,
    string StructureType,
    string FieldName,
    string CandidateValue,
    string SourceArchivePath,
    string SourceArchiveSha256,
    string DumpJsonSha256,
    string EvidenceNote);

public sealed record EcologyMetadataCandidateAnalysisResult(
    string ReportPath,
    string ReportSha256,
    string GameBuild,
    string GameExecutableSha256,
    int PayloadsReviewed,
    int CandidateCount,
    IReadOnlyDictionary<string, int> CategoryCounts,
    bool InputIndexWasComplete,
    bool CompletenessVerified,
    bool PackageEligibilityUnlocked,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<EcologyMetadataCandidate> Candidates);

/// <summary>
/// Converts bounded rage-cli metadata JSON dumps into a reviewable, provenance-linked
/// candidate index. Classification is deliberately heuristic: a value found under a
/// population, animal, plant, habitat or water-like field is not by itself a proven
/// species identity. This service never creates a trusted vanilla baseline or unlocks
/// ecology-sensitive package deployment.
/// </summary>
public sealed class RageCliEcologyCandidateAnalysisService
{
    public const int MaximumCandidates = 10000;
    public const int MaximumNodesPerPayload = 1_000_000;
    public const int MaximumValueLength = 256;
    public const long MaximumIndexBytes = 64L * 1024 * 1024;
    public const long MaximumCandidateReportBytes = 64L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = RageCliJsonStructureAnalyzer.MaximumDepth
    };

    private readonly RageCliToolchainService _toolchain;
    private readonly string _dataRoot;
    private readonly string _evidenceRoot;

    private sealed record MetadataDumpIndexReport(
        int SchemaVersion,
        string ToolVersion,
        string ToolSha256,
        string GameBuild,
        string GameExecutableSha256,
        string DiscoveryReportPath,
        string DiscoveryReportSha256,
        int CandidateCount,
        bool CandidateCapReached,
        IReadOnlyList<string> Errors,
        IReadOnlyList<RageCliMetadataPayload> Payloads);

    private sealed record DiscoveryProvenanceReport(
        int SchemaVersion,
        string ToolVersion,
        string ToolSha256,
        string GameBuild,
        string GameExecutableSha256,
        IReadOnlyList<RageCliMetadataPatternResult> PatternResults,
        IReadOnlyList<string> Errors);

    private sealed record CandidateAnalysisReport(
        int SchemaVersion,
        DateTimeOffset CreatedAtUtc,
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
        string Limitation,
        IReadOnlyDictionary<string, int> CategoryCounts,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<EcologyMetadataCandidate> Candidates);

    public RageCliEcologyCandidateAnalysisService(
        RageCliToolchainService toolchain,
        string? dataRoot = null,
        string? evidenceRoot = null)
    {
        _toolchain = toolchain ?? throw new ArgumentNullException(nameof(toolchain));
        _dataRoot = Path.GetFullPath(dataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "rage-cli"));
        _evidenceRoot = Path.GetFullPath(evidenceRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "ecology-evidence", "candidate-analysis"));
    }

    public async Task<EcologyMetadataCandidateAnalysisResult> AnalyzeAsync(
        string rageExecutablePath,
        GameInstallation installation,
        string metadataDumpIndexPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var tool = await _toolchain.ValidateExecutableAsync(rageExecutablePath, cancellationToken);
        var (gameExecutable, gameRoot) = ValidateInstallation(installation);
        var gameHash = await RageCliToolchainService.ComputeSha256Async(gameExecutable, cancellationToken);
        var buildDirectory = RageCliToolchainService.BuildDirectoryNameFromExecutableHash(gameHash);

        var metadataDirectory = Path.Combine(_dataRoot, "metadata-dumps", buildDirectory);
        var safeIndexPath = ValidateOwnedFile(metadataDirectory, metadataDumpIndexPath, MaximumIndexBytes, "metadata dump index");
        var indexHash = await RageCliToolchainService.ComputeSha256Async(safeIndexPath, cancellationToken);
        var index = await ReadJsonAsync<MetadataDumpIndexReport>(safeIndexPath, cancellationToken);

        ValidateIndexProvenance(index, installation.BuildVersion, gameHash, tool);

        var discoveryDirectory = Path.Combine(_dataRoot, "catalogue", buildDirectory);
        var discoveryPath = ValidateOwnedFile(
            discoveryDirectory, index.DiscoveryReportPath, 64L * 1024 * 1024,
            "metadata discovery report");
        var discoveryHash = await RageCliToolchainService.ComputeSha256Async(discoveryPath, cancellationToken);
        if (!discoveryHash.Equals(index.DiscoveryReportSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The metadata discovery report changed after the dump index was created.");

        var discovery = await ReadJsonAsync<DiscoveryProvenanceReport>(discoveryPath, cancellationToken);
        ValidateDiscoveryProvenance(discovery, installation.BuildVersion, gameHash, tool);

        if (index.Payloads is null || index.Payloads.Count == 0 ||
            index.Payloads.Count > RageCliMetadataPayloadService.MaximumCandidatesToDump ||
            index.CandidateCount != index.Payloads.Count)
            throw new InvalidDataException("The metadata dump index has an invalid payload count.");

        var warnings = new List<string>();
        if (index.CandidateCapReached)
            warnings.Add("The original metadata discovery or dump stage reached a candidate cap.");
        if (index.Errors is { Count: > 0 })
            warnings.AddRange(index.Errors.Take(100));
        if (discovery.Errors is { Count: > 0 })
            warnings.AddRange(discovery.Errors.Take(100));
        if (discovery.PatternResults is null ||
            discovery.PatternResults.Any(result => result.HitLimit || result.OutputTruncated || result.HasArchiveErrors))
            warnings.Add("One or more discovery patterns hit a limit, truncated output or had archive errors.");
        if (index.Payloads.Count < index.CandidateCount)
            warnings.Add("The dump index contains fewer payloads than its declared candidate count.");

        var candidates = new List<EcologyMetadataCandidate>();
        var payloadsReviewed = 0;
        var payloadDirectory = Path.Combine(_dataRoot, "metadata-dumps", buildDirectory);

        foreach (var payload in index.Payloads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceArchive = ValidateSourceArchive(gameRoot, payload.SourceArchivePath);
            var currentArchiveHash = await RageCliToolchainService.ComputeSha256Async(sourceArchive, cancellationToken);
            if (!currentArchiveHash.Equals(payload.SourceArchiveSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The original source RPF changed after metadata extraction: " + payload.InternalPath);

            var jsonPath = ValidateOwnedFile(
                payloadDirectory, payload.DumpJsonPath, RageCliMetadataPayloadService.MaximumDumpJsonBytes,
                "metadata JSON payload");
            var jsonInfo = new FileInfo(jsonPath);
            if (jsonInfo.Length != payload.DumpJsonBytes)
                throw new InvalidDataException("A metadata JSON payload size no longer matches its dump index.");
            var jsonHash = await RageCliToolchainService.ComputeSha256Async(jsonPath, cancellationToken);
            if (!jsonHash.Equals(payload.DumpJsonSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("A metadata JSON payload changed after it was indexed: " + payload.InternalPath);

            await using var stream = new FileStream(
                jsonPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument.ParseAsync(
                stream,
                new JsonDocumentOptions { MaxDepth = RageCliJsonStructureAnalyzer.MaximumDepth },
                cancellationToken);

            var before = candidates.Count;
            ExtractCandidates(
                document.RootElement,
                payload.ResourceName,
                payload.InternalPath,
                sourceArchive,
                payload.SourceArchiveSha256,
                payload.DumpJsonSha256,
                candidates,
                warnings,
                MaximumCandidates);
            payloadsReviewed++;
            if (candidates.Count == MaximumCandidates)
            {
                warnings.Add("The semantic candidate review reached its 10,000-record output cap.");
                break;
            }

            if (candidates.Count == before)
                warnings.Add("No field names matched the current ecology candidate rules for resource '" +
                             payload.ResourceName + "'. Review the raw JSON manually.");
        }

        var gameHashAfter = await RageCliToolchainService.ComputeSha256Async(gameExecutable, cancellationToken);
        if (!gameHashAfter.Equals(gameHash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("GTA5.exe changed during candidate analysis. The report was rejected.");

        var categoryCounts = candidates
            .GroupBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var indexComplete = index.Errors is { Count: 0 } &&
                            discovery.Errors is { Count: 0 } &&
                            discovery.PatternResults is { Count: > 0 } &&
                            discovery.PatternResults.All(item =>
                                !item.HitLimit && !item.OutputTruncated && !item.HasArchiveErrors) &&
                            !index.CandidateCapReached &&
                            index.Payloads.Count == index.CandidateCount;

        const string limitation =
            "Candidates are heuristic field/path matches from the bounded metadata resources that were successfully dumped. " +
            "They may include false positives and may miss data stored in unsearched or unsupported resources. " +
            "No species, habitat, population or water identity is certified by this report. " +
            "CompletenessVerified and PackageEligibilityUnlocked are always false.";

        Directory.CreateDirectory(_evidenceRoot);
        EnsureDirectoryPathIsNotReparsePoint(
            Path.GetPathRoot(_evidenceRoot)!, _evidenceRoot);
        var reportPath = Path.Combine(
            _evidenceRoot,
            "ecology-candidates-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") +
            "-" + Guid.NewGuid().ToString("N") + ".json");
        var temporaryPath = reportPath + ".tmp";

        var report = new CandidateAnalysisReport(
            1,
            DateTimeOffset.UtcNow,
            installation.BuildVersion,
            gameHash,
            tool.Version,
            tool.Sha256,
            Path.GetFileName(safeIndexPath),
            indexHash,
            Path.GetFileName(discoveryPath),
            discoveryHash,
            payloadsReviewed,
            candidates.Count,
            indexComplete,
            CompletenessVerified: false,
            PackageEligibilityUnlocked: false,
            limitation,
            categoryCounts,
            warnings,
            candidates);

        try
        {
            await using (var output = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    output, report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase },
                    cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            var reportInfo = new FileInfo(temporaryPath);
            if (reportInfo.Length <= 0 || reportInfo.Length > MaximumCandidateReportBytes)
                throw new InvalidDataException("The candidate analysis report is empty or exceeds the 64 MB limit.");
            File.Move(temporaryPath, reportPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }

        var reportSha = await RageCliToolchainService.ComputeSha256Async(reportPath, cancellationToken);
        return new EcologyMetadataCandidateAnalysisResult(
            reportPath,
            reportSha,
            installation.BuildVersion,
            gameHash,
            payloadsReviewed,
            candidates.Count,
            categoryCounts,
            indexComplete,
            CompletenessVerified: false,
            PackageEligibilityUnlocked: false,
            warnings,
            candidates);
    }

    public static string? ClassifyCandidateField(
        string resourceName,
        string structureType,
        string jsonPath,
        string fieldName)
    {
        var pathContext = (structureType + "/" + jsonPath + "/" + fieldName).ToLowerInvariant();
        var fileContext = resourceName.ToLowerInvariant();

        if (ContainsAny(pathContext, "water", "ocean", "river", "lake", "wave", "underwater", "shoreline", "waterbody"))
            return "Water";
        if (ContainsAny(pathContext, "population", "popgroup", "popcycle", "modelset", "spawn", "pedgroup"))
            return "Population";
        if (ContainsAny(pathContext, "habitat", "biome", "ecology", "zone"))
            return "Habitat";
        if (ContainsAny(pathContext, "vegetation", "flora", "plant", "tree", "grass", "reed", "seagrass", "kelp"))
            return "Plant";
        if (ContainsAny(pathContext, "animal", "fauna", "wildlife", "fish", "marine", "bird", "deer", "coyote", "boar", "shark", "whale"))
            return "Animal";
        if (ContainsAny(fieldName.ToLowerInvariant(), "modelname", "model_name", "archetype", "speciesmodel") &&
            ContainsAny(fileContext, "animal", "fauna", "wildlife", "fish", "plant", "flora", "vegetation"))
            return "ModelIdentifier";

        return null;
    }

    public static IReadOnlyList<EcologyMetadataCandidate> ExtractCandidatesFromJson(
        string json,
        string resourceName,
        string internalArchivePath,
        string sourceArchivePath,
        string sourceArchiveSha256,
        string dumpJsonSha256)
    {
        using var document = JsonDocument.Parse(
            json, new JsonDocumentOptions { MaxDepth = RageCliJsonStructureAnalyzer.MaximumDepth });
        var candidates = new List<EcologyMetadataCandidate>();
        var warnings = new List<string>();
        ExtractCandidates(
            document.RootElement, resourceName, internalArchivePath, sourceArchivePath,
            sourceArchiveSha256, dumpJsonSha256, candidates, warnings, MaximumCandidates);
        return candidates;
    }

    private static void ExtractCandidates(
        JsonElement root,
        string resourceName,
        string internalArchivePath,
        string sourceArchivePath,
        string sourceArchiveSha256,
        string dumpJsonSha256,
        ICollection<EcologyMetadataCandidate> output,
        ICollection<string> warnings,
        int maximumCandidates)
    {
        var nodes = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(JsonElement element, string path, string structureType, string? fieldName, string inheritedContext)
        {
            if (++nodes > MaximumNodesPerPayload)
            {
                warnings.Add("The JSON walker reached the one-million-node cap for '" + resourceName + "'.");
                return;
            }
            if (output.Count >= maximumCandidates)
                return;

            if (element.ValueKind == JsonValueKind.Object)
            {
                var localType = structureType;
                if (element.TryGetProperty("$type", out var typeElement) &&
                    typeElement.ValueKind == JsonValueKind.String)
                    localType = typeElement.GetString() ?? structureType;

                var localContext = inheritedContext + "/" + localType + "/" + path;
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("$type"))
                        continue;
                    var childPath = path + "/" + EscapeJsonPointer(property.Name);
                    Visit(property.Value, childPath, localType, property.Name, localContext);
                    if (nodes > MaximumNodesPerPayload || output.Count >= maximumCandidates)
                        break;
                }
                return;
            }

            if (element.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Visit(item, path + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        structureType, fieldName, inheritedContext);
                    index++;
                    if (nodes > MaximumNodesPerPayload || output.Count >= maximumCandidates)
                        break;
                }
                return;
            }

            if (fieldName is null || element.ValueKind is not (
                    JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))
                return;

            var currentCategory = ClassifyCandidateField(string.Empty, structureType, path, fieldName);
            var inheritedCategory = ClassifyCandidateField(string.Empty, inheritedContext, path, fieldName);
            var category = currentCategory ?? inheritedCategory ??
                           ClassifyCandidateField(resourceName, structureType, path, fieldName);
            if (category is null)
                return;

            var value = element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? string.Empty
                : element.ToString();
            if (string.IsNullOrWhiteSpace(value))
                return;

            var truncated = value.Length > MaximumValueLength;
            if (truncated)
                value = value[..MaximumValueLength] + "...[truncated]";
            var key = category + "|" + path + "|" + value;
            if (!seen.Add(key))
                return;

            output.Add(new EcologyMetadataCandidate(
                category,
                resourceName,
                internalArchivePath,
                path,
                structureType,
                fieldName,
                value,
                sourceArchivePath,
                sourceArchiveSha256,
                dumpJsonSha256,
                truncated
                    ? "Heuristic field/path match; candidate value was truncated."
                    : "Heuristic field/path match only; verify the underlying resource semantics."));
        }

        Visit(root, "$", string.Empty, null, string.Empty);
    }

    private static void ValidateIndexProvenance(
        MetadataDumpIndexReport index,
        string expectedBuild,
        string expectedGameHash,
        RageCliToolchainInfo tool)
    {
        if (index.SchemaVersion != 1 ||
            !index.GameBuild.Equals(expectedBuild, StringComparison.OrdinalIgnoreCase) ||
            !index.GameExecutableSha256.Equals(expectedGameHash, StringComparison.OrdinalIgnoreCase) ||
            !index.ToolVersion.Equals(tool.Version, StringComparison.OrdinalIgnoreCase) ||
            !index.ToolSha256.Equals(tool.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !IsSha256(index.DiscoveryReportSha256))
            throw new InvalidDataException(
                "The metadata dump index is not bound to this exact GTA5.exe build/hash and pinned rage-cli tool.");
    }

    private static void ValidateDiscoveryProvenance(
        DiscoveryProvenanceReport report,
        string expectedBuild,
        string expectedGameHash,
        RageCliToolchainInfo tool)
    {
        if (report.SchemaVersion != 1 ||
            !report.GameBuild.Equals(expectedBuild, StringComparison.OrdinalIgnoreCase) ||
            !report.GameExecutableSha256.Equals(expectedGameHash, StringComparison.OrdinalIgnoreCase) ||
            !report.ToolVersion.Equals(tool.Version, StringComparison.OrdinalIgnoreCase) ||
            !report.ToolSha256.Equals(tool.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "The metadata discovery report is not bound to this exact GTA5.exe build/hash and pinned rage-cli tool.");
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        return value ?? throw new InvalidDataException("A required provenance JSON document was empty.");
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
        var full = Path.GetFullPath(archivePath);
        EnsureContained(gameRoot, full);
        EnsureDirectoryPathIsNotReparsePoint(gameRoot, Path.GetDirectoryName(full)!);
        if (!File.Exists(full) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("A source archive is missing or redirected.");
        return full;
    }

    private static string ValidateOwnedFile(string root, string path, long maxBytes, string description)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(path);
        EnsureContained(fullRoot, full);
        if (!File.Exists(full))
            throw new FileNotFoundException("The " + description + " was not found.", full);
        EnsureDirectoryPathIsNotReparsePoint(fullRoot, Path.GetDirectoryName(full)!);
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The " + description + " is a reparse point.");
        var info = new FileInfo(full);
        if (info.Length <= 0 || info.Length > maxBytes)
            throw new InvalidDataException("The " + description + " is empty or exceeds its safety limit.");
        return full;
    }

    private static void EnsureContained(string root, string path)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A provenance path escapes its expected local directory.");
    }

    private static void EnsureDirectoryPathIsNotReparsePoint(string root, string directory)
    {
        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullDirectory = Path.GetFullPath(directory);
        EnsureContained(rootPath, fullDirectory);
        if ((File.GetAttributes(rootPath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A provenance directory is a reparse point.");

        var relative = Path.GetRelativePath(rootPath, fullDirectory);
        var current = rootPath;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) || (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A provenance directory is missing or redirected.");
        }
    }

    private static void EnsureDirectoryPathIsNotReparsePoint(string root, string leaf)
    {
        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullLeaf = Path.GetFullPath(leaf);
        var filesystemRoot = Path.GetPathRoot(rootPath)
            ?? throw new IOException("The candidate evidence directory has no filesystem root.");
        var current = filesystemRoot;
        var relative = Path.GetRelativePath(filesystemRoot, rootPath);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            Directory.CreateDirectory(current);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A candidate evidence directory is a reparse point.");
        }
        EnsureDirectoryPathIsNotReparsePoint(rootPath, fullLeaf);
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.Ordinal));

    private static string EscapeJsonPointer(string value) =>
        value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
