using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GtaTransformationHub.Services;

public sealed record RageCliToolchainInfo(
    string ExecutablePath,
    string Version,
    string Sha256);

public sealed record RageCliCatalogueBuildResult(
    string DatabasePath,
    string ReportPath,
    long DatabaseBytes,
    long ReportBytes,
    string DatabaseSha256,
    string ReportSha256,
    string GameExecutableSha256,
    TimeSpan Duration,
    string Summary);

public sealed record RageCliProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated,
    TimeSpan Duration);

public interface IRageCliProcessRunner
{
    Task<RageCliProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs a verified, user-selected copy of rage-cli. No binary is downloaded or
/// bundled. The exact release asset digest and version are pinned; every operation
/// re-verifies the executable before starting it. All arguments are passed without
/// a shell, and stdout/stderr are bounded.
/// </summary>
public sealed class RageCliToolchainService
{
    public const string PinnedVersion = "0.28.1";
    public const string PinnedSha256 =
        "B7A9AA1D6196632544889CDC97481DB001936DADC8E046784EADEB38ACD3B1AC";
    public const string ReleaseUrl = "https://github.com/VIRUXE/rage-cli/releases/tag/v0.28.1";
    public const string WindowsAssetUrl =
        "https://github.com/VIRUXE/rage-cli/releases/download/v0.28.1/rage-windows-x86_64.exe";

    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CatalogueTimeout = TimeSpan.FromMinutes(45);
    private const long MaximumExecutableBytes = 64L * 1024 * 1024;
    private const long MaximumReportBytes = 256L * 1024 * 1024;

    private readonly string _dataRoot;
    private readonly string _settingsPath;
    private readonly IRageCliProcessRunner _processRunner;

    public RageCliToolchainService(
        string? dataRoot = null,
        IRageCliProcessRunner? processRunner = null)
    {
        _dataRoot = Path.GetFullPath(dataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "rage-cli"));
        _settingsPath = Path.Combine(_dataRoot, "toolchain.json");
        _processRunner = processRunner ?? new RageCliProcessRunner();
    }

