using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Portal.Services;

/// <summary>
/// Pure static helpers for computing filing due dates, period keys, and
/// submission status relative to a deadline.  Shared by both
/// <see cref="CalendarService"/> and <see cref="DashboardService"/>
/// so neither needs a direct reference to the other.
/// </summary>
internal static class FilingCalendarHelpers
{
    /// <summary>
    /// Generate the list of due dates for a given frequency within a date range.
    /// Each due date falls on the last day of the applicable period.
    /// </summary>
    internal static List<DateTime> GetDueDatesForRange(
        ReturnFrequency frequency,
        DateTime rangeStart,
        DateTime rangeEnd)
    {
        var dates = new List<DateTime>();

        if (frequency == ReturnFrequency.Quarterly)
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
        else
        {
            // Monthly (also default for Annual/AdHoc/Computed)
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

    /// <summary>
    /// Determine the calendar entry status for a submission relative to a due date.
    /// </summary>
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
}
