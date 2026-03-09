using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Models;

public class RegulatorInboxFilter
{
    public string? InstitutionName { get; set; }
    public string? LicenceType { get; set; }
    public string? ModuleCode { get; set; }
    public string? PeriodCode { get; set; }
    public string? Status { get; set; }
}

public class RegulatorSubmissionInboxItem
{
    public int SubmissionId { get; set; }
    public Guid TenantId { get; set; }
    public int InstitutionId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string LicenceType { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string PeriodLabel { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public string SubmissionStatus { get; set; } = string.Empty;
    public RegulatorReceiptStatus ReceiptStatus { get; set; } = RegulatorReceiptStatus.Received;
    public int OpenQueryCount { get; set; }
}

public class RegulatorSubmissionDetail
{
    public RegulatorSubmissionInboxItem Header { get; set; } = new();
    public RegulatorReceipt? Receipt { get; set; }
    public List<ExaminerQuery> Queries { get; set; } = new();
    public List<ValidationErrorAggregate> TopValidationErrors { get; set; } = new();
}

public class SectorCarDistribution
{
    public string PeriodCode { get; set; } = string.Empty;
    public decimal AverageCar { get; set; }
    public decimal MedianCar { get; set; }
    public int InstitutionCount { get; set; }
    public List<HistogramBucket> Buckets { get; set; } = new();
}

public class HistogramBucket
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class SectorNplTrend
{
    public List<string> PeriodLabels { get; set; } = new();
    public List<decimal> AverageNplRatios { get; set; } = new();
}

public class SectorDepositStructure
{
    public string PeriodCode { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public List<DepositSlice> Slices { get; set; } = new();
}

public class DepositSlice
{
    public string Label { get; set; } = string.Empty;
    public decimal Value { get; set; }
}

public class FilingTimeliness
{
    public string PeriodCode { get; set; } = string.Empty;
    public int OnTimeCount { get; set; }
    public int LateCount { get; set; }
    public List<InstitutionTimelinessItem> Institutions { get; set; } = new();
}

public class InstitutionTimelinessItem
{
    public int InstitutionId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public int OnTime { get; set; }
    public int Late { get; set; }
}

public class FilingHeatmap
{
    public string PeriodCode { get; set; } = string.Empty;
    public List<string> Institutions { get; set; } = new();
    public List<string> Modules { get; set; } = new();
    public List<FilingHeatmapCell> Cells { get; set; } = new();
}

public class FilingHeatmapCell
{
    public string Institution { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public bool Filed { get; set; }
}

public class EntityBenchmarkResult
{
    public int InstitutionId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;

    public decimal CarValue { get; set; }
    public decimal CarPeerAverage { get; set; }
    public decimal CarPeerMedian { get; set; }
    public decimal CarPeerP25 { get; set; }
    public decimal CarPeerP75 { get; set; }

    public decimal NplValue { get; set; }
    public decimal NplPeerAverage { get; set; }

    public decimal TimelinessScore { get; set; }
    public decimal TimelinessPeerAverage { get; set; }

    public decimal DataQualityScore { get; set; }
    public decimal DataQualityPeerAverage { get; set; }
}

public class EarlyWarningFlag
{
    public int InstitutionId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public EarlyWarningSeverity Severity { get; set; }
    public string FlagCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;
}

public class ExaminationProjectCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public List<int> InstitutionIds { get; set; } = new();
    public List<string> ModuleCodes { get; set; } = new();
    public DateTime? PeriodFrom { get; set; }
    public DateTime? PeriodTo { get; set; }
}

public class ExaminationWorkspaceData
{
    public ExaminationProject Project { get; set; } = new();
    public List<RegulatorSubmissionInboxItem> Submissions { get; set; } = new();
    public List<ExaminationAnnotation> Annotations { get; set; } = new();
    public Dictionary<int, EntityBenchmarkResult> BenchmarksByInstitution { get; set; } = new();
}

// ── RG-36: Systemic Risk Engine ──────────────────────────────────────

public enum RiskRating { Green, Amber, Red }

public enum CamelsComponent { Capital, AssetQuality, Management, Earnings, Liquidity, Sensitivity }

public class CamelsScore
{
    public int InstitutionId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string LicenceType { get; set; } = string.Empty;
    public decimal Capital { get; set; }
    public decimal AssetQuality { get; set; }
    public decimal Management { get; set; }
    public decimal Earnings { get; set; }
    public decimal Liquidity { get; set; }
    public decimal Sensitivity { get; set; }
    public decimal Composite { get; set; }
    public RiskRating Rating { get; set; }
    public decimal TotalAssets { get; set; }
    public int ActiveFlags { get; set; }
}

public class SystemicRiskSummary
{
    public int TotalEntities { get; set; }
    public int GreenCount { get; set; }
    public int AmberCount { get; set; }
    public int RedCount { get; set; }
    public decimal SectorAverageCar { get; set; }
    public decimal SectorAverageNpl { get; set; }
    public int TotalActiveFlags { get; set; }
    public decimal SystemicRiskIndex { get; set; }
}

public class HeatmapEntity
{
    public int InstitutionId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string LicenceType { get; set; } = string.Empty;
    public decimal RiskScore { get; set; }
    public decimal Size { get; set; }
    public RiskRating Rating { get; set; }
    public int FlagCount { get; set; }
}

public class ContagionLink
{
    public int SourceId { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public int TargetId { get; set; }
    public string TargetName { get; set; } = string.Empty;
    public decimal CorrelationStrength { get; set; }
}

public class ContagionAnalysis
{
    public List<ContagionLink> Links { get; set; } = new();
    public List<string> HighRiskClusters { get; set; } = new();
    public int ClusterCount { get; set; }
}

public class SystemicEwi
{
    public string IndicatorCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public RiskRating Severity { get; set; }
    public decimal CurrentValue { get; set; }
    public decimal Threshold { get; set; }
    public int AffectedEntities { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}

public class SupervisoryAction
{
    public int Id { get; set; }
    public int InstitutionId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string TriggerFlag { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string EscalationLevel { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string? LetterTemplate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }
    public string? RemediationNotes { get; set; }
}

public class SystemicRiskDashboard
{
    public SystemicRiskSummary Summary { get; set; } = new();
    public List<CamelsScore> Scores { get; set; } = new();
    public List<HeatmapEntity> HeatmapData { get; set; } = new();
    public List<EarlyWarningFlag> InstitutionalFlags { get; set; } = new();
    public List<SystemicEwi> SystemicIndicators { get; set; } = new();
    public ContagionAnalysis Contagion { get; set; } = new();
    public List<SupervisoryAction> PendingActions { get; set; } = new();
}
