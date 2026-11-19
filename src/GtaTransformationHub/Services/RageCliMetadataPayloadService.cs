using System.Text.Json;

namespace GtaTransformationHub.Services;

public sealed record RageCliNestedArchiveExtractionPlan(
    IReadOnlyList<string> NestedArchivePaths,
    string ResourcePath);

public sealed record RageCliMetadataPayload(
    int CandidateIndex,
    string SourceArchivePath,
    string SourceArchiveSha256,
    string InternalPath,
    string ResourceName,
    IReadOnlyList<string> NestedArchivePaths,
    IReadOnlyList<string> NestedArchiveSha256,
    string DumpJsonPath,
    long DumpJsonBytes,
    string DumpJsonSha256,
    string JsonRootKind,
    RageCliJsonStructureSummary JsonStructure);

public sealed record RageCliMetadataDumpResult(
    string IndexReportPath,
    string IndexReportSha256,
    int CandidateCount,
    int StartingCandidateIndex,
    int NextCandidateIndex,
    int DumpedCount,
    bool Complete,
    TimeSpan Duration,
    IReadOnlyList<RageCliMetadataPayload> Payloads,
    IReadOnlyList<string> Errors);

public sealed record RageCliMetadataBatchWindow(
    int StartingCandidateIndex,
    int NextCandidateIndex,
    int CandidateCount);

/// <summary>
/// Extracts only explicitly identified nested RPF containers, then asks the pinned
/// rage-cli to dump individual Meta/PSO resources as JSON. It never uses recursive
/// extraction (which would read every leaf file) and never writes into the game
/// installation. Dump output is for inspection and semantic-parser development,
/// not proof that every ecological resource has been enumerated.
/// </summary>
public sealed class RageCliMetadataPayloadService
{
    public const int MaximumCandidatesToDump = 50;
    public const long MaximumNestedArchiveBytesPerCandidate = 512L * 1024 * 1024;
    public const long MaximumDumpJsonBytes = 128L * 1024 * 1024;
    public const long MaximumTotalDumpJsonBytes = 512L * 1024 * 1024;

    private static readonly TimeSpan ExtractTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DumpTimeout = TimeSpan.FromMinutes(2);

