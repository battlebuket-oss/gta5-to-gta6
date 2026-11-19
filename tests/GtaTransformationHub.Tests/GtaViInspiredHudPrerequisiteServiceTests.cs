using System;
using System.IO;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class GtaViInspiredHudPrerequisiteServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "GtaHudPrerequisiteTests", Guid.NewGuid().ToString("N"));

    private readonly GtaViInspiredHudPrerequisiteService _service = new();

    [Fact]
    public void Inspect_MissingLoaderFiles_BlocksInstallReadiness()
    {
        Directory.CreateDirectory(_root);
        var inspection = _service.Inspect(Installation());

        Assert.False(inspection.AllRequiredFilesPresent);
        Assert.Contains(inspection.MissingItems,
            item => item.RelativePath == "ScriptHookVDotNet3.dll");
        Assert.Contains(inspection.MissingItems,
            item => item.RelativePath == "OpenIV.asi");
        Assert.Contains(inspection.MissingItems,
            item => item.RelativePath == "mods/");
    }

    [Fact]
    public void Inspect_AllDocumentedFilesAndModsFolderPresent_PassesPresenceCheck()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "GTA5.exe"), "test");
        foreach (var name in new[]
        {
            "ScriptHookV.dll",
            "dinput8.dll",
            "ScriptHookVDotNet-Loader.asi",
            "ScriptHookVDotNet.dll",
            "ScriptHookVDotNet2.dll",
            "ScriptHookVDotNet3.dll",
            "ScriptHookVDotNet.ini",
            "OpenIV.asi"
        })
        {
            File.WriteAllText(Path.Combine(_root, name), "test");
        }

        Directory.CreateDirectory(Path.Combine(_root, "mods"));

        var inspection = _service.Inspect(Installation());

        Assert.True(inspection.AllRequiredFilesPresent);
        Assert.Empty(inspection.MissingItems);
    }

    private GameInstallation Installation() =>
        new(_root, Path.Combine(_root, "GTA5.exe"), "1.0.0.0", "1.0.0.0", "1.0.0.0");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
