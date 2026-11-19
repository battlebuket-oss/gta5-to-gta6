using System.Text;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class CodeWalkerInstallHistoryServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "GtaHubCodeWalkerHistoryTests", Guid.NewGuid().ToString("N"));

    public CodeWalkerInstallHistoryServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ReadAsync_ReturnsEmptyWhenCodeWalkerHasNoBackupDirectory()
    {
        var report = await new CodeWalkerInstallHistoryService().ReadAsync(_root);

        Assert.False(report.BackupDirectoryExists);
        Assert.Empty(report.Packages);
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public async Task ReadAsync_ParsesAndSortsCodeWalkerInstallRecords()
    {
        var oldDirectory = CreatePackageDirectory("Old Package");
        var newDirectory = CreatePackageDirectory("New Package");

        WriteInstallLog(oldDirectory, """
            {
              "PackageName": "Older Visual Pack",
              "Description": "Older test record",
              "Version": "1.0",
              "IsGen9": false,
              "InstallDate": "2026-11-18T10:00:00+05:30",
              "Entries": [
                { "Action": 0, "OriginalPath": "mods/example/a.rpf" },
                { "Action": 1, "OriginalPath": "mods/example/b.rpf" }
              ]
            }
            """);
        WriteInstallLog(newDirectory, """
            {
              "PackageName": "Newer Water Pack",
              "Description": "Newer test record",
              "Version": "2.1",
              "IsGen9": true,
              "InstallDate": "2026-11-19T09:00:00+05:30",
              "Entries": [
                { "Action": 0, "OriginalPath": "mods/update/water.rpf" }
              ]
            }
            """);

        var report = await new CodeWalkerInstallHistoryService().ReadAsync(_root);

        Assert.True(report.BackupDirectoryExists);
        Assert.Empty(report.Warnings);
        Assert.Equal(2, report.Packages.Count);
        Assert.Equal("Newer Water Pack", report.Packages[0].PackageName);
        Assert.Equal("Enhanced", report.Packages[0].GameEdition);
        Assert.Equal("2.1", report.Packages[0].Version);
        Assert.Equal(1, report.Packages[0].FileOperationCount);
        Assert.Equal("Older Visual Pack", report.Packages[1].PackageName);
        Assert.Equal("Legacy", report.Packages[1].GameEdition);
        Assert.Equal(2, report.Packages[1].FileOperationCount);
    }

    [Fact]
    public async Task ReadAsync_SkipsMalformedLogsAndReportsWarnings()
    {
        var validDirectory = CreatePackageDirectory("Valid Package");
        var invalidDirectory = CreatePackageDirectory("Invalid Package");
        WriteInstallLog(validDirectory, """
            {
              "PackageName": "Known Package",
              "Description": "",
              "Version": "1.0",
              "IsGen9": false,
              "InstallDate": "2026-11-19T09:00:00Z",
              "Entries": []
            }
            """);
        WriteInstallLog(invalidDirectory, "{not-json");

        var report = await new CodeWalkerInstallHistoryService().ReadAsync(_root);

        var package = Assert.Single(report.Packages);
        Assert.Equal("Known Package", package.PackageName);
        Assert.Contains(report.Warnings, warning =>
            warning.Contains("Invalid Package", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReadAsync_DoesNotTreatInstallerRecordsAsGameRuntimeProof()
    {
        var directory = CreatePackageDirectory("Record Only");
        WriteInstallLog(directory, """
            {
              "PackageName": "Record Only",
              "Description": "This is only an installer log",
              "Version": "1",
              "IsGen9": false,
              "InstallDate": "2026-11-19T09:00:00Z",
              "Entries": []
            }
            """);

        var report = await new CodeWalkerInstallHistoryService().ReadAsync(_root);

        var package = Assert.Single(report.Packages);
        Assert.Equal(0, package.FileOperationCount);
        Assert.True(File.Exists(package.InstallLogPath));
        // The report is only an inventory of third-party installer records. It
        // intentionally exposes no "active", "working", or "verified" status.
        Assert.DoesNotContain("Verified", typeof(CodeWalkerInstallHistoryEntry).GetProperties()
            .Select(property => property.Name), StringComparer.OrdinalIgnoreCase);
    }

    private string CreatePackageDirectory(string name)
    {
        var root = Path.Combine(_root, "OIV_CW_Uninstall_Data");
        Directory.CreateDirectory(root);
        var directory = Path.Combine(root, name.Replace(' ', '_'));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WriteInstallLog(string directory, string json)
    {
        File.WriteAllText(
            Path.Combine(directory, "install.log"),
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
