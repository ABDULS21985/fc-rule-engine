using System.Globalization;
using System.Text.Json;
using Dapper;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Audit;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed class ForeSightService : IForeSightService
{
    private readonly MetadataDbContext _db;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ForeSightService> _logger;

    public ForeSightService(
        MetadataDbContext db,
        IDbConnectionFactory connectionFactory,
        IMemoryCache cache,
        ILogger<ForeSightService> logger)
    {
        _db = db;
        _connectionFactory = connectionFactory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ForeSightDashboardData> GetTenantDashboardAsync(Guid tenantId, CancellationToken ct = default)
    {
        var predictions = await GetPredictionsAsync(tenantId, null, ct);
        var alerts = await GetAlertsAsync(tenantId, true, ct);

        var filingRisks = predictions
            .Where(x => x.ModelCode == ForeSightModelCodes.FilingRisk)
            .OrderByDescending(x => x.PredictedValue)
            .Select(x => new FilingRiskForecast
            {
                ModuleCode = x.TargetModuleCode,
                ModuleName = string.IsNullOrWhiteSpace(x.TargetLabel) ? x.TargetModuleCode : x.TargetLabel,
                PeriodCode = x.TargetPeriodCode,
                DueDate = x.HorizonDate ?? x.PredictionDate,
                DaysToDeadline = (int)((x.HorizonDate ?? x.PredictionDate).Date - DateTime.UtcNow.Date).TotalDays,
                ProbabilityLate = x.PredictedValue,
                ConfidenceScore = x.ConfidenceScore,
                ConfidenceLower = x.ConfidenceLower ?? x.PredictedValue,
                ConfidenceUpper = x.ConfidenceUpper ?? x.PredictedValue,
                RiskBand = x.RiskBand,
                Explanation = x.Explanation,
                RootCauseNarrative = x.RootCauseNarrative,
                Recommendation = x.Recommendation,
                TopFactors = x.Features.OrderByDescending(f => f.ContributionScore).Take(3).ToList()
            })
            .ToList();

        var capitalForecasts = predictions
            .Where(x => x.ModelCode == ForeSightModelCodes.CapitalBreach)
            .OrderByDescending(x => x.RiskBand)
            .ThenByDescending(x => x.PredictedValue)
            .Select(x => new CapitalForecastSummary
            {
                MetricCode = x.TargetMetric,
                MetricLabel = string.IsNullOrWhiteSpace(x.TargetLabel) ? x.TargetMetric : x.TargetLabel,
                CurrentValue = x.Features.FirstOrDefault(f => f.FeatureName == "current_value")?.RawValue ?? x.PredictedValue,
                ProjectedValue = x.PredictedValue,
                ThresholdValue = x.Features.FirstOrDefault(f => f.FeatureName == "threshold_value")?.RawValue
                    ?? (x.Features.FirstOrDefault(f => f.FeatureName == "threshold_buffer")?.RawValue is { } buffer
                    ? Math.Round(x.PredictedValue - buffer, 2)
                    : 0m),
                ConfidenceScore = x.ConfidenceScore,
                ConfidenceLower = x.ConfidenceLower ?? x.PredictedValue,
                ConfidenceUpper = x.ConfidenceUpper ?? x.PredictedValue,
                HorizonLabel = x.HorizonLabel,
                RiskBand = x.RiskBand,
                BreachPredicted = x.RiskBand is "CRITICAL" or "HIGH",
                Explanation = x.Explanation,
                RootCauseNarrative = x.RootCauseNarrative,
                Recommendation = x.Recommendation,
                TopFactors = x.Features.OrderByDescending(f => f.ContributionScore).Take(3).ToList()
            })
            .ToList();

        var compliancePrediction = predictions
            .Where(x => x.ModelCode == ForeSightModelCodes.ComplianceTrend)
            .OrderByDescending(x => x.PredictionDate)
            .ThenByDescending(x => x.CreatedAtUtc())
            .FirstOrDefault();

        return new ForeSightDashboardData
        {
            TenantId = tenantId,
            GeneratedAt = predictions.Count == 0 ? DateTime.UtcNow : predictions.Max(x => x.PredictionDate),
            FilingRisks = filingRisks,
            CapitalForecasts = capitalForecasts,
            ComplianceForecast = compliancePrediction is null
                ? null
                : new ComplianceScoreForecast
                {
                    CurrentScore = compliancePrediction.Features.FirstOrDefault(f => f.FeatureName == "current_score")?.RawValue ?? compliancePrediction.PredictedValue,
                    ProjectedScore = compliancePrediction.PredictedValue,
                    ScoreChange = compliancePrediction.Features.FirstOrDefault(f => f.FeatureName == "projected_change")?.RawValue ?? 0m,
                    CurrentRating = ForeSightSupport.RatingLabel(compliancePrediction.Features.FirstOrDefault(f => f.FeatureName == "current_score")?.RawValue ?? compliancePrediction.PredictedValue),
                    ProjectedRating = ForeSightSupport.RatingLabel(compliancePrediction.PredictedValue),
                    RiskBand = compliancePrediction.RiskBand,
                    ConfidenceScore = compliancePrediction.ConfidenceScore,
                    ConfidenceLower = compliancePrediction.ConfidenceLower ?? compliancePrediction.PredictedValue,
                    ConfidenceUpper = compliancePrediction.ConfidenceUpper ?? compliancePrediction.PredictedValue,
                    DecliningPillar = compliancePrediction.RootCausePillar,
                    Explanation = compliancePrediction.Explanation,
                    RootCauseNarrative = compliancePrediction.RootCauseNarrative,
                    Recommendation = compliancePrediction.Recommendation,
                    TopFactors = compliancePrediction.Features.OrderByDescending(f => f.ContributionScore).Take(3).ToList()
                },
            Alerts = alerts.ToList()
        };
    }

    public async Task<IReadOnlyList<ForeSightPredictionSummary>> GetPredictionsAsync(Guid tenantId, string? modelCode = null, CancellationToken ct = default)
    {
        var query = _db.ForeSightPredictions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && !x.IsSuppressed);

        if (!string.IsNullOrWhiteSpace(modelCode))
        {
            query = query.Where(x => x.ModelCode == modelCode);
        }

        var rows = await query
            .OrderByDescending(x => x.PredictionDate)
            .ThenByDescending(x => x.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        return rows.Select(MapPrediction).ToList();
    }

    public async Task<IReadOnlyList<ForeSightAlertItem>> GetAlertsAsync(Guid tenantId, bool unreadOnly = true, CancellationToken ct = default)
    {
        var query = _db.ForeSightAlerts
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && !x.IsDismissed);

        if (unreadOnly)
        {
            query = query.Where(x => !x.IsRead);
        }

        var rows = await query
            .OrderByDescending(x => x.DispatchedAt)
            .Take(unreadOnly ? 20 : 50)
            .ToListAsync(ct);

        return rows.Select(MapAlert).ToList();
    }

    public async Task MarkAlertReadAsync(int alertId, string userId, CancellationToken ct = default)
    {
        var alert = await _db.ForeSightAlerts.FirstOrDefaultAsync(x => x.Id == alertId, ct);
        if (alert is null)
        {
            return;
        }

        alert.IsRead = true;
        alert.ReadBy = userId;
        alert.ReadAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await WriteAuditEntryAsync(alert.TenantId, "ForeSightAlert", alert.Id, "READ", new { alert.AlertType, alert.Title }, userId, ct);
    }

    public async Task DismissAlertAsync(int alertId, string userId, CancellationToken ct = default)
    {
        var alert = await _db.ForeSightAlerts.FirstOrDefaultAsync(x => x.Id == alertId, ct);
        if (alert is null)
        {
            return;
        }

        alert.IsDismissed = true;
        alert.DismissedBy = userId;
        alert.DismissedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await WriteAuditEntryAsync(alert.TenantId, "ForeSightAlert", alert.Id, "DISMISSED", new { alert.AlertType, alert.Title }, userId, ct);
    }

    public async Task RunAllPredictionsAsync(Guid tenantId, string performedBy = "FORESIGHT", CancellationToken ct = default)
    {
        var context = await ResolveInstitutionContextAsync(tenantId, ct);
        if (context is null)
        {
            _logger.LogInformation("ForeSight skipped for tenant {TenantId} because no institution context was found.", tenantId);
            return;
        }

        var config = await GetConfigMapAsync(ct);
        var generated = new List<GeneratedPrediction>();

        try
        {
            generated.AddRange(await BuildFilingRiskPredictionsAsync(context, config, ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ForeSight filing-risk generation failed for tenant {TenantId}.", tenantId);
        }

        try
        {
            generated.AddRange(await BuildCapitalForecastPredictionsAsync(context, config, ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ForeSight capital forecast generation failed for tenant {TenantId}.", tenantId);
        }

        try
        {
            var compliance = await BuildCompliancePredictionAsync(context, config, ct);
            if (compliance is not null)
            {
                generated.Add(compliance);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ForeSight compliance-trend generation failed for tenant {TenantId}.", tenantId);
        }

        try
        {
            var churn = await BuildChurnPredictionAsync(context, config, ct);
            if (churn is not null)
            {
                generated.Add(churn);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ForeSight churn generation failed for tenant {TenantId}.", tenantId);
        }

        try
        {
            var regulatoryAction = await BuildRegulatoryActionPredictionAsync(context, config, ct);
            if (regulatoryAction is not null)
            {
                generated.Add(regulatoryAction);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ForeSight regulatory-action generation failed for tenant {TenantId}.", tenantId);
        }

        foreach (var prediction in generated)
        {
            var persisted = await SavePredictionAsync(context.TenantId, prediction, config, performedBy, ct);
            if (persisted is not null && !persisted.IsSuppressed && prediction.Alert is not null)
            {
                await SaveAlertIfNeededAsync(persisted, prediction.Alert, performedBy, ct);
            }
        }

        await WriteAuditEntryAsync(
            tenantId,
            "ForeSightRun",
            0,
            "PREDICTIONS_GENERATED",
            new { Count = generated.Count, Tenant = context.TenantName },
            performedBy,
            ct);
    }

    public async Task<IReadOnlyList<RegulatoryActionForecast>> GetRegulatoryRiskRankingAsync(string regulatorCode, string? licenceType = null, CancellationToken ct = default)
    {
        var latestDate = await _db.ForeSightPredictions
            .AsNoTracking()
            .Where(x => x.ModelCode == ForeSightModelCodes.RegulatoryAction && !x.IsSuppressed)
            .MaxAsync(x => (DateTime?)x.PredictionDate, ct);

        if (!latestDate.HasValue)
        {
            return Array.Empty<RegulatoryActionForecast>();
        }

        var tenantLicenceRows = await _db.TenantLicenceTypes
            .AsNoTracking()
            .Include(x => x.LicenceType)
            .Where(x => x.IsActive
                        && x.LicenceType != null
                        && x.LicenceType.Regulator == regulatorCode
                        && (licenceType == null || x.LicenceType.Code == licenceType))
            .ToListAsync(ct);

        var relevantTenantIds = tenantLicenceRows.Select(x => x.TenantId).Distinct().ToList();
        if (relevantTenantIds.Count == 0)
        {
            return Array.Empty<RegulatoryActionForecast>();
        }

        var predictions = await _db.ForeSightPredictions
            .AsNoTracking()
            .Where(x => x.ModelCode == ForeSightModelCodes.RegulatoryAction
                        && x.PredictionDate == latestDate.Value
                        && !x.IsSuppressed
                        && relevantTenantIds.Contains(x.TenantId))
            .OrderByDescending(x => x.PredictedValue)
            .ToListAsync(ct);

        var institutions = await _db.Institutions
            .AsNoTracking()
            .Where(x => relevantTenantIds.Contains(x.TenantId) && x.IsActive)
            .GroupBy(x => x.TenantId)
            .Select(g => g.OrderByDescending(i => i.LastSubmissionAt).ThenBy(i => i.Id).First())
            .ToListAsync(ct);

        var institutionMap = institutions.ToDictionary(x => x.TenantId);
        var licenceMap = tenantLicenceRows
            .GroupBy(x => x.TenantId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.EffectiveDate).First().LicenceType?.Code ?? string.Empty);

        return predictions.Select(prediction =>
        {
            var institution = institutionMap.GetValueOrDefault(prediction.TenantId);
            var features = ForeSightSupport.DeserializeFeatures(prediction.FeatureImportanceJson);
            return new RegulatoryActionForecast
            {
                TenantId = prediction.TenantId,
                InstitutionId = institution?.Id ?? 0,
                InstitutionName = institution?.InstitutionName ?? prediction.TargetLabel,
                LicenceType = licenceMap.GetValueOrDefault(prediction.TenantId, string.Empty),
                RegulatorCode = regulatorCode,
                InterventionProbability = prediction.PredictedValue,
                ConfidenceScore = prediction.ConfidenceScore,
                ConfidenceLower = prediction.ConfidenceLower ?? prediction.PredictedValue,
                ConfidenceUpper = prediction.ConfidenceUpper ?? prediction.PredictedValue,
                RiskBand = prediction.RiskBand,
                Explanation = prediction.Explanation,
                RootCauseNarrative = prediction.RootCauseNarrative,
                Recommendation = prediction.Recommendation,
                TopFactors = features.OrderByDescending(x => x.ContributionScore).Take(3).ToList()
            };
        }).ToList();
    }

    public async Task<IReadOnlyList<ChurnRiskAssessment>> GetChurnRiskDashboardAsync(CancellationToken ct = default)
    {
        var latestDate = await _db.ForeSightPredictions
            .AsNoTracking()
            .Where(x => x.ModelCode == ForeSightModelCodes.ChurnRisk && !x.IsSuppressed)
            .MaxAsync(x => (DateTime?)x.PredictionDate, ct);

        if (!latestDate.HasValue)
        {
            return Array.Empty<ChurnRiskAssessment>();
        }

        var predictions = await _db.ForeSightPredictions
            .AsNoTracking()
            .Where(x => x.ModelCode == ForeSightModelCodes.ChurnRisk
                        && x.PredictionDate == latestDate.Value
                        && !x.IsSuppressed)
            .OrderByDescending(x => x.PredictedValue)
            .ToListAsync(ct);

        var tenants = await _db.Tenants
            .AsNoTracking()
            .Where(x => predictions.Select(p => p.TenantId).Contains(x.TenantId))
            .ToListAsync(ct);

        var tenantMap = tenants.ToDictionary(x => x.TenantId);
        var licenceMap = await _db.TenantLicenceTypes
            .AsNoTracking()
            .Include(x => x.LicenceType)
            .Where(x => x.IsActive && predictions.Select(p => p.TenantId).Contains(x.TenantId))
            .GroupBy(x => x.TenantId)
            .Select(g => g.OrderByDescending(x => x.EffectiveDate).First())
            .ToDictionaryAsync(x => x.TenantId, x => x.LicenceType != null ? x.LicenceType.Code : string.Empty, ct);

        return predictions.Select(prediction =>
        {
            var tenant = tenantMap.GetValueOrDefault(prediction.TenantId);
            var features = ForeSightSupport.DeserializeFeatures(prediction.FeatureImportanceJson);
            return new ChurnRiskAssessment
            {
                TenantId = prediction.TenantId,
                TenantName = tenant?.TenantName ?? prediction.TargetLabel,
                LicenceType = licenceMap.GetValueOrDefault(prediction.TenantId, string.Empty),
                ChurnProbability = prediction.PredictedValue,
                ConfidenceScore = prediction.ConfidenceScore,
                ConfidenceLower = prediction.ConfidenceLower ?? prediction.PredictedValue,
                ConfidenceUpper = prediction.ConfidenceUpper ?? prediction.PredictedValue,
                RiskBand = prediction.RiskBand,
                Explanation = prediction.Explanation,
                RootCauseNarrative = prediction.RootCauseNarrative,
                Recommendation = prediction.Recommendation,
                TopFactors = features.OrderByDescending(x => x.ContributionScore).Take(3).ToList()
            };
        }).ToList();
    }

    public async Task<byte[]> ExportFilingRiskReportAsync(Guid tenantId, CancellationToken ct = default)
    {
        var dashboard = await GetTenantDashboardAsync(tenantId, ct);
        var tenantName = await _db.Tenants
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => x.TenantName)
            .FirstOrDefaultAsync(ct) ?? "Unknown tenant";

        return new ForeSightFilingRiskReportDocument(tenantName, dashboard.FilingRisks).GeneratePdf();
    }

    private async Task<List<GeneratedPrediction>> BuildFilingRiskPredictionsAsync(InstitutionContext context, IReadOnlyDictionary<string, string> config, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var upcomingPeriods = await _db.ReturnPeriods
            .AsNoTracking()
            .Include(x => x.Module)
            .Where(x => x.TenantId == context.TenantId
                        && x.ModuleId.HasValue
                        && x.Module != null
                        && (x.DeadlineOverrideDate ?? x.DeadlineDate) >= today
                        && (x.DeadlineOverrideDate ?? x.DeadlineDate) <= today.AddDays(30)
                        && x.Status != "Completed"
                        && x.Status != "Closed")
            .OrderBy(x => x.DeadlineOverrideDate ?? x.DeadlineDate)
            .ToListAsync(ct);

        var mediumThreshold = ForeSightSupport.ParseDecimal(config.GetValueOrDefault("filing.risk_medium_threshold"), 0.40m);
        var highThreshold = ForeSightSupport.ParseDecimal(config.GetValueOrDefault("filing.risk_high_threshold"), 0.70m);
        var minHistoryPeriods = ForeSightSupport.ParseInt(config.GetValueOrDefault("filing.min_history_periods"), 4);
        var alertHorizonDays = ForeSightSupport.ParseInt(config.GetValueOrDefault("filing.alert_horizon_days"), 14);
        var escalationDays = ForeSightSupport.ParseInt(config.GetValueOrDefault("filing.escalation_horizon_days"), 7);
        var criticalDays = ForeSightSupport.ParseInt(config.GetValueOrDefault("filing.critical_horizon_days"), 3);
        var weights = await GetFeatureWeightsAsync(ForeSightModelCodes.FilingRisk, ct);

        var predictions = new List<GeneratedPrediction>();

        foreach (var period in upcomingPeriods)
        {
            var module = period.Module;
            if (module is null)
            {
                continue;
            }

            var dueDate = period.DeadlineOverrideDate ?? period.DeadlineDate;
            var daysToDeadline = (int)(dueDate.Date - today).TotalDays;
            var periodCode = ForeSightSupport.FormatPeriodCode(period.Year, period.Month, period.Quarter);

            var filingHistory = await _db.FilingSlaRecords
                .AsNoTracking()
                .Where(x => x.TenantId == context.TenantId && x.ModuleId == period.ModuleId)
                .OrderByDescending(x => x.PeriodEndDate)
                .Take(8)
                .ToListAsync(ct);

            var latestTemplateVersionId = await _db.TemplateVersions
                .AsNoTracking()
                .Where(x => x.Status == TemplateStatus.Published
                            && _db.ReturnTemplates.Any(t => t.Id == x.TemplateId && t.ModuleId == period.ModuleId))
                .OrderByDescending(x => x.VersionNumber)
                .Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(ct);

            var expectedFields = latestTemplateVersionId.HasValue
                ? await _db.TemplateFields.AsNoTracking().CountAsync(x => x.TemplateVersionId == latestTemplateVersionId.Value, ct)
                : 0;

            var latestDraft = await _db.ReturnDrafts
                .AsNoTracking()
                .Where(x => x.TenantId == context.TenantId
                            && x.ReturnCode == period.Module!.ModuleCode)
                .OrderByDescending(x => x.LastSavedAt)
                .FirstOrDefaultAsync(ct);

            var filledValues = ForeSightSupport.CountNonEmptyJsonValues(latestDraft?.DataJson);
            var completeness = expectedFields <= 0
                ? (filledValues > 0 ? 0.50m : 0m)
                : ForeSightSupport.Clamp((decimal)filledValues / expectedFields);

            var latestSubmission = await _db.Submissions
                .AsNoTracking()
                .Where(x => x.TenantId == context.TenantId && x.ReturnPeriodId == period.Id)
                .OrderByDescending(x => x.SubmittedAt)
                .FirstOrDefaultAsync(ct);

            var loginsLast7Days = await _db.LoginAttempts
                .AsNoTracking()
                .Where(x => x.TenantId == context.TenantId && x.Succeeded && x.AttemptedAt >= DateTime.UtcNow.AddDays(-7))
                .CountAsync(ct);

            var latestAnomaly = await _db.AnomalyReports
                .AsNoTracking()
                .Where(x => x.TenantId == context.TenantId && x.ModuleCode == module.ModuleCode)
                .OrderByDescending(x => x.AnalysedAt)
                .FirstOrDefaultAsync(ct);

            var concurrentFilings = await _db.ReturnPeriods
                .AsNoTracking()
                .Where(x => x.TenantId == context.TenantId
                            && x.Id != period.Id
                            && (x.DeadlineOverrideDate ?? x.DeadlineDate) >= dueDate.AddDays(-14)
                            && (x.DeadlineOverrideDate ?? x.DeadlineDate) <= dueDate.AddDays(14)
                            && x.Status != "Completed"
                            && x.Status != "Closed")
                .CountAsync(ct);

            var lateRate = filingHistory.Count == 0
                ? 0m
                : (decimal)filingHistory.Count(x => x.OnTime == false) / filingHistory.Count;
            var recentLateCount = filingHistory.Count(x => x.OnTime == false);

            var features = new List<ForeSightPredictionFeature>
            {
                CreateFeature("days_to_deadline", "Days to Deadline", daysToDeadline, ForeSightSupport.Clamp(1m - (Math.Max(daysToDeadline, 0) / 30m)), weights),
                CreateFeature("historical_late_rate", "Historical Late Rate", lateRate, lateRate, weights),
                CreateFeature("draft_completeness_gap", "Draft Completeness Gap", 1m - completeness, 1m - completeness, weights),
                CreateFeature("preparation_stage", "Preparation Stage", StageRisk(latestSubmission?.Status), StageRisk(latestSubmission?.Status), weights),
                CreateFeature("login_activity_gap", "Login Activity Gap", loginsLast7Days, ForeSightSupport.Clamp(1m - (loginsLast7Days / 20m)), weights),
                CreateFeature("recent_late_count", "Recent Late Count", recentLateCount, ForeSightSupport.Clamp(recentLateCount / 5m), weights),
                CreateFeature("anomaly_pressure", "Anomaly Pressure", latestAnomaly?.OverallQualityScore ?? 100m, ForeSightSupport.Clamp(1m - ((latestAnomaly?.OverallQualityScore ?? 100m) / 100m)), weights),
                CreateFeature("concurrent_filings", "Concurrent Filing Load", concurrentFilings, ForeSightSupport.Clamp(concurrentFilings / 5m), weights)
            };

            var score = features.Sum(x => x.ContributionScore);
            var probability = ForeSightSupport.Logistic(score);
            var coverage = expectedFields > 0 || filingHistory.Count > 0 ? 1m : 0.55m;
            var confidence = ForeSightSupport.ComputeConfidence(
                observations: filingHistory.Count,
                targetObservations: Math.Max(minHistoryPeriods, 4),
                dataCoverage: coverage,
                volatilityPenalty: ForeSightSupport.Clamp(lateRate * 0.15m));

            var margin = 0.08m + ((1m - confidence) * 0.22m);
            var lower = ForeSightSupport.Clamp(probability - margin);
            var upper = ForeSightSupport.Clamp(probability + margin);
            var riskBand = probability >= highThreshold
                ? "HIGH"
                : probability >= mediumThreshold
                    ? "MEDIUM"
                    : "LOW";

            var topFactors = features.OrderByDescending(x => x.ContributionScore).Take(3).ToList();
            var explanation = $"ForeSight estimates a {probability:P0} chance of late filing for {module.ModuleName} {periodCode}. The return is {daysToDeadline} day(s) from deadline, and the dominant pressure comes from {ForeSightSupport.HumanizeFactorList(topFactors)}.";
            var rootCause = BuildFilingRootCause(topFactors, completeness, lateRate, loginsLast7Days, daysToDeadline);
            var recommendation = BuildFilingRecommendation(topFactors, completeness, daysToDeadline);

            AlertPlan? alert = null;
            if (riskBand is "HIGH" or "CRITICAL")
            {
                if (daysToDeadline <= criticalDays)
                {
                    alert = new AlertPlan("FILING_RISK", "CRITICAL", "ComplianceOfficer,InstitutionAdmin");
                }
                else if (daysToDeadline <= escalationDays)
                {
                    alert = new AlertPlan("FILING_RISK", "WARNING", "ComplianceOfficer,InstitutionAdmin");
                }
                else if (daysToDeadline <= alertHorizonDays)
                {
                    alert = new AlertPlan("FILING_RISK", "WARNING", "ComplianceOfficer");
                }
            }

            predictions.Add(new GeneratedPrediction
            {
                ModelCode = ForeSightModelCodes.FilingRisk,
                HorizonLabel = $"T-{Math.Max(daysToDeadline, 0)}d",
                HorizonDate = dueDate,
                PredictedValue = probability,
                ConfidenceLower = lower,
                ConfidenceUpper = upper,
                ConfidenceScore = confidence,
                RiskBand = riskBand,
                TargetModuleCode = module.ModuleCode,
                TargetPeriodCode = periodCode,
                TargetMetric = "LATE_FILING_PROBABILITY",
                TargetLabel = module.ModuleName,
                Explanation = explanation,
                RootCauseNarrative = rootCause,
                Recommendation = recommendation,
                RootCausePillar = topFactors.FirstOrDefault()?.FeatureLabel ?? "Filing Timeliness",
                FeatureImportanceJson = ForeSightSupport.SerializeFeatures(features),
                Features = features,
                Alert = alert,
                HasLowData = filingHistory.Count < minHistoryPeriods,
                LowDataReason = $"Only {filingHistory.Count} historical filing period(s) were available."
            });
        }

        return predictions;
    }

    private async Task<List<GeneratedPrediction>> BuildCapitalForecastPredictionsAsync(InstitutionContext context, IReadOnlyDictionary<string, string> config, CancellationToken ct)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(null, ct);

        var minDataPoints = ForeSightSupport.ParseInt(config.GetValueOrDefault("capital.min_data_points"), 6);
        var forecastQuarters = ForeSightSupport.ParseInt(config.GetValueOrDefault("capital.forecast_quarters"), 2);
        var warningBuffer = ForeSightSupport.ParseDecimal(config.GetValueOrDefault("capital.warning_buffer"), 2m);
        var weights = await GetFeatureWeightsAsync(ForeSightModelCodes.CapitalBreach, ct);

        var history = (await conn.QueryAsync<PrudentialMetricRow>(
            """
            SELECT PeriodCode, AsOfDate, CAR, NPLRatio, LCR, ProvisioningCoverage
            FROM   meta.prudential_metrics
            WHERE  InstitutionId = @InstitutionId
            ORDER BY AsOfDate ASC
            """,
            new { context.InstitutionId })).ToList();

        if (history.Count == 0)
        {
            return new List<GeneratedPrediction>();
        }

        var activeEwiCount = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM   meta.ewi_triggers
            WHERE  InstitutionId = @InstitutionId
              AND  IsActive = 1
            """,
            new { context.InstitutionId });

        var results = new List<GeneratedPrediction>();

        foreach (var metric in new[]
        {
            new MetricDefinition("CAR", "Capital Adequacy Ratio", history.Where(x => x.CAR.HasValue).Select(x => x.CAR!.Value).ToList()),
            new MetricDefinition("NPL", "Non-Performing Loan Ratio", history.Where(x => x.NPLRatio.HasValue).Select(x => x.NPLRatio!.Value).ToList()),
            new MetricDefinition("LCR", "Liquidity Coverage Ratio", history.Where(x => x.LCR.HasValue).Select(x => x.LCR!.Value).ToList())
        })
        {
            if (metric.Values.Count == 0)
            {
                continue;
            }

            var thresholds = await GetThresholdsAsync(context.RegulatorCode, context.LicenceTypeCode, metric.Code, ct);
            if (thresholds.Count == 0)
            {
                continue;
            }

            var current = metric.Values[^1];
            var forecast = ForeSightSupport.ForecastHoltLinear(metric.Values, forecastQuarters);
            var volatility = ForeSightSupport.StandardDeviation(metric.Values);
            var slope = ForeSightSupport.CalculateSlope(metric.Values);
            var lastHistory = history[^1];

            var projected = forecast.LastOrDefault(current);
            var thresholdBuffer = ThresholdBuffer(metric.Code, current, thresholds);
            var breach = DetectProjectedBreach(metric.Code, forecast, thresholds, lastHistory.PeriodCode);

            var creditStress = metric.Code switch
            {
                "CAR" => lastHistory.NPLRatio ?? 0m,
                "NPL" => lastHistory.CAR is { } lastCar
                    ? Math.Max(0m, 15m - lastCar)
                    : 0m,
                _ => lastHistory.ProvisioningCoverage is { } lastCoverage
                    ? Math.Max(0m, 100m - lastCoverage)
                    : 0m
            };

            var features = new List<ForeSightPredictionFeature>
            {
                CreateInformationalFeature("current_value", "Current Value", current),
                CreateInformationalFeature("threshold_value", "Threshold Value", breach.ThresholdValue),
                CreateFeature("threshold_buffer", "Threshold Buffer", thresholdBuffer, ForeSightSupport.Clamp(Math.Abs(thresholdBuffer) / 10m), weights, metric.Code == "NPL" ? "INCREASES_RISK" : "DECREASES_RESILIENCE"),
                CreateFeature("trend_slope", "Trend Slope", slope, slope < 0m && metric.Code != "NPL"
                    ? ForeSightSupport.Clamp(Math.Abs(slope) / 3m)
                    : metric.Code == "NPL" && slope > 0m
                        ? ForeSightSupport.Clamp(slope / 3m)
                        : 0m, weights),
                CreateFeature("volatility", "Volatility", volatility, ForeSightSupport.Clamp(volatility / Math.Max(1m, current == 0m ? 1m : Math.Abs(current))), weights),
                CreateFeature("credit_stress_proxy", "Credit Stress Proxy", creditStress, ForeSightSupport.Clamp(creditStress / 10m), weights),
                CreateFeature("ewi_pressure", "EWI Pressure", activeEwiCount, ForeSightSupport.Clamp(activeEwiCount / 5m), weights)
            };

            var confidence = ForeSightSupport.ComputeConfidence(metric.Values.Count, minDataPoints, 1m, ForeSightSupport.Clamp(volatility / Math.Max(10m, Math.Abs(current))));
            var margin = Math.Max(volatility * (1.15m - confidence), Math.Abs(current) * (0.03m + ((1m - confidence) * 0.05m)));
            var lower = projected - margin;
            var upper = projected + margin;
            var riskBand = DetermineCapitalRiskBand(metric.Code, current, thresholdBuffer, forecast, thresholds, warningBuffer);
            var explanation = BuildCapitalExplanation(metric.Label, current, projected, breach, thresholds, forecastQuarters);
            var topFactors = features.OrderByDescending(x => x.ContributionScore).Take(3).ToList();
            var rootCause = $"Projected {metric.Label.ToLowerInvariant()} risk is being driven by {ForeSightSupport.HumanizeFactorList(topFactors)}.";
            var recommendation = BuildCapitalRecommendation(metric.Code, breach, thresholdBuffer);

            AlertPlan? alert = riskBand switch
            {
                "CRITICAL" => new AlertPlan("CAPITAL_CRITICAL", "CRITICAL", "InstitutionAdmin,ComplianceOfficer"),
                "HIGH" => new AlertPlan("CAPITAL_WARNING", "WARNING", "InstitutionAdmin,ComplianceOfficer"),
                _ => null
            };

            results.Add(new GeneratedPrediction
            {
                ModelCode = ForeSightModelCodes.CapitalBreach,
                HorizonLabel = breach.HorizonLabel,
                HorizonDate = null,
                PredictedValue = projected,
                ConfidenceLower = lower,
                ConfidenceUpper = upper,
                ConfidenceScore = confidence,
                RiskBand = riskBand,
                TargetModuleCode = string.Empty,
                TargetPeriodCode = breach.HorizonLabel,
                TargetMetric = metric.Code,
                TargetLabel = metric.Label,
                Explanation = explanation,
                RootCauseNarrative = rootCause,
                Recommendation = recommendation,
                RootCausePillar = topFactors.FirstOrDefault()?.FeatureLabel ?? "Regulatory Capital",
                FeatureImportanceJson = ForeSightSupport.SerializeFeatures(features),
                Features = features,
                Alert = alert,
                HasLowData = metric.Values.Count < minDataPoints,
                LowDataReason = $"Only {metric.Values.Count} prudential observation(s) were available for {metric.Label}."
            });
        }

        return results;
    }

    private async Task<GeneratedPrediction?> BuildCompliancePredictionAsync(InstitutionContext context, IReadOnlyDictionary<string, string> config, CancellationToken ct)
    {
        var snapshots = await _db.ChsScoreSnapshots
            .AsNoTracking()
            .Where(x => x.TenantId == context.TenantId)
            .OrderBy(x => x.ComputedAt)
            .Take(12)
            .ToListAsync(ct);

        if (snapshots.Count == 0)
        {
            return null;
        }

        var periods = ForeSightSupport.ParseInt(config.GetValueOrDefault("chs.forecast_periods"), 3);
        var declineThreshold = ForeSightSupport.ParseDecimal(config.GetValueOrDefault("chs.decline_alert_threshold"), 5m);
        var weights = await GetFeatureWeightsAsync(ForeSightModelCodes.ComplianceTrend, ct);

        var overallSeries = snapshots.Select(x => x.OverallScore).ToList();
        var current = overallSeries[^1];
        var slope = ForeSightSupport.CalculateSlope(overallSeries);
        var projected = decimal.Round((current * 0.5m) + (overallSeries.TakeLast(Math.Min(3, overallSeries.Count)).Average() * 0.3m) + ((current + (slope * periods)) * 0.2m), 2);
        projected = ForeSightSupport.Clamp(projected, 0m, 100m);
        var change = decimal.Round(projected - current, 2);
        var volatility = ForeSightSupport.StandardDeviation(overallSeries);
        var confidence = ForeSightSupport.ComputeConfidence(snapshots.Count, 8, 1m, ForeSightSupport.Clamp(volatility / 100m));
        var margin = Math.Max(1.5m, volatility * (1.2m - confidence));
        var lower = ForeSightSupport.Clamp(projected - margin, 0m, 100m);
        var upper = ForeSightSupport.Clamp(projected + margin, 0m, 100m);

        var consecutiveDeclines = CountConsecutiveDeclines(overallSeries);
        var latest = snapshots[^1];
        var pillarScores = new Dictionary<string, decimal>
        {
            ["Filing Timeliness"] = latest.FilingTimeliness,
            ["Data Quality"] = latest.DataQuality,
            ["Regulatory Capital"] = latest.RegulatoryCapital,
            ["Engagement"] = latest.Engagement
        };

        var decliningPillar = pillarScores.OrderBy(x => x.Value).First().Key;
        var features = new List<ForeSightPredictionFeature>
        {
            CreateFeature("current_score", "Current Score", current, 0m, weights, "INFORMATIONAL"),
            CreateFeature("projected_change", "Projected Change", change, change < 0m ? ForeSightSupport.Clamp(Math.Abs(change) / 10m) : 0m, weights, change < 0m ? "INCREASES_RISK" : "IMPROVES_POSTURE"),
            CreateFeature("overall_trend_slope", "Overall Trend Slope", slope, slope < 0m ? ForeSightSupport.Clamp(Math.Abs(slope) / 5m) : 0m, weights),
            CreateFeature("filing_pillar", "Filing Timeliness Pillar", latest.FilingTimeliness, ForeSightSupport.Clamp(1m - (latest.FilingTimeliness / 100m)), weights),
            CreateFeature("data_quality_pillar", "Data Quality Pillar", latest.DataQuality, ForeSightSupport.Clamp(1m - (latest.DataQuality / 100m)), weights),
            CreateFeature("capital_pillar", "Regulatory Capital Pillar", latest.RegulatoryCapital, ForeSightSupport.Clamp(1m - (latest.RegulatoryCapital / 100m)), weights),
            CreateFeature("engagement_pillar", "Engagement Pillar", latest.Engagement, ForeSightSupport.Clamp(1m - (latest.Engagement / 100m)), weights),
            CreateFeature("consecutive_declines", "Consecutive Declines", consecutiveDeclines, ForeSightSupport.Clamp(consecutiveDeclines / 4m), weights)
        };

        var riskBand = change <= -8m
            ? "CRITICAL"
            : change <= -declineThreshold
                ? "HIGH"
                : change <= -2m
                    ? "MEDIUM"
                    : "LOW";

        var topFactors = features.Where(x => x.ContributionScore > 0m).OrderByDescending(x => x.ContributionScore).Take(3).ToList();
        var explanation = $"ForeSight projects the compliance health score to move from {current:F1} to {projected:F1} over the next {periods} scoring period(s). The forecast implies a {change:+0.0;-0.0;0.0}-point move with {confidence:P0} confidence.";
        var rootCause = $"The most vulnerable pillar is {decliningPillar}, and the trend signal is being shaped by {ForeSightSupport.HumanizeFactorList(topFactors)}.";
        var recommendation = change < 0m
            ? $"Stabilise the {decliningPillar.ToLowerInvariant()} pillar first. A targeted remediation sprint should aim to recover at least {Math.Max(3m, Math.Abs(change)):F0} points before the next scoring cycle."
            : "Maintain the current control-improvement cadence and continue monitoring CHS pillars for early deterioration.";

        return new GeneratedPrediction
        {
            ModelCode = ForeSightModelCodes.ComplianceTrend,
            HorizonLabel = $"P+{periods}",
            HorizonDate = null,
            PredictedValue = projected,
            ConfidenceLower = lower,
            ConfidenceUpper = upper,
            ConfidenceScore = confidence,
            RiskBand = riskBand,
            TargetModuleCode = string.Empty,
            TargetPeriodCode = latest.PeriodLabel,
            TargetMetric = "CHS_OVERALL",
            TargetLabel = "Compliance Health Score",
            Explanation = explanation,
            RootCauseNarrative = rootCause,
            Recommendation = recommendation,
            RootCausePillar = decliningPillar,
            FeatureImportanceJson = ForeSightSupport.SerializeFeatures(features),
            Features = features,
            Alert = riskBand is "HIGH" or "CRITICAL"
                ? new AlertPlan("CHS_DECLINE", "WARNING", "ComplianceOfficer,InstitutionAdmin")
                : null,
            HasLowData = snapshots.Count < 4,
            LowDataReason = $"Only {snapshots.Count} CHS snapshot(s) were available."
        };
    }

    private async Task<GeneratedPrediction?> BuildChurnPredictionAsync(InstitutionContext context, IReadOnlyDictionary<string, string> config, CancellationToken ct)
    {
        var lookbackDays = ForeSightSupport.ParseInt(config.GetValueOrDefault("churn.lookback_days"), 90);
        var highThreshold = ForeSightSupport.ParseDecimal(config.GetValueOrDefault("churn.high_threshold"), 0.70m);
        var mediumThreshold = ForeSightSupport.ParseDecimal(config.GetValueOrDefault("churn.medium_threshold"), 0.40m);
        var since = DateTime.UtcNow.AddDays(-lookbackDays);
        var weights = await GetFeatureWeightsAsync(ForeSightModelCodes.ChurnRisk, ct);

        var hasLoginAttempts = await TableExistsAsync("meta", "login_attempts", ct);
        var hasUsageRecords = await TableExistsAsync("dbo", "usage_records", ct);
        var hasComplianceIqTurns = await TableExistsAsync("meta", "complianceiq_turns", ct);
        var hasSupportTickets = await TableExistsAsync("dbo", "partner_support_tickets", ct);
        var hasInvoices = await TableExistsAsync("dbo", "invoices", ct);
        var hasFilingSlaRecords = await TableExistsAsync("dbo", "filing_sla_records", ct);

        var recentLogins = hasLoginAttempts
            ? await _db.LoginAttempts.AsNoTracking()
                .Where(x => x.TenantId == context.TenantId && x.Succeeded && x.AttemptedAt >= DateTime.UtcNow.AddDays(-30))
                .CountAsync(ct)
            : 0;
        var priorLogins = hasLoginAttempts
            ? await _db.LoginAttempts.AsNoTracking()
                .Where(x => x.TenantId == context.TenantId
                            && x.Succeeded
                            && x.AttemptedAt >= DateTime.UtcNow.AddDays(-60)
                            && x.AttemptedAt < DateTime.UtcNow.AddDays(-30))
                .CountAsync(ct)
            : 0;
        var loginTrend = priorLogins == 0 ? 0m : (decimal)(recentLogins - priorLogins) / priorLogins;

        var usageRecent = hasUsageRecords
            ? await _db.UsageRecords.AsNoTracking()
                .Where(x => x.TenantId == context.TenantId && x.RecordDate >= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)))
                .OrderBy(x => x.RecordDate)
                .ToListAsync(ct)
            : new List<UsageRecord>();
        var usagePrior = hasUsageRecords
            ? await _db.UsageRecords.AsNoTracking()
                .Where(x => x.TenantId == context.TenantId
                            && x.RecordDate >= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-60))
                            && x.RecordDate < DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)))
                .OrderBy(x => x.RecordDate)
                .ToListAsync(ct)
            : new List<UsageRecord>();

        var recentUsageValue = usageRecent.Count == 0
            ? 0m
            : usageRecent.Average(x => (decimal)(x.ActiveUsers + x.ActiveEntities + x.ActiveModules + x.ReturnsSubmitted));
        var priorUsageValue = usagePrior.Count == 0
            ? recentUsageValue
            : usagePrior.Average(x => (decimal)(x.ActiveUsers + x.ActiveEntities + x.ActiveModules + x.ReturnsSubmitted));
        var usageDrop = priorUsageValue <= 0m ? 0m : ForeSightSupport.Clamp((priorUsageValue - recentUsageValue) / priorUsageValue);

        var complianceIqTurns = hasComplianceIqTurns
            ? await _db.ComplianceIqTurns.AsNoTracking()
                .Where(x => x.TenantId == context.TenantId && x.CreatedAt >= DateTime.UtcNow.AddDays(-30))
                .CountAsync(ct)
            : 0;

        var openTickets = hasSupportTickets
            ? await _db.PartnerSupportTickets.AsNoTracking()
                .Where(x => x.TenantId == context.TenantId
                            && x.CreatedAt >= since
                            && x.Status != PartnerSupportTicketStatus.Resolved)
                .CountAsync(ct)
            : 0;

        var overdueInvoices = hasInvoices
            ? await _db.Invoices.AsNoTracking()
                .Where(x => x.TenantId == context.TenantId
                            && x.DueDate.HasValue
                            && x.DueDate.Value < DateOnly.FromDateTime(DateTime.UtcNow)
                            && x.Status != InvoiceStatus.Paid
                            && x.Status != InvoiceStatus.Voided)
                .ToListAsync(ct)
            : new List<Invoice>();
        var paymentDelay = overdueInvoices.Count == 0
            ? 0m
            : overdueInvoices.Average(x => (decimal)(DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - x.DueDate!.Value.DayNumber));

        var recentFilings = hasFilingSlaRecords
            ? await _db.FilingSlaRecords.AsNoTracking()
                .Where(x => x.TenantId == context.TenantId && x.PeriodEndDate >= since)
                .ToListAsync(ct)
            : new List<FilingSlaRecord>();
        var filingTimeliness = recentFilings.Count == 0
            ? 1m
            : (decimal)recentFilings.Count(x => x.OnTime != false) / recentFilings.Count;

        var features = new List<ForeSightPredictionFeature>
        {
            CreateFeature("login_trend", "Login Trend", loginTrend, loginTrend < 0m ? ForeSightSupport.Clamp(Math.Abs(loginTrend)) : 0m, weights),
            CreateFeature("usage_drop", "Usage Drop", usageDrop, usageDrop, weights),
            CreateFeature("complianceiq_gap", "ComplianceIQ Activity Gap", complianceIqTurns, ForeSightSupport.Clamp(1m - (complianceIqTurns / 20m)), weights),
            CreateFeature("support_pressure", "Support Pressure", openTickets, ForeSightSupport.Clamp(openTickets / 6m), weights),
            CreateFeature("payment_delay", "Payment Delay", paymentDelay, ForeSightSupport.Clamp(paymentDelay / 30m), weights),
            CreateFeature("filing_timeliness_gap", "Filing Timeliness Gap", 1m - filingTimeliness, 1m - filingTimeliness, weights)
        };

        var probability = ForeSightSupport.Logistic(features.Sum(x => x.ContributionScore));
        var confidence = ForeSightSupport.ComputeConfidence(
            observations: usageRecent.Count + usagePrior.Count + recentFilings.Count,
            targetObservations: 12,
            dataCoverage: 0.75m + (recentFilings.Count > 0 ? 0.1m : 0m));
        var margin = 0.08m + ((1m - confidence) * 0.20m);
        var lower = ForeSightSupport.Clamp(probability - margin);
        var upper = ForeSightSupport.Clamp(probability + margin);
        var riskBand = probability >= highThreshold
            ? "HIGH"
            : probability >= mediumThreshold
                ? "MEDIUM"
                : "LOW";

        var topFactors = features.OrderByDescending(x => x.ContributionScore).Take(3).ToList();
        var explanation = $"ForeSight estimates a {probability:P0} probability that {context.TenantName} experiences subscription-churn pressure within the next 90 days.";
        var rootCause = $"The strongest churn signals come from {ForeSightSupport.HumanizeFactorList(topFactors)}.";
        var recommendation = riskBand is "HIGH" or "MEDIUM"
            ? "Engage the tenant success lane now: review adoption blockers, unresolved support issues, and overdue billing posture before renewal conversations begin."
            : "Engagement remains stable. Continue monitoring usage and support pressure.";

        return new GeneratedPrediction
        {
            ModelCode = ForeSightModelCodes.ChurnRisk,
            HorizonLabel = "90D",
            HorizonDate = DateTime.UtcNow.Date.AddDays(90),
            PredictedValue = probability,
            ConfidenceLower = lower,
            ConfidenceUpper = upper,
            ConfidenceScore = confidence,
            RiskBand = riskBand,
            TargetModuleCode = string.Empty,
            TargetPeriodCode = string.Empty,
            TargetMetric = "TENANT_CHURN",
            TargetLabel = context.TenantName,
            Explanation = explanation,
            RootCauseNarrative = rootCause,
            Recommendation = recommendation,
            RootCausePillar = topFactors.FirstOrDefault()?.FeatureLabel ?? "Tenant Engagement",
            FeatureImportanceJson = ForeSightSupport.SerializeFeatures(features),
            Features = features,
            Alert = riskBand == "HIGH"
                ? new AlertPlan("CHURN_RISK", "WARNING", "PlatformAdmin")
                : null,
            HasLowData = usageRecent.Count + usagePrior.Count == 0,
            LowDataReason = "Platform-usage history is sparse for this tenant."
        };
    }

    private async Task<GeneratedPrediction?> BuildRegulatoryActionPredictionAsync(InstitutionContext context, IReadOnlyDictionary<string, string> config, CancellationToken ct)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(null, ct);

        var highThreshold = ForeSightSupport.ParseDecimal(config.GetValueOrDefault("regaction.high_threshold"), 0.60m);
        var weights = await GetFeatureWeightsAsync(ForeSightModelCodes.RegulatoryAction, ct);
        var hasEwiTriggers = await TableExistsAsync("meta", "ewi_triggers", ct);
        var hasAnomalyReports = await TableExistsAsync("meta", "anomaly_reports", ct);
        var hasFilingSlaRecords = await TableExistsAsync("dbo", "filing_sla_records", ct);
        var hasPrudentialMetrics = await TableExistsAsync("meta", "prudential_metrics", ct);
        var hasCamelsRatings = await TableExistsAsync("meta", "camels_ratings", ct);

        var criticalEwiCount = hasEwiTriggers
            ? await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM   meta.ewi_triggers
                WHERE  InstitutionId = @InstitutionId
                  AND  IsActive = 1
                  AND  Severity = 'CRITICAL'
                """,
                new { context.InstitutionId })
            : 0;
        var highEwiCount = hasEwiTriggers
            ? await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM   meta.ewi_triggers
                WHERE  InstitutionId = @InstitutionId
                  AND  IsActive = 1
                  AND  Severity = 'HIGH'
                """,
                new { context.InstitutionId })
            : 0;

        var latestChs = await _db.ChsScoreSnapshots.AsNoTracking()
            .Where(x => x.TenantId == context.TenantId)
            .OrderByDescending(x => x.ComputedAt)
            .FirstOrDefaultAsync(ct);

        var latestAnomaly = hasAnomalyReports
            ? await _db.AnomalyReports.AsNoTracking()
                .Where(x => x.TenantId == context.TenantId)
                .OrderByDescending(x => x.AnalysedAt)
                .FirstOrDefaultAsync(ct)
            : null;

        var recentFilingRecords = hasFilingSlaRecords
            ? await _db.FilingSlaRecords.AsNoTracking()
                .Where(x => x.TenantId == context.TenantId)
                .OrderByDescending(x => x.PeriodEndDate)
                .Take(8)
                .ToListAsync(ct)
            : new List<FilingSlaRecord>();
        var delinquency = recentFilingRecords.Count == 0
            ? 0m
            : (decimal)recentFilingRecords.Count(x => x.OnTime == false) / recentFilingRecords.Count;

        var latestPrudential = hasPrudentialMetrics
            ? await conn.QueryFirstOrDefaultAsync<PrudentialMetricRow>(
                """
                SELECT TOP 1 PeriodCode, AsOfDate, CAR, NPLRatio, LCR, ProvisioningCoverage
                FROM   meta.prudential_metrics
                WHERE  InstitutionId = @InstitutionId
                ORDER BY AsOfDate DESC
                """,
                new { context.InstitutionId })
            : null;

        decimal capitalProximity = 0.50m;
        if (latestPrudential is not null)
        {
            var thresholds = await GetThresholdsAsync(context.RegulatorCode, context.LicenceTypeCode, "CAR", ct);
            if (thresholds.Count > 0 && latestPrudential.CAR.HasValue)
            {
                capitalProximity = ForeSightSupport.Clamp(1m - Math.Max(0m, latestPrudential.CAR.Value - thresholds.Max(x => x.ThresholdValue)) / 10m);
            }
        }

        var latestCamels = hasCamelsRatings
            ? await conn.QueryFirstOrDefaultAsync<CamelsRow>(
                """
                SELECT TOP 1 CompositeScore, RiskBand
                FROM   meta.camels_ratings
                WHERE  InstitutionId = @InstitutionId
                ORDER BY ComputedAt DESC
                """,
                new { context.InstitutionId })
            : null;

        var camelsPressure = latestCamels?.RiskBand switch
        {
            "CRITICAL" => 1m,
            "RED" => 0.80m,
            "AMBER" => 0.50m,
            _ => 0.15m
        };

        var features = new List<ForeSightPredictionFeature>
        {
            CreateFeature("critical_ewi_count", "Critical EWI Count", criticalEwiCount + (highEwiCount * 0.5m), ForeSightSupport.Clamp((criticalEwiCount + (highEwiCount * 0.5m)) / 5m), weights),
            CreateFeature("chs_deficit", "CHS Deficit", latestChs is null ? 50m : 100m - latestChs.OverallScore, latestChs is null ? 0.50m : ForeSightSupport.Clamp((100m - latestChs.OverallScore) / 60m), weights),
            CreateFeature("anomaly_pressure", "Anomaly Pressure", latestAnomaly?.OverallQualityScore ?? 100m, ForeSightSupport.Clamp(1m - ((latestAnomaly?.OverallQualityScore ?? 100m) / 100m)), weights),
            CreateFeature("filing_delinquency", "Filing Delinquency", delinquency, delinquency, weights),
            CreateFeature("capital_proximity", "Capital Proximity", capitalProximity, capitalProximity, weights),
            CreateFeature("camels_pressure", "CAMELS Pressure", camelsPressure, camelsPressure, weights)
        };

        var probability = ForeSightSupport.Logistic(features.Sum(x => x.ContributionScore));
        var confidence = ForeSightSupport.ComputeConfidence(
            observations: recentFilingRecords.Count + criticalEwiCount + highEwiCount + (latestChs is null ? 0 : 1),
            targetObservations: 10,
            dataCoverage: latestPrudential is null ? 0.65m : 0.90m);
        var margin = 0.10m + ((1m - confidence) * 0.18m);
        var lower = ForeSightSupport.Clamp(probability - margin);
        var upper = ForeSightSupport.Clamp(probability + margin);
        var riskBand = probability >= 0.80m
            ? "CRITICAL"
            : probability >= highThreshold
                ? "HIGH"
                : probability >= 0.35m
                    ? "MEDIUM"
                    : "LOW";

        var topFactors = features.OrderByDescending(x => x.ContributionScore).Take(3).ToList();
        var explanation = $"ForeSight estimates a {probability:P0} supervisory-intervention probability for {context.InstitutionName} across the next six months.";
        var rootCause = $"The leading supervisory triggers are {ForeSightSupport.HumanizeFactorList(topFactors)}.";
        var recommendation = riskBand is "HIGH" or "CRITICAL"
            ? "Prioritise examiner review, validate the current prudential return set, and seek management remediation plans for the dominant risk drivers."
            : "No immediate intervention is forecast. Continue supervisory monitoring.";

        return new GeneratedPrediction
        {
            ModelCode = ForeSightModelCodes.RegulatoryAction,
            HorizonLabel = "6M",
            HorizonDate = DateTime.UtcNow.Date.AddMonths(6),
            PredictedValue = probability,
            ConfidenceLower = lower,
            ConfidenceUpper = upper,
            ConfidenceScore = confidence,
            RiskBand = riskBand,
            TargetModuleCode = string.Empty,
            TargetPeriodCode = string.Empty,
            TargetMetric = "SUPERVISORY_INTERVENTION",
            TargetLabel = context.InstitutionName,
            Explanation = explanation,
            RootCauseNarrative = rootCause,
            Recommendation = recommendation,
            RootCausePillar = topFactors.FirstOrDefault()?.FeatureLabel ?? "Supervisory Posture",
            FeatureImportanceJson = ForeSightSupport.SerializeFeatures(features),
            Features = features,
            Alert = riskBand is "HIGH" or "CRITICAL"
                ? new AlertPlan("REG_ACTION_PRIORITY", riskBand == "CRITICAL" ? "CRITICAL" : "WARNING", "Examiner,RegulatorAdmin")
                : null,
            HasLowData = latestPrudential is null && latestChs is null,
            LowDataReason = "Prudential and CHS supervisory signals are incomplete."
        };
    }

    private async Task<ForeSightPrediction?> SavePredictionAsync(
        Guid tenantId,
        GeneratedPrediction generated,
        IReadOnlyDictionary<string, string> config,
        string performedBy,
        CancellationToken ct)
    {
        var modelVersionId = await _db.ForeSightModelVersions
            .Where(x => x.ModelCode == generated.ModelCode && x.Status == "ACTIVE")
            .OrderByDescending(x => x.VersionNumber)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(ct);

        if (!modelVersionId.HasValue)
        {
            return null;
        }

        var minConfidence = ForeSightSupport.ParseDecimal(config.GetValueOrDefault("prediction.min_confidence"), 0.55m);
        var suppressLowData = ForeSightSupport.TryParseBoolean(config.GetValueOrDefault("prediction.suppress_low_data"), out var suppressFlag)
            ? suppressFlag
            : true;

        var isSuppressed = generated.ConfidenceScore < minConfidence || (generated.HasLowData && suppressLowData);
        var suppressionReason = generated.ConfidenceScore < minConfidence
            ? $"Prediction confidence {generated.ConfidenceScore:P0} is below the minimum threshold."
            : generated.HasLowData && suppressLowData
                ? generated.LowDataReason
                : null;

        var entity = await _db.ForeSightPredictions
            .Include(x => x.Features)
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantId
                && x.ModelCode == generated.ModelCode
                && x.PredictionDate == DateTime.UtcNow.Date
                && x.HorizonLabel == generated.HorizonLabel
                && x.TargetModuleCode == generated.TargetModuleCode
                && x.TargetPeriodCode == generated.TargetPeriodCode
                && x.TargetMetric == generated.TargetMetric, ct);

        var now = DateTime.UtcNow;

        if (entity is null)
        {
            entity = new ForeSightPrediction
            {
                TenantId = tenantId,
                ModelCode = generated.ModelCode,
                ModelVersionId = modelVersionId.Value,
                PredictionDate = DateTime.UtcNow.Date,
                CreatedAt = now
            };
            _db.ForeSightPredictions.Add(entity);
        }
        else
        {
            _db.ForeSightPredictionFeatures.RemoveRange(entity.Features);
            entity.ModelVersionId = modelVersionId.Value;
        }

        entity.HorizonLabel = generated.HorizonLabel;
        entity.HorizonDate = generated.HorizonDate;
        entity.PredictedValue = generated.PredictedValue;
        entity.ConfidenceLower = generated.ConfidenceLower;
        entity.ConfidenceUpper = generated.ConfidenceUpper;
        entity.ConfidenceScore = generated.ConfidenceScore;
        entity.RiskBand = generated.RiskBand;
        entity.TargetModuleCode = generated.TargetModuleCode;
        entity.TargetPeriodCode = generated.TargetPeriodCode;
        entity.TargetMetric = generated.TargetMetric;
        entity.TargetLabel = generated.TargetLabel;
        entity.Explanation = generated.Explanation;
        entity.RootCauseNarrative = generated.RootCauseNarrative;
        entity.Recommendation = generated.Recommendation;
        entity.RootCausePillar = generated.RootCausePillar;
        entity.FeatureImportanceJson = generated.FeatureImportanceJson;
        entity.IsSuppressed = isSuppressed;
        entity.SuppressionReason = suppressionReason;
        entity.UpdatedAt = now;
        entity.Features = generated.Features.Select(x => new ForeSightPredictionFeatureRecord
        {
            FeatureName = x.FeatureName,
            FeatureLabel = x.FeatureLabel,
            RawValue = x.RawValue,
            NormalizedValue = x.NormalizedValue,
            Weight = x.Weight,
            ContributionScore = x.ContributionScore,
            ImpactDirection = x.ImpactDirection,
            CreatedAt = now
        }).ToList();

        await _db.SaveChangesAsync(ct);

        await WriteAuditEntryAsync(
            tenantId,
            "ForeSightPrediction",
            entity.Id > int.MaxValue ? int.MaxValue : (int)entity.Id,
            "PREDICTION_GENERATED",
            new
            {
                entity.ModelCode,
                entity.TargetMetric,
                entity.RiskBand,
                entity.PredictedValue,
                entity.ConfidenceScore,
                entity.IsSuppressed
            },
            performedBy,
            ct);

        return entity;
    }

    private async Task SaveAlertIfNeededAsync(ForeSightPrediction prediction, AlertPlan alertPlan, string performedBy, CancellationToken ct)
    {
        var config = await GetConfigMapAsync(ct);
        var cooldownHours = ForeSightSupport.ParseInt(config.GetValueOrDefault("alert.cooldown_hours"), 24);
        var title = BuildAlertTitle(prediction, alertPlan);

        var exists = await _db.ForeSightAlerts
            .AsNoTracking()
            .AnyAsync(x =>
                x.TenantId == prediction.TenantId
                && x.AlertType == alertPlan.AlertType
                && x.Title == title
                && x.DispatchedAt >= DateTime.UtcNow.AddHours(-cooldownHours), ct);

        if (exists)
        {
            return;
        }

        var alert = new ForeSightAlert
        {
            PredictionId = prediction.Id,
            TenantId = prediction.TenantId,
            AlertType = alertPlan.AlertType,
            Severity = alertPlan.Severity,
            Title = title,
            Body = prediction.Explanation,
            Recommendation = prediction.Recommendation,
            RecipientRole = alertPlan.RecipientRole,
            DispatchedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _db.ForeSightAlerts.Add(alert);
        await _db.SaveChangesAsync(ct);

        await WriteAuditEntryAsync(
            prediction.TenantId,
            "ForeSightAlert",
            alert.Id,
            "ALERT_DISPATCHED",
            new { alert.AlertType, alert.Severity, alert.Title },
            performedBy,
            ct);
    }

    private async Task<Dictionary<string, string>> GetConfigMapAsync(CancellationToken ct)
    {
        var result = await _cache.GetOrCreateAsync("foresight:config", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            var rows = await _db.ForeSightConfigs
                .AsNoTracking()
                .Where(x => x.EffectiveTo == null)
                .OrderByDescending(x => x.EffectiveFrom)
                .ToListAsync(ct);

            return rows
                .GroupBy(x => x.ConfigKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First().ConfigValue, StringComparer.OrdinalIgnoreCase);
        });

        return result ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<bool> TableExistsAsync(string schema, string table, CancellationToken ct)
    {
        var key = $"foresight:table:{schema}.{table}";
        var exists = await _cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            using var conn = await _connectionFactory.CreateConnectionAsync(null, ct);
            return await conn.ExecuteScalarAsync<bool>(
                """
                SELECT CASE
                           WHEN EXISTS (
                               SELECT 1
                               FROM   INFORMATION_SCHEMA.TABLES
                               WHERE  TABLE_SCHEMA = @SchemaName
                                  AND TABLE_NAME = @TableName
                           )
                           THEN CAST(1 AS bit)
                           ELSE CAST(0 AS bit)
                       END
                """,
                new { SchemaName = schema, TableName = table });
        });

        return exists;
    }

    private async Task<Dictionary<string, decimal>> GetFeatureWeightsAsync(string modelCode, CancellationToken ct)
    {
        var key = $"foresight:weights:{modelCode}";
        var weights = await _cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            var rows = await _db.ForeSightFeatureDefinitions
                .AsNoTracking()
                .Where(x => x.ModelCode == modelCode && x.IsActive)
                .ToListAsync(ct);

            return rows.ToDictionary(x => x.FeatureName, x => x.DefaultWeight, StringComparer.OrdinalIgnoreCase);
        });

        return weights ?? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<List<ForeSightRegulatoryThreshold>> GetThresholdsAsync(string regulatorCode, string licenceTypeCode, string metricCode, CancellationToken ct)
    {
        var key = $"foresight:thresholds:{regulatorCode}:{licenceTypeCode}:{metricCode}";
        var result = await _cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await _db.ForeSightRegulatoryThresholds
                .AsNoTracking()
                .Where(x => x.IsActive
                            && x.Regulator == regulatorCode
                            && x.LicenceCategory == licenceTypeCode
                            && x.MetricCode == metricCode)
                .OrderBy(x => x.ThresholdValue)
                .ToListAsync(ct);
        });

        return result ?? new List<ForeSightRegulatoryThreshold>();
    }

    private async Task<InstitutionContext?> ResolveInstitutionContextAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await _db.Tenants
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => new { x.TenantId, x.TenantName })
            .FirstOrDefaultAsync(ct);

        if (tenant is null)
        {
            return null;
        }

        var institution = await _db.Institutions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderByDescending(x => x.LastSubmissionAt)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (institution is null)
        {
            return null;
        }

        var licence = await _db.TenantLicenceTypes
            .AsNoTracking()
            .Include(x => x.LicenceType)
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderByDescending(x => x.EffectiveDate)
            .FirstOrDefaultAsync(ct);

        return new InstitutionContext(
            tenant.TenantId,
            tenant.TenantName,
            institution.Id,
            institution.InstitutionName,
            licence?.LicenceType?.Code ?? "DMB",
            licence?.LicenceType?.Regulator ?? "CBN");
    }

    private async Task WriteAuditEntryAsync(Guid tenantId, string entityType, int entityId, string action, object? payload, string performedBy, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var previous = await _db.AuditLog
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.SequenceNumber > 0)
            .OrderByDescending(x => x.SequenceNumber)
            .Select(x => new { x.SequenceNumber, x.Hash })
            .FirstOrDefaultAsync(ct);

        var previousHash = previous?.Hash ?? "GENESIS";
        var sequence = (previous?.SequenceNumber ?? 0) + 1;
        var newValues = payload is null ? null : JsonSerializer.Serialize(payload);
        var normalizedAction = ForeSightSupport.NormalizeAction(action);

        var entry = new AuditLogEntry
        {
            TenantId = tenantId,
            EntityType = entityType,
            EntityId = entityId,
            Action = normalizedAction,
            NewValues = newValues,
            PerformedBy = performedBy,
            PerformedAt = now,
            PreviousHash = previousHash,
            SequenceNumber = sequence,
            Hash = AuditLogger.ComputeHash(
                sequence,
                entityType,
                now,
                tenantId,
                performedBy,
                entityType,
                entityId,
                normalizedAction,
                null,
                newValues,
                previousHash)
        };

        _db.AuditLog.Add(entry);
        await _db.SaveChangesAsync(ct);
    }

    private static ForeSightPredictionSummary MapPrediction(ForeSightPrediction entity)
    {
        return new ForeSightPredictionSummary
        {
            Id = entity.Id,
            TenantId = entity.TenantId,
            ModelCode = entity.ModelCode,
            PredictionDate = entity.PredictionDate,
            HorizonLabel = entity.HorizonLabel,
            HorizonDate = entity.HorizonDate,
            PredictedValue = entity.PredictedValue,
            ConfidenceLower = entity.ConfidenceLower,
            ConfidenceUpper = entity.ConfidenceUpper,
            ConfidenceScore = entity.ConfidenceScore,
            RiskBand = entity.RiskBand,
            TargetModuleCode = entity.TargetModuleCode,
            TargetPeriodCode = entity.TargetPeriodCode,
            TargetMetric = entity.TargetMetric,
            TargetLabel = entity.TargetLabel,
            Explanation = entity.Explanation,
            RootCauseNarrative = entity.RootCauseNarrative,
            Recommendation = entity.Recommendation,
            RootCausePillar = entity.RootCausePillar,
            IsSuppressed = entity.IsSuppressed,
            SuppressionReason = entity.SuppressionReason,
            Features = ForeSightSupport.DeserializeFeatures(entity.FeatureImportanceJson)
        };
    }

    private static ForeSightAlertItem MapAlert(ForeSightAlert entity)
    {
        return new ForeSightAlertItem
        {
            Id = entity.Id,
            PredictionId = entity.PredictionId,
            TenantId = entity.TenantId,
            AlertType = entity.AlertType,
            Severity = entity.Severity,
            Title = entity.Title,
            Body = entity.Body,
            Recommendation = entity.Recommendation,
            RecipientRole = entity.RecipientRole,
            IsRead = entity.IsRead,
            IsDismissed = entity.IsDismissed,
            DispatchedAt = entity.DispatchedAt
        };
    }

    private static ForeSightPredictionFeature CreateFeature(
        string name,
        string label,
        decimal rawValue,
        decimal normalizedValue,
        IReadOnlyDictionary<string, decimal> weights,
        string impactDirection = "INCREASES_RISK")
    {
        var weight = weights.GetValueOrDefault(name, 0.05m);
        return new ForeSightPredictionFeature
        {
            FeatureName = name,
            FeatureLabel = label,
            RawValue = decimal.Round(rawValue, 4),
            NormalizedValue = decimal.Round(ForeSightSupport.Clamp(normalizedValue), 4),
            Weight = weight,
            ContributionScore = decimal.Round(ForeSightSupport.Clamp(normalizedValue) * weight, 6),
            ImpactDirection = impactDirection
        };
    }

    private static ForeSightPredictionFeature CreateInformationalFeature(string name, string label, decimal rawValue)
    {
        return new ForeSightPredictionFeature
        {
            FeatureName = name,
            FeatureLabel = label,
            RawValue = decimal.Round(rawValue, 4),
            NormalizedValue = 0m,
            Weight = 0m,
            ContributionScore = 0m,
            ImpactDirection = "INFORMATIONAL"
        };
    }

    private static decimal StageRisk(SubmissionStatus? status) => status switch
    {
        SubmissionStatus.Accepted => 0m,
        SubmissionStatus.AcceptedWithWarnings => 0.05m,
        SubmissionStatus.RegulatorAccepted => 0m,
        SubmissionStatus.RegulatorAcknowledged => 0.20m,
        SubmissionStatus.PendingApproval => 0.25m,
        SubmissionStatus.SubmittedToRegulator => 0.30m,
        SubmissionStatus.Validating => 0.40m,
        SubmissionStatus.Parsing => 0.50m,
        SubmissionStatus.Draft => 0.70m,
        SubmissionStatus.Rejected => 0.85m,
        SubmissionStatus.ApprovalRejected => 0.85m,
        _ => 1m
    };

    private static string BuildFilingRootCause(
        IReadOnlyList<ForeSightPredictionFeature> topFactors,
        decimal completeness,
        decimal lateRate,
        int loginsLast7Days,
        int daysToDeadline)
    {
        if (topFactors.Count == 0)
        {
            return "Signal detail is limited for this filing.";
        }

        return topFactors[0].FeatureName switch
        {
            "draft_completeness_gap" => $"Draft completeness is only {completeness:P0}, which is weak for a filing with {daysToDeadline} day(s) remaining.",
            "historical_late_rate" => $"This module has been late in {lateRate:P0} of recent periods, which materially increases the current late-filing probability.",
            "login_activity_gap" => $"Portal activity is subdued with just {loginsLast7Days} successful login(s) in the last seven days.",
            "days_to_deadline" => $"Deadline pressure is acute because the filing is only {daysToDeadline} day(s) away.",
            _ => $"The main risk driver is {topFactors[0].FeatureLabel.ToLowerInvariant()}."
        };
    }

    private static string BuildFilingRecommendation(IReadOnlyList<ForeSightPredictionFeature> topFactors, decimal completeness, int daysToDeadline)
    {
        if (topFactors.Count == 0)
        {
            return $"Set an internal filing checkpoint within the next {Math.Max(1, daysToDeadline / 2)} day(s) and confirm ownership.";
        }

        return topFactors[0].FeatureName switch
        {
            "draft_completeness_gap" => $"Increase completion from {completeness:P0} to at least 80% within the next two working days and assign review ownership now.",
            "preparation_stage" => "Escalate the return into review today and confirm that checker capacity is reserved before the regulatory deadline.",
            "login_activity_gap" => "Confirm the responsible preparers still have access and actively schedule filing-preparation time on the team calendar.",
            "historical_late_rate" => "Set an internal deadline at least three business days ahead of the regulator deadline and assign a second reviewer.",
            _ => $"Run a same-day filing readiness review because only {daysToDeadline} day(s) remain."
        };
    }

    private static string BuildCapitalExplanation(
        string metricLabel,
        decimal current,
        decimal projected,
        BreachProjection breach,
        IReadOnlyList<ForeSightRegulatoryThreshold> thresholds,
        int forecastQuarters)
    {
        if (breach.WillBreach)
        {
            return $"{metricLabel} is currently {current:F2}. ForeSight projects {projected:F2} by {breach.HorizonLabel}, which would breach the regulatory threshold of {breach.ThresholdValue:F2}.";
        }

        var keyThreshold = thresholds.OrderByDescending(x => x.ThresholdValue).First();
        return $"{metricLabel} is currently {current:F2}. Across the next {forecastQuarters} quarter(s), the projected trajectory stays away from the {keyThreshold.ThresholdValue:F2} {keyThreshold.ThresholdType.ToLowerInvariant()} threshold, but should still be monitored.";
    }

    private static string BuildCapitalRecommendation(string metricCode, BreachProjection breach, decimal thresholdBuffer)
    {
        if (breach.WillBreach)
        {
            return metricCode switch
            {
                "CAR" => "Review capital-restoration levers immediately: retained earnings retention, capital raising, or RWA optimisation.",
                "NPL" => "Accelerate collections, provisioning reviews, and watch-list remediation to arrest the projected NPL breach.",
                _ => "Increase liquidity buffers and review funding concentration to avoid a projected liquidity shortfall."
            };
        }

        if (Math.Abs(thresholdBuffer) <= 2m)
        {
            return "The forecast is still inside the warning buffer. Increase monitoring frequency and confirm management action plans.";
        }

        return "No immediate breach is projected. Continue prudential monitoring.";
    }

    private static string DetermineCapitalRiskBand(
        string metricCode,
        decimal current,
        decimal thresholdBuffer,
        IReadOnlyList<decimal> forecast,
        IReadOnlyList<ForeSightRegulatoryThreshold> thresholds,
        decimal warningBuffer)
    {
        var breach = DetectProjectedBreach(metricCode, forecast, thresholds, "Q+1");
        if (breach.WillBreach && breach.Severity == "CRITICAL")
        {
            return "CRITICAL";
        }

        if (breach.WillBreach || Math.Abs(thresholdBuffer) <= warningBuffer)
        {
            return "HIGH";
        }

        if (Math.Abs(thresholdBuffer) <= warningBuffer * 2)
        {
            return "MEDIUM";
        }

        _ = current;
        return "LOW";
    }

    private static BreachProjection DetectProjectedBreach(string metricCode, IReadOnlyList<decimal> forecast, IReadOnlyList<ForeSightRegulatoryThreshold> thresholds, string lastPeriodCode)
    {
        for (var index = 0; index < forecast.Count; index++)
        {
            var value = forecast[index];
            var horizonLabel = IncrementQuarter(lastPeriodCode, index + 1);

            foreach (var threshold in thresholds)
            {
                var breach = threshold.ThresholdType == "MINIMUM"
                    ? value < threshold.ThresholdValue
                    : value > threshold.ThresholdValue;

                if (breach)
                {
                    return new BreachProjection(true, horizonLabel, threshold.ThresholdValue, threshold.SeverityIfBreached, value);
                }
            }
        }

        return new BreachProjection(false, IncrementQuarter(lastPeriodCode, forecast.Count), thresholds.OrderByDescending(x => x.ThresholdValue).First().ThresholdValue, "NONE", forecast.LastOrDefault());
    }

    private static decimal ThresholdBuffer(string metricCode, decimal current, IReadOnlyList<ForeSightRegulatoryThreshold> thresholds)
    {
        var relevant = thresholds.OrderBy(x => x.ThresholdValue).ToList();
        if (relevant.Count == 0)
        {
            return 0m;
        }

        return metricCode switch
        {
            "NPL" => relevant[0].ThresholdValue - current,
            _ => current - relevant[^1].ThresholdValue
        };
    }

    private static int CountConsecutiveDeclines(IReadOnlyList<decimal> series)
    {
        var count = 0;
        for (var index = series.Count - 1; index >= 1; index--)
        {
            if (series[index] < series[index - 1])
            {
                count++;
            }
            else
            {
                break;
            }
        }

        return count;
    }

    private static string IncrementQuarter(string periodCode, int quarters)
    {
        if (!periodCode.Contains("-Q", StringComparison.OrdinalIgnoreCase))
        {
            return $"Q+{quarters}";
        }

        var parts = periodCode.Split("-Q", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!int.TryParse(parts[0], out var year) || !int.TryParse(parts[1], out var quarter))
        {
            return $"Q+{quarters}";
        }

        var zeroBased = ((year * 4) + (quarter - 1)) + quarters;
        var projectedYear = zeroBased / 4;
        var projectedQuarter = (zeroBased % 4) + 1;
        return $"{projectedYear}-Q{projectedQuarter}";
    }

    private static string BuildAlertTitle(ForeSightPrediction prediction, AlertPlan plan)
    {
        return plan.AlertType switch
        {
            "FILING_RISK" => $"Filing risk: {prediction.TargetLabel} {prediction.TargetPeriodCode}",
            "CAPITAL_WARNING" => $"Capital warning: {prediction.TargetLabel}",
            "CAPITAL_CRITICAL" => $"Capital breach forecast: {prediction.TargetLabel}",
            "CHS_DECLINE" => "Compliance health deterioration forecast",
            "CHURN_RISK" => $"Tenant churn risk: {prediction.TargetLabel}",
            "REG_ACTION_PRIORITY" => $"Supervisory priority: {prediction.TargetLabel}",
            _ => $"ForeSight alert: {prediction.TargetLabel}"
        };
    }

    private sealed record InstitutionContext(
        Guid TenantId,
        string TenantName,
        int InstitutionId,
        string InstitutionName,
        string LicenceTypeCode,
        string RegulatorCode);

    private sealed class GeneratedPrediction
    {
        public string ModelCode { get; set; } = string.Empty;
        public string HorizonLabel { get; set; } = string.Empty;
        public DateTime? HorizonDate { get; set; }
        public decimal PredictedValue { get; set; }
        public decimal? ConfidenceLower { get; set; }
        public decimal? ConfidenceUpper { get; set; }
        public decimal ConfidenceScore { get; set; }
        public string RiskBand { get; set; } = "LOW";
        public string TargetModuleCode { get; set; } = string.Empty;
        public string TargetPeriodCode { get; set; } = string.Empty;
        public string TargetMetric { get; set; } = string.Empty;
        public string TargetLabel { get; set; } = string.Empty;
        public string Explanation { get; set; } = string.Empty;
        public string RootCauseNarrative { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public string RootCausePillar { get; set; } = string.Empty;
        public string FeatureImportanceJson { get; set; } = "[]";
        public List<ForeSightPredictionFeature> Features { get; set; } = new();
        public AlertPlan? Alert { get; set; }
        public bool HasLowData { get; set; }
        public string? LowDataReason { get; set; }
    }

    private sealed record AlertPlan(string AlertType, string Severity, string RecipientRole);
    private sealed record MetricDefinition(string Code, string Label, List<decimal> Values);
    private sealed record BreachProjection(bool WillBreach, string HorizonLabel, decimal ThresholdValue, string Severity, decimal ProjectedValue);

    private sealed class PrudentialMetricRow
    {
        public string PeriodCode { get; set; } = string.Empty;
        public DateTime AsOfDate { get; set; }
        public decimal? CAR { get; set; }
        public decimal? NPLRatio { get; set; }
        public decimal? LCR { get; set; }
        public decimal? ProvisioningCoverage { get; set; }
    }

    private sealed class CamelsRow
    {
        public decimal CompositeScore { get; set; }
        public string RiskBand { get; set; } = string.Empty;
    }
}

internal static class ForeSightPredictionSummaryExtensions
{
    public static DateTime CreatedAtUtc(this ForeSightPredictionSummary prediction) => prediction.HorizonDate ?? prediction.PredictionDate;
}
