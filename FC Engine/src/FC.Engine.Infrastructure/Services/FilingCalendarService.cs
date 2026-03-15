using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

/// <summary>
/// Implements the filing calendar service: RAG status, deadline overrides, and SLA tracking.
/// </summary>
public class FilingCalendarService : IFilingCalendarService
{
    private readonly MetadataDbContext _db;
    private readonly DeadlineComputationService _deadlineService;
    private readonly ILogger<FilingCalendarService> _logger;

    public FilingCalendarService(
        MetadataDbContext db,
        DeadlineComputationService deadlineService,
        ILogger<FilingCalendarService> logger)
    {
        _db = db;
        _deadlineService = deadlineService;
        _logger = logger;
    }

    /// <inheritdoc />
    public DateTime ComputeDeadline(Module module, ReturnPeriod period)
    {
        return _deadlineService.ComputeDeadline(module, period);
    }

    /// <inheritdoc />
    public async Task<List<RagItem>> GetRagStatus(Guid tenantId, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var items = new List<RagItem>();

        // Get all non-closed periods for this tenant that have a module
        var periods = await _db.ReturnPeriods
            .Include(rp => rp.Module)
            .Where(rp => rp.TenantId == tenantId
                      && rp.ModuleId != null
                      && rp.Status != "Closed")
            .OrderBy(rp => rp.DeadlineDate)
            .ToListAsync(ct);

        // Get submission status for these periods
        var periodIds = periods.Select(p => p.Id).ToList();
        var submissions = await _db.Submissions
            .Where(s => s.TenantId == tenantId && periodIds.Contains(s.ReturnPeriodId))
            .Select(s => new { s.ReturnPeriodId, s.Status })
            .ToListAsync(ct);

        var submissionLookup = submissions
            .GroupBy(s => s.ReturnPeriodId)
            .ToDictionary(g => g.Key, g => g.Select(s => s.Status).ToList());

        foreach (var period in periods)
        {
            if (period.Module is null) continue;

            var effectiveDeadline = period.EffectiveDeadline;
            var hasSubmitted = submissionLookup.TryGetValue(period.Id, out var statuses)
                && statuses.Any(s => s == SubmissionStatus.Accepted || s == SubmissionStatus.AcceptedWithWarnings);
            var inReview = statuses?.Any(s => s == SubmissionStatus.PendingApproval || s == SubmissionStatus.Validating) == true;

            var color = ComputeRagColor(today, effectiveDeadline, period.ReportingDate, hasSubmitted, inReview, period.Status);

            items.Add(new RagItem
            {
                ModuleName = period.Module.ModuleName,
                ModuleCode = period.Module.ModuleCode,
                PeriodLabel = FormatPeriod(period),
                StatusLabel = hasSubmitted ? "Submitted" : period.Status,
                Deadline = effectiveDeadline,
                Color = color
            });
        }

        return items;
    }

    /// <inheritdoc />
    public async Task OverrideDeadline(Guid tenantId, int periodId, DateTime newDeadline, string reason, int overrideByUserId, CancellationToken ct = default)
    {
        var period = await _db.ReturnPeriods.FirstOrDefaultAsync(
            rp => rp.Id == periodId && rp.TenantId == tenantId, ct);

        if (period is null)
            throw new InvalidOperationException($"Period {periodId} not found for tenant {tenantId}");

        period.DeadlineOverrideDate = newDeadline;
        period.DeadlineOverrideBy = overrideByUserId;
        period.DeadlineOverrideReason = reason;
        period.NotificationLevel = 0; // Reset escalation for new deadline

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Deadline override: Tenant {TenantId}, Period {PeriodId}, New deadline {Deadline}, Reason: {Reason}",
            tenantId, periodId, newDeadline, reason);
    }

    /// <inheritdoc />
    public async Task RecordSla(int periodId, int submissionId, CancellationToken ct = default)
    {
        var period = await _db.ReturnPeriods
            .Include(rp => rp.Module)
            .FirstOrDefaultAsync(rp => rp.Id == periodId, ct);

        if (period?.ModuleId is null) return;

        var submission = await _db.Submissions.FindAsync(new object[] { submissionId }, ct);
        if (submission is null) return;

        var effectiveDeadline = period.EffectiveDeadline;
        var submittedDate = (submission.SubmittedAt ?? DateTime.UtcNow).Date;
        var daysToDeadline = (effectiveDeadline.Date - submittedDate).Days;
        var periodEndDate = DeadlineComputationService.GetPeriodEndDate(
            period.Frequency, period.Year, period.Month, period.Quarter);

        var existing = await _db.FilingSlaRecords.FirstOrDefaultAsync(
            s => s.TenantId == period.TenantId && s.ModuleId == period.ModuleId.Value && s.PeriodId == periodId, ct);

        if (existing is not null)
        {
            existing.SubmissionId = submissionId;
            existing.SubmittedDate = submittedDate;
            existing.DaysToDeadline = daysToDeadline;
            existing.OnTime = daysToDeadline >= 0;
        }
        else
        {
            _db.FilingSlaRecords.Add(new FilingSlaRecord
            {
                TenantId = period.TenantId,
                ModuleId = period.ModuleId.Value,
                PeriodId = periodId,
                SubmissionId = submissionId,
                PeriodEndDate = periodEndDate,
                DeadlineDate = effectiveDeadline,
                SubmittedDate = submittedDate,
                DaysToDeadline = daysToDeadline,
                OnTime = daysToDeadline >= 0
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    // ── Helpers ──────────────────────────────────────────────────

    internal static RagColor ComputeRagColor(
        DateTime today, DateTime deadline, DateTime periodStart,
        bool hasSubmitted, bool inReview, string status)
    {
        if (hasSubmitted)
            return RagColor.Green;

        if (status == "Overdue" || today > deadline)
            return RagColor.Red;

        var totalDays = (deadline - periodStart).TotalDays;
        var remainingDays = (deadline - today).TotalDays;

        // Draft with < 7 days = Red
        if (remainingDays < 7 && status != "Completed")
            return RagColor.Red;

        // In review = Amber
        if (inReview)
            return RagColor.Amber;

        // < 50% time remaining = Amber
        if (totalDays > 0 && remainingDays / totalDays < 0.5)
            return RagColor.Amber;

        // > 50% time remaining = Green
        return RagColor.Green;
    }

    internal static string FormatPeriod(ReturnPeriod period)
    {
        return period.Frequency switch
        {
            "Quarterly" => $"Q{period.Quarter} {period.Year}",
            "SemiAnnual" => period.Month <= 6 ? $"H1 {period.Year}" : $"H2 {period.Year}",
            "Annual" => $"FY {period.Year}",
            _ => new DateTime(period.Year, period.Month, 1).ToString("MMM yyyy")
        };
    }
}
