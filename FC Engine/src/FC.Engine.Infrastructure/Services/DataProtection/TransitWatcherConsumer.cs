using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Events;
using MassTransit;

namespace FC.Engine.Infrastructure.Services.DataProtection;

public sealed class TransitWatcherConsumer : IConsumer<DataPipelineLifecycleEvent>
{
    private readonly IDataProtectionService _dataProtectionService;

    public TransitWatcherConsumer(IDataProtectionService dataProtectionService)
        => _dataProtectionService = dataProtectionService;

    public Task Consume(ConsumeContext<DataPipelineLifecycleEvent> context)
    {
        if (!context.Message.Status.Equals("running", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        return _dataProtectionService.HandlePipelineLifecycleEventAsync(context.Message, context.CancellationToken);
    }
}
