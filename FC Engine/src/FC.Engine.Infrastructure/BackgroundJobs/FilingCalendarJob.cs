using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Domain.Notifications;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.BackgroundJobs;

/// <summary>
/// RG-12: Filing Calendar background job.
/// Runs hourly to:
/// 1. Generate upcoming ReturnPeriods for all active tenant × module combinations
/// 2. Check deadlines and escalate notifications
/// 3. Auto-create draft returns at T-60
/// </summary>
public class FilingCalendarJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FilingCalendarJob> _logger;

    public FilingCalendarJob(
        IServiceProvider serviceProvider,
        ILogger<FilingCalendarJob> logger)
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
                await RunCycle(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "FilingCalendarJob cycle failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunCycle(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
        var entitlementService = scope.ServiceProvider.GetRequiredService<IEntitlementService>();
        var deadlineService = scope.ServiceProvider.GetRequiredService<DeadlineComputationService>();
        var notifications = scope.ServiceProvider.GetService<INotificationOrchestrator>();

        var periodsGenerated = await GenerateUpcomingPeriods(db, entitlementService, deadlineService, ct);
        var draftsCreated = await AutoCreateDraftReturns(db, notifications, ct);
        var escalated = await CheckDeadlinesAndEscalate(db, notifications, ct);

        _logger.LogInformation(
            "FilingCalendarJob: {PeriodsGenerated} periods generated, {Escalated} escalations, {DraftsCreated} drafts created",
            periodsGenerated, escalated, draftsCreated);
    }

    /// <summary>
    /// Ensure next 12 months of ReturnPeriods exist for every active tenant × active module.
    /// </summary>
    private async Task<int> GenerateUpcomingPeriods(
        MetadataDbContext db,
        IEntitlementService entitlementService,
        DeadlineComputationService deadlineService,
        CancellationToken ct)
    {
        var activeTenantIds = await db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial)
            .Select(s => s.TenantId)
            .Distinct()
            .ToListAsync(ct);

        var count = 0;

        foreach (var tenantId in activeTenantIds)
        {
            try
            {
                var entitlement = await entitlementService.ResolveEntitlements(tenantId, ct);

                foreach (var entitledModule in entitlement.ActiveModules)
                {
                    var dbModule = await db.Modules.FindAsync(new object[] { entitledModule.ModuleId }, ct);
                    if (dbModule is null) continue;

                    var periods = deadlineService.GeneratePeriodsForNext12Months(dbModule);

                    foreach (var period in periods)
                    {
                        var exists = await db.ReturnPeriods.AnyAsync(
                            rp => rp.TenantId == tenantId
                               && rp.ModuleId == entitledModule.ModuleId
                               && rp.Year == period.Year
                               && rp.Month == period.Month
                               && rp.Quarter == period.Quarter, ct);

                        if (!exists)
                        {
                            var deadline = deadlineService.ComputeDeadline(dbModule, period);
                            period.TenantId = tenantId;
                            period.ModuleId = entitledModule.ModuleId;
                            period.DeadlineDate = deadline;
                            period.Status = "Upcoming";
                            db.ReturnPeriods.Add(period);
                            count++;
                        }
                    }
                }

                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate periods for tenant {TenantId}", tenantId);
            }
        }

        return count;
    }

    /// <summary>
    /// Check all open periods against deadlines and trigger notification escalation.
    /// </summary>
    private async Task<int> CheckDeadlinesAndEscalate(
        MetadataDbContext db,
        INotificationOrchestrator? notifications,
        CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var escalationCount = 0;

        var openPeriods = await db.ReturnPeriods
            .Include(rp => rp.Module)
            .Where(rp => rp.ModuleId != null
                      && rp.Status != "Completed"
                      && rp.Status != "Closed")
            .ToListAsync(ct);

        foreach (var period in openPeriods)
        {
            if (period.Module is null) continue;

            var effectiveDeadline = period.EffectiveDeadline;
            var daysToDeadline = (effectiveDeadline.Date - today).Days;

            // Check if return already submitted for this period
            var submitted = await db.Submissions.AnyAsync(
                s => s.TenantId == period.TenantId
                  && s.ReturnPeriodId == period.Id
                  && s.Status != SubmissionStatus.Draft
                  && s.Status != SubmissionStatus.Rejected
                  && s.Status != SubmissionStatus.ApprovalRejected, ct);

            if (submitted)
            {
                period.Status = "Completed";
                continue;
            }

            // Determine new notification level
            var newLevel = daysToDeadline switch
            {
                <= 0 => 6,    // Overdue
                1 => 5,       // T-1
                <= 3 => 4,    // T-3
                <= 7 => 3,    // T-7
                <= 14 => 2,   // T-14
                <= 30 => 1,   // T-30
                _ => 0
            };

            // Update status
            period.Status = daysToDeadline switch
            {
                <= 0 => "Overdue",
                <= 7 => "DueSoon",
                <= 60 => "Open",
                _ => "Upcoming"
            };

            var overdueReminderSentToday = false;
            if (newLevel == 6 && period.NotificationLevel == 6)
            {
                overdueReminderSentToday = await HasOverdueReminderSentToday(db, period, ct);
            }

            // Escalate on level increase, and re-send overdue once per day until submitted.
            var shouldNotify = ShouldTriggerEscalation(
                period.NotificationLevel,
                newLevel,
                overdueReminderSentToday);

            if (shouldNotify && notifications is not null)
            {
                if (newLevel > period.NotificationLevel)
                {
                    period.NotificationLevel = newLevel;
                }
                await TriggerEscalationNotification(notifications, period, daysToDeadline, newLevel, ct);
                escalationCount++;
            }
        }

        await db.SaveChangesAsync(ct);
        return escalationCount;
    }

    private static async Task TriggerEscalationNotification(
        INotificationOrchestrator notifications,
        ReturnPeriod period,
        int daysToDeadline,
        int level,
        CancellationToken ct)
    {
        var eventType = level switch
        {
            1 => NotificationEvents.DeadlineT30,
            2 => NotificationEvents.DeadlineT14,
            3 => NotificationEvents.DeadlineT7,
            4 => NotificationEvents.DeadlineT3,
            5 => NotificationEvents.DeadlineT1,
            6 => NotificationEvents.DeadlineOverdue,
            _ => null
        };
        if (eventType is null) return;

        var priority = level switch
        {
            >= 5 => NotificationPriority.Critical,
            >= 3 => NotificationPriority.High,
            >= 2 => NotificationPriority.Normal,
            _ => NotificationPriority.Low
        };

        var periodLabel = FilingCalendarService.FormatPeriod(period);

        await notifications.Notify(new NotificationRequest
        {
            TenantId = period.TenantId,
            EventType = eventType,
            Title = level >= 6
                ? $"OVERDUE: {period.Module!.ModuleName} Return"
                : $"{Math.Abs(daysToDeadline)} Days Until {period.Module!.ModuleName} Deadline",
            Message = $"{period.Module.ModuleName} return for {periodLabel} is due {period.EffectiveDeadline:dd MMM yyyy}.",
            Priority = priority,
            IsMandatory = level >= 5,
            RecipientRoles = level switch
            {
                1 => new List<string> { "Maker" },
                2 => new List<string> { "Maker", "Admin" },
                3 or 4 => new List<string> { "Maker", "Checker", "Admin" },
                _ => new List<string> { "Maker", "Checker", "Approver", "Admin" }
            },
            ActionUrl = $"/returns/create?module={period.Module.ModuleCode}&period={period.Id}",
            Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PeriodId"] = period.Id.ToString(),
                ["ModuleCode"] = period.Module.ModuleCode,
                ["ModuleName"] = period.Module.ModuleName,
                ["PeriodLabel"] = periodLabel,
                ["Deadline"] = period.EffectiveDeadline.ToString("dd MMM yyyy"),
                ["DaysRemaining"] = daysToDeadline.ToString()
            }
        }, ct);
    }

    internal static bool ShouldTriggerEscalation(int currentLevel, int newLevel, bool overdueReminderSentToday)
    {
        if (newLevel > currentLevel)
        {
            return true;
        }

        if (newLevel == 6 && currentLevel == 6 && !overdueReminderSentToday)
        {
            return true;
        }

        return false;
    }

    private static async Task<bool> HasOverdueReminderSentToday(
        MetadataDbContext db,
        ReturnPeriod period,
        CancellationToken ct)
    {
        var todayStart = DateTime.UtcNow.Date;
        var periodToken = $"\"PeriodId\":\"{period.Id}\"";

        var inAppSent = await db.PortalNotifications.AnyAsync(n =>
            n.TenantId == period.TenantId &&
            n.EventType == NotificationEvents.DeadlineOverdue &&
            n.CreatedAt >= todayStart &&
            n.Metadata != null &&
            n.Metadata.Contains(periodToken), ct);

        if (inAppSent)
        {
            return true;
        }

        return await db.NotificationDeliveries.AnyAsync(d =>
            d.TenantId == period.TenantId &&
            d.NotificationEventType == NotificationEvents.DeadlineOverdue &&
            d.CreatedAt >= todayStart &&
            d.Payload != null &&
            d.Payload.Contains(periodToken), ct);
    }

    /// <summary>
    /// At T-60, auto-create a Draft return for the period.
    /// </summary>
    private async Task<int> AutoCreateDraftReturns(
        MetadataDbContext db,
        INotificationOrchestrator? notifications,
        CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var count = 0;

        var periods = await db.ReturnPeriods
            .Where(rp => rp.AutoCreatedReturnId == null
                      && rp.ModuleId != null
                      && rp.Status != "Completed"
                      && rp.Status != "Closed")
            .Include(rp => rp.Module)
            .ToListAsync(ct);

        // Filter in memory: periods within T-60
        var eligiblePeriods = periods
            .Where(rp => (rp.EffectiveDeadline.Date - today).Days <= 60)
            .ToList();

        foreach (var period in eligiblePeriods)
        {
            if (period.Module is null) continue;

            var existingSubmissionId = await db.Submissions
                .Where(s => s.TenantId == period.TenantId && s.ReturnPeriodId == period.Id)
                .OrderByDescending(s => s.Id)
                .Select(s => (int?)s.Id)
                .FirstOrDefaultAsync(ct);

            if (existingSubmissionId.HasValue)
            {
                period.AutoCreatedReturnId = existingSubmissionId.Value;
                if (period.Status == "Upcoming")
                {
                    period.Status = "Open";
                }
                continue;
            }

            // Get primary template for this module
            var primaryTemplate = await db.ReturnTemplates
                .Include(t => t.Module)
                .Where(t => t.ModuleId == period.ModuleId)
                .OrderBy(t => t.Id)
                .FirstOrDefaultAsync(ct);

            var returnCode = primaryTemplate?.ReturnCode ?? period.Module.ModuleCode;
            var periodLabel = FilingCalendarService.FormatPeriod(period);

            // Find the latest published template version
            int? templateVersionId = null;
            if (primaryTemplate is not null)
            {
                templateVersionId = await db.TemplateVersions
                    .Where(v => v.TemplateId == primaryTemplate.Id && v.Status == TemplateStatus.Published)
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => (int?)v.Id)
                    .FirstOrDefaultAsync(ct);
            }

            var submission = new Submission
            {
                TenantId = period.TenantId,
                InstitutionId = 0, // Will be set when user opens the return
                ReturnPeriodId = period.Id,
                ReturnCode = returnCode,
                TemplateVersionId = templateVersionId,
                Status = SubmissionStatus.Draft,
                SubmittedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            db.Submissions.Add(submission);
            await db.SaveChangesAsync(ct);

            period.AutoCreatedReturnId = submission.Id;
            period.Status = "Open";
            await db.SaveChangesAsync(ct);

            count++;

            // Notify Makers
            if (notifications is not null)
            {
                await notifications.Notify(new NotificationRequest
                {
                    TenantId = period.TenantId,
                    EventType = NotificationEvents.ReturnCreated,
                    Title = $"New {period.Module.ModuleName} Return Ready",
                    Message = $"A draft return for {periodLabel} has been created. Data entry can begin.",
                    Priority = NotificationPriority.Normal,
                    RecipientRoles = new List<string> { "Maker", "Admin" },
                    ActionUrl = $"/returns/{submission.Id}/edit",
                    Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ModuleName"] = period.Module.ModuleName,
                        ["PeriodLabel"] = periodLabel
                    }
                }, ct);
            }
        }

        await db.SaveChangesAsync(ct);
        return count;
    }
}
