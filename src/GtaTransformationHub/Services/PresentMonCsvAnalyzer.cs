using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GtaTransformationHub.Services;

/// <summary>
/// Analyses PresentMon v1/v2 CSV captures for GTA5.exe. It does not launch the game,
/// install PresentMon, or make performance guarantees from an unrecorded benchmark.
/// </summary>
public sealed class PresentMonCsvAnalyzer
{
    private const long MaximumCsvBytes = 1024L * 1024 * 1024;
    private const int MaximumFrames = 5_000_000;
    private static readonly string[] FrameTimeColumns =
        { "MsBetweenPresents", "FrameTime", "msBetweenPresents" };
    private static readonly string[] CpuBusyColumns = { "MsCPUBusy", "CPUBusy" };
    private static readonly string[] GpuBusyColumns = { "MsGPUBusy", "GPUBusy" };

    public async Task<PerformanceBenchmarkReport> AnalyzeAsync(
        string csvPath,
        PerformanceBenchmarkMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csvPath);
        ArgumentNullException.ThrowIfNull(metadata);

        var fullPath = Path.GetFullPath(csvPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The PresentMon CSV file was not found.", fullPath);

        var info = new FileInfo(fullPath);
        if (info.Length <= 0 || info.Length > MaximumCsvBytes)
            throw new InvalidDataException("The PresentMon CSV file is empty or exceeds the 1 GiB analysis limit.");

        using var hashStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        var sourceSha256 = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, cancellationToken));

        var frameTimes = new List<double>();
        var cpuBusy = new List<double>();
        var gpuBusy = new List<double>();
        long droppedFrames = 0;
        long matchingProcessRows = 0;
        var hasApplicationColumn = false;
        var frameColumnIndex = -1;
        var applicationColumnIndex = -1;
        var droppedColumnIndex = -1;
        var cpuBusyColumnIndex = -1;
        var gpuBusyColumnIndex = -1;

        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        using var reader = new StreamReader(stream);
        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
            throw new InvalidDataException("The PresentMon CSV does not contain a header row.");

        var headers = ParseCsvLine(headerLine);
        frameColumnIndex = FindColumn(headers, FrameTimeColumns);
        if (frameColumnIndex < 0)
            throw new InvalidDataException("The CSV does not contain a supported frame-time column (MsBetweenPresents or FrameTime).");

        applicationColumnIndex = FindColumn(headers, "Application");
        hasApplicationColumn = applicationColumnIndex >= 0;
        if (!hasApplicationColumn)
            throw new InvalidDataException("The CSV does not include an Application column; GTA5.exe rows cannot be isolated safely.");

        droppedColumnIndex = FindColumn(headers, "Dropped");
        cpuBusyColumnIndex = FindColumn(headers, CpuBusyColumns);
        gpuBusyColumnIndex = FindColumn(headers, GpuBusyColumns);

        string? line;
        long rowCount = 0;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            rowCount++;
            if (rowCount > MaximumFrames)
                throw new InvalidDataException("The CSV exceeds the 5,000,000-row analysis limit.");

            var fields = ParseCsvLine(line);
            if (fields.Count <= applicationColumnIndex ||
                !string.Equals(Path.GetFileName(fields[applicationColumnIndex].Trim().Trim('"')),
                    "GTA5.exe", StringComparison.OrdinalIgnoreCase))
                continue;

            matchingProcessRows++;
            if (fields.Count > droppedColumnIndex && droppedColumnIndex >= 0 &&
                IsDropped(fields[droppedColumnIndex]))
                droppedFrames++;

            if (fields.Count <= frameColumnIndex ||
                !TryPositiveMilliseconds(fields[frameColumnIndex], out var frameTime))
                continue;

            frameTimes.Add(frameTime);

            if (cpuBusyColumnIndex >= 0 && fields.Count > cpuBusyColumnIndex &&
                TryNonNegativeMilliseconds(fields[cpuBusyColumnIndex], out var cpu))
                cpuBusy.Add(cpu);

            if (gpuBusyColumnIndex >= 0 && fields.Count > gpuBusyColumnIndex &&
                TryNonNegativeMilliseconds(fields[gpuBusyColumnIndex], out var gpu))
                gpuBusy.Add(gpu);
        }

        if (frameTimes.Count < 30)
            throw new InvalidDataException(
                "At least 30 valid GTA5.exe frame-time samples are required. Matching process rows: " +
                matchingProcessRows + ", valid frame samples: " + frameTimes.Count + ".");

        frameTimes.Sort();
        var meanFrameTime = frameTimes.Average();
        var p50 = Percentile(frameTimes, 0.50);
        var p95 = Percentile(frameTimes, 0.95);
        var p99 = Percentile(frameTimes, 0.99);
        var normalizedMetadata = metadata with
        {
            GameBuild = metadata.GameBuild?.Trim() ?? string.Empty,
            Resolution = metadata.Resolution?.Trim() ?? string.Empty,
            Gpu = metadata.Gpu?.Trim() ?? string.Empty,
            Driver = metadata.Driver?.Trim() ?? string.Empty,
            ProfileName = metadata.ProfileName?.Trim() ?? string.Empty,
            Scenario = metadata.Scenario?.Trim() ?? string.Empty,
            Notes = metadata.Notes?.Trim() ?? string.Empty
        };

        return new PerformanceBenchmarkReport(
            1,
            DateTimeOffset.UtcNow,
            Path.GetFileName(fullPath),
            sourceSha256,
            normalizedMetadata,
            frameTimes.Count,
            matchingProcessRows,
            droppedFrames,
            meanFrameTime,
            p50,
            p95,
            p99,
            1000.0 / meanFrameTime,
            1000.0 / p99,
            cpuBusy.Count == 0 ? null : cpuBusy.Average(),
            gpuBusy.Count == 0 ? null : gpuBusy.Average(),
            "Frame-time and optional CPU/GPU busy metrics are parsed from the imported PresentMon CSV. 1% low is estimated as 1000 / p99 frame time. VRAM, GPU utilisation, temperature, clocks and power are not inferred unless separately recorded.");
    }

    public async Task<string> SaveReportAsync(
        PerformanceBenchmarkReport report,
        string reportDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportDirectory);
        var root = Path.GetFullPath(reportDirectory);
        Directory.CreateDirectory(root);

        var safePrefix = report.SourceCsvSha256[..12].ToLowerInvariant();
        var fileName = "benchmark-" + report.CapturedAt.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
                       "-" + safePrefix + ".json";
        var target = Path.Combine(root, fileName);
        if (File.Exists(target))
            throw new IOException("A benchmark report with this timestamp and source digest already exists.");

        await using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, report, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        return target;
    }

    private static int FindColumn(IReadOnlyList<string> headers, params string[] choices)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (choices.Any(choice => string.Equals(headers[i].Trim(), choice, StringComparison.OrdinalIgnoreCase)))
                return i;
        }

        return -1;
    }

    private static bool TryPositiveMilliseconds(string value, out double result) =>
        double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
        double.IsFinite(result) && result > 0 && result <= 1000;

    private static bool TryNonNegativeMilliseconds(string value, out double result) =>
        double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
        double.IsFinite(result) && result >= 0 && result <= 1000;

    private static bool IsDropped(string value)
    {
        var normalized = value.Trim();
        return normalized.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("dropped", StringComparison.OrdinalIgnoreCase);
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        var index = Math.Clamp((int)Math.Ceiling(percentile * sortedValues.Count) - 1, 0, sortedValues.Count - 1);
        return sortedValues[index];
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var value = new System.Text.StringBuilder();
        var quoted = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                fields.Add(value.ToString());
                value.Clear();
            }
            else
            {
                value.Append(character);
            }
        }

        if (quoted)
            throw new InvalidDataException("A CSV row contains an unterminated quoted field.");

        fields.Add(value.ToString());
        return fields;
    }
}

public sealed record PerformanceBenchmarkMetadata(
    string GameBuild,
    string Resolution,
    string Gpu,
    string Driver,
    string ProfileName,
    string Scenario,
    string Notes);

public sealed record PerformanceBenchmarkReport(
    int SchemaVersion,
    DateTimeOffset CapturedAt,
    string SourceCsvFileName,
    string SourceCsvSha256,
    PerformanceBenchmarkMetadata Metadata,
    int FramesAnalyzed,
    long MatchingProcessRows,
    long DroppedFrames,
    double MeanFrameTimeMs,
    double MedianFrameTimeMs,
    double P95FrameTimeMs,
    double P99FrameTimeMs,
    double AverageFps,
    double EstimatedOnePercentLowFps,
    double? AverageCpuBusyMs,
    double? AverageGpuBusyMs,
    string MethodologyNote);
