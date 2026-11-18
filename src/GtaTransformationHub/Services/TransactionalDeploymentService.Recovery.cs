using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GtaTransformationHub.Services;

public sealed partial class TransactionalDeploymentService
{
    /// <summary>
    /// Recovers one in-flight transaction when the caller already owns
    /// GameMutationGate.Gate. This avoids recursively acquiring the shared
    /// semaphore from package-removal error handling.
    /// </summary>
    internal async Task RecoverTransactionWhileMutationGateHeldAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        _processMonitor.EnsureConfigurationUnlocked();

        if (string.IsNullOrWhiteSpace(transactionId) ||
            transactionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            transactionId.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Invalid transaction identifier.", nameof(transactionId));

        var transactionRoot = Path.Combine(_dataRoot, transactionId);
        var journalPath = Path.Combine(transactionRoot, "journal.json");
        if (!File.Exists(journalPath))
            throw new FileNotFoundException("The transaction journal was not found.", journalPath);

        var journal = JsonSerializer.Deserialize<DeploymentJournal>(
            await File.ReadAllTextAsync(journalPath, cancellationToken))
            ?? throw new InvalidDataException("The transaction journal is invalid.");

        ValidateJournal(journal, transactionId);
        if (!string.Equals(journal.State, "Applying", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Only an interrupted Applying transaction can be recovered through this path.");

        await RollbackJournalAsync(
            journal,
            Path.Combine(transactionRoot, "backup"),
            journalPath,
            cancellationToken);
    }
}
