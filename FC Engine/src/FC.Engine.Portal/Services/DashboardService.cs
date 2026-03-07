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
    /// Pass customFrom/customTo for a specific date window; otherwise dateRangeDays back from now is used.
    /// </summary>
    public async Task<DashboardData> GetDashboardDataAsync(
        int institutionId, string institutionName, string institutionCode,
        int dateRangeDays = 30, DateTime? customFrom = null, DateTime? customTo = null)
    {
        var cacheKey = customFrom.HasValue
            ? $"dashboard:{institutionId}:custom:{customFrom:yyyyMMdd}:{customTo:yyyyMMdd}"
            : $"dashboard:{institutionId}:{dateRangeDays}";

        if (_cache.TryGetValue(cacheKey, out DashboardData? cached) && cached is not null)
            return cached;

        var data = await BuildDashboardDataAsync(institutionId, institutionName, institutionCode, dateRangeDays, customFrom, customTo);

        _cache.Set(cacheKey, data, CacheDuration);

        return data;
    }

    private async Task<DashboardData> BuildDashboardDataAsync(
        int institutionId, string institutionName, string institutionCode,
        int dateRangeDays = 30, DateTime? customFrom = null, DateTime? customTo = null)
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
                Frequency = template.Frequency.ToString()
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

        dashboard.DateRangeDays = dateRangeDays;

        // Compute effective date range (custom window overrides rolling days)
        var rangeStart = customFrom?.ToUniversalTime() ?? now.AddDays(-dateRangeDays);
        var rangeEnd   = customTo?.ToUniversalTime()   ?? now;

        // ── Hero Metrics ──────────────────────────────────────────
        dashboard.HeroMetrics = BuildHeroMetrics(submissions, allTemplates.Count, now, dateRangeDays, rangeStart, rangeEnd);

        // ── Submission Volume (last 6 months, for area chart) ────
        dashboard.SubmissionVolume = BuildSubmissionVolume(submissions, now);

        // ── Template Health (donut chart) ─────────────────────────
        dashboard.TemplateHealth = BuildTemplateHealth(submissions, allTemplates, rangeStart, rangeEnd);

        // ── Top Modules (horizontal bar chart) ───────────────────
        dashboard.TopModules = BuildTopModules(submissions, allTemplates, rangeStart, rangeEnd);

        // ── Heatmap (last 90 days) ────────────────────────────────
        dashboard.HeatmapData = BuildHeatmap(submissions, now);

        // ── Activity Feed (last 20 actions) ───────────────────────
        dashboard.ActivityFeed = BuildActivityFeed(submissions);

        // ── Drill-down data (submissions grouped by return code) ──
        dashboard.DrilldownByReturnCode = BuildDrilldownData(submissions, allTemplates, rangeStart, rangeEnd);

        return dashboard;
    }

    private static List<HeroMetric> BuildHeroMetrics(
        IReadOnlyList<Submission> submissions, int totalTemplates, DateTime now, int days,
        DateTime rangeStart, DateTime rangeEnd)
    {
        var rangeSpan = rangeEnd - rangeStart;
        var prevStart = rangeStart - rangeSpan;

        var periodSubs = submissions.Where(s => s.SubmittedAt >= rangeStart && s.SubmittedAt <= rangeEnd).ToList();
        var prevSubs   = submissions.Where(s => s.SubmittedAt >= prevStart && s.SubmittedAt < rangeStart).ToList();

        // Sparkline helpers — daily counts for last 7 days
        var sparklineSubs = submissions.Where(s => s.SubmittedAt >= now.AddDays(-6)).ToList();
        List<decimal> DailySparkline(Func<Domain.Entities.Submission, bool> filter)
        {
            return Enumerable.Range(0, 7)
                .Select(i =>
                {
                    var d = now.AddDays(-6 + i).Date;
                    return (decimal)sparklineSubs.Count(s => s.SubmittedAt.Date == d && filter(s));
                })
                .ToList();
        }

        // Compliance rate
        var periodAccepted = periodSubs
            .Where(s => s.Status == SubmissionStatus.Accepted || s.Status == SubmissionStatus.AcceptedWithWarnings)
            .Select(s => s.ReturnCode).Distinct().Count();
        var prevAccepted = prevSubs
            .Where(s => s.Status == SubmissionStatus.Accepted || s.Status == SubmissionStatus.AcceptedWithWarnings)
            .Select(s => s.ReturnCode).Distinct().Count();
        var compliance = totalTemplates > 0 ? Math.Round(periodAccepted * 100m / totalTemplates, 1) : 100m;
        var prevCompliance = totalTemplates > 0 ? Math.Round(prevAccepted * 100m / totalTemplates, 1) : 100m;

        // Submission count sparkline
        var subCountSparkline = DailySparkline(_ => true);
        // Compliance sparkline (daily accepted distinct return codes / total)
        var complianceSparkline = Enumerable.Range(0, 7).Select(i =>
        {
            var d = now.AddDays(-6 + i).Date;
            var acc = sparklineSubs.Where(s => s.SubmittedAt.Date == d &&
                (s.Status == SubmissionStatus.Accepted || s.Status == SubmissionStatus.AcceptedWithWarnings))
                .Select(s => s.ReturnCode).Distinct().Count();
            return totalTemplates > 0 ? Math.Round(acc * 100m / totalTemplates, 1) : 0m;
        }).ToList();

        // Pass rate
        var passRate = periodSubs.Count > 0
            ? Math.Round(periodSubs.Count(s => s.Status == SubmissionStatus.Accepted || s.Status == SubmissionStatus.AcceptedWithWarnings) * 100m / periodSubs.Count, 1)
            : 0m;
        var prevPassRate = prevSubs.Count > 0
            ? Math.Round(prevSubs.Count(s => s.Status == SubmissionStatus.Accepted || s.Status == SubmissionStatus.AcceptedWithWarnings) * 100m / prevSubs.Count, 1)
            : 0m;
        var passRateSparkline = Enumerable.Range(0, 7).Select(i =>
        {
            var d = now.AddDays(-6 + i).Date;
            var daily = sparklineSubs.Where(s => s.SubmittedAt.Date == d).ToList();
            return daily.Count > 0
                ? Math.Round(daily.Count(s => s.Status == SubmissionStatus.Accepted || s.Status == SubmissionStatus.AcceptedWithWarnings) * 100m / daily.Count, 1)
                : 0m;
        }).ToList();

        // Pending review
        var pending = (decimal)submissions.Count(s => s.Status == SubmissionStatus.PendingApproval);
        var prevPending = (decimal)prevSubs.Count(s => s.Status == SubmissionStatus.PendingApproval);
        var pendingSparkline = DailySparkline(s => s.Status == SubmissionStatus.PendingApproval);

        static decimal SafeChange(decimal current, decimal previous) =>
            previous == 0 ? 0 : Math.Round((current - previous) / previous * 100, 1);

        return new List<HeroMetric>
        {
            new()
            {
                Id = "compliance",
                Label = "Compliance Rate",
                Value = compliance,
                PreviousValue = prevCompliance,
                ChangePercent = compliance - prevCompliance,
                IsPositiveTrend = compliance >= prevCompliance,
                Suffix = "%",
                SparklineData = complianceSparkline,
                Color = "#0f766e",
                IconPath = "M22 11.08V12a10 10 0 1 1-5.93-9.14 M22 4 12 14.01 9 11.01"
            },
            new()
            {
                Id = "submissions",
                Label = "Submissions",
                Value = periodSubs.Count,
                PreviousValue = prevSubs.Count,
                ChangePercent = SafeChange(periodSubs.Count, prevSubs.Count),
                IsPositiveTrend = periodSubs.Count >= prevSubs.Count,
                Suffix = "",
                SparklineData = subCountSparkline,
                Color = "#1d4ed8",
                IconPath = "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M17 8 12 3 7 8 M12 3v12"
            },
            new()
            {
                Id = "passrate",
                Label = "Pass Rate",
                Value = passRate,
                PreviousValue = prevPassRate,
                ChangePercent = passRate - prevPassRate,
                IsPositiveTrend = passRate >= prevPassRate,
                Suffix = "%",
                SparklineData = passRateSparkline,
                Color = "#7c3aed",
                IconPath = "M20 6 9 17 4 12"
            },
            new()
            {
                Id = "pending",
                Label = "Pending Review",
                Value = pending,
                PreviousValue = prevPending,
                ChangePercent = SafeChange(pending, prevPending),
                IsPositiveTrend = pending <= prevPending,
                Suffix = "",
                SparklineData = pendingSparkline,
                Color = "#d97706",
                IconPath = "M12 22c5.523 0 10-4.477 10-10S17.523 2 12 2 2 6.477 2 12s4.477 10 10 10z M12 6v6l4 2"
            }
        };
    }

    private static List<SubmissionVolumeItem> BuildSubmissionVolume(
        IReadOnlyList<Submission> submissions, DateTime now)
    {
        var result = new List<SubmissionVolumeItem>();
        var currentMonth = new DateTime(now.Year, now.Month, 1);

        for (int i = 5; i >= 0; i--)
        {
            var monthStart = currentMonth.AddMonths(-i);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var monthSubs = submissions.Where(s => s.SubmittedAt >= monthStart && s.SubmittedAt <= monthEnd).ToList();

            result.Add(new SubmissionVolumeItem
            {
                Label = monthStart.ToString("MMM"),
                Accepted = monthSubs.Count(s => s.Status == SubmissionStatus.Accepted || s.Status == SubmissionStatus.AcceptedWithWarnings),
                Rejected = monthSubs.Count(s => s.Status == SubmissionStatus.Rejected || s.Status == SubmissionStatus.ApprovalRejected),
                Pending = monthSubs.Count(s => s.Status == SubmissionStatus.PendingApproval || s.Status == SubmissionStatus.Draft)
            });
        }

        return result;
    }

    private static List<TemplateHealthItem> BuildTemplateHealth(
        IReadOnlyList<Submission> submissions,
        IReadOnlyList<CachedTemplate> templates,
        DateTime rangeStart, DateTime rangeEnd)
    {
        var periodSubs = submissions.Where(s => s.SubmittedAt >= rangeStart && s.SubmittedAt <= rangeEnd).ToList();

        return templates.Select(t =>
        {
            var ts = periodSubs.Where(s => s.ReturnCode.Equals(t.ReturnCode, StringComparison.OrdinalIgnoreCase)).ToList();
            var accepted = ts.Count(s => s.Status == SubmissionStatus.Accepted || s.Status == SubmissionStatus.AcceptedWithWarnings);
            var rejected = ts.Count(s => s.Status == SubmissionStatus.Rejected || s.Status == SubmissionStatus.ApprovalRejected);
            var total = accepted + rejected;
            return new TemplateHealthItem
            {
                ReturnCode = t.ReturnCode,
                ReturnName = t.Name,
                PassRate = total > 0 ? Math.Round(accepted * 100m / total, 1) : 0m,
                TotalSubmissions = ts.Count,
                ErrorCount = ts.Sum(s => s.ValidationReport?.ErrorCount ?? 0),
                WarningCount = ts.Sum(s => s.ValidationReport?.WarningCount ?? 0)
            };
        })
        .OrderByDescending(t => t.TotalSubmissions)
        .Take(8)
        .ToList();
    }

    private static List<ModuleRankItem> BuildTopModules(
        IReadOnlyList<Submission> submissions,
        IReadOnlyList<CachedTemplate> templates,
        DateTime rangeStart, DateTime rangeEnd)
    {
        var periodSubs = submissions.Where(s => s.SubmittedAt >= rangeStart && s.SubmittedAt <= rangeEnd).ToList();
        var lookup = templates.ToDictionary(t => t.ReturnCode, t => t.Name, StringComparer.OrdinalIgnoreCase);

        return periodSubs
            .GroupBy(s => s.ReturnCode, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var accepted = g.Count(s => s.Status == SubmissionStatus.Accepted || s.Status == SubmissionStatus.AcceptedWithWarnings);
                return new ModuleRankItem
                {
                    ReturnCode = g.Key,
                    ReturnName = lookup.GetValueOrDefault(g.Key, g.Key),
                    SubmissionCount = g.Count(),
                    ComplianceRate = g.Count() > 0 ? Math.Round(accepted * 100m / g.Count(), 1) : 0m
                };
            })
            .OrderByDescending(m => m.SubmissionCount)
            .Take(5)
            .ToList();
    }

    private static List<HeatmapDay> BuildHeatmap(
        IReadOnlyList<Submission> submissions, DateTime now)
    {
        var start = now.AddDays(-89).Date;
        var lookup = submissions
            .Where(s => s.SubmittedAt.Date >= start)
            .GroupBy(s => s.SubmittedAt.Date)
            .ToDictionary(g => g.Key, g => g.Count());

        var maxCount = lookup.Values.DefaultIfEmpty(0).Max();

        return Enumerable.Range(0, 90).Select(i =>
        {
            var date = start.AddDays(i);
            var count = lookup.GetValueOrDefault(date, 0);
            var intensity = count == 0 ? 0
                : maxCount <= 2 ? 1
                : count <= maxCount / 4 ? 1
                : count <= maxCount / 2 ? 2
                : count <= maxCount * 3 / 4 ? 3
                : 4;
            return new HeatmapDay { Date = date, Count = count, Intensity = intensity };
        }).ToList();
    }

    private static List<ActivityFeedItem> BuildActivityFeed(
        IReadOnlyList<Submission> submissions)
    {
        return submissions
            .OrderByDescending(s => s.SubmittedAt)
            .Take(20)
            .Select(s =>
            {
                var (icon, message, badge) = s.Status switch
                {
                    SubmissionStatus.Accepted => ("approve", $"{s.ReturnCode} accepted", "portal-badge-success"),
                    SubmissionStatus.AcceptedWithWarnings => ("approve", $"{s.ReturnCode} accepted with warnings", "portal-badge-warning"),
                    SubmissionStatus.Rejected => ("reject", $"{s.ReturnCode} rejected", "portal-badge-danger"),
                    SubmissionStatus.ApprovalRejected => ("reject", $"{s.ReturnCode} returned by checker", "portal-badge-danger"),
                    SubmissionStatus.PendingApproval => ("validate", $"{s.ReturnCode} awaiting checker approval", "portal-badge-info"),
                    SubmissionStatus.Draft => ("draft", $"{s.ReturnCode} saved as draft", "portal-badge-neutral"),
                    SubmissionStatus.Validating => ("validate", $"{s.ReturnCode} validation in progress", "portal-badge-info"),
                    _ => ("submit", $"{s.ReturnCode} submitted", "portal-badge-neutral")
                };
                return new ActivityFeedItem
                {
                    Icon = icon,
                    Message = message,
                    Actor = "System",
                    Timestamp = s.SubmittedAt,
                    LinkUrl = $"/submissions/{s.Id}",
                    BadgeClass = badge
                };
            })
            .ToList();
    }

    private static Dictionary<string, List<DrilldownSubmissionItem>> BuildDrilldownData(
        IReadOnlyList<Submission> submissions,
        IReadOnlyList<CachedTemplate> templates,
        DateTime rangeStart, DateTime rangeEnd)
    {
        var periodSubs = submissions
            .Where(s => s.SubmittedAt >= rangeStart && s.SubmittedAt <= rangeEnd)
            .ToList();

        // Collect all relevant return codes (from both templates and period submissions)
        var codes = templates.Select(t => t.ReturnCode)
            .Union(periodSubs.Select(s => s.ReturnCode), StringComparer.OrdinalIgnoreCase)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return codes.ToDictionary(
            code => code,
            code => periodSubs
                .Where(s => s.ReturnCode.Equals(code, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.SubmittedAt)
                .Take(50)
                .Select(s => new DrilldownSubmissionItem
                {
                    SubmissionId = s.Id,
                    Period = FormatPeriod(s),
                    SubmittedDate = s.SubmittedAt,
                    Status = s.Status,
                    ErrorCount = s.ValidationReport?.ErrorCount ?? 0,
                    WarningCount = s.ValidationReport?.WarningCount ?? 0
                })
                .ToList(),
            StringComparer.OrdinalIgnoreCase);
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

    // ── Compliance Performance Dashboard ────────────────────────

    /// <summary>
    /// Builds data for the compliance performance dashboard. Cached 5 min.
    /// </summary>
    public async Task<ComplianceDashboardData> GetComplianceDashboardAsync(
        int institutionId, string institutionName, string institutionCode,
        string? returnCodeFilter = null, string? frequencyFilter = null)
    {
        var cacheKey = $"compliance-dash:{institutionId}:{returnCodeFilter}:{frequencyFilter}";
        if (_cache.TryGetValue(cacheKey, out ComplianceDashboardData? cached) && cached is not null)
            return cached;

        var data = await BuildComplianceDashboardAsync(
            institutionId, institutionName, institutionCode, returnCodeFilter, frequencyFilter);

        _cache.Set(cacheKey, data, CacheDuration);
        return data;
    }

    private async Task<ComplianceDashboardData> BuildComplianceDashboardAsync(
        int institutionId, string institutionName, string institutionCode,
        string? returnCodeFilter, string? frequencyFilter)
    {
        var allTemplates = await _templateCache.GetAllPublishedTemplates();
        var submissions  = await _submissionRepo.GetByInstitution(institutionId);
        var now          = DateTime.UtcNow;
        var currentMonth = new DateTime(now.Year, now.Month, 1);

        var finalStatuses = new HashSet<SubmissionStatus>
        {
            SubmissionStatus.Accepted,
            SubmissionStatus.AcceptedWithWarnings
        };

        // Apply module/frequency filters
        var templates = allTemplates
            .Where(t => string.IsNullOrEmpty(returnCodeFilter)
                        || t.ReturnCode.Equals(returnCodeFilter, StringComparison.OrdinalIgnoreCase))
            .Where(t => string.IsNullOrEmpty(frequencyFilter)
                        || t.Frequency.ToString().Equals(frequencyFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var templateCodes = templates.Select(t => t.ReturnCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filteredSubs = submissions
            .Where(s => templateCodes.Contains(s.ReturnCode))
            .ToList();

        // ── Hero metrics (current calendar month) ─────────────────
        var periodSubs = filteredSubs
            .Where(s => s.SubmittedAt >= currentMonth)
            .ToList();

        var acceptedCodes = periodSubs
            .Where(s => finalStatuses.Contains(s.Status))
            .Select(s => s.ReturnCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var complianceRate = templates.Count > 0
            ? Math.Round(acceptedCodes.Count * 100m / templates.Count, 1)
            : 100m;

        var returnsFiled = acceptedCodes.Count;
        var outstanding  = Math.Max(0, templates.Count - acceptedCodes.Count);
        var overdue = filteredSubs
            .Where(s => s.ReturnPeriod is not null
                        && s.ReturnPeriod.EffectiveDeadline < now
                        && !finalStatuses.Contains(s.Status))
            .Select(s => s.ReturnPeriodId)
            .Distinct()
            .Count();

        // ── Quarter-over-quarter comparison ──────────────────────
        var currentQStart = new DateTime(now.Year, ((now.Month - 1) / 3) * 3 + 1, 1);
        var prevQStart    = currentQStart.AddMonths(-3);
        var prevQEnd      = currentQStart;

        decimal CalcQuarterRate(DateTime start, DateTime end)
        {
            var months   = Math.Max(1, (int)Math.Round((end - start).TotalDays / 30.4));
            var accepted = filteredSubs
                .Where(s => s.SubmittedAt >= start && s.SubmittedAt < end && finalStatuses.Contains(s.Status))
                .Select(s => s.ReturnCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var due = templates.Count * months;
            return due > 0 ? Math.Round(accepted * 100m / due, 1) : 100m;
        }

        var currentQRate = CalcQuarterRate(currentQStart, now.AddSeconds(1));
        var prevQRate    = CalcQuarterRate(prevQStart, prevQEnd);
        var quarterChange = Math.Round(currentQRate - prevQRate, 1);

        // ── 12-month grouped bar chart ────────────────────────────
        var monthlyBars = new List<ComplianceBarMonth>();
        for (int i = 11; i >= 0; i--)
        {
            var mStart = currentMonth.AddMonths(-i);
            var mEnd   = mStart.AddMonths(1);
            int onTime = 0, late = 0, missed = 0;

            foreach (var template in templates)
            {
                var acc = filteredSubs.FirstOrDefault(s =>
                    s.ReturnCode.Equals(template.ReturnCode, StringComparison.OrdinalIgnoreCase)
                    && s.SubmittedAt >= mStart && s.SubmittedAt < mEnd
                    && finalStatuses.Contains(s.Status));

                if (acc is not null)
                {
                    var deadline = acc.ReturnPeriod?.EffectiveDeadline;
                    if (deadline.HasValue && acc.SubmittedAt <= deadline.Value)
                        onTime++;
                    else
                        late++;
                }
                else if (mEnd <= now)
                {
                    missed++;
                }
            }

            monthlyBars.Add(new ComplianceBarMonth
            {
                MonthLabel = mStart.ToString("MMM yy"),
                Year  = mStart.Year,
                Month = mStart.Month,
                OnTime  = onTime,
                Late    = late,
                Missed  = missed
            });
        }

        // ── Per-module breakdown ───────────────────────────────────
        var moduleBreakdowns = templates.Select(template =>
        {
            var tSubs = filteredSubs
                .Where(s => s.ReturnCode.Equals(template.ReturnCode, StringComparison.OrdinalIgnoreCase))
                .ToList();

            int due = 0, submitted = 0;
            var sparkline = new List<decimal>();

            for (int i = 11; i >= 0; i--)
            {
                var mStart = currentMonth.AddMonths(-i);
                var mEnd   = mStart.AddMonths(1);
                if (mEnd > now) continue;   // skip future/current partial month

                due++;
                var hasAccepted = tSubs.Any(s =>
                    finalStatuses.Contains(s.Status)
                    && s.SubmittedAt >= mStart && s.SubmittedAt < mEnd);
                if (hasAccepted) submitted++;
                if (i <= 5) sparkline.Add(hasAccepted ? 100m : 0m);
            }

            return new ComplianceModuleBreakdown
            {
                ReturnCode     = template.ReturnCode,
                ReturnName     = template.Name,
                Frequency      = template.Frequency.ToString(),
                ReturnsDue     = due,
                Submitted      = submitted,
                ComplianceRate = due > 0 ? Math.Round(submitted * 100m / due, 1) : 0m,
                TrendSparkline = sparkline
            };
        })
        .OrderByDescending(m => m.ComplianceRate)
        .ThenBy(m => m.ReturnCode)
        .ToList();

        return new ComplianceDashboardData
        {
            InstitutionName = institutionName,
            InstitutionCode = institutionCode,
            GeneratedAt     = now,
            ComplianceRate  = complianceRate,
            ReturnsFiled    = returnsFiled,
            Outstanding     = outstanding,
            Overdue         = overdue,
            QuarterChange   = quarterChange,
            IsTrendImproving = quarterChange >= 0,
            MonthlyBars       = monthlyBars,
            ModuleBreakdowns  = moduleBreakdowns,
            AvailableReturnCodes  = allTemplates.Select(t => t.ReturnCode).Distinct().OrderBy(x => x).ToList(),
            AvailableFrequencies  = allTemplates.Select(t => t.Frequency.ToString()).Distinct().OrderBy(x => x).ToList()
        };
    }

    // ── Nav Badge Counts ─────────────────────────────────────────

    /// <summary>
    /// Lightweight method returning sidebar badge counts. Cached for 5 minutes.
    /// </summary>
    public async Task<NavBadgeCounts> GetNavBadgeCountsAsync(int institutionId)
    {
        var cacheKey = $"nav-badges:{institutionId}";
        if (_cache.TryGetValue(cacheKey, out NavBadgeCounts? cached) && cached is not null)
            return cached;

        var submissions = await _submissionRepo.GetByInstitution(institutionId);
        var now = DateTime.UtcNow;
        var in7Days = now.AddDays(7);

        var finalStatuses = new HashSet<SubmissionStatus>
        {
            SubmissionStatus.Accepted,
            SubmissionStatus.AcceptedWithWarnings,
            SubmissionStatus.Historical
        };

        // Overdue: periods past their effective deadline with no final accepted submission
        var acceptedPeriods = submissions
            .Where(s => finalStatuses.Contains(s.Status))
            .Select(s => s.ReturnPeriodId)
            .ToHashSet();

        var overdueCount = submissions
            .Where(s => s.ReturnPeriod is not null
                        && s.ReturnPeriod.EffectiveDeadline < now
                        && !acceptedPeriods.Contains(s.ReturnPeriodId)
                        && !finalStatuses.Contains(s.Status))
            .Select(s => s.ReturnPeriodId)
            .Distinct()
            .Count();

        // Pending approvals awaiting checker action
        var pendingApprovalCount = submissions
            .Count(s => s.Status == SubmissionStatus.PendingApproval);

        // Calendar: open return periods with deadline within the next 7 days, not yet accepted
        var calendarDue7Days = submissions
            .Where(s => s.ReturnPeriod is not null
                        && s.ReturnPeriod.EffectiveDeadline >= now
                        && s.ReturnPeriod.EffectiveDeadline <= in7Days
                        && !finalStatuses.Contains(s.Status))
            .Select(s => s.ReturnPeriodId)
            .Distinct()
            .Count();

        // 5 most recently submitted return codes (for Quick Submit dropdown)
        var recentReturnCodes = submissions
            .Where(s => s.Status != SubmissionStatus.Draft)
            .OrderByDescending(s => s.SubmittedAt)
            .Select(s => s.ReturnCode)
            .Distinct()
            .Take(5)
            .ToList();

        var counts = new NavBadgeCounts(overdueCount, pendingApprovalCount, calendarDue7Days, recentReturnCodes);
        _cache.Set(cacheKey, counts, CacheDuration);
        return counts;
    }
}

/// <summary>Sidebar badge counts returned by a single lightweight query.</summary>
public record NavBadgeCounts(
    int OverdueCount,
    int PendingApprovalCount,
    int CalendarDue7Days,
    IReadOnlyList<string> RecentReturnCodes
)
{
    public static readonly NavBadgeCounts Empty =
        new(0, 0, 0, Array.Empty<string>());
}
