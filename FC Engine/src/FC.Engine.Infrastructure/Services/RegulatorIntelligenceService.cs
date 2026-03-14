using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed class RegulatorIntelligenceService : IRegulatorIntelligenceService
{
    private static readonly Regex SafeFieldCodeRegex = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);
    private static readonly string[] CarKeys = ["car", "carratio", "capitaladequacyratio", "capitalratio", "capitaladequacy"];
    private static readonly string[] NplKeys = ["npl", "nplratio", "nonperformingloanratio", "nonperformingloansratio"];
    private static readonly string[] LiquidityKeys = ["liquidityratio", "liquidity", "lcr", "liquiditycoverageratio"];
    private static readonly string[] RoaKeys = ["roa", "returnonassets", "returnonasset"];
    private static readonly string[] TotalAssetsKeys = ["totalassets", "totalasset", "assets"];

    private readonly IDbContextFactory<MetadataDbContext> _dbFactory;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IComplianceHealthService _complianceHealthService;
    private readonly IAnomalyDetectionService _anomalyDetectionService;
    private readonly IForeSightService _foreSightService;
    private readonly IEarlyWarningService _earlyWarningService;
    private readonly ISystemicRiskService _systemicRiskService;
    private readonly IHeatmapQueryService _heatmapQueryService;
    private readonly ISectorAnalyticsService _sectorAnalyticsService;
    private readonly ILogger<RegulatorIntelligenceService> _logger;

    public RegulatorIntelligenceService(
        IDbContextFactory<MetadataDbContext> dbFactory,
        IDbConnectionFactory connectionFactory,
        IComplianceHealthService complianceHealthService,
        IAnomalyDetectionService anomalyDetectionService,
        IForeSightService foreSightService,
        IEarlyWarningService earlyWarningService,
        ISystemicRiskService systemicRiskService,
        IHeatmapQueryService heatmapQueryService,
        ISectorAnalyticsService sectorAnalyticsService,
        ILogger<RegulatorIntelligenceService> logger)
    {
        _dbFactory = dbFactory;
        _connectionFactory = connectionFactory;
        _complianceHealthService = complianceHealthService;
        _anomalyDetectionService = anomalyDetectionService;
        _foreSightService = foreSightService;
        _earlyWarningService = earlyWarningService;
        _systemicRiskService = systemicRiskService;
        _heatmapQueryService = heatmapQueryService;
        _sectorAnalyticsService = sectorAnalyticsService;
        _logger = logger;
    }

    public async Task<EntityIntelligenceProfile> GetEntityProfileAsync(
        Guid targetTenantId,
        string regulatorCode,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regulatorCode);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var institution = await ResolveInstitutionContextAsync(db, targetTenantId, ct);
        var hasAnomalyModule = await TableExistsAsync("meta", "anomaly_reports", ct);

        var latestSubmission = await LoadLatestAcceptedSubmissionAsync(db, targetTenantId, regulatorCode, ct);
        var filingSummary = await LoadFilingSummaryAsync(db, targetTenantId, regulatorCode, ct);
        var sanctionsSummary = await LoadSanctionsExposureAsync(db, institution.InstitutionName, ct);

        var complianceHealth = await TryCallAsync<ComplianceHealthScore?>(
            async () => await _complianceHealthService.GetCurrentScore(targetTenantId, ct),
            "ComplianceHealth.GetCurrentScore",
            null);
        var anomalyReports = hasAnomalyModule
            ? await TryCallAsync(
                () => _anomalyDetectionService.GetReportsForTenantAsync(targetTenantId, null, null, ct),
                "Anomaly.GetReportsForTenantAsync",
                [])
            : [];
        var predictions = await TryCallAsync(
            () => _foreSightService.GetPredictionsAsync(targetTenantId, null, ct),
            "ForeSight.GetPredictionsAsync",
            Array.Empty<ForeSightPredictionSummary>());
        var foresightAlerts = await TryCallAsync(
            () => _foreSightService.GetAlertsAsync(targetTenantId, false, ct),
            "ForeSight.GetAlertsAsync",
            Array.Empty<ForeSightAlertItem>());
        var earlyWarnings = (await TryCallAsync(
            () => _earlyWarningService.ComputeFlags(regulatorCode, ct),
            "EarlyWarning.ComputeFlags",
            []))
            .Where(x => x.InstitutionId == institution.InstitutionId)
            .OrderByDescending(x => x.TriggeredAt)
            .ToList();
        var camelsScore = (await TryCallAsync(
            () => _systemicRiskService.ComputeCamelsScores(regulatorCode, ct),
            "SystemicRisk.ComputeCamelsScores",
            []))
            .FirstOrDefault(x => x.InstitutionId == institution.InstitutionId);
        var ewiHistory = (await TryCallAsync(
            () => _heatmapQueryService.GetInstitutionEWIHistoryAsync(institution.InstitutionId, regulatorCode, 12, ct),
            "Heatmap.GetInstitutionEWIHistoryAsync",
            Array.Empty<EWITriggerRow>()))
            .ToList();

        var latestAnomaly = anomalyReports
            .OrderByDescending(x => x.AnalysedAt)
            .FirstOrDefault();
        var filingRisk = BuildPredictionSummary(
            predictions,
            ForeSightModelCodes.FilingRisk,
            "Filing Risk");
        var capitalForecast = BuildPredictionSummary(
            predictions,
            ForeSightModelCodes.CapitalBreach,
            "Capital Forecast");

        var profile = new EntityIntelligenceProfile
        {
            TenantId = targetTenantId,
            InstitutionId = institution.InstitutionId,
            InstitutionName = institution.InstitutionName,
            LicenceCategory = institution.LicenceCategory,
            RegulatorAgency = regulatorCode,
            InstitutionType = institution.LicenceCategory,
            HoldingCompanyName = institution.HoldingCompanyName,
            LatestPeriodCode = latestSubmission?.ReturnPeriod is null ? null : RegulatorAnalyticsSupport.FormatPeriodCode(latestSubmission.ReturnPeriod),
            LatestSubmissionAt = latestSubmission?.SubmittedAt,
            KeyMetrics = BuildKeyMetrics(latestSubmission?.ParsedDataJson),
            ComplianceHealth = complianceHealth,
            Anomaly = latestAnomaly is null
                ? null
                : new RegIqAnomalySummary
                {
                    ReportId = latestAnomaly.Id,
                    ModuleCode = latestAnomaly.ModuleCode,
                    PeriodCode = latestAnomaly.PeriodCode,
                    QualityScore = latestAnomaly.OverallQualityScore,
                    TrafficLight = latestAnomaly.TrafficLight,
                    AlertCount = latestAnomaly.AlertCount,
                    WarningCount = latestAnomaly.WarningCount,
                    TotalFindings = latestAnomaly.TotalFindings,
                    NarrativeSummary = latestAnomaly.NarrativeSummary
                },
            FilingRisk = filingRisk,
            CapitalForecast = capitalForecast,
            CamelsScore = camelsScore,
            FilingTimeliness = filingSummary,
            SanctionsExposure = sanctionsSummary,
            EarlyWarningFlags = earlyWarnings,
            EwiHistory = ewiHistory,
            LatestNarrative = latestAnomaly?.NarrativeSummary
        };

        profile.ActiveAlerts = BuildActiveAlerts(profile, foresightAlerts);
        profile.CrossModuleDiscrepancies = BuildDiscrepancies(profile);
        profile.DataSourcesUsed = BuildDataSourceList(
            latestSubmission is not null ? "RG-07" : null,
            complianceHealth is not null ? "RG-32" : null,
            latestAnomaly is not null ? "AI-01" : null,
            predictions.Count > 0 || foresightAlerts.Count > 0 ? "AI-04" : null,
            filingSummary is not null ? "RG-12" : null,
            earlyWarnings.Count > 0 || camelsScore is not null || ewiHistory.Count > 0 ? "RG-36" : null,
            sanctionsSummary.MatchCount > 0 ? "RG-48" : null);

        return profile;
    }

    public async Task<SectorIntelligenceSummary> GetSectorSummaryAsync(
        string regulatorCode,
        string? licenceCategory = null,
        string? periodCode = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regulatorCode);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var resolvedPeriodCode = periodCode ?? await ResolveLatestPeriodCodeAsync(db, regulatorCode, ct) ?? DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var directory = await LoadRegulatorDirectoryAsync(db, regulatorCode, licenceCategory, ct);
        var institutionIds = directory.Select(x => x.InstitutionId).ToHashSet();
        var tenantIds = directory.Select(x => x.TenantId).ToHashSet();
        var hasAnomalyModule = await TableExistsAsync("meta", "anomaly_reports", ct);

        var carDistribution = await TryCallAsync(
            () => _sectorAnalyticsService.GetCarDistribution(regulatorCode, resolvedPeriodCode, ct),
            "SectorAnalytics.GetCarDistribution",
            new SectorCarDistribution { PeriodCode = resolvedPeriodCode });
        var nplTrend = await TryCallAsync(
            () => _sectorAnalyticsService.GetNplTrend(regulatorCode, 8, ct),
            "SectorAnalytics.GetNplTrend",
            new SectorNplTrend());
        var filingTimeliness = await TryCallAsync(
            () => _sectorAnalyticsService.GetFilingTimeliness(regulatorCode, resolvedPeriodCode, ct),
            "SectorAnalytics.GetFilingTimeliness",
            new FilingTimeliness { PeriodCode = resolvedPeriodCode });
        var sectorChs = await TryCallAsync(
            () => _complianceHealthService.GetSectorSummary(regulatorCode, ct),
            "ComplianceHealth.GetSectorSummary",
            new SectorChsSummary { RegulatorCode = regulatorCode });
        var watchList = await TryCallAsync(
            () => _complianceHealthService.GetWatchList(regulatorCode, ct),
            "ComplianceHealth.GetWatchList",
            []);
        var heatmap = await TryCallAsync(
            () => _heatmapQueryService.GetSectorHeatmapAsync(regulatorCode, resolvedPeriodCode, licenceCategory, ct),
            "Heatmap.GetSectorHeatmapAsync",
            Array.Empty<HeatmapCell>());
        var anomalySummary = hasAnomalyModule
            ? await TryCallAsync(
                () => _anomalyDetectionService.GetSectorSummaryAsync(regulatorCode, null, resolvedPeriodCode, ct),
                "Anomaly.GetSectorSummaryAsync",
                [])
            : [];
        var regulatoryRanking = await TryCallAsync(
            () => _foreSightService.GetRegulatoryRiskRankingAsync(regulatorCode, licenceCategory, ct),
            "ForeSight.GetRegulatoryRiskRankingAsync",
            Array.Empty<RegulatoryActionForecast>());
        var systemicIndicators = await TryCallAsync(
            () => _systemicRiskService.ComputeSystemicIndicators(regulatorCode, ct),
            "SystemicRisk.ComputeSystemicIndicators",
            []);

        var snapshots = await LoadLatestSubmissionSnapshotsAsync(db, regulatorCode, tenantIds, resolvedPeriodCode, ct);

        var filteredWatchList = watchList
            .Where(x => tenantIds.Contains(x.TenantId))
            .OrderBy(x => x.CurrentScore)
            .ToList();
        var filteredAnomalySummary = anomalySummary
            .Where(x => tenantIds.Contains(x.TenantId))
            .OrderBy(x => x.QualityScore)
            .ToList();
        var filteredRanking = regulatoryRanking
            .Where(x => tenantIds.Contains(x.TenantId))
            .OrderByDescending(x => x.InterventionProbability)
            .ToList();

        var summary = new SectorIntelligenceSummary
        {
            RegulatorAgency = regulatorCode,
            LicenceCategory = licenceCategory,
            PeriodCode = resolvedPeriodCode,
            EntityCount = directory.Count,
            AverageCarRatio = AverageMetric(snapshots.Select(x => RegulatorAnalyticsSupport.ExtractFirstMetric(x.ParsedDataJson, CarKeys))),
            AverageNplRatio = AverageMetric(snapshots.Select(x => RegulatorAnalyticsSupport.ExtractFirstMetric(x.ParsedDataJson, NplKeys))),
            AverageLiquidityRatio = AverageMetric(snapshots.Select(x => RegulatorAnalyticsSupport.ExtractFirstMetric(x.ParsedDataJson, LiquidityKeys))),
            AverageComplianceHealthScore = filteredWatchList.Count > 0
                ? decimal.Round(filteredWatchList.Average(x => x.CurrentScore), 2)
                : sectorChs.SectorAverage,
            AverageAnomalyQualityScore = filteredAnomalySummary.Count > 0
                ? decimal.Round(filteredAnomalySummary.Average(x => x.QualityScore), 2)
                : null,
            OverdueFilingCount = filingTimeliness.Institutions
                .Where(x => institutionIds.Contains(x.InstitutionId))
                .Sum(x => x.Late),
            HighRiskEntityCount = filteredRanking.Count(x => x.RiskBand is "HIGH" or "CRITICAL")
                + filteredAnomalySummary.Count(x => x.TrafficLight == "RED"),
            CarDistribution = carDistribution,
            NplTrend = nplTrend,
            FilingTimeliness = filingTimeliness,
            ComplianceHealthSummary = sectorChs,
            WatchList = filteredWatchList,
            Heatmap = heatmap.Where(x => institutionIds.Contains(x.InstitutionId)).ToList(),
            AnomalyHotspots = filteredAnomalySummary,
            RegulatoryRiskRanking = filteredRanking,
            SystemicRiskIndicatorsRaw = systemicIndicators,
            ExaminationPriorityRanking = filteredRanking
                .Take(20)
                .Select((x, index) => new RegIqEntityRankingItem
                {
                    TenantId = x.TenantId,
                    InstitutionName = x.InstitutionName,
                    LicenceCategory = x.LicenceType,
                    Score = x.InterventionProbability,
                    ScoreLabel = "Intervention Probability",
                    RiskBand = x.RiskBand
                })
                .ToList(),
            SystemicIndicators = systemicIndicators
                .Select(x => new RegIqSystemicIndicator
                {
                    IndicatorCode = x.IndicatorCode,
                    Title = x.Title,
                    CurrentValue = x.CurrentValue,
                    Threshold = x.Threshold,
                    Severity = x.Severity.ToString().ToUpperInvariant(),
                    AffectedEntities = x.AffectedEntities,
                    Description = x.Description
                })
                .ToList()
        };

        summary.DataSourcesUsed = BuildDataSourceList(
            "RG-07",
            "RG-32",
            "AI-01",
            filteredRanking.Count > 0 ? "AI-04" : null,
            "RG-12",
            systemicIndicators.Count > 0 || heatmap.Count > 0 ? "RG-36" : null);

        return summary;
    }

    public async Task<List<MetricRankingEntry>> RankEntitiesByMetricAsync(
        string fieldCode,
        string regulatorCode,
        string? licenceCategory = null,
        string? periodCode = null,
        int topN = 20,
        bool ascending = false,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(regulatorCode);

        var normalizedFieldCode = NormalizeFieldCode(fieldCode);
        if (!SafeFieldCodeRegex.IsMatch(normalizedFieldCode))
        {
            throw new InvalidOperationException($"Field code '{fieldCode}' contains unsupported characters for SQL JSON extraction.");
        }

        var jsonExpression = BuildJsonMetricExpression(normalizedFieldCode);
        var direction = ascending ? "ASC" : "DESC";
        var sql = $"""
WITH ranked_submissions AS
(
    SELECT
        s.TenantId,
        s.InstitutionId,
        i.InstitutionName,
        COALESCE(licence.Code, i.LicenseType, 'UNKNOWN') AS LicenceCategory,
        CASE
            WHEN rp.Quarter IS NOT NULL AND rp.Quarter BETWEEN 1 AND 4
                THEN CONCAT(rp.Year, '-Q', rp.Quarter)
            ELSE CONCAT(rp.Year, '-', RIGHT(CONCAT('0', rp.Month), 2))
        END AS PeriodCode,
        s.SubmittedAt,
        TRY_CONVERT(decimal(18,6), {jsonExpression}) AS MetricValue,
        ROW_NUMBER() OVER (PARTITION BY s.TenantId ORDER BY s.SubmittedAt DESC, s.Id DESC) AS rn
    FROM dbo.return_submissions s
    INNER JOIN dbo.institutions i
        ON i.Id = s.InstitutionId
    INNER JOIN dbo.return_periods rp
        ON rp.Id = s.ReturnPeriodId
    OUTER APPLY
    (
        SELECT TOP (1)
            lt.Code,
            lt.Regulator
        FROM dbo.tenant_licence_types tlt
        INNER JOIN dbo.licence_types lt
            ON lt.Id = tlt.LicenceTypeId
        WHERE tlt.TenantId = s.TenantId
          AND tlt.IsActive = 1
        ORDER BY tlt.EffectiveDate DESC, tlt.Id DESC
    ) licence
    WHERE s.Status = 'Accepted'
      AND s.ParsedDataJson IS NOT NULL
      AND licence.Regulator = @RegulatorCode
      AND (@LicenceCategory IS NULL OR licence.Code = @LicenceCategory)
      AND (@PeriodCode IS NULL OR
           CASE
               WHEN rp.Quarter IS NOT NULL AND rp.Quarter BETWEEN 1 AND 4
                   THEN CONCAT(rp.Year, '-Q', rp.Quarter)
               ELSE CONCAT(rp.Year, '-', RIGHT(CONCAT('0', rp.Month), 2))
           END = @PeriodCode)
)
SELECT TOP (@TopN)
    TenantId,
    InstitutionId,
    InstitutionName,
    LicenceCategory,
    PeriodCode,
    SubmittedAt,
    MetricValue
FROM ranked_submissions
WHERE rn = 1
  AND MetricValue IS NOT NULL
ORDER BY MetricValue {direction}, InstitutionName ASC;
""";

        using var connection = await _connectionFactory.CreateConnectionAsync(null, ct);
        if (connection is not SqlConnection sqlConnection)
        {
            throw new InvalidOperationException("Regulator metric ranking requires a SQL Server connection.");
        }

        await using var command = sqlConnection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.Parameters.Add(new SqlParameter("@RegulatorCode", SqlDbType.VarChar, 20) { Value = regulatorCode });
        command.Parameters.Add(new SqlParameter("@LicenceCategory", SqlDbType.VarChar, 50) { Value = (object?)licenceCategory ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@PeriodCode", SqlDbType.VarChar, 20) { Value = (object?)periodCode ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@TopN", SqlDbType.Int) { Value = Math.Clamp(topN, 1, 250) });

        var results = new List<MetricRankingEntry>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rank = 1;
        while (await reader.ReadAsync(ct))
        {
            results.Add(new MetricRankingEntry
            {
                Rank = rank++,
                TenantId = reader.GetGuid(0),
                InstitutionId = reader.GetInt32(1),
                InstitutionName = reader.GetString(2),
                LicenceCategory = reader.GetString(3),
                PeriodCode = reader.IsDBNull(4) ? null : reader.GetString(4),
                SubmittedAt = reader.GetDateTime(5),
                FieldCode = normalizedFieldCode,
                MetricValue = reader.GetDecimal(6)
            });
        }

        return results;
    }

    public async Task<ExaminationBriefing> GenerateExaminationBriefingAsync(
        Guid targetTenantId,
        string regulatorCode,
        CancellationToken ct = default)
    {
        var profile = await GetEntityProfileAsync(targetTenantId, regulatorCode, ct);
        var peerContext = await GetSectorSummaryAsync(regulatorCode, profile.LicenceCategory, profile.LatestPeriodCode, ct);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var trends = await LoadMetricTrendSeriesAsync(db, targetTenantId, regulatorCode, ct);
        var focusAreas = BuildFocusAreas(profile);

        return new ExaminationBriefing
        {
            TenantId = targetTenantId,
            InstitutionName = profile.InstitutionName,
            LicenceCategory = profile.LicenceCategory,
            RegulatorAgency = regulatorCode,
            GeneratedAt = DateTime.UtcNow,
            Profile = profile,
            PeerContext = peerContext,
            Trends = trends,
            FocusAreas = focusAreas,
            OpenInvestigations = [],
            DataSourcesUsed = profile.DataSourcesUsed
                .Concat(peerContext.DataSourcesUsed)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private async Task<InstitutionContext> ResolveInstitutionContextAsync(
        MetadataDbContext db,
        Guid tenantId,
        CancellationToken ct)
    {
        var institution = await db.Institutions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"No active institution was found for tenant {tenantId}.");

        var licence = await db.TenantLicenceTypes
            .AsNoTracking()
            .Include(x => x.LicenceType)
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderByDescending(x => x.EffectiveDate)
            .FirstOrDefaultAsync(ct);

        return new InstitutionContext(
            tenantId,
            institution.Id,
            institution.InstitutionName,
            licence?.LicenceType?.Code ?? institution.LicenseType ?? "UNKNOWN",
            licence?.LicenceType?.Name ?? institution.LicenseType ?? "UNKNOWN",
            institution.ParentInstitutionId.HasValue ? institution.ParentInstitution?.InstitutionName : null);
    }

    private async Task<List<DirectoryInstitution>> LoadRegulatorDirectoryAsync(
        MetadataDbContext db,
        string regulatorCode,
        string? licenceCategory,
        CancellationToken ct)
    {
        var tenantLicences = await db.TenantLicenceTypes
            .AsNoTracking()
            .Include(x => x.LicenceType)
            .Where(x => x.IsActive
                && x.LicenceType != null
                && x.LicenceType.Regulator == regulatorCode
                && (licenceCategory == null || x.LicenceType.Code == licenceCategory))
            .ToListAsync(ct);

        var tenantIds = tenantLicences.Select(x => x.TenantId).Distinct().ToList();
        if (tenantIds.Count == 0)
        {
            return [];
        }

        var institutions = await db.Institutions
            .AsNoTracking()
            .Where(x => tenantIds.Contains(x.TenantId) && x.IsActive)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

        return institutions
            .Join(
                tenantLicences.GroupBy(x => x.TenantId).Select(g => g.OrderByDescending(x => x.EffectiveDate).First()),
                institution => institution.TenantId,
                licence => licence.TenantId,
                (institution, licence) => new DirectoryInstitution(
                    institution.TenantId,
                    institution.Id,
                    institution.InstitutionName,
                    licence.LicenceType?.Code ?? institution.LicenseType ?? "UNKNOWN"))
            .ToList();
    }

    private async Task<Submission?> LoadLatestAcceptedSubmissionAsync(
        MetadataDbContext db,
        Guid tenantId,
        string regulatorCode,
        CancellationToken ct)
    {
        var scoped = await db.Submissions
            .AsNoTracking()
            .Include(x => x.ReturnPeriod)
                .ThenInclude(x => x!.Module)
            .Where(x => x.TenantId == tenantId
                && x.ParsedDataJson != null
                && (x.Status == SubmissionStatus.Accepted || x.Status == SubmissionStatus.AcceptedWithWarnings)
                && x.ReturnPeriod != null
                && x.ReturnPeriod.Module != null
                && x.ReturnPeriod.Module.RegulatorCode == regulatorCode)
            .OrderByDescending(x => x.SubmittedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (scoped is not null)
        {
            return scoped;
        }

        return await db.Submissions
            .AsNoTracking()
            .Include(x => x.ReturnPeriod)
            .Where(x => x.TenantId == tenantId
                && x.ParsedDataJson != null
                && (x.Status == SubmissionStatus.Accepted || x.Status == SubmissionStatus.AcceptedWithWarnings))
            .OrderByDescending(x => x.SubmittedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<List<Submission>> LoadLatestSubmissionSnapshotsAsync(
        MetadataDbContext db,
        string regulatorCode,
        ISet<Guid> tenantIds,
        string periodCode,
        CancellationToken ct)
    {
        var rows = await db.Submissions
            .AsNoTracking()
            .Include(x => x.ReturnPeriod)
                .ThenInclude(x => x!.Module)
            .Where(x => tenantIds.Contains(x.TenantId)
                && x.ParsedDataJson != null
                && x.Status == SubmissionStatus.Accepted
                && x.ReturnPeriod != null
                && x.ReturnPeriod.Module != null
                && x.ReturnPeriod.Module.RegulatorCode == regulatorCode)
            .OrderByDescending(x => x.SubmittedAt)
            .ToListAsync(ct);

        var filtered = rows
            .Where(x => x.ReturnPeriod != null
                && string.Equals(RegulatorAnalyticsSupport.FormatPeriodCode(x.ReturnPeriod), periodCode, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (filtered.Count > 0)
        {
            return filtered
                .GroupBy(x => x.TenantId)
                .Select(g => g.OrderByDescending(x => x.SubmittedAt).ThenByDescending(x => x.Id).First())
                .ToList();
        }

        return rows
            .GroupBy(x => x.TenantId)
            .Select(g => g.OrderByDescending(x => x.SubmittedAt).ThenByDescending(x => x.Id).First())
            .ToList();
    }

    private async Task<RegIqFilingTimelinessSummary> LoadFilingSummaryAsync(
        MetadataDbContext db,
        Guid tenantId,
        string regulatorCode,
        CancellationToken ct)
    {
        var records = await db.FilingSlaRecords
            .AsNoTracking()
            .Include(x => x.Module)
            .Where(x => x.TenantId == tenantId
                && x.Module != null
                && x.Module.RegulatorCode == regulatorCode)
            .OrderByDescending(x => x.DeadlineDate)
            .Take(24)
            .ToListAsync(ct);

        return new RegIqFilingTimelinessSummary
        {
            TotalFilings = records.Count,
            OnTimeFilings = records.Count(x => x.OnTime == true),
            LateFilings = records.Count(x => x.OnTime == false),
            OverdueFilings = records.Count(x => x.SubmittedDate == null && x.DeadlineDate.Date < DateTime.UtcNow.Date),
            LatestDeadline = records.FirstOrDefault()?.DeadlineDate,
            LatestSubmittedAt = records.Where(x => x.SubmittedDate.HasValue).Select(x => x.SubmittedDate).Max()
        };
    }

    private async Task<RegIqSanctionsExposureSummary> LoadSanctionsExposureAsync(
        MetadataDbContext db,
        string institutionName,
        CancellationToken ct)
    {
        var rows = await db.SanctionsScreeningResults
            .AsNoTracking()
            .Where(x => x.Subject == institutionName || x.Subject.Contains(institutionName) || institutionName.Contains(x.Subject))
            .OrderByDescending(x => x.CreatedAt)
            .Take(25)
            .ToListAsync(ct);

        var highest = rows
            .OrderByDescending(x => x.MatchScore)
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefault();

        return new RegIqSanctionsExposureSummary
        {
            MatchCount = rows.Count,
            HighestMatchScore = highest?.MatchScore,
            HighestRiskLevel = highest?.RiskLevel ?? "NONE",
            LatestMatchedName = highest?.MatchedName,
            LatestMatchedAt = rows.FirstOrDefault()?.CreatedAt
        };
    }

    private async Task<string?> ResolveLatestPeriodCodeAsync(
        MetadataDbContext db,
        string regulatorCode,
        CancellationToken ct)
    {
        var latestPeriod = await db.Submissions
            .AsNoTracking()
            .Include(x => x.ReturnPeriod)
                .ThenInclude(x => x!.Module)
            .Where(x => x.Status == SubmissionStatus.Accepted
                && x.ReturnPeriod != null
                && x.ReturnPeriod.Module != null
                && x.ReturnPeriod.Module.RegulatorCode == regulatorCode)
            .OrderByDescending(x => x.SubmittedAt)
            .Select(x => x.ReturnPeriod)
            .FirstOrDefaultAsync(ct);

        return latestPeriod is null ? null : RegulatorAnalyticsSupport.FormatPeriodCode(latestPeriod);
    }

    private async Task<List<RegIqMetricTrendSeries>> LoadMetricTrendSeriesAsync(
        MetadataDbContext db,
        Guid tenantId,
        string regulatorCode,
        CancellationToken ct)
    {
        var submissions = await db.Submissions
            .AsNoTracking()
            .Include(x => x.ReturnPeriod)
                .ThenInclude(x => x!.Module)
            .Where(x => x.TenantId == tenantId
                && x.ParsedDataJson != null
                && (x.Status == SubmissionStatus.Accepted || x.Status == SubmissionStatus.AcceptedWithWarnings)
                && x.ReturnPeriod != null
                && x.ReturnPeriod.Module != null
                && x.ReturnPeriod.Module.RegulatorCode == regulatorCode)
            .OrderByDescending(x => x.SubmittedAt)
            .Take(8)
            .ToListAsync(ct);

        return BuildTrendSeries(submissions
            .OrderBy(x => x.SubmittedAt)
            .ToList());
    }

    private static List<RegIqMetricSnapshot> BuildKeyMetrics(string? parsedDataJson)
    {
        return new List<(string Code, string Label, string[] Keys)>
        {
            ("carratio", "Capital Adequacy Ratio", CarKeys),
            ("nplratio", "NPL Ratio", NplKeys),
            ("liquidityratio", "Liquidity Ratio", LiquidityKeys),
            ("roa", "Return on Assets", RoaKeys),
            ("totalassets", "Total Assets", TotalAssetsKeys)
        }
        .Select(x => new RegIqMetricSnapshot
        {
            MetricCode = x.Code,
            MetricLabel = x.Label,
            Value = RegulatorAnalyticsSupport.ExtractFirstMetric(parsedDataJson, x.Keys)
        })
        .Where(x => x.Value.HasValue)
        .ToList();
    }

    private static RegIqPredictionSummary? BuildPredictionSummary(
        IReadOnlyList<ForeSightPredictionSummary> predictions,
        string modelCode,
        string label)
    {
        var prediction = predictions
            .Where(x => x.ModelCode == modelCode)
            .OrderByDescending(x => x.PredictionDate)
            .ThenByDescending(x => x.ConfidenceScore)
            .FirstOrDefault();

        return prediction is null
            ? null
            : new RegIqPredictionSummary
            {
                ModelCode = prediction.ModelCode,
                Label = label,
                PredictedValue = prediction.PredictedValue,
                ConfidenceScore = prediction.ConfidenceScore,
                RiskBand = prediction.RiskBand,
                PeriodCode = prediction.TargetPeriodCode,
                Explanation = prediction.Explanation,
                Recommendation = prediction.Recommendation
            };
    }

    private static List<RegIqAlertSummary> BuildActiveAlerts(
        EntityIntelligenceProfile profile,
        IReadOnlyList<ForeSightAlertItem> foresightAlerts)
    {
        var alerts = foresightAlerts
            .Take(10)
            .Select(x => new RegIqAlertSummary
            {
                Source = "AI-04",
                AlertCode = x.AlertType,
                Severity = x.Severity,
                Title = x.Title,
                Detail = x.Body,
                TriggeredAt = x.DispatchedAt
            })
            .ToList();

        alerts.AddRange(profile.EarlyWarningFlags.Select(x => new RegIqAlertSummary
        {
            Source = "RG-36",
            AlertCode = x.FlagCode,
            Severity = x.Severity.ToString().ToUpperInvariant(),
            Title = x.Message,
            TriggeredAt = x.TriggeredAt
        }));

        if (profile.SanctionsExposure?.MatchCount > 0)
        {
            alerts.Add(new RegIqAlertSummary
            {
                Source = "RG-48",
                AlertCode = "SANCTIONS_MATCH",
                Severity = "WARNING",
                Title = $"{profile.SanctionsExposure.MatchCount} sanctions screening match(es) linked to the institution.",
                Detail = profile.SanctionsExposure.LatestMatchedName,
                TriggeredAt = profile.SanctionsExposure.LatestMatchedAt
            });
        }

        if (profile.FilingTimeliness?.OverdueFilings > 0)
        {
            alerts.Add(new RegIqAlertSummary
            {
                Source = "RG-12",
                AlertCode = "OVERDUE_FILING",
                Severity = "WARNING",
                Title = $"{profile.FilingTimeliness.OverdueFilings} filing(s) are currently overdue."
            });
        }

        return alerts
            .OrderByDescending(x => x.TriggeredAt)
            .ThenByDescending(x => x.Severity, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<RegIqDiscrepancySummary> BuildDiscrepancies(EntityIntelligenceProfile profile)
    {
        var discrepancies = new List<RegIqDiscrepancySummary>();

        if (profile.ComplianceHealth?.OverallScore is { } chsScore
            && profile.Anomaly?.QualityScore is { } anomalyScore
            && chsScore >= 75m
            && anomalyScore < 70m)
        {
            discrepancies.Add(new RegIqDiscrepancySummary
            {
                DiscrepancyCode = "CHS_VS_ANOMALY",
                Title = "Strong compliance score alongside weak data quality",
                Severity = "WARNING",
                Description = $"CHS is {chsScore:0.##} while anomaly quality is {anomalyScore:0.##}.",
                Source = "RG-32/AI-01"
            });
        }

        if (profile.FilingRisk?.RiskBand is "HIGH" or "CRITICAL"
            && profile.FilingTimeliness is { LateFilings: 0, OverdueFilings: 0 })
        {
            discrepancies.Add(new RegIqDiscrepancySummary
            {
                DiscrepancyCode = "FORECAST_VS_TIMELINESS",
                Title = "Forecasted filing risk exceeds recent observed timeliness",
                Severity = "INFO",
                Description = "ForeSight indicates elevated filing risk despite clean recent filing SLA records.",
                Source = "AI-04/RG-12"
            });
        }

        if (profile.CamelsScore is { Rating: RiskRating.Red } && profile.EarlyWarningFlags.Count == 0)
        {
            discrepancies.Add(new RegIqDiscrepancySummary
            {
                DiscrepancyCode = "CAMELS_WITHOUT_EWI",
                Title = "Weak CAMELS posture without active early-warning flags",
                Severity = "INFO",
                Description = "CAMELS composite is red, but no current EWI flags were returned for the institution.",
                Source = "RG-36"
            });
        }

        return discrepancies;
    }

    private static List<RegIqFocusArea> BuildFocusAreas(EntityIntelligenceProfile profile)
    {
        var areas = new List<RegIqFocusArea>();

        if (profile.KeyMetrics.FirstOrDefault(x => x.MetricCode == "carratio")?.Value is { } car && car < 15m)
        {
            areas.Add(new RegIqFocusArea
            {
                Area = "Capital adequacy",
                Reason = $"Latest CAR is {car:0.##}% against the 15% supervisory floor.",
                Priority = "HIGH"
            });
        }

        if (profile.Anomaly?.TotalFindings > 0)
        {
            areas.Add(new RegIqFocusArea
            {
                Area = "Data quality and return accuracy",
                Reason = $"{profile.Anomaly.TotalFindings} anomaly finding(s) were recorded in the latest analysed return.",
                Priority = profile.Anomaly.TrafficLight == "RED" ? "HIGH" : "MEDIUM"
            });
        }

        if (profile.FilingTimeliness?.LateFilings > 0 || profile.FilingTimeliness?.OverdueFilings > 0)
        {
            areas.Add(new RegIqFocusArea
            {
                Area = "Filing governance",
                Reason = $"Late filings: {profile.FilingTimeliness?.LateFilings ?? 0}; overdue filings: {profile.FilingTimeliness?.OverdueFilings ?? 0}.",
                Priority = "MEDIUM"
            });
        }

        if (profile.SanctionsExposure?.MatchCount > 0)
        {
            areas.Add(new RegIqFocusArea
            {
                Area = "Sanctions and AML controls",
                Reason = $"{profile.SanctionsExposure.MatchCount} sanctions screening match(es) were linked to the institution name.",
                Priority = "HIGH"
            });
        }

        if (profile.EarlyWarningFlags.Count > 0)
        {
            areas.Add(new RegIqFocusArea
            {
                Area = "Emerging early-warning indicators",
                Reason = profile.EarlyWarningFlags[0].Message,
                Priority = profile.EarlyWarningFlags[0].Severity == EarlyWarningSeverity.Red ? "HIGH" : "MEDIUM"
            });
        }

        return areas
            .DistinctBy(x => x.Area)
            .Take(6)
            .ToList();
    }

    private static List<RegIqMetricTrendSeries> BuildTrendSeries(IReadOnlyList<Submission> submissions)
    {
        var seriesDefinitions = new List<(string Code, string Label, string[] Keys)>
        {
            ("carratio", "Capital Adequacy Ratio", CarKeys),
            ("nplratio", "NPL Ratio", NplKeys),
            ("liquidityratio", "Liquidity Ratio", LiquidityKeys)
        };

        return seriesDefinitions
            .Select(definition => new RegIqMetricTrendSeries
            {
                MetricCode = definition.Code,
                MetricLabel = definition.Label,
                Points = submissions
                    .Where(x => x.ReturnPeriod != null)
                    .Select(x => new RegIqMetricTrendPoint
                    {
                        PeriodCode = RegulatorAnalyticsSupport.FormatPeriodCode(x.ReturnPeriod!),
                        Value = RegulatorAnalyticsSupport.ExtractFirstMetric(x.ParsedDataJson, definition.Keys),
                        SubmittedAt = x.SubmittedAt
                    })
                    .Where(x => x.Value.HasValue)
                    .ToList()
            })
            .Where(x => x.Points.Count > 0)
            .ToList();
    }

    private static decimal? AverageMetric(IEnumerable<decimal?> values)
    {
        var filtered = values.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        return filtered.Count == 0 ? null : decimal.Round(filtered.Average(), 2);
    }

    private static string NormalizeFieldCode(string fieldCode)
    {
        var letters = fieldCode
            .Trim()
            .ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch) || ch == '_')
            .ToArray();

        return new string(letters);
    }

    private static string BuildJsonMetricExpression(string fieldCode)
    {
        var expressions = new[]
        {
            $"JSON_VALUE(s.ParsedDataJson, '$.{fieldCode}')",
            $"JSON_VALUE(s.ParsedDataJson, '$.metrics.{fieldCode}')",
            $"JSON_VALUE(s.ParsedDataJson, '$.summary.{fieldCode}')",
            $"JSON_VALUE(s.ParsedDataJson, '$.ratios.{fieldCode}')"
        }.Select(x => $"NULLIF({x}, '')");

        return $"COALESCE({string.Join(", ", expressions)})";
    }

    private static List<string> BuildDataSourceList(params string?[] sources)
    {
        return sources
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<T> TryCallAsync<T>(
        Func<Task<T>> action,
        string dependency,
        T fallback = default!)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Regulator intelligence dependency {Dependency} failed; continuing with fallback.", dependency);
            return fallback;
        }
    }

    private async Task<bool> TableExistsAsync(string schema, string table, CancellationToken ct)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(null, ct);
        if (connection is not SqlConnection sqlConnection)
        {
            return true;
        }

        if (sqlConnection.State != ConnectionState.Open)
        {
            await sqlConnection.OpenAsync(ct);
        }

        await using var command = sqlConnection.CreateCommand();
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

    private sealed record InstitutionContext(
        Guid TenantId,
        int InstitutionId,
        string InstitutionName,
        string LicenceCategory,
        string LicenceName,
        string? HoldingCompanyName);

    private sealed record DirectoryInstitution(
        Guid TenantId,
        int InstitutionId,
        string InstitutionName,
        string LicenceCategory);
}
