using System.Security.Cryptography;
using System.Text.Json;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class TransactionalDeploymentServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GtaHubTests", Guid.NewGuid().ToString("N"));
    private readonly string _gameRoot;
    private readonly string _packageRoot;
    private readonly string _transactionRoot;

    public TransactionalDeploymentServiceTests()
    {
        _gameRoot = Path.Combine(_root, "game");
        _packageRoot = Path.Combine(_root, "package");
        _transactionRoot = Path.Combine(_root, "transactions");
        Directory.CreateDirectory(_gameRoot);
        Directory.CreateDirectory(_packageRoot);
        File.WriteAllText(Path.Combine(_gameRoot, "GTA5.exe"), "test executable marker");
    }

    [Fact]
    public async Task ApplyAndRollback_RestoresOriginalFile()
    {
        var destination = Path.Combine(_gameRoot, "update.dat");
        File.WriteAllText(destination, "original");
        var source = CreatePackageFile("update.dat", "replacement");
        var service = new TransactionalDeploymentService(dataRoot: _transactionRoot);

        var result = await service.ApplyAsync(_gameRoot, _packageRoot, new[]
        {
            Entry("update.dat", source)
        });

        Assert.Equal("replacement", await File.ReadAllTextAsync(destination));
        await service.RollbackLatestAsync(result.TransactionId);
        Assert.Equal("original", await File.ReadAllTextAsync(destination));
    }

    [Fact]
    public async Task Apply_NewFile_RollbackRemovesOnlyTheDeployedFile()
    {
        var source = CreatePackageFile("newfile.dat", "managed content");
        var service = new TransactionalDeploymentService(dataRoot: _transactionRoot);

        var result = await service.ApplyAsync(_gameRoot, _packageRoot, new[]
        {
            Entry("newfile.dat", source)
        });

        var destination = Path.Combine(_gameRoot, "newfile.dat");
        Assert.True(File.Exists(destination));
        await service.RollbackLatestAsync(result.TransactionId);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task RecoverInterruptedTransactions_RollsBackPartiallyAppliedJournal()
    {
        const string transactionId = "interrupted-transaction";
        var transactionDirectory = Path.Combine(_transactionRoot, transactionId);
        var backupDirectory = Path.Combine(transactionDirectory, "backup");
        Directory.CreateDirectory(backupDirectory);

        var destination = Path.Combine(_gameRoot, "update.dat");
        await File.WriteAllTextAsync(destination, "replacement already copied");
        var backupPath = Path.Combine(backupDirectory, "000000.bak");
        await File.WriteAllTextAsync(backupPath, "original");

        var deployedHash = Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(destination)));
        var originalHash = Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(backupPath)));
        var journal = new DeploymentJournal(
            transactionId,
            _gameRoot,
            "Applying",
            new[]
            {
                new DeploymentJournalEntry(
                    "test.package",
                    "update.dat",
                    true,
                    "000000.bak",
                    deployedHash,
                    originalHash)
            });
        await File.WriteAllTextAsync(
            Path.Combine(transactionDirectory, "journal.json"),
            JsonSerializer.Serialize(journal));

        var service = new TransactionalDeploymentService(dataRoot: _transactionRoot);
        var recovered = await service.RecoverInterruptedTransactionsAsync();

        Assert.Contains(transactionId, recovered);
        Assert.Equal("original", await File.ReadAllTextAsync(destination));
    }

    [Fact]
    public async Task Apply_RejectsHashMismatch_BeforeChangingGameFiles()
    {
        var destination = Path.Combine(_gameRoot, "update.dat");
        File.WriteAllText(destination, "original");
        var source = CreatePackageFile("update.dat", "replacement");
        var service = new TransactionalDeploymentService(dataRoot: _transactionRoot);
        var entry = Entry("update.dat", source) with { ExpectedSha256 = new string('0', 64) };

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ApplyAsync(_gameRoot, _packageRoot, new[] { entry }));
        Assert.Equal("original", await File.ReadAllTextAsync(destination));
    }

    [Fact]
    public async Task Apply_RejectsPathTraversal()
    {
        var source = CreatePackageFile("escape.dat", "payload");
        var service = new TransactionalDeploymentService(dataRoot: _transactionRoot);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ApplyAsync(_gameRoot, _packageRoot, new[]
            {
                Entry("../outside.dat", source)
            }));
        Assert.False(File.Exists(Path.Combine(_root, "outside.dat")));
    }

    [Fact]
    public async Task Rollback_ProtectsFilesChangedAfterDeployment()
    {
        var source = CreatePackageFile("managed.dat", "installed version");
        var service = new TransactionalDeploymentService(dataRoot: _transactionRoot);
        var result = await service.ApplyAsync(_gameRoot, _packageRoot, new[]
        {
            Entry("managed.dat", source)
        });

        var destination = Path.Combine(_gameRoot, "managed.dat");
        await File.WriteAllTextAsync(destination, "user modification");

        await Assert.ThrowsAsync<IOException>(() => service.RollbackLatestAsync(result.TransactionId));
        Assert.Equal("user modification", await File.ReadAllTextAsync(destination));
    }

    private string CreatePackageFile(string name, string contents)
    {
        var path = Path.Combine(_packageRoot, name);
        File.WriteAllText(path, contents);
        return name;
    }

    private DeploymentFile Entry(string destination, string sourceRelativePath)
    {
        var bytes = File.ReadAllBytes(Path.Combine(_packageRoot, sourceRelativePath));
        return new DeploymentFile(
            "test.package",
            sourceRelativePath,
            destination,
            Convert.ToHexString(SHA256.HashData(bytes)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
