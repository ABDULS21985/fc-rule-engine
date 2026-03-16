using System.Data;
using System.Diagnostics;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Domain.Notifications;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Reports;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed class AnomalyDetectionService : IAnomalyDetectionService
{
    private readonly IDbContextFactory<MetadataDbContext> _dbFactory;
    private readonly IAnomalyModelTrainingService _modelTrainingService;
    private readonly IAuditLogger _auditLogger;
    private readonly INotificationOrchestrator? _notificationOrchestrator;
    private readonly ILogger<AnomalyDetectionService> _logger;

    public AnomalyDetectionService(
        IDbContextFactory<MetadataDbContext> dbFactory,
        IAnomalyModelTrainingService modelTrainingService,
        IAuditLogger auditLogger,
        ILogger<AnomalyDetectionService> logger,
        INotificationOrchestrator? notificationOrchestrator = null)
    {
        _dbFactory = dbFactory;
        _modelTrainingService = modelTrainingService;
        _auditLogger = auditLogger;
        _logger = logger;
        _notificationOrchestrator = notificationOrchestrator;
    }

    public async Task<AnomalyReport> AnalyzeSubmissionAsync(
        int submissionId,
        Guid tenantId,
        string performedBy,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var started = Stopwatch.StartNew();
        var submission = await db.Submissions
            .Include(x => x.Institution)
            .Include(x => x.ReturnPeriod)
                .ThenInclude(x => x!.Module)
            .FirstOrDefaultAsync(x => x.Id == submissionId && x.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Submission #{submissionId} was not found for tenant {tenantId}.");

        if (submission.ReturnPeriod?.Module is null)
        {
            throw new InvalidOperationException($"Submission #{submissionId} does not have a resolved module.");
        }

        var moduleCode = submission.ReturnPeriod.Module.ModuleCode;
        var regulatorCode = submission.ReturnPeriod.Module.RegulatorCode;
        var periodCode = AnomalySupport.BuildPeriodCode(submission.ReturnPeriod);
        var institutionName = submission.Institution?.InstitutionName ?? "Unknown institution";
        var institutionId = submission.InstitutionId;
        var licenceType = await ResolveLicenceTypeAsync(db, submission, ct);
        var currentMetrics = AnomalySupport.ExtractSubmissionMetrics(submission.ParsedDataJson);

        var activeModel = await db.AnomalyModelVersions
            .OrderByDescending(x => x.VersionNumber)
            .FirstOrDefaultAsync(x => x.ModuleCode == moduleCode && x.Status == "ACTIVE", ct);

        if (activeModel is null)
        {
            _logger.LogInformation("No active anomaly model for module {ModuleCode}; training bootstrap model.", moduleCode);
            activeModel = await _modelTrainingService.TrainModuleModelAsync(moduleCode, "SYSTEM_BOOTSTRAP", true, ct);
        }

        var config = await LoadConfigAsync(db, ct);
        var baselines = await db.AnomalyRuleBaselines
            .AsNoTracking()
            .Where(x => x.IsActive
                        && x.RegulatorCode == regulatorCode
                        && (x.ModuleCode == null || x.ModuleCode == moduleCode))
            .ToListAsync(ct);

        var fieldModels = await db.AnomalyFieldModels
            .AsNoTracking()
            .Where(x => x.ModelVersionId == activeModel.Id)
            .ToListAsync(ct);

        var correlationRules = await db.AnomalyCorrelationRules
            .AsNoTracking()
            .Where(x => x.ModelVersionId == activeModel.Id && x.IsActive)
            .ToListAsync(ct);

        var peerStats = await db.AnomalyPeerGroupStatistics
            .AsNoTracking()
            .Where(x => x.ModelVersionId == activeModel.Id
                        && x.ModuleCode == moduleCode
                        && x.LicenceCategory == licenceType
                        && x.PeriodCode == periodCode
                        && x.InstitutionSizeBand == "ALL")
            .ToListAsync(ct);

        var findings = new List<AnomalyFinding>();
        findings.AddRange(ScoreFieldLevelFindings(submissionId, tenantId, currentMetrics, fieldModels, baselines, config));
        findings.AddRange(ScoreCorrelationFindings(submissionId, tenantId, currentMetrics, correlationRules, config));
        findings.AddRange(await ScoreTemporalFindingsAsync(db, submission, currentMetrics, config, ct));
        findings.AddRange(ScorePeerFindings(submissionId, tenantId, licenceType, currentMetrics, peerStats, config));

        var qualityScore = CalculateQualityScore(findings, config);
        var report = new AnomalyReport
        {
            TenantId = tenantId,
            InstitutionId = institutionId,
            InstitutionName = institutionName,
            SubmissionId = submissionId,
            ModuleCode = moduleCode,
            RegulatorCode = regulatorCode,
            PeriodCode = periodCode,
            ModelVersionId = activeModel.Id,
            OverallQualityScore = qualityScore,
            TotalFieldsAnalysed = currentMetrics.Count,
            TotalFindings = findings.Count,
            AlertCount = findings.Count(x => x.Severity == "ALERT"),
            WarningCount = findings.Count(x => x.Severity == "WARNING"),
            InfoCount = findings.Count(x => x.Severity == "INFO"),
            RelationshipFindings = findings.Count(x => x.FindingType == "RELATIONSHIP"),
            TemporalFindings = findings.Count(x => x.FindingType == "TEMPORAL"),
            PeerFindings = findings.Count(x => x.FindingType == "PEER"),
            TrafficLight = AnomalySupport.DetermineTrafficLight(qualityScore),
            NarrativeSummary = BuildNarrativeSummary(moduleCode, periodCode, institutionName, findings, qualityScore),
            AnalysedAt = DateTime.UtcNow
        };

        started.Stop();
        report.AnalysisDurationMs = (int)started.ElapsedMilliseconds;
        foreach (var finding in findings)
        {
            finding.TenantId = tenantId;
            finding.SubmissionId = submissionId;
        }

        var existing = await db.AnomalyReports
            .Include(x => x.Findings)
            .Where(x => x.SubmissionId == submissionId && x.ModelVersionId == activeModel.Id)
            .ToListAsync(ct);

        if (existing.Count > 0)
        {
            db.AnomalyReports.RemoveRange(existing);
        }

        report.Findings = findings;
        db.AnomalyReports.Add(report);
        await db.SaveChangesAsync(ct);

        await _auditLogger.Log(
            "AnomalyReport",
            report.Id,
            "Analyzed",
            null,
            new
            {
                report.SubmissionId,
                report.ModuleCode,
                report.PeriodCode,
                report.OverallQualityScore,
                report.TotalFindings,
                report.TrafficLight
            },
            performedBy,
            ct);

        await MaybeNotifyInstitutionAsync(report, performedBy, ct);

        return await GetReportByIdAsync(report.Id, tenantId, ct)
            ?? report;
    }

    public async Task<AnomalyReport?> GetLatestReportForSubmissionAsync(
        int submissionId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (!await TableExistsAsync(db, "meta", "anomaly_reports", ct))
        {
            _logger.LogInformation(
                "Skipping latest anomaly report lookup for tenant {TenantId} because meta.anomaly_reports is not available.",
                tenantId);
            return null;
        }

        var report = await db.AnomalyReports
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.SubmissionId == submissionId)
            .OrderByDescending(x => x.AnalysedAt)
            .FirstOrDefaultAsync(ct);

        return report is null
            ? null
            : await GetReportByIdAsync(report.Id, tenantId, ct);
    }

    public async Task<AnomalyReport?> GetReportByIdAsync(
        int reportId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (!await TableExistsAsync(db, "meta", "anomaly_reports", ct))
        {
            _logger.LogInformation(
                "Skipping anomaly report lookup for report {ReportId} because meta.anomaly_reports is not available.",
                reportId);
            return null;
        }

        var hasAnomalyFindings = await TableExistsAsync(db, "meta", "anomaly_findings", ct);
        var report = await db.AnomalyReports
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == reportId && x.TenantId == tenantId, ct);

        if (report is null)
        {
            return null;
        }

        report.Findings = hasAnomalyFindings
            ? await db.AnomalyFindings
                .AsNoTracking()
                .Where(x => x.AnomalyReportId == report.Id)
                .ToListAsync(ct)
            : [];

        report.Findings = report.Findings
            .OrderByDescending(x => AnomalySupport.SeverityRank(x.Severity))
            .ThenBy(x => x.FindingType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.FieldLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return report;
    }

    public async Task<List<AnomalyReport>> GetReportsForTenantAsync(
        Guid tenantId,
        string? moduleCode = null,
        string? periodCode = null,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (!await TableExistsAsync(db, "meta", "anomaly_reports", ct))
        {
            _logger.LogInformation(
                "Skipping anomaly report lookup for tenant {TenantId} because meta.anomaly_reports is not available.",
                tenantId);
            return [];
        }

        var query = db.AnomalyReports
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(moduleCode))
        {
            var normalizedModuleCode = moduleCode.Trim().ToUpperInvariant();
            query = query.Where(x => x.ModuleCode == normalizedModuleCode);
        }

        if (!string.IsNullOrWhiteSpace(periodCode))
        {
            var normalizedPeriodCode = periodCode.Trim().ToUpperInvariant();
            query = query.Where(x => x.PeriodCode == normalizedPeriodCode);
        }

        var reports = await query
            .OrderByDescending(x => x.AnalysedAt)
            .ToListAsync(ct);

        return reports
            .GroupBy(x => x.SubmissionId)
            .Select(x => x.OrderByDescending(y => y.AnalysedAt).First())
            .OrderByDescending(x => x.AnalysedAt)
            .ToList();
    }

    public async Task<List<AnomalySectorSummary>> GetSectorSummaryAsync(
        string regulatorCode,
        string? moduleCode = null,
        string? periodCode = null,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (!await TableExistsAsync(db, "meta", "anomaly_reports", ct))
        {
            _logger.LogInformation(
                "Skipping sector anomaly summary for regulator {RegulatorCode} because meta.anomaly_reports is not available.",
                regulatorCode);
            return [];
        }

        var normalizedRegulatorCode = regulatorCode.Trim().ToUpperInvariant();
        var query = db.AnomalyReports
            .AsNoTracking()
            .Where(x => x.RegulatorCode == normalizedRegulatorCode);

        if (!string.IsNullOrWhiteSpace(moduleCode))
        {
            var normalizedModuleCode = moduleCode.Trim().ToUpperInvariant();
            query = query.Where(x => x.ModuleCode == normalizedModuleCode);
        }

        if (!string.IsNullOrWhiteSpace(periodCode))
        {
            var normalizedPeriodCode = periodCode.Trim().ToUpperInvariant();
            query = query.Where(x => x.PeriodCode == normalizedPeriodCode);
        }

        var reports = await query
            .OrderByDescending(x => x.AnalysedAt)
            .ToListAsync(ct);

        var latestByTenant = reports
            .GroupBy(x => new { x.TenantId, x.InstitutionId, x.ModuleCode, x.PeriodCode })
            .Select(x => x.OrderByDescending(y => y.AnalysedAt).First())
            .OrderBy(x => x.OverallQualityScore)
            .ThenByDescending(x => x.AlertCount)
            .ToList();

        var tenantIds = latestByTenant.Select(x => x.TenantId).Distinct().ToList();
        var licenceByTenant = await db.TenantLicenceTypes
            .AsNoTracking()
            .Include(x => x.LicenceType)
            .Where(x => tenantIds.Contains(x.TenantId) && x.IsActive)
            .GroupBy(x => x.TenantId)
            .Select(x => x.OrderByDescending(y => y.EffectiveDate).First())
            .ToDictionaryAsync(
                x => x.TenantId,
                x => x.LicenceType != null ? x.LicenceType.Code : string.Empty,
                ct);

        Dictionary<int, int> unackByReport = [];
        if (latestByTenant.Count > 0 && await TableExistsAsync(db, "meta", "anomaly_findings", ct))
        {
            var reportIds = latestByTenant.Select(x => x.Id).ToList();
            unackByReport = await db.AnomalyFindings
                .AsNoTracking()
                .Where(x => !x.IsAcknowledged && reportIds.Contains(x.AnomalyReportId))
                .GroupBy(x => x.AnomalyReportId)
                .Select(x => new { ReportId = x.Key, Count = x.Count() })
                .ToDictionaryAsync(x => x.ReportId, x => x.Count, ct);
        }

        return latestByTenant
            .Select(x => new AnomalySectorSummary
            {
                TenantId = x.TenantId,
                InstitutionId = x.InstitutionId,
                InstitutionName = x.InstitutionName,
                LicenceType = licenceByTenant.GetValueOrDefault(x.TenantId, string.Empty),
                ModuleCode = x.ModuleCode,
                PeriodCode = x.PeriodCode,
                QualityScore = x.OverallQualityScore,
                TrafficLight = x.TrafficLight,
                AlertCount = x.AlertCount,
                WarningCount = x.WarningCount,
                InfoCount = x.InfoCount,
                TotalFindings = x.TotalFindings,
                UnacknowledgedCount = unackByReport.GetValueOrDefault(x.Id)
            })
            .ToList();
    }

    public async Task AcknowledgeFindingAsync(AnomalyAcknowledgementRequest request, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var finding = await db.AnomalyFindings
            .Include(x => x.Report)
            .ThenInclude(x => x!.Findings)
            .FirstOrDefaultAsync(x => x.Id == request.FindingId && x.TenantId == request.TenantId, ct)
            ?? throw new InvalidOperationException($"Anomaly finding #{request.FindingId} was not found.");

        if (finding.IsAcknowledged)
        {
            return;
        }

        finding.IsAcknowledged = true;
        finding.AcknowledgedBy = request.AcknowledgedBy;
        finding.AcknowledgedAt = DateTime.UtcNow;
        finding.AcknowledgementReason = request.Reason;
        if (finding.Report is not null)
        {
            await RefreshReportAggregateAsync(db, finding.Report, ct);
        }
        await db.SaveChangesAsync(ct);

        await _auditLogger.Log(
            "AnomalyFinding",
            finding.Id,
            "Acknowledged",
            null,
            new { finding.Id, finding.FieldCode, finding.Severity, request.Reason },
            request.AcknowledgedBy,
            ct);
    }

    public async Task RevokeAcknowledgementAsync(int findingId, Guid tenantId, string revokedBy, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var finding = await db.AnomalyFindings
            .Include(x => x.Report)
            .ThenInclude(x => x!.Findings)
            .FirstOrDefaultAsync(x => x.Id == findingId && x.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Anomaly finding #{findingId} was not found.");

        if (!finding.IsAcknowledged)
        {
            return;
        }

        finding.IsAcknowledged = false;
        finding.AcknowledgedBy = null;
        finding.AcknowledgedAt = null;
        finding.AcknowledgementReason = null;
        if (finding.Report is not null)
        {
            await RefreshReportAggregateAsync(db, finding.Report, ct);
        }
        await db.SaveChangesAsync(ct);

        await _auditLogger.Log(
            "AnomalyFinding",
            finding.Id,
            "AcknowledgementRevoked",
            null,
            new { finding.Id, finding.FieldCode, finding.Severity },
            revokedBy,
            ct);
    }

    public async Task<byte[]> ExportReportPdfAsync(int reportId, Guid tenantId, CancellationToken ct = default)
    {
        var report = await GetReportByIdAsync(reportId, tenantId, ct)
            ?? throw new InvalidOperationException($"Anomaly report #{reportId} was not found.");

        var document = new AnomalyReportDocument(report);
        return document.GeneratePdf();
    }

    private async Task<Dictionary<string, decimal>> LoadConfigAsync(MetadataDbContext db, CancellationToken ct)
    {
        var configs = await db.AnomalyThresholdConfigs
            .AsNoTracking()
            .Where(x => x.EffectiveTo == null)
            .ToListAsync(ct);
        return AnomalySupport.BuildConfigMap(configs);
    }

    private async Task<bool> TableExistsAsync(MetadataDbContext db, string schema, string table, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            return true;
        }

        var connection = db.Database.GetDbConnection();
        if (connection is not SqlConnection sqlConnection)
        {
            return true;
        }

        if (sqlConnection.State != ConnectionState.Open)
        {
            await sqlConnection.OpenAsync(ct);
        }

        await using var command = sqlConnection.CreateCommand();
        var currentTransaction = db.Database.CurrentTransaction?.GetDbTransaction();
        if (currentTransaction is SqlTransaction sqlTransaction)
        {
            command.Transaction = sqlTransaction;
        }

        command.CommandText = """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM sys.tables t
                INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE s.name = @schemaName
                  AND t.name = @tableName
            ) THEN 1 ELSE 0 END;
            """;
        command.Parameters.Add(new SqlParameter("@schemaName", SqlDbType.NVarChar, 128) { Value = schema });
        command.Parameters.Add(new SqlParameter("@tableName", SqlDbType.NVarChar, 128) { Value = table });

        var scalar = await command.ExecuteScalarAsync(ct);
        return scalar switch
        {
            bool boolValue => boolValue,
            int intValue => intValue == 1,
            long longValue => longValue == 1L,
            decimal decimalValue => decimalValue == 1m,
            _ => false
        };
    }

    private static IEnumerable<AnomalyFinding> ScoreFieldLevelFindings(
        int submissionId,
        Guid tenantId,
        IReadOnlyDictionary<string, AnomalySupport.MetricPoint> currentMetrics,
        IReadOnlyCollection<AnomalyFieldModel> fieldModels,
        IReadOnlyCollection<AnomalyRuleBaseline> baselines,
        IReadOnlyDictionary<string, decimal> config)
    {
        var modelsByCode = fieldModels.ToDictionary(x => x.FieldCode, x => x, StringComparer.OrdinalIgnoreCase);
        var baselineByCode = baselines
            .GroupBy(x => x.FieldCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var infoThreshold = config.GetValueOrDefault("zscore.info_threshold", 1.5m);
        var warningThreshold = config.GetValueOrDefault("zscore.warning_threshold", 2.0m);
        var alertThreshold = config.GetValueOrDefault("zscore.alert_threshold", 3.0m);
        var requiredObservations = config.GetValueOrDefault("coldstart.min_observations", 30m);

        var findings = new List<AnomalyFinding>();
        foreach (var metric in currentMetrics.Values.OrderBy(x => x.FieldCode, StringComparer.OrdinalIgnoreCase))
        {
            modelsByCode.TryGetValue(metric.FieldCode, out var model);
            baselineByCode.TryGetValue(metric.FieldCode, out var baseline);

            if (model is { IsColdStart: false } && model.MeanValue.HasValue && model.StdDev is > 0m)
            {
                var zScore = (double)((metric.Value - model.MeanValue.Value) / model.StdDev.Value);
                var absoluteZ = Math.Abs(zScore);
                if (absoluteZ < (double)infoThreshold)
                {
                    continue;
                }

                var severity = absoluteZ >= (double)alertThreshold
                    ? "ALERT"
                    : absoluteZ >= (double)warningThreshold
                        ? "WARNING"
                        : "INFO";

                var rangeLow = model.MeanValue.Value - (2m * model.StdDev.Value);
                var rangeHigh = model.MeanValue.Value + (2m * model.StdDev.Value);
                var direction = zScore >= 0 ? "above" : "below";
                findings.Add(new AnomalyFinding
                {
                    TenantId = tenantId,
                    SubmissionId = submissionId,
                    FindingType = "FIELD",
                    DetectionMethod = "ZSCORE",
                    Severity = severity,
                    FieldCode = metric.FieldCode,
                    FieldLabel = metric.FieldLabel,
                    ReportedValue = metric.Value,
                    HistoricalMean = model.MeanValue,
                    HistoricalStdDev = model.StdDev,
                    ExpectedRangeLow = rangeLow,
                    ExpectedRangeHigh = rangeHigh,
                    ZScore = zScore,
                    Explanation =
                        $"{metric.FieldLabel} was reported as {metric.Value:N2}, which is {absoluteZ:F1} standard deviations {direction} the historical mean of {model.MeanValue.Value:N2}. " +
                        $"Based on {model.Observations:N0} prior observations, the expected band is {rangeLow:N2} to {rangeHigh:N2}."
                });

                continue;
            }

            var minimum = model?.RuleBasedMin ?? baseline?.MinimumValue;
            var maximum = model?.RuleBasedMax ?? baseline?.MaximumValue;
            if (minimum is null && maximum is null)
            {
                continue;
            }

            if ((minimum is null || metric.Value >= minimum.Value)
                && (maximum is null || metric.Value <= maximum.Value))
            {
                continue;
            }

            var boundary = minimum.HasValue && metric.Value < minimum.Value ? minimum.Value : maximum ?? 0m;
            var distancePct = boundary == 0m
                ? 100m
                : Math.Abs((metric.Value - boundary) / boundary) * 100m;
            var severityForRule = distancePct >= 50m
                ? "ALERT"
                : distancePct >= 20m
                    ? "WARNING"
                    : "INFO";

            findings.Add(new AnomalyFinding
            {
                TenantId = tenantId,
                SubmissionId = submissionId,
                FindingType = "FIELD",
                DetectionMethod = "RULE_BASED",
                Severity = severityForRule,
                FieldCode = metric.FieldCode,
                FieldLabel = metric.FieldLabel,
                ReportedValue = metric.Value,
                ExpectedRangeLow = minimum,
                ExpectedRangeHigh = maximum,
                Explanation =
                    $"{metric.FieldLabel} was reported as {metric.Value:N2}, outside the regulatory cold-start range of {minimum?.ToString("N2") ?? "N/A"} to {maximum?.ToString("N2") ?? "N/A"}. " +
                    $"Statistical scoring is not yet active for this field because fewer than {requiredObservations:N0} comparable observations are available."
            });
        }

        return findings;
    }

    private static IEnumerable<AnomalyFinding> ScoreCorrelationFindings(
        int submissionId,
        Guid tenantId,
        IReadOnlyDictionary<string, AnomalySupport.MetricPoint> currentMetrics,
        IReadOnlyCollection<AnomalyCorrelationRule> correlationRules,
        IReadOnlyDictionary<string, decimal> config)
    {
        var deviationThreshold = config.GetValueOrDefault("correlation.deviation_threshold", 0.30m);
        var findings = new List<AnomalyFinding>();

        foreach (var rule in correlationRules)
        {
            if (!currentMetrics.TryGetValue(rule.FieldCodeA, out var metricA)
                || !currentMetrics.TryGetValue(rule.FieldCodeB, out var metricB))
            {
                continue;
            }

            var expectedB = (rule.Slope * metricA.Value) + rule.Intercept;
            var denominator = Math.Max(Math.Abs(expectedB), 1m);
            var deviation = Math.Abs(metricB.Value - expectedB) / denominator;
            if (deviation < deviationThreshold)
            {
                continue;
            }

            var severity = deviation >= deviationThreshold * 2m
                ? "ALERT"
                : deviation >= deviationThreshold * 1.5m
                    ? "WARNING"
                    : "INFO";

            var direction = metricB.Value >= expectedB ? "higher" : "lower";
            findings.Add(new AnomalyFinding
            {
                TenantId = tenantId,
                SubmissionId = submissionId,
                FindingType = "RELATIONSHIP",
                DetectionMethod = "CORRELATION",
                Severity = severity,
                FieldCode = rule.FieldCodeA,
                FieldLabel = rule.FieldLabelA,
                RelatedFieldCode = rule.FieldCodeB,
                RelatedFieldLabel = rule.FieldLabelB,
                ReportedValue = metricA.Value,
                RelatedValue = metricB.Value,
                ExpectedValue = expectedB,
                DeviationPercent = deviation * 100m,
                Explanation =
                    $"{rule.FieldLabelA} and {rule.FieldLabelB} do not align with the historical relationship learned for this module. " +
                    $"Given {rule.FieldLabelA} at {metricA.Value:N2}, the expected {rule.FieldLabelB} is about {expectedB:N2}, but the submission reported {metricB.Value:N2}, which is {deviation:P1} {direction} than expected."
            });
        }

        return findings;
    }

    private async Task<IEnumerable<AnomalyFinding>> ScoreTemporalFindingsAsync(
        MetadataDbContext db,
        Submission submission,
        IReadOnlyDictionary<string, AnomalySupport.MetricPoint> currentMetrics,
        IReadOnlyDictionary<string, decimal> config,
        CancellationToken ct)
    {
        var minPeriods = (int)config.GetValueOrDefault("temporal.min_periods", 3m);
        var periodCount = await db.Submissions
            .AsNoTracking()
            .Where(x => x.TenantId == submission.TenantId
                        && AnomalySupport.AcceptedStatuses.Contains(x.Status)
                        && x.ReturnPeriod != null
                        && submission.ReturnPeriod != null
                        && x.ReturnPeriod.ModuleId == submission.ReturnPeriod.ModuleId)
            .Select(x => x.ReturnPeriodId)
            .Distinct()
            .CountAsync(ct);

        if (periodCount < minPeriods)
        {
            return Array.Empty<AnomalyFinding>();
        }

        var previous = await db.Submissions
            .AsNoTracking()
            .Include(x => x.ReturnPeriod)
            .Where(x => x.TenantId == submission.TenantId
                        && x.Id != submission.Id
                        && AnomalySupport.AcceptedStatuses.Contains(x.Status)
                        && x.ReturnPeriod != null
                        && submission.ReturnPeriod != null
                        && x.ReturnPeriod.ModuleId == submission.ReturnPeriod.ModuleId
                        && x.ReturnPeriod.ReportingDate < submission.ReturnPeriod.ReportingDate)
            .OrderByDescending(x => x.ReturnPeriod!.ReportingDate)
            .ThenByDescending(x => x.SubmittedAt)
            .FirstOrDefaultAsync(ct);

        if (previous?.ReturnPeriod is null)
        {
            return Array.Empty<AnomalyFinding>();
        }

        var previousMetrics = AnomalySupport.ExtractSubmissionMetrics(previous.ParsedDataJson);
        var warningThreshold = config.GetValueOrDefault("temporal.jump_pct_warning", 30m);
        var alertThreshold = config.GetValueOrDefault("temporal.jump_pct_alert", 50m);

        var findings = new List<AnomalyFinding>();
        foreach (var metric in currentMetrics.Values)
        {
            if (!previousMetrics.TryGetValue(metric.FieldCode, out var previousMetric))
            {
                continue;
            }

            var change = metric.Value - previousMetric.Value;
            if (change == 0m && metric.Value == 0m)
            {
                continue;
            }

            var changePct = previousMetric.Value == 0m
                ? 100m
                : Math.Abs(change / previousMetric.Value) * 100m;
            if (changePct < warningThreshold)
            {
                continue;
            }

            var severity = changePct >= alertThreshold ? "ALERT" : "WARNING";
            var direction = change >= 0m ? "increased" : "decreased";
            findings.Add(new AnomalyFinding
            {
                TenantId = submission.TenantId,
                SubmissionId = submission.Id,
                FindingType = "TEMPORAL",
                DetectionMethod = "TEMPORAL",
                Severity = severity,
                FieldCode = metric.FieldCode,
                FieldLabel = metric.FieldLabel,
                ReportedValue = metric.Value,
                BaselineValue = previousMetric.Value,
                DeviationPercent = changePct,
                Explanation =
                    $"{metric.FieldLabel} {direction} by {changePct:F1}% compared with the previous approved filing ({AnomalySupport.BuildPeriodCode(previous.ReturnPeriod)}). " +
                    $"The prior value was {previousMetric.Value:N2} and the current value is {metric.Value:N2}."
            });
        }

        return findings;
    }

    private static IEnumerable<AnomalyFinding> ScorePeerFindings(
        int submissionId,
        Guid tenantId,
        string licenceType,
        IReadOnlyDictionary<string, AnomalySupport.MetricPoint> currentMetrics,
        IReadOnlyCollection<AnomalyPeerGroupStatistic> peerStats,
        IReadOnlyDictionary<string, decimal> config)
    {
        var minPeers = (int)config.GetValueOrDefault("peer.min_peers", 5m);
        var iqrMultiplier = config.GetValueOrDefault("peer.iqr_multiplier", 2.5m);
        var statsByCode = peerStats.ToDictionary(x => x.FieldCode, x => x, StringComparer.OrdinalIgnoreCase);
        var findings = new List<AnomalyFinding>();

        foreach (var metric in currentMetrics.Values)
        {
            if (!statsByCode.TryGetValue(metric.FieldCode, out var stats)
                || stats.PeerCount < minPeers
                || !stats.PeerQ1.HasValue
                || !stats.PeerQ3.HasValue
                || !stats.PeerMedian.HasValue)
            {
                continue;
            }

            var iqr = stats.PeerQ3.Value - stats.PeerQ1.Value;
            if (iqr <= 0m)
            {
                continue;
            }

            var lowerFence = stats.PeerQ1.Value - (iqrMultiplier * iqr);
            var upperFence = stats.PeerQ3.Value + (iqrMultiplier * iqr);
            if (metric.Value >= lowerFence && metric.Value <= upperFence)
            {
                continue;
            }

            var beyondFence = metric.Value < lowerFence
                ? Math.Abs(metric.Value - lowerFence) / iqr
                : Math.Abs(metric.Value - upperFence) / iqr;
            var severity = beyondFence >= 2m
                ? "ALERT"
                : beyondFence >= 1m
                    ? "WARNING"
                    : "INFO";
            var deviationMedian = stats.PeerMedian.Value == 0m
                ? 0m
                : ((metric.Value - stats.PeerMedian.Value) / Math.Abs(stats.PeerMedian.Value)) * 100m;
            var direction = deviationMedian >= 0m ? "above" : "below";
            findings.Add(new AnomalyFinding
            {
                TenantId = tenantId,
                SubmissionId = submissionId,
                FindingType = "PEER",
                DetectionMethod = "PEER_IQR",
                Severity = severity,
                FieldCode = metric.FieldCode,
                FieldLabel = metric.FieldLabel,
                ReportedValue = metric.Value,
                ExpectedRangeLow = lowerFence,
                ExpectedRangeHigh = upperFence,
                BaselineValue = stats.PeerMedian,
                PeerCount = stats.PeerCount,
                PeerGroup = licenceType,
                DeviationPercent = deviationMedian,
                Explanation =
                    $"{metric.FieldLabel} is {Math.Abs(deviationMedian):F1}% {direction} the median reported by {stats.PeerCount:N0} peer institutions in the {licenceType} group. " +
                    $"The interquartile peer band is {stats.PeerQ1.Value:N2} to {stats.PeerQ3.Value:N2} and this submission reported {metric.Value:N2}."
            });
        }

        return findings;
    }

    private static decimal CalculateQualityScore(
        IEnumerable<AnomalyFinding> findings,
        IReadOnlyDictionary<string, decimal> config)
    {
        var alertWeight = config.GetValueOrDefault("quality.anomaly_weight_alert", 10m);
        var warningWeight = config.GetValueOrDefault("quality.anomaly_weight_warning", 5m);
        var infoWeight = config.GetValueOrDefault("quality.anomaly_weight_info", 2m);
        var maxPenalty = config.GetValueOrDefault("quality.max_penalty", 100m);

        var penalty = findings
            .Where(x => !x.IsAcknowledged)
            .Sum(x => x.Severity switch
            {
                "ALERT" => alertWeight,
                "WARNING" => warningWeight,
                "INFO" => infoWeight,
                _ => 0m
            });

        penalty = Math.Min(penalty, maxPenalty);
        return decimal.Round(Math.Max(0m, 100m - penalty), 2);
    }

    private async Task<string> ResolveLicenceTypeAsync(MetadataDbContext db, Submission submission, CancellationToken ct)
    {
        if (submission.Institution != null && !string.IsNullOrWhiteSpace(submission.Institution.LicenseType))
        {
            return submission.Institution.LicenseType!.Trim().ToUpperInvariant();
        }

        var licence = await db.TenantLicenceTypes
            .AsNoTracking()
            .Include(x => x.LicenceType)
            .Where(x => x.TenantId == submission.TenantId && x.IsActive)
            .OrderByDescending(x => x.EffectiveDate)
            .Select(x => x.LicenceType != null ? x.LicenceType.Code : string.Empty)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(licence) ? "UNKNOWN" : licence.Trim().ToUpperInvariant();
    }

    private static string BuildNarrativeSummary(
        string moduleCode,
        string periodCode,
        string institutionName,
        IReadOnlyCollection<AnomalyFinding> findings,
        decimal qualityScore)
    {
        if (findings.Count == 0)
        {
            return $"{institutionName} filed {moduleCode} for {periodCode} with no statistically unusual values detected. The submission quality score is {qualityScore:F1}/100.";
        }

        var highest = findings
            .OrderByDescending(x => AnomalySupport.SeverityRank(x.Severity))
            .ThenBy(x => x.FieldLabel, StringComparer.OrdinalIgnoreCase)
            .First();

        var alerts = findings.Count(x => x.Severity == "ALERT");
        var warnings = findings.Count(x => x.Severity == "WARNING");
        var peers = findings.Count(x => x.FindingType == "PEER");
        var temporal = findings.Count(x => x.FindingType == "TEMPORAL");

        return $"{institutionName} filed {moduleCode} for {periodCode} with {findings.Count:N0} anomaly findings ({alerts:N0} alert, {warnings:N0} warning). " +
               $"The strongest signal is on {highest.FieldLabel}. Peer comparison contributed {peers:N0} findings and period-over-period analysis contributed {temporal:N0}. " +
               $"Overall submission quality is {qualityScore:F1}/100.";
    }

    private async Task MaybeNotifyInstitutionAsync(AnomalyReport report, string performedBy, CancellationToken ct)
    {
        if (_notificationOrchestrator is null || report.TrafficLight == "GREEN")
        {
            return;
        }

        var priority = report.TrafficLight == "RED"
            ? NotificationPriority.High
            : NotificationPriority.Normal;

        await _notificationOrchestrator.Notify(
            new NotificationRequest
            {
                TenantId = report.TenantId,
                EventType = NotificationEvents.SystemAnnouncement,
                Title = $"Anomaly review required: {report.ModuleCode} {report.PeriodCode}",
                Message = $"{report.InstitutionName} has a {report.TrafficLight} anomaly profile with a quality score of {report.OverallQualityScore:F1}/100. Review the flagged values before final submission sign-off.",
                Priority = priority,
                RecipientInstitutionId = report.InstitutionId,
                RecipientRoles = new List<string> { "Admin", "Checker" },
                ActionUrl = $"/analytics/anomalies/{report.Id}",
                Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["reportId"] = report.Id.ToString(),
                    ["moduleCode"] = report.ModuleCode,
                    ["periodCode"] = report.PeriodCode,
                    ["trafficLight"] = report.TrafficLight,
                    ["performedBy"] = performedBy
                }
            },
            ct);
    }

    private async Task RefreshReportAggregateAsync(MetadataDbContext db, AnomalyReport report, CancellationToken ct)
    {
        var config = await LoadConfigAsync(db, ct);
        report.OverallQualityScore = CalculateQualityScore(report.Findings, config);
        report.TrafficLight = AnomalySupport.DetermineTrafficLight(report.OverallQualityScore);
        report.NarrativeSummary = BuildNarrativeSummary(
            report.ModuleCode,
            report.PeriodCode,
            report.InstitutionName,
            report.Findings,
            report.OverallQualityScore);
        report.AnalysedAt = DateTime.UtcNow;
    }
}
