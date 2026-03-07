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

    // Drill-down data: submissions grouped by ReturnCode for the chart click-through modal
    public Dictionary<string, List<DrilldownSubmissionItem>> DrilldownByReturnCode { get; set; } = new();
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

/// <summary>
/// A single submission row shown in the chart drill-down modal.
/// </summary>
public class DrilldownSubmissionItem
{
    public int SubmissionId { get; set; }
    public string Period { get; set; } = "";
    public DateTime SubmittedDate { get; set; }
    public SubmissionStatus Status { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
}

// ── Compliance Performance Dashboard (/dashboard/compliance) ──────────

/// <summary>
/// Full data for the compliance performance dashboard.
/// </summary>
public class ComplianceDashboardData
{
    public string InstitutionName { get; set; } = "";
    public string InstitutionCode { get; set; } = "";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    // Hero cards
    public decimal ComplianceRate { get; set; }   // % of returns filed on time this period
    public int ReturnsFiled { get; set; }          // distinct accepted return codes this period
    public int Outstanding { get; set; }           // templates not yet accepted this period
    public int Overdue { get; set; }               // periods past deadline, no accepted submission

    // Quarter-over-quarter trend
    public decimal QuarterChange { get; set; }     // +/- percentage points vs last quarter
    public bool IsTrendImproving { get; set; }

    // Grouped bar chart — last 12 months
    public List<ComplianceBarMonth> MonthlyBars { get; set; } = new();

    // Per-module breakdown table
    public List<ComplianceModuleBreakdown> ModuleBreakdowns { get; set; } = new();

    // Filter option lists (always all templates, unfiltered)
    public List<string> AvailableReturnCodes { get; set; } = new();
    public List<string> AvailableFrequencies { get; set; } = new();
}

/// <summary>
/// One month's on-time / late / missed counts for the bar chart.
/// </summary>
public class ComplianceBarMonth
{
    public string MonthLabel { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    public int OnTime { get; set; }
    public int Late { get; set; }
    public int Missed { get; set; }
}

/// <summary>
/// One row in the compliance module breakdown table.
/// </summary>
public class ComplianceModuleBreakdown
{
    public string ReturnCode { get; set; } = "";
    public string ReturnName { get; set; } = "";
    public string Frequency { get; set; } = "";
    public int ReturnsDue { get; set; }            // past periods in last 12 months
    public int Submitted { get; set; }             // of those, periods with accepted submission
    public decimal ComplianceRate { get; set; }    // Submitted / ReturnsDue * 100
    public List<decimal> TrendSparkline { get; set; } = new(); // 6 months, values 0 or 100
}
