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
