using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.BackgroundJobs;

public class UsageTrackingJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<UsageTrackingJob> _logger;

    public UsageTrackingJob(
        IServiceProvider serviceProvider,
        ILogger<UsageTrackingJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextRun();
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                await SnapshotUsage(stoppingToken);
            }
        }
    }

    private static TimeSpan GetDelayUntilNextRun()
    {
        var lagosTz = ResolveLagosTimeZone();
        var nowUtc = DateTimeOffset.UtcNow;
        var nowLagos = TimeZoneInfo.ConvertTime(nowUtc, lagosTz);

        var nextRunLagos = new DateTimeOffset(
            nowLagos.Year,
            nowLagos.Month,
            nowLagos.Day,
            2,
            0,
            0,
            nowLagos.Offset);

        if (nowLagos >= nextRunLagos)
        {
            nextRunLagos = nextRunLagos.AddDays(1);
        }

        var nextRunUtc = nextRunLagos.ToUniversalTime();
        var delay = nextRunUtc - nowUtc;
        return delay > TimeSpan.Zero ? delay : TimeSpan.FromMinutes(5);
    }

    private async Task SnapshotUsage(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();

            var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

            var subscriptions = await db.Subscriptions
                .Include(s => s.Plan)
                .Where(s => s.Status != SubscriptionStatus.Cancelled && s.Status != SubscriptionStatus.Expired)
                .ToListAsync(ct);

            foreach (var subscription in subscriptions)
            {
                var tenantId = subscription.TenantId;

                var activeUsers = await db.InstitutionUsers
                    .Where(u => u.TenantId == tenantId && u.IsActive)
                    .CountAsync(ct);

                var activeEntities = await db.Institutions
                    .Where(i => i.TenantId == tenantId && i.IsActive)
                    .CountAsync(ct);

                var activeModules = await db.SubscriptionModules
                    .Where(sm => sm.SubscriptionId == subscription.Id && sm.IsActive)
                    .CountAsync(ct);

                var returnsSubmitted = await db.Submissions
                    .Where(s => s.TenantId == tenantId && s.SubmittedAt.HasValue && DateOnly.FromDateTime(s.SubmittedAt!.Value.Date) == today)
                    .CountAsync(ct);

                var submissionCount = await db.Submissions
                    .Where(s => s.TenantId == tenantId)
                    .CountAsync(ct);

                // Approximate storage as 0.5MB per submission until object storage billing is introduced.
                var storageMb = Math.Round(submissionCount * 0.5m, 2);

                var record = await db.UsageRecords
                    .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.RecordDate == today, ct);

                if (record is null)
                {
                    record = new UsageRecord
                    {
                        TenantId = tenantId,
                        RecordDate = today
                    };
                    db.UsageRecords.Add(record);
                }

                record.ActiveUsers = activeUsers;
                record.ActiveEntities = activeEntities;
                record.ActiveModules = activeModules;
                record.ReturnsSubmitted = returnsSubmitted;
                record.StorageUsedMb = storageMb;
                // API metering will be wired in RG-06; keep zero for now.
                record.ApiCallCount = 0;

                await CreateLimitWarningsIfNeeded(db, subscription, record, ct);
            }

            await db.SaveChangesAsync(ct);
            _logger.LogInformation("UsageTrackingJob completed usage snapshot for {Count} subscriptions", subscriptions.Count);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "UsageTrackingJob failed while snapshotting tenant usage");
        }
    }

    private static TimeZoneInfo ResolveLagosTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static async Task CreateLimitWarningsIfNeeded(
        MetadataDbContext db,
        Subscription subscription,
        UsageRecord usage,
        CancellationToken ct)
    {
        var plan = subscription.Plan;
        if (plan is null)
        {
            return;
        }

        var warnings = new List<string>();

        if (usage.ActiveUsers > plan.MaxUsersPerEntity)
        {
            warnings.Add($"Users limit exceeded ({usage.ActiveUsers}/{plan.MaxUsersPerEntity})");
        }

        if (usage.ActiveEntities > plan.MaxEntities)
        {
            warnings.Add($"Entities limit exceeded ({usage.ActiveEntities}/{plan.MaxEntities})");
        }

        if (usage.ActiveModules > plan.MaxModules)
        {
            warnings.Add($"Modules limit exceeded ({usage.ActiveModules}/{plan.MaxModules})");
        }

        if (plan.MaxApiCallsPerMonth > 0 && usage.ApiCallCount > plan.MaxApiCallsPerMonth)
        {
            warnings.Add($"API calls limit exceeded ({usage.ApiCallCount}/{plan.MaxApiCallsPerMonth})");
        }

        if (usage.StorageUsedMb > plan.MaxStorageMb)
        {
            warnings.Add($"Storage limit exceeded ({usage.StorageUsedMb}MB/{plan.MaxStorageMb}MB)");
        }

        if (warnings.Count == 0)
        {
            return;
        }

        var institutionId = await db.Institutions
            .Where(i => i.TenantId == subscription.TenantId)
            .OrderBy(i => i.Id)
            .Select(i => i.Id)
            .FirstOrDefaultAsync(ct);

        if (institutionId <= 0)
        {
            return;
        }

        var today = usage.RecordDate;
        var existing = await db.PortalNotifications.AnyAsync(n =>
            n.TenantId == subscription.TenantId
            && n.InstitutionId == institutionId
            && n.Type == NotificationType.SystemAnnouncement
            && n.Title == "Plan Limit Warning"
            && DateOnly.FromDateTime(n.CreatedAt.Date) == today,
            ct);

        if (existing)
        {
            return;
        }

        db.PortalNotifications.Add(new PortalNotification
        {
            TenantId = subscription.TenantId,
            InstitutionId = institutionId,
            Type = NotificationType.SystemAnnouncement,
            Title = "Plan Limit Warning",
            Message = string.Join("; ", warnings),
            CreatedAt = DateTime.UtcNow
        });
    }
}
