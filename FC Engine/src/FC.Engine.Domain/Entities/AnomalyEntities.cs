namespace FC.Engine.Domain.Entities;

public class AnomalyThresholdConfig
{
    public int Id { get; set; }
    public string ConfigKey { get; set; } = string.Empty;
    public decimal ConfigValue { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;
    public DateTime? EffectiveTo { get; set; }
    public string CreatedBy { get; set; } = "SYSTEM";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class AnomalyModelVersion
{
    public int Id { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string Status { get; set; } = "SHADOW";
    public DateTime TrainingStartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? TrainingCompletedAt { get; set; }
    public int SubmissionCount { get; set; }
    public int ObservationCount { get; set; }
    public int TenantCount { get; set; }
    public int PeriodCount { get; set; }
    public DateTime? PromotedAt { get; set; }
    public string? PromotedBy { get; set; }
    public DateTime? RetiredAt { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<AnomalyFieldModel> FieldModels { get; set; } = new();
    public List<AnomalyCorrelationRule> CorrelationRules { get; set; } = new();
    public List<AnomalyPeerGroupStatistic> PeerStatistics { get; set; } = new();
}

public class AnomalyFieldModel
{
    public int Id { get; set; }
    public int ModelVersionId { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string FieldCode { get; set; } = string.Empty;
    public string FieldLabel { get; set; } = string.Empty;
    public string DistributionType { get; set; } = "NORMAL";
    public int Observations { get; set; }
    public decimal? MeanValue { get; set; }
    public decimal? StdDev { get; set; }
    public decimal? MedianValue { get; set; }
    public decimal? Q1Value { get; set; }
    public decimal? Q3Value { get; set; }
    public decimal? MinObserved { get; set; }
    public decimal? MaxObserved { get; set; }
    public decimal? Percentile05 { get; set; }
    public decimal? Percentile95 { get; set; }
    public bool IsColdStart { get; set; }
    public decimal? RuleBasedMin { get; set; }
    public decimal? RuleBasedMax { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AnomalyModelVersion? ModelVersion { get; set; }
}

public class AnomalyCorrelationRule
{
    public int Id { get; set; }
    public int ModelVersionId { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string FieldCodeA { get; set; } = string.Empty;
    public string FieldLabelA { get; set; } = string.Empty;
    public string FieldCodeB { get; set; } = string.Empty;
    public string FieldLabelB { get; set; } = string.Empty;
    public decimal CorrelationCoefficient { get; set; }
    public decimal RSquared { get; set; }
    public decimal Slope { get; set; }
    public decimal Intercept { get; set; }
    public int Observations { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AnomalyModelVersion? ModelVersion { get; set; }
}

public class AnomalyPeerGroupStatistic
{
    public int Id { get; set; }
    public int ModelVersionId { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string FieldCode { get; set; } = string.Empty;
    public string LicenceCategory { get; set; } = string.Empty;
    public string InstitutionSizeBand { get; set; } = "ALL";
    public int PeerCount { get; set; }
    public decimal? PeerMean { get; set; }
    public decimal? PeerMedian { get; set; }
    public decimal? PeerStdDev { get; set; }
    public decimal? PeerQ1 { get; set; }
    public decimal? PeerQ3 { get; set; }
    public decimal? PeerMin { get; set; }
    public decimal? PeerMax { get; set; }
    public string PeriodCode { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AnomalyModelVersion? ModelVersion { get; set; }
}

public class AnomalyRuleBaseline
{
    public int Id { get; set; }
    public string RegulatorCode { get; set; } = string.Empty;
    public string? ModuleCode { get; set; }
    public string FieldCode { get; set; } = string.Empty;
    public string FieldLabel { get; set; } = string.Empty;
    public decimal? MinimumValue { get; set; }
    public decimal? MaximumValue { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AnomalySeedCorrelationRule
{
    public int Id { get; set; }
    public string RegulatorCode { get; set; } = string.Empty;
    public string? ModuleCode { get; set; }
    public string FieldCodeA { get; set; } = string.Empty;
    public string FieldLabelA { get; set; } = string.Empty;
    public string FieldCodeB { get; set; } = string.Empty;
    public string FieldLabelB { get; set; } = string.Empty;
    public decimal CorrelationCoefficient { get; set; }
    public decimal RSquared { get; set; }
    public decimal Slope { get; set; }
    public decimal Intercept { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AnomalyReport
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public int InstitutionId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public int SubmissionId { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public string PeriodCode { get; set; } = string.Empty;
    public int ModelVersionId { get; set; }
    public decimal OverallQualityScore { get; set; } = 100m;
    public int TotalFieldsAnalysed { get; set; }
    public int TotalFindings { get; set; }
    public int AlertCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }
    public int RelationshipFindings { get; set; }
    public int TemporalFindings { get; set; }
    public int PeerFindings { get; set; }
    public string TrafficLight { get; set; } = "GREEN";
    public string NarrativeSummary { get; set; } = string.Empty;
    public DateTime AnalysedAt { get; set; } = DateTime.UtcNow;
    public int? AnalysisDurationMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AnomalyModelVersion? ModelVersion { get; set; }
    public List<AnomalyFinding> Findings { get; set; } = new();
}

public class AnomalyFinding
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public int SubmissionId { get; set; }
    public int AnomalyReportId { get; set; }
    public string FindingType { get; set; } = "FIELD";
    public string Severity { get; set; } = "INFO";
    public string DetectionMethod { get; set; } = string.Empty;
    public string FieldCode { get; set; } = string.Empty;
    public string FieldLabel { get; set; } = string.Empty;
    public string? RelatedFieldCode { get; set; }
    public string? RelatedFieldLabel { get; set; }
    public decimal? ReportedValue { get; set; }
    public decimal? RelatedValue { get; set; }
    public decimal? ExpectedValue { get; set; }
    public decimal? ExpectedRangeLow { get; set; }
    public decimal? ExpectedRangeHigh { get; set; }
    public decimal? HistoricalMean { get; set; }
    public decimal? HistoricalStdDev { get; set; }
    public decimal? BaselineValue { get; set; }
    public decimal? DeviationPercent { get; set; }
    public double? ZScore { get; set; }
    public int? PeerCount { get; set; }
    public string? PeerGroup { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public bool IsAcknowledged { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public string? AcknowledgementReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AnomalyReport? Report { get; set; }
}
