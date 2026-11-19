using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

public sealed record OpenRageInstalledOivGroup(string Name, int RecordedChanges);

public sealed record HudLoosePluginPresence(
    bool ViMinimapPresent,
    bool ViPreloadPresent,
    IReadOnlyList<string> LocatedFiles)
{
    public bool AnyPresent => ViMinimapPresent || ViPreloadPresent;
    public bool BothPresent => ViMinimapPresent && ViPreloadPresent;
}

public sealed record ManagedPackageApplyResult(
    bool Success,
    bool Changed,
    bool IsInstalled,
    string PackageGroupName,
    string Message,
    IReadOnlyList<string> Details);

/// <summary>
/// The first real profile adapter. It manages only the pinned GTA V Legacy
/// GTA VI-Inspired HUD OIV and delegates all OIV file operations to OpenRAGE.
/// It never guesses paths or deletes files directly.
/// </summary>
public sealed class GtaViInspiredHudProfileAdapter
{
    private static readonly Regex GroupLine = new(
        @"^(?<name>.+?)\s+\((?<count>[0-9]+)\s+recorded changes\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly OpenRageToolchainService _openRage;
    private readonly ImportedModLibraryService _library;

    public GtaViInspiredHudProfileAdapter(
        OpenRageToolchainService openRage,
        ImportedModLibraryService? library = null)
    {
        _openRage = openRage ?? throw new ArgumentNullException(nameof(openRage));
        _library = library ?? new ImportedModLibraryService();
    }

    public static string GetPackageGroupName(OivManifestInspectionResult inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        return OpenRageToolchainService.BuildOivGroupName(
            inspection.Name, inspection.ModVersion);
    }

    /// <summary>
    /// Parses the documented OpenRAGE installed-package list output. Unknown
    /// non-empty lines are rejected so the UI never reports an uncertain package as disabled.
    /// </summary>
    public static IReadOnlyList<OpenRageInstalledOivGroup> ParseInstalledGroups(string output)
    {
        var text = output ?? string.Empty;
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 ||
            (lines.Length == 1 && lines[0].Trim().Equals(
                "No packages installed with OpenRAGE.", StringComparison.OrdinalIgnoreCase)))
        {
            return Array.Empty<OpenRageInstalledOivGroup>();
        }

        var groups = new List<OpenRageInstalledOivGroup>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            var match = GroupLine.Match(line);
            if (!match.Success ||
                !int.TryParse(match.Groups["count"].Value, out var count) ||
                count < 0)
            {
                throw new InvalidDataException(
                    "OpenRAGE returned an unrecognised installed-package list line. " +
                    "The Hub will not infer package state from incomplete output: " + line);
            }

            var name = match.Groups["name"].Value.Trim();
            OpenRageToolchainService.ValidatePackageGroupName(name);
            if (!names.Add(name))
                throw new InvalidDataException("OpenRAGE returned a duplicate installed-package group: " + name);

            groups.Add(new OpenRageInstalledOivGroup(name, count));
        }

