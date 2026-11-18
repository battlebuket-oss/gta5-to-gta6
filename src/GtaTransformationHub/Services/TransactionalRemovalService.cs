using System.Security.Cryptography;
using System.Text.Json;
using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

/// <summary>
/// Removes only files previously installed by this Hub, identified through active
/// deployment-journal ownership and matching hashes. Every removal has a rollback journal.
/// </summary>
public sealed class TransactionalRemovalService
{
    private sealed record RemovalCandidate(
        string PackageId,
        string RelativePath,
        string ExpectedSha256,
        string FullPath,
        IReadOnlyList<string> OwnerTransactionIds);

    private static readonly SemaphoreSlim RemovalLock = new(1, 1);
    private readonly string _transactionsRoot;
    private readonly GameProcessMonitor _processMonitor;
    private readonly TransactionalDeploymentService _recoveryService;

    public TransactionalRemovalService(
        GameProcessMonitor? processMonitor = null,
        string? transactionsRoot = null)
    {
        _processMonitor = processMonitor ?? new GameProcessMonitor();
        _transactionsRoot = Path.GetFullPath(transactionsRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "transactions"));
        _recoveryService = new TransactionalDeploymentService(_processMonitor, _transactionsRoot);
    }

    public async Task<DeploymentResult> RemovePackagesAsync(
        GameInstallation installation,
        IReadOnlyCollection<ImportedPackage> packages,
        CancellationToken cancellationToken = default)
    {
        await RemovalLock.WaitAsync(cancellationToken);
        try
        {
            _processMonitor.EnsureConfigurationUnlocked();
            var gameRoot = ValidateGameRoot(installation.RootPath);
            if (!Path.GetFullPath(installation.ExecutablePath).Equals(
                    Path.Combine(gameRoot, "GTA5.exe"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The selected game executable does not match the installation root.");

            if (packages is null || packages.Count == 0)
                throw new InvalidOperationException("Select at least one package to remove.");

            var candidates = new List<RemovalCandidate>();
            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var package in packages)
            {
                if (!PackageIdValidator.IsValid(package.Manifest.Id))
                    throw new InvalidDataException("A selected package has an invalid ID.");

                foreach (var file in package.Manifest.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relativePath = PackageArchiveService.ValidateArchivePath(
                        file.DestinationPath, isDirectory: false);

                    if (Path.GetFileName(relativePath).Equals("GTA5.exe", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetExtension(relativePath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Executable removal is blocked.");

                    if (!destinations.Add(relativePath))
                        throw new InvalidDataException("The selected packages contain duplicate destination paths.");

                    var destination = ResolveContainedPath(gameRoot, relativePath);
                    EnsureNoReparsePoints(gameRoot, destination);

                    if (!File.Exists(destination))
                        continue;

                    if (string.IsNullOrWhiteSpace(file.Sha256) || file.Sha256.Length != 64 ||
                        !file.Sha256.All(Uri.IsHexDigit))
                        throw new InvalidDataException("A package manifest contains an invalid SHA-256 digest.");

                    var actualHash = await ComputeSha256Async(destination, cancellationToken);
                    if (!actualHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new IOException(
                            "The game file no longer matches the selected package and was not removed: " + relativePath);

                    var owners = await GetActiveDeploymentOwnersAsync(
                        package.Manifest.Id, relativePath, file.Sha256, cancellationToken);
                    if (owners.Count == 0)
                        throw new InvalidOperationException(
                            "The Hub has no active deployment record for " + relativePath +
                            ". It will not remove a file it cannot prove it currently manages.");

                    candidates.Add(new RemovalCandidate(
                        package.Manifest.Id, relativePath, file.Sha256, destination, owners));
                }
            }

            if (candidates.Count == 0)
                return new DeploymentResult(string.Empty, 0, string.Empty);

            Directory.CreateDirectory(_transactionsRoot);
            var transactionId = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ") +
                                "-" + Guid.NewGuid().ToString("N");
            var transactionRoot = Path.Combine(_transactionsRoot, transactionId);
            var backupRoot = Path.Combine(transactionRoot, "backup");
            Directory.CreateDirectory(backupRoot);

            var journal = new DeploymentJournal(transactionId, gameRoot, "Applying", Array.Empty<DeploymentJournalEntry>());
            var journalPath = Path.Combine(transactionRoot, "journal.json");
            await SaveJournalAsync(journalPath, journal, cancellationToken);

            try
            {
                for (var index = 0; index < candidates.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _processMonitor.EnsureConfigurationUnlocked();

                    var candidate = candidates[index];
                    EnsureNoReparsePoints(gameRoot, candidate.FullPath);
                    if (!File.Exists(candidate.FullPath))
                        continue;

                    var currentHash = await ComputeSha256Async(candidate.FullPath, cancellationToken);
                    if (!currentHash.Equals(candidate.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                        throw new IOException(
                            "A game file changed during removal and was preserved: " + candidate.RelativePath);

                    var backupRelative = index.ToString("D6") + ".bak";
                    var backupPath = Path.Combine(backupRoot, backupRelative);
                    File.Copy(candidate.FullPath, backupPath, overwrite: false);

                    var backupHash = await ComputeSha256Async(backupPath, cancellationToken);
                    if (!backupHash.Equals(candidate.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("A removal backup failed SHA-256 verification.");

                    var removalMarker = "removed:" + candidate.PackageId + ":" +
                                        string.Join(",", candidate.OwnerTransactionIds);
                    var entry = new DeploymentJournalEntry(
                        removalMarker,
                        candidate.RelativePath,
                        true,
                        backupRelative,
                        candidate.ExpectedSha256,
                        currentHash);
                    journal = journal with { Entries = journal.Entries.Append(entry).ToArray() };
                    await SaveJournalAsync(journalPath, journal, cancellationToken);

                    File.Delete(candidate.FullPath);
                }

                journal = journal with { State = "Applied" };
                await SaveJournalAsync(journalPath, journal, cancellationToken);
                return new DeploymentResult(transactionId, journal.Entries.Count, transactionRoot);
            }
            catch (Exception removalError)
            {
                try
                {
                    await _recoveryService.RecoverInterruptedTransactionsAsync(CancellationToken.None);
                }
                catch (Exception recoveryError)
                {
                    throw new AggregateException(
                        "Package removal failed and automatic recovery was incomplete. Preserve the transaction folder.",
                        removalError, recoveryError);
                }

                throw new InvalidOperationException(
                    "Package removal failed. Changes from this transaction were rolled back.", removalError);
            }
        }
        finally
        {
            RemovalLock.Release();
        }
    }

    private async Task<IReadOnlyList<string>> GetActiveDeploymentOwnersAsync(
        string packageId,
        string relativePath,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_transactionsRoot))
            return Array.Empty<string>();

        var activeOwners = new List<string>();
        foreach (var transactionRoot in Directory.EnumerateDirectories(_transactionsRoot)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transactionId = Path.GetFileName(transactionRoot);
            var journalPath = Path.Combine(transactionRoot, "journal.json");
            if (!File.Exists(journalPath))
                continue;

            DeploymentJournal? journal;
            try
            {
                journal = JsonSerializer.Deserialize<DeploymentJournal>(
                    await File.ReadAllTextAsync(journalPath, cancellationToken));
            }
            catch (JsonException)
            {
                continue;
            }

            if (journal is null ||
                !string.Equals(journal.TransactionId, transactionId, StringComparison.Ordinal))
                continue;

            foreach (var entry in journal.Entries.Where(entry =>
                         entry.DestinationRelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase) &&
                         entry.DeployedSha256.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)))
            {
                if (entry.PackageId.StartsWith("removed:", StringComparison.OrdinalIgnoreCase))
                {
                    var marker = entry.PackageId.Split(':', 3);
                    if (marker.Length != 3 ||
                        !marker[1].Equals(packageId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var owners = marker[2].Split(',', StringSplitOptions.RemoveEmptyEntries);
                    if (journal.State.Equals("Applied", StringComparison.Ordinal))
                    {
                        foreach (var owner in owners)
                            activeOwners.RemoveAll(value => value.Equals(owner, StringComparison.Ordinal));
                    }
                    else if (journal.State.Equals("RolledBack", StringComparison.Ordinal))
                    {
                        foreach (var owner in owners)
                        {
                            if (!activeOwners.Contains(owner, StringComparer.Ordinal))
                                activeOwners.Add(owner);
                        }
                    }
                }
                else if (entry.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase))
                {
                    if (journal.State.Equals("Applied", StringComparison.Ordinal))
                    {
                        if (!activeOwners.Contains(journal.TransactionId, StringComparer.Ordinal))
                            activeOwners.Add(journal.TransactionId);
                    }
                    else if (journal.State.Equals("RolledBack", StringComparison.Ordinal))
                    {
                        activeOwners.RemoveAll(value =>
                            value.Equals(journal.TransactionId, StringComparison.Ordinal));
                    }
                }
            }
        }

        return activeOwners;
    }

    private static string ValidateGameRoot(string gameRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
        if (!Directory.Exists(root) || !File.Exists(Path.Combine(root, "GTA5.exe")))
            throw new InvalidOperationException("The selected folder is not a valid GTA V installation root.");
        return root;
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        var safeRelative = PackageArchiveService.ValidateArchivePath(relativePath, isDirectory: false);
        var full = Path.GetFullPath(Path.Combine(root, safeRelative));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A removal path escapes the game installation.");
        return full;
    }

    private static void EnsureNoReparsePoints(string root, string destination)
    {
        var rootInfo = new DirectoryInfo(root);
        for (var ancestor = rootInfo; ancestor is not null; ancestor = ancestor.Parent)
        {
            if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Removal through a reparse-point directory is blocked.");
        }

        var relativeDirectory = Path.GetRelativePath(root, Path.GetDirectoryName(destination)!);
        var current = root;
        foreach (var segment in relativeDirectory.Split(
                     Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Removal through a reparse-point directory is blocked.");
        }

        if (File.Exists(destination) &&
            (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Removal of a reparse-point file is blocked.");
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static async Task SaveJournalAsync(
        string path, DeploymentJournal journal, CancellationToken cancellationToken)
    {
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, journal, cancellationToken: cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
