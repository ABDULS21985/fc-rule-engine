namespace FC.Engine.Domain.Entities;

public class ApiKey
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public string KeyHash { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Permissions { get; set; } // JSON array
    public int RateLimitPerMinute { get; set; } = 100;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public string? LastUsedIp { get; set; }
    public int CreatedBy { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
