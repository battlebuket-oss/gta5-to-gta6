using System.Security.Cryptography;
using GtaTransformationHub.Models;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class PackageSignatureServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "GtaHubSignatureTests", Guid.NewGuid().ToString("N"));
    private readonly string _trustPath;

    public PackageSignatureServiceTests()
    {
        Directory.CreateDirectory(_root);
        _trustPath = Path.Combine(_root, "trusted-publishers.json");
    }

    [Fact]
    public async Task VerifyAsync_AcceptsValidSignatureFromTrustedPublisher()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportSubjectPublicKeyInfoPem();
        var fingerprint = PackageSignatureService.ComputePublisherFingerprint(pem);
        var store = new TrustedPublisherStore(_trustPath);
        await store.AddAsync("Test Publisher", pem, fingerprint);
        var service = new PackageSignatureService(store);

        var manifest = Sign(CreateManifest(), rsa, fingerprint);
        var result = await service.VerifyAsync(manifest);

        Assert.Equal(PackageSignatureStatus.TrustedPublisher, result.Status);
        Assert.Equal("Test Publisher", result.PublisherName);
    }

    [Fact]
    public async Task VerifyAsync_RejectsManifestModifiedAfterSigning()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportSubjectPublicKeyInfoPem();
        var fingerprint = PackageSignatureService.ComputePublisherFingerprint(pem);
        var store = new TrustedPublisherStore(_trustPath);
        await store.AddAsync("Test Publisher", pem, fingerprint);
        var service = new PackageSignatureService(store);

        var signed = Sign(CreateManifest(), rsa, fingerprint);
        var tampered = signed with { Licence = "A different licence" };
        var result = await service.VerifyAsync(tampered);

        Assert.Equal(PackageSignatureStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task VerifyAsync_V2SignatureAuthenticatesStoryMetadata()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportSubjectPublicKeyInfoPem();
        var fingerprint = PackageSignatureService.ComputePublisherFingerprint(pem);
        var store = new TrustedPublisherStore(_trustPath);
        await store.AddAsync("Test Publisher", pem, fingerprint);
        var service = new PackageSignatureService(store);

        var manifest = CreateManifest() with
        {
            SignatureVersion = 2,
            ImpactAreas = new[] { "story" },
            StoryContent = CreateStoryContent()
        };
        var signed = Sign(manifest, rsa, fingerprint);
        var valid = await service.VerifyAsync(signed);
        Assert.Equal(PackageSignatureStatus.TrustedPublisher, valid.Status);

        var tampered = signed with
        {
            StoryContent = signed.StoryContent! with
            {
                ProtagonistRequirements = new[] { "Trevor" }
            }
        };
        var invalid = await service.VerifyAsync(tampered);
        Assert.Equal(PackageSignatureStatus.Invalid, invalid.Status);
    }

    [Fact]
    public async Task VerifyAsync_LegacySignatureCannotAuthenticateNewMetadata()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportSubjectPublicKeyInfoPem();
        var fingerprint = PackageSignatureService.ComputePublisherFingerprint(pem);
        var store = new TrustedPublisherStore(_trustPath);
        await store.AddAsync("Test Publisher", pem, fingerprint);
        var service = new PackageSignatureService(store);

        var legacySigned = Sign(CreateManifest(), rsa, fingerprint);
        var tampered = legacySigned with
        {
            ImpactAreas = new[] { "story" },
            StoryContent = CreateStoryContent()
        };

        var result = await service.VerifyAsync(tampered);
        Assert.Equal(PackageSignatureStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task VerifyAsync_V3SignatureAuthenticatesUiReplacementContract()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportSubjectPublicKeyInfoPem();
        var fingerprint = PackageSignatureService.ComputePublisherFingerprint(pem);
        var store = new TrustedPublisherStore(_trustPath);
        await store.AddAsync("Test Publisher", pem, fingerprint);
        var service = new PackageSignatureService(store);

        var manifest = CreateManifest() with
        {
            SignatureVersion = 3,
            ImpactAreas = new[] { "interface" },
            CompatibilityVerified = true,
            Ui = CreateUiDescriptor()
        };
        var signed = Sign(manifest, rsa, fingerprint);
        Assert.Equal(PackageSignatureStatus.TrustedPublisher, (await service.VerifyAsync(signed)).Status);

        var tampered = signed with
        {
            Ui = signed.Ui! with { Surfaces = new[] { "map" } }
        };
        Assert.Equal(PackageSignatureStatus.Invalid, (await service.VerifyAsync(tampered)).Status);
    }

    [Fact]
    public async Task VerifyAsync_V2SignatureCannotAuthenticateUiMetadata()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportSubjectPublicKeyInfoPem();
        var fingerprint = PackageSignatureService.ComputePublisherFingerprint(pem);
        var store = new TrustedPublisherStore(_trustPath);
        await store.AddAsync("Test Publisher", pem, fingerprint);
        var service = new PackageSignatureService(store);

        var v2 = CreateManifest() with
        {
            SignatureVersion = 2,
            ImpactAreas = new[] { "interface" },
            CompatibilityVerified = true
        };
        var signed = Sign(v2, rsa, fingerprint) with { Ui = CreateUiDescriptor() };
        Assert.Equal(PackageSignatureStatus.Invalid, (await service.VerifyAsync(signed)).Status);
    }

    [Fact]
    public async Task VerifyAsync_ReportsUnknownPublisherWithoutTrustingIt()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportSubjectPublicKeyInfoPem();
        var fingerprint = PackageSignatureService.ComputePublisherFingerprint(pem);
        var manifest = Sign(CreateManifest(), rsa, fingerprint);
        var service = new PackageSignatureService(new TrustedPublisherStore(_trustPath));

        var result = await service.VerifyAsync(manifest);
        Assert.Equal(PackageSignatureStatus.UnknownPublisher, result.Status);
    }

    [Fact]
    public async Task VerifyAsync_ReportsUnsignedManifest()
    {
        var service = new PackageSignatureService(new TrustedPublisherStore(_trustPath));
        var result = await service.VerifyAsync(CreateManifest());
        Assert.Equal(PackageSignatureStatus.Unsigned, result.Status);
    }

    [Fact]
    public async Task TrustedPublisherStore_RequiresMatchingIndependentFingerprint()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportSubjectPublicKeyInfoPem();
        var correct = PackageSignatureService.ComputePublisherFingerprint(pem);
        var wrong = new string('0', 64);
        var store = new TrustedPublisherStore(_trustPath);

        await Assert.ThrowsAsync<CryptographicException>(() => store.AddAsync("Publisher", pem, wrong));
        await store.AddAsync("Publisher", pem, correct);
        var publisher = Assert.Single(await store.ListAsync());
        Assert.Equal(correct, publisher.Fingerprint);
    }

    [Fact]
    public async Task RevokingPublisherKey_MakesPreviouslyTrustedSignatureUnknown()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportSubjectPublicKeyInfoPem();
        var fingerprint = PackageSignatureService.ComputePublisherFingerprint(pem);
        var store = new TrustedPublisherStore(_trustPath);
        await store.AddAsync("Test Publisher", pem, fingerprint);
        var service = new PackageSignatureService(store);
        var signed = Sign(CreateManifest(), rsa, fingerprint);

        Assert.Equal(PackageSignatureStatus.TrustedPublisher, (await service.VerifyAsync(signed)).Status);
        Assert.True(await store.RemoveAsync(fingerprint));
        Assert.Equal(PackageSignatureStatus.UnknownPublisher, (await service.VerifyAsync(signed)).Status);
    }

    private static StoryContentDescriptor CreateStoryContent() => new(
        "MissionPack",
        new[] { "mission-01" },
        new[] { "Any" },
        new[] { "downtown-bank" },
        "sample-story-progression",
        "SeparateNamespace",
        true,
        true,
        Array.Empty<string>(),
        new[] { "en-US" },
        "https://example.com/test-evidence");

    private static UiPackageDescriptor CreateUiDescriptor() => new(
        new[] { "hud", "radial" },
        "GameScript",
        new[] { "KeyboardMouse", "Controller" },
        new[] { "1920x1080", "2560x1440" },
        new[] { "KeyboardNavigation", "ControllerNavigation", "VisibleFocus", "HighContrast", "SubtitleSafeArea" },
        true,
        true,
        true,
        true,
        true,
        "https://example.com/ui-compatibility-evidence",
        "https://example.com/ui-rollback-evidence",
        "1.0.1000.0",
        new string('A', 64));

    private static PackageManifest Sign(PackageManifest manifest, RSA privateKey, string fingerprint)
    {
        var toSign = manifest with { SigningKeyId = fingerprint, Signature = null };
        var signature = privateKey.SignData(
            PackageSignatureService.CreateSigningPayload(toSign),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return toSign with { Signature = Convert.ToBase64String(signature) };
    }

    private static PackageManifest CreateManifest() => new(
        1,
        "sample-package",
        "Sample Package",
        "Test Author",
        "https://example.com/sample",
        "Test licence",
        "1.0.0",
        "GTA V Legacy",
        "1.0.1000.0",
        "1.0.9999.0",
        Array.Empty<string>(),
        Array.Empty<string>(),
        new[]
        {
            new PackageManifestFile("payload/sample.dat", "mods/sample.dat", new string('A', 64), 12)
        });

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
