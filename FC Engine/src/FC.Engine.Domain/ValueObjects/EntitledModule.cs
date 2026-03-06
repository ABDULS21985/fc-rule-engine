namespace FC.Engine.Domain.ValueObjects;

public class EntitledModule
{
    public int ModuleId { get; init; }
    public int? JurisdictionId { get; init; }
    public string? JurisdictionCode { get; init; }
    public string ModuleCode { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public string RegulatorCode { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public bool IsActive { get; init; }
    public int SheetCount { get; init; }
    public string DefaultFrequency { get; init; } = string.Empty;
}
