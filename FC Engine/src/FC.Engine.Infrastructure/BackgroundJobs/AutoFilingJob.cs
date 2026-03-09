using Dapper;
using FC.Engine.Domain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.BackgroundJobs;

/// <summary>
/// Processes pending webhook deliveries every 30 seconds.
/// Uses scoped service lifetime via IServiceProvider.CreateAsyncScope().
/// </summary>
public sealed class WebhookDispatcherBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<WebhookDispatcherBackgroundService> _log;

    public WebhookDispatcherBackgroundService(
        IServiceProvider services,
        ILogger<WebhookDispatcherBackgroundService> log)
    {
        _services = services;
        _log      = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = _services.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider
                    .GetRequiredService<ICaaSWebhookDispatcher>();
                await dispatcher.ProcessPendingAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Webhook dispatcher background cycle failed.");
            }
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }
}

/// <summary>
/// Polls CaaSAutoFilingSchedules for due schedules every 60 seconds.
/// Fires each due schedule asynchronously; failures are logged but do not
/// block subsequent schedules in the same cycle.
/// </summary>
public sealed class AutoFilingSchedulerBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AutoFilingSchedulerBackgroundService> _log;

    public AutoFilingSchedulerBackgroundService(
        IServiceProvider services,
        ILogger<AutoFilingSchedulerBackgroundService> log)
    {
        _services = services;
        _log      = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = _services.CreateAsyncScope();
                var db            = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
                var filingService = scope.ServiceProvider.GetRequiredService<ICaaSAutoFilingService>();

                using var conn = await db.OpenAsync(ct);
                var dueSchedules = await conn.QueryAsync<int>(
                    """
                    SELECT Id
                    FROM   CaaSAutoFilingSchedules
                    WHERE  IsActive = 1
                      AND  NextRunAt <= SYSUTCDATETIME()
                    """);

                foreach (var scheduleId in dueSchedules)
                {
                    _ = filingService.ExecuteScheduleAsync(scheduleId, ct)
                        .ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                                _log.LogError(t.Exception,
                                    "Auto-filing schedule {Id} failed.", scheduleId);
                        }, TaskScheduler.Default);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Auto-filing scheduler background cycle failed.");
            }
            await Task.Delay(TimeSpan.FromSeconds(60), ct);
        }
    }
}
