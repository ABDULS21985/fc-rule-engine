namespace FC.Engine.Domain.Entities;

public class RefreshToken
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public int UserId { get; set; }
    public string UserType { get; set; } = string.Empty; // InstitutionUser | PortalUser
    public string Token { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedByIp { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedByIp { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public bool IsRevoked { get; set; }
    public bool IsUsed { get; set; }
}
