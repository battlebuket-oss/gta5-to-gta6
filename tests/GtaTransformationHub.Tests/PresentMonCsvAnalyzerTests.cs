using System.Globalization;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class PresentMonCsvAnalyzerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "GtaHubPresentMonTests", Guid.NewGuid().ToString("N"));

    public PresentMonCsvAnalyzerTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task AnalyzeAsync_ParsesPresentMonV2AndFiltersGta5Frames()
    {
        var path = Path.Combine(_root, "capture-v2.csv");
        await using (var writer = new StreamWriter(File.Create(path)))
        {
            await writer.WriteLineAsync("Application,ProcessID,TimeInMs,MsBetweenPresents,MsCPUBusy,MsGPUBusy,Dropped");
            await writer.WriteLineAsync("\"OtherGame.exe\",100,0,5,1,2,0");

            for (var index = 0; index < 98; index++)
                await writer.WriteLineAsync($"\"GTA5.exe\",200,{index * 8},8,2,4,0");

            await writer.WriteLineAsync("\"GTA5.exe\",200,784,12,3,5,0");
            await writer.WriteLineAsync("\"GTA5.exe\",200,796,30,4,6,1");
            await writer.WriteLineAsync("\"GTA5.exe\",200,826,NA,NA,NA,0");
        }

        var report = await new PresentMonCsvAnalyzer().AnalyzeAsync(path, Metadata());

        Assert.Equal(100, report.FramesAnalyzed);
        Assert.Equal(101, report.MatchingProcessRows);
        Assert.Equal(1, report.DroppedFrames);
        Assert.Equal(12, report.P99FrameTimeMs, 6);
        Assert.Equal(8, report.P95FrameTimeMs, 6);
        Assert.Equal(1000.0 / 8.26, report.AverageFps, 6);
        Assert.Equal(1000.0 / 12.0, report.EstimatedOnePercentLowFps, 6);
        Assert.Equal(2.03, report.AverageCpuBusyMs, 6);
        Assert.Equal(4.03, report.AverageGpuBusyMs, 6);
        Assert.Equal("1920x1080", report.Metadata.Resolution);
        Assert.Equal(64, report.SourceCsvSha256.Length);
    }

    [Fact]
    public async Task AnalyzeAsync_AcceptsLegacyV1FrameTimeHeaderAndQuotedProcessName()
    {
        var path = Path.Combine(_root, "capture-v1.csv");
        await using (var writer = new StreamWriter(File.Create(path)))
        {
            await writer.WriteLineAsync("Application,ProcessID,TimeInSeconds,msBetweenPresents,MsInPresentAPI,Dropped");
            for (var index = 0; index < 30; index++)
                await writer.WriteLineAsync($"\"GTA5.exe\",300,{index * 0.016:0.000},16,0.2,0");
        }

        var report = await new PresentMonCsvAnalyzer().AnalyzeAsync(path, Metadata());

        Assert.Equal(30, report.FramesAnalyzed);
        Assert.Equal(16, report.MedianFrameTimeMs, 6);
        Assert.Equal(62.5, report.AverageFps, 6);
        Assert.Null(report.AverageCpuBusyMs);
        Assert.Null(report.AverageGpuBusyMs);
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsCaptureWithoutApplicationColumn()
    {
        var path = Path.Combine(_root, "no-process.csv");
        await File.WriteAllTextAsync(path,
            "ProcessID,MsBetweenPresents\n" + string.Join("\n", Enumerable.Repeat("200,8", 30)));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PresentMonCsvAnalyzer().AnalyzeAsync(path, Metadata()));
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsTooFewValidGta5Samples()
    {
        var path = Path.Combine(_root, "short.csv");
        var rows = Enumerable.Repeat("\"GTA5.exe\",200,8", 29);
        await File.WriteAllTextAsync(path,
            "Application,ProcessID,MsBetweenPresents\n" + string.Join("\n", rows));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PresentMonCsvAnalyzer().AnalyzeAsync(path, Metadata()));
    }

    [Fact]
    public async Task SaveReportAsync_WritesMachineReadableJsonReport()
    {
        var csv = Path.Combine(_root, "save.csv");
        await File.WriteAllTextAsync(csv,
            "Application,ProcessID,MsBetweenPresents\n" +
            string.Join("\n", Enumerable.Repeat("\"GTA5.exe\",200,10", 30)));
        var report = await new PresentMonCsvAnalyzer().AnalyzeAsync(csv, Metadata());
        var reportPath = await new PresentMonCsvAnalyzer().SaveReportAsync(
            report, Path.Combine(_root, "reports"));

        Assert.True(File.Exists(reportPath));
        var json = await File.ReadAllTextAsync(reportPath);
        Assert.Contains("\"FramesAnalyzed\": 30", json);
        Assert.Contains("\"EstimatedOnePercentLowFps\"", json);
    }

    private static PerformanceBenchmarkMetadata Metadata() => new(
        "1.0.3889.0",
        "1920x1080",
        "NVIDIA GeForce RTX 4050 Laptop GPU",
        "test-driver",
        "Balanced 1080p",
        "Fixed benchmark route",
        "Automated fixture");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
