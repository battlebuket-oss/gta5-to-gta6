using System.IO.Compression;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class SaveBackupAndPerformanceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GtaHubUtilityTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StorySaveBackup_CreatesArchiveWithoutChangingSource()
    {
        var profiles = Path.Combine(_root, "Profiles");
        var profile = Path.Combine(profiles, "A1B2C3");
        Directory.CreateDirectory(profile);
        var savePath = Path.Combine(profile, "SGTA50000");
        await File.WriteAllTextAsync(savePath, "user save fixture");

        var backup = await new StorySaveBackupService(Path.Combine(_root, "backups"))
            .CreateBackupAsync(profiles);

        Assert.True(File.Exists(backup.ArchivePath));
        Assert.Equal(1, backup.FileCount);
        Assert.Equal("user save fixture", await File.ReadAllTextAsync(savePath));
        using var archive = ZipFile.OpenRead(backup.ArchivePath);
        Assert.Single(archive.Entries);
        Assert.Equal("A1B2C3/SGTA50000", archive.Entries[0].FullName.Replace('\\', '/'));
    }

    [Fact]
    public void PerformanceProfiles_AreGuidanceOnlyAndHaveUniqueIds()
    {
        var profiles = PerformanceProfileService.GetProfiles();
        Assert.NotEmpty(profiles);
        Assert.Equal(profiles.Count, profiles.Select(profile => profile.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(profiles, profile => profile.Id == "balanced");
        Assert.All(profiles, profile => Assert.False(profile.RayTracing));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}