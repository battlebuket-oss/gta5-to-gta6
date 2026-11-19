using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GtaTransformationHub.Services;

public sealed record HudRootPluginFileStatus(
    string FileName,
    bool SourcePresent,
    bool DestinationPresent,
    bool DestinationMatchesSource,
    string? SourceSha256);

public sealed record HudRootPluginInspection(
    IReadOnlyList<HudRootPluginFileStatus> Files)
{
    public bool AllSourcesPresent => Files.Count == 2 && Files.All(file => file.SourcePresent);
    public bool HasConflictingDestinations => Files.Any(file =>
        file.DestinationPresent && !file.DestinationMatchesSource);
    public bool AllPluginsAlreadyCurrent => AllSourcesPresent &&
        Files.All(file => file.DestinationMatchesSource);
    public IReadOnlyList<string> MissingSourceFiles =>
        Files.Where(file => !file.SourcePresent).Select(file => file.FileName).ToArray();
    public IReadOnlyList<string> ConflictingFiles =>
        Files.Where(file => file.DestinationPresent && !file.DestinationMatchesSource)
            .Select(file => file.FileName).ToArray();
}

public sealed record HudRootPluginDeploymentResult(
    string TransactionId,
    int FilesApplied,
    bool AlreadyCurrent,
    IReadOnlyList<string> FileNames);

/// <summary>
/// Deploys only the two root-level ASI files explicitly documented by the
/// GTA VI-Inspired HUD's Legacy installation guide. Source files must already be
/// present in the OIV-created mods folder. Deployment uses the Hub's journalled
/// transaction engine and stores the transaction ID for guarded rollback.
/// No other package path or filename can be passed to this service.
/// </summary>
public sealed class GtaViInspiredHudRootPluginService
{
    public const string PackageId = "gta-vi-inspired-hud-single-player";

    private static readonly string[] RequiredFileNames =
    {
        "ViMinimap.asi",
        "ViPreload.asi"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly TransactionalDeploymentService _deploymentService;
    private readonly string _statePath;

    public GtaViInspiredHudRootPluginService(
        TransactionalDeploymentService? deploymentService = null,
        string? statePath = null)
    {
        _deploymentService = deploymentService ?? new TransactionalDeploymentService();
        _statePath = Path.GetFullPath(statePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "HudRootPlugins", "active-deployment.json"));
    }

    public string StatePath => _statePath;

