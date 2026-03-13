namespace FC.Engine.Domain.Models;

public static class ForeSightModelCodes
{
    public const string FilingRisk = "FILING_RISK";
    public const string CapitalBreach = "CAPITAL_BREACH";
    public const string ComplianceTrend = "CHS_TREND";
    public const string ChurnRisk = "CHURN_RISK";
    public const string RegulatoryAction = "REG_ACTION";
}

public sealed class ForeSightPredictionFeature
{
    public string FeatureName { get; set; } = string.Empty;
    public string FeatureLabel { get; set; } = string.Empty;
    public decimal RawValue { get; set; }
    public decimal NormalizedValue { get; set; }
    public decimal Weight { get; set; }
    public decimal ContributionScore { get; set; }
    public string ImpactDirection { get; set; } = "INCREASES_RISK";
}

public sealed class ForeSightPredictionSummary
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public string ModelCode { get; set; } = string.Empty;
    public DateTime PredictionDate { get; set; }
    public string HorizonLabel { get; set; } = string.Empty;
    public DateTime? HorizonDate { get; set; }
    public decimal PredictedValue { get; set; }
    public decimal? ConfidenceLower { get; set; }
    public decimal? ConfidenceUpper { get; set; }
    public decimal ConfidenceScore { get; set; }
    public string RiskBand { get; set; } = "NONE";
    public string TargetModuleCode { get; set; } = string.Empty;
    public string TargetPeriodCode { get; set; } = string.Empty;
    public string TargetMetric { get; set; } = string.Empty;
    public string TargetLabel { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string RootCauseNarrative { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public string RootCausePillar { get; set; } = string.Empty;
    public bool IsSuppressed { get; set; }
    public string? SuppressionReason { get; set; }
    public List<ForeSightPredictionFeature> Features { get; set; } = new();

    public string RiskBandCssClass => RiskBand switch
    {
        "CRITICAL" => "badge bg-danger",
        "HIGH" => "badge bg-danger",
        "MEDIUM" => "badge bg-warning text-dark",
        "LOW" => "badge bg-success",
        _ => "badge bg-secondary"
    };
}

public sealed class ForeSightAlertItem
{
    public int Id { get; set; }
    public long PredictionId { get; set; }
    public Guid TenantId { get; set; }
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = "INFO";
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public string RecipientRole { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public bool IsDismissed { get; set; }
    public DateTime DispatchedAt { get; set; }

    public string SeverityCssClass => Severity switch
    {
        "CRITICAL" => "border-danger bg-danger bg-opacity-10",
        "WARNING" => "border-warning bg-warning bg-opacity-10",
        _ => "border-info bg-info bg-opacity-10"
    };
}

public sealed class FilingRiskForecast
{
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string PeriodCode { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public int DaysToDeadline { get; set; }
    public decimal ProbabilityLate { get; set; }
    public decimal ConfidenceScore { get; set; }
    public decimal ConfidenceLower { get; set; }
    public decimal ConfidenceUpper { get; set; }
    public string RiskBand { get; set; } = "LOW";
    public string Explanation { get; set; } = string.Empty;
    public string RootCauseNarrative { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public List<ForeSightPredictionFeature> TopFactors { get; set; } = new();
}

public sealed class CapitalForecastSummary
{
    public string MetricCode { get; set; } = string.Empty;
    public string MetricLabel { get; set; } = string.Empty;
    public decimal CurrentValue { get; set; }
    public decimal ProjectedValue { get; set; }
    public decimal ThresholdValue { get; set; }
    public decimal ConfidenceScore { get; set; }
    public decimal ConfidenceLower { get; set; }
    public decimal ConfidenceUpper { get; set; }
    public string HorizonLabel { get; set; } = string.Empty;
    public string RiskBand { get; set; } = "LOW";
    public bool BreachPredicted { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string RootCauseNarrative { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public List<ForeSightPredictionFeature> TopFactors { get; set; } = new();
}

public sealed class ComplianceScoreForecast
{
    public decimal CurrentScore { get; set; }
    public decimal ProjectedScore { get; set; }
    public decimal ScoreChange { get; set; }
    public string CurrentRating { get; set; } = string.Empty;
    public string ProjectedRating { get; set; } = string.Empty;
    public string RiskBand { get; set; } = "LOW";
    public decimal ConfidenceScore { get; set; }
    public decimal ConfidenceLower { get; set; }
    public decimal ConfidenceUpper { get; set; }
    public string DecliningPillar { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string RootCauseNarrative { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public List<ForeSightPredictionFeature> TopFactors { get; set; } = new();
}

public sealed class ChurnRiskAssessment
{
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string LicenceType { get; set; } = string.Empty;
    public decimal ChurnProbability { get; set; }
    public decimal ConfidenceScore { get; set; }
    public decimal ConfidenceLower { get; set; }
    public decimal ConfidenceUpper { get; set; }
    public string RiskBand { get; set; } = "LOW";
    public string Explanation { get; set; } = string.Empty;
    public string RootCauseNarrative { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public List<ForeSightPredictionFeature> TopFactors { get; set; } = new();
}

public sealed class RegulatoryActionForecast
{
    public Guid TenantId { get; set; }
    public int InstitutionId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string LicenceType { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public decimal InterventionProbability { get; set; }
    public decimal ConfidenceScore { get; set; }
    public decimal ConfidenceLower { get; set; }
    public decimal ConfidenceUpper { get; set; }
    public string RiskBand { get; set; } = "LOW";
    public string Explanation { get; set; } = string.Empty;
    public string RootCauseNarrative { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public List<ForeSightPredictionFeature> TopFactors { get; set; } = new();
}

public sealed class ForeSightDashboardData
{
    public Guid TenantId { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public List<FilingRiskForecast> FilingRisks { get; set; } = new();
    public List<CapitalForecastSummary> CapitalForecasts { get; set; } = new();
    public ComplianceScoreForecast? ComplianceForecast { get; set; }
    public List<ForeSightAlertItem> Alerts { get; set; } = new();
}
