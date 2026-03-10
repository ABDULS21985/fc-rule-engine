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
                await RunCycleAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Auto-filing scheduler background cycle failed.");
            }
            await Task.Delay(TimeSpan.FromSeconds(60), ct);
        }
    }

    internal async Task RunCycleAsync(CancellationToken ct)
    {
        var dueSchedules = await GetDueScheduleIdsAsync(ct);
        if (dueSchedules.Count == 0)
            return;

        var tasks = dueSchedules
            .Select(scheduleId => ExecuteScheduleInOwnScopeAsync(scheduleId, ct))
            .ToArray();

        await Task.WhenAll(tasks);
    }

    private async Task<IReadOnlyList<int>> GetDueScheduleIdsAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

        using var conn = await db.OpenAsync(ct);
        var dueSchedules = await conn.QueryAsync<int>(
            """
            SELECT Id
            FROM   CaaSAutoFilingSchedules
            WHERE  IsActive = 1
              AND  NextRunAt <= SYSUTCDATETIME()
            """);

        return dueSchedules.ToList();
    }

    private async Task ExecuteScheduleInOwnScopeAsync(int scheduleId, CancellationToken ct)
    {
        try
        {
            await using var scope = _services.CreateAsyncScope();
            var filingService = scope.ServiceProvider.GetRequiredService<ICaaSAutoFilingService>();
            await filingService.ExecuteScheduleAsync(scheduleId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogError(ex, "Auto-filing schedule {Id} failed.", scheduleId);
        }
    }
}