        return groups;
    }

    public static HudLoosePluginPresence InspectLoosePlugins(GameInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation.RootPath));
        var searchRoots = new[]
        {
            root,
            Path.Combine(root, "mods")
        };
        var minimapLocations = new List<string>();
        var preloadLocations = new List<string>();

        foreach (var searchRoot in searchRoots)
        {
            if (!Directory.Exists(searchRoot))
                continue;

            if ((File.GetAttributes(searchRoot) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The HUD plugin inspection path contains a reparse-point directory.");

            AddIfPresent(Path.Combine(searchRoot, "ViMinimap.asi"), minimapLocations);
            AddIfPresent(Path.Combine(searchRoot, "ViPreload.asi"), preloadLocations);
        }

        return new HudLoosePluginPresence(
            minimapLocations.Count > 0,
            preloadLocations.Count > 0,
            minimapLocations.Concat(preloadLocations).ToArray());
    }

    public static void ValidateJournalAndPluginPresence(
        OpenRageInstalledOivGroup? journalGroup,
        HudLoosePluginPresence presence)
    {
        ArgumentNullException.ThrowIfNull(presence);

        if (journalGroup is null && presence.AnyPresent)
        {
            throw new InvalidDataException(
                "The OpenRAGE journal does not list the HUD, but HUD-specific root plugin file(s) exist: " +
                string.Join(", ", presence.LocatedFiles) +
                ". This may be a manual OpenIV install or a partial install. The Hub refuses to report OFF or uninstall files it cannot attribute to its exact OpenRAGE package group.");
        }

        if (journalGroup is not null && !presence.BothPresent)
        {
            throw new InvalidDataException(
                "OpenRAGE lists the HUD package group, but one or both required root plugins are missing. " +
                "The installation state is inconsistent and has not been treated as ON.");
        }
    }

    public async Task<OpenRageInstalledOivGroup?> GetInstalledStateAsync(
        GameInstallation installation,
        string packageGroupName,
        CancellationToken cancellationToken = default)
    {
        OpenRageToolchainService.ValidatePackageGroupName(packageGroupName);
        var result = await _openRage.ListInstalledOivPackagesAsync(
            installation, cancellationToken);
        var groups = ParseInstalledGroups(result.StandardOutput);
        var group = groups.FirstOrDefault(item =>
            string.Equals(item.Name, packageGroupName, StringComparison.OrdinalIgnoreCase));

        var loosePlugins = InspectLoosePlugins(installation);
        ValidateJournalAndPluginPresence(group, loosePlugins);
        return group;
    }

    private static void AddIfPresent(string path, ICollection<string> results)
    {
        if (!File.Exists(path))
            return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A HUD plugin file is a reparse point: " + Path.GetFileName(path));
        results.Add(path);
    }

    public async Task<ManagedPackageApplyResult> ApplyAsync(
        GameInstallation installation,
        string packageLibraryRoot,
        ImportedModLibraryEntry package,
        bool shouldBeEnabled,
        bool userConfirmed,
        bool allowGameFolderFiles,
        bool confirmedHudRuntimeAndLanguage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentNullException.ThrowIfNull(package);

        if (!string.Equals(package.PackageId, GtaViInspiredHudPackageService.PackageId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "This adapter only manages the pinned GTA VI-Inspired HUD Legacy package.");
        }

        if (!package.ManifestValid ||
            package.OivInspection is null ||
            package.OriginalArchivePath is null ||
            package.OriginalArchiveSha256 is null ||
            !package.OriginalArchiveSha256.Equals(
                GtaViInspiredHudPackageService.PackageSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The local HUD OIV is missing, not fully verified, or is not the pinned author release.");
        }

        var groupName = GetPackageGroupName(package.OivInspection);
        var verification = await _library.VerifyAsync(
            packageLibraryRoot, package.PackageDirectory, cancellationToken);
        if (!verification.IsValid)
        {
            throw new InvalidDataException(
                "The retained HUD package failed local inventory verification: " +
                string.Join("; ", verification.Errors.Take(8)));
        }

        var installed = await GetInstalledStateAsync(
            installation, groupName, cancellationToken);

        if (shouldBeEnabled && installed is not null)
        {
            return new ManagedPackageApplyResult(
                true, false, true, groupName,
                "The HUD is already recorded as installed by OpenRAGE.",
                new[] { installed.RecordedChanges + " recorded file changes" });
        }

        if (!shouldBeEnabled && installed is null)
        {
            return new ManagedPackageApplyResult(
                true, false, false, groupName,
                "The HUD is already disabled according to the OpenRAGE package journal.",
                Array.Empty<string>());
        }

        if (!userConfirmed)
            throw new InvalidOperationException(
                "Applying a profile that changes GTA V files requires explicit confirmation.");

        if (!shouldBeEnabled)
        {
            await _openRage.UninstallOivPackageAsync(
                installation, groupName, userConfirmed: true, cancellationToken);

            var remaining = await GetInstalledStateAsync(
                installation, groupName, cancellationToken);
            if (remaining is not null)
            {
                throw new IOException(
                    "OpenRAGE returned from uninstall, but the exact package group is still listed. " +
                    "The Hub has not marked the profile as applied.");
            }

            return new ManagedPackageApplyResult(
                true, true, false, groupName,
                "OpenRAGE uninstalled the exact HUD package group and the follow-up list confirms it is absent.",
                new[] { "OpenRAGE journal group: " + groupName });
        }

        if (!confirmedHudRuntimeAndLanguage)
        {
            throw new InvalidOperationException(
                "The HUD requires an explicit confirmation of English game language, the required Script Hook V .NET nightly, and exact Legacy build support.");
        }

        var prerequisites = new GtaViInspiredHudPrerequisiteService().Inspect(installation);
        if (!prerequisites.AllRequiredFilesPresent)
        {
            throw new InvalidOperationException(
                "The HUD's documented loader prerequisites are missing: " +
                string.Join(", ", prerequisites.MissingItems.Select(item => item.RelativePath)));
        }

        var preview = await _openRage.InspectOivAsync(
            installation, package.OriginalArchivePath, cancellationToken);

        if (!preview.PackageSha256.Equals(
                GtaViInspiredHudPackageService.PackageSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The OIV examined by OpenRAGE is not the pinned author release.");
        }

        if (!preview.IsInstallable)
        {
            throw new InvalidDataException(
                "OpenRAGE blocked this OIV: " + string.Join("; ", preview.Blockers));
        }

        if (preview.RequiresExecutableConsent && !allowGameFolderFiles)
        {
            throw new InvalidOperationException(
                "This OIV contains game-root executable/plugin changes. Separate consent is required.");
        }

        await _openRage.InstallOivAsync(
            installation,
            preview,
            userConfirmed: true,
            allowGameFolderFiles,
            cancellationToken);

        var afterInstall = await GetInstalledStateAsync(
            installation, groupName, cancellationToken);
        if (afterInstall is null)
        {
            throw new IOException(
                "OpenRAGE returned from install, but the exact package group was not found in its installed-package list. " +
                "The Hub cannot mark the profile as applied; inspect OpenRAGE history and game files.");
        }

        return new ManagedPackageApplyResult(
            true, true, true, groupName,
            "OpenRAGE installed the HUD OIV and its package journal now lists the exact package group.",
            new[]
            {
                afterInstall.RecordedChanges + " recorded file changes",
                "OpenRAGE owns the OIV's loose-file operations, including the two documented root ASI plugins."
            });
    }
}
