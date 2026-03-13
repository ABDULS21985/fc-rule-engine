using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using FC.Engine.Domain.Models.BatchSubmission;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure.Export;

/// <summary>
/// Signs submission payloads using institution X.509 certificate (RSA-SHA512).
/// Optionally stamps with RFC 3161 token from a configured TSA.
/// Certificate is loaded from the path configured in RegulatoryApiSettings —
/// in production this path points to a file sourced from Azure Key Vault.
/// </summary>
public sealed class BatchSubmissionSigningService : ISubmissionSigningService
{
    private readonly RegulatoryApiSettings _settings;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<BatchSubmissionSigningService> _logger;

    // Thread-safe cert cache: avoid re-reading disk on every request
    private X509Certificate2? _certCache;
    private readonly SemaphoreSlim _certLock = new(1, 1);

    public BatchSubmissionSigningService(
        IOptions<RegulatoryApiSettings> options,
        IHttpClientFactory httpFactory,
        ILogger<BatchSubmissionSigningService> logger)
    {
        _settings = options.Value;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<BatchSignatureInfo> SignPayloadAsync(
        int institutionId,
        byte[] payloadContent,
        CancellationToken ct = default)
    {
        var cert = await LoadCertificateAsync(ct);
        var rsa = cert.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("Certificate does not contain an RSA private key.");

        // Compute SHA-512 of payload
        var payloadHash = SHA512.HashData(payloadContent);
        var hashHex = Convert.ToHexString(payloadHash).ToLowerInvariant();

        // Sign the hash (PKCS#1 v1.5 padding)
        var signatureBytes = rsa.SignHash(payloadHash, HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1);

        var thumbprint = Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256)).ToLowerInvariant();
        var signedAt = DateTimeOffset.UtcNow;

        // RFC 3161 timestamp (optional — only if TSA is configured)
        byte[]? tsaToken = null;
        if (!string.IsNullOrWhiteSpace(_settings.DigitalSignature.TsaUrl))
        {
            tsaToken = await RequestTsaTokenAsync(signatureBytes, ct);
        }

        _logger.LogInformation(
            "Payload signed for institution {InstitutionId}: thumbprint={Thumbprint}, algo=RSA-SHA512",
            institutionId, thumbprint);

