namespace FC.Engine.Domain.Abstractions;

public interface IDigitalSignatureService
{
    Task<DigitalSignatureResult> SignPackageAsync(
        byte[] packageBytes, string regulatorCode, CancellationToken ct = default);

    Task<bool> VerifySignatureAsync(
        byte[] packageBytes, byte[] signature, string certificateThumbprint,
        CancellationToken ct = default);
}

public class DigitalSignatureResult
{
    public bool Success { get; set; }
    public byte[] Signature { get; set; } = Array.Empty<byte>();
    public string Algorithm { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public string CertificateThumbprint { get; set; } = string.Empty;
    public DateTime SignedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
