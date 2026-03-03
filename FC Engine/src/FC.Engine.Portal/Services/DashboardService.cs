namespace FC.Engine.Portal.Services;

using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

/// <summary>
/// Aggregates dashboard data for a financial institution.
/// Caches results for 5 minutes to reduce database load.
/// </summary>
public class DashboardService
{
    private readonly ISubmissionRepository _submissionRepo;
    private readonly ITemplateMetadataCache _templateCache;
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public DashboardService(
        ISubmissionRepository submissionRepo,
        ITemplateMetadataCache templateCache,
        IMemoryCache cache)
    {
        _submissionRepo = submissionRepo;
        _templateCache = templateCache;
        _cache = cache;
    }

    /// <summary>
    /// Gets the full dashboard data for an institution, with 5-minute caching.
    /// </summary>
    public async Task<DashboardData> GetDashboardDataAsync(int institutionId, string institutionName, string institutionCode)
    {
        var cacheKey = $"dashboard:{institutionId}";

        if (_cache.TryGetValue(cacheKey, out DashboardData? cached) && cached is not null)
            return cached;

        var data = await BuildDashboardDataAsync(institutionId, institutionName, institutionCode);

        _cache.Set(cacheKey, data, CacheDuration);

        return data;
    }

    private async Task<DashboardData> BuildDashboardDataAsync(int institutionId, string institutionName, string institutionCode)
    {
        var dashboard = new DashboardData
        {
            InstitutionName = institutionName,
            InstitutionCode = institutionCode
        };

        // Get all published templates
        var allTemplates = await _templateCache.GetAllPublishedTemplates();

        // Get all submissions for this institution (includes ReturnPeriod and ValidationReport)
        var submissions = await _submissionRepo.GetByInstitution(institutionId);

        var now = DateTime.UtcNow;
        var currentMonth = new DateTime(now.Year, now.Month, 1);
        var currentMonthEnd = currentMonth.AddMonths(1).AddDays(-1);

        // ── Stat Cards ───────────────────────────────────────────

        // Due this month: count of all monthly templates
        // (quarterly/semi-annual templates are due in their respective months)
        dashboard.DueThisMonth = allTemplates.Count;

        // Submissions this period
        var thisMonthSubmissions = submissions
            .Where(s => s.SubmittedAt >= currentMonth && s.SubmittedAt <= currentMonthEnd)
            .ToList();

        dashboard.SubmittedThisPeriod = thisMonthSubmissions.Count;
        dashboard.AcceptedCount = thisMonthSubmissions.Count(s =>
            s.Status == SubmissionStatus.Accepted || s.Status == SubmissionStatus.AcceptedWithWarnings);
        dashboard.RejectedCount = thisMonthSubmissions.Count(s => s.Status == SubmissionStatus.Rejected);

        // Pending review (maker-checker)
        dashboard.PendingReviewCount = submissions.Count(s => s.Status == SubmissionStatus.PendingApproval);

        // Overdue: templates due this month not yet submitted
        var submittedReturnCodes = thisMonthSubmissions
            .Where(s => s.Status != SubmissionStatus.Rejected && s.Status != SubmissionStatus.ApprovalRejected)
            .Select(s => s.ReturnCode)
            .Distinct()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        dashboard.OverdueCount = allTemplates.Count(t => !submittedReturnCodes.Contains(t.ReturnCode))
            - Math.Max(0, allTemplates.Count - submittedReturnCodes.Count);
        dashboard.OverdueCount = Math.Max(0, allTemplates.Count - submittedReturnCodes.Count);

        // Average validation score: percentage of accepted submissions
        dashboard.AverageValidationScore = thisMonthSubmissions.Count > 0
            ? Math.Round(
                thisMonthSubmissions.Count(s => s.Status == SubmissionStatus.Accepted || s.Status == SubmissionStatus.AcceptedWithWarnings)
                * 100m / thisMonthSubmissions.Count, 1)
            : 0;

        // ── Compliance Score ─────────────────────────────────────
        dashboard.TotalReturnsDue = allTemplates.Count;
        dashboard.TotalReturnsSubmitted = submittedReturnCodes.Count;
        dashboard.CompliancePercentage = dashboard.TotalReturnsDue > 0
            ? Math.Round(dashboard.TotalReturnsSubmitted * 100m / dashboard.TotalReturnsDue, 1)
            : 100;

        // Previous period comparison (last month)
        var lastMonth = currentMonth.AddMonths(-1);
        var lastMonthEnd = currentMonth.AddDays(-1);
        var lastMonthAccepted = submissions
            .Where(s => s.SubmittedAt >= lastMonth && s.SubmittedAt <= lastMonthEnd)
            .Select(s => s.ReturnCode)
            .Distinct()
            .Count();

        dashboard.PreviousPeriodCompliancePercentage = dashboard.TotalReturnsDue > 0
            ? Math.Round(lastMonthAccepted * 100m / dashboard.TotalReturnsDue, 1)
            : 100;

        // ── Upcoming Deadlines ──────────────────────────────────
        var deadlines = new List<DeadlineItem>();
        foreach (var template in allTemplates)
        {
            var dueDate = currentMonthEnd; // Default: end of current month
            var daysRemaining = (dueDate - now).Days;

            var existingSubmission = thisMonthSubmissions
                .FirstOrDefault(s => s.ReturnCode.Equals(template.ReturnCode, StringComparison.OrdinalIgnoreCase)
                    && s.Status != SubmissionStatus.Rejected
                    && s.Status != SubmissionStatus.ApprovalRejected);

            var draftSubmission = thisMonthSubmissions
                .FirstOrDefault(s => s.ReturnCode.Equals(template.ReturnCode, StringComparison.OrdinalIgnoreCase)
                    && s.Status == SubmissionStatus.Draft);

            DeadlineStatus status;
            if (existingSubmission is not null && existingSubmission.Status != SubmissionStatus.Draft)
                status = DeadlineStatus.Submitted;
            else if (draftSubmission is not null)
                status = DeadlineStatus.Draft;
            else if (daysRemaining < 0)
                status = DeadlineStatus.Overdue;
            else
                status = DeadlineStatus.NotStarted;

            deadlines.Add(new DeadlineItem
            {
                ReturnCode = template.ReturnCode,
                ReturnName = template.Name,
                DueDate = dueDate,
                DaysRemaining = daysRemaining,
                Status = status,
                SubmissionId = existingSubmission?.Id ?? draftSubmission?.Id,
                Frequency = template.StructuralCategory
            });
        }

        dashboard.UpcomingDeadlines = deadlines
            .OrderBy(d => d.Status == DeadlineStatus.Submitted ? 1 : 0) // unsubmitted first
            .ThenBy(d => d.DueDate)
            .Take(5)
            .ToList();

        // ── Recent Submissions (last 10) ─────────────────────────
        // Build template name lookup
        var templateNameLookup = allTemplates.ToDictionary(
            t => t.ReturnCode,
            t => t.Name,
            StringComparer.OrdinalIgnoreCase);

        dashboard.RecentSubmissions = submissions
            .OrderByDescending(s => s.SubmittedAt)
            .Take(10)
            .Select(s => new RecentSubmissionItem
            {
                SubmissionId = s.Id,
                ReturnCode = s.ReturnCode,
                ReturnName = templateNameLookup.GetValueOrDefault(s.ReturnCode, ""),
                Period = FormatPeriod(s),
                SubmittedDate = s.SubmittedAt,
                Status = s.Status,
                ErrorCount = s.ValidationReport?.ErrorCount ?? 0,
                WarningCount = s.ValidationReport?.WarningCount ?? 0
            })
            .ToList();

        // ── Compliance Trend (last 6 months) ─────────────────────
        var totalTemplates = allTemplates.Count;
        for (int i = 5; i >= 0; i--)
        {
            var monthStart = currentMonth.AddMonths(-i);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            var monthAccepted = submissions
                .Where(s => s.SubmittedAt >= monthStart && s.SubmittedAt <= monthEnd)
                .Where(s => s.Status == SubmissionStatus.Accepted || s.Status == SubmissionStatus.AcceptedWithWarnings)
                .Select(s => s.ReturnCode)
                .Distinct()
                .Count();

            dashboard.ComplianceTrend.Add(new ComplianceTrendItem
            {
                MonthLabel = monthStart.ToString("MMM"),
                Year = monthStart.Year,
                Month = monthStart.Month,
                Submitted = monthAccepted,
                Total = totalTemplates,
                CompliancePercent = totalTemplates > 0 ? Math.Round(monthAccepted * 100m / totalTemplates, 1) : 0
            });
        }

        return dashboard;
    }

    private static string FormatPeriod(Submission submission)
    {
        if (submission.ReturnPeriod is not null)
        {
            var rp = submission.ReturnPeriod;
            return new DateTime(rp.Year, rp.Month, 1).ToString("MMM yyyy");
        }
        return submission.SubmittedAt.ToString("MMM yyyy");
    }
}
