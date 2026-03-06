using FC.Engine.Domain.Events;

namespace FC.Engine.Domain.Abstractions;

public interface IDomainEventPublisher
{
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default)
        where TEvent : class, IDomainEvent;
}
