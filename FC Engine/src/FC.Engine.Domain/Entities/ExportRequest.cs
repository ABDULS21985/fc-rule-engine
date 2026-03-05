using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Entities;

public class ExportRequest
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public int SubmissionId { get; set; }
    public ExportFormat Format { get; set; }
    public ExportRequestStatus Status { get; set; } = ExportRequestStatus.Queued;
    public int RequestedBy { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? FilePath { get; set; }
    public long? FileSize { get; set; }
    public string? Sha256Hash { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? ExpiresAt { get; set; }

    // Navigation
    public Submission? Submission { get; set; }
}