        return new BatchSignatureInfo(
            CertificateThumbprint: thumbprint,
            SignatureAlgorithm: "RSA-SHA512",
            SignatureValue: signatureBytes,
            SignedDataHash: hashHex,
            SignedAt: signedAt,
            TimestampToken: tsaToken);
    }

    public Task<bool> VerifySignatureAsync(
        BatchSignatureInfo signature,
        byte[] payloadContent,
        CancellationToken ct = default)
    {
        try
        {
            var certPath = _settings.DigitalSignature.CertificatePath;
            if (string.IsNullOrWhiteSpace(certPath))
                return Task.FromResult(false);

            using var cert = LoadCertificateFromPath(
                certPath,
                _settings.DigitalSignature.CertificatePassword,
                X509KeyStorageFlags.DefaultKeySet);
            var rsa = cert.GetRSAPublicKey();
            if (rsa is null) return Task.FromResult(false);

            var payloadHash = SHA512.HashData(payloadContent);
            var valid = rsa.VerifyHash(
                payloadHash, signature.SignatureValue,
                HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1);

            return Task.FromResult(valid);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Signature verification failed");
            return Task.FromResult(false);
        }
    }

    // ── Private helpers ─────────────────────────────────────────────────

    private async Task<X509Certificate2> LoadCertificateAsync(CancellationToken ct)
    {
        if (_certCache is not null)
            return _certCache;

        await _certLock.WaitAsync(ct);
        try
        {
            if (_certCache is not null)
                return _certCache;

            var path = _settings.DigitalSignature.CertificatePath;
            var password = _settings.DigitalSignature.CertificatePassword;

            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("DigitalSignature:CertificatePath is not configured.");

            _certCache = LoadCertificateFromPath(path, password, X509KeyStorageFlags.Exportable);

            return _certCache;
        }
        finally
        {
            _certLock.Release();
        }
    }

    private static X509Certificate2 LoadCertificateFromPath(
        string path,
        string? password,
        X509KeyStorageFlags keyStorageFlags)
    {
        var isPkcs12 = path.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".p12", StringComparison.OrdinalIgnoreCase);

        if (!isPkcs12 && string.IsNullOrWhiteSpace(password))
        {
            return X509CertificateLoader.LoadCertificateFromFile(path);
        }

        return X509CertificateLoader.LoadPkcs12FromFile(
            path,
            password ?? string.Empty,
            keyStorageFlags,
            loaderLimits: null);
    }

    /// <summary>
    /// Requests an RFC 3161 timestamp token from the configured TSA.
    /// The token covers the signature bytes (not the raw payload), matching
    /// the non-repudiation requirement in R-07.
    /// </summary>
    private async Task<byte[]?> RequestTsaTokenAsync(byte[] signatureBytes, CancellationToken ct)
    {
        try
        {
            // Build RFC 3161 TimeStampReq as simple DER structure
            var sigHash = SHA256.HashData(signatureBytes);
            var tsReq = BuildTsaRequest(sigHash);

            var client = _httpFactory.CreateClient("TsaClient");
            using var content = new ByteArrayContent(tsReq);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");

            var response = await client.PostAsync(_settings.DigitalSignature.TsaUrl, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TSA request failed with {StatusCode}", (int)response.StatusCode);
                return null;
            }

            var token = await response.Content.ReadAsByteArrayAsync(ct);
            _logger.LogDebug("RFC 3161 timestamp token received ({Bytes} bytes)", token.Length);
            return token;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RFC 3161 timestamping failed — submission will proceed without timestamp");
            return null;
        }
    }

    /// <summary>
    /// Builds a minimal DER-encoded RFC 3161 TimeStampRequest for the given hash.
    /// Uses SHA-256 OID (2.16.840.1.101.3.4.2.1) and a random nonce.
    /// </summary>
    private static byte[] BuildTsaRequest(byte[] hashValue)
    {
        // DER encoding of TimeStampReq per RFC 3161 §2.4.1
        // SEQUENCE {
        //   INTEGER 1                         -- version
        //   SEQUENCE {                        -- messageImprint
        //     SEQUENCE { OID sha256, NULL }   -- hashAlgorithm
        //     OCTET STRING hashValue          -- hashedMessage
        //   }
        //   INTEGER nonce                     -- nonce
        //   BOOLEAN TRUE                      -- certReq
        // }

        var nonce = RandomNumberGenerator.GetBytes(8);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // We write a simplified but spec-compliant request.
        // The SHA-256 OID in DER bytes:
        byte[] sha256OidDer = [0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x02, 0x01];

        var hashImprint = BuildDerSequence([
            BuildDerSequence([sha256OidDer, [0x05, 0x00]]),   // algorithm + NULL
            BuildDerOctetString(hashValue)
        ]);

        var version = new byte[] { 0x02, 0x01, 0x01 };        // INTEGER 1
        var nonceField = BuildDerInteger(nonce);
        var certReq = new byte[] { 0x01, 0x01, 0xFF };        // BOOLEAN TRUE

        return BuildDerSequence([version, hashImprint, nonceField, certReq]);
    }

    private static byte[] BuildDerSequence(IEnumerable<byte[]> children)
    {
        var body = children.SelectMany(c => c).ToArray();
        return [0x30, .. DerLength(body.Length), .. body];
    }

    private static byte[] BuildDerOctetString(byte[] data)
        => [0x04, .. DerLength(data.Length), .. data];

    private static byte[] BuildDerInteger(byte[] bytes)
    {
        // Prepend 0x00 if high bit is set to avoid negative interpretation
        var content = bytes[0] >= 0x80 ? [0x00, .. bytes] : bytes;
        return [0x02, .. DerLength(content.Length), .. content];
    }

    private static byte[] DerLength(int length)
    {
        if (length < 0x80)
            return [(byte)length];
        if (length <= 0xFF)
            return [0x81, (byte)length];
        return [0x82, (byte)(length >> 8), (byte)(length & 0xFF)];
    }
}
