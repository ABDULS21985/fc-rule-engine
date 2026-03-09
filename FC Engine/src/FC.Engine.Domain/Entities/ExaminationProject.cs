using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Entities;

public class ExaminationProject
{
    public int Id { get; set; }

    /// <summary>Regulator tenant ID for RLS.</summary>
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string EntityIdsJson { get; set; } = "[]";
    public string ModuleCodesJson { get; set; } = "[]";
    public DateTime? PeriodFrom { get; set; }
    public DateTime? PeriodTo { get; set; }
    public ExaminationProjectStatus Status { get; set; } = ExaminationProjectStatus.Draft;
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string TeamAssignmentsJson { get; set; } = "[]";
    public string TimelineJson { get; set; } = "[]";
    public string? ReportFilePath { get; set; }
    public DateTime? LastReportGeneratedAt { get; set; }
    public string? IntelligencePackFilePath { get; set; }
    public DateTime? IntelligencePackGeneratedAt { get; set; }

    public List<ExaminationAnnotation> Annotations { get; set; } = new();
    public List<ExaminationFinding> Findings { get; set; } = new();
    public List<ExaminationEvidenceRequest> EvidenceRequests { get; set; } = new();
    public List<ExaminationEvidenceFile> EvidenceFiles { get; set; } = new();
}

public class ExaminationAnnotation
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public int ProjectId { get; set; }
    public int SubmissionId { get; set; }
    public int? InstitutionId { get; set; }
    public string? FieldCode { get; set; }
    public string Note { get; set; } = string.Empty;
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ExaminationProject? Project { get; set; }
    public Submission? Submission { get; set; }
}
