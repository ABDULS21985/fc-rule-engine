using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public class SystemicRiskService : ISystemicRiskService
{
    private static readonly string[] CarKeys = { "car", "carratio", "capitaladequacyratio", "capitalratio", "capitaladequacy" };
    private static readonly string[] NplKeys = { "npl", "nplratio", "nonperformingloanratio", "nonperformingloansratio" };
    private static readonly string[] LcrKeys = { "lcr", "liquiditycoverageratio", "liquiditycoverage" };
    private static readonly string[] NsfrKeys = { "nsfr", "netstablefundingratio", "netstablefunding" };
    private static readonly string[] RoaKeys = { "roa", "returnonassets", "returnonasset" };
    private static readonly string[] RoeKeys = { "roe", "returnonequity" };
    private static readonly string[] TotalAssetsKeys = { "totalassets", "totalasset", "assets" };
    private static readonly string[] FxExposureKeys = { "fxexposure", "foreignexchangeexposure", "fxposition", "netfxposition" };
    private static readonly string[] Tier1Keys = { "tier1", "tier1ratio", "tier1capital" };
    private static readonly string[] ProvisionKeys = { "provisioncoverage", "provisioningratio", "provisioncoverage" };

    private readonly MetadataDbContext _db;
    private readonly IEarlyWarningService _earlyWarning;
    private readonly ILogger<SystemicRiskService> _logger;

    public SystemicRiskService(MetadataDbContext db, IEarlyWarningService earlyWarning, ILogger<SystemicRiskService> logger)
    {
        _db = db;
        _earlyWarning = earlyWarning;
        _logger = logger;
    }

    public async Task<SystemicRiskDashboard> GetDashboard(string regulatorCode, CancellationToken ct = default)
    {
        var scores = await ComputeCamelsScores(regulatorCode, ct);
        var flags = await _earlyWarning.ComputeFlags(regulatorCode, ct);
        var systemicIndicators = await ComputeSystemicIndicators(regulatorCode, ct);
        var contagion = await AnalyzeContagion(regulatorCode, ct);

        var flagsByInstitution = flags.GroupBy(f => f.InstitutionId).ToDictionary(g => g.Key, g => g.Count());
        foreach (var s in scores)
        {
            s.ActiveFlags = flagsByInstitution.GetValueOrDefault(s.InstitutionId);
        }

        var heatmap = scores.Select(s => new HeatmapEntity
        {
            InstitutionId = s.InstitutionId,
            InstitutionName = s.InstitutionName,
            LicenceType = s.LicenceType,
            RiskScore = s.Composite,
            Size = s.TotalAssets,
            Rating = s.Rating,
            FlagCount = s.ActiveFlags
        }).ToList();

        var pendingActions = flags
            .Where(f => f.Severity == EarlyWarningSeverity.Red)
            .Select(f => new SupervisoryAction
            {
                InstitutionId = f.InstitutionId,
                InstitutionName = f.InstitutionName,
                TriggerFlag = f.FlagCode,
                ActionType = MapFlagToAction(f.FlagCode),
                EscalationLevel = MapSeverityToEscalation(f.Severity),
                Status = "Pending",
                LetterTemplate = GenerateLetterTemplate(f),
                CreatedAt = f.TriggeredAt,
                DueDate = f.TriggeredAt.AddDays(14)
            })
            .ToList();

        var summary = new SystemicRiskSummary
        {
            TotalEntities = scores.Count,
            GreenCount = scores.Count(s => s.Rating == RiskRating.Green),
            AmberCount = scores.Count(s => s.Rating == RiskRating.Amber),
            RedCount = scores.Count(s => s.Rating == RiskRating.Red),
            SectorAverageCar = scores.Count > 0 ? Math.Round(scores.Average(s => s.Capital * 30m), 2) : 0,
            SectorAverageNpl = scores.Count > 0 ? Math.Round(scores.Average(s => (5m - s.AssetQuality) * 2m), 2) : 0,
            TotalActiveFlags = flags.Count,
            SystemicRiskIndex = ComputeSystemicRiskIndex(scores, systemicIndicators)
        };

        return new SystemicRiskDashboard
        {
            Summary = summary,
            Scores = scores,
            HeatmapData = heatmap,
            InstitutionalFlags = flags,
            SystemicIndicators = systemicIndicators,
            Contagion = contagion,
            PendingActions = pendingActions
        };
    }

    public async Task<List<CamelsScore>> ComputeCamelsScores(string regulatorCode, CancellationToken ct = default)
    {
        var rows = await BuildScopedSubmissionQuery(regulatorCode)
            .Select(s => new
            {
                s.Id,
                s.InstitutionId,
                InstitutionName = s.Institution != null ? s.Institution.InstitutionName : "Unknown",
                LicenceType = s.Institution != null ? s.Institution.LicenseType : "Unknown",
                s.ParsedDataJson,
                Year = s.ReturnPeriod != null ? s.ReturnPeriod.Year : 0,
                Quarter = s.ReturnPeriod != null ? s.ReturnPeriod.Quarter : null,
                Month = s.ReturnPeriod != null ? s.ReturnPeriod.Month : 1
            })
            .ToListAsync(ct);

        // Group by institution and take latest submission per institution
        var byInstitution = rows
            .GroupBy(x => new { x.InstitutionId, x.InstitutionName, x.LicenceType })
            .ToList();

        var submissionIds = rows.Select(x => x.Id).ToList();

        // Timeliness data
        var slaRecords = await _db.FilingSlaRecords
            .AsNoTracking()
            .Where(x => x.SubmissionId.HasValue && submissionIds.Contains(x.SubmissionId.Value))
            .ToListAsync(ct);
        var slaBySub = slaRecords
            .Where(x => x.SubmissionId.HasValue)
            .GroupBy(x => x.SubmissionId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Validation data
        var valReports = await _db.ValidationReports
            .AsNoTracking()
            .Include(r => r.Errors)
            .Where(r => submissionIds.Contains(r.SubmissionId))
            .ToListAsync(ct);
        var valBySub = valReports.ToDictionary(r => r.SubmissionId);

        var scores = new List<CamelsScore>();

        foreach (var inst in byInstitution)
        {
            var latest = inst
                .OrderByDescending(x => x.Year)
                .ThenByDescending(x => RegulatorAnalyticsSupport.ResolveQuarter(x.Month, x.Quarter))
                .First();

            var car = RegulatorAnalyticsSupport.ExtractFirstMetric(latest.ParsedDataJson, CarKeys);
            var npl = RegulatorAnalyticsSupport.ExtractFirstMetric(latest.ParsedDataJson, NplKeys);
            var lcr = RegulatorAnalyticsSupport.ExtractFirstMetric(latest.ParsedDataJson, LcrKeys);
            var nsfr = RegulatorAnalyticsSupport.ExtractFirstMetric(latest.ParsedDataJson, NsfrKeys);
            var roa = RegulatorAnalyticsSupport.ExtractFirstMetric(latest.ParsedDataJson, RoaKeys);
            var roe = RegulatorAnalyticsSupport.ExtractFirstMetric(latest.ParsedDataJson, RoeKeys);
            var totalAssets = RegulatorAnalyticsSupport.ExtractFirstMetric(latest.ParsedDataJson, TotalAssetsKeys);
            var fxExposure = RegulatorAnalyticsSupport.ExtractFirstMetric(latest.ParsedDataJson, FxExposureKeys);
            var tier1 = RegulatorAnalyticsSupport.ExtractFirstMetric(latest.ParsedDataJson, Tier1Keys);
            var provision = RegulatorAnalyticsSupport.ExtractFirstMetric(latest.ParsedDataJson, ProvisionKeys);

            // C - Capital Adequacy (1-5, 5=best)
            var capitalScore = ScoreCapital(car ?? 0, tier1 ?? 0);

            // A - Asset Quality (1-5, 5=best)
            var assetScore = ScoreAssetQuality(npl ?? 0, provision ?? 0);

            // M - Management (governance proxy: timeliness + data quality)
            var instSubIds = inst.Select(x => x.Id).ToList();
            var managementScore = ScoreManagement(instSubIds, slaBySub, valBySub);

            // E - Earnings
            var earningsScore = ScoreEarnings(roa ?? 0, roe ?? 0);

            // L - Liquidity
            var liquidityScore = ScoreLiquidity(lcr ?? 0, nsfr ?? 0);

            // S - Sensitivity to Market Risk
            var sensitivityScore = ScoreSensitivity(fxExposure ?? 0, car ?? 0);

            var composite = Math.Round(
                (capitalScore * 0.20m) +
                (assetScore * 0.20m) +
                (managementScore * 0.15m) +
                (earningsScore * 0.15m) +
                (liquidityScore * 0.20m) +
                (sensitivityScore * 0.10m), 2);

            var rating = composite >= 3.5m ? RiskRating.Green
                       : composite >= 2.0m ? RiskRating.Amber
                       : RiskRating.Red;

            scores.Add(new CamelsScore
            {
                InstitutionId = inst.Key.InstitutionId,
                InstitutionName = inst.Key.InstitutionName,
                LicenceType = inst.Key.LicenceType ?? "Unknown",
                Capital = Math.Round(capitalScore, 2),
                AssetQuality = Math.Round(assetScore, 2),
                Management = Math.Round(managementScore, 2),
                Earnings = Math.Round(earningsScore, 2),
                Liquidity = Math.Round(liquidityScore, 2),
                Sensitivity = Math.Round(sensitivityScore, 2),
                Composite = composite,
                Rating = rating,
                TotalAssets = totalAssets ?? 0
            });
        }

        return scores.OrderBy(s => s.Composite).ToList();
    }

    public async Task<List<SystemicEwi>> ComputeSystemicIndicators(string regulatorCode, CancellationToken ct = default)
    {
        var rows = await BuildScopedSubmissionQuery(regulatorCode)
            .Select(s => new
            {
                s.InstitutionId,
                s.ParsedDataJson,
                Year = s.ReturnPeriod != null ? s.ReturnPeriod.Year : 0,
                Quarter = s.ReturnPeriod != null ? s.ReturnPeriod.Quarter : null,
                Month = s.ReturnPeriod != null ? s.ReturnPeriod.Month : 1
            })
            .ToListAsync(ct);

        var latestByInstitution = rows
            .GroupBy(x => x.InstitutionId)
            .Select(g => g.OrderByDescending(x => x.Year)
                          .ThenByDescending(x => RegulatorAnalyticsSupport.ResolveQuarter(x.Month, x.Quarter))
                          .First())
            .ToList();

        var indicators = new List<SystemicEwi>();
        var entityCount = latestByInstitution.Count;
        if (entityCount == 0) return indicators;

        // 1. Sector average CAR trending toward minimum
        var carValues = latestByInstitution
            .Select(x => RegulatorAnalyticsSupport.ExtractFirstMetric(x.ParsedDataJson, CarKeys))
            .Where(x => x.HasValue).Select(x => x!.Value).ToList();
        if (carValues.Count > 0)
        {
            var avgCar = carValues.Average();
            var belowMinCount = carValues.Count(v => v < 10m);
            if (avgCar < 15m)
            {
                indicators.Add(new SystemicEwi
                {
                    IndicatorCode = "SECTOR_CAR_DECLINING",
                    Title = "Sector Capital Adequacy Under Pressure",
                    Description = $"Sector average CAR is {avgCar:0.##}%, approaching the 10% minimum. {belowMinCount} entities below minimum.",
                    Severity = avgCar < 12m ? RiskRating.Red : RiskRating.Amber,
                    CurrentValue = Math.Round(avgCar, 2),
                    Threshold = 15m,
                    AffectedEntities = belowMinCount
                });
            }
        }

        // 2. Aggregate NPL rising across multiple institution types
        var nplValues = latestByInstitution
            .Select(x => RegulatorAnalyticsSupport.ExtractFirstMetric(x.ParsedDataJson, NplKeys))
            .Where(x => x.HasValue).Select(x => x!.Value).ToList();
        if (nplValues.Count > 0)
        {
            var avgNpl = nplValues.Average();
            var highNplCount = nplValues.Count(v => v > 5m);
            if (highNplCount > entityCount * 0.2m)
            {
                indicators.Add(new SystemicEwi
                {
                    IndicatorCode = "SECTOR_NPL_RISING",
                    Title = "Widespread Asset Quality Deterioration",
                    Description = $"{highNplCount} of {entityCount} entities have NPL ratio above 5%. Sector average: {avgNpl:0.##}%.",
                    Severity = highNplCount > entityCount * 0.4m ? RiskRating.Red : RiskRating.Amber,
                    CurrentValue = Math.Round(avgNpl, 2),
                    Threshold = 5m,
                    AffectedEntities = highNplCount
                });
            }
        }

        // 3. Liquidity stress: multiple entities breaching LCR
        var lcrValues = latestByInstitution
            .Select(x => RegulatorAnalyticsSupport.ExtractFirstMetric(x.ParsedDataJson, LcrKeys))
            .Where(x => x.HasValue).Select(x => x!.Value).ToList();
        if (lcrValues.Count > 0)
        {
            var belowLcr = lcrValues.Count(v => v < 100m);
            var nearLcr = lcrValues.Count(v => v >= 100m && v < 110m);
            if (belowLcr + nearLcr > entityCount * 0.15m)
            {
                indicators.Add(new SystemicEwi
                {
                    IndicatorCode = "LIQUIDITY_STRESS",
                    Title = "Sector Liquidity Stress",
                    Description = $"{belowLcr} entities below LCR minimum (100%), {nearLcr} approaching. Potential sector-wide liquidity crunch.",
                    Severity = belowLcr > 2 ? RiskRating.Red : RiskRating.Amber,
                    CurrentValue = lcrValues.Count > 0 ? Math.Round(lcrValues.Average(), 2) : 0,
                    Threshold = 100m,
                    AffectedEntities = belowLcr + nearLcr
                });
            }
        }

        // 4. FX exposure buildup
        var fxValues = latestByInstitution
            .Select(x => RegulatorAnalyticsSupport.ExtractFirstMetric(x.ParsedDataJson, FxExposureKeys))
            .Where(x => x.HasValue).Select(x => x!.Value).ToList();
        if (fxValues.Count > 0)
        {
            var highFx = fxValues.Count(v => v > 20m);
            if (highFx > entityCount * 0.25m)
            {
                indicators.Add(new SystemicEwi
                {
                    IndicatorCode = "FX_EXPOSURE_BUILDUP",
                    Title = "FX Exposure Concentration",
                    Description = $"{highFx} entities with FX exposure exceeding 20% of capital. Currency risk is elevated.",
                    Severity = highFx > entityCount * 0.4m ? RiskRating.Red : RiskRating.Amber,
                    CurrentValue = Math.Round(fxValues.Average(), 2),
                    Threshold = 20m,
                    AffectedEntities = highFx
                });
            }
        }

        // 5. Sector-wide deposit concentration risk
        var depConcentrationKeys = new[] { "top20depositorshare", "top20depositorconcentration", "depositconcentration", "largedepositorshare" };
        var depConcentrationValues = latestByInstitution
            .Select(x => RegulatorAnalyticsSupport.ExtractFirstMetric(x.ParsedDataJson, depConcentrationKeys))
            .Where(x => x.HasValue).Select(x => x!.Value).ToList();
        if (depConcentrationValues.Count > 0)
        {
            var highConcentration = depConcentrationValues.Count(v => v > 30m);
            if (highConcentration > entityCount * 0.2m)
            {
                indicators.Add(new SystemicEwi
                {
                    IndicatorCode = "DEPOSIT_CONCENTRATION_RISK",
                    Title = "Sector-Wide Deposit Concentration",
                    Description = $"{highConcentration} entities have top-20 depositor share exceeding 30%. Systemic deposit flight risk if large depositors withdraw simultaneously.",
                    Severity = highConcentration > entityCount * 0.35m ? RiskRating.Red : RiskRating.Amber,
                    CurrentValue = Math.Round(depConcentrationValues.Average(), 2),
                    Threshold = 30m,
                    AffectedEntities = highConcentration
                });
            }
        }

        // 6. Sector-wide asset growth surge (multiple entities growing rapidly)
        var totalAssetsValues = latestByInstitution
            .Select(x => RegulatorAnalyticsSupport.ExtractFirstMetric(x.ParsedDataJson, TotalAssetsKeys))
            .Where(x => x.HasValue).Select(x => x!.Value).ToList();

        // Compare with previous period for growth detection
        var previousByInstitution = rows
            .GroupBy(x => x.InstitutionId)
            .Select(g =>
            {
                var ordered = g.OrderByDescending(x => x.Year)
                               .ThenByDescending(x => RegulatorAnalyticsSupport.ResolveQuarter(x.Month, x.Quarter))
                               .ToList();
                return ordered.Count >= 2 ? new { Id = g.Key, Latest = ordered[0], Previous = ordered[1] } : null;
            })
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();

        var rapidGrowthCount = 0;
        foreach (var pair in previousByInstitution)
        {
            var latestAssets = RegulatorAnalyticsSupport.ExtractFirstMetric(pair.Latest.ParsedDataJson, TotalAssetsKeys) ?? 0;
            var previousAssets = RegulatorAnalyticsSupport.ExtractFirstMetric(pair.Previous.ParsedDataJson, TotalAssetsKeys) ?? 0;
            if (previousAssets > 0 && (latestAssets - previousAssets) / previousAssets > 0.3m)
            {
                rapidGrowthCount++;
            }
        }

        if (rapidGrowthCount > 2)
        {
            indicators.Add(new SystemicEwi
            {
                IndicatorCode = "SECTOR_RAPID_GROWTH",
                Title = "Multiple Entities with Rapid Asset Growth",
                Description = $"{rapidGrowthCount} entities show >30% QoQ asset growth. May indicate credit bubble or unsustainable expansion sector-wide.",
                Severity = rapidGrowthCount > entityCount * 0.2m ? RiskRating.Red : RiskRating.Amber,
                CurrentValue = rapidGrowthCount,
                Threshold = 2m,
                AffectedEntities = rapidGrowthCount
            });
        }

        // 7. Filing compliance decline — multiple late filers
        var slaRecords = await _db.FilingSlaRecords
            .AsNoTracking()
            .Include(x => x.Module)
            .Where(x => x.Module != null && x.Module.RegulatorCode == regulatorCode)
            .OrderByDescending(x => x.PeriodEndDate)
            .Take(500)
            .ToListAsync(ct);

        var lateFilingCount = slaRecords.Count(x => x.OnTime == false);
        var totalSla = slaRecords.Count;
        if (totalSla > 0)
        {
            var lateRate = 100m * lateFilingCount / totalSla;
            if (lateRate > 25m)
            {
                indicators.Add(new SystemicEwi
                {
                    IndicatorCode = "FILING_COMPLIANCE_DECLINE",
                    Title = "Filing Compliance Declining",
                    Description = $"{lateRate:0.#}% of recent filings are late. Regulatory compliance is deteriorating across the sector.",
                    Severity = lateRate > 40m ? RiskRating.Red : RiskRating.Amber,
                    CurrentValue = Math.Round(lateRate, 2),
                    Threshold = 25m,
                    AffectedEntities = lateFilingCount
                });
            }
        }

        return indicators.OrderByDescending(i => i.Severity).ToList();
    }

    public async Task<ContagionAnalysis> AnalyzeContagion(string regulatorCode, CancellationToken ct = default)
    {
        var rows = await BuildScopedSubmissionQuery(regulatorCode)
            .Select(s => new
            {
                s.InstitutionId,
                InstitutionName = s.Institution != null ? s.Institution.InstitutionName : "Unknown",
                s.ParsedDataJson,
                Year = s.ReturnPeriod != null ? s.ReturnPeriod.Year : 0,
                Quarter = s.ReturnPeriod != null ? s.ReturnPeriod.Quarter : null,
                Month = s.ReturnPeriod != null ? s.ReturnPeriod.Month : 1
            })
            .ToListAsync(ct);

        // Build time-series of CAR per institution per period
        var byInstitution = rows
            .GroupBy(x => new { x.InstitutionId, x.InstitutionName })
            .Where(g => g.Count() >= 2)
            .Select(g => new
            {
                g.Key.InstitutionId,
                g.Key.InstitutionName,
                Series = g.OrderBy(x => x.Year)
                          .ThenBy(x => RegulatorAnalyticsSupport.ResolveQuarter(x.Month, x.Quarter))
                          .Select(x => RegulatorAnalyticsSupport.ExtractFirstMetric(x.ParsedDataJson, CarKeys) ?? 0)
                          .ToList()
            })
            .ToList();

        var links = new List<ContagionLink>();
        var clusters = new List<string>();

        // Compute pairwise correlation of CAR time-series
        for (int i = 0; i < byInstitution.Count; i++)
        {
            for (int j = i + 1; j < byInstitution.Count; j++)
            {
                var a = byInstitution[i];
                var b = byInstitution[j];
                var minLen = Math.Min(a.Series.Count, b.Series.Count);
                if (minLen < 2) continue;

                var corr = ComputeCorrelation(
                    a.Series.TakeLast(minLen).ToList(),
                    b.Series.TakeLast(minLen).ToList());

                if (corr > 0.7m)
                {
                    links.Add(new ContagionLink
                    {
                        SourceId = a.InstitutionId,
                        SourceName = a.InstitutionName,
                        TargetId = b.InstitutionId,
                        TargetName = b.InstitutionName,
                        CorrelationStrength = Math.Round(corr, 3)
                    });
                }
            }
        }

        // Simple cluster detection: connected components of high-correlation pairs
        var visited = new HashSet<int>();
        foreach (var link in links.Where(l => l.CorrelationStrength > 0.85m))
        {
            if (!visited.Contains(link.SourceId) || !visited.Contains(link.TargetId))
            {
                var clusterMembers = new HashSet<string> { link.SourceName, link.TargetName };
                visited.Add(link.SourceId);
                visited.Add(link.TargetId);

                // Expand cluster
                foreach (var other in links.Where(l => l.CorrelationStrength > 0.85m))
                {
                    if (clusterMembers.Contains(other.SourceName) || clusterMembers.Contains(other.TargetName))
                    {
                        clusterMembers.Add(other.SourceName);
                        clusterMembers.Add(other.TargetName);
                        visited.Add(other.SourceId);
                        visited.Add(other.TargetId);
                    }
                }

                if (clusterMembers.Count >= 2)
                {
                    clusters.Add(string.Join(", ", clusterMembers.OrderBy(x => x)));
                }
            }
        }

        return new ContagionAnalysis
        {
            Links = links.OrderByDescending(l => l.CorrelationStrength).Take(20).ToList(),
            HighRiskClusters = clusters.Distinct().ToList(),
            ClusterCount = clusters.Distinct().Count()
        };
    }

    public Task<SupervisoryAction> GenerateSupervisoryAction(string regulatorCode, int institutionId, string flagCode, CancellationToken ct = default)
    {
        var action = new SupervisoryAction
        {
            InstitutionId = institutionId,
            TriggerFlag = flagCode,
            ActionType = MapFlagToAction(flagCode),
            EscalationLevel = "Senior Examiner",
            Status = "Pending",
            LetterTemplate = GenerateLetterTemplate(new EarlyWarningFlag
            {
                InstitutionId = institutionId,
                FlagCode = flagCode,
                Message = $"Supervisory action triggered for flag: {flagCode}",
                Severity = EarlyWarningSeverity.Red
            }),
            CreatedAt = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(14)
        };

        return Task.FromResult(action);
    }

    // ── CAMELS Scoring Helpers ──────────────────────────────────────

    private static decimal ScoreCapital(decimal car, decimal tier1)
    {
        // CAR: >15% = 5, 12-15% = 4, 10-12% = 3, 8-10% = 2, <8% = 1
        var carScore = car >= 15m ? 5m : car >= 12m ? 4m : car >= 10m ? 3m : car >= 8m ? 2m : 1m;
        if (tier1 > 0)
        {
            var t1Score = tier1 >= 10m ? 5m : tier1 >= 8m ? 4m : tier1 >= 6m ? 3m : tier1 >= 4m ? 2m : 1m;
            return (carScore * 0.7m) + (t1Score * 0.3m);
        }
        return carScore;
    }

    private static decimal ScoreAssetQuality(decimal npl, decimal provision)
    {
        // NPL: <2% = 5, 2-5% = 4, 5-8% = 3, 8-12% = 2, >12% = 1
        var nplScore = npl < 2m ? 5m : npl < 5m ? 4m : npl < 8m ? 3m : npl < 12m ? 2m : 1m;
        if (provision > 0)
        {
            var provScore = provision >= 100m ? 5m : provision >= 80m ? 4m : provision >= 60m ? 3m : provision >= 40m ? 2m : 1m;
            return (nplScore * 0.7m) + (provScore * 0.3m);
        }
        return nplScore;
    }

    private static decimal ScoreManagement(List<int> submissionIds, Dictionary<int, List<FilingSlaRecord>> slaBySub, Dictionary<int, ValidationReport> valBySub)
    {
        // Management score = weighted average of timeliness + data quality
        var onTimeCount = 0;
        var totalSla = 0;
        foreach (var sid in submissionIds)
        {
            if (slaBySub.TryGetValue(sid, out var records))
            {
                foreach (var r in records)
                {
                    if (r.OnTime.HasValue)
                    {
                        totalSla++;
                        if (r.OnTime.Value) onTimeCount++;
                    }
                }
            }
        }
        var timelinessRate = totalSla > 0 ? 100m * onTimeCount / totalSla : 50m;
        var timelinessScore = timelinessRate >= 95m ? 5m : timelinessRate >= 85m ? 4m : timelinessRate >= 70m ? 3m : timelinessRate >= 50m ? 2m : 1m;

        var totalErrors = 0;
        var totalWarnings = 0;
        var reportCount = 0;
        foreach (var sid in submissionIds)
        {
            if (valBySub.TryGetValue(sid, out var report))
            {
                reportCount++;
                totalErrors += report.Errors.Count(e => e.Severity == ValidationSeverity.Error);
                totalWarnings += report.Errors.Count(e => e.Severity == ValidationSeverity.Warning);
            }
        }
        var avgIssues = reportCount > 0 ? (decimal)(totalErrors + totalWarnings) / reportCount : 0;
        var qualityScore = avgIssues < 1m ? 5m : avgIssues < 3m ? 4m : avgIssues < 6m ? 3m : avgIssues < 10m ? 2m : 1m;

        return (timelinessScore * 0.5m) + (qualityScore * 0.5m);
    }

    private static decimal ScoreEarnings(decimal roa, decimal roe)
    {
        if (roa == 0 && roe == 0) return 3m; // Neutral if no data
        var roaScore = roa >= 2m ? 5m : roa >= 1m ? 4m : roa >= 0.5m ? 3m : roa >= 0m ? 2m : 1m;
        if (roe > 0)
        {
            var roeScore = roe >= 15m ? 5m : roe >= 10m ? 4m : roe >= 5m ? 3m : roe >= 0m ? 2m : 1m;
            return (roaScore * 0.5m) + (roeScore * 0.5m);
        }
        return roaScore;
    }

    private static decimal ScoreLiquidity(decimal lcr, decimal nsfr)
    {
        if (lcr == 0 && nsfr == 0) return 3m;
        var lcrScore = lcr >= 150m ? 5m : lcr >= 120m ? 4m : lcr >= 100m ? 3m : lcr >= 80m ? 2m : 1m;
        if (nsfr > 0)
        {
            var nsfrScore = nsfr >= 120m ? 5m : nsfr >= 110m ? 4m : nsfr >= 100m ? 3m : nsfr >= 90m ? 2m : 1m;
            return (lcrScore * 0.6m) + (nsfrScore * 0.4m);
        }
        return lcrScore;
    }

    private static decimal ScoreSensitivity(decimal fxExposure, decimal car)
    {
        if (fxExposure == 0) return 4m; // Low exposure = good
        var fxScore = fxExposure < 5m ? 5m : fxExposure < 10m ? 4m : fxExposure < 20m ? 3m : fxExposure < 30m ? 2m : 1m;
        // If CAR is also low + high FX, amplify the risk
        if (car < 12m && fxExposure > 15m) fxScore -= 0.5m;
        return Math.Max(1m, fxScore);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static decimal ComputeCorrelation(List<decimal> a, List<decimal> b)
    {
        if (a.Count != b.Count || a.Count < 2) return 0;
        var n = a.Count;
        var meanA = a.Average();
        var meanB = b.Average();
        decimal cov = 0, varA = 0, varB = 0;
        for (int i = 0; i < n; i++)
        {
            var da = a[i] - meanA;
            var db = b[i] - meanB;
            cov += da * db;
            varA += da * da;
            varB += db * db;
        }
        if (varA == 0 || varB == 0) return 0;
        return cov / (decimal)(Math.Sqrt((double)(varA * varB)));
    }

    private static decimal ComputeSystemicRiskIndex(List<CamelsScore> scores, List<SystemicEwi> indicators)
    {
        if (scores.Count == 0) return 0;
        // Weighted: 60% average composite inverted + 40% indicator severity
        var avgComposite = scores.Average(s => s.Composite);
        var compositeRisk = Math.Max(0, 100m - (avgComposite * 20m)); // 5→0, 1→80

        var indicatorRisk = indicators.Sum(i => i.Severity == RiskRating.Red ? 15m : 8m);
        indicatorRisk = Math.Min(100m, indicatorRisk);

        return Math.Round((compositeRisk * 0.6m) + (indicatorRisk * 0.4m), 1);
    }

    private static string MapFlagToAction(string flagCode) => flagCode switch
    {
        "DECLINING_CAR" => "Request Capital Restoration Plan",
        "CAPITAL_BELOW_MINIMUM" => "Issue Prompt Corrective Action Notice",
        "NPL_ABOVE_THRESHOLD" => "Request Asset Quality Review Report",
        "RISING_NPL" => "Schedule Targeted Examination",
        "CONSECUTIVE_LATE_FILINGS" => "Issue Compliance Warning Letter",
        "HIGH_VALIDATION_WARNING_RATE" => "Request Data Quality Remediation Plan",
        _ => "Schedule Supervisory Review Meeting"
    };

    private static string MapSeverityToEscalation(EarlyWarningSeverity severity) => severity switch
    {
        EarlyWarningSeverity.Red => "Director",
        EarlyWarningSeverity.Amber => "Senior Examiner",
        _ => "Analyst"
    };

    private static string GenerateLetterTemplate(EarlyWarningFlag flag)
    {
        return $"""
        CONFIDENTIAL — SUPERVISORY LETTER

        Re: Early Warning — {flag.FlagCode.Replace("_", " ")}

        Dear Board of Directors / Managing Director,

        This letter serves as formal notification that {flag.InstitutionName} has triggered an early warning indicator in our supervisory monitoring system.

        Finding: {flag.Message}
        Severity: {flag.Severity}
        Date Detected: {flag.TriggeredAt:dd MMMM yyyy}

        Required Action:
        You are required to submit a remediation plan within 14 calendar days of receipt of this letter, detailing:
        1. Root cause analysis of the identified issue
        2. Specific corrective measures to be implemented
        3. Timeline for implementation (not to exceed 90 days)
        4. Responsible officers for each corrective measure

        Failure to respond within the stipulated timeframe may result in escalation of supervisory action, including but not limited to:
        - On-site examination
        - Restrictions on business activities
        - Imposition of additional prudential requirements

        This letter is issued under the authority of the relevant banking regulations and supervisory guidelines.

        Yours faithfully,
        [Director of Supervision]
        """;
    }

    private IQueryable<Submission> BuildScopedSubmissionQuery(string regulatorCode)
    {
        var code = regulatorCode.Trim();
        return _db.Submissions
            .AsNoTracking()
            .Include(s => s.Institution)
            .Include(s => s.ReturnPeriod)
                .ThenInclude(rp => rp!.Module)
            .Where(s => s.ReturnPeriod != null
                        && s.ReturnPeriod.Module != null
                        && s.ReturnPeriod.Module.RegulatorCode == code);
    }
}
