using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Portal.Services;

/// <summary>
/// Aggregates template metadata, return periods, and submission data
/// to produce a calendar of reporting obligations for an institution.
/// </summary>
public class CalendarService
{
    private readonly ITemplateMetadataCache _templateCache;
    private readonly ISubmissionRepository _submissionRepo;
    private readonly IEntitlementService _entitlementService;
    private readonly ITenantContext _tenantContext;

    public CalendarService(
        ITemplateMetadataCache templateCache,
        ISubmissionRepository submissionRepo,
        IEntitlementService entitlementService,
        ITenantContext tenantContext)
    {
        _templateCache = templateCache;
        _submissionRepo = submissionRepo;
        _entitlementService = entitlementService;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Get all calendar entries for the given institution within a date range.
    /// Each entry represents one (template + period) obligation.
    /// </summary>
    public async Task<CalendarData> GetCalendarData(
        int institutionId,
        DateTime rangeStart,
        DateTime rangeEnd,
        string? frequencyFilter = null,
        CancellationToken ct = default)
    {
        // Step 1: Get entitled published templates for the current tenant
        var allTemplates = await GetScopedTemplatesAsync(ct);

        // Apply frequency filter if provided
        IEnumerable<CachedTemplate> filteredTemplates = allTemplates;
        if (!string.IsNullOrEmpty(frequencyFilter) && frequencyFilter != "All")
        {
            filteredTemplates = allTemplates
                .Where(t => t.Frequency.ToString().Equals(frequencyFilter, StringComparison.OrdinalIgnoreCase));
        }

        // Step 2: Get existing submissions for this institution
        var submissions = await _submissionRepo.GetByInstitution(institutionId, ct);

        // Build lookup: (returnCode, periodKey) → most recent submission
        var submissionLookup = submissions
            .GroupBy(s => $"{s.ReturnCode}|{GetPeriodKey(s)}")
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.CreatedAt).First(),
                StringComparer.OrdinalIgnoreCase
            );

        // Step 3: Generate entries for each template × period
        var entries = new List<CalendarEntry>();

        foreach (var template in filteredTemplates)
        {
            var dueDates = GetDueDatesForRange(template.Frequency, rangeStart, rangeEnd);

            foreach (var dueDate in dueDates)
            {
                var periodKey = $"{template.ReturnCode}|{dueDate:yyyy-MM}";
                submissionLookup.TryGetValue(periodKey, out var existingSub);

                var status = DetermineStatus(existingSub, dueDate);

                entries.Add(new CalendarEntry
                {
                    ReturnCode = template.ReturnCode,
                    TemplateName = template.Name,
                    ModuleCode = template.ModuleCode,
                    ModuleName = PortalSubmissionLinkBuilder.ResolveModuleName(template.ModuleCode),
                    Frequency = template.Frequency.ToString(),
                    DueDate = dueDate,
                    PeriodLabel = FormatPeriodLabel(dueDate, template.Frequency),
                    PeriodValue = dueDate.ToString("yyyy-MM"),
                    Status = status,
                    SubmissionId = existingSub?.Id,
                    DaysUntilDue = (dueDate.Date - DateTime.UtcNow.Date).Days,
                    StartHref = PortalSubmissionLinkBuilder.BuildSubmitHref(template.ReturnCode, template.ModuleCode),
                    WorkspaceHref = PortalSubmissionLinkBuilder.ResolveWorkspaceHref(template.ModuleCode),
                    DeadlineDescription = FormatDeadlineDescription(dueDate, template.Frequency)
                });
            }
        }

        // Sort entries by due date
        entries = entries.OrderBy(e => e.DueDate).ToList();

        // Compute summary statistics for the current month
        var now = DateTime.UtcNow;
        var currentMonthEntries = entries
            .Where(e => e.DueDate.Year == now.Year && e.DueDate.Month == now.Month)
            .ToList();

        var summary = new CalendarSummary
        {
            TotalDueThisMonth = currentMonthEntries.Count,
            SubmittedThisMonth = currentMonthEntries.Count(e => e.Status == CalendarEntryStatus.Submitted),
            OutstandingThisMonth = currentMonthEntries.Count(e =>
                e.Status == CalendarEntryStatus.NotStarted || e.Status == CalendarEntryStatus.Rejected),
            OverdueThisMonth = currentMonthEntries.Count(e => e.Status == CalendarEntryStatus.Overdue),
            CompliancePercentage = currentMonthEntries.Count > 0
                ? (int)Math.Round(100.0 * currentMonthEntries.Count(e => e.Status == CalendarEntryStatus.Submitted) / currentMonthEntries.Count)
                : 100
        };

