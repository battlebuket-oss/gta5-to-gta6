using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class GtaViInspiredHudRootPluginServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "GtaHudRootPluginTests", Guid.NewGuid().ToString("N"));
    private readonly string _gameRoot;
    private readonly string _modsRoot;
    private readonly string _transactionRoot;
    private readonly string _statePath;

    public GtaViInspiredHudRootPluginServiceTests()
    {
        _gameRoot = Path.Combine(_root, "game");
        _modsRoot = Path.Combine(_gameRoot, "mods");
        _transactionRoot = Path.Combine(_root, "transactions");
        _statePath = Path.Combine(_root, "state", "active.json");
        Directory.CreateDirectory(_modsRoot);
        File.WriteAllText(Path.Combine(_gameRoot, "GTA5.exe"), "test executable");
        File.WriteAllText(Path.Combine(_modsRoot, "ViMinimap.asi"), "minimap-plugin-v1");
        File.WriteAllText(Path.Combine(_modsRoot, "ViPreload.asi"), "preload-plugin-v1");
    }

    [Fact]
    public async Task DeployAndRollback_CopiesOnlyTheTwoDocumentedRootPlugins()
    {
        var service = CreateService();

        var inspection = await service.InspectAsync(Installation());
        Assert.True(inspection.AllSourcesPresent);
        Assert.False(inspection.HasConflictingDestinations);

        var result = await service.DeployAsync(
            Installation(), allowReplacingDifferentRootFiles: false);

        Assert.Equal(2, result.FilesApplied);
        Assert.True(File.Exists(Path.Combine(_gameRoot, "ViMinimap.asi")));
        Assert.True(File.Exists(Path.Combine(_gameRoot, "ViPreload.asi")));
        Assert.False(File.Exists(Path.Combine(_gameRoot, "unrelated.asi")));
        Assert.True(await service.HasActiveDeploymentAsync());

        await service.RollbackLatestAsync(Installation());

        Assert.False(File.Exists(Path.Combine(_gameRoot, "ViMinimap.asi")));
        Assert.False(File.Exists(Path.Combine(_gameRoot, "ViPreload.asi")));
        Assert.False(await service.HasActiveDeploymentAsync());
    }

    [Fact]
    public async Task Deploy_IsIdempotentWhenTheExactFilesAreAlreadyInstalled()
    {
        var service = CreateService();
        var first = await service.DeployAsync(Installation(), allowReplacingDifferentRootFiles: false);
        var second = await service.DeployAsync(Installation(), allowReplacingDifferentRootFiles: false);

        Assert.False(first.AlreadyCurrent);
        Assert.True(second.AlreadyCurrent);
        Assert.Equal(first.TransactionId, second.TransactionId);
        Assert.Equal(0, second.FilesApplied);
    }

    [Fact]
    public async Task Deploy_RefusesDifferentExistingRootPluginsWithoutConsent()
    {
        File.WriteAllText(Path.Combine(_gameRoot, "ViMinimap.asi"), "another-mod");
        var service = CreateService();

        var inspection = await service.InspectAsync(Installation());
        Assert.True(inspection.HasConflictingDestinations);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeployAsync(Installation(), allowReplacingDifferentRootFiles: false));

        Assert.Equal("another-mod",
            await File.ReadAllTextAsync(Path.Combine(_gameRoot, "ViMinimap.asi")));
    }

    [Fact]
    public async Task Deploy_WithConsent_BackupsAndRestoresDifferentExistingRootPlugins()
    {
        File.WriteAllText(Path.Combine(_gameRoot, "ViMinimap.asi"), "previous-plugin");
        var service = CreateService();

        var result = await service.DeployAsync(
            Installation(), allowReplacingDifferentRootFiles: true);

        Assert.Equal("minimap-plugin-v1",
            await File.ReadAllTextAsync(Path.Combine(_gameRoot, "ViMinimap.asi")));
        await service.RollbackLatestAsync(Installation());

        Assert.Equal("previous-plugin",
            await File.ReadAllTextAsync(Path.Combine(_gameRoot, "ViMinimap.asi")));
        Assert.False(File.Exists(Path.Combine(_gameRoot, "ViPreload.asi")));
    }

    [Fact]
    public async Task Rollback_RefusesToOverwriteUserModifiedRootPlugin()
    {
        var service = CreateService();
        await service.DeployAsync(Installation(), allowReplacingDifferentRootFiles: false);
        await File.WriteAllTextAsync(Path.Combine(_gameRoot, "ViMinimap.asi"), "user modification");

        await Assert.ThrowsAsync<IOException>(() => service.RollbackLatestAsync(Installation()));

        Assert.Equal("user modification",
            await File.ReadAllTextAsync(Path.Combine(_gameRoot, "ViMinimap.asi")));
    }

    [Fact]
    public async Task Inspect_RequiresBothSourcePlugins()
    {
        File.Delete(Path.Combine(_modsRoot, "ViPreload.asi"));
        var inspection = await CreateService().InspectAsync(Installation());

        Assert.False(inspection.AllSourcesPresent);
        Assert.Contains("ViPreload.asi", inspection.MissingSourceFiles);
    }

    private GtaViInspiredHudRootPluginService CreateService() =>
        new(new TransactionalDeploymentService(dataRoot: _transactionRoot), _statePath);

    private GameInstallation Installation() =>
        new(_gameRoot, Path.Combine(_gameRoot, "GTA5.exe"), "1.0.3889.0", "1.0.3889.0", "1.0.3889.0");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
