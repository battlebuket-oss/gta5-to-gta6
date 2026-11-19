using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GtaTransformationHub.Services;

public enum AuditLogLevel
{
    Information,
    Warning,
    Error,
    Critical
}

public sealed record LocalAuditLogRecord(
    DateTimeOffset TimestampUtc,
    AuditLogLevel Level,
    string EventName,
    string Message,
    string? ExceptionType,
    string? ExceptionDetails);

/// <summary>
/// Local-only structured diagnostics. Logs are JSON Lines, user paths are redacted
/// before persistence, and support bundles include logs/system metadata only.
/// Game files, saves, package binaries, and environment secrets are never collected.
/// </summary>
public sealed class LocalAuditLogService
{
    private const long MaximumDailyLogBytes = 5L * 1024 * 1024;
    private const long MaximumBundleLogBytes = 20L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Match Windows drive paths, UNC paths, and common Unix absolute-path roots.
    // This is evaluated before any environment-specific path substitutions.
    private static readonly Regex AbsolutePathPattern = new(
        @"(?i)(?<![A-Za-z0-9])(?:[A-Z]:\\|\\\\|/(?:Users|home|tmp|private|var|mnt|root|opt)/)[^<>""'\r\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _logDirectory;
    private readonly object _writeLock = new();

    public LocalAuditLogService(string? logDirectory = null)
    {
        _logDirectory = Path.GetFullPath(logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "logs"));
    }

    public string LogDirectory => _logDirectory;

    public void Write(
        AuditLogLevel level,
        string eventName,
        string message,
        Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            var record = new LocalAuditLogRecord(
                DateTimeOffset.UtcNow,
                level,
                Limit(Redact(eventName), 160),
                Limit(Redact(message), 8000),
                exception?.GetType().FullName,
                exception is null ? null : Limit(Redact(exception.ToString()), 20000));
            var json = JsonSerializer.Serialize(record, JsonOptions);

            lock (_writeLock)
            {
                var path = GetWritableLogPath(DateTimeOffset.UtcNow);
                File.AppendAllText(path, json + Environment.NewLine, new UTF8Encoding(false));
            }
        }
        catch
        {
            // Diagnostics must never cause an application operation to fail.
        }
    }

    public SupportBundleResult ExportSupportBundle(string destinationPath, int retentionDays = 14)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("A destination path is required.", nameof(destinationPath));
        if (retentionDays is < 1 or > 90)
            throw new ArgumentOutOfRangeException(nameof(retentionDays), "Retention must be between 1 and 90 days.");

        var fullDestination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        var included = new List<(string Path, long Size, string Sha256)>();
        long totalBytes = 0;

        var files = Directory.Exists(_logDirectory)
            ? Directory.GetFiles(_logDirectory, "*.jsonl", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Where(info => info.LastWriteTimeUtc >= cutoff.UtcDateTime && info.Length <= MaximumDailyLogBytes)
                .OrderByDescending(info => info.LastWriteTimeUtc)
            : Enumerable.Empty<FileInfo>();

        foreach (var file in files)
        {
            if (totalBytes + file.Length > MaximumBundleLogBytes)
                continue;
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            included.Add((file.FullName, file.Length, hash));
            totalBytes += file.Length;
        }

        using (var output = new FileStream(fullDestination, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
        {
            foreach (var item in included)
            {
                var entry = archive.CreateEntry("logs/" + Path.GetFileName(item.Path), CompressionLevel.Optimal);
                using var target = entry.Open();
                using var source = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                source.CopyTo(target);
            }

            var diagnostics = new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                applicationVersion = typeof(LocalAuditLogService).Assembly.GetName().Version?.ToString() ?? "unknown",
                operatingSystem = RuntimeInformation.OSDescription,
                osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                framework = RuntimeInformation.FrameworkDescription,
                logRetentionDays = retentionDays,
                includedLogFiles = included.Count,
                includedLogBytes = totalBytes
            };
            var diagnosticsEntry = archive.CreateEntry("diagnostics.json", CompressionLevel.Optimal);
            using (var target = diagnosticsEntry.Open())
                JsonSerializer.Serialize(target, diagnostics, JsonOptions);

            var manifest = included.Select(item => new
            {
                file = "logs/" + Path.GetFileName(item.Path),
                sizeBytes = item.Size,
                sha256 = item.Sha256
            }).ToArray();
            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using (var target = manifestEntry.Open())
                JsonSerializer.Serialize(target, manifest, JsonOptions);
        }

        var bundleInfo = new FileInfo(fullDestination);
        return new SupportBundleResult(fullDestination, included.Count, totalBytes, bundleInfo.Length);
    }

    private string GetWritableLogPath(DateTimeOffset timestamp)
    {
        var stem = "application-" + timestamp.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var candidate = Path.Combine(_logDirectory, stem + ".jsonl");
        if (!File.Exists(candidate) || new FileInfo(candidate).Length < MaximumDailyLogBytes)
            return candidate;

        for (var index = 1; index <= 20; index++)
        {
            candidate = Path.Combine(_logDirectory, stem + "." + index + ".jsonl");
            if (!File.Exists(candidate) || new FileInfo(candidate).Length < MaximumDailyLogBytes)
                return candidate;
        }

        return Path.Combine(_logDirectory, stem + ".overflow.jsonl");
    }

    private static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // Redact the full path first. Replacing the profile prefix first would turn
        // "C:\Users\name\..." into a token that no longer matches an absolute path.
        var redacted = AbsolutePathPattern.Replace(value, "[PATH REDACTED]");

        // Keep a final platform-specific fallback for uncommon profile-root formats.
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(userProfile))
            redacted = redacted.Replace(userProfile, "[PATH REDACTED]", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(localAppData))
            redacted = redacted.Replace(localAppData, "[PATH REDACTED]", StringComparison.OrdinalIgnoreCase);

        return redacted;
    }

    private static string Limit(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength] + " [TRUNCATED]";
}

public sealed record SupportBundleResult(
    string ArchivePath,
    int IncludedLogFiles,
    long IncludedLogBytes,
    long ArchiveBytes);
