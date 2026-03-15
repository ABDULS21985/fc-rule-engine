using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Events;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.BackgroundJobs;

/// <summary>
/// Computes weekly CHS snapshots for all active tenants, persists them,
/// and publishes ComplianceScoreChangedEvent when the rating band changes.
/// </summary>
public class ChsComputationJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChsComputationJob> _logger;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    public ChsComputationJob(IServiceScopeFactory scopeFactory, ILogger<ChsComputationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ChsComputationJob started — checking every {Interval}", CheckInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ComputeWeeklySnapshots(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChsComputationJob iteration failed");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task ComputeWeeklySnapshots(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
        var chsService = scope.ServiceProvider.GetRequiredService<IComplianceHealthService>();
        var eventPublisher = scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>();

        var now = DateTime.UtcNow;
        var currentPeriodLabel = $"{now.Year}-W{GetIsoWeek(now):D2}";

        var tenantIds = await db.Tenants
            .AsNoTracking()
            .Where(t => t.Status == TenantStatus.Active)
            .Select(t => t.TenantId)
            .ToListAsync(ct);

        _logger.LogInformation("CHS computation: {Count} active tenants, period {Period}",
            tenantIds.Count, currentPeriodLabel);

        var computed = 0;

        foreach (var tenantId in tenantIds)
        {
            var exists = await db.ChsScoreSnapshots
                .AnyAsync(s => s.TenantId == tenantId && s.PeriodLabel == currentPeriodLabel, ct);

            if (exists) continue;

            try
            {
                var score = await chsService.GetCurrentScore(tenantId, ct);

                var snapshot = new ChsScoreSnapshot
                {
                    TenantId = tenantId,
                    PeriodLabel = currentPeriodLabel,
                    ComputedAt = now,
                    OverallScore = score.OverallScore,
                    Rating = (int)score.Rating,
                    FilingTimeliness = score.FilingTimeliness,
                    DataQuality = score.DataQuality,
                    RegulatoryCapital = score.RegulatoryCapital,
                    AuditGovernance = score.AuditGovernance,
                    Engagement = score.Engagement
                };

                db.ChsScoreSnapshots.Add(snapshot);
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex) when (IsUniquePeriodViolation(ex))
                {
                    db.Entry(snapshot).State = EntityState.Detached;
                    _logger.LogDebug(
                        "CHS snapshot for tenant {TenantId} and period {Period} was already created by another worker.",
                        tenantId,
                        currentPeriodLabel);
                    continue;
                }

                computed++;

                // Detect rating band change
                var previousSnapshot = await db.ChsScoreSnapshots
                    .Where(s => s.TenantId == tenantId && s.PeriodLabel != currentPeriodLabel)
                    .OrderByDescending(s => s.ComputedAt)
                    .FirstOrDefaultAsync(ct);

                if (previousSnapshot is not null && previousSnapshot.Rating != (int)score.Rating)
                {
                    var domainEvent = new ComplianceScoreChangedEvent(
                        TenantId: tenantId,
                        PreviousScore: previousSnapshot.OverallScore,
                        NewScore: score.OverallScore,
                        Rating: ComplianceHealthService.RatingLabel(score.Rating),
                        Trend: score.Trend.ToString(),
                        ComputedAt: now,
                        OccurredAt: now,
                        CorrelationId: Guid.NewGuid());

                    await eventPublisher.PublishAsync(domainEvent, ct);

                    _logger.LogInformation(
                        "CHS band change for tenant {TenantId}: {OldRating} -> {NewRating} ({OldScore} -> {NewScore})",
                        tenantId, previousSnapshot.Rating, (int)score.Rating,
                        previousSnapshot.OverallScore, score.OverallScore);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CHS computation failed for tenant {TenantId}", tenantId);
            }
        }

        if (computed > 0)
            _logger.LogInformation("CHS computation complete: {Computed} new snapshots for period {Period}",
                computed, currentPeriodLabel);
    }

    private static int GetIsoWeek(DateTime date) =>
        System.Globalization.CultureInfo.InvariantCulture.Calendar
            .GetWeekOfYear(date, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);

    private static bool IsUniquePeriodViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx && sqlEx.Number is 2601 or 2627;
}
