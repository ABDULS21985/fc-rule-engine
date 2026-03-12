namespace FC.Engine.Domain.Entities;

public class CapitalPlanningScenarioRecord
{
    public int Id { get; set; }
    public string ScenarioKey { get; set; } = string.Empty;
    public decimal CurrentCarPercent { get; set; }
    public decimal CurrentRwaBn { get; set; }
    public decimal QuarterlyRwaGrowthPercent { get; set; }
    public decimal QuarterlyRetainedEarningsBn { get; set; }
    public decimal CapitalActionBn { get; set; }
    public decimal MinimumRequirementPercent { get; set; }
    public decimal ConservationBufferPercent { get; set; }
    public decimal CountercyclicalBufferPercent { get; set; }
    public decimal DsibBufferPercent { get; set; }
    public decimal RwaOptimisationPercent { get; set; }
    public decimal TargetCarPercent { get; set; }
    public decimal Cet1CostPercent { get; set; }
    public decimal At1CostPercent { get; set; }
    public decimal Tier2CostPercent { get; set; }
    public decimal MaxAt1SharePercent { get; set; }
    public decimal MaxTier2SharePercent { get; set; }
    public decimal StepPercent { get; set; }
    public DateTime SavedAtUtc { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CapitalPlanningScenarioHistoryRecord
{
    public int Id { get; set; }
    public decimal CurrentCarPercent { get; set; }
    public decimal CurrentRwaBn { get; set; }
    public decimal QuarterlyRwaGrowthPercent { get; set; }
    public decimal QuarterlyRetainedEarningsBn { get; set; }
    public decimal CapitalActionBn { get; set; }
    public decimal MinimumRequirementPercent { get; set; }
    public decimal ConservationBufferPercent { get; set; }
    public decimal CountercyclicalBufferPercent { get; set; }
    public decimal DsibBufferPercent { get; set; }
    public decimal RwaOptimisationPercent { get; set; }
    public decimal TargetCarPercent { get; set; }
    public decimal Cet1CostPercent { get; set; }
    public decimal At1CostPercent { get; set; }
    public decimal Tier2CostPercent { get; set; }
    public decimal MaxAt1SharePercent { get; set; }
    public decimal MaxTier2SharePercent { get; set; }
    public decimal StepPercent { get; set; }
    public DateTime SavedAtUtc { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
