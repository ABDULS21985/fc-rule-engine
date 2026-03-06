using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Events;
using FC.Engine.Infrastructure.Metadata;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Webhooks;

public class WebhookDeliveryConsumer :
    IConsumer<ReturnCreatedEvent>,
    IConsumer<ReturnSubmittedEvent>,
    IConsumer<ReturnApprovedEvent>,
    IConsumer<ReturnRejectedEvent>,
    IConsumer<ReturnSubmittedToRegulatorEvent>,
    IConsumer<ValidationCompletedEvent>,
    IConsumer<DeadlineApproachingEvent>,
    IConsumer<SubscriptionChangedEvent>,
    IConsumer<ModuleActivatedEvent>,
    IConsumer<UserCreatedEvent>,
    IConsumer<ExportCompletedEvent>
{
    private readonly MetadataDbContext _db;
    private readonly IWebhookService _webhookService;
    private readonly ILogger<WebhookDeliveryConsumer> _logger;

    public WebhookDeliveryConsumer(
        MetadataDbContext db,
        IWebhookService webhookService,
        ILogger<WebhookDeliveryConsumer> logger)
    {
        _db = db;
        _webhookService = webhookService;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<ReturnCreatedEvent> context) => DispatchAsync(context.Message, context.CancellationToken);
    public Task Consume(ConsumeContext<ReturnSubmittedEvent> context) => DispatchAsync(context.Message, context.CancellationToken);
    public Task Consume(ConsumeContext<ReturnApprovedEvent> context) => DispatchAsync(context.Message, context.CancellationToken);
    public Task Consume(ConsumeContext<ReturnRejectedEvent> context) => DispatchAsync(context.Message, context.CancellationToken);
    public Task Consume(ConsumeContext<ReturnSubmittedToRegulatorEvent> context) => DispatchAsync(context.Message, context.CancellationToken);
    public Task Consume(ConsumeContext<ValidationCompletedEvent> context) => DispatchAsync(context.Message, context.CancellationToken);
    public Task Consume(ConsumeContext<DeadlineApproachingEvent> context) => DispatchAsync(context.Message, context.CancellationToken);
    public Task Consume(ConsumeContext<SubscriptionChangedEvent> context) => DispatchAsync(context.Message, context.CancellationToken);
    public Task Consume(ConsumeContext<ModuleActivatedEvent> context) => DispatchAsync(context.Message, context.CancellationToken);
    public Task Consume(ConsumeContext<UserCreatedEvent> context) => DispatchAsync(context.Message, context.CancellationToken);
    public Task Consume(ConsumeContext<ExportCompletedEvent> context) => DispatchAsync(context.Message, context.CancellationToken);

    private async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken ct)
    {
        var eventType = domainEvent.EventType;
        var tenantId = domainEvent.TenantId;

        var endpoints = await _db.WebhookEndpoints
            .Where(e => e.TenantId == tenantId && e.IsActive)
            .ToListAsync(ct);

        foreach (var endpoint in endpoints)
        {
            try
            {
                var subscribedEvents = JsonSerializer.Deserialize<List<string>>(endpoint.EventTypes)
                    ?? new List<string>();

                if (subscribedEvents.Contains(eventType, StringComparer.OrdinalIgnoreCase)
                    || subscribedEvents.Contains("*"))
                {
                    await _webhookService.DeliverAsync(endpoint, eventType, domainEvent, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to deliver {EventType} to webhook endpoint {EndpointId}",
                    eventType, endpoint.Id);
            }
        }
    }
}
