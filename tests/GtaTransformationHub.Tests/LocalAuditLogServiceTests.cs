using System.IO.Compression;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class LocalAuditLogServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GtaHubDiagnosticsTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Write_RedactsAbsoluteUserPathsBeforePersisting()
    {
        var logDirectory = Path.Combine(_root, "logs");
        var service = new LocalAuditLogService(logDirectory);
        var privatePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "private", "save.dat");

        service.Write(AuditLogLevel.Error, "TestPathRedaction", "A test failure referenced " + privatePath);

        var logFile = Assert.Single(Directory.GetFiles(logDirectory, "*.jsonl"));
        var content = File.ReadAllText(logFile);
        Assert.False(content.Contains(privatePath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("[PATH REDACTED]", content);
    }

    [Fact]
    public void ExportSupportBundle_ContainsOnlyLogsAndSystemMetadata()
    {
        var logDirectory = Path.Combine(_root, "logs");
        var privateDirectory = Path.Combine(_root, "user-data");
        Directory.CreateDirectory(privateDirectory);
        var privateSave = Path.Combine(privateDirectory, "story-save.dat");
        File.WriteAllText(privateSave, "private game save marker");

        var service = new LocalAuditLogService(logDirectory);
        service.Write(AuditLogLevel.Information, "TestBundle", "Bundle fixture references " + privateSave);
        var bundlePath = Path.Combine(_root, "support-bundle.zip");

        var result = service.ExportSupportBundle(bundlePath);

        Assert.True(File.Exists(bundlePath));
        Assert.Equal(1, result.IncludedLogFiles);
        using var archive = ZipFile.OpenRead(bundlePath);
        var names = archive.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Contains("diagnostics.json", names);
        Assert.Contains("manifest.json", names);
        Assert.Single(names.Where(name => name.StartsWith("logs/", StringComparison.Ordinal)));
        Assert.DoesNotContain(names, name => name.Contains("story-save", StringComparison.OrdinalIgnoreCase));

        var logEntry = Assert.Single(archive.Entries.Where(entry => entry.FullName.StartsWith("logs/", StringComparison.Ordinal)));
        using var reader = new StreamReader(logEntry.Open());
        var logContent = reader.ReadToEnd();
        Assert.DoesNotContain(privateSave, logContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[PATH REDACTED]", logContent);

        Assert.DoesNotContain("private game save marker", archive.Entries.Select(entry => entry.FullName));
    }

    [Fact]
    public void ExportSupportBundle_RejectsUnboundedRetentionPeriod()
    {
        var service = new LocalAuditLogService(Path.Combine(_root, "logs"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.ExportSupportBundle(Path.Combine(_root, "support.zip"), retentionDays: 365));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
