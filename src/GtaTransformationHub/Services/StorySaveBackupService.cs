using System.IO.Compression;
using System.Security.Cryptography;

namespace GtaTransformationHub.Services;

public sealed record StorySaveBackup(
    string ArchivePath,
    int FileCount,
    long UncompressedBytes,
    string Sha256);

/// <summary>
/// Creates a portable, read-only copy of a user-selected GTA V Story Mode Profiles
/// directory before story/mission package testing. It never edits or deletes saves.
/// </summary>
public sealed class StorySaveBackupService
{
    private const int MaximumFiles = 100000;
    private const long MaximumTotalBytes = 10L * 1024 * 1024 * 1024;

    private readonly string _backupRoot;

    public StorySaveBackupService(string? backupRoot = null)
    {
        _backupRoot = Path.GetFullPath(backupRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "story-save-backups"));
    }

    public async Task<StorySaveBackup> CreateBackupAsync(
        string profilesDirectory,
        CancellationToken cancellationToken = default)
    {
        var sourceRoot = Path.GetFullPath(profilesDirectory);
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException("Select the GTA V Story Mode Profiles directory before creating a backup.");

        EnsureNoReparsePoint(sourceRoot);
        var files = EnumerateFilesWithoutLinks(sourceRoot);
        if (files.Count == 0)
            throw new InvalidDataException("The selected save directory contains no files.");
        if (files.Count > MaximumFiles)
            throw new InvalidDataException("The selected save directory contains too many files.");

        var totalBytes = files.Sum(file => new FileInfo(file).Length);
        if (totalBytes > MaximumTotalBytes)
            throw new InvalidDataException("The selected save directory exceeds the 10 GB backup limit.");

        Directory.CreateDirectory(_backupRoot);
        var archivePath = Path.Combine(
            _backupRoot, "story-saves-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" +
            Guid.NewGuid().ToString("N") + ".zip");
        var temporaryPath = archivePath + ".tmp";

        try
        {
            await using (var output = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureNoReparsePoint(file);
                    var relative = Path.GetRelativePath(sourceRoot, file)
                        .Replace('\\', '/');
                    _ = PackageArchiveService.ValidateArchivePath(relative, isDirectory: false);

                    var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                    await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                    await using var entryStream = entry.Open();
                    await input.CopyToAsync(entryStream, cancellationToken);
                }

                await output.FlushAsync(cancellationToken);
            }

            string hash;
            await using (var verify = new FileStream(
                temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                hash = Convert.ToHexString(await SHA256.HashDataAsync(verify, cancellationToken));
            }

            // Close the verification handle before renaming. Windows does not allow
            // moving the archive while the verification stream is still open.
            File.Move(temporaryPath, archivePath, overwrite: false);
            return new StorySaveBackup(archivePath, files.Count, totalBytes, hash);
        }
        catch
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            throw;
        }
    }

    private static List<string> EnumerateFilesWithoutLinks(string root)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("A symbolic link or reparse point was found in the save directory; backup was stopped.");

                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push(entry);
                else
                    files.Add(entry);

                if (files.Count > MaximumFiles)
                    throw new InvalidDataException("The selected save directory exceeds the file-count limit.");
            }
        }

        return files;
    }

    private static void EnsureNoReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Save backups do not follow symbolic links or reparse points.");
    }
}
