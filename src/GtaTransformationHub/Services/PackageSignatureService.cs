using System.Security.Cryptography;
using System.Text.Json;
using GtaTransformationHub.Models;

namespace GtaTransformationHub.Services;

public enum PackageSignatureStatus
{
    Unsigned,
    TrustedPublisher,
    UnknownPublisher,
    Invalid
}

public sealed record PackageSignatureVerification(
    PackageSignatureStatus Status,
    string? PublisherName,
    string? KeyFingerprint,
    string Message);

/// <summary>
/// Verifies RSA-SHA256 signatures over a deterministic package-manifest payload.
/// The signature authenticates the manifest and its file hashes, not the runtime
/// behaviour of third-party mod code.
/// </summary>
public sealed class PackageSignatureService
{
    private sealed record CanonicalManifest(
        int SchemaVersion,
        string Id,
        string Name,
        string Author,
        string SourceUri,
        string Licence,
        string Version,
        string GameTarget,
        string MinimumGameBuild,
        string MaximumGameBuild,
        IReadOnlyList<string> Dependencies,
        IReadOnlyList<string> Conflicts,
        string? ExclusiveGroup,
        string SigningKeyId,
        IReadOnlyList<CanonicalFile> Files);

    private sealed record CanonicalFile(
        string SourcePath,
        string DestinationPath,
        string Sha256,
        long SizeBytes);

    private readonly TrustedPublisherStore _trustedPublisherStore;

    public PackageSignatureService(TrustedPublisherStore? trustedPublisherStore = null)
    {
        _trustedPublisherStore = trustedPublisherStore ?? new TrustedPublisherStore();
    }

    public async Task<PackageSignatureVerification> VerifyAsync(
        PackageManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.Files is null || manifest.Dependencies is null || manifest.Conflicts is null ||
            manifest.Files.Any(file => file is null ||
                string.IsNullOrWhiteSpace(file.SourcePath) ||
                string.IsNullOrWhiteSpace(file.DestinationPath) ||
                string.IsNullOrWhiteSpace(file.Sha256)))
            return new PackageSignatureVerification(
                PackageSignatureStatus.Invalid, null, manifest.SigningKeyId,
                "The package manifest is incomplete and cannot be signature-verified.");

        var hasKeyId = !string.IsNullOrWhiteSpace(manifest.SigningKeyId);
        var hasSignature = !string.IsNullOrWhiteSpace(manifest.Signature);
        if (!hasKeyId && !hasSignature)
            return new PackageSignatureVerification(
                PackageSignatureStatus.Unsigned, null, null,
                "The package is unsigned. File hashes verify integrity but do not authenticate its publisher.");

        if (!hasKeyId || !hasSignature)
            return new PackageSignatureVerification(
                PackageSignatureStatus.Invalid, null, manifest.SigningKeyId,
                "A signed package must contain both signingKeyId and signature.");

        string normalizedFingerprint;
        try
        {
            normalizedFingerprint = NormalizeFingerprint(manifest.SigningKeyId!);
        }
        catch (ArgumentException exception)
        {
            return new PackageSignatureVerification(
                PackageSignatureStatus.Invalid, null, manifest.SigningKeyId, exception.Message);
        }

        var trustedPublisher = await _trustedPublisherStore.FindAsync(
            normalizedFingerprint, cancellationToken);

        if (trustedPublisher is null)
            return new PackageSignatureVerification(
                PackageSignatureStatus.UnknownPublisher, null, normalizedFingerprint,
                "The package is signed, but its publisher key is not trusted on this computer.");

        byte[] signature;
        try
        {
            if (manifest.Signature!.Length > 8192)
                throw new FormatException("The package signature is too large.");
            signature = Convert.FromBase64String(manifest.Signature);
        }
        catch (FormatException exception)
        {
            return new PackageSignatureVerification(
                PackageSignatureStatus.Invalid, trustedPublisher.Name, normalizedFingerprint,
                "The package signature is not valid base64: " + exception.Message);
        }

        try
        {
            using var key = RsaPublicKey.Import(trustedPublisher.PublicKeyPem);
            var payload = CreateSigningPayload(manifest);
            var valid = key.VerifyData(
                payload,
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            return valid
                ? new PackageSignatureVerification(
                    PackageSignatureStatus.TrustedPublisher,
                    trustedPublisher.Name,
                    normalizedFingerprint,
                    "The package manifest signature is valid and its publisher key is trusted.")
                : new PackageSignatureVerification(
                    PackageSignatureStatus.Invalid,
                    trustedPublisher.Name,
                    normalizedFingerprint,
                    "The package manifest signature does not match its declared contents.");
        }
        catch (Exception exception) when (
            exception is CryptographicException ||
            exception is InvalidDataException ||
            exception is ArgumentException)
        {
            return new PackageSignatureVerification(
                PackageSignatureStatus.Invalid,
                trustedPublisher.Name,
                normalizedFingerprint,
                "The package signature could not be verified: " + exception.Message);
        }
    }

    /// <summary>
    /// Returns the exact bytes publishers must sign with RSA-SHA256/PKCS#1 v1.5.
    /// The signature field itself is deliberately excluded.
    /// </summary>
    public static byte[] CreateSigningPayload(PackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(manifest.SigningKeyId))
            throw new InvalidDataException("A signing key fingerprint is required.");

        var fingerprint = NormalizeFingerprint(manifest.SigningKeyId);
        if (manifest.Files is null || manifest.Dependencies is null || manifest.Conflicts is null ||
            manifest.Files.Any(file => file is null ||
                string.IsNullOrWhiteSpace(file.SourcePath) ||
                string.IsNullOrWhiteSpace(file.DestinationPath) ||
                string.IsNullOrWhiteSpace(file.Sha256)))
            throw new InvalidDataException("A package manifest must contain complete file, dependency and conflict declarations.");

        var canonical = new CanonicalManifest(
            manifest.SchemaVersion,
            manifest.Id,
            manifest.Name,
            manifest.Author,
            manifest.SourceUri,
            manifest.Licence,
            manifest.Version,
            manifest.GameTarget,
            manifest.MinimumGameBuild,
            manifest.MaximumGameBuild,
            manifest.Dependencies.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            manifest.Conflicts.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            manifest.ExclusiveGroup,
            fingerprint,
            manifest.Files
                .OrderBy(file => file.SourcePath, StringComparer.Ordinal)
                .ThenBy(file => file.DestinationPath, StringComparer.Ordinal)
                .Select(file => new CanonicalFile(
                    file.SourcePath,
                    file.DestinationPath,
                    file.Sha256.ToUpperInvariant(),
                    file.SizeBytes))
                .ToArray());

        return JsonSerializer.SerializeToUtf8Bytes(canonical);
    }

    public static string ComputePublisherFingerprint(string publicKeyPem)
    {
        using var key = RsaPublicKey.Import(publicKeyPem);
        return RsaPublicKey.ComputeFingerprint(key);
    }

    private static string NormalizeFingerprint(string fingerprint)
    {
        var normalized = fingerprint.Trim().Replace(":", string.Empty, StringComparison.Ordinal);
        if (normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
            throw new ArgumentException("The signing key ID must be a 64-character SHA-256 public-key fingerprint.");
        return normalized.ToUpperInvariant();
    }
}
