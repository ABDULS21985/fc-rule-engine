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

    public CalendarService(
        ITemplateMetadataCache templateCache,
        ISubmissionRepository submissionRepo)
    {
        _templateCache = templateCache;
        _submissionRepo = submissionRepo;
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
        // Step 1: Get all published templates
        var allTemplates = await _templateCache.GetAllPublishedTemplates(ct);

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
                    Frequency = template.Frequency.ToString(),
                    DueDate = dueDate,
                    PeriodLabel = FormatPeriodLabel(dueDate, template.Frequency),
                    PeriodValue = dueDate.ToString("yyyy-MM"),
                    Status = status,
                    SubmissionId = existingSub?.Id,
                    DaysUntilDue = (dueDate.Date - DateTime.UtcNow.Date).Days
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

    // ── Due Date Computation ─────────────────────────────────────

    private static List<DateTime> GetDueDatesForRange(
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

    private static CalendarEntryStatus DetermineStatus(Submission? submission, DateTime dueDate)
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
    private static string GetPeriodKey(Submission s)
    {
        if (s.ReturnPeriod is not null)
            return $"{s.ReturnPeriod.Year}-{s.ReturnPeriod.Month:D2}";

        // Fallback: use submission date
        return s.SubmittedAt.ToString("yyyy-MM");
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
}
