using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FC.Engine.Domain.Models;

namespace FC.Engine.Infrastructure.Services;

/// <summary>
/// Monthly background job that captures predicted vs actual impact for all enacted policies.
/// </summary>
public sealed class HistoricalImpactTrackerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HistoricalImpactTrackerWorker> _log;
    private readonly int _intervalDays;

    public HistoricalImpactTrackerWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<HistoricalImpactTrackerWorker> log,
        IOptions<PolicySimulationOptions> options)
    {
        _scopeFactory = scopeFactory;
        _log = log;
        _intervalDays = options.Value.TrackingCycleIntervalDays;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Delay initial run by 1 minute to let the application fully start
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var tracker = scope.ServiceProvider.GetRequiredService<IHistoricalImpactTracker>();
                await tracker.RunTrackingCycleAsync(stoppingToken);
                _log.LogInformation("Historical impact tracking cycle completed.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Unhandled error in historical impact tracker cycle.");
            }

            await Task.Delay(TimeSpan.FromDays(_intervalDays), stoppingToken);
        }
    }
}

/// <summary>
/// Periodically checks for expired consultations and auto-closes them.
/// </summary>
public sealed class ConsultationDeadlineMonitorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConsultationDeadlineMonitorWorker> _log;
    private readonly int _intervalHours;

    public ConsultationDeadlineMonitorWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ConsultationDeadlineMonitorWorker> log,
        IOptions<PolicySimulationOptions> options)
    {
        _scopeFactory = scopeFactory;
        _log = log;
        _intervalHours = options.Value.ConsultationDeadlineCheckIntervalHours;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
                var today = DateOnly.FromDateTime(DateTime.UtcNow);

                // Find open/published consultations past their deadline
                var expiredConsultations = await db.ConsultationRounds
                    .Where(c => (c.Status == ConsultationStatus.Published || c.Status == ConsultationStatus.Open)
                        && c.DeadlineDate < today)
                    .ToListAsync(stoppingToken);

                foreach (var consultation in expiredConsultations)
                {
                    consultation.Status = ConsultationStatus.Closed;
                    consultation.UpdatedAt = DateTime.UtcNow;

                    _log.LogInformation(
                        "Auto-closed expired consultation: Id={Id}, Deadline={Deadline}",
                        consultation.Id, consultation.DeadlineDate);
                }

                if (expiredConsultations.Count > 0)
                    await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Unhandled error in consultation deadline monitor.");
            }

            await Task.Delay(TimeSpan.FromHours(_intervalHours), stoppingToken);
        }
    }
}
