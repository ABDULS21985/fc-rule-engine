namespace FC.Engine.Domain.Entities;

public class CapitalActionTemplateRecord
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string PrimaryLever { get; set; } = string.Empty;
    public decimal CapitalActionBn { get; set; }
    public decimal RwaOptimisationPercent { get; set; }
    public decimal QuarterlyRetainedEarningsDeltaBn { get; set; }
    public decimal EstimatedAnnualCostPercent { get; set; }
    public DateTime MaterializedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
