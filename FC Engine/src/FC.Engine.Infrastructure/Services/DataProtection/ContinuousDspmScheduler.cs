using Cronos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure.Services.DataProtection;

public sealed class ContinuousDspmScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ContinuousDspmOptions _options;
    private readonly ILogger<ContinuousDspmScheduler> _logger;

    public ContinuousDspmScheduler(
        IServiceScopeFactory scopeFactory,
        IOptions<ContinuousDspmOptions> options,
        ILogger<ContinuousDspmScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextAtRest = DateTimeOffset.UtcNow.AddMinutes(1);
        var cron = CronExpression.Parse(_options.ShadowScanCron);
        var nextShadow = cron.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc) ?? DateTimeOffset.UtcNow.AddDays(7);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            if (now >= nextAtRest)
            {
                await ExecuteWatcherAsync("at_rest", stoppingToken);
                nextAtRest = now.AddHours(Math.Max(1, _options.AtRestIntervalHours));
            }

            if (now >= nextShadow)
            {
                await ExecuteWatcherAsync("shadow_copy", stoppingToken);
                nextShadow = cron.GetNextOccurrence(now.AddMinutes(1), TimeZoneInfo.Utc) ?? now.AddDays(7);
            }

            var nextDue = nextAtRest < nextShadow ? nextAtRest : nextShadow;
            var delay = nextDue - DateTimeOffset.UtcNow;
            if (delay < TimeSpan.FromSeconds(30))
            {
                delay = TimeSpan.FromSeconds(30);
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task ExecuteWatcherAsync(string watcherName, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var watcher = scope.ServiceProvider
            .GetRequiredService<IEnumerable<IContinuousDspmWatcher>>()
            .FirstOrDefault(x => x.Name == watcherName);

        if (watcher is null)
        {
            _logger.LogWarning("Continuous DSPM watcher '{WatcherName}' was not registered.", watcherName);
            return;
        }

        try
        {
            await watcher.ExecuteAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Continuous DSPM watcher '{WatcherName}' failed.", watcherName);
        }
    }
}
