using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GtaTransformationHub.Services;

/// <summary>
/// Reads CodeWalker's own OIV backup/install records from a GTA V installation.
/// The records are third-party installer metadata, not proof that a mod is still
/// active, that its files remain unchanged, or that the game works in-game.
/// This service never modifies the game folder.
/// </summary>
public sealed class CodeWalkerInstallHistoryService
{
    private const string BackupFolderName = "OIV_CW_Uninstall_Data";
    private const int MaximumPackageDirectories = 500;
    private const long MaximumInstallLogBytes = 4 * 1024 * 1024;
    private const int MaximumEntriesPerPackage = 100_000;

    public async Task<CodeWalkerInstallHistoryReport> ReadAsync(
        string gameDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);

        var fullGameDirectory = Path.GetFullPath(gameDirectory);
        if (!Directory.Exists(fullGameDirectory))
            throw new DirectoryNotFoundException("The selected GTA V Legacy game directory does not exist.");

        if ((File.GetAttributes(fullGameDirectory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The selected game directory is a reparse point.");

        var backupRoot = Path.Combine(fullGameDirectory, BackupFolderName);
        if (!Directory.Exists(backupRoot))
            return new CodeWalkerInstallHistoryReport(
                backupRoot,
                false,
                Array.Empty<CodeWalkerInstallHistoryEntry>(),
                Array.Empty<string>());

        if ((File.GetAttributes(backupRoot) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("CodeWalker backup history directory is a reparse point.");

        var warnings = new List<string>();
        var packageDirectories = Directory.EnumerateDirectories(backupRoot)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumPackageDirectories + 1)
            .ToArray();

        if (packageDirectories.Length > MaximumPackageDirectories)
        {
            warnings.Add(
                "The CodeWalker backup directory contains more than " +
                MaximumPackageDirectories + " package folders. Only the first " +
                MaximumPackageDirectories + " folders were inspected.");
        }

        var results = new List<CodeWalkerInstallHistoryEntry>();
        foreach (var packageDirectory in packageDirectories.Take(MaximumPackageDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                EnsureContainedDirectory(backupRoot, packageDirectory);
                if ((File.GetAttributes(packageDirectory) & FileAttributes.ReparsePoint) != 0)
                {
                    warnings.Add("Skipped a reparse-point package directory: " + Path.GetFileName(packageDirectory));
                    continue;
                }

                var logPath = Path.Combine(packageDirectory, "install.log");
                if (!File.Exists(logPath))
                    continue;

                if ((File.GetAttributes(logPath) & FileAttributes.ReparsePoint) != 0)
                {
                    warnings.Add("Skipped a reparse-point install.log: " + Path.GetFileName(packageDirectory));
                    continue;
                }

                var logInfo = new FileInfo(logPath);
                if (logInfo.Length <= 0 || logInfo.Length > MaximumInstallLogBytes)
                {
                    warnings.Add("Skipped an empty or oversized install.log: " + Path.GetFileName(packageDirectory));
                    continue;
                }

                await using var stream = new FileStream(
                    logPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    65536,
                    useAsync: true);

                using var document = await JsonDocument.ParseAsync(
                    stream,
                    new JsonDocumentOptions { MaxDepth = 32 },
                    cancellationToken);

                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    warnings.Add("Skipped an install.log that is not a JSON object: " + Path.GetFileName(packageDirectory));
                    continue;
                }

                var packageName = ReadString(root, "PackageName");
                if (string.IsNullOrWhiteSpace(packageName) || packageName.Length > 200)
                {
                    warnings.Add("Skipped an install.log with an invalid package name: " + Path.GetFileName(packageDirectory));
                    continue;
                }

                var description = ReadString(root, "Description") ?? string.Empty;
                if (description.Length > 2000)
                    description = description[..2000];

                var version = ReadString(root, "Version") ?? string.Empty;
                if (version.Length > 120)
                    version = version[..120];

                var isEnhanced = ReadBoolean(root, "IsGen9");
                var installedAt = ReadDate(root, "InstallDate");
                if (installedAt is null)
                    warnings.Add("Install date is missing or invalid for package record: " + packageName);

                var entryCount = ReadEntryCount(root, warnings, packageName);
                results.Add(new CodeWalkerInstallHistoryEntry(
                    packageName,
                    description,
                    version,
                    isEnhanced,
                    installedAt,
                    entryCount,
                    packageDirectory,
                    logPath));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException or
                InvalidOperationException or ArgumentException or NotSupportedException)
            {
                warnings.Add(
                    "Could not read CodeWalker package record '" +
                    Path.GetFileName(packageDirectory) + "': " + exception.Message);
            }
        }

        var ordered = results
            .OrderByDescending(entry => entry.InstalledAt ?? DateTimeOffset.MinValue)
            .ThenBy(entry => entry.PackageName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CodeWalkerInstallHistoryReport(
            backupRoot,
            true,
            ordered,
            warnings);
    }

    private static void EnsureContainedDirectory(string root, string candidate)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullCandidate = Path.GetFullPath(candidate);
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        if (!fullCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A CodeWalker package folder escapes the backup directory.");
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
            return null;

        return property.GetString()?.Trim();
    }

    private static bool ReadBoolean(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) &&
               property.ValueKind is JsonValueKind.True;
    }

    private static DateTimeOffset? ReadDate(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(
                property.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var date))
            return date;

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt64(out var ticks))
        {
            try
            {
                return new DateTimeOffset(new DateTime(ticks, DateTimeKind.Local));
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return null;
    }

    private static int ReadEntryCount(
        JsonElement root,
        List<string> warnings,
        string packageName)
    {
        if (!root.TryGetProperty("Entries", out var entries) ||
            entries.ValueKind != JsonValueKind.Array)
        {
            warnings.Add("File-operation count is missing for package record: " + packageName);
            return 0;
        }

        if (entries.GetArrayLength() > MaximumEntriesPerPackage)
        {
            warnings.Add("File-operation count exceeds the safety limit for package record: " + packageName);
            return MaximumEntriesPerPackage;
        }

        return entries.GetArrayLength();
    }
}

public sealed record CodeWalkerInstallHistoryEntry(
    string PackageName,
    string Description,
    string Version,
    bool IsEnhanced,
    DateTimeOffset? InstalledAt,
    int FileOperationCount,
    string RecordDirectory,
    string InstallLogPath)
{
    public string GameEdition => IsEnhanced ? "Enhanced" : "Legacy";
}

public sealed record CodeWalkerInstallHistoryReport(
    string BackupDirectory,
    bool BackupDirectoryExists,
    IReadOnlyList<CodeWalkerInstallHistoryEntry> Packages,
    IReadOnlyList<string> Warnings);
