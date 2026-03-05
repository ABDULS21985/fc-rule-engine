namespace FC.Engine.Domain.Entities;

/// <summary>
/// Immutable evidence package (ZIP) containing all artifacts
/// for a submission's audit trail.
/// </summary>
public class EvidencePackage
{
    public int Id { get; set; }

    /// <summary>FK to Tenant for RLS.</summary>
    public Guid TenantId { get; set; }

    public int SubmissionId { get; set; }

    /// <summary>SHA-256 hash of the ZIP file contents.</summary>
    public string PackageHash { get; set; } = string.Empty;

    public string StoragePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime GeneratedAt { get; set; }
    public string GeneratedBy { get; set; } = string.Empty;

    // Navigation
    public Submission? Submission { get; set; }
}
