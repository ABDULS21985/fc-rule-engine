namespace FC.Engine.Domain.Entities;

public class UserMfaConfig
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public int UserId { get; set; }
    public string UserType { get; set; } = string.Empty; // InstitutionUser | PortalUser
    public string SecretKey { get; set; } = string.Empty; // Base32
    public string BackupCodes { get; set; } = "[]"; // JSON (hashed codes)
    public bool IsEnabled { get; set; }
    public DateTime? EnabledAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
