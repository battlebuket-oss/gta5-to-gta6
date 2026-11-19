using System;
using System.IO;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class GameInstallationDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "GtaHubGameDetection", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Inspect_IdentifiesLegacyExecutableAndRecordsFingerprintWithoutApprovingCompatibility()
    {
        var executable = CreateLegacyLayout();
        var detector = new GameInstallationDetector(_ => new ExecutableVersionDetails(
            "1.0.3889.0",
            "1.0.3889.0",
            "Grand Theft Auto V"));

        var result = detector.Inspect(executable);

        Assert.Equal("GTA V Legacy", result.Edition);
        Assert.Equal("1.0.3889.0", result.FileVersion);
        Assert.Equal(Path.GetDirectoryName(executable), result.GameDirectory);
        Assert.Equal(64, result.ExecutableSha256.Length);
        Assert.Contains("Unknown", result.CompatibilityStatus);
        Assert.Contains("approval", result.CompatibilityStatus);
    }

    [Fact]
    public void Inspect_RejectsEnhancedExecutableName()
    {
        var directory = Path.Combine(_root, "enhanced");
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "GTA5_Enhanced.exe");
        File.WriteAllText(executable, "not a game executable");

        var detector = new GameInstallationDetector(_ => new ExecutableVersionDetails(
            "1.0.1.0", "1.0.1.0", "Grand Theft Auto V"));

        Assert.Throws<InvalidDataException>(() => detector.Inspect(executable));
    }

    [Fact]
    public void Inspect_RejectsFolderWithoutGtaUpdateArchive()
    {
        var directory = Path.Combine(_root, "incomplete");
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "GTA5.exe");
        File.WriteAllText(executable, "placeholder");

        var detector = new GameInstallationDetector(_ => new ExecutableVersionDetails(
            "1.0.3889.0", "1.0.3889.0", "Grand Theft Auto V"));

        Assert.Throws<InvalidDataException>(() => detector.Inspect(executable));
    }

    [Fact]
    public void Inspect_RejectsExecutableWithNonGtaProductMetadata()
    {
        var executable = CreateLegacyLayout();
        var detector = new GameInstallationDetector(_ => new ExecutableVersionDetails(
            "1.0.3889.0", "1.0.3889.0", "Unrelated Application"));

        Assert.Throws<InvalidDataException>(() => detector.Inspect(executable));
    }

    [Fact]
    public void Inspect_RejectsMissingOrUnparseableExecutableVersion()
    {
        var executable = CreateLegacyLayout();
        var detector = new GameInstallationDetector(_ => new ExecutableVersionDetails(
            "unknown", "unknown", "Grand Theft Auto V"));

        Assert.Throws<InvalidDataException>(() => detector.Inspect(executable));
    }

    private string CreateLegacyLayout()
    {
        var directory = Path.Combine(_root, "legacy");
        Directory.CreateDirectory(Path.Combine(directory, "update"));
        File.WriteAllText(Path.Combine(directory, "GTA5.exe"), "test executable bytes");
        File.WriteAllText(Path.Combine(directory, "update", "update.rpf"), "test RPF placeholder");
        return Path.Combine(directory, "GTA5.exe");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
