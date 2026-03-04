namespace FC.Engine.Domain.Entities;

public class Module
{
    public int Id { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SheetCount { get; set; }
    public string DefaultFrequency { get; set; } = "Monthly";
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public List<LicenceModuleMatrix> LicenceModuleEntries { get; set; } = new();
}
