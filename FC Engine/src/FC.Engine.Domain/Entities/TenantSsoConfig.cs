namespace FC.Engine.Domain.Entities;

public class TenantSsoConfig
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public bool SsoEnabled { get; set; }
    public string IdpEntityId { get; set; } = string.Empty;
    public string IdpSsoUrl { get; set; } = string.Empty;
    public string? IdpSloUrl { get; set; }
    public string IdpCertificate { get; set; } = string.Empty;
    public string SpEntityId { get; set; } = string.Empty;
    public string AttributeMapping { get; set; } = "{}";
    public string DefaultRole { get; set; } = "Viewer";
    public bool JitProvisioningEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Tenant? Tenant { get; set; }
}
