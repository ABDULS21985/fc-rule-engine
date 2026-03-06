using FC.Engine.Domain.Entities;

namespace FC.Engine.Domain.Abstractions;

public interface IWebhookService
{
    Task<WebhookEndpoint> CreateEndpointAsync(Guid tenantId, string url, string? description,
        List<string> eventTypes, int createdBy, CancellationToken ct = default);
    Task<WebhookEndpoint?> GetEndpointAsync(int id, CancellationToken ct = default);
    Task<List<WebhookEndpoint>> GetEndpointsAsync(Guid tenantId, CancellationToken ct = default);
    Task UpdateEndpointAsync(int id, string? url, string? description,
        List<string>? eventTypes, bool? isActive, CancellationToken ct = default);
    Task DeleteEndpointAsync(int id, CancellationToken ct = default);
    Task<string> RotateSecretAsync(int id, CancellationToken ct = default);
    Task<List<WebhookDelivery>> GetDeliveryLogAsync(int endpointId, int take = 50,
        CancellationToken ct = default);
    Task SendTestWebhookAsync(int endpointId, CancellationToken ct = default);
    Task DeliverAsync(WebhookEndpoint endpoint, string eventType, object eventData,
        CancellationToken ct = default);
    Task RetryDeliveryAsync(WebhookDelivery delivery, CancellationToken ct = default);
}
