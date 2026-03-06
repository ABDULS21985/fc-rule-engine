using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Webhooks;

public class MassTransitDomainEventPublisher : IDomainEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<MassTransitDomainEventPublisher> _logger;

    public MassTransitDomainEventPublisher(
        IPublishEndpoint publishEndpoint,
        ILogger<MassTransitDomainEventPublisher> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default)
        where TEvent : class, IDomainEvent
    {
        _logger.LogInformation(
            "Publishing domain event {EventType} for tenant {TenantId} (correlation: {CorrelationId})",
            domainEvent.EventType, domainEvent.TenantId, domainEvent.CorrelationId);

        await _publishEndpoint.Publish(domainEvent, ct);
    }
}
