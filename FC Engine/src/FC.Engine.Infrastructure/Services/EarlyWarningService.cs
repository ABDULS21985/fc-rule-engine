using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Services;

public class EarlyWarningService : IEarlyWarningService
{
    private static readonly string[] CarKeys =
    {
        "car",
        "carratio",
        "capitaladequacyratio",
        "capitalratio",
        "capitaladequacy"
    };

    private static readonly string[] NplKeys =
    {
        "npl",
        "nplratio",
        "nonperformingloanratio",
        "nonperformingloansratio"
    };

    private static readonly string[] LcrKeys =
    {
        "lcr",
        "liquiditycoverageratio",
        "liquiditycoverage"
    };

    private static readonly string[] TotalAssetsKeys =
    {
        "totalassets",
        "totalasset",
        "assets"
    };

    private static readonly string[] DepositConcentrationKeys =
    {
        "top20depositorshare",
        "top20depositorconcentration",
        "depositconcentration",
        "largedepositorshare"
    };

    private static readonly string[] RelatedPartyKeys =
    {
        "relatedpartylending",
        "relatedpartyexposure",
        "insiderlending",
        "insiderexposure"
    };

    private readonly MetadataDbContext _db;

    public EarlyWarningService(MetadataDbContext db)
    {
        _db = db;
    }

