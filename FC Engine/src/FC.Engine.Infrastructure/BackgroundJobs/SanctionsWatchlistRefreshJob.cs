using FC.Engine.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.BackgroundJobs;

public sealed class SanctionsWatchlistRefreshJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SanctionsWatchlistRefreshJob> _logger;

    public SanctionsWatchlistRefreshJob(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<SanctionsWatchlistRefreshJob> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalHours = Math.Max(1, _configuration.GetValue("SanctionsWatchlistRefresh:IntervalHours", 24));
        var runOnStartup = _configuration.GetValue("SanctionsWatchlistRefresh:RunOnStartup", true);
        var interval = TimeSpan.FromHours(intervalHours);

        if (runOnStartup)
        {
            await RunRefreshCycleAsync(interval, stoppingToken);
        }

        using var timer = new PeriodicTimer(interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await RunRefreshCycleAsync(interval, stoppingToken);
        }
    }

    private async Task RunRefreshCycleAsync(TimeSpan maxAge, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var refreshService = scope.ServiceProvider.GetRequiredService<SanctionsWatchlistRefreshService>();
            var refreshed = await refreshService.RefreshIfStaleAsync(maxAge, ct);
            _logger.LogInformation(
                "Sanctions watchlist refresh cycle completed. Refreshed={Refreshed} MaxAgeHours={MaxAgeHours}",
                refreshed,
                maxAge.TotalHours);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sanctions watchlist refresh cycle failed.");
        }
    }
}
