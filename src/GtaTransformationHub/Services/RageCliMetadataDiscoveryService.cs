using System.Text.Json;

namespace GtaTransformationHub.Services;

public sealed record RageCliMetadataHit(
    string ArchivePath,
    string InternalPath,
    string Name,
    long FileSize,
    long MemorySize,
    string Kind,
    string NameHash,
    string StemHash,
    IReadOnlyList<string> MatchedPatterns,
    string? SourceArchiveSha256);

public sealed record RageCliMetadataPatternResult(
    string Pattern,
    int HitCount,
    bool HitLimit,
    bool OutputTruncated,
    bool HasArchiveErrors,
    string Diagnostic);

public sealed record RageCliMetadataDiscoveryResult(
    string ReportPath,
    long ReportBytes,
    string ReportSha256,
    string GameExecutableSha256,
    int UniqueMetadataCandidates,
    int SourceArchivesHashed,
    bool PatternDiscoveryComplete,
    TimeSpan Duration,
    IReadOnlyList<RageCliMetadataPatternResult> PatternResults,
    IReadOnlyList<string> Errors);

/// <summary>
/// Finds likely GTA V metadata and ecology-related resources inside nested RPF
/// archives using a checksum-pinned rage-cli. This is a discovery/indexing step only:
/// it does not dump metadata contents, establish a complete species baseline, or
/// enable ecology-sensitive package installation.
/// </summary>
public sealed class RageCliMetadataDiscoveryService
{
    public const int MaximumHitsPerPattern = 10_000;

    private static readonly TimeSpan SearchTimeout = TimeSpan.FromMinutes(15);
    private const long MaximumReportBytes = 64L * 1024 * 1024;

    // Search by supported metadata container extension in one pass per file type.
    // Ecology keywords are prioritized after parsing the hit list; separate keyword
    // searches would rescan every nested RPF archive and return the same files repeatedly.
    public static readonly IReadOnlyList<string> EcologyMetadataPatterns = new[]
    {
        "*.ymt",
        "*.meta",
        "*.ytyp",
        "*.ymap",
        "*.ymf",
        "*.pso",
        "*.xml"
    };

    private readonly RageCliToolchainService _toolchain;
    private readonly IRageCliProcessRunner _processRunner;
    private readonly string _dataRoot;

