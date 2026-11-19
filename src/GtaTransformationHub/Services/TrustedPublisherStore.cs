using System.Security.Cryptography;
using System.Text.Json;

namespace GtaTransformationHub.Services;

public sealed record TrustedPublisher(
    string Fingerprint,
    string Name,
    string PublicKeyPem,
    DateTimeOffset TrustedAt);

public sealed class TrustedPublisherStore
{
    private sealed record TrustStoreDocument(int SchemaVersion, IReadOnlyList<TrustedPublisher> Publishers);

    private readonly string _storePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public TrustedPublisherStore(string? storePath = null)
    {
        _storePath = Path.GetFullPath(storePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "trusted-publishers.json"));
    }

    public async Task<IReadOnlyList<TrustedPublisher>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadWithoutLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TrustedPublisher?> FindAsync(
        string fingerprint,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            return null;

        var normalized = NormalizeFingerprint(fingerprint);
        var publishers = await ListAsync(cancellationToken);
        return publishers.FirstOrDefault(publisher =>
            publisher.Fingerprint.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds a public key only when the caller supplies the fingerprint obtained
    /// independently from the publisher's official channel.
    /// </summary>
    public async Task<TrustedPublisher> AddAsync(
        string name,
        string publicKeyPem,
        string independentlyVerifiedFingerprint,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
            throw new ArgumentException("Enter a publisher name up to 120 characters.", nameof(name));

        var key = RsaPublicKey.Import(publicKeyPem);
        string fingerprint;
        using (key)
            fingerprint = RsaPublicKey.ComputeFingerprint(key);

        var expected = NormalizeFingerprint(independentlyVerifiedFingerprint);
        if (!fingerprint.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException(
                "The public-key fingerprint does not match the independently verified fingerprint.");

        var publisher = new TrustedPublisher(
            fingerprint,
            name.Trim(),
            publicKeyPem.Trim(),
            DateTimeOffset.UtcNow);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var publishers = (await LoadWithoutLockAsync(cancellationToken)).ToList();
            if (publishers.Any(item =>
                    item.Fingerprint.Equals(fingerprint, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("This publisher key is already trusted.");

            publishers.Add(publisher);
            await WriteWithoutLockAsync(publishers, cancellationToken);
            return publisher;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RemoveAsync(
        string fingerprint,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeFingerprint(fingerprint);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var publishers = await LoadWithoutLockAsync(cancellationToken);
            var remaining = publishers
                .Where(item => !item.Fingerprint.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (remaining.Length == publishers.Count)
                return false;

            await WriteWithoutLockAsync(remaining, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<TrustedPublisher>> LoadWithoutLockAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_storePath))
            return Array.Empty<TrustedPublisher>();

        var info = new FileInfo(_storePath);
        if (info.Length <= 0 || info.Length > 1024 * 1024)
            throw new InvalidDataException("The trusted-publisher store has an invalid size.");

        await using var stream = new FileStream(_storePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var document = await JsonSerializer.DeserializeAsync<TrustStoreDocument>(
            stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The trusted-publisher store is empty or invalid.");

        if (document.SchemaVersion != 1 || document.Publishers is null)
            throw new InvalidDataException("The trusted-publisher store has an unsupported schema.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var publisher in document.Publishers)
        {
            if (publisher is null ||
                string.IsNullOrWhiteSpace(publisher.Name) ||
                publisher.Name.Length > 120 ||
                string.IsNullOrWhiteSpace(publisher.Fingerprint) ||
                string.IsNullOrWhiteSpace(publisher.PublicKeyPem) ||
                !seen.Add(NormalizeFingerprint(publisher.Fingerprint)))
                throw new InvalidDataException("The trusted-publisher store contains invalid or duplicate entries.");

            using var key = RsaPublicKey.Import(publisher.PublicKeyPem);
            var computed = RsaPublicKey.ComputeFingerprint(key);
            if (!computed.Equals(publisher.Fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A trusted publisher key does not match its stored fingerprint.");
        }

        return document.Publishers;
    }

    private async Task WriteWithoutLockAsync(
        IReadOnlyCollection<TrustedPublisher> publishers,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_storePath)!;
        Directory.CreateDirectory(directory);
        var tempPath = _storePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new TrustStoreDocument(1, publishers.OrderBy(
                        item => item.Fingerprint, StringComparer.OrdinalIgnoreCase).ToArray()),
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, _storePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static string NormalizeFingerprint(string fingerprint)
    {
        var normalized = fingerprint.Trim().Replace(":", string.Empty, StringComparison.Ordinal);
        if (normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
            throw new ArgumentException("A publisher fingerprint must contain exactly 64 hexadecimal characters.");
        return normalized.ToUpperInvariant();
    }
}

internal static class RsaPublicKey
{
    private const string PemBegin = "-----BEGIN PUBLIC KEY-----";
    private const string PemEnd = "-----END PUBLIC KEY-----";

    public static RSA Import(string publicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(publicKeyPem))
            throw new InvalidDataException("A public RSA key is required.");

        var value = publicKeyPem.Trim();
        if (!value.StartsWith(PemBegin, StringComparison.Ordinal) ||
            !value.EndsWith(PemEnd, StringComparison.Ordinal))
            throw new InvalidDataException("Only an X.509 SubjectPublicKeyInfo PEM public key is accepted.");

        var encoded = value[ PemBegin.Length..^PemEnd.Length ];
        encoded = string.Concat(encoded.Where(character => !char.IsWhiteSpace(character)));

        byte[] der;
        try
        {
            der = Convert.FromBase64String(encoded);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The public-key PEM is not valid base64.", exception);
        }

        var rsa = RSA.Create();
        try
        {
            rsa.ImportSubjectPublicKeyInfo(der, out var bytesRead);
            if (bytesRead != der.Length || rsa.KeySize < 2048)
                throw new InvalidDataException("The publisher RSA key is incomplete or smaller than 2048 bits.");

            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    public static string ComputeFingerprint(RSA key) =>
        Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));
}
