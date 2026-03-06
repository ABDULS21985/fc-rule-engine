namespace FC.Engine.Domain.Entities;

public class WebhookEndpoint
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string EventTypes { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastDeliveryAt { get; set; }
    public int FailureCount { get; set; }
    public string? DisabledReason { get; set; }
}
