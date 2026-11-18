using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace GtaTransformationHub.Services;

public sealed record AcquiredPackage(string FilePath, Uri SourceUri, string Sha256, long SizeBytes);

/// <summary>
/// Downloads a user-supplied direct package URL. DNS is revalidated at socket-connect
/// time to prevent a DNS-rebinding response from redirecting the connection to a private host.
/// </summary>
public sealed class PackageAcquisitionService : IDisposable
{
    private const long MaximumDownloadBytes = 2L * 1024 * 1024 * 1024;
    private const int MaximumRedirects = 5;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly string _downloadsRoot;

    public PackageAcquisitionService(string? downloadsRoot = null, HttpClient? httpClient = null)
    {
        _downloadsRoot = Path.GetFullPath(downloadsRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GtaTransformationHub", "downloads"));
        if (httpClient is null)
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                ConnectCallback = ConnectToPublicAddressAsync
            };
            _httpClient = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromMinutes(30) };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("GtaTransformationHub/1.0");
            _ownsClient = true;
        }
        else { _httpClient = httpClient; _ownsClient = false; }
    }

    public async Task<AcquiredPackage> DownloadAsync(Uri sourceUri, string? expectedSha256 = null, CancellationToken cancellationToken = default)
    {
        ValidateExpectedHash(expectedSha256);
        var currentUri = sourceUri ?? throw new ArgumentNullException(nameof(sourceUri));
        for (var redirect = 0; redirect <= MaximumRedirects; redirect++)
        {
            await ValidatePublicHttpsUriAsync(currentUri, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                if (redirect == MaximumRedirects || response.Headers.Location is null)
                    throw new InvalidDataException("The download exceeded the permitted redirect limit.");
                var nextUri = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(currentUri, response.Headers.Location);
                await ValidatePublicHttpsUriAsync(nextUri, cancellationToken);
                currentUri = nextUri;
                continue;
            }

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
                throw new InvalidDataException("The package download exceeds the 2 GB limit.");
            Directory.CreateDirectory(_downloadsRoot);
            var extension = GetPackageExtension(response.Content.Headers.ContentDisposition?.FileNameStar)
                ?? GetPackageExtension(response.Content.Headers.ContentDisposition?.FileName)
                ?? GetPackageExtension(currentUri.AbsolutePath)
                ?? ".zip";
            var destination = Path.Combine(_downloadsRoot, "package-" + Guid.NewGuid().ToString("N") + extension);
            var temporary = destination + ".download";
            try
            {
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long copied = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read == 0) break;
                    copied = checked(copied + read);
                    if (copied > MaximumDownloadBytes)
                        throw new InvalidDataException("The package download exceeded the 2 GB limit.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                if (copied == 0) throw new InvalidDataException("The package download was empty.");
                await output.FlushAsync(cancellationToken);
                var actualHash = Convert.ToHexString(hash.GetHashAndReset());
                if (!string.IsNullOrWhiteSpace(expectedSha256) && !actualHash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded package does not match the expected SHA-256 digest.");
                File.Move(temporary, destination, overwrite: false);
                return new AcquiredPackage(destination, currentUri, actualHash, copied);
            }
            catch
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                throw;
            }
        }
        throw new InvalidOperationException("The package download could not be completed.");
    }

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
            throw new InvalidDataException("The package source resolved to a private or reserved network address.");

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            var client = new TcpClient(address.AddressFamily);
            try
            {
                await client.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken);
                return client.GetStream();
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException)
            {
                client.Dispose();
                lastError = exception;
                if (exception is OperationCanceledException) throw;
            }
        }
        throw new IOException("Could not connect to the public package source.", lastError);
    }

    private static async Task ValidatePublicHttpsUriAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.Port != 443)
            throw new InvalidDataException("Package downloads require a direct HTTPS URL on the standard port, without embedded credentials.");
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Local network and localhost package sources are not allowed.");
        var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
            throw new InvalidDataException("The package source must resolve exclusively to public internet addresses.");
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var a = bytes[0]; var b = bytes[1]; var c = bytes[2];
            return !(a == 0 || a == 10 || a == 127 || (a == 169 && b == 254) ||
                (a == 172 && b >= 16 && b <= 31) || (a == 192 && b == 168) ||
                (a == 100 && b >= 64 && b <= 127) || (a == 192 && b == 0) ||
                (a == 198 && (b == 18 || b == 19 || b == 51 && c == 100)) ||
                (a == 203 && b == 0 && c == 113) || a >= 224);
        }
        if (address.AddressFamily != AddressFamily.InterNetworkV6 || bytes.Length != 16 ||
            (bytes[0] & 0xE0) != 0x20)
            return false;
        // Exclude documentation-only 2001:db8::/32.
        return !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8);
    }

    private static string? GetPackageExtension(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var extension = Path.GetExtension(value.Trim().Trim('"'));
        return extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".oiv", StringComparison.OrdinalIgnoreCase) ? extension.ToLowerInvariant() : null;
    }

    private static void ValidateExpectedHash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("An expected SHA-256 digest must contain exactly 64 hexadecimal characters.", nameof(value));
    }

    public void Dispose() { if (_ownsClient) _httpClient.Dispose(); }
}