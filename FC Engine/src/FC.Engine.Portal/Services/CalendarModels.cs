namespace FC.Engine.Portal.Services;

/// <summary>
/// Aggregated calendar data for a single institution.
/// </summary>
public class CalendarData
{
    public List<CalendarEntry> Entries { get; set; } = new();
    public CalendarSummary Summary { get; set; } = new();
}

/// <summary>
/// A single reporting obligation: one (template × period) combination.
/// </summary>
public class CalendarEntry
{
    public string ReturnCode { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public string Frequency { get; set; } = "";
    public DateTime DueDate { get; set; }
    public string PeriodLabel { get; set; } = "";
    public string PeriodValue { get; set; } = "";
    public CalendarEntryStatus Status { get; set; }
    public int? SubmissionId { get; set; }
    public int DaysUntilDue { get; set; }
}

/// <summary>
/// Period summary statistics for the current month.
/// </summary>
public class CalendarSummary
{
    public int TotalDueThisMonth { get; set; }
    public int SubmittedThisMonth { get; set; }
    public int OutstandingThisMonth { get; set; }
    public int OverdueThisMonth { get; set; }
    public int CompliancePercentage { get; set; }
}

/// <summary>
/// Represents a single day in the calendar grid.
/// </summary>
public class CalendarDay
{
    public DateTime Date { get; set; }
    public bool IsCurrentMonth { get; set; }
    public bool IsToday { get; set; }
    public List<CalendarEntry> Entries { get; set; } = new();
}

public enum CalendarEntryStatus
{
    NotStarted,
    Draft,
    Submitted,
    Rejected,
    Overdue
}
