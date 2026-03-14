namespace FC.Engine.Domain.Models;

public sealed class MetricRankingEntry
{
    public int Rank { get; set; }
    public Guid TenantId { get; set; }
    public int InstitutionId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string LicenceCategory { get; set; } = string.Empty;
    public string FieldCode { get; set; } = string.Empty;
    public decimal MetricValue { get; set; }
    public string? PeriodCode { get; set; }
    public DateTime SubmittedAt { get; set; }
}

public sealed class RegIqMetricSnapshot
{
    public string MetricCode { get; set; } = string.Empty;
    public string MetricLabel { get; set; } = string.Empty;
    public decimal? Value { get; set; }
    public string? PeriodCode { get; set; }
    public string? ModuleCode { get; set; }
}

public sealed class RegIqAnomalySummary
{
    public int? ReportId { get; set; }
    public string? ModuleCode { get; set; }
    public string? PeriodCode { get; set; }
    public decimal? QualityScore { get; set; }
    public string TrafficLight { get; set; } = "GREEN";
    public int AlertCount { get; set; }
    public int WarningCount { get; set; }
    public int TotalFindings { get; set; }
    public string NarrativeSummary { get; set; } = string.Empty;
}

public sealed class RegIqPredictionSummary
{
    public string ModelCode { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal PredictedValue { get; set; }
    public decimal ConfidenceScore { get; set; }
    public string RiskBand { get; set; } = "LOW";
    public string? PeriodCode { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
}

public sealed class RegIqConductRiskSummary
{
    public decimal CompositeScore { get; set; }
    public string RiskBand { get; set; } = "LOW";
    public int ActiveAlertCount { get; set; }
    public string? PeriodCode { get; set; }
    public DateTime? ComputedAt { get; set; }
}

public sealed class RegIqStrAdequacySummary
{
    public string? PeriodCode { get; set; }
    public int StrFilingCount { get; set; }
    public decimal? PeerAverageStrCount { get; set; }
    public decimal? StrDeviation { get; set; }
    public int StructuringAlertCount { get; set; }
    public decimal? TfsFalsePositiveRate { get; set; }
}

public sealed class RegIqDiscrepancySummary
{
    public string DiscrepancyCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Severity { get; set; } = "INFO";
    public string Description { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}

public sealed class RegIqAlertSummary
{
    public string Source { get; set; } = string.Empty;
    public string AlertCode { get; set; } = string.Empty;
    public string Severity { get; set; } = "INFO";
    public string Title { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public DateTime? TriggeredAt { get; set; }
}

public sealed class RegIqEntityRankingItem
{
    public Guid TenantId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string LicenceCategory { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public string ScoreLabel { get; set; } = string.Empty;
    public string RiskBand { get; set; } = string.Empty;
}

public sealed class RegIqSystemicIndicator
{
    public string IndicatorCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public decimal? CurrentValue { get; set; }
    public decimal? Threshold { get; set; }
    public string Severity { get; set; } = "LOW";
    public int AffectedEntities { get; set; }
    public string Description { get; set; } = string.Empty;
}

public sealed class RegIqInvestigationSummary
{
    public string InvestigationType { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int PriorityScore { get; set; }
    public DateTime? OpenedAt { get; set; }
}

public sealed class RegIqFocusArea
{
    public string Area { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Priority { get; set; } = "MEDIUM";
}

public sealed class RegIqMetricTrendPoint
{
    public string PeriodCode { get; set; } = string.Empty;
    public decimal? Value { get; set; }
    public DateTime? SubmittedAt { get; set; }
}

public sealed class RegIqMetricTrendSeries
{
    public string MetricCode { get; set; } = string.Empty;
    public string MetricLabel { get; set; } = string.Empty;
    public List<RegIqMetricTrendPoint> Points { get; set; } = new();
}

public sealed class RegIqFilingTimelinessSummary
{
    public int TotalFilings { get; set; }
    public int OnTimeFilings { get; set; }
    public int LateFilings { get; set; }
    public int OverdueFilings { get; set; }
    public DateTime? LatestDeadline { get; set; }
    public DateTime? LatestSubmittedAt { get; set; }
}

public sealed class RegIqSanctionsExposureSummary
{
    public int MatchCount { get; set; }
    public double? HighestMatchScore { get; set; }
    public string HighestRiskLevel { get; set; } = "NONE";
    public string? LatestMatchedName { get; set; }
    public DateTime? LatestMatchedAt { get; set; }
}

public sealed class EntityIntelligenceProfile
{
    public Guid TenantId { get; set; }
    public int InstitutionId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string LicenceCategory { get; set; } = string.Empty;
    public string RegulatorAgency { get; set; } = string.Empty;
    public string InstitutionType { get; set; } = string.Empty;
    public string? HoldingCompanyName { get; set; }
    public string? LatestPeriodCode { get; set; }
    public DateTime? LatestSubmissionAt { get; set; }
    public List<RegIqMetricSnapshot> KeyMetrics { get; set; } = new();
    public ComplianceHealthScore? ComplianceHealth { get; set; }
    public RegIqAnomalySummary? Anomaly { get; set; }
    public RegIqPredictionSummary? FilingRisk { get; set; }
    public RegIqPredictionSummary? CapitalForecast { get; set; }
    public CamelsScore? CamelsScore { get; set; }
    public RegIqFilingTimelinessSummary? FilingTimeliness { get; set; }
    public RegIqSanctionsExposureSummary? SanctionsExposure { get; set; }
    public List<EarlyWarningFlag> EarlyWarningFlags { get; set; } = new();
    public List<EWITriggerRow> EwiHistory { get; set; } = new();
    public RegIqConductRiskSummary? FinancialCrimeRisk { get; set; }
    public RegIqStrAdequacySummary? StrAdequacy { get; set; }
    public List<RegIqDiscrepancySummary> CrossModuleDiscrepancies { get; set; } = new();
    public List<RegIqAlertSummary> ActiveAlerts { get; set; } = new();
    public string? LatestNarrative { get; set; }
    public List<string> DataSourcesUsed { get; set; } = new();
}

public sealed class SectorIntelligenceSummary
{
    public string RegulatorAgency { get; set; } = string.Empty;
    public string? LicenceCategory { get; set; }
    public string? PeriodCode { get; set; }
    public int EntityCount { get; set; }
    public decimal? AverageCarRatio { get; set; }
    public decimal? AverageNplRatio { get; set; }
    public decimal? AverageLiquidityRatio { get; set; }
    public decimal? AverageComplianceHealthScore { get; set; }
    public decimal? AverageAnomalyQualityScore { get; set; }
    public decimal? AverageConductRiskScore { get; set; }
    public int OverdueFilingCount { get; set; }
    public int HighRiskEntityCount { get; set; }
    public SectorCarDistribution? CarDistribution { get; set; }
    public SectorNplTrend? NplTrend { get; set; }
    public FilingTimeliness? FilingTimeliness { get; set; }
    public SectorChsSummary? ComplianceHealthSummary { get; set; }
    public List<ChsWatchListItem> WatchList { get; set; } = new();
    public List<HeatmapCell> Heatmap { get; set; } = new();
    public List<AnomalySectorSummary> AnomalyHotspots { get; set; } = new();
    public List<RegulatoryActionForecast> RegulatoryRiskRanking { get; set; } = new();
    public List<SystemicEwi> SystemicRiskIndicatorsRaw { get; set; } = new();
    public List<RegIqEntityRankingItem> ExaminationPriorityRanking { get; set; } = new();
    public List<RegIqSystemicIndicator> SystemicIndicators { get; set; } = new();
    public List<string> DataSourcesUsed { get; set; } = new();
}

public sealed class ExaminationBriefing
{
    public Guid TenantId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string LicenceCategory { get; set; } = string.Empty;
    public string RegulatorAgency { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public EntityIntelligenceProfile Profile { get; set; } = new();
    public SectorIntelligenceSummary PeerContext { get; set; } = new();
    public List<RegIqMetricTrendSeries> Trends { get; set; } = new();
    public List<RegIqFocusArea> FocusAreas { get; set; } = new();
    public List<RegIqInvestigationSummary> OpenInvestigations { get; set; } = new();
    public List<string> DataSourcesUsed { get; set; } = new();
}
