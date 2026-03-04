namespace FC.Engine.Domain.Entities;

public class TenantLicenceType
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public int LicenceTypeId { get; set; }
    public string? RegistrationNumber { get; set; }
    public DateTime EffectiveDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public Tenant? Tenant { get; set; }
    public LicenceType? LicenceType { get; set; }
}