    private static readonly HashSet<string> SupportedMetadataExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ymt", ".meta", ".ytyp", ".ymap", ".ymf", ".pso"
    };

    private readonly RageCliToolchainService _toolchain;
    private readonly IRageCliProcessRunner _processRunner;
    private readonly string _dataRoot;

    public RageCliMetadataPayloadService(
        RageCliToolchainService toolchain,
        IRageCliProcessRunner? processRunner = null,
        string? dataRoot = null)
    {
        _toolchain = toolchain ?? throw new ArgumentNullException(nameof(toolchain));
        _processRunner = processRunner ?? new RageCliProcessRunner();
        _dataRoot = Path.GetFullPath(dataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "rage-cli"));
    }

    public async Task<RageCliMetadataDumpResult> DumpCandidatesAsync(
        string rageExecutablePath,
        GameInstallation installation,
        string discoveryReportPath,
        CancellationToken cancellationToken = default,
        string? namesFilePath = null,
        int startingCandidateIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var verifiedTool = await _toolchain.ValidateExecutableAsync(
            rageExecutablePath, cancellationToken);

        var gameExecutable = Path.GetFullPath(installation.ExecutablePath);
        var gameRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation.RootPath));
        if (!File.Exists(gameExecutable) ||
            !Path.GetFileName(gameExecutable).Equals("GTA5.exe", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetDirectoryName(gameExecutable)!.Equals(gameRoot, StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(gameRoot))
            throw new InvalidDataException("The selected GTA V executable and installation root do not match.");

        var gameExecutableSha256 = await RageCliToolchainService.ComputeSha256Async(
            gameExecutable, cancellationToken);
        var buildDirectory = RageCliToolchainService.BuildDirectoryNameFromExecutableHash(
            gameExecutableSha256);
        var expectedReportDirectory = Path.Combine(_dataRoot, "catalogue", buildDirectory);
        var safeDiscoveryReport = ValidateOwnedReportPath(expectedReportDirectory, discoveryReportPath);
        var discoveryReportJson = await File.ReadAllTextAsync(safeDiscoveryReport, cancellationToken);
        using var discoveryDocument = JsonDocument.Parse(discoveryReportJson);
        var discovery = discoveryDocument.RootElement;

        ValidateDiscoveryReport(
            discovery,
            gameRoot,
            gameExecutableSha256,
            verifiedTool);

        var allCandidates = ReadCandidates(discovery);
        var eligible = allCandidates
            .Where(IsSupportedMetadataCandidate)
            .DistinctBy(item => item.ArchivePath + "\u001f" + item.InternalPath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetCandidatePriority)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ArchivePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.InternalPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var batch = GetBatchWindow(eligible.Length, startingCandidateIndex);
        var selected = eligible
            .Skip(batch.StartingCandidateIndex)
            .Take(batch.CandidateCount)
            .ToArray();
        var errors = new List<string>();
        var hasMoreBatches = batch.NextCandidateIndex < eligible.Length;

        Directory.CreateDirectory(_dataRoot);
        EnsureDirectoryIsNotReparsePoint(_dataRoot);
        var metadataDumpRoot = Path.Combine(_dataRoot, "metadata-dumps");
        Directory.CreateDirectory(metadataDumpRoot);
        EnsureDirectoryIsNotReparsePoint(metadataDumpRoot);
        var payloadRoot = Path.Combine(metadataDumpRoot, buildDirectory);
        Directory.CreateDirectory(payloadRoot);
        EnsureDirectoryIsNotReparsePoint(payloadRoot);

        var stagingRoot = Path.Combine(_dataRoot, "staging");
        Directory.CreateDirectory(stagingRoot);
        EnsureDirectoryIsNotReparsePoint(stagingRoot);

        var payloads = new List<RageCliMetadataPayload>();
        long totalDumpJsonBytes = 0;
        for (var index = 0; index < selected.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (totalDumpJsonBytes >= MaximumTotalDumpJsonBytes)
            {
                errors.Add("The aggregate 512 MB metadata JSON output limit was reached. Remaining candidates were not dumped.");
                break;
            }

            var candidate = selected[index];
            var stagingDirectory = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDirectory);

            var temporaryJsonPath = string.Empty;
            try
            {
                var sourceArchive = ValidateArchivePathWithinGameRoot(gameRoot, candidate.ArchivePath);
                if (string.IsNullOrWhiteSpace(candidate.SourceArchiveSha256) ||
                    candidate.SourceArchiveSha256.Length != 64 ||
                    !candidate.SourceArchiveSha256.All(Uri.IsHexDigit))
                    throw new InvalidDataException("The discovery report has no valid source archive fingerprint.");

                var currentSourceHash = await RageCliToolchainService.ComputeSha256Async(
                    sourceArchive, cancellationToken);
                if (!currentSourceHash.Equals(candidate.SourceArchiveSha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The source archive changed since metadata discovery.");

                var plan = BuildNestedArchiveExtractionPlan(
                    candidate.InternalPath,
                    Path.GetFileName(sourceArchive));

                var currentArchive = sourceArchive;
                var nestedHashes = new List<string>();
                long extractedBytes = 0;

                for (var depth = 0; depth < plan.NestedArchivePaths.Count; depth++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var nestedRelativePath = plan.NestedArchivePaths[depth];
                    var levelOutput = Path.Combine(stagingDirectory, "level-" + depth.ToString("D2"));
                    Directory.CreateDirectory(levelOutput);
                    EnsureDirectoryIsNotReparsePoint(levelOutput);

                    var extractResult = await _processRunner.RunAsync(
                        verifiedTool.ExecutablePath,
                        BuildNestedArchiveExtractionArguments(
                            gameExecutable, currentArchive, nestedRelativePath, levelOutput),
                        ExtractTimeout,
                        cancellationToken);

                    if (extractResult.ExitCode != 0 ||
                        extractResult.StandardOutputTruncated ||
                        extractResult.StandardErrorTruncated)
                    {
                        throw new InvalidDataException(
                            "rage-cli failed to extract nested RPF '" + nestedRelativePath + "': " +
                            LimitForMessage(extractResult.StandardError + " " + extractResult.StandardOutput));
                    }

                    var extractedPath = ResolveContainedOutputPath(levelOutput, nestedRelativePath);
                    EnsureNoReparsePoints(levelOutput, extractedPath);
                    EnsureRegularFile(extractedPath, "nested RPF");
                    var nestedInfo = new FileInfo(extractedPath);
                    extractedBytes += nestedInfo.Length;
                    if (nestedInfo.Length == 0 || extractedBytes > MaximumNestedArchiveBytesPerCandidate)
                        throw new InvalidDataException(
                            "Nested RPF extraction exceeded the per-candidate 512 MB safety limit.");

                    var nestedHash = await RageCliToolchainService.ComputeSha256Async(
                        extractedPath, cancellationToken);
                    nestedHashes.Add(nestedHash);
                    currentArchive = extractedPath;
                }

                var resourcePath = plan.ResourcePath;
                var safeName = SafeFileName(candidate.Name);
                temporaryJsonPath = Path.Combine(
                    stagingDirectory,
                    "dump-" + index.ToString("D4") + "-" + safeName + ".partial.json");

                var dumpResult = await _processRunner.RunAsync(
                    verifiedTool.ExecutablePath,
                    BuildResourceDumpArguments(
                        gameExecutable, currentArchive, resourcePath, temporaryJsonPath, namesFilePath),
                    DumpTimeout,
                    cancellationToken);

                if (dumpResult.ExitCode != 0 ||
                    dumpResult.StandardOutputTruncated ||
                    dumpResult.StandardErrorTruncated)
                {
                    throw new InvalidDataException(
                        "rage-cli could not dump metadata resource '" + resourcePath + "': " +
                        LimitForMessage(dumpResult.StandardError + " " + dumpResult.StandardOutput));
                }

                EnsureRegularFile(temporaryJsonPath, "metadata JSON dump");
                var dumpInfo = new FileInfo(temporaryJsonPath);
                if (dumpInfo.Length == 0 || dumpInfo.Length > MaximumDumpJsonBytes)
                    throw new InvalidDataException("The metadata JSON dump is empty or exceeds 128 MB.");
                if (totalDumpJsonBytes + dumpInfo.Length > MaximumTotalDumpJsonBytes)
                    throw new InvalidDataException("The aggregate metadata JSON output would exceed 512 MB.");

                string jsonRootKind;
                RageCliJsonStructureSummary jsonStructure;
                await using (var dumpStream = new FileStream(
                    temporaryJsonPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                using (var parsed = await JsonDocument.ParseAsync(
                    dumpStream,
                    new JsonDocumentOptions { MaxDepth = RageCliJsonStructureAnalyzer.MaximumDepth },
                    cancellationToken))
                {
                    jsonRootKind = parsed.RootElement.ValueKind.ToString();
                    if (parsed.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                        throw new InvalidDataException("The metadata JSON dump has an unexpected root value.");
                    jsonStructure = RageCliJsonStructureAnalyzer.Analyze(parsed.RootElement);
                }

                var finalJsonPath = Path.Combine(
                    payloadRoot,
                    index.ToString("D4") + "-" + safeName + "-" +
                    Guid.NewGuid().ToString("N") + ".json");
                File.Move(temporaryJsonPath, finalJsonPath, overwrite: false);
                temporaryJsonPath = string.Empty;

                var finalInfo = new FileInfo(finalJsonPath);
                var finalHash = await RageCliToolchainService.ComputeSha256Async(
                    finalJsonPath, cancellationToken);

                var sourceHashAfter = await RageCliToolchainService.ComputeSha256Async(
                    sourceArchive, cancellationToken);
                if (!sourceHashAfter.Equals(candidate.SourceArchiveSha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(finalJsonPath);
                    throw new IOException("The source archive changed while its metadata was being dumped.");
                }

                totalDumpJsonBytes += finalInfo.Length;
                payloads.Add(new RageCliMetadataPayload(
                    startingCandidateIndex + index,
                    sourceArchive,
                    candidate.SourceArchiveSha256,
                    candidate.InternalPath,
                    candidate.Name,
                    plan.NestedArchivePaths,
                    nestedHashes,
                    finalJsonPath,
                    finalInfo.Length,
                    finalHash,
                    jsonRootKind,
                    jsonStructure));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or
                RageCliExecutionException or TimeoutException)
            {
                errors.Add("Metadata dump failed for candidate " + (startingCandidateIndex + index) + " ('" + candidate.InternalPath + "'): " +
                           LimitForMessage(exception.Message));
                // Preserve a contiguous prefix. The next batch resumes at this exact
                // candidate and will not overlap later successful payloads.
                break;
            }
            finally
            {
                if (Directory.Exists(stagingDirectory))
                {
                    try
                    {
                        Directory.Delete(stagingDirectory, recursive: true);
                    }
                    catch (Exception cleanupException) when (
                        cleanupException is IOException or UnauthorizedAccessException)
                    {
                        errors.Add("Could not remove temporary metadata extraction folder: " +
                                   LimitForMessage(cleanupException.Message));
                    }
                }
            }
        }

        var gameExecutableAfterSha256 = await RageCliToolchainService.ComputeSha256Async(
            gameExecutable, cancellationToken);
        if (!gameExecutableAfterSha256.Equals(gameExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("GTA5.exe changed during metadata extraction. The result index was rejected.");

        var indexReport = new MetadataDumpIndexReport(
            SchemaVersion: 3,
            ToolVersion: verifiedTool.Version,
            ToolSha256: verifiedTool.Sha256,
            GameBuild: installation.BuildVersion,
            GameExecutableSha256: gameExecutableSha256,
            DiscoveryReportPath: safeDiscoveryReport,
            DiscoveryReportSha256: await RageCliToolchainService.ComputeSha256Async(
                safeDiscoveryReport, cancellationToken),
            CandidateCount: eligible.Length,
            StartingCandidateIndex: batch.StartingCandidateIndex,
            NextCandidateIndex: GetNextCandidateIndex(batch, payloads),
            SelectedCandidateCount: selected.Length,
            MoreBatchesAvailable: hasMoreBatches,
            BatchComplete: errors.Count == 0 && payloads.Count == selected.Length,
            Errors: errors,
            Payloads: payloads);

        var indexReportPath = Path.Combine(
            payloadRoot,
            "metadata-dump-index-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") +
            "-" + Guid.NewGuid().ToString("N") + ".json");
        var tempIndexPath = indexReportPath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                tempIndexPath,
                JsonSerializer.Serialize(indexReport, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
            var tempInfo = new FileInfo(tempIndexPath);
            if (tempInfo.Length == 0 || tempInfo.Length > MaximumDumpJsonBytes)
                throw new InvalidDataException("The metadata dump index is empty or exceeds 128 MB.");
            File.Move(tempIndexPath, indexReportPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(tempIndexPath))
                File.Delete(tempIndexPath);
        }

        var indexHash = await RageCliToolchainService.ComputeSha256Async(
            indexReportPath, cancellationToken);
        timer.Stop();

        var discoveryComplete =
            discovery.TryGetProperty("Errors", out var discoveryErrors) &&
            discoveryErrors.ValueKind == JsonValueKind.Array &&
            discoveryErrors.GetArrayLength() == 0 &&
            discovery.TryGetProperty("PatternResults", out var patternResults) &&
            patternResults.ValueKind == JsonValueKind.Array &&
            patternResults.EnumerateArray().All(item =>
                item.TryGetProperty("HitLimit", out var capped) && capped.ValueKind == JsonValueKind.False &&
                item.TryGetProperty("OutputTruncated", out var truncated) && truncated.ValueKind == JsonValueKind.False &&
                item.TryGetProperty("HasArchiveErrors", out var archiveErrors) && archiveErrors.ValueKind == JsonValueKind.False);

        var nextCandidateIndex = GetNextCandidateIndex(batch, payloads);
        return new RageCliMetadataDumpResult(
            indexReportPath,
            indexHash,
            eligible.Length,
            batch.StartingCandidateIndex,
            nextCandidateIndex,
            payloads.Count,
            discoveryComplete && nextCandidateIndex == eligible.Length &&
                errors.Count == 0 && payloads.Count == selected.Length,
            timer.Elapsed,
            payloads,
            errors);
    }

    public static RageCliMetadataBatchWindow GetBatchWindow(
        int totalCandidateCount,
        int startingCandidateIndex)
    {
        if (totalCandidateCount < 0)
            throw new ArgumentOutOfRangeException(nameof(totalCandidateCount));
        if (startingCandidateIndex < 0 || startingCandidateIndex > totalCandidateCount)
            throw new ArgumentOutOfRangeException(
                nameof(startingCandidateIndex),
                "The starting candidate index must be between zero and the total candidate count.");

        var selectedCount = Math.Min(MaximumCandidatesToDump, totalCandidateCount - startingCandidateIndex);
        return new RageCliMetadataBatchWindow(
            startingCandidateIndex,
            startingCandidateIndex + selectedCount,
            selectedCount);
    }

    private static int GetNextCandidateIndex(
        RageCliMetadataBatchWindow batch,
        IReadOnlyCollection<RageCliMetadataPayload> payloads)
    {
        var completedIndexes = payloads
            .Select(payload => payload.CandidateIndex)
            .ToHashSet();

        for (var candidateIndex = batch.StartingCandidateIndex;
             candidateIndex < batch.NextCandidateIndex;
             candidateIndex++)
        {
            if (!completedIndexes.Contains(candidateIndex))
                return candidateIndex;
        }

        return batch.NextCandidateIndex;
    }

    public static RageCliNestedArchiveExtractionPlan BuildNestedArchiveExtractionPlan(
        string internalPath,
        string rootArchiveFileName)
    {
        if (string.IsNullOrWhiteSpace(internalPath) || string.IsNullOrWhiteSpace(rootArchiveFileName))
            throw new ArgumentException("An internal RPF path and root archive name are required.");

        var normalized = internalPath.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Contains(':'))
            throw new InvalidDataException("A metadata path must be archive-relative.");

        var segments = normalized.Split('/');
        if (segments.Length < 2 ||
            !segments[0].Equals(rootArchiveFileName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The metadata path does not begin with its reported root archive.");

        if (segments.Any(segment =>
                segment.Length == 0 ||
                segment == "." ||
                segment == ".." ||
                segment.Contains(':') ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("The metadata path contains an unsafe path segment.");

        var nestedArchives = new List<string>();
        var start = 1;
        for (var index = 1; index < segments.Length; index++)
        {
            if (!segments[index].EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                continue;

            nestedArchives.Add(string.Join(
                '/',
                segments.Skip(start).Take(index - start + 1)));
            start = index + 1;
        }

        if (start >= segments.Length)
            throw new InvalidDataException("The metadata path does not identify a resource after the RPF chain.");

        var resourcePath = string.Join('/', segments.Skip(start));
        if (resourcePath.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The metadata path resolves to an archive, not a metadata resource.");

        return new RageCliNestedArchiveExtractionPlan(nestedArchives, resourcePath);
    }

    public static IReadOnlyList<string> BuildNestedArchiveExtractionArguments(
        string gameExecutablePath,
        string archivePath,
        string nestedArchivePath,
        string outputDirectory) =>
        new[]
        {
            "--no-update-check",
            "--exe", Path.GetFullPath(gameExecutablePath),
            "extract",
            Path.GetFullPath(archivePath),
            nestedArchivePath,
            "--output", Path.GetFullPath(outputDirectory)
        };

    public static IReadOnlyList<string> BuildRawResourceExtractionArguments(
        string gameExecutablePath,
        string archivePath,
        string resourcePath,
        string outputDirectory)
    {
        var normalizedResourcePath = resourcePath.Replace('\\\\', '/');
        if (string.IsNullOrWhiteSpace(normalizedResourcePath) ||
            normalizedResourcePath.StartsWith('/') ||
            normalizedResourcePath.Contains(':') ||
            normalizedResourcePath.Split('/').Any(segment =>
                segment.Length == 0 || segment is "." or ".."))
            throw new InvalidDataException("A raw resource extraction path must be a safe archive-relative path.");

        return new[]
        {
            "--no-update-check",
            "--exe", Path.GetFullPath(gameExecutablePath),
            "extract",
            Path.GetFullPath(archivePath),
            normalizedResourcePath,
            "--output", Path.GetFullPath(outputDirectory)
        };
    }

    public static IReadOnlyList<string> BuildResourceDumpArguments(
        string gameExecutablePath,
        string archivePath,
        string resourcePath,
        string outputJsonPath,
        string? namesFilePath = null)
    {
        var arguments = new List<string>
        {
            "--no-update-check",
            "--exe", Path.GetFullPath(gameExecutablePath),
            "resource",
            "dump",
            resourcePath,
            "--archive", Path.GetFullPath(archivePath),
            "--json"
        };

        if (!string.IsNullOrWhiteSpace(namesFilePath))
        {
            var namesPath = Path.GetFullPath(namesFilePath);
            if (!File.Exists(namesPath) ||
                (File.GetAttributes(namesPath) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The optional game names list is missing or redirected.");
            arguments.Add("--names");
            arguments.Add(namesPath);
        }

        arguments.Add("--output");
        arguments.Add(Path.GetFullPath(outputJsonPath));
        return arguments;
    }

    private static string ValidateOwnedReportPath(string reportDirectory, string reportPath)
    {
        var fullPath = Path.GetFullPath(reportPath);
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(reportDirectory)) +
                     Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullPath) ||
            !fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Select a metadata discovery report created by this Hub for the current GTA V build.");

        EnsureNoReparsePoints(reportDirectory, fullPath);
        return fullPath;
    }

    private static void ValidateDiscoveryReport(
        JsonElement report,
        string gameRoot,
        string gameExecutableSha256,
        RageCliToolchainInfo verifiedTool)
    {
        if (!report.TryGetProperty("SchemaVersion", out var schema) ||
            !schema.TryGetInt32(out var schemaVersion) || schemaVersion != 1)
            throw new InvalidDataException("The metadata discovery report schema is unsupported.");

        if (!MatchesString(report, "GameRoot", gameRoot) ||
            !MatchesString(report, "GameExecutableSha256", gameExecutableSha256) ||
            !MatchesString(report, "ToolVersion", verifiedTool.Version) ||
            !MatchesString(report, "ToolSha256", verifiedTool.Sha256))
            throw new InvalidDataException(
                "The metadata discovery report does not match the selected game build and pinned rage-cli executable.");

        if (!report.TryGetProperty("Candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The metadata discovery report has no candidate array.");
    }

    private static bool MatchesString(JsonElement element, string propertyName, string expected) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        string.Equals(value.GetString(), expected, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<RageCliMetadataHit> ReadCandidates(JsonElement report)
    {
        var candidates = report.GetProperty("Candidates")
            .Deserialize<RageCliMetadataHit[]>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false
            });
        if (candidates is null ||
            candidates.Any(item => item is null ||
                string.IsNullOrWhiteSpace(item.ArchivePath) ||
                string.IsNullOrWhiteSpace(item.InternalPath) ||
                string.IsNullOrWhiteSpace(item.Name) ||
                string.IsNullOrWhiteSpace(item.Kind)))
            throw new InvalidDataException("The metadata discovery candidate array contains an invalid record.");

        return candidates;
    }

    private static bool IsSupportedMetadataCandidate(RageCliMetadataHit candidate) =>
        candidate is not null &&
        candidate.Kind.Equals("resource", StringComparison.OrdinalIgnoreCase) &&
        SupportedMetadataExtensions.Contains(Path.GetExtension(candidate.Name));

    private static int GetCandidatePriority(RageCliMetadataHit candidate)
    {
        var name = candidate.Name;
        if (new[] { "popgroups", "popcycle", "pedmodelsets", "animal", "fish", "water", "vegetation", "flora", "fauna" }
            .Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase)))
            return 0;

        return Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".ymt" => 1,
            ".meta" => 2,
            ".ytyp" => 3,
            ".ymf" => 4,
            ".pso" => 5,
            ".ymap" => 6,
            _ => 7
        };
    }

    internal static string ValidateArchivePathWithinGameRoot(string gameRoot, string archivePath)
    {
        var fullPath = Path.GetFullPath(archivePath);
        var prefix = gameRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !fullPath.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullPath))
            throw new InvalidDataException("A metadata candidate references an RPF outside the selected game folder.");

        EnsureNoReparsePoints(gameRoot, fullPath);
        return fullPath;
    }

    internal static string ResolveContainedOutputPath(string outputDirectory, string relativePath)
    {
        var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
            throw new InvalidDataException("An extracted RPF path must remain relative to its staging folder.");

        var fullPath = Path.GetFullPath(Path.Combine(outputDirectory, normalized));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory)) +
                     Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("rage-cli's extracted path escaped its staging folder.");

        return fullPath;
    }

    internal static void EnsureNoReparsePoints(string root, string destination)
    {
        var current = Path.GetFullPath(root);
        if (!Directory.Exists(current) ||
            (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A metadata extraction path has an invalid or redirected root directory.");

        var relative = Path.GetRelativePath(current, Path.GetFullPath(destination));
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A metadata extraction path traverses a reparse point.");
        }
    }

    internal static void EnsureDirectoryIsNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A metadata dump directory cannot be a reparse point.");
    }

    internal static void EnsureRegularFile(string path, string description)
    {
        if (!File.Exists(path))
            throw new InvalidDataException("rage-cli did not create the expected " + description + ".");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The " + description + " is a reparse point.");
    }

    private static string SafeFileName(string name)
    {
        var cleaned = new string(name.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'
                ? character
                : '_').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "metadata" : cleaned[..Math.Min(cleaned.Length, 96)];
    }

    internal static string LimitForMessage(string value)
    {
        const int limit = 3000;
        var text = value.Trim();
        return text.Length <= limit ? text : text[..limit] + " ...";
    }

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
}
