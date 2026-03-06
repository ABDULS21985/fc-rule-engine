using FC.Engine.Domain.Metadata;
using FC.Engine.Domain.Validation;

namespace FC.Engine.Domain.Entities;

public class Module
{
    public int Id { get; set; }
    public int? JurisdictionId { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SheetCount { get; set; }
    public string DefaultFrequency { get; set; } = "Monthly";
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    /// <summary>Custom deadline offset in days from period end. Null uses frequency defaults (Monthly=30, Quarterly=45, SemiAnnual=60, Annual=90).</summary>
    public int? DeadlineOffsetDays { get; set; }

    public DateTime CreatedAt { get; set; }

    // Navigation
    public Jurisdiction? Jurisdiction { get; set; }
    public List<LicenceModuleMatrix> LicenceModuleEntries { get; set; } = new();
    public List<PlanModulePricing> PlanModulePricing { get; set; } = new();
    public List<SubscriptionModule> SubscriptionModules { get; set; } = new();
    public List<ReturnTemplate> Templates { get; set; } = new();
    public List<CrossSheetRule> CrossSheetRules { get; set; } = new();
    public List<CrossSheetRule> SourceCrossSheetRules { get; set; } = new();
    public List<CrossSheetRule> TargetCrossSheetRules { get; set; } = new();
    public List<InterModuleDataFlow> OutboundDataFlows { get; set; } = new();
    public List<InterModuleDataFlow> InboundDataFlows { get; set; } = new();
    public List<ModuleVersion> Versions { get; set; } = new();
}