    public RageCliMetadataDiscoveryService(
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

    public async Task<RageCliMetadataDiscoveryResult> DiscoverAsync(
        string rageExecutablePath,
        GameInstallation installation,
        CancellationToken cancellationToken = default)
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
        {
            throw new InvalidDataException(
                "The selected GTA V executable and installation root do not match.");
        }

        var gameExecutableSha256 = await RageCliToolchainService.ComputeSha256Async(
            gameExecutable, cancellationToken);
        var buildDirectory = RageCliToolchainService.BuildDirectoryNameFromExecutableHash(
            gameExecutableSha256);
        Directory.CreateDirectory(_dataRoot);
        EnsureDirectoryIsNotReparsePoint(_dataRoot);

        var catalogueRoot = Path.Combine(_dataRoot, "catalogue");
        Directory.CreateDirectory(catalogueRoot);
        EnsureDirectoryIsNotReparsePoint(catalogueRoot);

        var reportDirectory = Path.Combine(catalogueRoot, buildDirectory);
        Directory.CreateDirectory(reportDirectory);
        EnsureDirectoryIsNotReparsePoint(reportDirectory);

        var errors = new List<string>();
        var patternResults = new List<RageCliMetadataPatternResult>();
        var candidates = new Dictionary<string, CandidateBuilder>(StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in EcologyMetadataPatterns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _processRunner.RunAsync(
                verifiedTool.ExecutablePath,
                BuildSearchArguments(gameExecutable, gameRoot, pattern, MaximumHitsPerPattern),
                SearchTimeout,
                cancellationToken);

            var hasArchiveErrors = HasArchiveErrors(result.StandardError);
            if (result.ExitCode != 0)
                errors.Add("rage-cli search failed for pattern '" + pattern + "' with exit code " +
                           result.ExitCode + ": " + LimitForMessage(result.StandardError + " " + result.StandardOutput));
            if (result.StandardOutputTruncated || result.StandardErrorTruncated)
                errors.Add("rage-cli output was truncated for pattern '" + pattern + "'.");
            if (hasArchiveErrors)
                errors.Add("rage-cli reported one or more skipped or unreadable archives for pattern '" +
                           pattern + "'. See the report diagnostics.");

            IReadOnlyList<RageCliMetadataSearchHit> hits = Array.Empty<RageCliMetadataSearchHit>();
            if (result.ExitCode == 0 && !result.StandardOutputTruncated)
            {
                try
                {
                    hits = ParseSearchJson(result.StandardOutput);
                }
                catch (Exception exception) when (exception is JsonException or InvalidDataException)
                {
                    errors.Add("Could not parse search results for pattern '" + pattern + "': " +
                               LimitForMessage(exception.Message));
                }
            }

            var hitLimit = hits.Count >= MaximumHitsPerPattern;
            if (hitLimit)
                errors.Add("The " + MaximumHitsPerPattern + "-hit cap was reached for pattern '" +
                           pattern + "'. The result is incomplete.");

            patternResults.Add(new RageCliMetadataPatternResult(
                pattern,
                hits.Count,
                hitLimit,
                result.StandardOutputTruncated || result.StandardErrorTruncated,
                hasArchiveErrors,
                LimitForMessage(result.StandardError)));

            foreach (var hit in hits)
            {
                var key = Path.GetFullPath(hit.ArchivePath) + "\u001f" + hit.Path;
                if (!candidates.TryGetValue(key, out var candidate))
                {
                    candidate = new CandidateBuilder(hit);
                    candidates.Add(key, candidate);
                }
                candidate.Patterns.Add(pattern);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var sourceHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var archivePath = ValidateArchivePathWithinGameRoot(gameRoot, candidate.Hit.ArchivePath);
                if (!sourceHashes.TryGetValue(archivePath, out var archiveHash))
                {
                    archiveHash = await RageCliToolchainService.ComputeSha256Async(
                        archivePath, cancellationToken);
                    sourceHashes.Add(archivePath, archiveHash);
                }
                candidate.SourceArchiveSha256 = archiveHash;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                errors.Add("Could not fingerprint source archive for '" + candidate.Hit.Path +
                           "': " + LimitForMessage(exception.Message));
            }
        }

        var gameExecutableAfterSha256 = await RageCliToolchainService.ComputeSha256Async(
            gameExecutable, cancellationToken);
        if (!gameExecutableAfterSha256.Equals(gameExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException(
                "GTA5.exe changed during metadata discovery. The scan has been rejected.");

        var report = new MetadataDiscoveryReport(
            SchemaVersion: 1,
            Tool: "rage-cli",
            ToolVersion: RageCliToolchainService.PinnedVersion,
            ToolSha256: verifiedTool.Sha256,
            GameBuild: installation.BuildVersion,
            GameExecutableSha256: gameExecutableSha256,
            GameRoot: gameRoot,
            CreatedAt: DateTimeOffset.UtcNow,
            SearchLimitPerPattern: MaximumHitsPerPattern,
            Notice: "Discovery results only. This report is not a complete ecological baseline and does not approve packages.",
            PatternResults: patternResults,
            Errors: errors,
            Candidates: candidates.Values.Select(candidate => new RageCliMetadataHit(
                candidate.Hit.ArchivePath,
                candidate.Hit.Path,
                candidate.Hit.Name,
                candidate.Hit.Size,
                candidate.Hit.MemorySize,
                candidate.Hit.Kind,
                candidate.Hit.Hash,
                candidate.Hit.StemHash,
                candidate.Patterns.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                candidate.SourceArchiveSha256)).ToArray());

        var reportPath = Path.Combine(
            reportDirectory,
            "metadata-discovery-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") +
            "-" + Guid.NewGuid().ToString("N") + ".json");
        var tempPath = reportPath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                tempPath,
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
            var reportInfo = new FileInfo(tempPath);
            if (reportInfo.Length == 0 || reportInfo.Length > MaximumReportBytes)
                throw new InvalidDataException("The metadata discovery report is empty or exceeds 64 MB.");
            File.Move(tempPath, reportPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        var finalInfo = new FileInfo(reportPath);
        var reportSha256 = await RageCliToolchainService.ComputeSha256Async(
            reportPath, cancellationToken);
        timer.Stop();

        return new RageCliMetadataDiscoveryResult(
            reportPath,
            finalInfo.Length,
            reportSha256,
            gameExecutableSha256,
            candidates.Count,
            sourceHashes.Count,
            errors.Count == 0 && patternResults.All(item => !item.HitLimit && !item.OutputTruncated && !item.HasArchiveErrors),
            timer.Elapsed,
            patternResults,
            errors);
    }

    public static IReadOnlyList<string> BuildSearchArguments(
        string gameExecutablePath,
        string gameRoot,
        string pattern,
        int limit)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Contains('"') || pattern.Contains('\n') || pattern.Contains('\r'))
            throw new ArgumentException("A single safe filename pattern is required.", nameof(pattern));
        if (limit is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(limit), "Search limits must be between 1 and 10,000.");

        return new[]
        {
            "--no-update-check",
            "--exe", Path.GetFullPath(gameExecutablePath),
            "search",
            Path.GetFullPath(gameRoot),
            pattern,
            "--json",
            "--limit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    public static IReadOnlyList<RageCliMetadataSearchHit> ParseSearchJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("rage-cli JSON search output must be an array.");

        var hits = new List<RageCliMetadataSearchHit>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var archive = RequiredString(element, "archive");
            var path = RequiredString(element, "path");
            var name = RequiredString(element, "name");
            var kind = RequiredString(element, "kind");
            var hash = RequiredString(element, "hash");
            var stemHash = RequiredString(element, "stem_hash");
            var size = RequiredInt64(element, "size");
            var memorySize = RequiredInt64(element, "mem_size");

            if (!Path.IsPathRooted(archive) || string.IsNullOrWhiteSpace(path) ||
                Path.IsPathRooted(path) || path.Contains("..", StringComparison.Ordinal))
                throw new InvalidDataException("rage-cli returned an invalid archive or internal path.");

            hits.Add(new RageCliMetadataSearchHit(
                archive, path, name, size, memorySize, kind, hash, stemHash));
        }

        return hits;
    }

