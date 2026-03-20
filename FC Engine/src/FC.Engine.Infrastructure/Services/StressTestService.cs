using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FC.Engine.Infrastructure.Services;

public class StressTestService : IStressTestService
{
    private static readonly string[] CarKeys = { "car", "carratio", "capitaladequacyratio", "capitalratio", "capitaladequacy" };
    private static readonly string[] NplKeys = { "npl", "nplratio", "nonperformingloanratio", "nonperformingloansratio" };
    private static readonly string[] LcrKeys = { "lcr", "liquiditycoverageratio", "liquiditycoverage" };
    private static readonly string[] TotalAssetsKeys = { "totalassets", "totalasset", "assets" };
    private static readonly string[] DepositKeys = { "totaldeposits", "totaldeposit", "deposits", "customerdeposits" };
    private static readonly string[] FxExposureKeys = { "fxexposure", "foreignexchangeexposure", "fxposition", "netfxposition" };
    private static readonly string[] Tier1Keys = { "tier1", "tier1ratio", "tier1capital" };
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);

    private readonly MetadataDbContext _db;
    private readonly ISystemicRiskService _systemicRisk;
    private readonly IMemoryCache _cache;
    private readonly ILogger<StressTestService> _logger;

    public StressTestService(
        MetadataDbContext db,
        ISystemicRiskService systemicRisk,
        IMemoryCache cache,
        ILogger<StressTestService> logger)
    {
        _db = db;
        _systemicRisk = systemicRisk;
        _cache = cache;
        _logger = logger;
    }

    // ── Predefined Scenario Parameters ──────────────────────────────

    private static readonly Dictionary<StressScenarioType, StressTestShockParameters> ScenarioPresets = new()
    {
        [StressScenarioType.NgfsOrderly] = new StressTestShockParameters
        {
            ScenarioName = "NGFS Net Zero 2050 (Orderly)",
            Description = "Gradual transition to net zero with rising carbon taxes. Moderate impact on carbon-intensive sectors.",
            GdpGrowthDeltaPct = -0.5m,
            FxDepreciationPct = 5m,
            InflationDeltaPp = 1m,
            InterestRateDeltaBps = 100m,
            CreditLossMultiplier = 1.2m,
            TradeVolumeDeltaPct = -5m,
            DepositFlightPct = 2m,
            ImpactChannels = new List<string> { "Carbon-intensive lending", "Transition risk on energy sector" },
            AffectedSectors = new List<string> { "DMB", "DFI", "PMB" }
        },
        [StressScenarioType.NgfsDisorderly] = new StressTestShockParameters
        {
            ScenarioName = "NGFS Delayed Transition (Disorderly)",
            Description = "Sudden policy shift creating stranded assets. Severe impact on fossil fuel portfolios and transition-lagging institutions.",
            GdpGrowthDeltaPct = -2m,
            FxDepreciationPct = 15m,
            InflationDeltaPp = 3m,
            InterestRateDeltaBps = 300m,
            CreditLossMultiplier = 1.8m,
            TradeVolumeDeltaPct = -15m,
            DepositFlightPct = 8m,
            ImpactChannels = new List<string> { "Stranded assets", "Sudden policy shift", "Asset repricing" },
            AffectedSectors = new List<string> { "DMB", "DFI", "PMB" }
        },
        [StressScenarioType.NgfsHotHouse] = new StressTestShockParameters
        {
            ScenarioName = "NGFS Hot House World",
            Description = "No climate transition; severe physical risks including flooding in Lagos and drought in northern regions.",
            GdpGrowthDeltaPct = -4m,
            FxDepreciationPct = 25m,
            InflationDeltaPp = 5m,
            InterestRateDeltaBps = 200m,
            CreditLossMultiplier = 2.5m,
            TradeVolumeDeltaPct = -20m,
            DepositFlightPct = 12m,
            ImpactChannels = new List<string> { "Physical risk (flooding, drought)", "Agricultural sector collapse", "Infrastructure damage" },
            AffectedSectors = new List<string> { "DMB", "DFI", "PMB", "MFB" }
        },
        [StressScenarioType.OilPriceCollapse] = new StressTestShockParameters
        {
            ScenarioName = "Oil Price Collapse (-50%)",
            Description = "Oil prices drop 50%, causing GDP contraction, FX depreciation, and fiscal stress across all sectors.",
            GdpGrowthDeltaPct = -3m,
            FxDepreciationPct = 30m,
            InflationDeltaPp = 5m,
            InterestRateDeltaBps = 150m,
            CreditLossMultiplier = 2.0m,
            TradeVolumeDeltaPct = -25m,
            RemittanceDeltaPct = -15m,
            FdiDeltaPct = -30m,
            DepositFlightPct = 10m,
            ImpactChannels = new List<string> { "Revenue shock", "FX pressure", "Fiscal tightening" },
            AffectedSectors = new List<string> { "DMB", "DFI", "PMB", "MFB", "IMTO" }
        },
        [StressScenarioType.GlobalRecession] = new StressTestShockParameters
        {
            ScenarioName = "Global Recession",
            Description = "Severe global downturn with trade collapse, remittance decline, and FDI withdrawal.",
            GdpGrowthDeltaPct = -5m,
            FxDepreciationPct = 20m,
            InflationDeltaPp = 2m,
            InterestRateDeltaBps = 200m,
            CreditLossMultiplier = 2.2m,
            TradeVolumeDeltaPct = -20m,
            RemittanceDeltaPct = -30m,
            FdiDeltaPct = -40m,
            DepositFlightPct = 8m,
            ImpactChannels = new List<string> { "Trade volumes", "Remittances", "FDI" },
            AffectedSectors = new List<string> { "DMB", "IMTO", "DFI" }
        },
        [StressScenarioType.InterestRateSpike] = new StressTestShockParameters
        {
            ScenarioName = "Interest Rate Spike (+500bps)",
            Description = "Sharp interest rate increase causing bond portfolio losses, NIM squeeze, and borrower stress.",
            GdpGrowthDeltaPct = -1m,
            FxDepreciationPct = 10m,
            InflationDeltaPp = 3m,
            InterestRateDeltaBps = 500m,
            CreditLossMultiplier = 1.5m,
            TradeVolumeDeltaPct = -5m,
            DepositFlightPct = 5m,
            ImpactChannels = new List<string> { "Bond portfolio losses", "NIM squeeze", "Borrower stress" },
            AffectedSectors = new List<string> { "DMB", "MFB", "PMB" }
        },
        [StressScenarioType.Pandemic] = new StressTestShockParameters
        {
            ScenarioName = "Pandemic (COVID-style)",
            Description = "Pandemic-level disruption with moratoria, provisioning surge, and digital adoption shift.",
            GdpGrowthDeltaPct = -6m,
            FxDepreciationPct = 15m,
            InflationDeltaPp = 4m,
            InterestRateDeltaBps = 100m,
            CreditLossMultiplier = 3.0m,
            TradeVolumeDeltaPct = -30m,
            RemittanceDeltaPct = -20m,
            DepositFlightPct = 5m,
            MoratoriaFlag = true,
            ImpactChannels = new List<string> { "Moratoria", "Provisioning surge", "Digital adoption" },
            AffectedSectors = new List<string> { "DMB", "MFB", "PMB", "PSP", "DFI" }
        },
        [StressScenarioType.CyberIncident] = new StressTestShockParameters
        {
            ScenarioName = "Cyber Incident (Systemic)",
            Description = "Major cyber attack disrupting payments infrastructure, causing confidence crisis and deposit outflows.",
            GdpGrowthDeltaPct = -1m,
            FxDepreciationPct = 5m,
            InflationDeltaPp = 1m,
            InterestRateDeltaBps = 50m,
            CreditLossMultiplier = 1.3m,
            DepositFlightPct = 15m,
            ImpactChannels = new List<string> { "Payments disruption", "Confidence crisis", "Deposit outflows" },
            AffectedSectors = new List<string> { "PSP", "DMB" }
        }
    };

    // ── Public API ──────────────────────────────────────────────────

    public List<StressScenarioInfo> GetAvailableScenarios()
    {
        var scenarios = new List<StressScenarioInfo>();

        foreach (var (type, parameters) in ScenarioPresets)
        {
            var category = type switch
            {
                StressScenarioType.NgfsOrderly or
                StressScenarioType.NgfsDisorderly or
                StressScenarioType.NgfsHotHouse => "NGFS Climate Scenarios",
                _ => "Macro-Economic Shocks"
            };

            scenarios.Add(new StressScenarioInfo
            {
                Type = type,
                Name = parameters.ScenarioName,
                Description = parameters.Description,
                Category = category,
                DefaultParameters = parameters
            });
        }

        scenarios.Add(new StressScenarioInfo
        {
            Type = StressScenarioType.Custom,
            Name = "Custom Scenario",
            Description = "Define custom shock parameters for a bespoke stress test.",
            Category = "Custom"
        });

        return scenarios;
    }

    public async Task<StressTestReport> RunStressTestAsync(
        string regulatorCode, StressTestRequest request, CancellationToken ct = default)
    {
        var cacheKey = BuildReportCacheKey(regulatorCode, request);
        if (_cache.TryGetValue(cacheKey, out StressTestReport? cachedReport) && cachedReport is not null)
        {
            _logger.LogDebug("Returning cached stress test report for {ScenarioType} and regulator {RegulatorCode}",
                request.ScenarioType, regulatorCode);
            return cachedReport;
        }

        _logger.LogInformation("Running stress test: {ScenarioType} for regulator {RegulatorCode}",
            request.ScenarioType, regulatorCode);

        var parameters = ResolveParameters(request);

        // 1. Fetch latest submission data for all entities
        var entityData = await FetchEntityDataAsync(regulatorCode, request.TargetLicenceTypes, ct);

        // 2. Get pre-stress CAMELS scores for baseline
        var camelsScores = await _systemicRisk.ComputeCamelsScores(regulatorCode, ct);
        var camelsMap = camelsScores.ToDictionary(c => c.InstitutionId, c => c.Composite);

        // 3. Apply shocks per entity
        var entityResults = new List<StressTestEntityResult>();
        foreach (var entity in entityData)
        {
            var result = ApplyShock(entity, parameters, camelsMap);
            entityResults.Add(result);
        }

        // 4. Contagion analysis — identify second-round effects
        var contagionResults = await ComputeContagionAsync(
            regulatorCode, entityResults.Where(e => e.CarBreach).ToList(), ct);

        // Apply second-round effects
        ApplySecondRoundEffects(entityResults, contagionResults);

        // 5. Sector aggregation
        var aggregation = ComputeAggregation(entityResults);

        // 6. Determine resilience rating
        var (rating, rationale) = DetermineResilienceRating(aggregation);

        // 7. Generate recommendations
        var recommendations = GenerateRecommendations(aggregation, contagionResults, parameters);

        var report = new StressTestReport
        {
            ScenarioName = parameters.ScenarioName,
            ScenarioType = request.ScenarioType,
            Parameters = parameters,
            ResilienceRating = rating,
            ResilienceRationale = rationale,
            EntityResults = entityResults.OrderBy(e => e.PostStressCar).ToList(),
            ContagionResults = contagionResults,
            Aggregation = aggregation,
            Recommendations = recommendations,
            GeneratedAt = DateTime.UtcNow,
            RegulatorCode = regulatorCode
        };

        _cache.Set(cacheKey, report, new MemoryCacheEntryOptions
        {
            SlidingExpiration = CacheTtl
        });

        return report;
    }

    public Task<byte[]> GenerateReportPdfAsync(
        string regulatorCode, StressTestReport report, CancellationToken ct = default)
    {
        var cacheKey = BuildPdfCacheKey(regulatorCode, report);
        if (_cache.TryGetValue(cacheKey, out byte[]? cachedPdf) && cachedPdf is not null)
        {
            return Task.FromResult(cachedPdf);
        }

        QuestPDF.Settings.License = LicenseType.Community;

        var primaryColor = "#006B3F";
        var dangerColor = "#dc2626";
        var warningColor = "#d97706";
        var successColor = "#059669";

        var pdf = Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("RegOS Sector Stress Test Report").Bold().FontSize(16).FontColor(primaryColor);
                        col.Item().Text(report.ScenarioName).FontSize(11).FontColor(Colors.Grey.Medium);
                        col.Item().Text($"Generated: {report.GeneratedAt:dd MMM yyyy HH:mm} UTC").FontSize(8).FontColor(Colors.Grey.Medium);
                    });
                    row.ConstantItem(120).AlignRight().Column(col =>
                    {
                        var ratingColor = report.ResilienceRating switch
                        {
                            SectorResilienceRating.Green => successColor,
                            SectorResilienceRating.Amber => warningColor,
                            _ => dangerColor
                        };
                        col.Item().Text("SECTOR RESILIENCE").FontSize(8).Bold();
                        col.Item().Text(report.ResilienceRating.ToString().ToUpperInvariant()).Bold().FontSize(18).FontColor(ratingColor);
                    });
                });

                page.Content().Column(col =>
                {
                    col.Spacing(8);

                    // Executive Summary
                    col.Item().Text("1. Executive Summary").FontSize(14).Bold().FontColor(primaryColor);
                    col.Item().Text(report.ResilienceRationale).FontSize(9);

                    col.Item().Row(row =>
                    {
                        var agg = report.Aggregation;
                        RenderKpiCell(row, "Entities Tested", agg.TotalEntitiesTested.ToString(), primaryColor);
                        RenderKpiCell(row, "CAR Breaches", agg.CarBreachCount.ToString(), agg.CarBreachCount > 0 ? dangerColor : successColor);
                        RenderKpiCell(row, "LCR Breaches", agg.LcrBreachCount.ToString(), agg.LcrBreachCount > 0 ? dangerColor : successColor);
                        RenderKpiCell(row, "Solvency Breaches", agg.SolvencyBreachCount.ToString(), agg.SolvencyBreachCount > 0 ? dangerColor : successColor);
                        RenderKpiCell(row, "NDIC Exposure Ratio", $"{agg.NdicExposureRatio}%", agg.NdicExposureRatio > 50 ? dangerColor : warningColor);
                    });

                    // Scenario Parameters
                    col.Item().PaddingTop(6).Text("2. Scenario Parameters").FontSize(14).Bold().FontColor(primaryColor);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(1);
                        });
                        var p = report.Parameters;
                        RenderParamRow(table, "GDP Growth Delta", $"{p.GdpGrowthDeltaPct:+0.0;-0.0}%", primaryColor);
                        RenderParamRow(table, "FX Depreciation", $"{p.FxDepreciationPct}%", primaryColor);
                        RenderParamRow(table, "Inflation Delta", $"+{p.InflationDeltaPp}pp", primaryColor);
                        RenderParamRow(table, "Interest Rate Delta", $"+{p.InterestRateDeltaBps}bps", primaryColor);
                        RenderParamRow(table, "Credit Loss Multiplier", $"{p.CreditLossMultiplier:0.0}x", primaryColor);
                        RenderParamRow(table, "Deposit Flight", $"{p.DepositFlightPct}%", primaryColor);
                    });

                    // Pre vs Post Comparison
                    col.Item().PaddingTop(6).Text("3. Pre-Stress vs Post-Stress Averages").FontSize(14).Bold().FontColor(primaryColor);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                        });
                        var agg = report.Aggregation;
                        RenderCompHeader(table, primaryColor);
                        RenderCompRow(table, "CAR (%)", agg.PreStressAverageCar, agg.PostStressAverageCar);
                        RenderCompRow(table, "NPL (%)", agg.PreStressAverageNpl, agg.PostStressAverageNpl);
                        RenderCompRow(table, "LCR (%)", agg.PreStressAverageLcr, agg.PostStressAverageLcr);
                    });

                    // Entity Results
                    col.Item().PageBreak();
                    col.Item().Text("4. Entity-Level Results").FontSize(14).Bold().FontColor(primaryColor);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3); // Name
                            columns.RelativeColumn(1); // Licence
                            columns.RelativeColumn(1); // Pre-CAR
                            columns.RelativeColumn(1); // Post-CAR
                            columns.RelativeColumn(1); // Pre-NPL
                            columns.RelativeColumn(1); // Post-NPL
                            columns.RelativeColumn(1); // Pre-LCR
                            columns.RelativeColumn(1); // Post-LCR
                            columns.RelativeColumn(1); // Breaches
                        });

                        table.Header(header =>
                        {
                            foreach (var h in new[] { "Institution", "Licence", "Pre-CAR", "Post-CAR", "Pre-NPL", "Post-NPL", "Pre-LCR", "Post-LCR", "Breaches" })
                            {
                                header.Cell().Background(primaryColor).Padding(3).Text(h).FontColor(Colors.White).Bold().FontSize(7);
                            }
                        });

                        foreach (var entity in report.EntityResults)
                        {
                            var breaches = new List<string>();
                            if (entity.CarBreach) breaches.Add("CAR");
                            if (entity.LcrBreach) breaches.Add("LCR");
                            if (entity.NplBreach) breaches.Add("NPL");

                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(2).Text(entity.InstitutionName).FontSize(7);
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(2).Text(entity.LicenceType).FontSize(7);
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(2).Text($"{entity.PreStressCar:0.##}%").FontSize(7);
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(2)
                                .Text($"{entity.PostStressCar:0.##}%").FontSize(7)
                                .FontColor(entity.CarBreach ? dangerColor : Colors.Black);
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(2).Text($"{entity.PreStressNpl:0.##}%").FontSize(7);
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(2)
                                .Text($"{entity.PostStressNpl:0.##}%").FontSize(7)
                                .FontColor(entity.NplBreach ? dangerColor : Colors.Black);
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(2).Text($"{entity.PreStressLcr:0.##}%").FontSize(7);
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(2)
                                .Text($"{entity.PostStressLcr:0.##}%").FontSize(7)
                                .FontColor(entity.LcrBreach ? dangerColor : Colors.Black);
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(2)
                                .Text(breaches.Count > 0 ? string.Join(", ", breaches) : "None").FontSize(7)
                                .FontColor(breaches.Count > 0 ? dangerColor : successColor);
                        }
                    });

                    // Contagion
                    if (report.ContagionResults.Count > 0)
                    {
                        col.Item().PaddingTop(8).Text("5. Contagion & Second-Round Effects").FontSize(14).Bold().FontColor(primaryColor);
                        foreach (var cr in report.ContagionResults)
                        {
                            col.Item().Text($"If {cr.FailedEntityName} fails (Post-CAR: {cr.FailedEntityCar:0.##}%):").Bold().FontSize(9);
                            foreach (var exp in cr.ExposedEntities)
                            {
                                var secondRound = exp.SecondRoundBreach ? " [SECOND-ROUND BREACH]" : "";
                                col.Item().Text($"  - {exp.EntityName}: correlation {exp.CorrelationStrength:0.###}, est. loss {exp.EstimatedLoss:N0}{secondRound}")
                                    .FontSize(8).FontColor(exp.SecondRoundBreach ? dangerColor : Colors.Black);
                            }
                        }
                    }

                    // Recommendations
                    col.Item().PaddingTop(8).Text($"{(report.ContagionResults.Count > 0 ? "6" : "5")}. Recommendations").FontSize(14).Bold().FontColor(primaryColor);
                    foreach (var rec in report.Recommendations)
                    {
                        col.Item().Text($"  \u2022 {rec}").FontSize(9);
                    }
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text("CONFIDENTIAL — Regulatory Stress Test Report — RegOS")
                        .FontSize(7).FontColor(Colors.Grey.Medium);
                    row.ConstantItem(100).AlignRight().Text(txt =>
                    {
                        txt.Span("Page ").FontSize(7);
                        txt.CurrentPageNumber().FontSize(7);
                        txt.Span(" of ").FontSize(7);
                        txt.TotalPages().FontSize(7);
                    });
                });
            });
        }).GeneratePdf();

        _cache.Set(cacheKey, pdf, new MemoryCacheEntryOptions
        {
            SlidingExpiration = CacheTtl
        });

        return Task.FromResult(pdf);
    }

    private static string BuildReportCacheKey(string regulatorCode, StressTestRequest request)
    {
        var payload = JsonSerializer.Serialize(request);
        return $"stress-test:report:{regulatorCode}:{ComputeHash(payload)}";
    }

    private static string BuildPdfCacheKey(string regulatorCode, StressTestReport report)
    {
        var payload = JsonSerializer.Serialize(new
        {
            regulatorCode,
            report.ScenarioType,
            report.GeneratedAt,
            report.ScenarioName,
            EntityCount = report.EntityResults.Count
        });
        return $"stress-test:pdf:{regulatorCode}:{ComputeHash(payload)}";
    }

    private static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    // ── Private Methods ─────────────────────────────────────────────

    private static StressTestShockParameters ResolveParameters(StressTestRequest request)
    {
        if (request.ScenarioType == StressScenarioType.Custom)
        {
            return request.CustomParameters ?? new StressTestShockParameters
            {
                ScenarioName = "Custom Scenario",
                Description = "User-defined stress scenario"
            };
        }

        return ScenarioPresets.TryGetValue(request.ScenarioType, out var preset)
            ? preset
            : throw new ArgumentException($"Unknown scenario type: {request.ScenarioType}");
    }

    private async Task<List<EntityMetrics>> FetchEntityDataAsync(
        string regulatorCode, List<string>? targetLicenceTypes, CancellationToken ct)
    {
        var query = BuildScopedSubmissionQuery(regulatorCode);

        var rows = await query
            .Select(s => new
            {
                s.InstitutionId,
                InstitutionName = s.Institution != null ? s.Institution.InstitutionName : "Unknown",
                LicenceType = s.Institution != null ? s.Institution.LicenseType : "Unknown",
                s.ParsedDataJson,
                Year = s.ReturnPeriod != null ? s.ReturnPeriod.Year : 0,
                Quarter = s.ReturnPeriod != null ? s.ReturnPeriod.Quarter : null,
                Month = s.ReturnPeriod != null ? s.ReturnPeriod.Month : 1
            })
            .ToListAsync(ct);

        var byInstitution = rows
            .GroupBy(x => new { x.InstitutionId, x.InstitutionName, x.LicenceType })
            .ToList();

        var results = new List<EntityMetrics>();

        foreach (var inst in byInstitution)
        {
            if (targetLicenceTypes is { Count: > 0 } &&
                !targetLicenceTypes.Any(t => string.Equals(t, inst.Key.LicenceType, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var latest = inst
                .OrderByDescending(x => x.Year)
                .ThenByDescending(x => RegulatorAnalyticsSupport.ResolveQuarter(x.Month, x.Quarter))
                .First();

            results.Add(new EntityMetrics
            {
                InstitutionId = inst.Key.InstitutionId,
                InstitutionName = inst.Key.InstitutionName,
                LicenceType = inst.Key.LicenceType ?? "Unknown",
                Car = RegulatorAnalyticsSupport.ExtractFirstMetric(latest.ParsedDataJson, CarKeys) ?? 0,
                Npl = RegulatorAnalyticsSupport.ExtractFirstMetric(latest.ParsedDataJson, NplKeys) ?? 0,
                Lcr = RegulatorAnalyticsSupport.ExtractFirstMetric(latest.ParsedDataJson, LcrKeys) ?? 0,
                TotalAssets = RegulatorAnalyticsSupport.ExtractFirstMetric(latest.ParsedDataJson, TotalAssetsKeys) ?? 0,
                Deposits = RegulatorAnalyticsSupport.ExtractFirstMetric(latest.ParsedDataJson, DepositKeys) ?? 0,
                FxExposure = RegulatorAnalyticsSupport.ExtractFirstMetric(latest.ParsedDataJson, FxExposureKeys) ?? 0,
                Tier1 = RegulatorAnalyticsSupport.ExtractFirstMetric(latest.ParsedDataJson, Tier1Keys) ?? 0
            });
        }

        return results;
    }

    private static StressTestEntityResult ApplyShock(
        EntityMetrics entity, StressTestShockParameters parameters, Dictionary<int, decimal> camelsMap)
    {
        // ── CAR Impact ──
        // CAR is reduced by: GDP contraction (weighted), credit loss multiplier, and FX depreciation (for exposed institutions)
        var gdpSensitivity = 0.5m; // CAR sensitivity to GDP
        var fxSensitivity = entity.FxExposure > 10m ? 0.3m : 0.1m;
        var interestSensitivity = 0.02m; // per 100bps

        var carDelta =
            (parameters.GdpGrowthDeltaPct / 100m * gdpSensitivity * entity.Car) +
            (parameters.FxDepreciationPct / 100m * fxSensitivity * entity.Car) +
            (parameters.InterestRateDeltaBps / 100m * interestSensitivity * entity.Car);

        // Credit loss multiplier erodes capital through increased provisioning
        var creditLossImpact = (parameters.CreditLossMultiplier - 1m) * entity.Npl * 0.4m;
        var postCar = Math.Max(0, entity.Car + carDelta - creditLossImpact);

        // ── NPL Impact ──
        // NPL increases with GDP contraction and credit loss multiplier
        var nplMultiplier = parameters.CreditLossMultiplier;
        if (parameters.MoratoriaFlag)
        {
            // Moratoria initially suppress NPL recognition, then surge
            nplMultiplier *= 1.3m;
        }
        var postNpl = entity.Npl * nplMultiplier;

        // ── LCR Impact ──
        // LCR decreases with deposit flight and interest rate pressure
        var depositFlightImpact = parameters.DepositFlightPct / 100m * entity.Lcr;
        var interestPressure = parameters.InterestRateDeltaBps / 10000m * entity.Lcr * 0.15m;
        var postLcr = Math.Max(0, entity.Lcr - depositFlightImpact - interestPressure);

        // ── Insurable Deposits ──
        // Estimate insurable deposits as 60% of total deposits (NDIC coverage)
        var insurableDeposits = entity.Deposits * 0.6m;

        // ── Post-stress CAMELS estimate ──
        var preCamels = camelsMap.GetValueOrDefault(entity.InstitutionId, 3m);
        var camelsDelta = (entity.Car - postCar) / entity.Car * 0.4m * preCamels;
        var postCamels = Math.Max(1m, preCamels - Math.Abs(camelsDelta));

        var carBreach = postCar < 10m;
        var lcrBreach = postLcr < 100m;
        var nplBreach = postNpl > 15m;

        return new StressTestEntityResult
        {
            InstitutionId = entity.InstitutionId,
            InstitutionName = entity.InstitutionName,
            LicenceType = entity.LicenceType,
            TotalAssets = entity.TotalAssets,
            InsurableDeposits = insurableDeposits,
            PreStressCar = Math.Round(entity.Car, 2),
            PreStressNpl = Math.Round(entity.Npl, 2),
            PreStressLcr = Math.Round(entity.Lcr, 2),
            PostStressCar = Math.Round(postCar, 2),
            PostStressNpl = Math.Round(postNpl, 2),
            PostStressLcr = Math.Round(postLcr, 2),
            CarBreach = carBreach,
            LcrBreach = lcrBreach,
            NplBreach = nplBreach,
            SolvencyBreach = carBreach && nplBreach,
            PreStressCamels = Math.Round(preCamels, 2),
            PostStressCamels = Math.Round(postCamels, 2)
        };
    }

    private async Task<List<StressTestContagionResult>> ComputeContagionAsync(
        string regulatorCode, List<StressTestEntityResult> failedEntities, CancellationToken ct)
    {
        if (failedEntities.Count == 0)
            return new List<StressTestContagionResult>();

        var contagion = await _systemicRisk.AnalyzeContagion(regulatorCode, ct);

        var results = new List<StressTestContagionResult>();
        var failedIds = failedEntities.Select(e => e.InstitutionId).ToHashSet();

        foreach (var failed in failedEntities)
        {
            var exposedLinks = contagion.Links
                .Where(l => l.SourceId == failed.InstitutionId || l.TargetId == failed.InstitutionId)
                .Where(l => !failedIds.Contains(l.SourceId == failed.InstitutionId ? l.TargetId : l.SourceId))
                .ToList();

            if (exposedLinks.Count == 0) continue;

            var exposures = new List<ContagionExposure>();
            decimal totalExposure = 0;

            foreach (var link in exposedLinks)
            {
                var exposedId = link.SourceId == failed.InstitutionId ? link.TargetId : link.SourceId;
                var exposedName = link.SourceId == failed.InstitutionId ? link.TargetName : link.SourceName;

                // Estimated loss is proportional to correlation strength and failed entity's total assets
                var estimatedLoss = failed.TotalAssets * link.CorrelationStrength * 0.05m;
                totalExposure += estimatedLoss;

                exposures.Add(new ContagionExposure
                {
                    EntityId = exposedId,
                    EntityName = exposedName,
                    CorrelationStrength = link.CorrelationStrength,
                    EstimatedLoss = Math.Round(estimatedLoss, 0),
                    SecondRoundBreach = false // will be evaluated later
                });
            }

            results.Add(new StressTestContagionResult
            {
                FailedEntityId = failed.InstitutionId,
                FailedEntityName = failed.InstitutionName,
                FailedEntityCar = failed.PostStressCar,
                ExposedEntities = exposures,
                TotalInterbankExposure = Math.Round(totalExposure, 0),
                EstimatedDepositFlight = Math.Round(failed.InsurableDeposits * 0.2m, 0)
            });
        }

        return results;
    }

    private static void ApplySecondRoundEffects(
        List<StressTestEntityResult> entityResults, List<StressTestContagionResult> contagionResults)
    {
        foreach (var contagion in contagionResults)
        {
            foreach (var exposure in contagion.ExposedEntities)
            {
                var entity = entityResults.FirstOrDefault(e => e.InstitutionId == exposure.EntityId);
                if (entity is null) continue;

                // Second-round impact: reduce CAR proportional to estimated loss / total assets
                if (entity.TotalAssets > 0)
                {
                    var lossRatio = exposure.EstimatedLoss / entity.TotalAssets;
                    entity.PostStressCar = Math.Max(0, entity.PostStressCar - lossRatio * 100m * 0.5m);
                    entity.PostStressCar = Math.Round(entity.PostStressCar, 2);

                    // Deposit flight from contagion
                    entity.PostStressLcr = Math.Max(0, entity.PostStressLcr - 5m);
                    entity.PostStressLcr = Math.Round(entity.PostStressLcr, 2);
                }

                // Re-evaluate breaches after second-round
                entity.CarBreach = entity.PostStressCar < 10m;
                entity.LcrBreach = entity.PostStressLcr < 100m;
                entity.SolvencyBreach = entity.CarBreach && entity.NplBreach;

                exposure.SecondRoundBreach = entity.CarBreach;
            }
        }
    }

    private static StressTestSectorAggregation ComputeAggregation(List<StressTestEntityResult> results)
    {
        if (results.Count == 0)
        {
            return new StressTestSectorAggregation();
        }

        var carBreachCount = results.Count(e => e.CarBreach);
        var lcrBreachCount = results.Count(e => e.LcrBreach);
        var nplBreachCount = results.Count(e => e.NplBreach);
        var solvencyBreachCount = results.Count(e => e.SolvencyBreach);

        var ndicAtRisk = results
            .Where(e => e.PostStressCar < 8m)
            .Sum(e => e.InsurableDeposits);

        // CAR distribution buckets (pre vs post)
        var carBuckets = new[] { "< 5%", "5-10%", "10-15%", "15-20%", ">= 20%" };
        Func<decimal, int> bucketIndex = car =>
            car < 5m ? 0 : car < 10m ? 1 : car < 15m ? 2 : car < 20m ? 3 : 4;

        var preDistribution = carBuckets.Select((label, i) => new DistributionBucket
        {
            Label = label,
            Count = results.Count(e => bucketIndex(e.PreStressCar) == i)
        }).ToList();

        var postDistribution = carBuckets.Select((label, i) => new DistributionBucket
        {
            Label = label,
            Count = results.Count(e => bucketIndex(e.PostStressCar) == i)
        }).ToList();

        return new StressTestSectorAggregation
        {
            TotalEntitiesTested = results.Count,
            CarBreachCount = carBreachCount,
            LcrBreachCount = lcrBreachCount,
            NplBreachCount = nplBreachCount,
            SolvencyBreachCount = solvencyBreachCount,
            NdicInsurableDepositsAtRisk = Math.Round(ndicAtRisk, 0),
            CarDistribution = new PrePostDistribution
            {
                PreStress = preDistribution,
                PostStress = postDistribution
            },
            PreStressAverageCar = Math.Round(results.Average(e => e.PreStressCar), 2),
            PostStressAverageCar = Math.Round(results.Average(e => e.PostStressCar), 2),
            PreStressAverageNpl = Math.Round(results.Average(e => e.PreStressNpl), 2),
            PostStressAverageNpl = Math.Round(results.Average(e => e.PostStressNpl), 2),
            PreStressAverageLcr = Math.Round(results.Average(e => e.PreStressLcr), 2),
            PostStressAverageLcr = Math.Round(results.Average(e => e.PostStressLcr), 2)
        };
    }

    private static (SectorResilienceRating Rating, string Rationale) DetermineResilienceRating(
        StressTestSectorAggregation agg)
    {
        if (agg.TotalEntitiesTested == 0)
        {
            return (SectorResilienceRating.Green, "No entities to test.");
        }

        var carBreachPct = 100m * agg.CarBreachCount / agg.TotalEntitiesTested;
        var solvencyBreachPct = 100m * agg.SolvencyBreachCount / agg.TotalEntitiesTested;
        var ndicRatio = agg.NdicExposureRatio;

        if (solvencyBreachPct > 20 || carBreachPct > 40 || ndicRatio > 80)
        {
            return (SectorResilienceRating.Red,
                $"Critical: {agg.SolvencyBreachCount} entities face solvency risk ({solvencyBreachPct:0.#}%), " +
                $"{agg.CarBreachCount} breach CAR minimum ({carBreachPct:0.#}%). " +
                $"NDIC exposure ratio: {ndicRatio}%. Immediate macro-prudential intervention required.");
        }

        if (solvencyBreachPct > 5 || carBreachPct > 15 || ndicRatio > 40)
        {
            return (SectorResilienceRating.Amber,
                $"Warning: {agg.CarBreachCount} entities breach CAR minimum ({carBreachPct:0.#}%), " +
                $"{agg.SolvencyBreachCount} face solvency risk. " +
                $"NDIC exposure ratio: {ndicRatio}%. Enhanced monitoring and targeted intervention recommended.");
        }

        return (SectorResilienceRating.Green,
            $"Resilient: Only {agg.CarBreachCount} of {agg.TotalEntitiesTested} entities breach CAR minimum ({carBreachPct:0.#}%). " +
            $"NDIC exposure ratio: {ndicRatio}%. Sector can absorb the shock.");
    }

    private static List<string> GenerateRecommendations(
        StressTestSectorAggregation agg,
        List<StressTestContagionResult> contagion,
        StressTestShockParameters parameters)
    {
        var recs = new List<string>();

        if (agg.CarBreachCount > 0)
        {
            recs.Add($"Request capital restoration plans from {agg.CarBreachCount} entities breaching the 10% CAR minimum.");
        }

        if (agg.LcrBreachCount > 0)
        {
            recs.Add($"Issue liquidity monitoring directives to {agg.LcrBreachCount} entities breaching LCR 100% minimum.");
        }

        if (agg.SolvencyBreachCount > 0)
        {
            recs.Add($"Activate Prompt Corrective Action (PCA) for {agg.SolvencyBreachCount} entities facing dual CAR + NPL stress.");
        }

        if (agg.NdicExposureRatio > 50)
        {
            recs.Add($"NDIC fund capacity may be insufficient — insurable deposits at risk represent {agg.NdicExposureRatio}% of NDIC fund. Consider contingency funding arrangements.");
        }

        if (contagion.Count > 0)
        {
            var totalSecondRound = contagion.SelectMany(c => c.ExposedEntities).Count(e => e.SecondRoundBreach);
            if (totalSecondRound > 0)
            {
                recs.Add($"Contagion risk identified: {totalSecondRound} entities may experience second-round breaches. Consider interbank exposure limits.");
            }
        }

        if (parameters.FxDepreciationPct > 15)
        {
            recs.Add("Tighten net open position limits for FX exposures given severe depreciation scenario.");
        }

        if (parameters.MoratoriaFlag)
        {
            recs.Add("If moratoria are implemented, mandate enhanced provisioning and forward-looking expected credit loss (ECL) models.");
        }

        if (agg.PostStressAverageNpl > 10)
        {
            recs.Add($"Sector-wide NPL average rises to {agg.PostStressAverageNpl}% under stress. Commission targeted asset quality reviews.");
        }

        if (recs.Count == 0)
        {
            recs.Add("No immediate macro-prudential actions required. Continue routine monitoring.");
        }

        return recs;
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

    // ── PDF Helpers ─────────────────────────────────────────────────

    private static void RenderKpiCell(RowDescriptor row, string label, string value, string color)
    {
        row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Column(col =>
        {
            col.Item().Text(label).FontSize(7).FontColor(Colors.Grey.Medium);
            col.Item().Text(value).FontSize(16).Bold().FontColor(color);
        });
    }

    private static void RenderParamRow(TableDescriptor table, string label, string value, string color)
    {
        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(label).FontSize(8).Bold();
        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(value).FontSize(8);
    }

    private static void RenderCompHeader(TableDescriptor table, string color)
    {
        table.Cell().Background(color).Padding(3).Text("Metric").FontColor(Colors.White).Bold().FontSize(8);
        table.Cell().Background(color).Padding(3).Text("Pre-Stress").FontColor(Colors.White).Bold().FontSize(8);
        table.Cell().Background(color).Padding(3).Text("Post-Stress").FontColor(Colors.White).Bold().FontSize(8);
        table.Cell().Background(color).Padding(3).Text("Change").FontColor(Colors.White).Bold().FontSize(8);
    }

    private static void RenderCompRow(TableDescriptor table, string metric, decimal pre, decimal post)
    {
        var delta = post - pre;
        var deltaColor = delta < 0 && metric.Contains("CAR") || delta < 0 && metric.Contains("LCR")
            ? "#dc2626"
            : delta > 0 && metric.Contains("NPL") ? "#dc2626" : "#059669";

        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(metric).FontSize(8).Bold();
        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text($"{pre:0.##}").FontSize(8);
        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text($"{post:0.##}").FontSize(8);
        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text($"{delta:+0.##;-0.##}").FontSize(8).FontColor(deltaColor);
    }

    // ── Internal Data Structures ────────────────────────────────────

    private sealed class EntityMetrics
    {
        public int InstitutionId { get; set; }
        public string InstitutionName { get; set; } = string.Empty;
        public string LicenceType { get; set; } = string.Empty;
        public decimal Car { get; set; }
        public decimal Npl { get; set; }
        public decimal Lcr { get; set; }
        public decimal TotalAssets { get; set; }
        public decimal Deposits { get; set; }
        public decimal FxExposure { get; set; }
        public decimal Tier1 { get; set; }
    }
}
