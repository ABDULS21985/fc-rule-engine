namespace FC.Engine.Domain.Entities;

public class Tenant
{
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string TenantSlug { get; set; } = string.Empty;
    public string TenantStatus { get; set; } = "PendingActivation";
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public List<Institution> Institutions { get; set; } = new();
}
