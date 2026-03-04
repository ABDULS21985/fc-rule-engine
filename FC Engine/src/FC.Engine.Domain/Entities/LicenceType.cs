namespace FC.Engine.Domain.Entities;

public class LicenceType
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Regulator { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public List<LicenceModuleMatrix> LicenceModuleEntries { get; set; } = new();
    public List<TenantLicenceType> TenantLicenceTypes { get; set; } = new();
}