        return new CalendarData
        {
            Entries = entries,
            Summary = summary
        };
    }

    private async Task<IReadOnlyList<CachedTemplate>> GetScopedTemplatesAsync(CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not { } tenantId)
        {
            return await _templateCache.GetAllPublishedTemplates(ct);
        }

        var entitlement = await _entitlementService.ResolveEntitlements(tenantId, ct);
        var activeModuleIds = entitlement.ActiveModules
            .Select(x => x.ModuleId)
            .Distinct()
            .ToHashSet();
        var activeModuleCodes = entitlement.ActiveModules
            .Select(x => x.ModuleCode)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (activeModuleIds.Count == 0 && activeModuleCodes.Count == 0)
        {
            return Array.Empty<CachedTemplate>();
        }

        var templates = await _templateCache.GetAllPublishedTemplates(tenantId, ct);
        return templates
            .Where(template =>
                (template.ModuleId.HasValue && activeModuleIds.Contains(template.ModuleId.Value))
                || (!string.IsNullOrWhiteSpace(template.ModuleCode) && activeModuleCodes.Contains(template.ModuleCode)))
            .ToList();
    }

    // ── Due Date Computation ─────────────────────────────────────

    internal static List<DateTime> GetDueDatesForRange(
        ReturnFrequency frequency,
        DateTime rangeStart,
        DateTime rangeEnd)
    {
        var dates = new List<DateTime>();

        if (frequency == ReturnFrequency.Monthly)
        {
            var current = new DateTime(rangeStart.Year, rangeStart.Month, 1);
            while (current <= rangeEnd)
            {
                var dueDate = new DateTime(current.Year, current.Month, DateTime.DaysInMonth(current.Year, current.Month));
                if (dueDate >= rangeStart && dueDate <= rangeEnd)
                    dates.Add(dueDate);
                current = current.AddMonths(1);
            }
        }
        else if (frequency == ReturnFrequency.Quarterly)
        {
            var quarterEndMonths = new[] { 3, 6, 9, 12 };
            var current = new DateTime(rangeStart.Year, 1, 1);
            while (current.Year <= rangeEnd.Year)
            {
                foreach (var month in quarterEndMonths)
                {
                    var dueDate = new DateTime(current.Year, month, DateTime.DaysInMonth(current.Year, month));
                    if (dueDate >= rangeStart && dueDate <= rangeEnd)
                        dates.Add(dueDate);
                }
                current = current.AddYears(1);
            }
        }
        else if (frequency == ReturnFrequency.SemiAnnual)
        {
            var semiEndMonths = new[] { 6, 12 };
            var current = new DateTime(rangeStart.Year, 1, 1);
            while (current.Year <= rangeEnd.Year)
            {
                foreach (var month in semiEndMonths)
                {
                    var dueDate = new DateTime(current.Year, month, DateTime.DaysInMonth(current.Year, month));
                    if (dueDate >= rangeStart && dueDate <= rangeEnd)
                        dates.Add(dueDate);
                }
                current = current.AddYears(1);
            }
        }
        else // Computed or other
        {
            // Default to monthly
            var current = new DateTime(rangeStart.Year, rangeStart.Month, 1);
            while (current <= rangeEnd)
            {
                var dueDate = new DateTime(current.Year, current.Month, DateTime.DaysInMonth(current.Year, current.Month));
                if (dueDate >= rangeStart && dueDate <= rangeEnd)
                    dates.Add(dueDate);
                current = current.AddMonths(1);
            }
        }

        return dates;
    }

    // ── Status Determination ─────────────────────────────────────

    internal static CalendarEntryStatus DetermineStatus(Submission? submission, DateTime dueDate)
    {
        if (submission is null)
        {
            return dueDate.Date < DateTime.UtcNow.Date
                ? CalendarEntryStatus.Overdue
                : CalendarEntryStatus.NotStarted;
        }

        if (submission.Status == SubmissionStatus.Accepted
            || submission.Status == SubmissionStatus.AcceptedWithWarnings)
        {
            return CalendarEntryStatus.Submitted;
        }

        if (submission.Status == SubmissionStatus.Rejected
            || submission.Status == SubmissionStatus.ApprovalRejected)
        {
            return CalendarEntryStatus.Rejected;
        }

        // Draft, Parsing, Validating, PendingApproval
        return CalendarEntryStatus.Draft;
    }

    // ── Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Extract a period key string from a submission for lookup matching.
    /// Uses ReturnPeriod navigation (Year, Month) to produce "yyyy-MM".
    /// </summary>
    internal static string GetPeriodKey(Submission s)
    {
        if (s.ReturnPeriod is not null)
            return $"{s.ReturnPeriod.Year}-{s.ReturnPeriod.Month:D2}";

        // Fallback: use submission date
        return (s.SubmittedAt ?? s.CreatedAt).ToString("yyyy-MM");
    }

    private static string FormatPeriodLabel(DateTime dueDate, ReturnFrequency frequency)
    {
        if (frequency == ReturnFrequency.Quarterly)
            return $"Q{((dueDate.Month - 1) / 3) + 1} {dueDate.Year}";

        if (frequency == ReturnFrequency.SemiAnnual)
            return dueDate.Month <= 6 ? $"H1 {dueDate.Year}" : $"H2 {dueDate.Year}";

        // Monthly or Computed
        return dueDate.ToString("MMMM yyyy");
    }

    /// <summary>
    /// Produces a human-readable deadline description for display in the calendar hover card,
    /// e.g. "Month-end", "Quarter-end", "5th BD".
    /// </summary>
    private static string FormatDeadlineDescription(DateTime dueDate, ReturnFrequency frequency)
        => frequency switch
        {
            ReturnFrequency.Quarterly  => "Quarter-end",
            ReturnFrequency.SemiAnnual => "Semi-annual end",
            _                          => BusinessDayHelper.DescribeDeadline(dueDate)
        };

}
