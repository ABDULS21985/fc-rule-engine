namespace FC.Engine.Domain.Entities;

public class WebhookDelivery
{
    public long Id { get; set; }
    public int EndpointId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public int? HttpStatus { get; set; }
    public string? ResponseBody { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public DateTime? NextRetryAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public int? DurationMs { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public WebhookEndpoint? Endpoint { get; set; }
}
