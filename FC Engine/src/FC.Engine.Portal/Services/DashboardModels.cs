namespace FC.Engine.Portal.Services;

using FC.Engine.Domain.Enums;

/// <summary>
/// Aggregated dashboard data for a single institution.
/// </summary>
public class DashboardData
{
    // Institution info
    public string InstitutionName { get; set; } = "";
    public string InstitutionCode { get; set; } = "";

    // Compliance score
    public int TotalReturnsDue { get; set; }
    public int TotalReturnsSubmitted { get; set; }
    public decimal CompliancePercentage { get; set; }
    public decimal PreviousPeriodCompliancePercentage { get; set; }

    // Stat cards
    public int DueThisMonth { get; set; }
    public int OverdueCount { get; set; }
    public int SubmittedThisPeriod { get; set; }
    public int AcceptedCount { get; set; }
    public int RejectedCount { get; set; }
    public int PendingReviewCount { get; set; }
    public decimal AverageValidationScore { get; set; }

    // Upcoming deadlines
    public List<DeadlineItem> UpcomingDeadlines { get; set; } = new();

    // Recent submissions
    public List<RecentSubmissionItem> RecentSubmissions { get; set; } = new();

    // Compliance trend (last 6 months)
    public List<ComplianceTrendItem> ComplianceTrend { get; set; } = new();

    // ── Enhanced Dashboard Collections ──────────────────────────
    public List<HeroMetric> HeroMetrics { get; set; } = new();
    public List<SubmissionVolumeItem> SubmissionVolume { get; set; } = new();
    public List<TemplateHealthItem> TemplateHealth { get; set; } = new();
    public List<ModuleRankItem> TopModules { get; set; } = new();
    public List<HeatmapDay> HeatmapData { get; set; } = new();
    public List<ActivityFeedItem> ActivityFeed { get; set; } = new();
    public int DateRangeDays { get; set; } = 30;
}

/// <summary>
/// A single upcoming deadline entry for the dashboard.
/// </summary>
public class DeadlineItem
{
    public string ReturnCode { get; set; } = "";
    public string ReturnName { get; set; } = "";
    public DateTime DueDate { get; set; }
    public int DaysRemaining { get; set; }
    public DeadlineStatus Status { get; set; }
    public int? SubmissionId { get; set; }
    public string Frequency { get; set; } = "";
}

public enum DeadlineStatus
{
    NotStarted,
    Draft,
    Submitted,
    Overdue
}

/// <summary>
/// A recent submission entry for the dashboard.
/// </summary>
public class RecentSubmissionItem
{
    public int SubmissionId { get; set; }
    public string ReturnCode { get; set; } = "";
    public string ReturnName { get; set; } = "";
    public string Period { get; set; } = "";
    public DateTime SubmittedDate { get; set; }
    public SubmissionStatus Status { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
}

/// <summary>
/// Monthly compliance trend data point.
/// </summary>
public class ComplianceTrendItem
{
    public string MonthLabel { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal CompliancePercent { get; set; }
    public int Submitted { get; set; }
    public int Total { get; set; }
}

/// <summary>
/// Hero metric card with animated counter and 7-day sparkline.
/// </summary>
public class HeroMetric
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal Value { get; set; }
    public decimal PreviousValue { get; set; }
    public decimal ChangePercent { get; set; }
    public bool IsPositiveTrend { get; set; }
    public string Suffix { get; set; } = "";
    public List<decimal> SparklineData { get; set; } = new();
    public string Color { get; set; } = "#0f766e";
    public string IconPath { get; set; } = "";
}

/// <summary>
/// Submission volume data point (for stacked area chart).
/// </summary>
public class SubmissionVolumeItem
{
    public string Label { get; set; } = "";
    public int Accepted { get; set; }
    public int Rejected { get; set; }
    public int Pending { get; set; }
}

/// <summary>
/// Template health entry (for donut drill-down chart).
/// </summary>
public class TemplateHealthItem
{
    public string ReturnCode { get; set; } = "";
    public string ReturnName { get; set; } = "";
    public decimal PassRate { get; set; }
    public int TotalSubmissions { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
}

/// <summary>
/// Module ranking entry (for horizontal bar chart).
/// </summary>
public class ModuleRankItem
{
    public string ReturnCode { get; set; } = "";
    public string ReturnName { get; set; } = "";
    public int SubmissionCount { get; set; }
    public decimal ComplianceRate { get; set; }
}

/// <summary>
/// A single day's submission intensity for the heatmap.
/// </summary>
public class HeatmapDay
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
    public int Intensity { get; set; }  // 0–4
}

/// <summary>
/// Activity feed entry.
/// </summary>
public class ActivityFeedItem
{
    public string Icon { get; set; } = "submit";   // submit|approve|reject|validate|draft
    public string Message { get; set; } = "";
    public string Actor { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string? LinkUrl { get; set; }
    public string BadgeClass { get; set; } = "portal-badge-neutral";
}