    public async Task<HudRootPluginInspection> InspectAsync(
        GameInstallation installation,
        CancellationToken cancellationToken = default)
    {
        ValidateInstallation(installation);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation.RootPath));
        var modsRoot = Path.Combine(root, "mods");

        if (!Directory.Exists(modsRoot))
            return new HudRootPluginInspection(RequiredFileNames.Select(name =>
                new HudRootPluginFileStatus(name, false, File.Exists(Path.Combine(root, name)),
                    false, null)).ToArray());

        EnsureNotReparsePoint(modsRoot, "The OpenIV mods folder is a reparse point.");

        var results = new List<HudRootPluginFileStatus>(RequiredFileNames.Length);
        foreach (var name in RequiredFileNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = Path.Combine(modsRoot, name);
            var destination = Path.Combine(root, name);

            var sourcePresent = File.Exists(source);
            string? sourceHash = null;
            if (sourcePresent)
            {
                EnsureNotReparsePoint(source, "A HUD source plugin is a reparse point: " + name);
                sourceHash = await ComputeSha256Async(source, cancellationToken);
            }

            var destinationPresent = File.Exists(destination);
            var destinationHash = (string?)null;
            if (destinationPresent)
            {
                EnsureNotReparsePoint(destination, "A game-root plugin destination is a reparse point: " + name);
                destinationHash = await ComputeSha256Async(destination, cancellationToken);
            }

            results.Add(new HudRootPluginFileStatus(
                name,
                sourcePresent,
                destinationPresent,
                sourceHash is not null && destinationHash is not null &&
                    sourceHash.Equals(destinationHash, StringComparison.OrdinalIgnoreCase),
                sourceHash));
        }

        return new HudRootPluginInspection(results);
    }

    public async Task<HudRootPluginDeploymentResult> DeployAsync(
        GameInstallation installation,
        bool allowReplacingDifferentRootFiles,
        CancellationToken cancellationToken = default)
    {
        var inspection = await InspectAsync(installation, cancellationToken);
        if (!inspection.AllSourcesPresent)
        {
            throw new InvalidOperationException(
                "The OIV has not placed all required HUD plugins in the mods folder. Missing: " +
                string.Join(", ", inspection.MissingSourceFiles));
        }

        if (inspection.HasConflictingDestinations && !allowReplacingDifferentRootFiles)
        {
            throw new InvalidOperationException(
                "A different file already exists beside GTA5.exe. The Hub will not replace it without explicit confirmation: " +
                string.Join(", ", inspection.ConflictingFiles));
        }

        var existingState = await LoadStateAsync(cancellationToken);
        if (existingState is { State: "Applied" } &&
            string.Equals(existingState.GameRoot, Path.GetFullPath(installation.RootPath),
                StringComparison.OrdinalIgnoreCase) &&
            inspection.AllPluginsAlreadyCurrent)
        {
            return new HudRootPluginDeploymentResult(
                existingState.TransactionId, 0, true, RequiredFileNames);
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation.RootPath));
        var files = inspection.Files.Select(file => new DeploymentFile(
            PackageId,
            Path.Combine("mods", file.FileName),
            file.FileName,
            file.SourceSha256!)).ToArray();

        var transaction = await _deploymentService.ApplyAsync(
            root, root, files, cancellationToken);

        var state = new PersistedHudRootPluginDeployment(
            1,
            transaction.TransactionId,
            root,
            "Applied",
            RequiredFileNames,
            DateTimeOffset.UtcNow);

        try
        {
            await SaveStateAsync(state, cancellationToken);
        }
        catch
        {
            // Do not leave a committed file transaction with no discoverable rollback ID.
            await _deploymentService.RollbackLatestAsync(
                transaction.TransactionId, CancellationToken.None);
            throw;
        }

        return new HudRootPluginDeploymentResult(
            transaction.TransactionId, transaction.FilesApplied, false, RequiredFileNames);
    }

    public async Task RollbackLatestAsync(
        GameInstallation installation,
        CancellationToken cancellationToken = default)
    {
        ValidateInstallation(installation);
        var state = await LoadStateAsync(cancellationToken)
            ?? throw new FileNotFoundException("No Hub-managed HUD root-plugin deployment was found.");

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation.RootPath));
        if (!string.Equals(state.GameRoot, root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The recorded HUD root-plugin transaction belongs to a different GTA V installation.");

        if (!string.Equals(state.State, "Applied", StringComparison.Ordinal))
            throw new InvalidOperationException("The HUD root-plugin transaction is not in the Applied state.");

        await _deploymentService.RollbackLatestAsync(state.TransactionId, cancellationToken);
        await SaveStateAsync(state with
        {
            State = "RolledBack",
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
    }

    public async Task<bool> HasActiveDeploymentAsync(CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken);
        return state is not null && string.Equals(state.State, "Applied", StringComparison.Ordinal);
    }

    private async Task<PersistedHudRootPluginDeployment?> LoadStateAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_statePath))
            return null;
        if ((File.GetAttributes(_statePath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The HUD root-plugin state file is a reparse point.");

        await using var stream = new FileStream(
            _statePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var state = await JsonSerializer.DeserializeAsync<PersistedHudRootPluginDeployment>(
            stream, JsonOptions, cancellationToken);
        if (state is null || state.SchemaVersion != 1 ||
            string.IsNullOrWhiteSpace(state.TransactionId) ||
            state.TransactionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            state.TransactionId.Contains("..", StringComparison.Ordinal) ||
            state.FileNames is null ||
            state.FileNames.Count != 2 ||
            !RequiredFileNames.All(name => state.FileNames.Contains(name, StringComparer.OrdinalIgnoreCase)) ||
            state.State is not ("Applied" or "RolledBack"))
        {
            throw new InvalidDataException(
                "The saved HUD root-plugin transaction state is invalid. It was left unchanged.");
        }

        return state;
    }

    private async Task SaveStateAsync(
        PersistedHudRootPluginDeployment state,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_statePath)
            ?? throw new InvalidOperationException("The HUD root-plugin state path has no parent directory.");
        Directory.CreateDirectory(directory);
        EnsureNotReparsePoint(directory, "The HUD root-plugin state directory is a reparse point.");

        var temporary = _statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporary, _statePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void ValidateInstallation(GameInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation.RootPath));
        var executable = Path.GetFullPath(installation.ExecutablePath);
        if (!Directory.Exists(root) ||
            !File.Exists(executable) ||
            !string.Equals(Path.GetFileName(executable), "GTA5.exe", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(executable), root, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(Path.Combine(root, "GTA5.exe")))
        {
            throw new InvalidDataException(
                "The selected GTA V Legacy installation is invalid. Locate GTA5.exe again.");
        }

        EnsureNotReparsePoint(executable, "GTA5.exe is a reparse point.");
        EnsureNotReparsePoint(root, "The GTA V root folder is a reparse point.");
    }

    private static void EnsureNotReparsePoint(string path, string message)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException(message);
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private sealed record PersistedHudRootPluginDeployment(
        int SchemaVersion,
        string TransactionId,
        string GameRoot,
        string State,
        IReadOnlyList<string> FileNames,
        DateTimeOffset UpdatedAt);
}
