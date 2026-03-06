using FC.Engine.Domain.Abstractions;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Webhooks;

public class WebhookRetryJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WebhookRetryJob> _logger;

    public WebhookRetryJob(
        IServiceProvider serviceProvider,
        ILogger<WebhookRetryJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RetryFailedDeliveries(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Webhook retry cycle failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private async Task RetryFailedDeliveries(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
        var webhookService = scope.ServiceProvider.GetRequiredService<IWebhookService>();

        var now = DateTime.UtcNow;
        var failedDeliveries = await db.WebhookDeliveries
            .Include(d => d.Endpoint)
            .Where(d => d.Status == "Failed"
                && d.NextRetryAt != null
                && d.NextRetryAt <= now
                && d.AttemptCount < d.MaxAttempts)
            .OrderBy(d => d.NextRetryAt)
            .Take(50)
            .ToListAsync(ct);

        if (failedDeliveries.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Retrying {Count} failed webhook deliveries", failedDeliveries.Count);

        foreach (var delivery in failedDeliveries)
        {
            try
            {
                await webhookService.RetryDeliveryAsync(delivery, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Webhook retry failed for delivery {DeliveryId}", delivery.Id);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