    public async Task<string?> LoadConfiguredExecutablePathAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
            return null;
        if ((File.GetAttributes(_settingsPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The saved rage-cli settings file is a reparse point.");

        try
        {
            var json = await File.ReadAllTextAsync(_settingsPath, cancellationToken);
            var settings = JsonSerializer.Deserialize<ToolchainSettings>(json);
            return string.IsNullOrWhiteSpace(settings?.ExecutablePath)
                ? null
                : settings.ExecutablePath;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The saved rage-cli toolchain settings are invalid. Select the executable again to replace them.",
                exception);
        }
    }

    public async Task SaveConfiguredExecutablePathAsync(
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(executablePath);
        Directory.CreateDirectory(_dataRoot);
        EnsureDirectoryIsNotReparsePoint(_dataRoot);
        if (File.Exists(_settingsPath) &&
            (File.GetAttributes(_settingsPath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The saved rage-cli settings file cannot be a reparse point.");

        var temporaryPath = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(new ToolchainSettings(fullPath), new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public async Task<RageCliToolchainInfo> ValidateExecutableAsync(
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = ValidateExecutablePath(executablePath);
        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length < 1024 * 1024 || fileInfo.Length > MaximumExecutableBytes)
            throw new InvalidDataException(
                "The selected rage-cli executable has an unexpected file size.");

        var actualHash = await ComputeSha256Async(fullPath, cancellationToken);
        if (!actualHash.Equals(PinnedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "The selected executable does not match the pinned rage-cli v" + PinnedVersion +
                " Windows x64 release checksum. It will not be launched.");

        var result = await _processRunner.RunAsync(
            fullPath,
            new[] { "--version" },
            VersionTimeout,
            cancellationToken);

        var versionLine = result.StandardOutput.Trim();
        if (result.ExitCode != 0 ||
            result.StandardOutputTruncated ||
            !versionLine.Equals("rage " + PinnedVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The pinned rage-cli executable did not report the expected version. " +
                "Expected 'rage " + PinnedVersion + "'. Output: " +
                LimitForMessage(result.StandardOutput + " " + result.StandardError));
        }

        return new RageCliToolchainInfo(fullPath, PinnedVersion, actualHash);
    }

    public async Task<RageCliCatalogueBuildResult> BuildAssetCatalogueAsync(
        string rageExecutablePath,
        GameInstallation installation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var verified = await ValidateExecutableAsync(rageExecutablePath, cancellationToken);

        var executable = Path.GetFullPath(installation.ExecutablePath);
        var gameRoot = Path.GetFullPath(installation.RootPath);
        if (!File.Exists(executable) ||
            !Path.GetFileName(executable).Equals("GTA5.exe", StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(gameRoot) ||
            !Path.GetDirectoryName(executable)!.Equals(
                Path.TrimEndingDirectorySeparator(gameRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The selected GTA V executable and installation root do not match.");
        }

        Directory.CreateDirectory(_dataRoot);
        EnsureDirectoryIsNotReparsePoint(_dataRoot);

        // Keep catalogues isolated by the exact GTA5.exe bytes. A scan from a
        // different game build must never silently reuse or overwrite this build's DB.
        var gameExecutableSha256 = await ComputeSha256Async(executable, cancellationToken);
        var buildDirectoryName = BuildDirectoryNameFromExecutableHash(gameExecutableSha256);
        var catalogueRoot = Path.Combine(_dataRoot, "catalogue");
        Directory.CreateDirectory(catalogueRoot);
        EnsureDirectoryIsNotReparsePoint(catalogueRoot);

        var databaseDirectory = Path.Combine(catalogueRoot, buildDirectoryName);
        Directory.CreateDirectory(databaseDirectory);
        EnsureDirectoryIsNotReparsePoint(databaseDirectory);

        var databasePath = Path.Combine(databaseDirectory, "catalog.sqlite");
        if (File.Exists(databasePath) &&
            (File.GetAttributes(databasePath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The rage-cli database path cannot be a reparse point.");

        var reportPath = Path.Combine(
            databaseDirectory,
            "catalog-report-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" +
            Guid.NewGuid().ToString("N") + ".json");

        var arguments = BuildCatalogueArguments(executable, gameRoot, databasePath, reportPath);
        var result = await _processRunner.RunAsync(
            verified.ExecutablePath,
            arguments,
            CatalogueTimeout,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new RageCliExecutionException(
                "rage-cli could not build a strict asset catalogue. A failed scan must be reviewed before any ecology package is approved.",
                result.ExitCode,
                LimitForMessage(result.StandardOutput),
                LimitForMessage(result.StandardError),
                File.Exists(reportPath) ? reportPath : null);
        }

        if (result.StandardOutputTruncated || result.StandardErrorTruncated)
            throw new InvalidDataException(
                "rage-cli produced more output than the Hub permits. The scan result cannot be accepted.");

        EnsureRegularFile(databasePath, "asset catalogue database");
        EnsureRegularFile(reportPath, "asset catalogue report");

        var databaseInfo = new FileInfo(databasePath);
        var reportInfo = new FileInfo(reportPath);
        if (databaseInfo.Length == 0 || reportInfo.Length == 0)
            throw new InvalidDataException("rage-cli created an empty asset catalogue or report.");
        if (reportInfo.Length > MaximumReportBytes)
            throw new InvalidDataException("The rage-cli report exceeded the 256 MB safety limit.");

        var databaseHash = await ComputeSha256Async(databasePath, cancellationToken);
        var reportHash = await ComputeSha256Async(reportPath, cancellationToken);

        return new RageCliCatalogueBuildResult(
            databasePath,
            reportPath,
            databaseInfo.Length,
            reportInfo.Length,
            databaseHash,
            reportHash,
            gameExecutableSha256,
            result.Duration,
            result.StandardOutput.Trim());
    }

    public static string BuildDirectoryNameFromExecutableHash(string executableSha256)
    {
        if (executableSha256 is null || executableSha256.Length != 64 ||
            executableSha256.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A 64-character SHA-256 digest is required.", nameof(executableSha256));

        return executableSha256.ToLowerInvariant();
    }

    public static IReadOnlyList<string> BuildCatalogueArguments(
        string gameExecutablePath,
        string gameRoot,
        string databasePath,
        string reportPath) =>
        new[]
        {
            "--no-update-check",
            "--exe", Path.GetFullPath(gameExecutablePath),
            "catalog",
            "--db", Path.GetFullPath(databasePath),
            "build",
            "--game", Path.GetFullPath(gameRoot),
            "--full",
            "--strict",
            "--report", Path.GetFullPath(reportPath)
        };

    public static string ValidateExecutablePath(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("Select the rage-cli Windows executable.", nameof(executablePath));

        var fullPath = Path.GetFullPath(executablePath);
        if (!Path.GetFileName(fullPath).Equals("rage-windows-x86_64.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Select the official rage-windows-x86_64.exe release asset.");
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The selected rage-cli executable was not found.", fullPath);
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("A reparse-point executable is not accepted.");

        return fullPath;
    }

    public static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static void EnsureRegularFile(string path, string description)
    {
        if (!File.Exists(path))
            throw new InvalidDataException("rage-cli did not create the expected " + description + ".");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The generated " + description + " is a reparse point.");
    }

    private static void EnsureDirectoryIsNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The rage-cli data directory cannot be a symbolic link or reparse point.");
    }

    private static string LimitForMessage(string value)
    {
        const int limit = 4000;
        var text = value.Trim();
        return text.Length <= limit ? text : text[..limit] + " ...";
    }

    private sealed record ToolchainSettings(string ExecutablePath);
}

public sealed class RageCliExecutionException : Exception
{
    public RageCliExecutionException(
        string message,
        int exitCode,
        string standardOutput,
        string standardError,
        string? reportPath)
        : base(message + " Exit code: " + exitCode + ". " +
               (string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError))
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
        ReportPath = reportPath;
    }

    public int ExitCode { get; }
    public string StandardOutput { get; }
    public string StandardError { get; }
    public string? ReportPath { get; }
}

public sealed class RageCliProcessRunner : IRageCliProcessRunner
{
    private const int MaximumCapturedCharacters = 16 * 1024 * 1024;

    public async Task<RageCliProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(executablePath),
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath))!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // Explicit command arguments win; inherited environment overrides are removed.
        startInfo.Environment.Remove("GTAV_PATH");
        startInfo.Environment.Remove("RAGE_CATALOG");
        startInfo.Environment.Remove("RAGE_NO_UPDATE_CHECK");
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        if (!process.Start())
            throw new InvalidOperationException("Windows could not start the verified rage-cli executable.");

        var stdoutTask = ReadBoundedAsync(process.StandardOutput, MaximumCapturedCharacters);
        var stderrTask = ReadBoundedAsync(process.StandardError, MaximumCapturedCharacters);

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linkedSource.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdoutTask, stderrTask);
            if (cancellationToken.IsCancellationRequested)
                throw;
            throw new TimeoutException(
                "rage-cli exceeded its allowed runtime of " + timeout.TotalMinutes.ToString("0") + " minute(s).");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        stopwatch.Stop();

        return new RageCliProcessResult(
            process.ExitCode,
            stdout.Text,
            stderr.Text,
            stdout.Truncated,
            stderr.Truncated,
            stopwatch.Elapsed);
    }

    private static async Task<(string Text, bool Truncated)> ReadBoundedAsync(
        StreamReader reader,
        int maximumCharacters)
    {
        var builder = new StringBuilder(Math.Min(maximumCharacters, 16 * 1024));
        var buffer = new char[8192];
        var truncated = false;

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory());
            if (read == 0)
                break;

            var remaining = maximumCharacters - builder.Length;
            if (remaining > 0)
                builder.Append(buffer, 0, Math.Min(remaining, read));
            if (read > remaining)
                truncated = true;
        }

        return (builder.ToString(), truncated);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Preserve the original timeout/cancellation exception.
        }
    }
}
