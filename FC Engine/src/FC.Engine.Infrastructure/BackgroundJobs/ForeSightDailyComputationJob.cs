using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.BackgroundJobs;

public sealed class ForeSightDailyComputationJob : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ForeSightDailyComputationJob> _logger;

    public ForeSightDailyComputationJob(IServiceProvider services, ILogger<ForeSightDailyComputationJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var nextRun = now.Date.AddHours(5);
                if (nextRun <= now)
                {
                    nextRun = nextRun.AddDays(1);
                }

                await Task.Delay(nextRun - now, stoppingToken);

                await using var scope = _services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
                var foresight = scope.ServiceProvider.GetRequiredService<IForeSightService>();

                var tenantIds = await db.Tenants
                    .AsNoTracking()
                    .Where(x => x.Status == TenantStatus.Active)
                    .Select(x => x.TenantId)
                    .ToListAsync(stoppingToken);

                foreach (var tenantId in tenantIds)
                {
                    try
                    {
                        await foresight.RunAllPredictionsAsync(tenantId, "FORESIGHT_JOB", stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "ForeSight daily run failed for tenant {TenantId}.", tenantId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "ForeSight background run crashed. Retrying in one hour.");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }
}
