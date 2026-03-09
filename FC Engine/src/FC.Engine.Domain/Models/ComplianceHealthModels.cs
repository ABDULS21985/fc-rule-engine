namespace FC.Engine.Domain.Models;

// ── Enums ──

public enum ChsRating { APlus, A, B, C, D, F }
public enum ChsTrend { Improving, Stable, Declining }
public enum ChsAlertType { ConsecutiveDecline, BelowThreshold, PillarCritical }

// ── Core CHS Snapshot ──

public class ComplianceHealthScore
{
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string LicenceType { get; set; } = string.Empty;
    public decimal OverallScore { get; set; }
    public ChsRating Rating { get; set; }
    public ChsTrend Trend { get; set; }
    public decimal FilingTimeliness { get; set; }
    public decimal DataQuality { get; set; }
    public decimal RegulatoryCapital { get; set; }
    public decimal AuditGovernance { get; set; }
    public decimal Engagement { get; set; }
    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
    public string PeriodLabel { get; set; } = string.Empty;
}

// ── Pillar Detail ──

public class ChsPillarDetail
{
    public string PillarName { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public decimal Weight { get; set; }
    public decimal WeightedContribution { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<ChsPillarFactor> Factors { get; set; } = new();
    public ChsTrend Trend { get; set; }
}

public class ChsPillarFactor
{
    public string Name { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public decimal Max { get; set; }
    public string Unit { get; set; } = string.Empty;
}

// ── Trend / Time Series ──

public class ChsTrendSnapshot
{
    public string PeriodLabel { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal OverallScore { get; set; }
    public ChsRating Rating { get; set; }
    public decimal FilingTimeliness { get; set; }
    public decimal DataQuality { get; set; }
    public decimal RegulatoryCapital { get; set; }
    public decimal AuditGovernance { get; set; }
    public decimal Engagement { get; set; }
}

public class ChsTrendData
{
    public Guid TenantId { get; set; }
    public List<ChsTrendSnapshot> Snapshots { get; set; } = new();
    public ChsTrend OverallTrend { get; set; }
    public int ConsecutiveDeclines { get; set; }
}

// ── Alerts ──

public class ChsAlert
{
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public ChsAlertType AlertType { get; set; }
    public string Message { get; set; } = string.Empty;
    public decimal CurrentScore { get; set; }
    public decimal? PreviousScore { get; set; }
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;
    public string Severity { get; set; } = "warning";
}

// ── Peer Comparison ──

public class ChsPeerComparison
{
    public Guid TenantId { get; set; }
    public string LicenceType { get; set; } = string.Empty;
    public int PeerCount { get; set; }
    public decimal TenantScore { get; set; }
    public decimal PeerMedian { get; set; }
    public decimal PeerP25 { get; set; }
    public decimal PeerP75 { get; set; }
    public int Percentile { get; set; }
    public List<ChsDistributionBucket> Distribution { get; set; } = new();
    public List<ChsPillarPeerComparison> PillarComparisons { get; set; } = new();
}

public class ChsDistributionBucket
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public bool ContainsTenant { get; set; }
}

public class ChsPillarPeerComparison
{
    public string PillarName { get; set; } = string.Empty;
    public decimal TenantScore { get; set; }
    public decimal PeerMedian { get; set; }
    public decimal Delta { get; set; }
}

// ── Regulator Sector View ──

public class SectorChsSummary
{
    public string RegulatorCode { get; set; } = string.Empty;
    public decimal SectorAverage { get; set; }
    public decimal SectorMedian { get; set; }
    public int TotalInstitutions { get; set; }
    public Dictionary<ChsRating, int> RatingDistribution { get; set; } = new();
    public List<ChsTrendSnapshot> SectorTrend { get; set; } = new();
}

public class ChsWatchListItem
{
    public Guid TenantId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string LicenceType { get; set; } = string.Empty;
    public decimal CurrentScore { get; set; }
    public ChsRating Rating { get; set; }
    public ChsTrend Trend { get; set; }
    public int ConsecutiveDeclines { get; set; }
    public decimal ScoreChange { get; set; }
    public string WatchReason { get; set; } = string.Empty;
    public List<ChsAlert> RecentAlerts { get; set; } = new();
}

public class ChsHeatmapItem
{
    public Guid TenantId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string LicenceType { get; set; } = string.Empty;
    public decimal OverallScore { get; set; }
    public ChsRating Rating { get; set; }
    public decimal FilingTimeliness { get; set; }
    public decimal DataQuality { get; set; }
    public decimal RegulatoryCapital { get; set; }
    public decimal AuditGovernance { get; set; }
    public decimal Engagement { get; set; }
}

// ── Full Dashboard Aggregate ──

public class ChsDashboardData
{
    public ComplianceHealthScore Current { get; set; } = new();
    public List<ChsPillarDetail> Pillars { get; set; } = new();
    public ChsTrendData Trend { get; set; } = new();
    public ChsPeerComparison PeerComparison { get; set; } = new();
    public List<ChsAlert> ActiveAlerts { get; set; } = new();
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}
