using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Admin.Services;

public sealed class PlatformIntelligenceRefreshJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PlatformIntelligenceRefreshJob> _logger;

    public PlatformIntelligenceRefreshJob(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<PlatformIntelligenceRefreshJob> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = Math.Max(5, _configuration.GetValue("PlatformIntelligenceRefresh:IntervalMinutes", 60));
        var runOnStartup = _configuration.GetValue("PlatformIntelligenceRefresh:RunOnStartup", true);
        var interval = TimeSpan.FromMinutes(intervalMinutes);

        if (runOnStartup)
        {
            await RunRefreshCycleAsync(stoppingToken);
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

            await RunRefreshCycleAsync(stoppingToken);
        }
    }

    private async Task RunRefreshCycleAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var refreshService = scope.ServiceProvider.GetRequiredService<PlatformIntelligenceRefreshService>();
            var result = await refreshService.RefreshAsync(ct);

            _logger.LogInformation(
                "Platform intelligence refresh completed at {GeneratedAt}. Institutions={InstitutionCount} Interventions={InterventionCount} Timeline={TimelineCount} DashboardPacks={DashboardPacksMaterialized}",
                result.GeneratedAt,
                result.InstitutionCount,
                result.InterventionCount,
                result.TimelineCount,
                result.DashboardPacksMaterialized);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Platform intelligence refresh failed.");
        }
    }
}
