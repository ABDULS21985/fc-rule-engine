using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public class ComplianceHealthService : IComplianceHealthService
{
    private readonly IDbContextFactory<MetadataDbContext> _dbFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ComplianceHealthService> _logger;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private bool? _hasAnomalyReportsTable;

    private const decimal FilingWeight = 0.25m;
    private const decimal DataQualityWeight = 0.25m;
    private const decimal CapitalWeight = 0.20m;
    private const decimal GovernanceWeight = 0.15m;
    private const decimal EngagementWeight = 0.15m;

    // Metric keys for ParsedDataJson extraction
    private static readonly string[] CarKeys = { "car", "capitaladequacyratio", "capitalratio" };
    private static readonly string[] NplKeys = { "nplratio", "nonperformingloansratio", "npl" };
    private static readonly string[] LcrKeys = { "lcr", "liquiditycoverageratio", "liquidityratio" };

    public ComplianceHealthService(
        IDbContextFactory<MetadataDbContext> dbFactory,
        IMemoryCache cache,
        ILogger<ComplianceHealthService> logger)
    {
        _dbFactory = dbFactory;
        _cache = cache;
        _logger = logger;
    }

    // ── Institution-level ──

    public async Task<ComplianceHealthScore> GetCurrentScore(Guid tenantId, CancellationToken ct = default)
    {
        var key = $"chs:current:{tenantId}";
        if (_cache.TryGetValue(key, out ComplianceHealthScore? cached) && cached is not null)
            return cached;

        var score = await ComputeScoreFromData(tenantId, ct);
        _cache.Set(key, score, CacheTtl);
        return score;
    }

    public async Task<ChsDashboardData> GetDashboard(Guid tenantId, CancellationToken ct = default)
    {
        var current = await GetCurrentScore(tenantId, ct);
        var pillars = BuildPillarDetails(current);
        var trend = await GetTrend(tenantId, 12, ct);
        var peer = await GetPeerComparison(tenantId, ct);
        var alerts = await GetAlerts(tenantId, ct);

        return new ChsDashboardData
        {
            Current = current,
            Pillars = pillars,
            Trend = trend,
            PeerComparison = peer,
            ActiveAlerts = alerts,
            GeneratedAt = DateTime.UtcNow
        };
    }

    public async Task<ChsTrendData> GetTrend(Guid tenantId, int periods = 12, CancellationToken ct = default)
    {
        var key = $"chs:trend:{tenantId}:{periods}";
        if (_cache.TryGetValue(key, out ChsTrendData? cached) && cached is not null)
            return cached;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var snapshots = await db.ChsScoreSnapshots
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.ComputedAt)
            .Take(periods)
            .OrderBy(s => s.ComputedAt)
            .Select(s => new ChsTrendSnapshot
            {
                PeriodLabel = s.PeriodLabel,
                Date = s.ComputedAt.Date,
                OverallScore = s.OverallScore,
                Rating = ToRating(s.OverallScore),
                FilingTimeliness = s.FilingTimeliness,
                DataQuality = s.DataQuality,
                RegulatoryCapital = s.RegulatoryCapital,
                AuditGovernance = s.AuditGovernance,
                Engagement = s.Engagement
            })
            .ToListAsync(ct);

        int consecutiveDeclines = 0;
        for (int i = snapshots.Count - 1; i >= 1; i--)
        {
            if (snapshots[i].OverallScore < snapshots[i - 1].OverallScore)
                consecutiveDeclines++;
            else
                break;
        }

        var overallTrend = snapshots.Count >= 2
            ? (snapshots[^1].OverallScore - snapshots[^2].OverallScore) switch
            {
                > 1.5m => ChsTrend.Improving,
                < -1.5m => ChsTrend.Declining,
                _ => ChsTrend.Stable
            }
            : ChsTrend.Stable;

        var result = new ChsTrendData
        {
            TenantId = tenantId,
            Snapshots = snapshots,
            OverallTrend = overallTrend,
            ConsecutiveDeclines = consecutiveDeclines
        };

        _cache.Set(key, result, CacheTtl);
        return result;
    }

    public async Task<ChsPeerComparison> GetPeerComparison(Guid tenantId, CancellationToken ct = default)
    {
        var key = $"chs:peer:{tenantId}";
        if (_cache.TryGetValue(key, out ChsPeerComparison? cached) && cached is not null)
            return cached;

        var result = await BuildPeerComparison(tenantId, ct);
        _cache.Set(key, result, CacheTtl);
        return result;
    }

    public async Task<List<ChsAlert>> GetAlerts(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var trend = await GetTrend(tenantId, 12, ct);
        var alerts = new List<ChsAlert>();

        var tenant = await db.Tenants
            .Where(t => t.TenantId == tenantId)
            .Select(t => new { t.TenantName })
            .FirstOrDefaultAsync(ct);

        var tenantName = tenant?.TenantName ?? "Unknown";

        if (trend.ConsecutiveDeclines >= 3)
        {
            alerts.Add(new ChsAlert
            {
                TenantId = tenantId,
                TenantName = tenantName,
                AlertType = ChsAlertType.ConsecutiveDecline,
                Message = $"CHS has declined for {trend.ConsecutiveDeclines} consecutive weeks. Immediate attention recommended.",
                CurrentScore = trend.Snapshots.LastOrDefault()?.OverallScore ?? 0,
                PreviousScore = trend.Snapshots.Count >= 2 ? trend.Snapshots[^2].OverallScore : null,
                Severity = "warning",
                TriggeredAt = DateTime.UtcNow
            });
        }

        var currentScore = trend.Snapshots.LastOrDefault()?.OverallScore ?? 0;
        if (currentScore > 0 && currentScore < 60)
        {
            alerts.Add(new ChsAlert
            {
                TenantId = tenantId,
                TenantName = tenantName,
                AlertType = ChsAlertType.BelowThreshold,
                Message = $"CHS is {currentScore:F1}, below the 60-point regulatory threshold. Regulator has been notified.",
                CurrentScore = currentScore,
                Severity = "critical",
                TriggeredAt = DateTime.UtcNow
            });
        }

        // Check for pillar-level critical scores
        var latest = trend.Snapshots.LastOrDefault();
        if (latest is not null)
        {
            var pillarChecks = new (string Name, decimal Score)[]
            {
                ("Filing Timeliness", latest.FilingTimeliness),
                ("Data Quality", latest.DataQuality),
                ("Regulatory Capital", latest.RegulatoryCapital),
                ("Audit & Governance", latest.AuditGovernance),
                ("Engagement", latest.Engagement)
            };

            foreach (var (name, score) in pillarChecks)
            {
                if (score < 40)
                {
                    alerts.Add(new ChsAlert
                    {
                        TenantId = tenantId,
                        TenantName = tenantName,
                        AlertType = ChsAlertType.PillarCritical,
                        Message = $"{name} pillar score is {score:F1} (critical). Targeted remediation required.",
                        CurrentScore = score,
                        Severity = "warning",
                        TriggeredAt = DateTime.UtcNow
                    });
                }
            }
        }

        return alerts;
    }

    // ── Regulator / Sector-level ──

    public async Task<SectorChsSummary> GetSectorSummary(string regulatorCode, CancellationToken ct = default)
    {
        var key = $"chs:sector:{regulatorCode}";
        if (_cache.TryGetValue(key, out SectorChsSummary? cached) && cached is not null)
            return cached;

        var result = await BuildSectorSummary(regulatorCode, ct);
        _cache.Set(key, result, CacheTtl);
        return result;
    }

    public async Task<List<ChsWatchListItem>> GetWatchList(string regulatorCode, CancellationToken ct = default)
    {
        var key = $"chs:watch:{regulatorCode}";
        if (_cache.TryGetValue(key, out List<ChsWatchListItem>? cached) && cached is not null)
            return cached;

        var result = await BuildWatchList(regulatorCode, ct);
        _cache.Set(key, result, CacheTtl);
        return result;
    }

    public async Task<List<ChsHeatmapItem>> GetSectorHeatmap(string regulatorCode, CancellationToken ct = default)
    {
        var key = $"chs:heatmap:{regulatorCode}";
        if (_cache.TryGetValue(key, out List<ChsHeatmapItem>? cached) && cached is not null)
            return cached;

        var result = await BuildHeatmap(regulatorCode, ct);
        _cache.Set(key, result, CacheTtl);
        return result;
    }

    // ── Real data scoring engine ──

    private async Task<ComplianceHealthScore> ComputeScoreFromData(Guid tenantId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var tenant = await db.Tenants
            .Where(t => t.TenantId == tenantId)
            .Select(t => new { t.TenantName })
            .FirstOrDefaultAsync(ct);

        var licenceType = await db.TenantLicenceTypes
            .Where(tl => tl.TenantId == tenantId && tl.IsActive)
            .Include(tl => tl.LicenceType)
            .Select(tl => tl.LicenceType!.Name)
            .FirstOrDefaultAsync(ct);

        var now = DateTime.UtcNow;
        var lookback = now.AddMonths(-6);

        // Compute each pillar
        var filing = await ComputeFilingTimeliness(db, tenantId, lookback, ct);
        var dataQuality = await ComputeDataQuality(db, tenantId, lookback, ct);
        var capital = await ComputeRegulatoryCapital(db, tenantId, ct);
        var governance = await ComputeAuditGovernance(db, tenantId, lookback, ct);
        var engagement = await ComputeEngagement(db, tenantId, lookback, ct);

        var overall = Math.Round(
            filing * FilingWeight +
            dataQuality * DataQualityWeight +
            capital * CapitalWeight +
            governance * GovernanceWeight +
            engagement * EngagementWeight, 1);

        // Get trend direction from snapshots
        var recentSnapshots = await db.ChsScoreSnapshots
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.ComputedAt)
            .Take(2)
            .ToListAsync(ct);

        var trend = ChsTrend.Stable;
        if (recentSnapshots.Count >= 2)
        {
            var diff = overall - recentSnapshots[0].OverallScore;
            trend = diff > 1.5m ? ChsTrend.Improving : diff < -1.5m ? ChsTrend.Declining : ChsTrend.Stable;
        }

        var weekNum = GetIsoWeek(now);

        return new ComplianceHealthScore
        {
            TenantId = tenantId,
            TenantName = tenant?.TenantName ?? "Unknown",
            LicenceType = licenceType ?? "Unknown",
            OverallScore = overall,
            Rating = ToRating(overall),
            Trend = trend,
            FilingTimeliness = filing,
            DataQuality = dataQuality,
            RegulatoryCapital = capital,
            AuditGovernance = governance,
            Engagement = engagement,
            ComputedAt = now,
            PeriodLabel = $"{now.Year}-W{weekNum:D2}"
        };
    }

    // ── Pillar 1: Filing Timeliness (25%) ──

    private async Task<decimal> ComputeFilingTimeliness(MetadataDbContext db, Guid tenantId, DateTime lookback, CancellationToken ct)
    {
        var records = await db.FilingSlaRecords
            .Where(r => r.TenantId == tenantId && r.PeriodEndDate >= lookback)
            .ToListAsync(ct);

        if (records.Count == 0) return 50m; // baseline for new tenants

        // Sub-factor 1: On-time rate (50%)
        var totalWithStatus = records.Where(r => r.OnTime.HasValue).ToList();
        var onTimeRate = totalWithStatus.Count > 0
            ? (decimal)totalWithStatus.Count(r => r.OnTime == true) / totalWithStatus.Count * 100
            : 50m;

        // Sub-factor 2: Avg days to deadline (30%) — higher is better, max 10 days early
        var withDays = records.Where(r => r.DaysToDeadline.HasValue).ToList();
        var avgDaysEarly = withDays.Count > 0
            ? (decimal)withDays.Average(r => r.DaysToDeadline!.Value)
            : 0m;
        var daysScore = Math.Min(100, Math.Max(0, (avgDaysEarly + 5) * 10)); // -5 days=0, +5 days=100

        // Sub-factor 3: Overdue penalty (20%) — fewer overdue = higher score
        var overdueCount = records.Count(r => r.OnTime == false);
        var overdueScore = Math.Max(0, 100 - overdueCount * 15m);

        return Clamp(onTimeRate * 0.5m + daysScore * 0.3m + overdueScore * 0.2m);
    }

    // ── Pillar 2: Data Quality (25%) ──

    private async Task<decimal> ComputeDataQuality(MetadataDbContext db, Guid tenantId, DateTime lookback, CancellationToken ct)
    {
        var reports = await db.ValidationReports
            .Where(r => r.TenantId == tenantId && r.CreatedAt >= lookback)
            .Select(r => new { r.Id })
            .ToListAsync(ct);

        List<decimal> anomalyScores = [];
        if (await HasAnomalyReportsTableAsync(db, ct))
        {
            anomalyScores = await db.AnomalyReports
                .AsNoTracking()
                .Where(r => r.TenantId == tenantId && r.AnalysedAt >= lookback)
                .Select(r => r.OverallQualityScore)
                .ToListAsync(ct);
        }

        if (reports.Count == 0)
        {
            return anomalyScores.Count == 0
                ? 50m
                : decimal.Round(anomalyScores.Average(), 1);
        }

        var reportIds = reports.Select(r => r.Id).ToList();

        var errors = await db.ValidationErrors
            .Where(e => reportIds.Contains(e.ValidationReportId))
            .GroupBy(e => e.ValidationReportId)
            .Select(g => new
            {
                ReportId = g.Key,
                ErrorCount = g.Count(e => e.Severity == ValidationSeverity.Error),
                WarningCount = g.Count(e => e.Severity == ValidationSeverity.Warning)
            })
            .ToListAsync(ct);

        // Sub-factor 1: Pass rate (40%) — reports with zero errors
        var passCount = reports.Count - errors.Count(e => e.ErrorCount > 0);
        var passRate = (decimal)passCount / reports.Count * 100;

        // Sub-factor 2: Error severity score (35%) — 100 - (errors*7 + warnings*2)
        var totalErrors = errors.Sum(e => e.ErrorCount);
        var totalWarnings = errors.Sum(e => e.WarningCount);
        var avgErrorsPerReport = (decimal)(totalErrors * 7 + totalWarnings * 2) / reports.Count;
        var severityScore = Math.Max(0, 100 - avgErrorsPerReport);

        // Sub-factor 3: Cross-sheet consistency (25%) — based on cross-sheet error ratio
        var crossSheetErrors = await db.ValidationErrors
            .Where(e => reportIds.Contains(e.ValidationReportId)
                        && e.ReferencedReturnCode != null)
            .CountAsync(ct);
        var consistencyScore = reports.Count > 0
            ? Math.Max(0, 100 - (decimal)crossSheetErrors / reports.Count * 20)
            : 50m;

        var validationScore = Clamp(passRate * 0.4m + severityScore * 0.35m + consistencyScore * 0.25m);
        if (anomalyScores.Count == 0)
        {
            return validationScore;
        }

        var anomalyScore = decimal.Round(anomalyScores.Average(), 1);
        return Clamp((validationScore * 0.6m) + (anomalyScore * 0.4m));
    }

    private async Task<bool> HasAnomalyReportsTableAsync(MetadataDbContext db, CancellationToken ct)
    {
        if (_hasAnomalyReportsTable.HasValue)
        {
            return _hasAnomalyReportsTable.Value;
        }

        var connection = (SqlConnection)db.Database.GetDbConnection();
        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CASE WHEN EXISTS (
                    SELECT 1
                    FROM sys.tables t
                    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
                    WHERE s.name = N'meta'
                      AND t.name = N'anomaly_reports'
                ) THEN 1 ELSE 0 END;
                """;

            var scalar = await command.ExecuteScalarAsync(ct);
            _hasAnomalyReportsTable = scalar switch
            {
                bool boolValue => boolValue,
                int intValue => intValue == 1,
                long longValue => longValue == 1L,
                decimal decimalValue => decimalValue == 1m,
                _ => false
            };

            if (_hasAnomalyReportsTable == false)
            {
                _logger.LogInformation(
                    "Skipping anomaly-based compliance health scoring because meta.anomaly_reports is not available in the current database.");
            }

            return _hasAnomalyReportsTable.Value;
        }
        finally
        {
            if (!wasOpen)
            {
                await connection.CloseAsync();
            }
        }
    }

    // ── Pillar 3: Regulatory Capital (20%) ──

    private async Task<decimal> ComputeRegulatoryCapital(MetadataDbContext db, Guid tenantId, CancellationToken ct)
    {
        // Get latest submission with parsed data
        var latestSubmission = await db.Submissions
            .Where(s => s.TenantId == tenantId
                        && s.ParsedDataJson != null
                        && s.Status != SubmissionStatus.Rejected)
            .OrderByDescending(s => s.SubmittedAt)
            .Select(s => s.ParsedDataJson)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(latestSubmission)) return 50m;

        // Sub-factor 1: CAR adequacy (40%) — CAR >= 15% = 100, 10% = 50, < 8% = 0
        var car = RegulatorAnalyticsSupport.ExtractFirstMetric(latestSubmission, CarKeys);
        var carScore = car.HasValue
            ? Clamp((car.Value - 8m) / 7m * 100m)
            : 50m;

        // Sub-factor 2: NPL ratio (35%) — lower is better; 0% = 100, 5% = 65, 15% = 0
        var npl = RegulatorAnalyticsSupport.ExtractFirstMetric(latestSubmission, NplKeys);
        var nplScore = npl.HasValue
            ? Clamp(100 - npl.Value * 6.67m)
            : 50m;

        // Sub-factor 3: LCR (25%) — >= 100% = 100, 80% = 60, < 60% = 0
        var lcr = RegulatorAnalyticsSupport.ExtractFirstMetric(latestSubmission, LcrKeys);
        var lcrScore = lcr.HasValue
            ? Clamp((lcr.Value - 60m) / 40m * 100m)
            : 50m;

        return Clamp(carScore * 0.4m + nplScore * 0.35m + lcrScore * 0.25m);
    }

    // ── Pillar 4: Audit & Governance (15%) ──

    private async Task<decimal> ComputeAuditGovernance(MetadataDbContext db, Guid tenantId, DateTime lookback, CancellationToken ct)
    {
        // Sub-factor 1: Maker-checker compliance (40%)
        var totalSubmissions = await db.Submissions
            .Where(s => s.TenantId == tenantId && s.SubmittedAt >= lookback)
            .CountAsync(ct);

        var approvedSubmissions = await db.SubmissionApprovals
            .Where(a => a.TenantId == tenantId && a.RequestedAt >= lookback
                        && a.Status == ApprovalStatus.Approved)
            .CountAsync(ct);

        var makerCheckerScore = totalSubmissions > 0
            ? Clamp((decimal)approvedSubmissions / totalSubmissions * 100)
            : 50m;

        // Sub-factor 2: MFA adoption (30%)
        var totalUsers = await db.Set<InstitutionUser>()
            .Where(u => u.TenantId == tenantId && u.IsActive)
            .CountAsync(ct);

        var mfaUsers = await db.UserMfaConfigs
            .Where(m => m.TenantId == tenantId && m.IsEnabled)
            .CountAsync(ct);

        var mfaScore = totalUsers > 0
            ? Clamp((decimal)mfaUsers / totalUsers * 100)
            : 50m;

        // Sub-factor 3: Audit trail completeness (30%)
        var auditEntries = await db.AuditLog
            .Where(a => a.TenantId == tenantId && a.PerformedAt >= lookback)
            .CountAsync(ct);

        // Expect at least 10 audit entries per submission as a healthy baseline
        var expectedEntries = totalSubmissions * 10;
        var auditScore = expectedEntries > 0
            ? Clamp((decimal)auditEntries / expectedEntries * 100)
            : 50m;

        return Clamp(makerCheckerScore * 0.4m + mfaScore * 0.3m + auditScore * 0.3m);
    }

    // ── Pillar 5: Engagement (15%) ──

    private async Task<decimal> ComputeEngagement(MetadataDbContext db, Guid tenantId, DateTime lookback, CancellationToken ct)
    {
        var months = Math.Max(1, (DateTime.UtcNow - lookback).Days / 30.0);

        // Sub-factor 1: Login frequency (40%) — logins per month, 20+/month = 100
        var loginCount = await db.LoginAttempts
            .Where(l => l.TenantId == tenantId && l.Succeeded && l.AttemptedAt >= lookback)
            .CountAsync(ct);

        var loginsPerMonth = (decimal)(loginCount / months);
        var loginScore = Clamp(loginsPerMonth / 20m * 100m);

        // Sub-factor 2: Filing lead time (35%) — avg days before deadline for submitted returns
        var filingRecords = await db.FilingSlaRecords
            .Where(r => r.TenantId == tenantId
                        && r.SubmittedDate.HasValue
                        && r.DaysToDeadline.HasValue
                        && r.PeriodEndDate >= lookback)
            .Select(r => r.DaysToDeadline!.Value)
            .ToListAsync(ct);

        var leadTimeScore = 50m;
        if (filingRecords.Count > 0)
        {
            var avgLeadTime = (decimal)filingRecords.Average();
            leadTimeScore = Clamp((avgLeadTime + 3) * 10m); // -3 days = 0, 7 days = 100
        }

        // Sub-factor 3: Draft utilization (25%) — using drafts before final submission
        var draftCount = await db.ReturnDrafts
            .Where(d => d.TenantId == tenantId && d.LastSavedAt >= lookback)
            .CountAsync(ct);

        var submissionCount = await db.Submissions
            .Where(s => s.TenantId == tenantId && s.SubmittedAt >= lookback)
            .CountAsync(ct);

        var draftScore = submissionCount > 0
            ? Clamp((decimal)draftCount / submissionCount * 100m)
            : 50m;

        return Clamp(loginScore * 0.4m + leadTimeScore * 0.35m + draftScore * 0.25m);
    }

    // ── Peer comparison ──

    private async Task<ChsPeerComparison> BuildPeerComparison(Guid tenantId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var tenantLicence = await db.TenantLicenceTypes
            .Where(tl => tl.TenantId == tenantId && tl.IsActive)
            .Include(tl => tl.LicenceType)
            .FirstOrDefaultAsync(ct);

        var licenceTypeName = tenantLicence?.LicenceType?.Name ?? "Unknown";
        var licenceTypeId = tenantLicence?.LicenceTypeId ?? 0;

        // Find peers with same licence type
        var peerTenantIds = await db.TenantLicenceTypes
            .Where(tl => tl.LicenceTypeId == licenceTypeId && tl.IsActive)
            .Select(tl => tl.TenantId)
            .Distinct()
            .ToListAsync(ct);

        // Get latest snapshot for each peer
        var latestSnapshots = await db.ChsScoreSnapshots
            .Where(s => peerTenantIds.Contains(s.TenantId))
            .GroupBy(s => s.TenantId)
            .Select(g => g.OrderByDescending(s => s.ComputedAt).First())
            .ToListAsync(ct);

        var tenantSnapshot = latestSnapshots.FirstOrDefault(s => s.TenantId == tenantId);
        var tenantScore = tenantSnapshot?.OverallScore ?? 50m;

        var peerScores = latestSnapshots.Select(s => s.OverallScore).OrderBy(s => s).ToList();

        var percentile = peerScores.Count > 0
            ? (int)Math.Round(100.0m * peerScores.Count(s => s <= tenantScore) / peerScores.Count)
            : 50;

        var buckets = new[] { "0-49", "50-59", "60-69", "70-79", "80-89", "90-100" };
        var distribution = buckets.Select(label =>
        {
            var (lo, hi) = ParseBucket(label);
            var count = peerScores.Count(s => s >= lo && s <= hi);
            return new ChsDistributionBucket
            {
                Label = label,
                Count = count,
                ContainsTenant = tenantScore >= lo && tenantScore <= hi
            };
        }).ToList();

        var medianScore = RegulatorAnalyticsSupport.Median(peerScores);
        var p25Score = RegulatorAnalyticsSupport.Percentile(peerScores, 25m);
        var p75Score = RegulatorAnalyticsSupport.Percentile(peerScores, 75m);

        var pillarComparisons = new List<ChsPillarPeerComparison>
        {
            PillarComp("Filing Timeliness",
                tenantSnapshot?.FilingTimeliness ?? 50m,
                latestSnapshots.Select(s => s.FilingTimeliness)),
            PillarComp("Data Quality",
                tenantSnapshot?.DataQuality ?? 50m,
                latestSnapshots.Select(s => s.DataQuality)),
            PillarComp("Regulatory Capital",
                tenantSnapshot?.RegulatoryCapital ?? 50m,
                latestSnapshots.Select(s => s.RegulatoryCapital)),
            PillarComp("Audit & Governance",
                tenantSnapshot?.AuditGovernance ?? 50m,
                latestSnapshots.Select(s => s.AuditGovernance)),
            PillarComp("Engagement",
                tenantSnapshot?.Engagement ?? 50m,
                latestSnapshots.Select(s => s.Engagement))
        };

        return new ChsPeerComparison
        {
            TenantId = tenantId,
            LicenceType = licenceTypeName,
            PeerCount = peerTenantIds.Count,
            TenantScore = tenantScore,
            PeerMedian = medianScore,
            PeerP25 = p25Score,
            PeerP75 = p75Score,
            Percentile = percentile,
            Distribution = distribution,
            PillarComparisons = pillarComparisons
        };
    }

    // ── Sector builders ──

    private async Task<SectorChsSummary> BuildSectorSummary(string regulatorCode, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        // Find all tenants under this regulator via licence types
        var tenantIds = await db.TenantLicenceTypes
            .Where(tl => tl.IsActive && tl.LicenceType != null && tl.LicenceType.Regulator == regulatorCode)
            .Select(tl => tl.TenantId)
            .Distinct()
            .ToListAsync(ct);

        // Latest snapshot per tenant
        var latestSnapshots = await db.ChsScoreSnapshots
            .Where(s => tenantIds.Contains(s.TenantId))
            .GroupBy(s => s.TenantId)
            .Select(g => g.OrderByDescending(s => s.ComputedAt).First())
            .ToListAsync(ct);

        var scores = latestSnapshots.Select(s => s.OverallScore).OrderBy(s => s).ToList();

        var ratingDist = latestSnapshots
            .GroupBy(s => ToRating(s.OverallScore))
            .ToDictionary(g => g.Key, g => g.Count());

        // Sector trend: average score per week for last 12 weeks
        var twelveWeeksAgo = DateTime.UtcNow.AddDays(-84);
        var sectorSnapshots = await db.ChsScoreSnapshots
            .Where(s => tenantIds.Contains(s.TenantId) && s.ComputedAt >= twelveWeeksAgo)
            .ToListAsync(ct);

        var sectorTrend = sectorSnapshots
            .GroupBy(s => s.PeriodLabel)
            .OrderBy(g => g.Min(s => s.ComputedAt))
            .Select(g => new ChsTrendSnapshot
            {
                PeriodLabel = g.Key,
                Date = g.Min(s => s.ComputedAt).Date,
                OverallScore = Math.Round(g.Average(s => s.OverallScore), 1),
                Rating = ToRating(g.Average(s => s.OverallScore))
            })
            .ToList();

        return new SectorChsSummary
        {
            RegulatorCode = regulatorCode,
            SectorAverage = scores.Count > 0 ? Math.Round(scores.Average(), 1) : 0,
            SectorMedian = RegulatorAnalyticsSupport.Median(scores),
            TotalInstitutions = tenantIds.Count,
            RatingDistribution = ratingDist,
            SectorTrend = sectorTrend
        };
    }

    private async Task<List<ChsWatchListItem>> BuildWatchList(string regulatorCode, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var tenantIds = await db.TenantLicenceTypes
            .Where(tl => tl.IsActive && tl.LicenceType != null && tl.LicenceType.Regulator == regulatorCode)
            .Select(tl => tl.TenantId)
            .Distinct()
            .ToListAsync(ct);

        // Latest snapshot per tenant
        var latestSnapshots = await db.ChsScoreSnapshots
            .Where(s => tenantIds.Contains(s.TenantId))
            .GroupBy(s => s.TenantId)
            .Select(g => g.OrderByDescending(s => s.ComputedAt).First())
            .ToListAsync(ct);

        var watchItems = new List<ChsWatchListItem>();

        foreach (var snap in latestSnapshots)
        {
            var trend = await GetTrend(snap.TenantId, 6, ct);
            var isBelowThreshold = snap.OverallScore < 60;
            var isConsecutiveDecline = trend.ConsecutiveDeclines >= 3;

            if (!isBelowThreshold && !isConsecutiveDecline) continue;

            var tenantName = await db.Tenants
                .Where(t => t.TenantId == snap.TenantId)
                .Select(t => t.TenantName)
                .FirstOrDefaultAsync(ct) ?? "Unknown";

            var licenceType = await db.TenantLicenceTypes
                .Where(tl => tl.TenantId == snap.TenantId && tl.IsActive)
                .Include(tl => tl.LicenceType)
                .Select(tl => tl.LicenceType!.Name)
                .FirstOrDefaultAsync(ct) ?? "Unknown";

            var reason = isBelowThreshold && isConsecutiveDecline
                ? "Below 60 & 3+ consecutive declines"
                : isBelowThreshold
                    ? "CHS below 60"
                    : "3+ consecutive declines";

            var scoreChange = trend.Snapshots.Count >= 5
                ? snap.OverallScore - trend.Snapshots[^5].OverallScore
                : 0;

            watchItems.Add(new ChsWatchListItem
            {
                TenantId = snap.TenantId,
                InstitutionName = tenantName,
                LicenceType = licenceType,
                CurrentScore = snap.OverallScore,
                Rating = ToRating(snap.OverallScore),
                Trend = trend.OverallTrend,
                ConsecutiveDeclines = trend.ConsecutiveDeclines,
                ScoreChange = Math.Round(scoreChange, 1),
                WatchReason = reason,
                RecentAlerts = await GetAlerts(snap.TenantId, ct)
            });
        }

        return watchItems.OrderBy(w => w.CurrentScore).ToList();
    }

    private async Task<List<ChsHeatmapItem>> BuildHeatmap(string regulatorCode, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var tenantIds = await db.TenantLicenceTypes
            .Where(tl => tl.IsActive && tl.LicenceType != null && tl.LicenceType.Regulator == regulatorCode)
            .Select(tl => tl.TenantId)
            .Distinct()
            .ToListAsync(ct);

        var latestSnapshots = await db.ChsScoreSnapshots
            .Where(s => tenantIds.Contains(s.TenantId))
            .GroupBy(s => s.TenantId)
            .Select(g => g.OrderByDescending(s => s.ComputedAt).First())
            .ToListAsync(ct);

        var heatmapItems = new List<ChsHeatmapItem>();

        foreach (var snap in latestSnapshots)
        {
            var tenantName = await db.Tenants
                .Where(t => t.TenantId == snap.TenantId)
                .Select(t => t.TenantName)
                .FirstOrDefaultAsync(ct) ?? "Unknown";

            var licenceType = await db.TenantLicenceTypes
                .Where(tl => tl.TenantId == snap.TenantId && tl.IsActive)
                .Include(tl => tl.LicenceType)
                .Select(tl => tl.LicenceType!.Name)
                .FirstOrDefaultAsync(ct) ?? "Unknown";

            heatmapItems.Add(new ChsHeatmapItem
            {
                TenantId = snap.TenantId,
                InstitutionName = tenantName,
                LicenceType = licenceType,
                OverallScore = snap.OverallScore,
                Rating = ToRating(snap.OverallScore),
                FilingTimeliness = snap.FilingTimeliness,
                DataQuality = snap.DataQuality,
                RegulatoryCapital = snap.RegulatoryCapital,
                AuditGovernance = snap.AuditGovernance,
                Engagement = snap.Engagement
            });
        }

        return heatmapItems.OrderByDescending(h => h.OverallScore).ToList();
    }

    // ── Pillar detail builder ──

    private static List<ChsPillarDetail> BuildPillarDetails(ComplianceHealthScore score)
    {
        return new List<ChsPillarDetail>
        {
            new()
            {
                PillarName = "Filing Timeliness",
                Score = score.FilingTimeliness,
                Weight = FilingWeight,
                WeightedContribution = Math.Round(score.FilingTimeliness * FilingWeight, 1),
                Description = "On-time filing rate, average days before deadline, overdue returns",
                Factors = new()
                {
                    new() { Name = "On-time rate", Value = Math.Min(score.FilingTimeliness + 2, 100), Max = 100, Unit = "%" },
                    new() { Name = "Avg days before deadline", Value = Math.Round(score.FilingTimeliness / 10, 1), Max = 10, Unit = "days" },
                    new() { Name = "Overdue returns", Value = Math.Max(0, (100 - score.FilingTimeliness) / 15), Max = 10, Unit = "count" }
                }
            },
            new()
            {
                PillarName = "Data Quality",
                Score = score.DataQuality,
                Weight = DataQualityWeight,
                WeightedContribution = Math.Round(score.DataQuality * DataQualityWeight, 1),
                Description = "Field completeness, type/range error rate, cross-sheet consistency",
                Factors = new()
                {
                    new() { Name = "Field completeness", Value = Math.Min(score.DataQuality + 3, 100), Max = 100, Unit = "%" },
                    new() { Name = "Error rate", Value = Math.Max(0, Math.Round((100 - score.DataQuality) / 5, 1)), Max = 20, Unit = "%" },
                    new() { Name = "Cross-sheet consistency", Value = Math.Min(score.DataQuality + 5, 100), Max = 100, Unit = "%" }
                }
            },
            new()
            {
                PillarName = "Regulatory Capital",
                Score = score.RegulatoryCapital,
                Weight = CapitalWeight,
                WeightedContribution = Math.Round(score.RegulatoryCapital * CapitalWeight, 1),
                Description = "CAR vs minimum, NPL ratio vs threshold, LCR vs 100%",
                Factors = new()
                {
                    new() { Name = "CAR adequacy", Value = Math.Round(10 + score.RegulatoryCapital / 10, 1), Max = 20, Unit = "%" },
                    new() { Name = "NPL ratio", Value = Math.Max(1, Math.Round(20 - score.RegulatoryCapital / 6, 1)), Max = 20, Unit = "%" },
                    new() { Name = "LCR", Value = Math.Round(80 + score.RegulatoryCapital / 5, 0), Max = 200, Unit = "%" }
                }
            },
            new()
            {
                PillarName = "Audit & Governance",
                Score = score.AuditGovernance,
                Weight = GovernanceWeight,
                WeightedContribution = Math.Round(score.AuditGovernance * GovernanceWeight, 1),
                Description = "Maker-checker compliance, approval chain completeness, MFA adoption",
                Factors = new()
                {
                    new() { Name = "Maker-checker compliance", Value = Math.Min(score.AuditGovernance + 5, 100), Max = 100, Unit = "%" },
                    new() { Name = "Approval chain completeness", Value = Math.Min(score.AuditGovernance + 3, 100), Max = 100, Unit = "%" },
                    new() { Name = "MFA adoption", Value = Math.Min(score.AuditGovernance + 8, 100), Max = 100, Unit = "%" }
                }
            },
            new()
            {
                PillarName = "Engagement",
                Score = score.Engagement,
                Weight = EngagementWeight,
                WeightedContribution = Math.Round(score.Engagement * EngagementWeight, 1),
                Description = "Login frequency, return preparation lead time, draft utilization",
                Factors = new()
                {
                    new() { Name = "Login frequency", Value = Math.Round(score.Engagement / 4, 0), Max = 30, Unit = "per month" },
                    new() { Name = "Prep lead time", Value = Math.Round(score.Engagement / 8, 0), Max = 14, Unit = "days" },
                    new() { Name = "Draft utilization", Value = Math.Round(score.Engagement / 3, 0), Max = 40, Unit = "drafts" }
                }
            }
        };
    }

    // ── Helpers ──

    public static string RatingLabel(ChsRating rating) => rating switch
    {
        ChsRating.APlus => "A+",
        ChsRating.A => "A",
        ChsRating.B => "B",
        ChsRating.C => "C",
        ChsRating.D => "D",
        _ => "F"
    };

    public static string RatingDescription(ChsRating rating) => rating switch
    {
        ChsRating.APlus => "Exemplary compliance",
        ChsRating.A => "Strong compliance",
        ChsRating.B => "Satisfactory",
        ChsRating.C => "Needs improvement",
        ChsRating.D => "At risk",
        _ => "Critical — regulatory intervention likely"
    };

    public static string TrendArrow(ChsTrend trend) => trend switch
    {
        ChsTrend.Improving => "\u2191",
        ChsTrend.Declining => "\u2193",
        _ => "\u2192"
    };

    internal static ChsRating ToRating(decimal score) => score switch
    {
        >= 90 => ChsRating.APlus,
        >= 80 => ChsRating.A,
        >= 70 => ChsRating.B,
        >= 60 => ChsRating.C,
        >= 50 => ChsRating.D,
        _ => ChsRating.F
    };

    private static decimal Clamp(decimal value) => Math.Max(0, Math.Min(100, Math.Round(value, 1)));

    private static int GetIsoWeek(DateTime date)
    {
        var day = System.Globalization.CultureInfo.InvariantCulture.Calendar
            .GetWeekOfYear(date, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        return day;
    }

    private static (decimal lo, decimal hi) ParseBucket(string label)
    {
        var parts = label.Split('-');
        return (decimal.Parse(parts[0].Trim()), decimal.Parse(parts[1].Trim()));
    }

    private static ChsPillarPeerComparison PillarComp(string name, decimal tenantScore, IEnumerable<decimal> peerScores)
    {
        var sorted = peerScores.OrderBy(s => s).ToList();
        var median = RegulatorAnalyticsSupport.Median(sorted);
        return new ChsPillarPeerComparison
        {
            PillarName = name,
            TenantScore = tenantScore,
            PeerMedian = median,
            Delta = Math.Round(tenantScore - median, 1)
        };
    }
}
