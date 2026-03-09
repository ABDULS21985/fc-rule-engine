using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public class ComplianceHealthService : IComplianceHealthService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<ComplianceHealthService> _logger;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private const decimal FilingWeight = 0.25m;
    private const decimal DataQualityWeight = 0.25m;
    private const decimal CapitalWeight = 0.20m;
    private const decimal GovernanceWeight = 0.15m;
    private const decimal EngagementWeight = 0.15m;

    private static readonly List<SimulatedInstitution> Pool = GeneratePool();

    public ComplianceHealthService(IMemoryCache cache, ILogger<ComplianceHealthService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    // ── Institution-level ──

    public Task<ComplianceHealthScore> GetCurrentScore(Guid tenantId, CancellationToken ct = default)
    {
        var key = $"chs:current:{tenantId}";
        var result = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return ComputeScore(tenantId, DateTime.UtcNow);
        })!;
        return Task.FromResult(result);
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

    public Task<ChsTrendData> GetTrend(Guid tenantId, int periods = 12, CancellationToken ct = default)
    {
        var key = $"chs:trend:{tenantId}:{periods}";
        var result = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return BuildTrend(tenantId, periods);
        })!;
        return Task.FromResult(result);
    }

    public Task<ChsPeerComparison> GetPeerComparison(Guid tenantId, CancellationToken ct = default)
    {
        var key = $"chs:peer:{tenantId}";
        var result = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return BuildPeerComparison(tenantId);
        })!;
        return Task.FromResult(result);
    }

    public Task<List<ChsAlert>> GetAlerts(Guid tenantId, CancellationToken ct = default)
    {
        var trend = BuildTrend(tenantId, 12);
        var alerts = new List<ChsAlert>();
        var inst = Pool.FirstOrDefault(p => p.TenantId == tenantId)
                   ?? new SimulatedInstitution(tenantId, "Your Institution", "Commercial Bank", 72m);

        if (trend.ConsecutiveDeclines >= 3)
        {
            alerts.Add(new ChsAlert
            {
                TenantId = tenantId,
                TenantName = inst.Name,
                AlertType = ChsAlertType.ConsecutiveDecline,
                Message = $"CHS has declined for {trend.ConsecutiveDeclines} consecutive weeks. Immediate attention recommended.",
                CurrentScore = trend.Snapshots.LastOrDefault()?.OverallScore ?? 0,
                PreviousScore = trend.Snapshots.Count >= 2 ? trend.Snapshots[^2].OverallScore : null,
                Severity = "warning",
                TriggeredAt = DateTime.UtcNow
            });
        }

        var currentScore = trend.Snapshots.LastOrDefault()?.OverallScore ?? 0;
        if (currentScore < 60)
        {
            alerts.Add(new ChsAlert
            {
                TenantId = tenantId,
                TenantName = inst.Name,
                AlertType = ChsAlertType.BelowThreshold,
                Message = $"CHS is {currentScore:F1}, below the 60-point regulatory threshold. Regulator has been notified.",
                CurrentScore = currentScore,
                Severity = "critical",
                TriggeredAt = DateTime.UtcNow
            });
        }

        return Task.FromResult(alerts);
    }

    // ── Regulator / Sector-level ──

    public Task<SectorChsSummary> GetSectorSummary(string regulatorCode, CancellationToken ct = default)
    {
        var key = $"chs:sector:{regulatorCode}";
        var result = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return BuildSectorSummary(regulatorCode);
        })!;
        return Task.FromResult(result);
    }

    public Task<List<ChsWatchListItem>> GetWatchList(string regulatorCode, CancellationToken ct = default)
    {
        var key = $"chs:watch:{regulatorCode}";
        var result = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return BuildWatchList();
        })!;
        return Task.FromResult(result);
    }

    public Task<List<ChsHeatmapItem>> GetSectorHeatmap(string regulatorCode, CancellationToken ct = default)
    {
        var key = $"chs:heatmap:{regulatorCode}";
        var result = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return BuildHeatmap();
        })!;
        return Task.FromResult(result);
    }

    // ── Scoring engine ──

    private ComplianceHealthScore ComputeScore(Guid tenantId, DateTime asOf)
    {
        var inst = Pool.FirstOrDefault(p => p.TenantId == tenantId)
                   ?? new SimulatedInstitution(tenantId, "Your Institution", "Commercial Bank", 72m);

        var weekNum = GetIsoWeek(asOf);
        var seed = Math.Abs(inst.TenantId.GetHashCode()) ^ (weekNum * 7919);
        var rng = new Random(seed);

        var filing = Clamp(inst.BaseScore + Jitter(rng, 8));
        var dataQuality = Clamp(inst.BaseScore + Jitter(rng, 10));
        var capital = Clamp(inst.BaseScore + Jitter(rng, 12));
        var governance = Clamp(inst.BaseScore + Jitter(rng, 6));
        var engagement = Clamp(inst.BaseScore + Jitter(rng, 15));

        var overall = Math.Round(
            filing * FilingWeight +
            dataQuality * DataQualityWeight +
            capital * CapitalWeight +
            governance * GovernanceWeight +
            engagement * EngagementWeight, 1);

        var trend = ComputeTrendDirection(tenantId, asOf);

        return new ComplianceHealthScore
        {
            TenantId = tenantId,
            TenantName = inst.Name,
            LicenceType = inst.LicenceType,
            OverallScore = overall,
            Rating = ToRating(overall),
            Trend = trend,
            FilingTimeliness = filing,
            DataQuality = dataQuality,
            RegulatoryCapital = capital,
            AuditGovernance = governance,
            Engagement = engagement,
            ComputedAt = asOf,
            PeriodLabel = $"{asOf.Year}-W{weekNum:D2}"
        };
    }

    private ChsTrend ComputeTrendDirection(Guid tenantId, DateTime asOf)
    {
        var current = ScoreForWeek(tenantId, asOf);
        var prev = ScoreForWeek(tenantId, asOf.AddDays(-7));
        var diff = current - prev;
        return diff > 1.5m ? ChsTrend.Improving : diff < -1.5m ? ChsTrend.Declining : ChsTrend.Stable;
    }

    private decimal ScoreForWeek(Guid tenantId, DateTime asOf)
    {
        var inst = Pool.FirstOrDefault(p => p.TenantId == tenantId)
                   ?? new SimulatedInstitution(tenantId, "Your Institution", "Commercial Bank", 72m);
        var weekNum = GetIsoWeek(asOf);
        var seed = Math.Abs(inst.TenantId.GetHashCode()) ^ (weekNum * 7919);
        var rng = new Random(seed);

        var filing = Clamp(inst.BaseScore + Jitter(rng, 8));
        var dataQuality = Clamp(inst.BaseScore + Jitter(rng, 10));
        var capital = Clamp(inst.BaseScore + Jitter(rng, 12));
        var governance = Clamp(inst.BaseScore + Jitter(rng, 6));
        var engagement = Clamp(inst.BaseScore + Jitter(rng, 15));

        return Math.Round(
            filing * FilingWeight +
            dataQuality * DataQualityWeight +
            capital * CapitalWeight +
            governance * GovernanceWeight +
            engagement * EngagementWeight, 1);
    }

    // ── Trend builder ──

    private ChsTrendData BuildTrend(Guid tenantId, int periods)
    {
        var snapshots = new List<ChsTrendSnapshot>();
        var now = DateTime.UtcNow;

        for (int i = periods - 1; i >= 0; i--)
        {
            var date = now.AddDays(-7 * i);
            var score = ComputeScore(tenantId, date);
            snapshots.Add(new ChsTrendSnapshot
            {
                PeriodLabel = score.PeriodLabel,
                Date = date.Date,
                OverallScore = score.OverallScore,
                Rating = score.Rating,
                FilingTimeliness = score.FilingTimeliness,
                DataQuality = score.DataQuality,
                RegulatoryCapital = score.RegulatoryCapital,
                AuditGovernance = score.AuditGovernance,
                Engagement = score.Engagement
            });
        }

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

        return new ChsTrendData
        {
            TenantId = tenantId,
            Snapshots = snapshots,
            OverallTrend = overallTrend,
            ConsecutiveDeclines = consecutiveDeclines
        };
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
                Description = "Login frequency, return preparation lead time, help article views",
                Factors = new()
                {
                    new() { Name = "Login frequency", Value = Math.Round(score.Engagement / 4, 0), Max = 30, Unit = "per month" },
                    new() { Name = "Prep lead time", Value = Math.Round(score.Engagement / 8, 0), Max = 14, Unit = "days" },
                    new() { Name = "Help article views", Value = Math.Round(score.Engagement / 3, 0), Max = 40, Unit = "views" }
                }
            }
        };
    }

    // ── Peer comparison ──

    private ChsPeerComparison BuildPeerComparison(Guid tenantId)
    {
        var inst = Pool.FirstOrDefault(p => p.TenantId == tenantId)
                   ?? new SimulatedInstitution(tenantId, "Your Institution", "Commercial Bank", 72m);

        var peers = Pool.Where(p => p.LicenceType == inst.LicenceType).ToList();
        var tenantScore = ComputeScore(tenantId, DateTime.UtcNow);
        var peerScores = peers.Select(p => ComputeScore(p.TenantId, DateTime.UtcNow)).ToList();
        var sorted = peerScores.OrderBy(s => s.OverallScore).ToList();

        var percentile = sorted.Count > 0
            ? (int)Math.Round(100.0 * sorted.Count(s => s.OverallScore <= tenantScore.OverallScore) / sorted.Count)
            : 50;

        var buckets = new[] { "0–49", "50–59", "60–69", "70–79", "80–89", "90–100" };
        var distribution = buckets.Select(label =>
        {
            var (lo, hi) = ParseBucket(label);
            var count = peerScores.Count(s => s.OverallScore >= lo && s.OverallScore <= hi);
            return new ChsDistributionBucket
            {
                Label = label,
                Count = count,
                ContainsTenant = tenantScore.OverallScore >= lo && tenantScore.OverallScore <= hi
            };
        }).ToList();

        var medianScore = sorted.Count > 0 ? sorted[sorted.Count / 2].OverallScore : 0;
        var p25Score = sorted.Count > 3 ? sorted[sorted.Count / 4].OverallScore : medianScore;
        var p75Score = sorted.Count > 3 ? sorted[3 * sorted.Count / 4].OverallScore : medianScore;

        var pillarComparisons = new List<ChsPillarPeerComparison>
        {
            PillarComp("Filing Timeliness", tenantScore.FilingTimeliness, peerScores.Select(s => s.FilingTimeliness)),
            PillarComp("Data Quality", tenantScore.DataQuality, peerScores.Select(s => s.DataQuality)),
            PillarComp("Regulatory Capital", tenantScore.RegulatoryCapital, peerScores.Select(s => s.RegulatoryCapital)),
            PillarComp("Audit & Governance", tenantScore.AuditGovernance, peerScores.Select(s => s.AuditGovernance)),
            PillarComp("Engagement", tenantScore.Engagement, peerScores.Select(s => s.Engagement))
        };

        return new ChsPeerComparison
        {
            TenantId = tenantId,
            LicenceType = inst.LicenceType,
            PeerCount = peers.Count,
            TenantScore = tenantScore.OverallScore,
            PeerMedian = medianScore,
            PeerP25 = p25Score,
            PeerP75 = p75Score,
            Percentile = percentile,
            Distribution = distribution,
            PillarComparisons = pillarComparisons
        };
    }

    // ── Sector builders ──

    private SectorChsSummary BuildSectorSummary(string regulatorCode)
    {
        var allScores = Pool.Select(p => ComputeScore(p.TenantId, DateTime.UtcNow)).ToList();
        var sorted = allScores.OrderBy(s => s.OverallScore).ToList();

        var ratingDist = allScores.GroupBy(s => s.Rating)
            .ToDictionary(g => g.Key, g => g.Count());

        // Sector trend: average score per week for last 12 weeks
        var sectorTrend = new List<ChsTrendSnapshot>();
        var now = DateTime.UtcNow;
        for (int i = 11; i >= 0; i--)
        {
            var date = now.AddDays(-7 * i);
            var weekScores = Pool.Select(p => ComputeScore(p.TenantId, date)).ToList();
            var avg = weekScores.Average(s => s.OverallScore);
            sectorTrend.Add(new ChsTrendSnapshot
            {
                PeriodLabel = $"{date.Year}-W{GetIsoWeek(date):D2}",
                Date = date.Date,
                OverallScore = Math.Round(avg, 1),
                Rating = ToRating(avg)
            });
        }

        return new SectorChsSummary
        {
            RegulatorCode = regulatorCode,
            SectorAverage = Math.Round(allScores.Average(s => s.OverallScore), 1),
            SectorMedian = sorted.Count > 0 ? sorted[sorted.Count / 2].OverallScore : 0,
            TotalInstitutions = allScores.Count,
            RatingDistribution = ratingDist,
            SectorTrend = sectorTrend
        };
    }

    private List<ChsWatchListItem> BuildWatchList()
    {
        var watchItems = new List<ChsWatchListItem>();

        foreach (var inst in Pool)
        {
            var score = ComputeScore(inst.TenantId, DateTime.UtcNow);
            var trend = BuildTrend(inst.TenantId, 6);
            var isBelowThreshold = score.OverallScore < 60;
            var isConsecutiveDecline = trend.ConsecutiveDeclines >= 3;

            if (!isBelowThreshold && !isConsecutiveDecline) continue;

            var reason = isBelowThreshold && isConsecutiveDecline
                ? "Below 60 & 3+ consecutive declines"
                : isBelowThreshold
                    ? "CHS below 60"
                    : "3+ consecutive declines";

            var scoreChange = trend.Snapshots.Count >= 5
                ? score.OverallScore - trend.Snapshots[^5].OverallScore
                : 0;

            watchItems.Add(new ChsWatchListItem
            {
                TenantId = inst.TenantId,
                InstitutionName = inst.Name,
                LicenceType = inst.LicenceType,
                CurrentScore = score.OverallScore,
                Rating = score.Rating,
                Trend = score.Trend,
                ConsecutiveDeclines = trend.ConsecutiveDeclines,
                ScoreChange = Math.Round(scoreChange, 1),
                WatchReason = reason,
                RecentAlerts = new()
            });
        }

        return watchItems.OrderBy(w => w.CurrentScore).ToList();
    }

    private List<ChsHeatmapItem> BuildHeatmap()
    {
        return Pool.Select(inst =>
        {
            var score = ComputeScore(inst.TenantId, DateTime.UtcNow);
            return new ChsHeatmapItem
            {
                TenantId = inst.TenantId,
                InstitutionName = inst.Name,
                LicenceType = inst.LicenceType,
                OverallScore = score.OverallScore,
                Rating = score.Rating,
                FilingTimeliness = score.FilingTimeliness,
                DataQuality = score.DataQuality,
                RegulatoryCapital = score.RegulatoryCapital,
                AuditGovernance = score.AuditGovernance,
                Engagement = score.Engagement
            };
        })
        .OrderByDescending(h => h.OverallScore)
        .ToList();
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

    private static ChsRating ToRating(decimal score) => score switch
    {
        >= 90 => ChsRating.APlus,
        >= 80 => ChsRating.A,
        >= 70 => ChsRating.B,
        >= 60 => ChsRating.C,
        >= 50 => ChsRating.D,
        _ => ChsRating.F
    };

    private static decimal Clamp(decimal value) => Math.Max(0, Math.Min(100, Math.Round(value, 1)));

    private static decimal Jitter(Random rng, int range)
        => (decimal)(rng.NextDouble() * range * 2 - range);

    private static int GetIsoWeek(DateTime date)
    {
        var day = System.Globalization.CultureInfo.InvariantCulture.Calendar
            .GetWeekOfYear(date, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        return day;
    }

    private static (decimal lo, decimal hi) ParseBucket(string label)
    {
        var cleaned = label.Replace("\u2013", "-").Replace("–", "-");
        var parts = cleaned.Split('-');
        return (decimal.Parse(parts[0].Trim()), decimal.Parse(parts[1].Trim()));
    }

    private static ChsPillarPeerComparison PillarComp(string name, decimal tenantScore, IEnumerable<decimal> peerScores)
    {
        var sorted = peerScores.OrderBy(s => s).ToList();
        var median = sorted.Count > 0 ? sorted[sorted.Count / 2] : 0;
        return new ChsPillarPeerComparison
        {
            PillarName = name,
            TenantScore = tenantScore,
            PeerMedian = median,
            Delta = Math.Round(tenantScore - median, 1)
        };
    }

    // ── Simulated Institution Pool ──

    private static List<SimulatedInstitution> GeneratePool()
    {
        return new List<SimulatedInstitution>
        {
            // Commercial Banks
            new(Guid.Parse("a1000001-0000-0000-0000-000000000001"), "First National Bank", "Commercial Bank", 88m),
            new(Guid.Parse("a1000001-0000-0000-0000-000000000002"), "Union Bank of Nigeria", "Commercial Bank", 75m),
            new(Guid.Parse("a1000001-0000-0000-0000-000000000003"), "Zenith Commercial Bank", "Commercial Bank", 92m),
            new(Guid.Parse("a1000001-0000-0000-0000-000000000004"), "Access Bank PLC", "Commercial Bank", 68m),
            new(Guid.Parse("a1000001-0000-0000-0000-000000000005"), "Sterling Finance Bank", "Commercial Bank", 55m),

            // Microfinance Banks
            new(Guid.Parse("b2000001-0000-0000-0000-000000000001"), "LAPO Microfinance", "Microfinance", 82m),
            new(Guid.Parse("b2000001-0000-0000-0000-000000000002"), "AB Microfinance", "Microfinance", 65m),
            new(Guid.Parse("b2000001-0000-0000-0000-000000000003"), "Accion Microfinance", "Microfinance", 45m),
            new(Guid.Parse("b2000001-0000-0000-0000-000000000004"), "Mutual Benefits MFB", "Microfinance", 78m),

            // Insurance Companies
            new(Guid.Parse("c3000001-0000-0000-0000-000000000001"), "Leadway Assurance", "Insurance", 85m),
            new(Guid.Parse("c3000001-0000-0000-0000-000000000002"), "AIICO Insurance", "Insurance", 72m),
            new(Guid.Parse("c3000001-0000-0000-0000-000000000003"), "Cornerstone Insurance", "Insurance", 60m),
            new(Guid.Parse("c3000001-0000-0000-0000-000000000004"), "NEM Insurance", "Insurance", 91m),

            // Securities Firms
            new(Guid.Parse("d4000001-0000-0000-0000-000000000001"), "Stanbic IBTC Securities", "Securities", 80m),
            new(Guid.Parse("d4000001-0000-0000-0000-000000000002"), "Chapel Hill Denham", "Securities", 58m),
            new(Guid.Parse("d4000001-0000-0000-0000-000000000003"), "Afrinvest Securities", "Securities", 74m),
            new(Guid.Parse("d4000001-0000-0000-0000-000000000004"), "CardinalStone Partners", "Securities", 85m),

            // Pension Administrators
            new(Guid.Parse("e5000001-0000-0000-0000-000000000001"), "Stanbic IBTC Pension", "Pension", 90m),
            new(Guid.Parse("e5000001-0000-0000-0000-000000000002"), "ARM Pension Managers", "Pension", 76m),
            new(Guid.Parse("e5000001-0000-0000-0000-000000000003"), "Trustfund Pensions", "Pension", 67m)
        };
    }

    private record SimulatedInstitution(Guid TenantId, string Name, string LicenceType, decimal BaseScore);
}
