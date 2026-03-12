namespace FC.Engine.Domain.Entities;

public class MarketplaceRolloutModuleRecord
{
    public int Id { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public int EligibleTenants { get; set; }
    public int ActiveEntitlements { get; set; }
    public int PendingEntitlements { get; set; }
    public int StaleTenants { get; set; }
    public int IncludedBasePlans { get; set; }
    public int AddOnPlans { get; set; }
    public decimal AdoptionRatePercent { get; set; }
    public string Signal { get; set; } = string.Empty;
    public string Commentary { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public DateTime MaterializedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class MarketplaceRolloutPlanCoverageRecord
{
    public int Id { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string PlanCode { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string CoverageMode { get; set; } = string.Empty;
    public int EligibleTenants { get; set; }
    public int ActiveEntitlements { get; set; }
    public int PendingEntitlements { get; set; }
    public decimal PriceMonthly { get; set; }
    public decimal PriceAnnual { get; set; }
    public string Signal { get; set; } = string.Empty;
    public string Commentary { get; set; } = string.Empty;
    public DateTime MaterializedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class MarketplaceRolloutQueueRecord
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string PlanCode { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int PendingModuleCount { get; set; }
    public string PendingModules { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Signal { get; set; } = string.Empty;
    public string? LastEntitlementAction { get; set; }
    public DateTime? LastEntitlementActionAt { get; set; }
    public string RecommendedAction { get; set; } = string.Empty;
    public DateTime MaterializedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
