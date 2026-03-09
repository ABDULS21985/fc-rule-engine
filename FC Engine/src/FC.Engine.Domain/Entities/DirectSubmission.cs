using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Entities;

public class DirectSubmission
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public int SubmissionId { get; set; }
    public string RegulatorCode { get; set; } = string.Empty;
    public SubmissionChannel Channel { get; set; } = SubmissionChannel.DirectApi;
    public DirectSubmissionStatus Status { get; set; } = DirectSubmissionStatus.Pending;

    // Digital signature
    public string? SignatureAlgorithm { get; set; }
    public string? SignatureHash { get; set; }
    public string? CertificateThumbprint { get; set; }
    public DateTime? SignedAt { get; set; }

    // Submission tracking
    public string? RegulatorReference { get; set; }
    public string? RegulatorResponseBody { get; set; }
    public int? HttpStatusCode { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public DateTime? NextRetryAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public string? ErrorMessage { get; set; }

    // Package info
    public string? PackageStoragePath { get; set; }
    public long? PackageSizeBytes { get; set; }
    public string? PackageSha256 { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;

    // Navigation
    public Submission? Submission { get; set; }
}