    public async Task<List<EarlyWarningFlag>> ComputeFlags(string regulatorCode, CancellationToken ct = default)
    {
        var rows = await _db.Submissions
            .AsNoTracking()
            .Include(s => s.Institution)
            .Include(s => s.ReturnPeriod)
                .ThenInclude(rp => rp!.Module)
            .Where(s => s.ReturnPeriod != null
                        && s.ReturnPeriod.Module != null
                        && s.ReturnPeriod.Module.RegulatorCode == regulatorCode)
            .Select(s => new
            {
                s.Id,
                s.InstitutionId,
                InstitutionName = s.Institution != null ? s.Institution.InstitutionName : "Unknown",
                s.ParsedDataJson,
                Year = s.ReturnPeriod != null ? s.ReturnPeriod.Year : 0,
                Month = s.ReturnPeriod != null ? s.ReturnPeriod.Month : 1,
                Quarter = s.ReturnPeriod != null ? s.ReturnPeriod.Quarter : null,
                s.SubmittedAt
            })
            .ToListAsync(ct);

        var flags = new List<EarlyWarningFlag>();

        var byInstitution = rows
            .GroupBy(x => new { x.InstitutionId, x.InstitutionName })
            .ToList();

        foreach (var institution in byInstitution)
        {
            var series = institution
                .Select(x => new
                {
                    x.Id,
                    x.Year,
                    Quarter = RegulatorAnalyticsSupport.ResolveQuarter(x.Month, x.Quarter),
                    x.SubmittedAt,
                    Car = RegulatorAnalyticsSupport.ExtractFirstMetric(x.ParsedDataJson, CarKeys),
                    Npl = RegulatorAnalyticsSupport.ExtractFirstMetric(x.ParsedDataJson, NplKeys),
                    Lcr = RegulatorAnalyticsSupport.ExtractFirstMetric(x.ParsedDataJson, LcrKeys),
                    TotalAssets = RegulatorAnalyticsSupport.ExtractFirstMetric(x.ParsedDataJson, TotalAssetsKeys),
                    DepositConcentration = RegulatorAnalyticsSupport.ExtractFirstMetric(x.ParsedDataJson, DepositConcentrationKeys),
                    RelatedParty = RegulatorAnalyticsSupport.ExtractFirstMetric(x.ParsedDataJson, RelatedPartyKeys)
                })
                .OrderByDescending(x => x.Year)
                .ThenByDescending(x => x.Quarter)
                .ThenByDescending(x => x.SubmittedAt)
                .ToList();

            var carSeries = series
                .Select(x => x.Car)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Take(3)
                .ToList();

            if (carSeries.Count == 3 && carSeries[0] < carSeries[1] && carSeries[1] < carSeries[2])
            {
                flags.Add(new EarlyWarningFlag
                {
                    InstitutionId = institution.Key.InstitutionId,
                    InstitutionName = institution.Key.InstitutionName,
                    Severity = EarlyWarningSeverity.Red,
                    FlagCode = "DECLINING_CAR",
                    Message = "Capital adequacy ratio has declined for 3 consecutive quarters."
                });
            }

            if (carSeries.Count > 0)
            {
                var latestCar = carSeries[0];
                if (latestCar < 10m)
                {
                    flags.Add(new EarlyWarningFlag
                    {
                        InstitutionId = institution.Key.InstitutionId,
                        InstitutionName = institution.Key.InstitutionName,
                        Severity = EarlyWarningSeverity.Red,
                        FlagCode = "CAPITAL_BELOW_MINIMUM",
                        Message = $"Latest CAR is {latestCar:0.##}% (below 10% minimum)."
                    });
                }
                else if (latestCar < 12m)
                {
                    flags.Add(new EarlyWarningFlag
                    {
                        InstitutionId = institution.Key.InstitutionId,
                        InstitutionName = institution.Key.InstitutionName,
                        Severity = EarlyWarningSeverity.Amber,
                        FlagCode = "CAPITAL_NEAR_MINIMUM",
                        Message = $"Latest CAR is {latestCar:0.##}% and is approaching minimum threshold."
                    });
                }
            }

            var nplSeries = series
                .Select(x => x.Npl)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Take(3)
                .ToList();

            if (nplSeries.Count > 0 && nplSeries[0] > 5m)
            {
                flags.Add(new EarlyWarningFlag
                {
                    InstitutionId = institution.Key.InstitutionId,
                    InstitutionName = institution.Key.InstitutionName,
                    Severity = EarlyWarningSeverity.Red,
                    FlagCode = "NPL_ABOVE_THRESHOLD",
                    Message = $"Latest NPL ratio is {nplSeries[0]:0.##}% (above 5%)."
                });
            }
            else if (nplSeries.Count >= 2 && (nplSeries[0] - nplSeries[1]) > 2m)
            {
                // NPL rising >2pp in a single quarter
                flags.Add(new EarlyWarningFlag
                {
                    InstitutionId = institution.Key.InstitutionId,
                    InstitutionName = institution.Key.InstitutionName,
                    Severity = EarlyWarningSeverity.Red,
                    FlagCode = "NPL_SPIKE",
                    Message = $"NPL ratio surged {(nplSeries[0] - nplSeries[1]):0.##}pp in a single quarter (from {nplSeries[1]:0.##}% to {nplSeries[0]:0.##}%)."
                });
            }
            else if (nplSeries.Count == 3 && nplSeries[0] > nplSeries[1] && nplSeries[1] > nplSeries[2])
            {
                flags.Add(new EarlyWarningFlag
                {
                    InstitutionId = institution.Key.InstitutionId,
                    InstitutionName = institution.Key.InstitutionName,
                    Severity = EarlyWarningSeverity.Amber,
                    FlagCode = "RISING_NPL",
                    Message = "NPL ratio is increasing for 3 consecutive quarters."
                });
            }

            // ── LCR below 110% (approaching 100% minimum) ──
            var lcrSeries = series
                .Select(x => x.Lcr)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Take(2)
                .ToList();

            if (lcrSeries.Count > 0)
            {
                var latestLcr = lcrSeries[0];
                if (latestLcr < 100m)
                {
                    flags.Add(new EarlyWarningFlag
                    {
                        InstitutionId = institution.Key.InstitutionId,
                        InstitutionName = institution.Key.InstitutionName,
                        Severity = EarlyWarningSeverity.Red,
                        FlagCode = "LCR_BELOW_MINIMUM",
                        Message = $"LCR is {latestLcr:0.##}% (below 100% regulatory minimum)."
                    });
                }
                else if (latestLcr < 110m)
                {
                    flags.Add(new EarlyWarningFlag
                    {
                        InstitutionId = institution.Key.InstitutionId,
                        InstitutionName = institution.Key.InstitutionName,
                        Severity = EarlyWarningSeverity.Amber,
                        FlagCode = "LCR_APPROACHING_MINIMUM",
                        Message = $"LCR is {latestLcr:0.##}% and is approaching the 100% minimum threshold."
                    });
                }
            }

            // ── Deposit concentration: top 20 depositors > 30% ──
            var latestDepConcentration = series
                .Select(x => x.DepositConcentration)
                .FirstOrDefault(x => x.HasValue);

            if (latestDepConcentration.HasValue && latestDepConcentration.Value > 30m)
            {
                flags.Add(new EarlyWarningFlag
                {
                    InstitutionId = institution.Key.InstitutionId,
                    InstitutionName = institution.Key.InstitutionName,
                    Severity = latestDepConcentration.Value > 50m ? EarlyWarningSeverity.Red : EarlyWarningSeverity.Amber,
                    FlagCode = "DEPOSIT_CONCENTRATION",
                    Message = $"Top 20 depositors represent {latestDepConcentration.Value:0.##}% of total deposits (above 30% threshold)."
                });
            }

            // ── Related-party lending exceeding regulatory limits ──
            var latestRelatedParty = series
                .Select(x => x.RelatedParty)
                .FirstOrDefault(x => x.HasValue);

            if (latestRelatedParty.HasValue && latestRelatedParty.Value > 20m)
            {
                flags.Add(new EarlyWarningFlag
                {
                    InstitutionId = institution.Key.InstitutionId,
                    InstitutionName = institution.Key.InstitutionName,
                    Severity = latestRelatedParty.Value > 35m ? EarlyWarningSeverity.Red : EarlyWarningSeverity.Amber,
                    FlagCode = "RELATED_PARTY_EXCESS",
                    Message = $"Related-party lending is {latestRelatedParty.Value:0.##}% of capital (exceeds 20% regulatory limit)."
                });
            }

            // ── Sudden asset growth (>30% quarter-on-quarter) ──
            var assetSeries = series
                .Select(x => x.TotalAssets)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Take(2)
                .ToList();

            if (assetSeries.Count == 2 && assetSeries[1] > 0)
            {
                var growthPct = (assetSeries[0] - assetSeries[1]) / assetSeries[1] * 100m;
                if (growthPct > 30m)
                {
                    flags.Add(new EarlyWarningFlag
                    {
                        InstitutionId = institution.Key.InstitutionId,
                        InstitutionName = institution.Key.InstitutionName,
                        Severity = growthPct > 50m ? EarlyWarningSeverity.Red : EarlyWarningSeverity.Amber,
                        FlagCode = "RAPID_ASSET_GROWTH",
                        Message = $"Total assets grew {growthPct:0.#}% quarter-on-quarter (above 30% threshold). May indicate unsustainable expansion."
                    });
                }
            }

            var submissionIds = institution.Select(x => x.Id).ToList();
            var recentSla = await _db.FilingSlaRecords
                .AsNoTracking()
                .Where(x => x.SubmissionId.HasValue && submissionIds.Contains(x.SubmissionId.Value))
                .OrderByDescending(x => x.PeriodEndDate)
                .Take(2)
                .ToListAsync(ct);

            if (recentSla.Count == 2 && recentSla.All(x => x.OnTime == false))
            {
                flags.Add(new EarlyWarningFlag
                {
                    InstitutionId = institution.Key.InstitutionId,
                    InstitutionName = institution.Key.InstitutionName,
                    Severity = EarlyWarningSeverity.Amber,
                    FlagCode = "CONSECUTIVE_LATE_FILINGS",
                    Message = "Institution has 2 consecutive late filings."
                });
            }

            var warningRate = await ComputeWarningRate(submissionIds, ct);
            if (warningRate > 20m)
            {
                flags.Add(new EarlyWarningFlag
                {
                    InstitutionId = institution.Key.InstitutionId,
                    InstitutionName = institution.Key.InstitutionName,
                    Severity = EarlyWarningSeverity.Amber,
                    FlagCode = "HIGH_VALIDATION_WARNING_RATE",
                    Message = $"Validation warning rate is {warningRate:0.##}% (above 20%)."
                });
            }
        }

        return flags
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.InstitutionName)
            .ThenBy(f => f.FlagCode)
            .ToList();
    }

    private async Task<decimal> ComputeWarningRate(IReadOnlyCollection<int> submissionIds, CancellationToken ct)
    {
        if (submissionIds.Count == 0)
        {
            return 0;
        }

        var reports = await _db.ValidationReports
            .AsNoTracking()
            .Include(r => r.Errors)
            .Where(r => submissionIds.Contains(r.SubmissionId))
            .ToListAsync(ct);

        if (reports.Count == 0)
        {
            return 0;
        }

        var warnings = reports.Sum(r => r.Errors.Count(e => e.Severity == ValidationSeverity.Warning));
        var errors = reports.Sum(r => r.Errors.Count(e => e.Severity == ValidationSeverity.Error));
        var total = warnings + errors;
        if (total == 0)
        {
            return 0;
        }

        return 100m * warnings / total;
    }
}
