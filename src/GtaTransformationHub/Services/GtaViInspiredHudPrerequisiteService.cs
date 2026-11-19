using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace GtaTransformationHub.Services;

public sealed record HudPrerequisiteItem(
    string Name,
    string RelativePath,
    bool IsPresent,
    string? DetectedFileVersion);

public sealed record HudPrerequisiteInspection(
    IReadOnlyList<HudPrerequisiteItem> Items)
{
    public bool AllRequiredFilesPresent => Items.All(item => item.IsPresent);

    public IReadOnlyList<HudPrerequisiteItem> MissingItems =>
        Items.Where(item => !item.IsPresent).ToArray();
}

/// <summary>
/// Checks the documented Windows Legacy prerequisites for the GTA VI-Inspired HUD.
/// This is a presence check, not proof of runtime version compatibility. The user
/// must still confirm the Script Hook V supported patch and SHVDN nightly version.
/// </summary>
public sealed class GtaViInspiredHudPrerequisiteService
{
    private static readonly string[] RequiredRootFiles =
    {
        "ScriptHookV.dll",
        "dinput8.dll",
        "ScriptHookVDotNet-Loader.asi",
        "ScriptHookVDotNet.dll",
        "ScriptHookVDotNet2.dll",
        "ScriptHookVDotNet3.dll",
        "ScriptHookVDotNet.ini",
        "OpenIV.asi"
    };

    public HudPrerequisiteInspection Inspect(GameInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);

        var gameRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation.RootPath));
        if (!Directory.Exists(gameRoot) ||
            !Path.GetFullPath(installation.ExecutablePath).Equals(
                Path.Combine(gameRoot, "GTA5.exe"), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The selected GTA V Legacy installation is invalid. Locate GTA5.exe again before checking HUD prerequisites.");
        }

        var items = new List<HudPrerequisiteItem>();
        foreach (var relativePath in RequiredRootFiles)
        {
            var fullPath = Path.Combine(gameRoot, relativePath);
            var present = File.Exists(fullPath) &&
                          (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) == 0;

            string? fileVersion = null;
            if (present)
            {
                try
                {
                    var version = FileVersionInfo.GetVersionInfo(fullPath);
                    fileVersion = FirstNonEmpty(version.ProductVersion, version.FileVersion);
                }
                catch
                {
                    // Presence remains visible, but version metadata is not trusted.
                }
            }

            items.Add(new HudPrerequisiteItem(
                relativePath,
                relativePath,
                present,
                fileVersion));
        }

        var modsPath = Path.Combine(gameRoot, "mods");
        var modsPresent = Directory.Exists(modsPath) &&
                          (File.GetAttributes(modsPath) & FileAttributes.ReparsePoint) == 0;
        items.Add(new HudPrerequisiteItem(
            "OpenIV mods folder",
            "mods/",
            modsPresent,
            null));

        return new HudPrerequisiteInspection(items);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
