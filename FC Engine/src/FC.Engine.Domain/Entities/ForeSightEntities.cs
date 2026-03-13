namespace FC.Engine.Domain.Entities;

public class ForeSightConfig
{
    public int Id { get; set; }
    public string ConfigKey { get; set; } = string.Empty;
    public string ConfigValue { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;
    public DateTime? EffectiveTo { get; set; }
    public string CreatedBy { get; set; } = "SYSTEM";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ForeSightModelVersion
{
    public int Id { get; set; }
    public string ModelCode { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public DateTime? TrainedAt { get; set; }
    public int ObservationsCount { get; set; }
    public decimal? AccuracyMetric { get; set; }
    public string AccuracyMetricName { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<ForeSightPrediction> Predictions { get; set; } = new();
}

public class ForeSightFeatureDefinition
{
    public int Id { get; set; }
    public string ModelCode { get; set; } = string.Empty;
    public string FeatureName { get; set; } = string.Empty;
    public string FeatureLabel { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DataSource { get; set; } = string.Empty;
    public decimal DefaultWeight { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ForeSightRegulatoryThreshold
{
    public int Id { get; set; }
    public string Regulator { get; set; } = string.Empty;
    public string LicenceCategory { get; set; } = string.Empty;
    public string MetricCode { get; set; } = string.Empty;
    public string MetricLabel { get; set; } = string.Empty;
    public decimal ThresholdValue { get; set; }
    public string ThresholdType { get; set; } = "MINIMUM";
    public string SeverityIfBreached { get; set; } = "WARNING";
    public string Description { get; set; } = string.Empty;
    public string? CircularReference { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ForeSightPrediction
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public string ModelCode { get; set; } = string.Empty;
    public int ModelVersionId { get; set; }
    public DateTime PredictionDate { get; set; } = DateTime.UtcNow.Date;
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
    public string FeatureImportanceJson { get; set; } = "[]";
    public bool IsSuppressed { get; set; }
    public string? SuppressionReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ForeSightModelVersion? ModelVersion { get; set; }
    public List<ForeSightPredictionFeatureRecord> Features { get; set; } = new();
    public List<ForeSightAlert> Alerts { get; set; } = new();
}

public class ForeSightPredictionFeatureRecord
{
    public long Id { get; set; }
    public long PredictionId { get; set; }
    public string FeatureName { get; set; } = string.Empty;
    public string FeatureLabel { get; set; } = string.Empty;
    public decimal RawValue { get; set; }
    public decimal NormalizedValue { get; set; }
    public decimal Weight { get; set; }
    public decimal ContributionScore { get; set; }
    public string ImpactDirection { get; set; } = "INCREASES_RISK";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ForeSightPrediction? Prediction { get; set; }
}

public class ForeSightAlert
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
    public string? ReadBy { get; set; }
    public DateTime? ReadAt { get; set; }
    public bool IsDismissed { get; set; }
    public string? DismissedBy { get; set; }
    public DateTime? DismissedAt { get; set; }
    public DateTime DispatchedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ForeSightPrediction? Prediction { get; set; }
}
