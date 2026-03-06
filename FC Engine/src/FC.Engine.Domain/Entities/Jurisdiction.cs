namespace FC.Engine.Domain.Entities;

public class Jurisdiction
{
    public int Id { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;
    public string RegulatoryBodies { get; set; } = "[]";
    public string DateFormat { get; set; } = "dd/MM/yyyy";
    public string? DataProtectionLaw { get; set; }
    public string DataResidencyRegion { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    public List<Module> Modules { get; set; } = new();
    public List<Institution> Institutions { get; set; } = new();
}
