using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed partial class RegulatorResponseGenerator : IRegulatorResponseGenerator
{
    private static readonly IReadOnlyDictionary<string, MetricDefinition> MetricDefinitions =
        new Dictionary<string, MetricDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["carratio"] = new("carratio", "Capital Adequacy Ratio", "RG-07", ["car", "carratio", "capital adequacy ratio", "capital adequacy", "capital ratio"]),
            ["nplratio"] = new("nplratio", "NPL Ratio", "RG-07", ["npl", "nplratio", "non performing loans", "non-performing loans"]),
            ["liquidityratio"] = new("liquidityratio", "Liquidity Ratio", "RG-07", ["liquidity", "liquidityratio", "liquidity ratio", "lcr"]),
            ["loandepositratio"] = new("loandepositratio", "Loan to Deposit Ratio", "RG-07", ["ldr", "loan to deposit", "loan to deposit ratio"]),
            ["roa"] = new("roa", "Return on Assets", "RG-07", ["roa", "return on assets"]),
            ["totalassets"] = new("totalassets", "Total Assets", "RG-07", ["total assets", "assets"]),
            ["chs"] = new("chs", "Compliance Health Score", "RG-32", ["chs", "compliance health", "health score"]),
            ["qualityscore"] = new("qualityscore", "Anomaly Quality Score", "AI-01", ["quality score", "anomaly quality", "data quality"]),
            ["filingtimeliness"] = new("filingtimeliness", "Filing Timeliness", "RG-12", ["filing timeliness", "timeliness", "late filings"]),
            ["interventionprobability"] = new("interventionprobability", "Regulatory Risk Probability", "AI-04", ["regulatory risk", "intervention probability", "filing risk"])
        };

    private static readonly HashSet<string> ComplexLlmIntents = new(StringComparer.OrdinalIgnoreCase)
    {
        "ENTITY_PROFILE",
        "ENTITY_COMPARE",
        "EXAMINATION_BRIEF",
        "SYSTEMIC_DASHBOARD",
        "CONTAGION_QUERY",
        "CROSS_BORDER",
        "POLICY_IMPACT"
    };

    private static readonly HashSet<string> ConfidentialIntents = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEMIC_DASHBOARD",
        "CONTAGION_QUERY",
        "STRESS_SCENARIOS",
        "SANCTIONS_EXPOSURE",
        "CROSS_BORDER",
        "POLICY_IMPACT",
        "SUPERVISORY_ACTIONS"
    };

    private readonly IDbContextFactory<MetadataDbContext> _dbFactory;
    private readonly IRegulatorIntelligenceService _regulatorIntelligenceService;
    private readonly IComplianceHealthService _complianceHealthService;
    private readonly IAnomalyDetectionService _anomalyDetectionService;
    private readonly IForeSightService _foreSightService;
    private readonly IEarlyWarningService _earlyWarningService;
    private readonly ISystemicRiskService _systemicRiskService;
    private readonly ISectorAnalyticsService _sectorAnalyticsService;
    private readonly IStressTestService _stressTestService;
    private readonly IPanAfricanDashboardService _panAfricanDashboardService;
    private readonly IPolicyScenarioService _policyScenarioService;
    private readonly ILlmService _llmService;
    private readonly ILogger<RegulatorResponseGenerator> _logger;

    public RegulatorResponseGenerator(
        IDbContextFactory<MetadataDbContext> dbFactory,
        IRegulatorIntelligenceService regulatorIntelligenceService,
        IComplianceHealthService complianceHealthService,
        IAnomalyDetectionService anomalyDetectionService,
        IForeSightService foreSightService,
        IEarlyWarningService earlyWarningService,
        ISystemicRiskService systemicRiskService,
        ISectorAnalyticsService sectorAnalyticsService,
        IStressTestService stressTestService,
        IPanAfricanDashboardService panAfricanDashboardService,
        IPolicyScenarioService policyScenarioService,
        ILlmService llmService,
        ILogger<RegulatorResponseGenerator> logger)
    {
        _dbFactory = dbFactory;
        _regulatorIntelligenceService = regulatorIntelligenceService;
        _complianceHealthService = complianceHealthService;
        _anomalyDetectionService = anomalyDetectionService;
        _foreSightService = foreSightService;
        _earlyWarningService = earlyWarningService;
        _systemicRiskService = systemicRiskService;
        _sectorAnalyticsService = sectorAnalyticsService;
        _stressTestService = stressTestService;
        _panAfricanDashboardService = panAfricanDashboardService;
        _policyScenarioService = policyScenarioService;
        _llmService = llmService;
        _logger = logger;
    }

    public async Task<RegulatorIqResponse> GenerateAsync(
        string originalQuery,
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalQuery);
        ArgumentNullException.ThrowIfNull(classifiedIntent);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            return classifiedIntent.IntentCode.ToUpperInvariant() switch
            {
                "HELP" => await GenerateHelpAsync(classifiedIntent, ct),
                "REGULATORY_LOOKUP" => await GenerateLookupAsync(originalQuery, classifiedIntent, ct),
                "ENTITY_PROFILE" => await GenerateEntityProfileAsync(originalQuery, classifiedIntent, context, ct),
                "ENTITY_COMPARE" => await GenerateEntityCompareAsync(originalQuery, classifiedIntent, context, ct),
                "SECTOR_SUMMARY" => await GenerateSectorSummaryAsync(classifiedIntent, context, ct),
                "SECTOR_AGGREGATE" => await GenerateSectorAggregateAsync(originalQuery, classifiedIntent, context, ct),
                "SECTOR_TREND" => await GenerateSectorTrendAsync(originalQuery, classifiedIntent, context, ct),
                "TOP_N_RANKING" => await GenerateTopRankingAsync(originalQuery, classifiedIntent, context, ct),
                "RISK_RANKING" => await GenerateRiskRankingAsync(classifiedIntent, context, ct),
                "CHS_RANKING" => await GenerateChsRankingAsync(classifiedIntent, context, ct),
                "CHS_ENTITY" => await GenerateChsEntityAsync(originalQuery, classifiedIntent, context, ct),
                "FILING_STATUS" => await GenerateFilingStatusAsync(classifiedIntent, context, ct),
                "FILING_DELINQUENCY" => await GenerateFilingDelinquencyAsync(classifiedIntent, context, ct),
                "EWI_STATUS" => await GenerateEwiStatusAsync(classifiedIntent, context, ct),
                "SYSTEMIC_DASHBOARD" => await GenerateSystemicDashboardAsync(originalQuery, classifiedIntent, context, ct),
                "CONTAGION_QUERY" => await GenerateContagionAsync(originalQuery, classifiedIntent, context, ct),
                "STRESS_SCENARIOS" => await GenerateStressScenarioAsync(originalQuery, classifiedIntent, context, ct),
                "SANCTIONS_EXPOSURE" => await GenerateSanctionsExposureAsync(classifiedIntent, context, ct),
                "EXAMINATION_BRIEF" => await GenerateExaminationBriefingAsync(originalQuery, classifiedIntent, context, ct),
                "SUPERVISORY_ACTIONS" => await GenerateSupervisoryActionsAsync(classifiedIntent, context, ct),
                "CROSS_BORDER" => await GenerateCrossBorderAsync(originalQuery, classifiedIntent, context, ct),
                "POLICY_IMPACT" => await GeneratePolicyImpactAsync(originalQuery, classifiedIntent, context, ct),
                "VALIDATION_HOTSPOT" => await GenerateValidationHotspotAsync(classifiedIntent, context, ct),
                "CURRENT_VALUE" => await GenerateCurrentValueAsync(classifiedIntent, context, ct),
                "TREND" => await GenerateEntityTrendAsync(classifiedIntent, context, ct),
                "COMPARISON_PEER" => await GeneratePeerComparisonAsync(classifiedIntent, context, ct),
                "COMPARISON_PERIOD" => await GenerateComparisonPeriodAsync(originalQuery, classifiedIntent, context, ct),
                "DEADLINE" => await GenerateDeadlineAsync(classifiedIntent, context, ct),
                "COMPLIANCE_STATUS" => await GenerateComplianceStatusAsync(originalQuery, classifiedIntent, context, ct),
                "ANOMALY_STATUS" => await GenerateAnomalyStatusAsync(originalQuery, classifiedIntent, context, ct),
                "SCENARIO" => await GenerateScenarioAsync(originalQuery, classifiedIntent, context, ct),
                "SEARCH" => await GenerateSearchAsync(originalQuery, classifiedIntent, context, ct),
                _ => BuildNoDataResponse(
                    classifiedIntent.IntentCode,
                    "I could not route that regulator query to a grounded data source.",
                    ResolveClassificationLevel(classifiedIntent.IntentCode),
                    classifiedIntent.Confidence)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate regulator response for intent {IntentCode}.", classifiedIntent.IntentCode);
            return new RegulatorIqResponse
            {
                AnswerText = "I could not generate a grounded regulator response for that query.",
                AnswerFormat = "text",
                ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
                ConfidenceLevel = "LOW",
                FollowUpSuggestions = BuildFollowUps(classifiedIntent.IntentCode)
            };
        }
    }

    private async Task<RegulatorIqResponse> GenerateHelpAsync(
        RegulatorIntentResult classifiedIntent,
        CancellationToken ct)
    {
        await Task.CompletedTask;
        return new RegulatorIqResponse
        {
            AnswerText = "You can ask for entity profiles, entity comparisons, sector aggregates, trends, CHS rankings, filing status, early warning flags, systemic risk, contagion, stress scenarios, sanctions exposure, policy simulations, cross-border views, and examination briefings.",
            AnswerFormat = "text",
            ClassificationLevel = "UNCLASSIFIED",
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence),
            FollowUpSuggestions = new List<string>
            {
                "Give me a full profile of Access Bank",
                "Compare GTBank vs Zenith on CAR and NPL",
                "Show sector NPL trend for commercial banks",
                "Generate an examination briefing for Wema Bank"
            }
        };
    }

    private async Task<RegulatorIqResponse> GenerateLookupAsync(
        string originalQuery,
        RegulatorIntentResult classifiedIntent,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var keyword = ExtractLookupKeyword(originalQuery);
        var articles = await db.KnowledgeBaseArticles
            .AsNoTracking()
            .Where(x => x.IsPublished
                        && (EF.Functions.Like(x.Title, $"%{keyword}%")
                            || EF.Functions.Like(x.Content, $"%{keyword}%")
                            || (x.Tags != null && EF.Functions.Like(x.Tags, $"%{keyword}%"))))
            .OrderBy(x => x.DisplayOrder)
            .ThenByDescending(x => x.CreatedAt)
            .Take(10)
            .ToListAsync(ct);

        if (articles.Count == 0)
        {
            return BuildNoDataResponse(
                classifiedIntent.IntentCode,
                $"I could not find published regulatory guidance matching \"{keyword}\".",
                "UNCLASSIFIED",
                classifiedIntent.Confidence);
        }

        var rows = articles.Select(x => new Dictionary<string, object?>
        {
            ["title"] = x.Title,
            ["category"] = x.Category,
            ["module_code"] = x.ModuleCode,
            ["summary"] = Truncate(x.Content, 240)
        }).ToList();

        return new RegulatorIqResponse
        {
            AnswerText = $"I found {articles.Count} regulatory reference item(s) matching \"{keyword}\". The strongest match is {articles[0].Title}.",
            AnswerFormat = "table",
            StructuredData = BuildTable(rows),
            ClassificationLevel = "UNCLASSIFIED",
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, articles.Count),
            DataSourcesUsed = new List<string> { "KNOWLEDGE" },
            Citations = articles.Take(3).Select(x => new DataCitation
            {
                SourceType = "Knowledge Base",
                SourceModule = x.ModuleCode ?? "KNOWLEDGE",
                Summary = x.Title
            }).ToList(),
            FollowUpSuggestions = BuildFollowUps(classifiedIntent.IntentCode)
        };
    }

    private async Task<RegulatorIqResponse> GenerateEntityProfileAsync(
        string originalQuery,
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        var tenantId = ResolveSingleEntityId(classifiedIntent, context);
        if (tenantId is null)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "I could not determine which institution profile to retrieve.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var profile = await _regulatorIntelligenceService.GetEntityProfileAsync(tenantId.Value, context.RegulatorCode, ct);
        var response = new RegulatorIqResponse
        {
            AnswerFormat = "profile",
            StructuredData = new RegulatorProfileData { Profile = profile },
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode, profile.DataSourcesUsed),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence),
            DataSourcesUsed = profile.DataSourcesUsed.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
            EntitiesAccessed = new List<Guid> { profile.TenantId },
            Citations = BuildProfileCitations(profile),
            Flags = BuildProfileFlags(profile),
            FollowUpSuggestions = new List<string>
            {
                $"Compare {profile.InstitutionName} to peers on CAR and NPL",
                $"Show {profile.InstitutionName} filing status",
                $"Generate an examination briefing for {profile.InstitutionName}"
            }
        };

        response.AnswerText = BuildEntityProfileAnswer(profile, response.Flags);
        response.AnswerText = await TryFormatWithLlmAsync(originalQuery, classifiedIntent.IntentCode, response.AnswerText, profile, response, ct);
        return response;
    }

    private async Task<RegulatorIqResponse> GenerateEntityCompareAsync(
        string originalQuery,
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        var tenantIds = ResolveEntityIds(classifiedIntent, context);
        if (tenantIds.Count < 2)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "I need at least two resolved institutions to build a comparison matrix.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var metricCodes = ResolveRequestedMetricCodes(originalQuery, classifiedIntent.FieldCode);
        var snapshots = await LoadLatestSnapshotsAsync(db, context.RegulatorCode, tenantIds, classifiedIntent.PeriodCode, ct);

        if (snapshots.Count == 0)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "I could not find accepted submissions for the requested institutions.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var entityNames = snapshots.Select(x => x.InstitutionName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var comparison = new RegulatorComparisonData
        {
            PeriodCode = classifiedIntent.PeriodCode ?? snapshots.MaxBy(x => x.ReportingDate)?.PeriodCode,
            EntityNames = entityNames
        };

        foreach (var metricCode in metricCodes)
        {
            var row = new RegulatorComparisonRow
            {
                MetricCode = metricCode,
                MetricLabel = GetMetricLabel(metricCode),
                SourceModules = new List<string> { GetMetricSource(metricCode) }
            };

            foreach (var snapshot in snapshots.OrderBy(x => x.InstitutionName))
            {
                row.Values[snapshot.InstitutionName] = GetSnapshotMetricValue(snapshot, metricCode);
            }

            comparison.Rows.Add(row);
        }

        var response = new RegulatorIqResponse
        {
            AnswerFormat = "comparison",
            StructuredData = comparison,
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, comparison.Rows.Count),
            DataSourcesUsed = comparison.Rows
                .SelectMany(x => x.SourceModules)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList(),
            EntitiesAccessed = snapshots.Select(x => x.TenantId).Distinct().ToList(),
            Citations = snapshots.Select(x => new DataCitation
            {
                SourceType = "Accepted Return",
                SourceModule = x.ModuleCode,
                SourcePeriod = x.PeriodCode,
                InstitutionName = x.InstitutionName
            }).DistinctBy(x => $"{x.SourceModule}|{x.SourcePeriod}|{x.InstitutionName}").ToList(),
            Flags = BuildComparisonFlags(comparison),
            FollowUpSuggestions = entityNames.Take(2).Count() == 2
                ? new List<string>
                {
                    $"Show a full profile of {entityNames[0]}",
                    $"Show a full profile of {entityNames[1]}",
                    "Rank all institutions by CAR"
                }
                : BuildFollowUps(classifiedIntent.IntentCode)
        };

        response.AnswerText = BuildComparisonAnswer(comparison);
        response.AnswerText = await TryFormatWithLlmAsync(originalQuery, classifiedIntent.IntentCode, response.AnswerText, comparison, response, ct);
        return response;
    }

    private async Task<RegulatorIqResponse> GenerateSectorSummaryAsync(
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        var sectorSummary = await _regulatorIntelligenceService.GetSectorSummaryAsync(
            context.RegulatorCode,
            classifiedIntent.LicenceCategory,
            classifiedIntent.PeriodCode,
            ct);

        var rows = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["period_code"] = sectorSummary.PeriodCode,
                ["licence_category"] = string.IsNullOrWhiteSpace(sectorSummary.LicenceCategory) ? "ALL" : sectorSummary.LicenceCategory,
                ["entity_count"] = sectorSummary.EntityCount,
                ["average_car_ratio"] = sectorSummary.AverageCarRatio,
                ["average_npl_ratio"] = sectorSummary.AverageNplRatio,
                ["average_liquidity_ratio"] = sectorSummary.AverageLiquidityRatio,
                ["average_chs_score"] = sectorSummary.AverageComplianceHealthScore,
                ["overdue_filing_count"] = sectorSummary.OverdueFilingCount,
                ["high_risk_entity_count"] = sectorSummary.HighRiskEntityCount
            }
        };

        return new RegulatorIqResponse
        {
            AnswerText = BuildSectorSummaryAnswer(sectorSummary),
            AnswerFormat = "table",
            StructuredData = BuildTable(rows),
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, sectorSummary.EntityCount),
            DataSourcesUsed = BuildDataSources("RG-07", sectorSummary.DataSourcesUsed),
            Citations = new List<DataCitation>
            {
                new()
                {
                    SourceType = "Sector Summary",
                    SourceModule = "RG-07",
                    SourcePeriod = sectorSummary.PeriodCode,
                    Summary = $"{sectorSummary.EntityCount} institution(s) in scope"
                }
            },
            FollowUpSuggestions = new List<string>
            {
                "Show sector NPL trend",
                "Rank institutions by compliance health score",
                "Show me the systemic risk dashboard"
            }
        };
    }

    private async Task<RegulatorIqResponse> GenerateSectorAggregateAsync(
        string originalQuery,
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var metricCode = classifiedIntent.FieldCode ?? ResolveRequestedMetricCodes(originalQuery, null).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(metricCode))
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "I could not determine which metric to aggregate across the sector.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var sectorSummary = await _regulatorIntelligenceService.GetSectorSummaryAsync(
            context.RegulatorCode,
            classifiedIntent.LicenceCategory,
            classifiedIntent.PeriodCode,
            ct);

        var snapshots = await LoadLatestSnapshotsAsync(
            db,
            context.RegulatorCode,
            null,
            classifiedIntent.PeriodCode ?? sectorSummary.PeriodCode,
            ct,
            classifiedIntent.LicenceCategory);

        var values = snapshots
            .Select(x => GetSnapshotMetricValue(x, metricCode))
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .OrderBy(x => x)
            .ToList();

        if (values.Count == 0)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, $"I could not find sector-wide data for {GetMetricLabel(metricCode)}.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var rows = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["metric_code"] = metricCode,
                ["metric_label"] = GetMetricLabel(metricCode),
                ["period_code"] = classifiedIntent.PeriodCode ?? sectorSummary.PeriodCode,
                ["entity_count"] = values.Count,
                ["average"] = decimal.Round(values.Average(), 2),
                ["median"] = decimal.Round(RegulatorAnalyticsSupport.Median(values), 2),
                ["minimum"] = values.Min(),
                ["maximum"] = values.Max(),
                ["std_dev"] = decimal.Round(StandardDeviation(values), 2)
            }
        };

        var response = new RegulatorIqResponse
        {
            AnswerFormat = "table",
            StructuredData = BuildTable(rows),
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, values.Count),
            DataSourcesUsed = BuildDataSources("RG-07", sectorSummary.DataSourcesUsed),
            EntitiesAccessed = snapshots.Select(x => x.TenantId).Distinct().ToList(),
            Citations = new List<DataCitation>
            {
                new()
                {
                    SourceType = "Sector Aggregate",
                    SourceModule = GetMetricSource(metricCode),
                    SourceField = metricCode,
                    SourcePeriod = classifiedIntent.PeriodCode ?? sectorSummary.PeriodCode,
                    Summary = $"{values.Count} institution(s)"
                }
            },
            FollowUpSuggestions = new List<string>
            {
                $"Show sector trend for {GetMetricLabel(metricCode)}",
                $"Rank institutions by {GetMetricLabel(metricCode)}",
                "Show systemic risk dashboard"
            }
        };

        response.AnswerText = $"Across {values.Count} institution(s), {GetMetricLabel(metricCode)} averages {FormatMetricValue(values.Average(), metricCode)} with a median of {FormatMetricValue(RegulatorAnalyticsSupport.Median(values), metricCode)}.";
        return response;
    }

    private async Task<RegulatorIqResponse> GenerateSectorTrendAsync(
        string originalQuery,
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var metricCode = classifiedIntent.FieldCode ?? ResolveRequestedMetricCodes(originalQuery, null).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(metricCode))
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "I could not determine which metric trend to calculate.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        if (string.Equals(metricCode, "nplratio", StringComparison.OrdinalIgnoreCase))
        {
            var trend = await _sectorAnalyticsService.GetNplTrend(context.RegulatorCode, 8, ct);
            var chart = new RegulatorChartData
            {
                ChartType = "line",
                Labels = trend.PeriodLabels,
                Series = new List<RegulatorChartSeries>
                {
                    new()
                    {
                        Name = "Average NPL Ratio",
                        Values = trend.AverageNplRatios.Select(x => (decimal?)x).ToList()
                    }
                }
            };

            return new RegulatorIqResponse
            {
                AnswerText = trend.PeriodLabels.Count > 1
                    ? $"Sector NPL ratio moved from {trend.AverageNplRatios[0]:N2}% in {trend.PeriodLabels[0]} to {trend.AverageNplRatios[^1]:N2}% in {trend.PeriodLabels[^1]}."
                    : "I generated the sector NPL trend series.",
                AnswerFormat = "chart_data",
                StructuredData = chart,
                ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
                ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, trend.PeriodLabels.Count),
                DataSourcesUsed = new List<string> { "RG-17" },
                Citations = new List<DataCitation>
                {
                    new()
                    {
                        SourceType = "Sector Trend",
                        SourceModule = "RG-17",
                        SourceField = metricCode,
                        Summary = "Sector analytics trend series"
                    }
                },
                FollowUpSuggestions = new List<string>
                {
                    "What is average NPL across the sector right now?",
                    "Rank institutions by NPL ratio",
                    "Show systemic risk dashboard"
                }
            };
        }

        var snapshots = await LoadSnapshotsAsync(
            db,
            context.RegulatorCode,
            null,
            classifiedIntent.LicenceCategory,
            ct);

        var series = snapshots
            .GroupBy(x => x.PeriodCode)
            .Select(g =>
            {
                var values = g.Select(x => GetSnapshotMetricValue(x, metricCode))
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();

                return new
                {
                    PeriodCode = g.Key,
                    ReportingDate = g.Max(x => x.ReportingDate),
                    Average = values.Count == 0 ? (decimal?)null : decimal.Round(values.Average(), 2)
                };
            })
            .Where(x => x.Average.HasValue)
            .OrderBy(x => x.ReportingDate)
            .TakeLast(ParseInt(classifiedIntent.ExtractedParameters, "limit", 8))
            .ToList();

        if (series.Count == 0)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, $"I could not find historical sector data for {GetMetricLabel(metricCode)}.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        return new RegulatorIqResponse
        {
            AnswerText = $"{GetMetricLabel(metricCode)} moved from {FormatMetricValue(series[0].Average, metricCode)} in {series[0].PeriodCode} to {FormatMetricValue(series[^1].Average, metricCode)} in {series[^1].PeriodCode}.",
            AnswerFormat = "chart_data",
            StructuredData = new RegulatorChartData
            {
                ChartType = "line",
                Labels = series.Select(x => x.PeriodCode).ToList(),
                Series = new List<RegulatorChartSeries>
                {
                    new()
                    {
                        Name = $"Average {GetMetricLabel(metricCode)}",
                        Values = series.Select(x => x.Average).ToList()
                    }
                }
            },
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, series.Count),
            DataSourcesUsed = new List<string> { GetMetricSource(metricCode) },
            Citations = new List<DataCitation>
            {
                new()
                {
                    SourceType = "Sector Trend",
                    SourceModule = GetMetricSource(metricCode),
                    SourceField = metricCode
                }
            },
            FollowUpSuggestions = new List<string>
            {
                $"What is the latest sector average for {GetMetricLabel(metricCode)}?",
                $"Rank institutions by {GetMetricLabel(metricCode)}",
                "Show filing delinquency ranking"
            }
        };
    }

    private async Task<RegulatorIqResponse> GenerateTopRankingAsync(
        string originalQuery,
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        var metricCode = classifiedIntent.FieldCode ?? ResolveRequestedMetricCodes(originalQuery, null).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(metricCode))
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "I could not determine which metric to rank institutions by.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var ranking = await _regulatorIntelligenceService.RankEntitiesByMetricAsync(
            metricCode,
            context.RegulatorCode,
            classifiedIntent.LicenceCategory,
            classifiedIntent.PeriodCode,
            ParseInt(classifiedIntent.ExtractedParameters, "limit", 10),
            string.Equals(classifiedIntent.ExtractedParameters.GetValueOrDefault("direction"), "ASC", StringComparison.OrdinalIgnoreCase),
            ct);

        if (ranking.Count == 0)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, $"I could not find accepted submissions for {GetMetricLabel(metricCode)}.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var rankingData = new RegulatorRankingData
        {
            MetricCode = metricCode,
            MetricLabel = GetMetricLabel(metricCode),
            Items = ranking.Select(x => new RegulatorRankingItem
            {
                Rank = x.Rank,
                TenantId = x.TenantId,
                InstitutionName = x.InstitutionName,
                LicenceCategory = x.LicenceCategory,
                Value = x.MetricValue,
                RiskBand = ResolveMetricRiskBand(metricCode, x.MetricValue),
                Source = "RG-07"
            }).ToList()
        };

        var response = new RegulatorIqResponse
        {
            AnswerText = BuildRankingAnswer(rankingData, false),
            AnswerFormat = "ranking",
            StructuredData = rankingData,
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, ranking.Count),
            DataSourcesUsed = new List<string> { "RG-07" },
            EntitiesAccessed = ranking.Select(x => x.TenantId).Distinct().ToList(),
            Citations = ranking.Take(5).Select(x => new DataCitation
            {
                SourceType = "Accepted Return",
                SourceModule = "RG-07",
                SourceField = metricCode,
                SourcePeriod = x.PeriodCode,
                InstitutionName = x.InstitutionName
            }).ToList(),
            Flags = rankingData.Items.SelectMany(x => BuildMetricFlags(x.InstitutionName, metricCode, x.Value, "RG-07")).ToList(),
            FollowUpSuggestions = new List<string>
            {
                $"Show sector aggregate for {GetMetricLabel(metricCode)}",
                $"Show trend for {GetMetricLabel(metricCode)} across the sector",
                $"Give me a full profile of {ranking[0].InstitutionName}"
            }
        };

        return response;
    }

    private async Task<RegulatorIqResponse> GenerateRiskRankingAsync(
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var query = db.AnomalyReports
            .AsNoTracking()
            .Where(x => x.RegulatorCode == context.RegulatorCode);

        if (!string.IsNullOrWhiteSpace(classifiedIntent.PeriodCode))
        {
            query = query.Where(x => x.PeriodCode == classifiedIntent.PeriodCode);
        }

        var reports = await query
            .OrderBy(x => x.OverallQualityScore)
            .ThenByDescending(x => x.AlertCount)
            .Take(ParseInt(classifiedIntent.ExtractedParameters, "limit", 10))
            .ToListAsync(ct);

        if (reports.Count == 0)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "I could not find anomaly reports to rank.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var licenceLookup = await db.Institutions
            .AsNoTracking()
            .Where(x => reports.Select(r => r.InstitutionId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.LicenseType ?? string.Empty, ct);

        var ranking = new RegulatorRankingData
        {
            MetricCode = "qualityscore",
            MetricLabel = "Anomaly Quality Score",
            Items = reports.Select((x, index) => new RegulatorRankingItem
            {
                Rank = index + 1,
                InstitutionName = x.InstitutionName,
                LicenceCategory = licenceLookup.GetValueOrDefault(x.InstitutionId, "UNKNOWN"),
                Value = x.OverallQualityScore,
                RiskBand = x.TrafficLight,
                Source = "AI-01"
            }).ToList()
        };

        return new RegulatorIqResponse
        {
            AnswerText = BuildRankingAnswer(ranking, true),
            AnswerFormat = "ranking",
            StructuredData = ranking,
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, reports.Count),
            DataSourcesUsed = new List<string> { "AI-01" },
            EntitiesAccessed = await ResolveTenantIdsByInstitutionNamesAsync(db, reports.Select(x => x.InstitutionName).ToList(), ct),
            Citations = reports.Take(5).Select(x => new DataCitation
            {
                SourceType = "Anomaly Report",
                SourceModule = "AI-01",
                SourcePeriod = x.PeriodCode,
                InstitutionName = x.InstitutionName,
                Summary = x.NarrativeSummary
            }).ToList(),
            Flags = reports.Where(x => string.Equals(x.TrafficLight, "RED", StringComparison.OrdinalIgnoreCase))
                .Select(x => new IntelligenceFlag
                {
                    FlagType = "WARNING",
                    Message = $"{x.InstitutionName} has a RED anomaly profile with {x.AlertCount} alert(s).",
                    Source = "AI-01"
                })
                .ToList(),
            FollowUpSuggestions = new List<string>
            {
                "Show validation hotspots across institutions",
                $"Give me a full profile of {reports[0].InstitutionName}",
                "Show filing delinquency ranking"
            }
        };
    }

    private async Task<RegulatorIqResponse> GenerateChsRankingAsync(
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        var heatmap = await _complianceHealthService.GetSectorHeatmap(context.RegulatorCode, ct);
        var filtered = heatmap
            .Where(x => MatchesLicenceCategory(x.LicenceType, classifiedIntent.LicenceCategory))
            .OrderByDescending(x => x.OverallScore)
            .ToList();

        if (filtered.Count == 0)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "I could not find compliance health snapshots to rank.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var items = filtered
            .Take(ParseInt(classifiedIntent.ExtractedParameters, "limit", 10))
            .Select((x, index) => new RegulatorRankingItem
            {
                Rank = index + 1,
                TenantId = x.TenantId,
                InstitutionName = x.InstitutionName,
                LicenceCategory = x.LicenceType,
                Value = x.OverallScore,
                RiskBand = ResolveMetricRiskBand("chs", x.OverallScore),
                Source = "RG-32"
            })
            .ToList();

        return new RegulatorIqResponse
        {
            AnswerText = BuildRankingAnswer(new RegulatorRankingData { MetricCode = "chs", MetricLabel = "Compliance Health Score", Items = items }, false),
            AnswerFormat = "ranking",
            StructuredData = new RegulatorRankingData
            {
                MetricCode = "chs",
                MetricLabel = "Compliance Health Score",
                Items = items
            },
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, items.Count),
            DataSourcesUsed = new List<string> { "RG-32" },
            EntitiesAccessed = items.Where(x => x.TenantId.HasValue).Select(x => x.TenantId!.Value).ToList(),
            Citations = items.Take(5).Select(x => new DataCitation
            {
                SourceType = "Compliance Health Snapshot",
                SourceModule = "RG-32",
                InstitutionName = x.InstitutionName
            }).ToList(),
            Flags = items.Where(x => x.Value.HasValue && x.Value.Value < 70m)
                .Select(x => new IntelligenceFlag
                {
                    FlagType = "CONCERN",
                    Message = $"{x.InstitutionName} has a compliance health score below 70.",
                    Source = "RG-32"
                }).ToList(),
            FollowUpSuggestions = new List<string>
            {
                $"Show CHS breakdown for {items[0].InstitutionName}",
                "Rank institutions by filing timeliness",
                "Show sector average CHS score"
            }
        };
    }

    private async Task<RegulatorIqResponse> GenerateChsEntityAsync(
        string originalQuery,
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        var tenantId = ResolveSingleEntityId(classifiedIntent, context);
        if (tenantId is null)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "I could not determine which institution's compliance health score to retrieve.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var dashboard = await _complianceHealthService.GetDashboard(tenantId.Value, ct);
        var response = new RegulatorIqResponse
        {
            AnswerFormat = "table",
            StructuredData = dashboard,
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence),
            DataSourcesUsed = new List<string> { "RG-32" },
            EntitiesAccessed = new List<Guid> { tenantId.Value },
            Citations = new List<DataCitation>
            {
                new()
                {
                    SourceType = "Compliance Health Dashboard",
                    SourceModule = "RG-32",
                    SourcePeriod = dashboard.Current.PeriodLabel,
                    InstitutionName = dashboard.Current.TenantName
                }
            },
            Flags = BuildChsFlags(dashboard.Current),
            FollowUpSuggestions = new List<string>
            {
                $"Give me a full profile of {dashboard.Current.TenantName}",
                $"Show {dashboard.Current.TenantName} anomalies",
                $"Show {dashboard.Current.TenantName} filing status"
            }
        };

        response.AnswerText = $"{dashboard.Current.TenantName} has a Compliance Health Score of {dashboard.Current.OverallScore:N1} ({dashboard.Current.Rating}) for {dashboard.Current.PeriodLabel}.";
        response.AnswerText = await TryFormatWithLlmAsync(originalQuery, classifiedIntent.IntentCode, response.AnswerText, dashboard, response, ct);
        return response;
    }

    private async Task<RegulatorIqResponse> GenerateFilingStatusAsync(
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entityIds = ResolveEntityIds(classifiedIntent, context);
        var statusRows = await LoadFilingStatusRowsAsync(db, context.RegulatorCode, classifiedIntent.LicenceCategory, ct);
        if (entityIds.Count > 0)
        {
            statusRows = statusRows
                .Where(x => entityIds.Contains(x.TenantId))
                .ToList();
        }

        if (statusRows.Count == 0)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "I could not find filing calendar records for the requested scope.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var tableRows = statusRows.Select(x => new Dictionary<string, object?>
        {
            ["tenant_id"] = x.TenantId,
            ["institution_name"] = x.InstitutionName,
            ["licence_type"] = x.LicenceType,
            ["module_code"] = x.ModuleCode,
            ["period_code"] = x.PeriodCode,
            ["deadline_date"] = x.DeadlineDate,
            ["submitted_at"] = x.SubmittedAt,
            ["status"] = x.Status
        }).ToList();

        var overdueCount = statusRows.Count(x => string.Equals(x.Status, "OVERDUE", StringComparison.OrdinalIgnoreCase));
        var scopeLabel = entityIds.Count == 1
            ? statusRows[0].InstitutionName
            : "the selected institutions";
        return new RegulatorIqResponse
        {
            AnswerText = overdueCount > 0
                ? $"{overdueCount} filing obligation(s) are currently overdue for {scopeLabel}."
                : $"There are no currently overdue filing obligations for {scopeLabel}.",
            AnswerFormat = "table",
            StructuredData = BuildTable(tableRows),
            ClassificationLevel = "RESTRICTED",
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, statusRows.Count),
            DataSourcesUsed = new List<string> { "RG-12" },
            EntitiesAccessed = statusRows
                .Select(x => x.TenantId)
                .Distinct()
                .ToList(),
            Citations = statusRows.Take(5).Select(x => new DataCitation
            {
                SourceType = "Filing SLA",
                SourceModule = "RG-12",
                SourcePeriod = x.PeriodCode,
                InstitutionName = x.InstitutionName
            }).ToList(),
            Flags = statusRows
                .Where(x => string.Equals(x.Status, "OVERDUE", StringComparison.OrdinalIgnoreCase))
                .Take(10)
                .Select(x => new IntelligenceFlag
                {
                    FlagType = "ACTION_REQUIRED",
                    Message = $"{x.InstitutionName} has an overdue filing for {x.ModuleCode} {x.PeriodCode}.",
                    Source = "RG-12"
                }).ToList(),
            FollowUpSuggestions = new List<string>
            {
                "Rank institutions by filing timeliness",
                "Show compliance health ranking",
                "Show validation hotspots across institutions"
            }
        };
    }

    private async Task<RegulatorIqResponse> GenerateFilingDelinquencyAsync(
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var periodCode = classifiedIntent.PeriodCode ?? await ResolveLatestPeriodCodeAsync(db, context.RegulatorCode, ct);
        if (string.IsNullOrWhiteSpace(periodCode))
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "I could not determine which reporting period to use for filing timeliness.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var timeliness = await _sectorAnalyticsService.GetFilingTimeliness(context.RegulatorCode, periodCode, ct);
        var ranking = new RegulatorRankingData
        {
            MetricCode = "filingtimeliness",
            MetricLabel = "Late Filings",
            Items = timeliness.Institutions
                .OrderByDescending(x => x.Late)
                .ThenBy(x => x.OnTime)
                .Take(ParseInt(classifiedIntent.ExtractedParameters, "limit", 10))
                .Select((x, index) => new RegulatorRankingItem
                {
                    Rank = index + 1,
                    InstitutionName = x.InstitutionName,
                    Value = x.Late,
                    RiskBand = x.Late == 0 ? "GREEN" : x.Late == 1 ? "AMBER" : "RED",
                    Source = "RG-12"
                }).ToList()
        };

        return new RegulatorIqResponse
        {
            AnswerText = ranking.Items.Count > 0
                ? $"{ranking.Items[0].InstitutionName} has the weakest filing timeliness in {periodCode} with {ranking.Items[0].Value:N0} late filing(s)."
                : "No filing delinquency records were found.",
            AnswerFormat = "ranking",
            StructuredData = ranking,
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, ranking.Items.Count),
            DataSourcesUsed = new List<string> { "RG-12" },
            Citations = ranking.Items.Take(5).Select(x => new DataCitation
            {
                SourceType = "Filing Timeliness",
                SourceModule = "RG-12",
                SourcePeriod = periodCode,
                InstitutionName = x.InstitutionName
            }).ToList(),
            Flags = ranking.Items.Where(x => x.Value.GetValueOrDefault() > 0m)
                .Select(x => new IntelligenceFlag
                {
                    FlagType = "WARNING",
                    Message = $"{x.InstitutionName} recorded {x.Value:N0} late filing(s) in {periodCode}.",
                    Source = "RG-12"
                }).ToList(),
            FollowUpSuggestions = new List<string>
            {
                "Which banks have overdue returns?",
                "Rank institutions by compliance health score",
                "Show systemic risk dashboard"
            }
        };
    }

    private async Task<RegulatorIqResponse> GenerateEwiStatusAsync(
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var flags = await _earlyWarningService.ComputeFlags(context.RegulatorCode, ct);
        var institutions = await db.Institutions
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToDictionaryAsync(x => x.Id, ct);

        var filtered = flags
            .Where(x => institutions.ContainsKey(x.InstitutionId))
            .Where(x => classifiedIntent.ResolvedEntityIds.Count == 0
                        || institutions.Values.Any(i => i.Id == x.InstitutionId && classifiedIntent.ResolvedEntityIds.Contains(i.TenantId)))
            .Where(x => string.IsNullOrWhiteSpace(classifiedIntent.LicenceCategory)
                        || MatchesLicenceCategory(institutions[x.InstitutionId].LicenseType, classifiedIntent.LicenceCategory))
            .OrderByDescending(x => SeverityScore(x.Severity))
            .ThenByDescending(x => x.TriggeredAt)
            .ToList();

        if (filtered.Count == 0)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "There are no active early warning flags for the selected scope.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var rows = filtered.Select(x => new Dictionary<string, object?>
        {
            ["institution_name"] = x.InstitutionName,
            ["flag_code"] = x.FlagCode,
            ["severity"] = x.Severity.ToString().ToUpperInvariant(),
            ["message"] = x.Message,
            ["triggered_at"] = x.TriggeredAt
        }).ToList();

        return new RegulatorIqResponse
        {
            AnswerText = $"{filtered.Count} active early warning flag(s) were found. The highest-severity item is {filtered[0].FlagCode} for {filtered[0].InstitutionName}.",
            AnswerFormat = "table",
            StructuredData = BuildTable(rows),
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, filtered.Count),
            DataSourcesUsed = new List<string> { "RG-36" },
            Citations = filtered.Take(5).Select(x => new DataCitation
            {
                SourceType = "Early Warning Flag",
                SourceModule = "RG-36",
                InstitutionName = x.InstitutionName,
                Summary = x.FlagCode
            }).ToList(),
            Flags = filtered.Take(10).Select(x => new IntelligenceFlag
            {
                FlagType = x.Severity == EarlyWarningSeverity.Red ? "ACTION_REQUIRED" : "WARNING",
                Message = $"{x.InstitutionName}: {x.Message}",
                Source = "RG-36"
            }).ToList(),
            FollowUpSuggestions = new List<string>
            {
                "Show the systemic risk dashboard",
                $"Give me a full profile of {filtered[0].InstitutionName}",
                "Show supervisory actions"
            }
        };
    }

    private async Task<RegulatorIqResponse> GenerateSystemicDashboardAsync(
        string originalQuery,
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        var dashboard = await _systemicRiskService.GetDashboard(context.RegulatorCode, ct);
        var response = new RegulatorIqResponse
        {
            AnswerFormat = "chart_data",
            StructuredData = dashboard,
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, dashboard.Scores.Count),
            DataSourcesUsed = new List<string> { "RG-36" },
            Citations = new List<DataCitation>
            {
                new()
                {
                    SourceType = "Systemic Risk Dashboard",
                    SourceModule = "RG-36",
                    Summary = "System-wide dashboard"
                }
            },
            Flags = BuildSystemicFlags(dashboard),
            FollowUpSuggestions = new List<string>
            {
                "What happens if Access Bank fails?",
                "List available stress scenarios",
                "Show open supervisory actions"
            }
        };

        response.AnswerText = $"The systemic dashboard currently shows {dashboard.Summary.RedCount} red-rated institution(s), {dashboard.Summary.AmberCount} amber-rated institution(s), and a systemic risk index of {dashboard.Summary.SystemicRiskIndex:N2}.";
        response.AnswerText = await TryFormatWithLlmAsync(originalQuery, classifiedIntent.IntentCode, response.AnswerText, dashboard, response, ct);
        return response;
    }

    private async Task<RegulatorIqResponse> GenerateContagionAsync(
        string originalQuery,
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        var analysis = await _systemicRiskService.AnalyzeContagion(context.RegulatorCode, ct);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var resolvedNames = await ResolveInstitutionNamesAsync(db, ResolveEntityIds(classifiedIntent, context), ct);
        var filteredLinks = resolvedNames.Count == 0
            ? analysis.Links
            : analysis.Links.Where(x =>
                    resolvedNames.Any(name => string.Equals(name, x.SourceName, StringComparison.OrdinalIgnoreCase)
                                           || string.Equals(name, x.TargetName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

        if (filteredLinks.Count == 0)
        {
            filteredLinks = analysis.Links.Take(10).ToList();
        }

        return new RegulatorIqResponse
        {
            AnswerText = filteredLinks.Count > 0
                ? $"The contagion network shows {analysis.ClusterCount} high-risk cluster(s). The strongest observed linkage is {filteredLinks[0].SourceName} to {filteredLinks[0].TargetName} at {filteredLinks[0].CorrelationStrength:N2}."
                : "I could not find contagion links for the selected scope.",
            AnswerFormat = "chart_data",
            StructuredData = new
            {
                analysis.ClusterCount,
                analysis.HighRiskClusters,
                Links = filteredLinks
            },
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, filteredLinks.Count),
            DataSourcesUsed = new List<string> { "RG-36" },
            Citations = filteredLinks.Take(5).Select(x => new DataCitation
            {
                SourceType = "Contagion Link",
                SourceModule = "RG-36",
                InstitutionName = $"{x.SourceName} -> {x.TargetName}",
                Summary = $"{x.CorrelationStrength:N2}"
            }).ToList(),
            FollowUpSuggestions = new List<string>
            {
                "Show the systemic risk dashboard",
                "List available stress scenarios",
                "Generate an examination briefing for Access Bank"
            }
        };
    }

    private async Task<RegulatorIqResponse> GenerateStressScenarioAsync(
        string originalQuery,
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        var scenarios = _stressTestService.GetAvailableScenarios();
        var scenario = TryResolveScenario(originalQuery, scenarios);
        if (scenario is not null && Regex.IsMatch(originalQuery, @"\b(run|execute)\b", RegexOptions.IgnoreCase))
        {
            try
            {
                var report = await _stressTestService.RunStressTestAsync(
                    context.RegulatorCode,
                    new StressTestRequest
                    {
                        ScenarioType = scenario.Type,
                        TargetLicenceTypes = string.IsNullOrWhiteSpace(classifiedIntent.LicenceCategory)
                            ? null
                            : new List<string> { classifiedIntent.LicenceCategory! }
                    },
                    ct);

                return new RegulatorIqResponse
                {
                    AnswerText = $"{report.ScenarioName} was run for {report.EntityResults.Count} entity result(s). Sector resilience is {report.ResilienceRating}.",
                    AnswerFormat = "chart_data",
                    StructuredData = report,
                    ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
                    ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, report.EntityResults.Count),
                    DataSourcesUsed = new List<string> { "RG-37" },
                    Citations = new List<DataCitation>
                    {
                        new()
                        {
                            SourceType = "Stress Test Report",
                            SourceModule = "RG-37",
                            Summary = report.ScenarioName
                        }
                    },
                    Flags = report.EntityResults.Where(x => x.CarBreach || x.LcrBreach || x.NplBreach || x.SolvencyBreach)
                        .Take(10)
                        .Select(x => new IntelligenceFlag
                        {
                            FlagType = "WARNING",
                            Message = $"{x.InstitutionName} breaches at least one stressed prudential threshold.",
                            Source = "RG-37"
                        }).ToList(),
                    FollowUpSuggestions = new List<string>
                    {
                        "Show the systemic risk dashboard",
                        "What happens if Access Bank fails?",
                        "Rank institutions by CAR"
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Stress test execution failed for scenario {ScenarioType}. Falling back to listing scenarios.", scenario.Type);
            }
        }

        var rows = scenarios.Select(x => new Dictionary<string, object?>
        {
            ["scenario_type"] = x.Type.ToString(),
            ["name"] = x.Name,
            ["category"] = x.Category,
            ["description"] = x.Description
        }).ToList();

        return new RegulatorIqResponse
        {
            AnswerText = $"There are {scenarios.Count} configured stress scenario(s). {scenarios[0].Name} is available for execution.",
            AnswerFormat = "table",
            StructuredData = BuildTable(rows),
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, scenarios.Count),
            DataSourcesUsed = new List<string> { "RG-37" },
            Citations = scenarios.Take(5).Select(x => new DataCitation
            {
                SourceType = "Stress Scenario",
                SourceModule = "RG-37",
                Summary = x.Name
            }).ToList(),
            FollowUpSuggestions = new List<string>
            {
                "Run the oil price collapse scenario",
                "Show systemic risk dashboard",
                "What happens if Access Bank fails?"
            }
        };
    }

    private async Task<RegulatorIqResponse> GenerateSanctionsExposureAsync(
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var resolvedNames = await ResolveInstitutionNamesAsync(db, ResolveEntityIds(classifiedIntent, context), ct);
        var query = db.SanctionsScreeningResults.AsNoTracking().AsQueryable();

        if (resolvedNames.Count > 0)
        {
            query = query.Where(x => resolvedNames.Any(name => x.Subject.Contains(name)));
        }

        var results = await query
            .OrderByDescending(x => x.MatchScore)
            .ThenByDescending(x => x.CreatedAt)
            .Take(ParseInt(classifiedIntent.ExtractedParameters, "limit", 20))
            .ToListAsync(ct);

        if (results.Count == 0)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "No sanctions screening matches were found for the selected scope.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var grouped = results
            .GroupBy(x => x.Subject)
            .Select((g, index) => new RegulatorRankingItem
            {
                Rank = index + 1,
                InstitutionName = g.Key,
                Value = (decimal?)g.Max(x => (decimal)x.MatchScore),
                RiskBand = g.Select(x => x.RiskLevel).FirstOrDefault() ?? "UNKNOWN",
                Source = "RG-48"
            })
            .OrderByDescending(x => x.Value)
            .ToList();

        return new RegulatorIqResponse
        {
            AnswerText = $"{grouped.Count} institution(s) have screening matches in the selected scope. The highest observed match is {grouped[0].InstitutionName} at {grouped[0].Value:N1}.",
            AnswerFormat = "ranking",
            StructuredData = new RegulatorRankingData
            {
                MetricCode = "sanctionsmatch",
                MetricLabel = "Highest Match Score",
                Items = grouped
            },
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, grouped.Count),
            DataSourcesUsed = new List<string> { "RG-48" },
            Citations = results.Take(5).Select(x => new DataCitation
            {
                SourceType = "Sanctions Screening Result",
                SourceModule = "RG-48",
                InstitutionName = x.Subject,
                Summary = $"{x.SourceCode}:{x.MatchedName}"
            }).ToList(),
            Flags = grouped.Take(10).Select(x => new IntelligenceFlag
            {
                FlagType = "WARNING",
                Message = $"{x.InstitutionName} has a potential sanctions match with score {x.Value:N1}.",
                Source = "RG-48"
            }).ToList(),
            FollowUpSuggestions = new List<string>
            {
                $"Give me a full profile of {grouped[0].InstitutionName}",
                "Show open supervisory actions",
                "Show systemic risk dashboard"
            }
        };
    }

    private async Task<RegulatorIqResponse> GenerateExaminationBriefingAsync(
        string originalQuery,
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        var tenantId = ResolveSingleEntityId(classifiedIntent, context);
        if (tenantId is null)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "I could not determine which institution to prepare an examination briefing for.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var briefing = await _regulatorIntelligenceService.GenerateExaminationBriefingAsync(tenantId.Value, context.RegulatorCode, ct);
        var response = new RegulatorIqResponse
        {
            AnswerFormat = "profile",
            StructuredData = briefing,
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode, briefing.DataSourcesUsed),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, briefing.FocusAreas.Count),
            DataSourcesUsed = briefing.DataSourcesUsed,
            EntitiesAccessed = new List<Guid> { briefing.TenantId },
            Citations = BuildExaminationCitations(briefing),
            Flags = BuildProfileFlags(briefing.Profile),
            FollowUpSuggestions = new List<string>
            {
                $"Show {briefing.InstitutionName} sanctions exposure",
                $"Compare {briefing.InstitutionName} to peers on CAR and NPL",
                "Show systemic risk dashboard"
            }
        };

        response.AnswerText = BuildExaminationBriefingAnswer(briefing);
        response.AnswerText = await TryFormatWithLlmAsync(originalQuery, classifiedIntent.IntentCode, response.AnswerText, briefing, response, ct);
        return response;
    }

    private async Task<RegulatorIqResponse> GenerateSupervisoryActionsAsync(
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        var dashboard = await _systemicRiskService.GetDashboard(context.RegulatorCode, ct);
        var actions = dashboard.PendingActions.ToList();
        if (actions.Count == 0)
        {
            var flags = await _earlyWarningService.ComputeFlags(context.RegulatorCode, ct);
            var topFlags = flags
                .OrderByDescending(x => SeverityScore(x.Severity))
                .ThenByDescending(x => x.TriggeredAt)
                .Take(10)
                .ToList();

            actions = new List<SupervisoryAction>();
            foreach (var flag in topFlags)
            {
                actions.Add(await _systemicRiskService.GenerateSupervisoryAction(context.RegulatorCode, flag.InstitutionId, flag.FlagCode, ct));
            }
        }

        if (actions.Count == 0)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "There are no pending supervisory actions in the selected scope.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var rows = actions.Select(x => new Dictionary<string, object?>
        {
            ["institution_id"] = x.InstitutionId,
            ["institution_name"] = x.InstitutionName,
            ["trigger_flag"] = x.TriggerFlag,
            ["action_type"] = x.ActionType,
            ["escalation_level"] = x.EscalationLevel,
            ["status"] = x.Status,
            ["due_date"] = x.DueDate
        }).ToList();

        return new RegulatorIqResponse
        {
            AnswerText = $"{actions.Count} supervisory action item(s) are currently pending or recommended. The most urgent action is {actions[0].ActionType} for institution {actions[0].InstitutionId}.",
            AnswerFormat = "table",
            StructuredData = BuildTable(rows),
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, actions.Count),
            DataSourcesUsed = new List<string> { "RG-36" },
            Citations = actions.Take(5).Select(x => new DataCitation
            {
                SourceType = "Supervisory Action",
                SourceModule = "RG-36",
                InstitutionName = x.InstitutionName,
                Summary = x.ActionType
            }).ToList(),
            FollowUpSuggestions = new List<string>
            {
                "Show systemic risk dashboard",
                "Which institutions have active EWIs?",
                "Generate an examination briefing for Access Bank"
            }
        };
    }

    private async Task<RegulatorIqResponse> GenerateCrossBorderAsync(
        string originalQuery,
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var groupId = await ResolveFinancialGroupIdAsync(db, originalQuery, ResolveEntityIds(classifiedIntent, context), ct);
        if (groupId is null)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "I could not resolve the financial group for the requested cross-border view.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var overview = await _panAfricanDashboardService.GetGroupOverviewAsync(groupId.Value, ct);
        var subsidiaries = await _panAfricanDashboardService.GetSubsidiarySnapshotsAsync(groupId.Value, classifiedIntent.PeriodCode, ct);
        var deadlines = await _panAfricanDashboardService.GetDeadlineCalendarAsync(groupId.Value, today, today.AddDays(30), ct);
        var consolidatedRisk = await _panAfricanDashboardService.GetConsolidatedRiskMetricsAsync(groupId.Value, classifiedIntent.PeriodCode ?? DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture), ct);

        if (overview is null)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "No cross-border overview was found for the resolved financial group.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var payload = new
        {
            Overview = overview,
            Subsidiaries = subsidiaries,
            Deadlines = deadlines,
            ConsolidatedRisk = consolidatedRisk
        };

        var response = new RegulatorIqResponse
        {
            AnswerFormat = "table",
            StructuredData = payload,
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, subsidiaries.Count),
            DataSourcesUsed = new List<string> { "RG-41" },
            Citations = new List<DataCitation>
            {
                new()
                {
                    SourceType = "Pan-African Dashboard",
                    SourceModule = "RG-41",
                    Summary = overview.GroupName
                }
            },
            FollowUpSuggestions = new List<string>
            {
                "Show policy simulation results",
                "Show systemic risk dashboard",
                "Generate an examination briefing for Access Bank"
            }
        };

        response.AnswerText = $"{overview.GroupName} has {subsidiaries.Count} subsidiary snapshot(s) in scope and {deadlines.Count} cross-border regulatory deadline(s) over the next 30 days.";
        response.AnswerText = await TryFormatWithLlmAsync(originalQuery, classifiedIntent.IntentCode, response.AnswerText, payload, response, ct);
        return response;
    }

    private async Task<RegulatorIqResponse> GeneratePolicyImpactAsync(
        string originalQuery,
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        var regulatorId = TenantAccessContextResolver.ComputeStableRegulatorId(context.RegulatorTenantId);
        var scenarios = await _policyScenarioService.ListScenariosAsync(
            regulatorId,
            TryResolvePolicyDomain(originalQuery),
            null,
            1,
            ParseInt(classifiedIntent.ExtractedParameters, "limit", 10),
            ct);

        if (scenarios.Items.Count == 0)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "No policy simulation scenarios were found for the selected regulator scope.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var rows = scenarios.Items.Select(x => new Dictionary<string, object?>
        {
            ["title"] = x.Title,
            ["domain"] = x.Domain.ToString(),
            ["status"] = x.Status.ToString(),
            ["target_entity_types"] = x.TargetEntityTypes,
            ["baseline_date"] = x.BaselineDate
        }).ToList();

        var response = new RegulatorIqResponse
        {
            AnswerFormat = "table",
            StructuredData = BuildTable(rows),
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, scenarios.Items.Count),
            DataSourcesUsed = new List<string> { "RG-40" },
            Citations = scenarios.Items.Take(5).Select(x => new DataCitation
            {
                SourceType = "Policy Scenario",
                SourceModule = "RG-40",
                Summary = x.Title
            }).ToList(),
            FollowUpSuggestions = new List<string>
            {
                "Show cross-border intelligence",
                "Show systemic risk dashboard",
                "List available stress scenarios"
            }
        };

        response.AnswerText = $"{scenarios.TotalCount} policy scenario(s) are available. The leading scenario is {scenarios.Items[0].Title} in the {scenarios.Items[0].Domain} domain.";
        response.AnswerText = await TryFormatWithLlmAsync(originalQuery, classifiedIntent.IntentCode, response.AnswerText, scenarios, response, ct);
        return response;
    }

    private async Task<RegulatorIqResponse> GenerateValidationHotspotAsync(
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await (
            from error in db.ValidationErrors.AsNoTracking()
            join report in db.ValidationReports.AsNoTracking() on error.ValidationReportId equals report.Id
            join submission in db.Submissions.AsNoTracking() on report.SubmissionId equals submission.Id
            join institution in db.Institutions.AsNoTracking() on submission.InstitutionId equals institution.Id
            where institution.IsActive
            group error by new { institution.TenantId, institution.InstitutionName, institution.LicenseType, error.Field } into g
            orderby g.Count() descending
            select new
            {
                g.Key.TenantId,
                g.Key.InstitutionName,
                g.Key.LicenseType,
                Field = g.Key.Field,
                ErrorCount = g.Count()
            })
            .Take(ParseInt(classifiedIntent.ExtractedParameters, "limit", 20))
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "No validation hotspots were found in the current data estate.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var tableRows = rows.Select(x => new Dictionary<string, object?>
        {
            ["tenant_id"] = x.TenantId,
            ["institution_name"] = x.InstitutionName,
            ["licence_type"] = x.LicenseType,
            ["field"] = x.Field,
            ["error_count"] = x.ErrorCount
        }).ToList();

        return new RegulatorIqResponse
        {
            AnswerText = $"{rows.Count} validation hotspots were identified. The most concentrated issue is {rows[0].Field} at {rows[0].InstitutionName} with {rows[0].ErrorCount} error(s).",
            AnswerFormat = "table",
            StructuredData = BuildTable(tableRows),
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, rows.Count),
            DataSourcesUsed = new List<string> { "RG-07" },
            EntitiesAccessed = rows.Select(x => x.TenantId).Distinct().ToList(),
            Citations = rows.Take(5).Select(x => new DataCitation
            {
                SourceType = "Validation Error",
                SourceModule = "RG-07",
                SourceField = x.Field,
                InstitutionName = x.InstitutionName
            }).ToList(),
            FollowUpSuggestions = new List<string>
            {
                $"Give me a full profile of {rows[0].InstitutionName}",
                "Rank institutions by anomaly pressure",
                "Which banks have overdue returns?"
            }
        };
    }

    private async Task<RegulatorIqResponse> GenerateCurrentValueAsync(
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        var tenantId = ResolveSingleEntityId(classifiedIntent, context);
        var metricCode = classifiedIntent.FieldCode;
        if (tenantId is null || string.IsNullOrWhiteSpace(metricCode))
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "I could not determine the entity and metric for that current-value query.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var snapshot = (await LoadLatestSnapshotsAsync(db, context.RegulatorCode, new List<Guid> { tenantId.Value }, classifiedIntent.PeriodCode, ct)).FirstOrDefault();
        if (snapshot is null)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "I could not find an accepted submission for that institution.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var value = GetSnapshotMetricValue(snapshot, metricCode);
        if (!value.HasValue)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, $"I could not find {GetMetricLabel(metricCode)} in the latest accepted submission.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        return new RegulatorIqResponse
        {
            AnswerText = $"{snapshot.InstitutionName} reported {GetMetricLabel(metricCode)} of {FormatMetricValue(value, metricCode)} in {snapshot.PeriodCode}.",
            AnswerFormat = "table",
            StructuredData = BuildTable(new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["institution_name"] = snapshot.InstitutionName,
                    ["metric_code"] = metricCode,
                    ["metric_label"] = GetMetricLabel(metricCode),
                    ["value"] = value,
                    ["period_code"] = snapshot.PeriodCode
                }
            }),
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence),
            DataSourcesUsed = new List<string> { GetMetricSource(metricCode) },
            EntitiesAccessed = new List<Guid> { snapshot.TenantId },
            Citations = new List<DataCitation>
            {
                new()
                {
                    SourceType = "Accepted Return",
                    SourceModule = GetMetricSource(metricCode),
                    SourceField = metricCode,
                    SourcePeriod = snapshot.PeriodCode,
                    InstitutionName = snapshot.InstitutionName
                }
            },
            Flags = BuildMetricFlags(snapshot.InstitutionName, metricCode, value, GetMetricSource(metricCode)),
            FollowUpSuggestions = new List<string>
            {
                $"Show {snapshot.InstitutionName} {GetMetricLabel(metricCode)} trend",
                $"Compare {snapshot.InstitutionName} to peers on {GetMetricLabel(metricCode)}",
                $"Give me a full profile of {snapshot.InstitutionName}"
            }
        };
    }

    private async Task<RegulatorIqResponse> GenerateEntityTrendAsync(
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        var tenantId = ResolveSingleEntityId(classifiedIntent, context);
        var metricCode = classifiedIntent.FieldCode;
        if (tenantId is null || string.IsNullOrWhiteSpace(metricCode))
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "I could not determine the entity and metric for that trend query.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var snapshots = await LoadSnapshotsAsync(db, context.RegulatorCode, new List<Guid> { tenantId.Value }, null, ct);
        var series = snapshots
            .Where(x => GetSnapshotMetricValue(x, metricCode).HasValue)
            .OrderBy(x => x.ReportingDate)
            .TakeLast(ParseInt(classifiedIntent.ExtractedParameters, "limit", 8))
            .ToList();

        if (series.Count == 0)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, $"I could not find historical accepted data for {GetMetricLabel(metricCode)}.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        return new RegulatorIqResponse
        {
            AnswerText = $"{GetMetricLabel(metricCode)} moved from {FormatMetricValue(GetSnapshotMetricValue(series[0], metricCode), metricCode)} in {series[0].PeriodCode} to {FormatMetricValue(GetSnapshotMetricValue(series[^1], metricCode), metricCode)} in {series[^1].PeriodCode}.",
            AnswerFormat = "chart_data",
            StructuredData = new RegulatorChartData
            {
                ChartType = "line",
                Labels = series.Select(x => x.PeriodCode).ToList(),
                Series = new List<RegulatorChartSeries>
                {
                    new()
                    {
                        Name = GetMetricLabel(metricCode),
                        Values = series.Select(x => GetSnapshotMetricValue(x, metricCode)).ToList()
                    }
                }
            },
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, series.Count),
            DataSourcesUsed = new List<string> { GetMetricSource(metricCode) },
            EntitiesAccessed = new List<Guid> { tenantId.Value },
            Citations = new List<DataCitation>
            {
                new()
                {
                    SourceType = "Trend Series",
                    SourceModule = GetMetricSource(metricCode),
                    SourceField = metricCode,
                    InstitutionName = series[0].InstitutionName
                }
            },
            FollowUpSuggestions = new List<string>
            {
                $"What is the latest {GetMetricLabel(metricCode)}?",
                $"Compare {series[0].InstitutionName} to peers on {GetMetricLabel(metricCode)}",
                $"Give me a full profile of {series[0].InstitutionName}"
            }
        };
    }

    private async Task<RegulatorIqResponse> GeneratePeerComparisonAsync(
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        var tenantId = ResolveSingleEntityId(classifiedIntent, context);
        var metricCode = classifiedIntent.FieldCode;
        if (tenantId is null || string.IsNullOrWhiteSpace(metricCode))
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "I could not determine the institution and metric for that peer comparison.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var snapshots = await LoadLatestSnapshotsAsync(db, context.RegulatorCode, null, classifiedIntent.PeriodCode, ct);
        var current = snapshots.FirstOrDefault(x => x.TenantId == tenantId.Value);
        if (current is null)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "I could not find the latest accepted submission for that institution.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var peerValues = snapshots
            .Where(x => MatchesLicenceCategory(x.LicenceCategory, current.LicenceCategory))
            .Select(x => GetSnapshotMetricValue(x, metricCode))
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .OrderBy(x => x)
            .ToList();

        var currentValue = GetSnapshotMetricValue(current, metricCode);
        if (!currentValue.HasValue || peerValues.Count == 0)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, $"I could not compare {GetMetricLabel(metricCode)} to peers for that institution.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var peerMedian = RegulatorAnalyticsSupport.Median(peerValues);
        var peerAverage = decimal.Round(peerValues.Average(), 2);
        var deltaPct = peerMedian == 0m ? 0m : decimal.Round(((currentValue.Value - peerMedian) / Math.Abs(peerMedian)) * 100m, 2);

        return new RegulatorIqResponse
        {
            AnswerText = $"{current.InstitutionName} reported {GetMetricLabel(metricCode)} of {FormatMetricValue(currentValue, metricCode)} versus a peer median of {FormatMetricValue(peerMedian, metricCode)} and peer average of {FormatMetricValue(peerAverage, metricCode)}.",
            AnswerFormat = "table",
            StructuredData = BuildTable(new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["institution_name"] = current.InstitutionName,
                    ["metric_code"] = metricCode,
                    ["current_value"] = currentValue,
                    ["peer_median"] = peerMedian,
                    ["peer_average"] = peerAverage,
                    ["delta_pct"] = deltaPct
                }
            }),
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence),
            DataSourcesUsed = new List<string> { GetMetricSource(metricCode) },
            EntitiesAccessed = new List<Guid> { current.TenantId },
            Citations = new List<DataCitation>
            {
                new()
                {
                    SourceType = "Peer Benchmark",
                    SourceModule = GetMetricSource(metricCode),
                    SourceField = metricCode,
                    SourcePeriod = current.PeriodCode,
                    InstitutionName = current.InstitutionName
                }
            },
            FollowUpSuggestions = new List<string>
            {
                $"Show {current.InstitutionName} {GetMetricLabel(metricCode)} trend",
                $"Give me a full profile of {current.InstitutionName}",
                $"Rank institutions by {GetMetricLabel(metricCode)}"
            }
        };
    }

    private async Task<RegulatorIqResponse> GenerateComparisonPeriodAsync(
        string originalQuery,
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        var tenantId = ResolveSingleEntityId(classifiedIntent, context);
        var metricCode = classifiedIntent.FieldCode;
        if (tenantId is null || string.IsNullOrWhiteSpace(metricCode))
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "I could not determine the institution and metric for that period comparison.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var snapshots = await LoadSnapshotsAsync(db, context.RegulatorCode, new List<Guid> { tenantId.Value }, null, ct);
        var selected = snapshots
            .Where(x => GetSnapshotMetricValue(x, metricCode).HasValue)
            .OrderBy(x => x.ReportingDate)
            .TakeLast(2)
            .ToList();

        if (selected.Count < 2)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, $"I could not find two periods to compare for {GetMetricLabel(metricCode)}.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var first = selected[0];
        var second = selected[1];
        var firstValue = GetSnapshotMetricValue(first, metricCode);
        var secondValue = GetSnapshotMetricValue(second, metricCode);

        return new RegulatorIqResponse
        {
            AnswerText = $"{GetMetricLabel(metricCode)} moved from {FormatMetricValue(firstValue, metricCode)} in {first.PeriodCode} to {FormatMetricValue(secondValue, metricCode)} in {second.PeriodCode}.",
            AnswerFormat = "table",
            StructuredData = BuildTable(new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["institution_name"] = first.InstitutionName,
                    ["metric_code"] = metricCode,
                    ["period_a"] = first.PeriodCode,
                    ["value_a"] = firstValue,
                    ["period_b"] = second.PeriodCode,
                    ["value_b"] = secondValue
                }
            }),
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence, 2),
            DataSourcesUsed = new List<string> { GetMetricSource(metricCode) },
            EntitiesAccessed = new List<Guid> { tenantId.Value },
            Citations = new List<DataCitation>
            {
                new()
                {
                    SourceType = "Period Comparison",
                    SourceModule = GetMetricSource(metricCode),
                    SourceField = metricCode,
                    InstitutionName = first.InstitutionName
                }
            },
            FollowUpSuggestions = new List<string>
            {
                $"Show {first.InstitutionName} {GetMetricLabel(metricCode)} trend",
                $"Compare {first.InstitutionName} to peers on {GetMetricLabel(metricCode)}",
                $"Give me a full profile of {first.InstitutionName}"
            }
        };
    }

    private Task<RegulatorIqResponse> GenerateDeadlineAsync(
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct) =>
        GenerateFilingStatusAsync(classifiedIntent, context, ct);

    private async Task<RegulatorIqResponse> GenerateComplianceStatusAsync(
        string originalQuery,
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        var tenantId = ResolveSingleEntityId(classifiedIntent, context);
        if (tenantId.HasValue)
        {
            return await GenerateChsEntityAsync(originalQuery, classifiedIntent, context, ct);
        }

        return await GenerateChsRankingAsync(classifiedIntent, context, ct);
    }

    private async Task<RegulatorIqResponse> GenerateAnomalyStatusAsync(
        string originalQuery,
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        var tenantId = ResolveSingleEntityId(classifiedIntent, context);
        if (tenantId.HasValue)
        {
            var profile = await _regulatorIntelligenceService.GetEntityProfileAsync(tenantId.Value, context.RegulatorCode, ct);
            if (profile.Anomaly is null)
            {
                return BuildNoDataResponse(classifiedIntent.IntentCode, $"No anomaly summary is available for {profile.InstitutionName}.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
            }

            var response = new RegulatorIqResponse
            {
                AnswerText = $"{profile.InstitutionName} has anomaly quality score {profile.Anomaly.QualityScore:N1} with traffic light {profile.Anomaly.TrafficLight} for {profile.Anomaly.ModuleCode} {profile.Anomaly.PeriodCode}.",
                AnswerFormat = "table",
                StructuredData = BuildTable(new List<Dictionary<string, object?>>
                {
                    new()
                    {
                        ["institution_name"] = profile.InstitutionName,
                        ["quality_score"] = profile.Anomaly.QualityScore,
                        ["traffic_light"] = profile.Anomaly.TrafficLight,
                        ["alert_count"] = profile.Anomaly.AlertCount,
                        ["warning_count"] = profile.Anomaly.WarningCount,
                        ["total_findings"] = profile.Anomaly.TotalFindings,
                        ["period_code"] = profile.Anomaly.PeriodCode
                    }
                }),
                ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
                ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence),
                DataSourcesUsed = new List<string> { "AI-01" },
                EntitiesAccessed = new List<Guid> { profile.TenantId },
                Citations = new List<DataCitation>
                {
                    new()
                    {
                        SourceType = "Anomaly Report",
                        SourceModule = "AI-01",
                        SourcePeriod = profile.Anomaly.PeriodCode,
                        InstitutionName = profile.InstitutionName
                    }
                },
                Flags = new List<IntelligenceFlag>
                {
                    new()
                    {
                        FlagType = string.Equals(profile.Anomaly.TrafficLight, "RED", StringComparison.OrdinalIgnoreCase) ? "WARNING" : "CONCERN",
                        Message = $"{profile.InstitutionName} has {profile.Anomaly.AlertCount} anomaly alert(s).",
                        Source = "AI-01"
                    }
                },
                FollowUpSuggestions = new List<string>
                {
                    $"Give me a full profile of {profile.InstitutionName}",
                    $"Show {profile.InstitutionName} filing status",
                    "Rank institutions by anomaly pressure"
                }
            };

            response.AnswerText = await TryFormatWithLlmAsync(originalQuery, classifiedIntent.IntentCode, response.AnswerText, profile.Anomaly, response, ct);
            return response;
        }

        return await GenerateRiskRankingAsync(classifiedIntent, context, ct);
    }

    private async Task<RegulatorIqResponse> GenerateScenarioAsync(
        string originalQuery,
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        var tenantId = ResolveSingleEntityId(classifiedIntent, context);
        if (tenantId is null)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "A what-if scenario requires an institution context.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var snapshot = (await LoadLatestSnapshotsAsync(db, context.RegulatorCode, new List<Guid> { tenantId.Value }, null, ct)).FirstOrDefault();
        if (snapshot is null)
        {
            return BuildNoDataResponse(classifiedIntent.IntentCode, "I could not find the latest accepted submission for that institution.", ResolveClassificationLevel(classifiedIntent.IntentCode), classifiedIntent.Confidence);
        }

        var multiplier = ParseScenarioMultiplier(originalQuery) ?? 2m;
        var currentCar = GetSnapshotMetricValue(snapshot, "carratio") ?? 0m;
        var currentNpl = GetSnapshotMetricValue(snapshot, "nplratio") ?? 0m;
        var projectedNpl = decimal.Round(currentNpl * multiplier, 2);
        var projectedCar = decimal.Round(currentCar - Math.Max(0m, projectedNpl - currentNpl) * 0.35m, 2);

        return new RegulatorIqResponse
        {
            AnswerText = $"Under the requested shock, {snapshot.InstitutionName}'s NPL ratio moves from {FormatMetricValue(currentNpl, "nplratio")} to {FormatMetricValue(projectedNpl, "nplratio")}, while CAR moves from {FormatMetricValue(currentCar, "carratio")} to {FormatMetricValue(projectedCar, "carratio")}.",
            AnswerFormat = "table",
            StructuredData = BuildTable(new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["institution_name"] = snapshot.InstitutionName,
                    ["current_car"] = currentCar,
                    ["projected_car"] = projectedCar,
                    ["current_npl_ratio"] = currentNpl,
                    ["projected_npl_ratio"] = projectedNpl,
                    ["scenario_multiplier"] = multiplier
                }
            }),
            ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
            ConfidenceLevel = DetermineConfidence(classifiedIntent.Confidence),
            DataSourcesUsed = new List<string> { "RG-07" },
            EntitiesAccessed = new List<Guid> { snapshot.TenantId },
            Citations = new List<DataCitation>
            {
                new()
                {
                    SourceType = "Scenario Projection",
                    SourceModule = "RG-07",
                    SourcePeriod = snapshot.PeriodCode,
                    InstitutionName = snapshot.InstitutionName
                }
            },
            Flags = projectedCar < 15m
                ? new List<IntelligenceFlag>
                {
                    new()
                    {
                        FlagType = "WARNING",
                        Message = "Projected CAR falls below the CBN minimum of 15%.",
                        Source = "RG-07"
                    }
                }
                : new List<IntelligenceFlag>(),
            FollowUpSuggestions = new List<string>
            {
                $"Give me a full profile of {snapshot.InstitutionName}",
                $"Show {snapshot.InstitutionName} CAR trend",
                "Show systemic risk dashboard"
            }
        };
    }

    private async Task<RegulatorIqResponse> GenerateSearchAsync(
        string originalQuery,
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct)
    {
        var hotspotQuery = new RegulatorIntentResult
        {
            IntentCode = "VALIDATION_HOTSPOT",
            Confidence = classifiedIntent.Confidence,
            ExtractedParameters = new Dictionary<string, string>(classifiedIntent.ExtractedParameters, StringComparer.OrdinalIgnoreCase)
        };

        hotspotQuery.ExtractedParameters["rawQuery"] = originalQuery;
        return await GenerateValidationHotspotAsync(hotspotQuery, context, ct);
    }

    private async Task<string> TryFormatWithLlmAsync(
        string originalQuery,
        string intentCode,
        string fallbackAnswer,
        object payload,
        RegulatorIqResponse response,
        CancellationToken ct)
    {
        if (!ComplexLlmIntents.Contains(intentCode))
        {
            return fallbackAnswer;
        }

        try
        {
            var llmResponse = await _llmService.CompleteAsync(
                new LlmRequest
                {
                    SystemPrompt = BuildFormattingSystemPrompt(response),
                    UserMessage = $"Original query: {originalQuery}\n\nGrounded data:\n{JsonSerializer.Serialize(payload)}",
                    Temperature = 0.1m,
                    MaxTokens = 1200
                },
                ct);

            if (llmResponse.Success && !string.IsNullOrWhiteSpace(llmResponse.Content))
            {
                return llmResponse.Content.Trim();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM formatting failed for regulator intent {IntentCode}.", intentCode);
        }

        return fallbackAnswer;
    }

    private static string BuildFormattingSystemPrompt(RegulatorIqResponse response)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are RegulatorIQ, writing for Nigerian financial regulators.");
        builder.AppendLine("Write like a senior bank examiner or financial intelligence analyst.");
        builder.AppendLine("Tone: analytical, precise, investigative, and concise.");
        builder.AppendLine("Use Nigerian prudential terminology such as CAR, NPL, liquidity ratio, and compliance health score.");
        builder.AppendLine("Use Naira formatting as ₦B/₦M/₦T when monetary values appear.");
        builder.AppendLine("Reference regulatory thresholds plainly, for example CAR minimum 15%, NPL warning 5%, liquidity minimum 30%.");
        builder.AppendLine("Cite source modules inline using bracketed labels such as [RG-07], [RG-32], [AI-01], [AI-04], [RG-36], [RG-48].");
        builder.AppendLine($"Response classification: {response.ClassificationLevel}.");
        return builder.ToString();
    }

    private static RegulatorIqResponse BuildNoDataResponse(
        string intentCode,
        string message,
        string classificationLevel,
        decimal confidence) =>
        new()
        {
            AnswerText = message,
            AnswerFormat = "text",
            ClassificationLevel = classificationLevel,
            ConfidenceLevel = DetermineConfidence(confidence, 0),
            FollowUpSuggestions = BuildFollowUps(intentCode)
        };

    private static string DetermineConfidence(decimal confidence, int rowCount = 1)
    {
        if (rowCount <= 0)
        {
            return "LOW";
        }

        if (confidence >= 0.85m)
        {
            return "HIGH";
        }

        return confidence >= 0.60m ? "MEDIUM" : "LOW";
    }

    private static string ResolveClassificationLevel(string intentCode, IEnumerable<string>? dataSources = null)
    {
        if (string.Equals(intentCode, "HELP", StringComparison.OrdinalIgnoreCase)
            || string.Equals(intentCode, "REGULATORY_LOOKUP", StringComparison.OrdinalIgnoreCase)
            || string.Equals(intentCode, "DEADLINE", StringComparison.OrdinalIgnoreCase))
        {
            return "UNCLASSIFIED";
        }

        if (ConfidentialIntents.Contains(intentCode))
        {
            return "CONFIDENTIAL";
        }

        if (dataSources?.Any(x => string.Equals(x, "RG-48", StringComparison.OrdinalIgnoreCase)) == true)
        {
            return "CONFIDENTIAL";
        }

        return "RESTRICTED";
    }

    private static List<string> BuildFollowUps(string intentCode)
    {
        return intentCode.ToUpperInvariant() switch
        {
            "ENTITY_PROFILE" => new List<string>
            {
                "Compare this entity to peers on CAR and NPL",
                "Show filing status for this entity",
                "Generate an examination briefing"
            },
            "ENTITY_COMPARE" => new List<string>
            {
                "Rank all institutions by CAR",
                "Show sector aggregate for CAR",
                "Generate an examination briefing"
            },
            "SYSTEMIC_DASHBOARD" => new List<string>
            {
                "What happens if Access Bank fails?",
                "List available stress scenarios",
                "Show open supervisory actions"
            },
            _ => new List<string>
            {
                "Give me a full profile of Access Bank",
                "Compare GTBank vs Zenith on CAR and NPL",
                "Show systemic risk dashboard"
            }
        };
    }

    private static List<DataCitation> BuildProfileCitations(EntityIntelligenceProfile profile)
    {
        var citations = new List<DataCitation>();
        if (!string.IsNullOrWhiteSpace(profile.LatestPeriodCode))
        {
            citations.Add(new DataCitation
            {
                SourceType = "Accepted Return",
                SourceModule = "RG-07",
                SourcePeriod = profile.LatestPeriodCode,
                InstitutionName = profile.InstitutionName
            });
        }

        if (profile.ComplianceHealth is not null)
        {
            citations.Add(new DataCitation
            {
                SourceType = "Compliance Health Score",
                SourceModule = "RG-32",
                SourcePeriod = profile.ComplianceHealth.PeriodLabel,
                InstitutionName = profile.InstitutionName
            });
        }

        if (profile.Anomaly is not null)
        {
            citations.Add(new DataCitation
            {
                SourceType = "Anomaly Report",
                SourceModule = "AI-01",
                SourcePeriod = profile.Anomaly.PeriodCode,
                InstitutionName = profile.InstitutionName
            });
        }

        if (profile.FilingRisk is not null)
        {
            citations.Add(new DataCitation
            {
                SourceType = "ForeSight Prediction",
                SourceModule = "AI-04",
                SourcePeriod = profile.FilingRisk.PeriodCode,
                InstitutionName = profile.InstitutionName
            });
        }

        if (profile.SanctionsExposure is not null && profile.SanctionsExposure.MatchCount > 0)
        {
            citations.Add(new DataCitation
            {
                SourceType = "Sanctions Screening",
                SourceModule = "RG-48",
                InstitutionName = profile.InstitutionName
            });
        }

        return citations;
    }

    private static List<DataCitation> BuildExaminationCitations(ExaminationBriefing briefing)
    {
        return briefing.DataSourcesUsed
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(x => new DataCitation
            {
                SourceType = "Examination Briefing Source",
                SourceModule = x,
                InstitutionName = briefing.InstitutionName
            })
            .ToList();
    }

    private static string BuildEntityProfileAnswer(EntityIntelligenceProfile profile, IReadOnlyCollection<IntelligenceFlag> flags)
    {
        var car = profile.KeyMetrics.FirstOrDefault(x => string.Equals(x.MetricCode, "carratio", StringComparison.OrdinalIgnoreCase))?.Value;
        var npl = profile.KeyMetrics.FirstOrDefault(x => string.Equals(x.MetricCode, "nplratio", StringComparison.OrdinalIgnoreCase))?.Value;
        var liquidity = profile.KeyMetrics.FirstOrDefault(x => string.Equals(x.MetricCode, "liquidityratio", StringComparison.OrdinalIgnoreCase))?.Value;

        return $"{profile.InstitutionName} ({profile.LicenceCategory}) last submitted in {profile.LatestPeriodCode ?? "the latest period"}. CAR is {FormatMetricValue(car, "carratio")}, NPL is {FormatMetricValue(npl, "nplratio")}, liquidity ratio is {FormatMetricValue(liquidity, "liquidityratio")}, and there are {flags.Count} active intelligence flag(s).";
    }

    private static string BuildExaminationBriefingAnswer(ExaminationBriefing briefing)
    {
        var topFocus = briefing.FocusAreas.Take(3).Select(x => $"{x.Area} ({x.Priority})");
        return $"{briefing.InstitutionName} examination briefing prepared with {briefing.FocusAreas.Count} focus area(s). Priority attention areas are {string.Join(", ", topFocus)}.";
    }

    private static string BuildComparisonAnswer(RegulatorComparisonData comparison)
    {
        if (comparison.Rows.Count == 0 || comparison.EntityNames.Count == 0)
        {
            return "I could not build the requested comparison matrix.";
        }

        var firstMetric = comparison.Rows[0];
        var values = comparison.EntityNames
            .Select(name => firstMetric.Values.TryGetValue(name, out var value)
                ? $"{name}: {FormatMetricValue(value, firstMetric.MetricCode)}"
                : $"{name}: n/a");

        return $"{firstMetric.MetricLabel} comparison for {comparison.PeriodCode ?? "the latest period"}: {string.Join("; ", values)}.";
    }

    private static string BuildRankingAnswer(RegulatorRankingData ranking, bool ascendingWorstFirst)
    {
        if (ranking.Items.Count == 0)
        {
            return "No ranking rows were produced.";
        }

        var lead = ranking.Items[0];
        var direction = ascendingWorstFirst ? "most acute" : "highest";
        return $"{lead.InstitutionName} currently has the {direction} {ranking.MetricLabel.ToLowerInvariant()} in the ranking at {FormatMetricValue(lead.Value, ranking.MetricCode)}.";
    }

    private static List<IntelligenceFlag> BuildProfileFlags(EntityIntelligenceProfile profile)
    {
        var flags = new List<IntelligenceFlag>();

        var car = profile.KeyMetrics.FirstOrDefault(x => string.Equals(x.MetricCode, "carratio", StringComparison.OrdinalIgnoreCase))?.Value;
        flags.AddRange(BuildMetricFlags(profile.InstitutionName, "carratio", car, "RG-07"));

        var npl = profile.KeyMetrics.FirstOrDefault(x => string.Equals(x.MetricCode, "nplratio", StringComparison.OrdinalIgnoreCase))?.Value;
        flags.AddRange(BuildMetricFlags(profile.InstitutionName, "nplratio", npl, "RG-07"));

        var liquidity = profile.KeyMetrics.FirstOrDefault(x => string.Equals(x.MetricCode, "liquidityratio", StringComparison.OrdinalIgnoreCase))?.Value;
        flags.AddRange(BuildMetricFlags(profile.InstitutionName, "liquidityratio", liquidity, "RG-07"));

        if (profile.ComplianceHealth is not null && profile.ComplianceHealth.OverallScore < 70m)
        {
            flags.Add(new IntelligenceFlag
            {
                FlagType = "CONCERN",
                Message = "Compliance health score is below the adequate threshold of 70.",
                Source = "RG-32"
            });
        }

        if (profile.FilingRisk is not null && profile.FilingRisk.RiskBand is "HIGH" or "CRITICAL")
        {
            flags.Add(new IntelligenceFlag
            {
                FlagType = "WARNING",
                Message = "ForeSight projects elevated regulatory pressure within the next reporting horizon.",
                Source = "AI-04"
            });
        }

        if (profile.FilingTimeliness is not null && profile.FilingTimeliness.OverdueFilings > 0)
        {
            flags.Add(new IntelligenceFlag
            {
                FlagType = "ACTION_REQUIRED",
                Message = $"There are {profile.FilingTimeliness.OverdueFilings} overdue filing(s) requiring follow-up.",
                Source = "RG-12"
            });
        }

        if (profile.SanctionsExposure is not null && profile.SanctionsExposure.MatchCount > 0)
        {
            flags.Add(new IntelligenceFlag
            {
                FlagType = "WARNING",
                Message = $"Potential sanctions exposure detected with {profile.SanctionsExposure.MatchCount} match(es).",
                Source = "RG-48"
            });
        }

        if (profile.Anomaly is not null && string.Equals(profile.Anomaly.TrafficLight, "RED", StringComparison.OrdinalIgnoreCase))
        {
            flags.Add(new IntelligenceFlag
            {
                FlagType = "WARNING",
                Message = "Latest anomaly report is RED and warrants examination attention.",
                Source = "AI-01"
            });
        }

        return flags
            .DistinctBy(x => $"{x.FlagType}|{x.Message}|{x.Source}")
            .ToList();
    }

    private static List<IntelligenceFlag> BuildComparisonFlags(RegulatorComparisonData comparison)
    {
        var flags = new List<IntelligenceFlag>();
        foreach (var row in comparison.Rows)
        {
            foreach (var value in row.Values)
            {
                flags.AddRange(BuildMetricFlags(value.Key, row.MetricCode, value.Value, row.SourceModules.FirstOrDefault() ?? "RG-07"));
            }
        }

        return flags.DistinctBy(x => $"{x.FlagType}|{x.Message}|{x.Source}").ToList();
    }

    private static List<IntelligenceFlag> BuildChsFlags(ComplianceHealthScore score)
    {
        var flags = new List<IntelligenceFlag>();
        if (score.OverallScore < 70m)
        {
            flags.Add(new IntelligenceFlag
            {
                FlagType = "CONCERN",
                Message = "Compliance health score is below 70.",
                Source = "RG-32"
            });
        }

        return flags;
    }

    private static List<IntelligenceFlag> BuildSystemicFlags(SystemicRiskDashboard dashboard)
    {
        var flags = new List<IntelligenceFlag>();
        if (dashboard.Summary.RedCount > 0)
        {
            flags.Add(new IntelligenceFlag
            {
                FlagType = "ACTION_REQUIRED",
                Message = $"{dashboard.Summary.RedCount} institution(s) are currently red-rated in the systemic dashboard.",
                Source = "RG-36"
            });
        }

        flags.AddRange(dashboard.SystemicIndicators
            .Where(x => x.Severity is RiskRating.Red or RiskRating.Amber)
            .Take(10)
            .Select(x => new IntelligenceFlag
            {
                FlagType = x.Severity == RiskRating.Red ? "WARNING" : "CONCERN",
                Message = $"{x.Title}: current value {x.CurrentValue:N2} versus threshold {x.Threshold:N2}.",
                Source = "RG-36"
            }));

        return flags;
    }

    private static List<IntelligenceFlag> BuildMetricFlags(
        string institutionName,
        string metricCode,
        decimal? value,
        string source)
    {
        if (!value.HasValue)
        {
            return new List<IntelligenceFlag>();
        }

        var flags = new List<IntelligenceFlag>();
        if (string.Equals(metricCode, "carratio", StringComparison.OrdinalIgnoreCase))
        {
            if (value.Value < 10m)
            {
                flags.Add(new IntelligenceFlag
                {
                    FlagType = "ACTION_REQUIRED",
                    Message = $"{institutionName} CAR is below 10%.",
                    Source = source
                });
            }
            else if (value.Value < 15m)
            {
                flags.Add(new IntelligenceFlag
                {
                    FlagType = "CONCERN",
                    Message = $"{institutionName} CAR is below the CBN minimum of 15%.",
                    Source = source
                });
            }
        }

        if (string.Equals(metricCode, "nplratio", StringComparison.OrdinalIgnoreCase) && value.Value > 5m)
        {
            flags.Add(new IntelligenceFlag
            {
                FlagType = "WARNING",
                Message = $"{institutionName} NPL ratio exceeds the 5% warning threshold.",
                Source = source
            });
        }

        if (string.Equals(metricCode, "liquidityratio", StringComparison.OrdinalIgnoreCase) && value.Value < 30m)
        {
            flags.Add(new IntelligenceFlag
            {
                FlagType = "CONCERN",
                Message = $"{institutionName} liquidity ratio is below the 30% minimum.",
                Source = source
            });
        }

        if (string.Equals(metricCode, "chs", StringComparison.OrdinalIgnoreCase) && value.Value < 70m)
        {
            flags.Add(new IntelligenceFlag
            {
                FlagType = "CONCERN",
                Message = $"{institutionName} compliance health score is below 70.",
                Source = source
            });
        }

        return flags;
    }

    private async Task<List<FilingStatusRow>> LoadFilingStatusRowsAsync(
        MetadataDbContext db,
        string regulatorCode,
        string? licenceCategory,
        CancellationToken ct)
    {
        var periods = await db.ReturnPeriods
            .AsNoTracking()
            .Include(x => x.Module)
            .Where(x => x.Module != null && x.Module.RegulatorCode == regulatorCode)
            .ToListAsync(ct);

        if (periods.Count == 0)
        {
            return new List<FilingStatusRow>();
        }

        var institutionLookup = (await db.Institutions
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(ct))
            .GroupBy(x => x.TenantId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.Id).First());

        var accepted = await db.Submissions
            .AsNoTracking()
            .Where(x => x.Status == SubmissionStatus.Accepted)
            .Select(x => new { x.TenantId, x.ReturnPeriodId, x.SubmittedAt })
            .ToListAsync(ct);

        var acceptedLookup = accepted
            .GroupBy(x => (x.TenantId, x.ReturnPeriodId))
            .ToDictionary(g => g.Key, g => g.Max(x => x.SubmittedAt));

        var rows = new List<FilingStatusRow>();
        foreach (var period in periods)
        {
            if (!institutionLookup.TryGetValue(period.TenantId, out var institution))
            {
                continue;
            }

            if (!MatchesLicenceCategory(institution.LicenseType, licenceCategory))
            {
                continue;
            }

            var hasAccepted = acceptedLookup.TryGetValue((period.TenantId, period.Id), out var submittedAt);
            var status = hasAccepted
                ? submittedAt.Date > period.DeadlineDate.Date ? "FILED_LATE" : "FILED"
                : DateTime.UtcNow.Date > period.DeadlineDate.Date ? "OVERDUE" : "PENDING";

            rows.Add(new FilingStatusRow
            {
                TenantId = period.TenantId,
                InstitutionName = institution.InstitutionName,
                LicenceType = institution.LicenseType ?? string.Empty,
                ModuleCode = period.Module?.ModuleCode ?? string.Empty,
                PeriodCode = RegulatorAnalyticsSupport.FormatPeriodCode(period),
                DeadlineDate = period.DeadlineDate,
                SubmittedAt = hasAccepted ? submittedAt : null,
                Status = status
            });
        }

        return rows
            .OrderByDescending(x => x.Status == "OVERDUE")
            .ThenBy(x => x.DeadlineDate)
            .ToList();
    }

    private async Task<List<SubmissionSnapshot>> LoadLatestSnapshotsAsync(
        MetadataDbContext db,
        string regulatorCode,
        List<Guid>? tenantIds,
        string? periodCode,
        CancellationToken ct,
        string? licenceCategory = null)
    {
        var snapshots = await LoadSnapshotsAsync(db, regulatorCode, tenantIds, licenceCategory, ct);
        if (!string.IsNullOrWhiteSpace(periodCode))
        {
            return snapshots.Where(x => string.Equals(x.PeriodCode, periodCode, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return snapshots
            .GroupBy(x => x.TenantId)
            .Select(g => g.OrderByDescending(x => x.ReportingDate).ThenByDescending(x => x.SubmittedAt).First())
            .ToList();
    }

    private async Task<List<SubmissionSnapshot>> LoadSnapshotsAsync(
        MetadataDbContext db,
        string regulatorCode,
        List<Guid>? tenantIds,
        string? licenceCategory,
        CancellationToken ct)
    {
        var query = db.Submissions
            .AsNoTracking()
            .Include(x => x.ReturnPeriod)
                .ThenInclude(x => x!.Module)
            .Include(x => x.Institution)
            .Where(x => x.Status == SubmissionStatus.Accepted
                        && x.ReturnPeriod != null
                        && x.ReturnPeriod.Module != null
                        && x.ReturnPeriod.Module.RegulatorCode == regulatorCode
                        && x.ParsedDataJson != null);

        if (tenantIds is not null && tenantIds.Count > 0)
        {
            query = query.Where(x => tenantIds.Contains(x.TenantId));
        }

        var submissions = await query.ToListAsync(ct);
        return submissions
            .Where(x => MatchesLicenceCategory(x.Institution?.LicenseType, licenceCategory))
            .Select(x => new SubmissionSnapshot
            {
                TenantId = x.TenantId,
                InstitutionId = x.InstitutionId,
                InstitutionName = x.Institution?.InstitutionName ?? "Unknown Institution",
                LicenceCategory = x.Institution?.LicenseType ?? string.Empty,
                ModuleCode = x.ReturnPeriod?.Module?.ModuleCode ?? x.ReturnCode,
                PeriodCode = x.ReturnPeriod is null ? string.Empty : RegulatorAnalyticsSupport.FormatPeriodCode(x.ReturnPeriod),
                ReportingDate = x.ReturnPeriod?.ReportingDate ?? x.SubmittedAt,
                SubmittedAt = x.SubmittedAt,
                ParsedDataJson = x.ParsedDataJson
            })
            .OrderBy(x => x.ReportingDate)
            .ThenBy(x => x.SubmittedAt)
            .ToList();
    }

    private static decimal? GetSnapshotMetricValue(SubmissionSnapshot snapshot, string metricCode)
    {
        var keys = ResolveMetricKeys(metricCode);
        return RegulatorAnalyticsSupport.ExtractFirstMetric(snapshot.ParsedDataJson, keys);
    }

    private static string GetMetricLabel(string metricCode)
    {
        if (MetricDefinitions.TryGetValue(metricCode, out var definition))
        {
            return definition.Label;
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(metricCode.Replace("_", " ", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetMetricSource(string metricCode)
    {
        if (MetricDefinitions.TryGetValue(metricCode, out var definition))
        {
            return definition.SourceModule;
        }

        return "RG-07";
    }

    private static List<string> ResolveRequestedMetricCodes(string query, string? primaryMetricCode)
    {
        var results = new List<string>();
        if (!string.IsNullOrWhiteSpace(primaryMetricCode))
        {
            results.Add(primaryMetricCode);
        }

        foreach (var definition in MetricDefinitions.Values.OrderByDescending(x => x.Synonyms.Max(y => y.Length)))
        {
            if (definition.Synonyms.Any(x => query.Contains(x, StringComparison.OrdinalIgnoreCase))
                && !results.Contains(definition.Code, StringComparer.OrdinalIgnoreCase))
            {
                results.Add(definition.Code);
            }
        }

        return results.Count == 0 ? new List<string> { "carratio" } : results;
    }

    private static IEnumerable<string> ResolveMetricKeys(string metricCode)
    {
        if (MetricDefinitions.TryGetValue(metricCode, out var definition))
        {
            return definition.Synonyms.Append(definition.Code);
        }

        return new[] { metricCode };
    }

    private static RegulatorTableData BuildTable(List<Dictionary<string, object?>> rows)
    {
        return new RegulatorTableData
        {
            Columns = rows.SelectMany(x => x.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Rows = rows
        };
    }

    private static string? ExtractLookupKeyword(string query)
    {
        var match = CircularReferenceRegex().Match(query);
        if (match.Success)
        {
            return match.Value.ToUpperInvariant();
        }

        var cleaned = query
            .Replace("what does", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("require", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("show", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("regulation", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? query.Trim() : cleaned;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength].TrimEnd() + "...";
    }

    private static string ResolveMetricRiskBand(string metricCode, decimal? value)
    {
        if (!value.HasValue)
        {
            return "UNKNOWN";
        }

        return metricCode.ToLowerInvariant() switch
        {
            "carratio" when value.Value < 10m => "RED",
            "carratio" when value.Value < 15m => "AMBER",
            "carratio" => "GREEN",
            "nplratio" when value.Value > 10m => "RED",
            "nplratio" when value.Value > 5m => "AMBER",
            "nplratio" => "GREEN",
            "liquidityratio" when value.Value < 20m => "RED",
            "liquidityratio" when value.Value < 30m => "AMBER",
            "liquidityratio" => "GREEN",
            "chs" when value.Value < 50m => "RED",
            "chs" when value.Value < 70m => "AMBER",
            "chs" => "GREEN",
            _ => "INFO"
        };
    }

    private static string FormatMetricValue(decimal? value, string? metricCode)
    {
        if (!value.HasValue)
        {
            return "n/a";
        }

        if (string.Equals(metricCode, "totalassets", StringComparison.OrdinalIgnoreCase))
        {
            return FormatNaira(value.Value);
        }

        return $"{value.Value:N2}%";
    }

    private static string FormatNaira(decimal value)
    {
        var absolute = Math.Abs(value);
        if (absolute >= 1_000_000_000_000m)
        {
            return $"₦{decimal.Round(value / 1_000_000_000_000m, 2):N2}T";
        }

        if (absolute >= 1_000_000_000m)
        {
            return $"₦{decimal.Round(value / 1_000_000_000m, 2):N2}B";
        }

        if (absolute >= 1_000_000m)
        {
            return $"₦{decimal.Round(value / 1_000_000m, 2):N2}M";
        }

        return $"₦{value:N2}";
    }

    private static List<string> BuildDataSources(string primary, IEnumerable<string>? others)
    {
        return new[] { primary }
            .Concat(others ?? Enumerable.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
    }

    private static string BuildSectorSummaryAnswer(SectorIntelligenceSummary sectorSummary)
    {
        var scopeLabel = string.IsNullOrWhiteSpace(sectorSummary.LicenceCategory)
            ? "the current regulator scope"
            : $"{sectorSummary.LicenceCategory} institutions";

        return $"{sectorSummary.EntityCount} institution(s) are currently in scope for {scopeLabel}. " +
               $"Average CAR is {FormatPercent(sectorSummary.AverageCarRatio)}, Average NPL is {FormatPercent(sectorSummary.AverageNplRatio)}, " +
               $"Average liquidity is {FormatPercent(sectorSummary.AverageLiquidityRatio)}, and Average CHS is {FormatScore(sectorSummary.AverageComplianceHealthScore)}. " +
               $"{sectorSummary.OverdueFilingCount} filing(s) are overdue and {sectorSummary.HighRiskEntityCount} institution(s) are currently flagged as high risk.";
    }

    private static string FormatPercent(decimal? value)
    {
        return value.HasValue
            ? $"{value.Value:N1}%"
            : "N/A";
    }

    private static string FormatScore(decimal? value)
    {
        return value.HasValue
            ? value.Value.ToString("N1", CultureInfo.InvariantCulture)
            : "N/A";
    }

    private static bool MatchesLicenceCategory(string? actual, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        return string.Equals(actual, requested, StringComparison.OrdinalIgnoreCase)
               || string.Equals(requested, "DMB", StringComparison.OrdinalIgnoreCase)
                  && (actual.Contains("Commercial", StringComparison.OrdinalIgnoreCase)
                      || actual.Contains("Deposit", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(actual, "DMB", StringComparison.OrdinalIgnoreCase));
    }

    private static Guid? ResolveSingleEntityId(RegulatorIntentResult classifiedIntent, RegulatorContext context)
    {
        if (classifiedIntent.ResolvedEntityIds.Count > 0)
        {
            return classifiedIntent.ResolvedEntityIds[0];
        }

        return context.CurrentExaminationEntityId;
    }

    private static List<Guid> ResolveEntityIds(RegulatorIntentResult classifiedIntent, RegulatorContext context)
    {
        var ids = classifiedIntent.ResolvedEntityIds.ToList();
        if (ids.Count == 0 && context.CurrentExaminationEntityId.HasValue)
        {
            ids.Add(context.CurrentExaminationEntityId.Value);
        }

        return ids.Distinct().ToList();
    }

    private static int ParseInt(Dictionary<string, string> parameters, string key, int fallback)
    {
        return parameters.TryGetValue(key, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static decimal StandardDeviation(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0)
        {
            return 0m;
        }

        var average = values.Average();
        var variance = values.Sum(x => (x - average) * (x - average)) / values.Count;
        return (decimal)Math.Sqrt((double)variance);
    }

    private static int SeverityScore(EarlyWarningSeverity severity) => severity switch
    {
        EarlyWarningSeverity.Red => 3,
        EarlyWarningSeverity.Amber => 2,
        _ => 1
    };

    private static decimal? ParseScenarioMultiplier(string? rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return null;
        }

        if (Regex.IsMatch(rawQuery, @"\bdoubled?\b", RegexOptions.IgnoreCase))
        {
            return 2m;
        }

        if (Regex.IsMatch(rawQuery, @"\btripled?\b", RegexOptions.IgnoreCase))
        {
            return 3m;
        }

        var increase = Regex.Match(rawQuery, @"(\d+(?:\.\d+)?)\s*%\s*(?:increase|rise|growth)", RegexOptions.IgnoreCase);
        if (increase.Success && decimal.TryParse(increase.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var pct))
        {
            return 1m + (pct / 100m);
        }

        return null;
    }

    private async Task<List<Guid>> ResolveTenantIdsByInstitutionNamesAsync(MetadataDbContext db, List<string> institutionNames, CancellationToken ct)
    {
        if (institutionNames.Count == 0)
        {
            return new List<Guid>();
        }

        return await db.Institutions
            .AsNoTracking()
            .Where(x => institutionNames.Contains(x.InstitutionName))
            .Select(x => x.TenantId)
            .Distinct()
            .ToListAsync(ct);
    }

    private async Task<List<string>> ResolveInstitutionNamesAsync(MetadataDbContext db, List<Guid> tenantIds, CancellationToken ct)
    {
        if (tenantIds.Count == 0)
        {
            return new List<string>();
        }

        return await db.Institutions
            .AsNoTracking()
            .Where(x => tenantIds.Contains(x.TenantId))
            .Select(x => x.InstitutionName)
            .Distinct()
            .ToListAsync(ct);
    }

    private async Task<string?> ResolveLatestPeriodCodeAsync(MetadataDbContext db, string regulatorCode, CancellationToken ct)
    {
        var period = await db.ReturnPeriods
            .AsNoTracking()
            .Include(x => x.Module)
            .Where(x => x.Module != null && x.Module.RegulatorCode == regulatorCode)
            .OrderByDescending(x => x.ReportingDate)
            .FirstOrDefaultAsync(ct);

        return period is null ? null : RegulatorAnalyticsSupport.FormatPeriodCode(period);
    }

    private async Task<int?> ResolveFinancialGroupIdAsync(
        MetadataDbContext db,
        string originalQuery,
        List<Guid> entityTenantIds,
        CancellationToken ct)
    {
        if (entityTenantIds.Count > 0)
        {
            var institutionIds = await db.Institutions
                .AsNoTracking()
                .Where(x => entityTenantIds.Contains(x.TenantId))
                .Select(x => x.Id)
                .ToListAsync(ct);

            var groupId = await db.GroupSubsidiaries
                .AsNoTracking()
                .Where(x => institutionIds.Contains(x.InstitutionId) && x.IsActive)
                .Select(x => (int?)x.GroupId)
                .FirstOrDefaultAsync(ct);

            if (groupId.HasValue)
            {
                return groupId;
            }
        }

        var normalized = originalQuery.Trim();
        return await db.FinancialGroups
            .AsNoTracking()
            .Where(x => x.IsActive
                        && (EF.Functions.Like(x.GroupName, $"%{normalized}%")
                            || EF.Functions.Like(x.GroupCode, $"%{normalized}%")))
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(ct);
    }

    private static StressScenarioInfo? TryResolveScenario(string query, IReadOnlyList<StressScenarioInfo> scenarios)
    {
        return scenarios.FirstOrDefault(x =>
            query.Contains(x.Name, StringComparison.OrdinalIgnoreCase)
            || query.Contains(x.Type.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private static PolicyDomain? TryResolvePolicyDomain(string query)
    {
        if (Regex.IsMatch(query, @"\b(capital|car|crr)\b", RegexOptions.IgnoreCase))
        {
            return PolicyDomain.CapitalAdequacy;
        }

        if (Regex.IsMatch(query, @"\b(liquidity|lcr|nsfr)\b", RegexOptions.IgnoreCase))
        {
            return PolicyDomain.Liquidity;
        }

        if (Regex.IsMatch(query, @"\b(reporting|filing|disclosure)\b", RegexOptions.IgnoreCase))
        {
            return PolicyDomain.RiskManagement;
        }

        return null;
    }

    private sealed class MetricDefinition
    {
        public MetricDefinition(string code, string label, string sourceModule, IReadOnlyList<string> synonyms)
        {
            Code = code;
            Label = label;
            SourceModule = sourceModule;
            Synonyms = synonyms;
        }

        public string Code { get; }
        public string Label { get; }
        public string SourceModule { get; }
        public IReadOnlyList<string> Synonyms { get; }
    }

    private sealed class SubmissionSnapshot
    {
        public Guid TenantId { get; set; }
        public int InstitutionId { get; set; }
        public string InstitutionName { get; set; } = string.Empty;
        public string LicenceCategory { get; set; } = string.Empty;
        public string ModuleCode { get; set; } = string.Empty;
        public string PeriodCode { get; set; } = string.Empty;
        public DateTime ReportingDate { get; set; }
        public DateTime SubmittedAt { get; set; }
        public string? ParsedDataJson { get; set; }
    }

    private sealed class FilingStatusRow
    {
        public Guid TenantId { get; set; }
        public string InstitutionName { get; set; } = string.Empty;
        public string LicenceType { get; set; } = string.Empty;
        public string ModuleCode { get; set; } = string.Empty;
        public string PeriodCode { get; set; } = string.Empty;
        public DateTime DeadlineDate { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    [GeneratedRegex(@"[A-Z]{2,5}/[A-Z]{2,6}/\d{4}/\d{3}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CircularReferenceRegex();

    [GeneratedRegex(@"(\d{4})-Q([1-4])", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex QuarterPeriodRegex();
}
