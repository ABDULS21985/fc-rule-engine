using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure.Export;

public class X509DigitalSignatureService : IDigitalSignatureService
{
    private readonly RegulatoryApiSettings _settings;
    private readonly ILogger<X509DigitalSignatureService> _logger;
    private X509Certificate2? _cachedCertificate;

    public X509DigitalSignatureService(
        IOptions<RegulatoryApiSettings> options,
        ILogger<X509DigitalSignatureService> logger)
    {
        _settings = options.Value;
        _logger = logger;
    }

    public Task<DigitalSignatureResult> SignPackageAsync(
        byte[] packageBytes, string regulatorCode, CancellationToken ct = default)
    {
        try
        {
            var cert = LoadCertificate();
            if (cert is null)
            {
                return Task.FromResult(new DigitalSignatureResult
                {
                    Success = false,
                    ErrorMessage = "Digital signature certificate is not configured."
                });
            }

            using var rsa = cert.GetRSAPrivateKey()
                ?? throw new InvalidOperationException("Certificate does not contain an RSA private key.");

            var hash = SHA256.HashData(packageBytes);
            var signature = rsa.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            return Task.FromResult(new DigitalSignatureResult
            {
                Success = true,
                Signature = signature,
                Algorithm = "SHA256withRSA",
                Hash = Convert.ToHexStringLower(hash),
                CertificateThumbprint = cert.Thumbprint,
                SignedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sign package for regulator {Regulator}", regulatorCode);
            return Task.FromResult(new DigitalSignatureResult
            {
                Success = false,
                ErrorMessage = ex.Message
            });
        }
    }

    public Task<bool> VerifySignatureAsync(
        byte[] packageBytes, byte[] signature, string certificateThumbprint,
        CancellationToken ct = default)
    {
        try
        {
            var cert = LoadCertificate();
            if (cert is null)
            {
                return Task.FromResult(false);
            }

            using var rsa = cert.GetRSAPublicKey()
                ?? throw new InvalidOperationException("Certificate does not contain an RSA public key.");

            var hash = SHA256.HashData(packageBytes);
            var isValid = rsa.VerifyHash(hash, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return Task.FromResult(isValid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify signature for thumbprint {Thumbprint}", certificateThumbprint);
            return Task.FromResult(false);
        }
    }

    private X509Certificate2? LoadCertificate()
    {
        if (_cachedCertificate is not null)
        {
            return _cachedCertificate;
        }

        var certPath = _settings.DigitalSignature.CertificatePath;
        if (string.IsNullOrWhiteSpace(certPath))
        {
            return null;
        }

        if (!File.Exists(certPath))
        {
            _logger.LogWarning("Digital signature certificate file not found: {Path}", certPath);
            return null;
        }

        var password = _settings.DigitalSignature.CertificatePassword;
        _cachedCertificate = LoadCertificateFromPath(certPath, password, X509KeyStorageFlags.MachineKeySet);

        _logger.LogInformation("Loaded digital signature certificate: {Thumbprint}", _cachedCertificate.Thumbprint);
        return _cachedCertificate;
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
}