    private static string ValidateArchivePathWithinGameRoot(string gameRoot, string archivePath)
    {
        var fullPath = Path.GetFullPath(archivePath);
        var prefix = gameRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !fullPath.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullPath))
            throw new InvalidDataException("A metadata search result references an RPF outside the selected game folder.");

        var current = gameRoot;
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The selected game folder is a reparse point.");

        var relative = Path.GetRelativePath(gameRoot, fullPath);
        foreach (var segment in relative.Split(
                     Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("A metadata source archive path traverses a reparse point.");
        }

        return fullPath;
    }

    private static bool HasArchiveErrors(string standardError) =>
        standardError.Split('\n', StringSplitOptions.RemoveEmptyEntries).Any(line =>
            line.Contains("skipping ", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("failed to open", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("failed to read", StringComparison.OrdinalIgnoreCase));

    private static string RequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException("rage-cli search result has no valid '" + name + "' field.");

        return value.GetString()!;
    }

    private static long RequiredInt64(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            !value.TryGetInt64(out var result) ||
            result < 0)
            throw new InvalidDataException("rage-cli search result has no valid '" + name + "' field.");

        return result;
    }

    private static void EnsureDirectoryIsNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The metadata report directory cannot be a reparse point.");
    }

    private static string LimitForMessage(string value)
    {
        const int limit = 3000;
        var text = value.Trim();
        return text.Length <= limit ? text : text[..limit] + " ...";
    }

    private sealed class CandidateBuilder
    {
        public CandidateBuilder(RageCliMetadataSearchHit hit) => Hit = hit;
        public RageCliMetadataSearchHit Hit { get; }
        public HashSet<string> Patterns { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? SourceArchiveSha256 { get; set; }
    }

    private sealed record MetadataDiscoveryReport(
        int SchemaVersion,
        string Tool,
        string ToolVersion,
        string ToolSha256,
        string GameBuild,
        string GameExecutableSha256,
        string GameRoot,
        DateTimeOffset CreatedAt,
        int SearchLimitPerPattern,
        string Notice,
        IReadOnlyList<RageCliMetadataPatternResult> PatternResults,
        IReadOnlyList<string> Errors,
        IReadOnlyList<RageCliMetadataHit> Candidates);
}

public sealed record RageCliMetadataSearchHit(
    string ArchivePath,
    string Path,
    string Name,
    long Size,
    long MemorySize,
    string Kind,
    string Hash,
    string StemHash);
