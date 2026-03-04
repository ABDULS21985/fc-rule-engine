namespace FC.Engine.Domain.Entities;

public class LicenceModuleMatrix
{
    public int Id { get; set; }
    public int LicenceTypeId { get; set; }
    public int ModuleId { get; set; }
    public bool IsRequired { get; set; }
    public bool IsOptional { get; set; } = true;

    // Navigation
    public LicenceType? LicenceType { get; set; }
    public Module? Module { get; set; }
}
