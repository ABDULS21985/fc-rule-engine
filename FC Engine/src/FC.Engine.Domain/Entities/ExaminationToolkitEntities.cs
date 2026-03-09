using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Entities;

public class ExaminationFinding
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public int ProjectId { get; set; }
    public int? SubmissionId { get; set; }
    public int? InstitutionId { get; set; }
    public int? CarriedForwardFromFindingId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string RiskArea { get; set; } = string.Empty;
    public string Observation { get; set; } = string.Empty;
    public ExaminationRiskRating RiskRating { get; set; } = ExaminationRiskRating.Medium;
    public string Recommendation { get; set; } = string.Empty;
    public ExaminationWorkflowStatus Status { get; set; } = ExaminationWorkflowStatus.ToReview;
    public ExaminationRemediationStatus RemediationStatus { get; set; } = ExaminationRemediationStatus.Open;
    public string? ModuleCode { get; set; }
    public string? PeriodLabel { get; set; }
    public string? FieldCode { get; set; }
    public string? FieldValue { get; set; }
    public string? ValidationRuleId { get; set; }
    public string? ValidationMessage { get; set; }
    public string? EvidenceReference { get; set; }
    public DateTime? ManagementResponseDeadline { get; set; }
    public string? ManagementResponse { get; set; }
    public DateTime? ManagementResponseSubmittedAt { get; set; }
    public string? ManagementActionPlan { get; set; }
    public bool IsCarriedForward { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public string? EscalationReason { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public int? VerifiedBy { get; set; }
    public DateTime? ClosedAt { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ExaminationProject? Project { get; set; }
    public Submission? Submission { get; set; }
    public Institution? Institution { get; set; }
    public ExaminationFinding? CarriedForwardFromFinding { get; set; }
    public List<ExaminationFinding> CarriedForwardChildren { get; set; } = new();
    public List<ExaminationEvidenceRequest> EvidenceRequests { get; set; } = new();
    public List<ExaminationEvidenceFile> EvidenceFiles { get; set; } = new();
}

public class ExaminationEvidenceRequest
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public int ProjectId { get; set; }
    public int? FindingId { get; set; }
    public int? SubmissionId { get; set; }
    public int? InstitutionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string RequestText { get; set; } = string.Empty;
    public string? RequestedItemsJson { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueAt { get; set; }
    public ExaminationEvidenceRequestStatus Status { get; set; } = ExaminationEvidenceRequestStatus.Open;
    public int RequestedBy { get; set; }
    public DateTime? FulfilledAt { get; set; }

    public ExaminationProject? Project { get; set; }
    public ExaminationFinding? Finding { get; set; }
    public Submission? Submission { get; set; }
    public Institution? Institution { get; set; }
    public List<ExaminationEvidenceFile> EvidenceFiles { get; set; } = new();
}

public class ExaminationEvidenceFile
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public int ProjectId { get; set; }
    public int? FindingId { get; set; }
    public int? EvidenceRequestId { get; set; }
    public int? SubmissionId { get; set; }
    public int? InstitutionId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long FileSizeBytes { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public ExaminationEvidenceKind Kind { get; set; } = ExaminationEvidenceKind.SupportingDocument;
    public ExaminationEvidenceUploaderRole UploadedByRole { get; set; } = ExaminationEvidenceUploaderRole.Examiner;
    public int UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public bool IsVerified { get; set; }
    public int? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }

    public ExaminationProject? Project { get; set; }
    public ExaminationFinding? Finding { get; set; }
    public ExaminationEvidenceRequest? EvidenceRequest { get; set; }
    public Submission? Submission { get; set; }
    public Institution? Institution { get; set; }
}
