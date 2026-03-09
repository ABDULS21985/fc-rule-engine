using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Models;

public class ExaminationTeamAssignment
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class ExaminationMilestone
{
    public string Title { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public DateTime? DueAt { get; set; }
    public bool Completed { get; set; }
}

public class ExaminationFindingCreateRequest
{
    public int? SubmissionId { get; set; }
    public int? InstitutionId { get; set; }
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
    public DateTime? ManagementResponseDeadline { get; set; }
    public string? ManagementResponse { get; set; }
    public string? ManagementActionPlan { get; set; }
}

public class ExaminationFindingUpdateRequest
{
    public ExaminationWorkflowStatus Status { get; set; }
    public ExaminationRemediationStatus RemediationStatus { get; set; }
    public ExaminationRiskRating RiskRating { get; set; } = ExaminationRiskRating.Medium;
    public string Observation { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public DateTime? ManagementResponseDeadline { get; set; }
    public string? ManagementResponse { get; set; }
    public string? ManagementActionPlan { get; set; }
    public string? EvidenceReference { get; set; }
}

public class ExaminationEvidenceRequestCreateRequest
{
    public int? FindingId { get; set; }
    public int? SubmissionId { get; set; }
    public int? InstitutionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string RequestText { get; set; } = string.Empty;
    public List<string> RequestedItems { get; set; } = new();
    public DateTime? DueAt { get; set; }
}

public class ExaminationIntelligencePack
{
    public int ProjectId { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public int TotalInstitutions { get; set; }
    public int TotalOutstandingRemediationItems { get; set; }
    public int TotalActiveEwis { get; set; }
    public List<string> KeyRiskAreas { get; set; } = new();
    public List<ExaminationInstitutionIntelligence> Institutions { get; set; } = new();
    public List<ExaminationFinding> OutstandingPreviousFindings { get; set; } = new();
}

public class ExaminationInstitutionIntelligence
{
    public int InstitutionId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string LicenceType { get; set; } = string.Empty;
    public string ChsTrendJson { get; set; } = "[]";
    public List<ExaminationQuarterTrendPoint> ChsTrend { get; set; } = new();
    public List<EarlyWarningFlag> ActiveWarnings { get; set; } = new();
    public EntityBenchmarkResult? PeerComparison { get; set; }
    public List<ExaminationFinding> OutstandingPreviousFindings { get; set; } = new();
    public List<string> KeyRiskAreas { get; set; } = new();
}

public class ExaminationQuarterTrendPoint
{
    public string QuarterLabel { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public DateTime SnapshotDate { get; set; }
}

public class ExaminationEvidenceDownload
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = Array.Empty<byte>();
}
