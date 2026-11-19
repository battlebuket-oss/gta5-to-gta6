using System.Text.Json;

namespace GtaTransformationHub.Services;

public sealed record RageCliNamesHarvestResult(
    string NamesFilePath,
    string ManifestPath,
    string NamesFileSha256,
    string GameExecutableSha256,
    string GameBuild,
    int NameCount,
    bool Complete,
    TimeSpan Duration,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Harvests the target GTA V Legacy build's own JOAAT name dictionary using the
/// checksum-pinned rage-cli. This enables later metadata dumps to resolve hashed
/// archetype, model and metadata member names. A local manifest pins the list to
/// the exact GTA5.exe bytes and tool release; a harvested list is never treated as
/// a species inventory or ecological approval.
/// </summary>
public sealed class RageCliNamesHarvestService
{
    public const long MaximumNamesFileBytes = 256L * 1024 * 1024;
    private static readonly TimeSpan HarvestTimeout = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly RageCliToolchainService _toolchain;
    private readonly IRageCliProcessRunner _processRunner;
    private readonly string _dataRoot;

    private sealed record NamesManifest(
        int SchemaVersion,
        string ToolVersion,
        string ToolSha256,
        string GameBuild,
        string GameExecutableSha256,
        string NamesFileSha256,
        int NameCount,
        DateTimeOffset CreatedAtUtc,
        bool Complete,
        IReadOnlyList<string> Warnings);

    public RageCliNamesHarvestService(
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

    public async Task<RageCliNamesHarvestResult> HarvestAsync(
        string rageExecutablePath,
        GameInstallation installation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var tool = await _toolchain.ValidateExecutableAsync(rageExecutablePath, cancellationToken);
        var (gameExecutable, gameRoot) = ValidateInstallation(installation);
        var gameExecutableSha256 = await RageCliToolchainService.ComputeSha256Async(
            gameExecutable, cancellationToken);

        var namesRoot = Path.Combine(_dataRoot, "names");
        Directory.CreateDirectory(namesRoot);
        EnsureDirectoryIsNotReparsePoint(_dataRoot);
        EnsureDirectoryIsNotReparsePoint(namesRoot);

        var buildDirectory = RageCliToolchainService.BuildDirectoryNameFromExecutableHash(gameExecutableSha256);
        var outputDirectory = Path.Combine(namesRoot, buildDirectory);
        Directory.CreateDirectory(outputDirectory);
        EnsureDirectoryIsNotReparsePoint(outputDirectory);

        var namesPath = Path.Combine(outputDirectory, "names.txt");
        var manifestPath = Path.Combine(outputDirectory, "names-manifest.json");
        EnsureFileIsNotReparsePointIfPresent(namesPath);
        EnsureFileIsNotReparsePointIfPresent(manifestPath);

        var temporaryNamesPath = Path.Combine(outputDirectory, "names-" + Guid.NewGuid().ToString("N") + ".tmp");
        var warnings = new List<string>();
        try
        {
            var result = await _processRunner.RunAsync(
                tool.ExecutablePath,
                BuildHarvestArguments(gameExecutable, temporaryNamesPath),
                HarvestTimeout,
                cancellationToken);

            if (result.ExitCode != 0)
                throw new RageCliExecutionException(
                    "rage-cli could not harvest the selected GTA V build's name list.",
                    result.ExitCode,
                    LimitForMessage(result.StandardOutput),
                    LimitForMessage(result.StandardError),
                    File.Exists(temporaryNamesPath) ? temporaryNamesPath : null);

            if (result.StandardOutputTruncated || result.StandardErrorTruncated)
                throw new InvalidDataException("rage-cli output was truncated. The names harvest was not accepted.");

            if (!string.IsNullOrWhiteSpace(result.StandardError))
                warnings.Add(LimitForMessage(result.StandardError));

            EnsureRegularFile(temporaryNamesPath, "harvested names list");
            var namesInfo = new FileInfo(temporaryNamesPath);
            if (namesInfo.Length <= 0 || namesInfo.Length > MaximumNamesFileBytes)
                throw new InvalidDataException("The harvested names list is empty or exceeds the 256 MB limit.");

            var (nameCount, buildCoverage) = await ReadNamesFileSummaryAsync(
                temporaryNamesPath, cancellationToken);
            var expectedBuild = ParseBuildNumber(installation.BuildVersion);
            if (nameCount == 0)
                throw new InvalidDataException("rage-cli did not harvest any usable names.");
            if (buildCoverage is not null && buildCoverage != expectedBuild)
                throw new InvalidDataException(
                    "The harvested name list declares build " + buildCoverage +
                    " but the selected GTA V executable is build " + expectedBuild + ".");

            var gameExecutableAfterSha256 = await RageCliToolchainService.ComputeSha256Async(
                gameExecutable, cancellationToken);
            if (!gameExecutableAfterSha256.Equals(gameExecutableSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("GTA5.exe changed during name harvesting. The output was rejected.");

            var namesHash = await RageCliToolchainService.ComputeSha256Async(
                temporaryNamesPath, cancellationToken);
            File.Move(temporaryNamesPath, namesPath, overwrite: true);

            var manifest = new NamesManifest(
                1,
                tool.Version,
                tool.Sha256,
                installation.BuildVersion,
                gameExecutableSha256,
                namesHash,
                nameCount,
                DateTimeOffset.UtcNow,
                Complete: warnings.Count == 0,
                warnings.ToArray());

            var temporaryManifestPath = manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = new FileStream(
                    temporaryManifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporaryManifestPath, manifestPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryManifestPath))
                    File.Delete(temporaryManifestPath);
            }

            timer.Stop();
            return new RageCliNamesHarvestResult(
                namesPath,
                manifestPath,
                namesHash,
                gameExecutableSha256,
                installation.BuildVersion,
                nameCount,
                manifest.Complete,
                timer.Elapsed,
                warnings);
        }
        finally
        {
            if (File.Exists(temporaryNamesPath))
                File.Delete(temporaryNamesPath);
        }
    }

    /// <summary>
    /// Returns a local names list only after rechecking its sidecar, file hash,
    /// current GTA5.exe hash/build and pinned rage-cli version/digest.
    /// </summary>
    public async Task<string?> GetVerifiedNamesFilePathAsync(
        string rageExecutablePath,
        GameInstallation installation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var tool = await _toolchain.ValidateExecutableAsync(rageExecutablePath, cancellationToken);
        var (gameExecutable, _) = ValidateInstallation(installation);
        var gameHash = await RageCliToolchainService.ComputeSha256Async(gameExecutable, cancellationToken);
        var directory = Path.Combine(
            _dataRoot,
            "names",
            RageCliToolchainService.BuildDirectoryNameFromExecutableHash(gameHash));
        var namesPath = Path.Combine(directory, "names.txt");
        var manifestPath = Path.Combine(directory, "names-manifest.json");

        if (!File.Exists(namesPath) || !File.Exists(manifestPath))
            return null;
        EnsureDirectoryPathIsNotReparsePoint(_dataRoot, directory);
        EnsureFileIsNotReparsePointIfPresent(namesPath);
        EnsureFileIsNotReparsePointIfPresent(manifestPath);

        var manifestInfo = new FileInfo(manifestPath);
        if (manifestInfo.Length <= 0 || manifestInfo.Length > 1024 * 1024)
            throw new InvalidDataException("The saved names manifest is empty or exceeds 1 MB.");

        await using var stream = new FileStream(
            manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var manifest = await JsonSerializer.DeserializeAsync<NamesManifest>(
            stream, JsonOptions, cancellationToken);
        if (manifest is null || manifest.SchemaVersion != 1 ||
            !manifest.GameBuild.Equals(installation.BuildVersion, StringComparison.OrdinalIgnoreCase) ||
            !manifest.GameExecutableSha256.Equals(gameHash, StringComparison.OrdinalIgnoreCase) ||
            !manifest.ToolVersion.Equals(tool.Version, StringComparison.OrdinalIgnoreCase) ||
            !manifest.ToolSha256.Equals(tool.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !IsSha256(manifest.NamesFileSha256) || manifest.NameCount <= 0)
            throw new InvalidDataException(
                "The saved game name list is not bound to this GTA5.exe build and the pinned rage-cli version.");

        var namesInfo = new FileInfo(namesPath);
        if (namesInfo.Length <= 0 || namesInfo.Length > MaximumNamesFileBytes)
            throw new InvalidDataException("The saved names file is empty or exceeds the 256 MB limit.");

        var actualNamesHash = await RageCliToolchainService.ComputeSha256Async(namesPath, cancellationToken);
        if (!actualNamesHash.Equals(manifest.NamesFileSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The saved names file changed after harvesting. Harvest it again.");

        var (nameCount, buildCoverage) = await ReadNamesFileSummaryAsync(namesPath, cancellationToken);
        if (nameCount != manifest.NameCount || buildCoverage is not null &&
            buildCoverage != ParseBuildNumber(installation.BuildVersion))
            throw new InvalidDataException("The saved names list does not match its verified manifest.");

        return namesPath;
    }

    public static IReadOnlyList<string> BuildHarvestArguments(
        string gameExecutablePath,
        string outputNamesFilePath) =>
        new[]
        {
            "--no-update-check",
            "--exe", Path.GetFullPath(gameExecutablePath),
            "names",
            "harvest",
            "--output", Path.GetFullPath(outputNamesFilePath)
        };

    public static int ParseBuildNumber(string buildVersion)
    {
        if (!Version.TryParse(buildVersion, out var version) || version.Build < 0)
            throw new InvalidDataException("The GTA V Legacy build version is not numeric.");
        return version.Build;
    }

    public static async Task<(int NameCount, int? BuildCoverage)> ReadNamesFileSummaryAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0 || info.Length > MaximumNamesFileBytes)
            throw new InvalidDataException("The names list is missing, empty or exceeds 256 MB.");

        var count = 0;
        int? build = null;
        using var reader = new StreamReader(new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan));

        var header = true;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trimmed = line.Trim().TrimStart('\uFEFF');
            if (trimmed.Length == 0)
                continue;
            if (header && trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                const string buildPrefix = "# build:";
                if (trimmed.StartsWith(buildPrefix, StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(trimmed[buildPrefix.Length..].Trim(), out var parsedBuild))
                    build = parsedBuild;
                continue;
            }

            header = false;
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
                continue;
            if (trimmed.Length > 256 ||
                trimmed.Any(character => !(char.IsAsciiLetterOrDigit(character) ||
                                           character is '_' or '+' or '-' or '.' or '@')))
                throw new InvalidDataException("The names list contains an invalid identifier.");
            count = checked(count + 1);
            if (count > 10_000_000)
                throw new InvalidDataException("The names list exceeds the 10-million-entry safety limit.");
        }

        return (count, build);
    }

    private static (string Executable, string Root) ValidateInstallation(GameInstallation installation)
    {
        var executable = Path.GetFullPath(installation.ExecutablePath);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation.RootPath));
        if (!File.Exists(executable) ||
            !Path.GetFileName(executable).Equals("GTA5.exe", StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(root) ||
            !Path.GetDirectoryName(executable)!.Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected GTA5.exe and game installation root do not match.");
        return (executable, root);
    }

    private static void EnsureRegularFile(string path, string description)
    {
        if (!File.Exists(path))
            throw new InvalidDataException("rage-cli did not create the expected " + description + ".");
        EnsureFileIsNotReparsePointIfPresent(path);
    }

    private static void EnsureFileIsNotReparsePointIfPresent(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The local names data contains a reparse-point file.");
    }

    private static void EnsureDirectoryIsNotReparsePoint(string path)
    {
        if (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A names data directory is missing or redirected.");
    }

    private static void EnsureDirectoryPathIsNotReparsePoint(string root, string leaf)
    {
        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var leafPath = Path.GetFullPath(leaf);
        var prefix = rootPath + Path.DirectorySeparatorChar;
        if (!leafPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The names data directory escaped the Hub's local data root.");

        EnsureDirectoryIsNotReparsePoint(rootPath);
        var relative = Path.GetRelativePath(rootPath, leafPath);
        var current = rootPath;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            EnsureDirectoryIsNotReparsePoint(current);
        }
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string LimitForMessage(string value)
    {
        const int limit = 3000;
        var text = value.Trim();
        return text.Length <= limit ? text : text[..limit] + " ...";
    }
}
