namespace FC.Engine.Domain.Entities;

/// <summary>
/// Persisted weekly CHS snapshot for trend analysis.
/// One row per tenant per computation week.
/// </summary>
public class ChsScoreSnapshot
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public string PeriodLabel { get; set; } = string.Empty;
    public DateTime ComputedAt { get; set; }
    public decimal OverallScore { get; set; }
    public int Rating { get; set; }
    public decimal FilingTimeliness { get; set; }
    public decimal DataQuality { get; set; }
    public decimal RegulatoryCapital { get; set; }
    public decimal AuditGovernance { get; set; }
    public decimal Engagement { get; set; }
}
