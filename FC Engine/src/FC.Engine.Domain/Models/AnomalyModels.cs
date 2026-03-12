namespace FC.Engine.Domain.Models;

public enum AnomalyTrafficLight
{
    Green,
    Amber,
    Red
}

public sealed class AnomalySectorSummary
{
    public Guid TenantId { get; set; }
    public int InstitutionId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string LicenceType { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = string.Empty;
    public string PeriodCode { get; set; } = string.Empty;
    public decimal QualityScore { get; set; }
    public string TrafficLight { get; set; } = "GREEN";
    public int AlertCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }
    public int TotalFindings { get; set; }
    public int UnacknowledgedCount { get; set; }
}

public sealed class AnomalyAcknowledgementRequest
{
    public int FindingId { get; set; }
    public Guid TenantId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string AcknowledgedBy { get; set; } = string.Empty;
}

public sealed class AnomalyMetricSnapshot
{
    public string FieldCode { get; set; } = string.Empty;
    public string FieldLabel { get; set; } = string.Empty;
    public decimal Value { get; set; }
}

public sealed class AnomalyModelTrainingSummary
{
    public int ModelVersionId { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public int SubmissionCount { get; set; }
    public int ObservationCount { get; set; }
    public int TenantCount { get; set; }
    public int PeriodCount { get; set; }
    public DateTime TrainingStartedAt { get; set; }
    public DateTime? TrainingCompletedAt { get; set; }
    public string? Notes { get; set; }
}
