using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text.Json;

namespace GtaTransformationHub.Services;

/// <summary>
/// Conservative, copy-only deployment transaction. It never executes package code
/// or modifies a running game process. Rollback removes only files created by its own journal.
/// </summary>
public sealed class TransactionalDeploymentService
{
    private static readonly SemaphoreSlim DeploymentLock = new(1, 1);
    private readonly GameProcessMonitor _processMonitor;
    private readonly string _dataRoot;

    public TransactionalDeploymentService(GameProcessMonitor? processMonitor = null, string? dataRoot = null)
    {
        _processMonitor = processMonitor ?? new GameProcessMonitor();
        _dataRoot = Path.GetFullPath(dataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "transactions"));
    }

    public async Task<DeploymentResult> ApplyAsync(
        string gameRoot,
        string packageRoot,
        IReadOnlyCollection<DeploymentFile> files,
        CancellationToken cancellationToken = default)
    {
        await DeploymentLock.WaitAsync(cancellationToken);
        try
        {
            _processMonitor.EnsureConfigurationUnlocked();
            var normalizedGameRoot = ValidateGameRoot(gameRoot);
            var normalizedPackageRoot = Path.GetFullPath(packageRoot);
            if (!Directory.Exists(normalizedPackageRoot))
                throw new DirectoryNotFoundException("The selected package directory does not exist.");
            if (files.Count == 0)
                throw new InvalidOperationException("The deployment plan contains no files.");

            var plan = await ValidateAndStageAsync(
                normalizedGameRoot, normalizedPackageRoot, files, cancellationToken);

            _processMonitor.EnsureConfigurationUnlocked();
            Directory.CreateDirectory(_dataRoot);
            var transactionId = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N");
            var transactionRoot = Path.Combine(_dataRoot, transactionId);
            var backupRoot = Path.Combine(transactionRoot, "backup");
            var stageRoot = Path.Combine(transactionRoot, "stage");
            Directory.CreateDirectory(backupRoot);
            Directory.CreateDirectory(stageRoot);

            var journal = new DeploymentJournal(transactionId, normalizedGameRoot, "Applying", []);
            var journalPath = Path.Combine(transactionRoot, "journal.json");
            await SaveJournalAsync(journalPath, journal, cancellationToken);

            try
            {
                for (var index = 0; index < plan.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _processMonitor.EnsureConfigurationUnlocked();

                    var item = plan[index];
                    var destination = ResolveContainedPath(normalizedGameRoot, item.DestinationRelativePath);
                    EnsureNoReparsePoints(normalizedGameRoot, destination);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                    var staged = Path.Combine(stageRoot, index.ToString("D6") + ".bin");
                    await CopyAndVerifyAsync(item.SourcePath, staged, item.ExpectedSha256, cancellationToken);

                    var existed = File.Exists(destination);
                    var backupRelative = index.ToString("D6") + ".bak";
                    var backup = Path.Combine(backupRoot, backupRelative);
                    string? originalSha256 = null;
                    if (existed)
                    {
                        originalSha256 = await ComputeSha256Async(destination, cancellationToken);
                        File.Copy(destination, backup, overwrite: false);
                    }

                    var record = new DeploymentJournalEntry(
                        item.PackageId, item.DestinationRelativePath, existed, backupRelative,
                        item.ExpectedSha256, originalSha256);
                    journal = journal with { Entries = journal.Entries.Append(record).ToArray() };
                    await SaveJournalAsync(journalPath, journal, cancellationToken);

                    var temporaryDestination = destination + ".gta-hub-" + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.Copy(staged, temporaryDestination, overwrite: false);
                        File.Move(temporaryDestination, destination, overwrite: true);
                    }
                    finally
                    {
                        if (File.Exists(temporaryDestination))
                            File.Delete(temporaryDestination);
                    }
                }

                journal = journal with { State = "Applied" };
                await SaveJournalAsync(journalPath, journal, cancellationToken);
                return new DeploymentResult(transactionId, plan.Count, transactionRoot);
            }
            catch (Exception applyError)
            {
                try
                {
                    await RollbackJournalAsync(journal, backupRoot, journalPath, CancellationToken.None);
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException(
                        "Deployment failed and automatic rollback was incomplete. Preserve the transaction folder for recovery.",
                        applyError, rollbackError);
                }

                throw new InvalidOperationException(
                    "Deployment failed. Changes from this transaction were rolled back.", applyError);
            }
        }
        finally
        {
            DeploymentLock.Release();
        }
    }

    public async Task RollbackLatestAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        await DeploymentLock.WaitAsync(cancellationToken);
        try
        {
            _processMonitor.EnsureConfigurationUnlocked();
            if (string.IsNullOrWhiteSpace(transactionId) ||
                transactionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                transactionId.Contains("..", StringComparison.Ordinal))
                throw new ArgumentException("Invalid transaction identifier.", nameof(transactionId));

            var transactionRoot = Path.Combine(_dataRoot, transactionId);
            var journalPath = Path.Combine(transactionRoot, "journal.json");
            if (!File.Exists(journalPath))
                throw new FileNotFoundException("The deployment journal was not found.", journalPath);

            var journal = JsonSerializer.Deserialize<DeploymentJournal>(
                await File.ReadAllTextAsync(journalPath, cancellationToken))
                ?? throw new InvalidDataException("The deployment journal is invalid.");

            if (!string.Equals(journal.TransactionId, transactionId, StringComparison.Ordinal))
                throw new InvalidDataException("The transaction journal ID does not match its directory.");
            if (!string.Equals(journal.State, "Applied", StringComparison.Ordinal))
                throw new InvalidOperationException("Only a fully applied transaction can be rolled back.");

            await RollbackJournalAsync(
                journal, Path.Combine(transactionRoot, "backup"), journalPath, cancellationToken);
        }
        finally
        {
            DeploymentLock.Release();
        }
    }

    private async Task<List<ValidatedFile>> ValidateAndStageAsync(
        string gameRoot,
        string packageRoot,
        IReadOnlyCollection<DeploymentFile> files,
        CancellationToken cancellationToken)
    {
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ValidatedFile>(files.Count);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PackageIdValidator.IsValid(file.PackageId))
                throw new InvalidDataException("A deployment entry contains an invalid package ID.");

            var source = ResolveContainedPath(packageRoot, file.SourcePath);
            var destination = ValidateRelativePath(file.DestinationRelativePath);
            if (!destinations.Add(destination))
                throw new InvalidDataException("The deployment plan contains duplicate destination paths.");
            EnsureNoReparsePoints(packageRoot, source);
            if (!File.Exists(source))
                throw new FileNotFoundException("A package file is missing.", source);
            if (file.ExpectedSha256.Length != 64 ||
                !file.ExpectedSha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Every deployment file must have a valid SHA-256 digest.");

            var actualHash = await ComputeSha256Async(source, cancellationToken);
            if (!actualHash.Equals(file.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A package file failed SHA-256 verification: " + file.SourcePath);

            result.Add(new ValidatedFile(file.PackageId, source, destination, actualHash));
        }

        return result;
    }

    private async Task CopyAndVerifyAsync(
        string source, string destination, string expectedHash, CancellationToken cancellationToken)
    {
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await input.CopyToAsync(output, cancellationToken);

        var actualHash = await ComputeSha256Async(destination, cancellationToken);
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(destination);
            throw new InvalidDataException("A staged package file failed SHA-256 verification.");
        }
    }

    private async Task RollbackJournalAsync(
        DeploymentJournal journal,
        string backupRoot,
        string journalPath,
        CancellationToken cancellationToken)
    {
        _processMonitor.EnsureConfigurationUnlocked();
        var root = ValidateGameRoot(journal.GameRoot);

        // Preflight every entry before changing anything, so a user-edited file or
        // missing/corrupt backup cannot leave a half-restored installation.
        foreach (var entry in journal.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = ResolveContainedPath(root, entry.DestinationRelativePath);
            EnsureNoReparsePoints(root, destination);
            if (entry.ExistedBefore)
            {
                if (string.IsNullOrWhiteSpace(entry.OriginalSha256) || entry.OriginalSha256.Length != 64)
                    throw new InvalidDataException("The journal is missing an original file hash.");
                var backupRelative = ValidateRelativePath(entry.BackupFile);
                var backup = ResolveContainedPath(backupRoot, backupRelative);
                if (!File.Exists(backup))
                    throw new FileNotFoundException("A required rollback backup is missing.", backup);
                var backupHash = await ComputeSha256Async(backup, cancellationToken);
                if (!backupHash.Equals(entry.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("A rollback backup failed integrity verification.");

                if (File.Exists(destination))
                {
                    var currentHash = await ComputeSha256Async(destination, cancellationToken);
                    if (!currentHash.Equals(entry.DeployedSha256, StringComparison.OrdinalIgnoreCase) &&
                        !currentHash.Equals(entry.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("A deployed file changed after installation; rollback stopped to protect user changes: " + entry.DestinationRelativePath);
                }
            }
            else if (File.Exists(destination))
            {
                var currentHash = await ComputeSha256Async(destination, cancellationToken);
                if (!currentHash.Equals(entry.DeployedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("A file created by the deployment was changed; rollback stopped to protect user changes: " + entry.DestinationRelativePath);
            }
        }

        foreach (var entry in journal.Entries.Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            _processMonitor.EnsureConfigurationUnlocked();
            var destination = ResolveContainedPath(root, entry.DestinationRelativePath);
            if (entry.ExistedBefore)
            {
                if (File.Exists(destination))
                {
                    var currentHash = await ComputeSha256Async(destination, cancellationToken);
                    if (currentHash.Equals(entry.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                var backup = ResolveContainedPath(backupRoot, ValidateRelativePath(entry.BackupFile));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var restoreTemp = destination + ".gta-hub-restore-" + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.Copy(backup, restoreTemp, overwrite: false);
                    File.Move(restoreTemp, destination, overwrite: true);
                }
                finally
                {
                    if (File.Exists(restoreTemp))
                        File.Delete(restoreTemp);
                }
            }
            else if (File.Exists(destination))
            {
                File.Delete(destination);
            }
        }

        var rolledBack = journal with { State = "RolledBack" };
        await SaveJournalAsync(journalPath, rolledBack, cancellationToken);
    }

    private static string ValidateGameRoot(string gameRoot)
    {
        var root = Path.GetFullPath(gameRoot);
        if (!Directory.Exists(root) || !File.Exists(Path.Combine(root, "GTA5.exe")))
            throw new InvalidOperationException("The selected folder is not a valid GTA V installation root.");
        return Path.TrimEndingDirectorySeparator(root);
    }

    private static string ValidateRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException("Package paths must be non-empty relative paths.");

        var normalized = relativePath.Replace('\\', '/');
        var segments = normalized.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment == "." || segment == ".." ||
                                    segment.Contains(':') || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("A package path contains an unsafe path segment.");

        return Path.Combine(segments);
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        var safeRelative = ValidateRelativePath(relativePath);
        var full = Path.GetFullPath(Path.Combine(root, safeRelative));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A package path escapes its permitted root.");
        return full;
    }

    private static void EnsureNoReparsePoints(string root, string destination)
    {
        var rootInfo = new DirectoryInfo(root);
        for (var ancestor = rootInfo; ancestor is not null; ancestor = ancestor.Parent)
        {
            if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Deployment through a reparse-point directory is blocked.");
        }

        var relative = Path.GetRelativePath(root, Path.GetDirectoryName(destination)!);
        var current = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Deployment into a reparse-point directory is blocked.");
        }

        if (File.Exists(destination) &&
            (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Replacing a reparse-point file is blocked.");
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static async Task SaveJournalAsync(
        string path, DeploymentJournal journal, CancellationToken cancellationToken)
    {
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(journal, new JsonSerializerOptions
        {
            WriteIndented = true
        }), cancellationToken);
        File.Move(temp, path, overwrite: true);
    }

    private sealed record ValidatedFile(
        string PackageId, string SourcePath, string DestinationRelativePath, string ExpectedSha256);
}

public sealed record DeploymentResult(string TransactionId, int FilesApplied, string TransactionDirectory);

public sealed record DeploymentJournal(
    string TransactionId,
    string GameRoot,
    string State,
    IReadOnlyList<DeploymentJournalEntry> Entries);

public sealed record DeploymentJournalEntry(
    string PackageId,
    string DestinationRelativePath,
    bool ExistedBefore,
    string BackupFile,
    string DeployedSha256,
    string? OriginalSha256);
