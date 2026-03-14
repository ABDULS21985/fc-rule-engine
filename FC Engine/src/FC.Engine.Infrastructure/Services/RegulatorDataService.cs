using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Audit;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed class RegulatorDataService : IRegulatorDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, string> DefaultModuleByRegulator = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["CBN"] = "CBN_PRUDENTIAL",
        ["NDIC"] = "NDIC_SRF",
        ["NAICOM"] = "NAICOM_QR",
        ["SEC"] = "SEC_CMO",
        ["NFIU"] = "NFIU_AML"
    };
    private static readonly IReadOnlyDictionary<string, string> MetricLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["carratio"] = "Capital Adequacy Ratio",
        ["nplratio"] = "NPL Ratio",
        ["liquidityratio"] = "Liquidity Ratio",
        ["loandepositratio"] = "Loan to Deposit Ratio",
        ["roa"] = "Return on Assets",
        ["totalassets"] = "Total Assets"
    };
    private static readonly string[] ReadOnlyForbiddenTokens =
    [
        "INSERT",
        "UPDATE",
        "DELETE",
        "MERGE",
        "ALTER",
        "DROP",
        "CREATE",
        "TRUNCATE",
        "EXEC",
        "EXECUTE",
        "GRANT",
        "DENY",
        "REVOKE",
        "DBCC"
    ];

    private readonly IDbContextFactory<MetadataDbContext> _dbFactory;
    private readonly ITenantContext _tenantContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<RegulatorDataService> _logger;

    public RegulatorDataService(
        IDbContextFactory<MetadataDbContext> dbFactory,
        ITenantContext tenantContext,
        IHttpContextAccessor httpContextAccessor,
        ILogger<RegulatorDataService> logger)
    {
        _dbFactory = dbFactory;
        _tenantContext = tenantContext;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<Guid?> ResolveEntityByName(string nameOrAlias, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nameOrAlias))
        {
            return null;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var matches = await FindEntityMatchesAsync(db, nameOrAlias, null, ct);
        if (matches.Count == 0)
        {
            return null;
        }

        if (matches.Count == 1 || matches[0].Score >= 0.98m || matches[0].Score - matches[1].Score >= 0.15m)
        {
            return matches[0].TenantId;
        }

        return null;
    }

    public async Task<List<(Guid TenantId, string Name, string LicenceCategory)>> SearchEntities(
        string searchTerm,
        string? licenceCategory = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return new List<(Guid TenantId, string Name, string LicenceCategory)>();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var matches = await FindEntityMatchesAsync(db, searchTerm, licenceCategory, ct);
        return matches
            .Take(20)
            .Select(x => (x.TenantId, x.InstitutionName, x.LicenceCategory))
            .ToList();
    }

    public async Task<List<Dictionary<string, object>>> ExecuteRegulatorQuery(
        string templateCode,
        Dictionary<string, object> parameters,
        string regulatorId,
        string regulatorAgency,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateCode);
        ArgumentNullException.ThrowIfNull(parameters);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var access = await EnsureRegulatorAccessAsync(db, regulatorId, regulatorAgency, ct);

        var template = await db.RegIqQueryTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TemplateCode == templateCode && x.IsActive, ct)
            ?? throw new InvalidOperationException($"RegulatorIQ query template '{templateCode}' was not found.");

        ValidateReadOnlySql(template.SqlTemplate);

        var rows = await ExecuteTemplateAsync(db, template.SqlTemplate, parameters, access, ct);
        var entitiesAccessed = await ResolveEntitiesAccessedAsync(db, parameters, rows, access.RegulatorAgency, ct);
        var dataSources = DeserializeStringList(template.DataSourcesJson);

        await AppendAccessLogAsync(
            db,
            access,
            $"TEMPLATE:{templateCode}",
            BuildSummary(templateCode, rows.Count),
            template.ClassificationLevel,
            entitiesAccessed,
            dataSources,
            parameters,
            ct);

        _logger.LogInformation(
            "RegulatorIQ template {TemplateCode} executed by {RegulatorId} ({RegulatorAgency}) and returned {RowCount} rows.",
            templateCode,
            access.RegulatorId,
            access.RegulatorAgency,
            rows.Count);

        return rows;
    }

    public async Task<EntityIntelligenceProfile> GetEntityProfile(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var access = await EnsureRegulatorAccessAsync(db, null, null, ct);
        var profile = await BuildEntityProfileAsync(db, access, tenantId, ct);

        await AppendAccessLogAsync(
            db,
            access,
            $"ENTITY_PROFILE:{profile.InstitutionName}",
            BuildSummary("ENTITY_PROFILE", 1),
            "CONFIDENTIAL",
            new List<Guid> { tenantId },
            profile.DataSourcesUsed,
            new Dictionary<string, object> { ["EntityTenantId"] = tenantId },
            ct);

        return profile;
    }

    public async Task<SectorIntelligenceSummary> GetSectorSummary(
        string? licenceCategory = null,
        string? periodCode = null,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var access = await EnsureRegulatorAccessAsync(db, null, null, ct);
        var summary = await BuildSectorSummaryAsync(db, access, licenceCategory, periodCode, ct);

        await AppendAccessLogAsync(
            db,
            access,
            "SECTOR_SUMMARY",
            BuildSummary("SECTOR_SUMMARY", summary.EntityCount),
            "CONFIDENTIAL",
            await ResolveSectorTenantScopeAsync(db, access.RegulatorAgency, licenceCategory, ct),
            summary.DataSourcesUsed,
            new Dictionary<string, object>
            {
                ["LicenceCategory"] = licenceCategory ?? string.Empty,
                ["PeriodCode"] = periodCode ?? string.Empty
            },
            ct);

        return summary;
    }

    public async Task<ExaminationBriefing> GenerateExaminationBriefing(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var access = await EnsureRegulatorAccessAsync(db, null, null, ct);
        var profile = await BuildEntityProfileAsync(db, access, tenantId, ct);
        var peerContext = await BuildSectorSummaryAsync(db, access, profile.LicenceCategory, profile.LatestPeriodCode, ct);
        var trends = await LoadMetricTrendsAsync(db, tenantId, access.RegulatorAgency, ct);
        var investigations = await LoadInvestigationsAsync(db, profile.InstitutionId, access.RegulatorAgency, ct);
        var focusAreas = BuildFocusAreas(profile, investigations, peerContext);
        var dataSources = profile.DataSourcesUsed
            .Concat(peerContext.DataSourcesUsed)
            .Concat(investigations.Select(x => x.InvestigationType))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var briefing = new ExaminationBriefing
        {
            TenantId = tenantId,
            InstitutionName = profile.InstitutionName,
            LicenceCategory = profile.LicenceCategory,
            RegulatorAgency = access.RegulatorAgency,
            GeneratedAt = DateTime.UtcNow,
            Profile = profile,
            PeerContext = peerContext,
            Trends = trends,
            FocusAreas = focusAreas,
            OpenInvestigations = investigations,
            DataSourcesUsed = dataSources
        };

        await AppendAccessLogAsync(
            db,
            access,
            $"EXAMINATION_BRIEFING:{profile.InstitutionName}",
            BuildSummary("EXAMINATION_BRIEFING", 1),
            "CONFIDENTIAL",
            new List<Guid> { tenantId },
            dataSources,
            new Dictionary<string, object> { ["EntityTenantId"] = tenantId },
            ct);

        return briefing;
    }

    private async Task<EntityIntelligenceProfile> BuildEntityProfileAsync(
        MetadataDbContext db,
        RegulatorExecutionContext access,
        Guid tenantId,
        CancellationToken ct)
    {
        var directory = await LoadEntityDirectoryAsync(db, null, access.RegulatorAgency, ct);
        var entity = directory.FirstOrDefault(x => x.TenantId == tenantId)
            ?? throw new InvalidOperationException($"Institution tenant '{tenantId}' was not found.");

        EnforceRegulatorCoverage(access.RegulatorAgency, entity.RegulatorAgency);

        var snapshots = await LoadSnapshotsAsync(db, new List<Guid> { tenantId }, ResolveModuleCode(access.RegulatorAgency), ct);
        var latestSnapshot = snapshots
            .OrderByDescending(x => x.PeriodSortKey)
            .ThenByDescending(x => x.SubmittedAt)
            .FirstOrDefault();

        var latestChs = await LoadLatestChsAsync(db, tenantId, ct);
        var latestAnomaly = await db.AnomalyReports
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.AnalysedAt)
            .FirstOrDefaultAsync(ct);

        var filingRisk = await LoadPredictionSummaryAsync(db, tenantId, ForeSightModelCodes.FilingRisk, "Filing Risk", ct);
        var capitalForecast = await LoadPredictionSummaryAsync(db, tenantId, ForeSightModelCodes.CapitalBreach, "Capital Forecast", ct);
        var foreSightAlerts = await db.ForeSightAlerts
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && !x.IsDismissed)
            .OrderByDescending(x => x.DispatchedAt)
            .Take(10)
            .ToListAsync(ct);

        var conductRisk = await LoadConductRiskAsync(db, entity.InstitutionId, access.RegulatorAgency, ct);
        var strAdequacy = await LoadStrAdequacyAsync(db, entity.InstitutionId, access.RegulatorAgency, ct);
        var surveillanceAlerts = await LoadSurveillanceAlertsAsync(db, entity.InstitutionId, access.RegulatorAgency, ct);
        var overdueFilingCount = await LoadOverdueFilingCountAsync(db, tenantId, access.RegulatorAgency, ct);
        var latestNarrative = latestAnomaly?.NarrativeSummary;

        var keyMetrics = BuildKeyMetrics(latestSnapshot);
        var activeAlerts = BuildActiveAlerts(foreSightAlerts, surveillanceAlerts, latestAnomaly, overdueFilingCount);
        var discrepancies = BuildDiscrepancies(latestSnapshot, latestChs, latestAnomaly, filingRisk, conductRisk, strAdequacy, overdueFilingCount);
        var dataSources = new List<string>();
        if (latestSnapshot is not null) dataSources.Add("RG-07");
        if (latestChs is not null) dataSources.Add("RG-32");
        if (latestAnomaly is not null) dataSources.Add("AI-01");
        if (filingRisk is not null || capitalForecast is not null || foreSightAlerts.Count > 0) dataSources.Add("AI-04");
        if (conductRisk is not null || strAdequacy is not null || surveillanceAlerts.Count > 0) dataSources.Add("RG-38");
        if (overdueFilingCount > 0) dataSources.Add("RG-12");

        return new EntityIntelligenceProfile
        {
            TenantId = tenantId,
            InstitutionId = entity.InstitutionId,
            InstitutionName = entity.InstitutionName,
            LicenceCategory = entity.LicenceCategory,
            RegulatorAgency = entity.RegulatorAgency,
            InstitutionType = entity.InstitutionType,
            HoldingCompanyName = entity.HoldingCompanyName,
            LatestPeriodCode = latestSnapshot?.PeriodCode,
            LatestSubmissionAt = latestSnapshot?.SubmittedAt,
            KeyMetrics = keyMetrics,
            ComplianceHealth = latestChs,
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
            FinancialCrimeRisk = conductRisk,
            StrAdequacy = strAdequacy,
            CrossModuleDiscrepancies = discrepancies,
            ActiveAlerts = activeAlerts,
            LatestNarrative = latestNarrative,
            DataSourcesUsed = dataSources.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private async Task<SectorIntelligenceSummary> BuildSectorSummaryAsync(
        MetadataDbContext db,
        RegulatorExecutionContext access,
        string? licenceCategory,
        string? periodCode,
        CancellationToken ct)
    {
        var entities = await LoadEntityDirectoryAsync(db, licenceCategory, access.RegulatorAgency, ct);
        if (entities.Count == 0)
        {
            return new SectorIntelligenceSummary
            {
                RegulatorAgency = access.RegulatorAgency,
                LicenceCategory = licenceCategory,
                PeriodCode = periodCode,
                EntityCount = 0
            };
        }

        var tenantIds = entities.Select(x => x.TenantId).Distinct().ToList();
        var snapshots = await LoadSnapshotsAsync(db, tenantIds, ResolveModuleCode(access.RegulatorAgency), ct);
        var latestSnapshots = snapshots
            .Where(x => string.IsNullOrWhiteSpace(periodCode) || x.PeriodCode == periodCode)
            .GroupBy(x => x.TenantId)
            .Select(g => g.OrderByDescending(x => x.PeriodSortKey).ThenByDescending(x => x.SubmittedAt).First())
            .ToList();

        var chsRows = await db.ChsScoreSnapshots
            .AsNoTracking()
            .Where(x => tenantIds.Contains(x.TenantId))
            .OrderByDescending(x => x.ComputedAt)
            .ToListAsync(ct);
        var latestChs = chsRows
            .GroupBy(x => x.TenantId)
            .Select(g => g.First())
            .ToDictionary(x => x.TenantId);

        var anomalyRows = await db.AnomalyReports
            .AsNoTracking()
            .Where(x => tenantIds.Contains(x.TenantId) && x.RegulatorCode == access.RegulatorAgency)
            .OrderByDescending(x => x.AnalysedAt)
            .ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(periodCode))
        {
            anomalyRows = anomalyRows.Where(x => x.PeriodCode == periodCode).ToList();
        }

        var latestAnomaly = anomalyRows
            .GroupBy(x => x.TenantId)
            .Select(g => g.First())
            .ToDictionary(x => x.TenantId);

        var predictionRows = await db.ForeSightPredictions
            .AsNoTracking()
            .Where(x => tenantIds.Contains(x.TenantId)
                        && x.ModelCode == ForeSightModelCodes.RegulatoryAction
                        && !x.IsSuppressed)
            .OrderByDescending(x => x.PredictionDate)
            .ToListAsync(ct);
        var latestPredictions = predictionRows
            .GroupBy(x => x.TenantId)
            .Select(g => g.First())
            .ToDictionary(x => x.TenantId);

        var conductRiskRows = await LoadSectorConductRiskAsync(db, entities.Select(x => x.InstitutionId).ToList(), access.RegulatorAgency, ct);
        var conductRiskByInstitution = conductRiskRows.ToDictionary(x => x.InstitutionId);
        var overdueCount = await LoadSectorOverdueCountAsync(db, tenantIds, access.RegulatorAgency, ct);
        var systemicIndicators = await LoadSystemicIndicatorsAsync(db, access.RegulatorAgency, licenceCategory, ct);

        var carValues = latestSnapshots.Select(x => GetMetricValue(x, "carratio")).Where(x => x.HasValue).Select(x => x!.Value).ToList();
        var nplValues = latestSnapshots.Select(x => GetMetricValue(x, "nplratio")).Where(x => x.HasValue).Select(x => x!.Value).ToList();
        var liquidityValues = latestSnapshots.Select(x => GetMetricValue(x, "liquidityratio")).Where(x => x.HasValue).Select(x => x!.Value).ToList();

        var rankings = new List<RegIqEntityRankingItem>();
        foreach (var entity in entities)
        {
            latestChs.TryGetValue(entity.TenantId, out var chs);
            latestAnomaly.TryGetValue(entity.TenantId, out var anomaly);
            latestPredictions.TryGetValue(entity.TenantId, out var prediction);
            conductRiskByInstitution.TryGetValue(entity.InstitutionId, out var conduct);

            var chsRisk = chs is null ? 50m : 100m - chs.OverallScore;
            var anomalyRisk = anomaly is null ? 20m : 100m - anomaly.OverallQualityScore;
            var filingRisk = prediction is null ? 20m : prediction.PredictedValue <= 1m ? prediction.PredictedValue * 100m : prediction.PredictedValue;
            var conductRisk = conduct?.CompositeScore ?? 20m;
            var score = Math.Round((chsRisk * 0.25m) + (anomalyRisk * 0.25m) + (filingRisk * 0.25m) + (conductRisk * 0.25m), 2);

            rankings.Add(new RegIqEntityRankingItem
            {
                TenantId = entity.TenantId,
                InstitutionName = entity.InstitutionName,
                LicenceCategory = entity.LicenceCategory,
                Score = score,
                ScoreLabel = "Examination Priority",
                RiskBand = score >= 70m ? "HIGH" : score >= 45m ? "MEDIUM" : "LOW"
            });
        }

        var dataSources = new List<string> { "RG-07" };
        if (latestChs.Count > 0) dataSources.Add("RG-32");
        if (latestAnomaly.Count > 0) dataSources.Add("AI-01");
        if (latestPredictions.Count > 0) dataSources.Add("AI-04");
        if (conductRiskRows.Count > 0) dataSources.Add("RG-38");
        if (systemicIndicators.Count > 0) dataSources.Add("RG-36");
        if (overdueCount > 0) dataSources.Add("RG-12");

        return new SectorIntelligenceSummary
        {
            RegulatorAgency = access.RegulatorAgency,
            LicenceCategory = licenceCategory,
            PeriodCode = periodCode ?? latestSnapshots.MaxBy(x => x.PeriodSortKey)?.PeriodCode,
            EntityCount = entities.Count,
            AverageCarRatio = carValues.Count == 0 ? null : Math.Round(carValues.Average(), 2),
            AverageNplRatio = nplValues.Count == 0 ? null : Math.Round(nplValues.Average(), 2),
            AverageLiquidityRatio = liquidityValues.Count == 0 ? null : Math.Round(liquidityValues.Average(), 2),
            AverageComplianceHealthScore = latestChs.Count == 0 ? null : Math.Round(latestChs.Values.Average(x => x.OverallScore), 2),
            AverageAnomalyQualityScore = latestAnomaly.Count == 0 ? null : Math.Round(latestAnomaly.Values.Average(x => x.OverallQualityScore), 2),
            AverageConductRiskScore = conductRiskRows.Count == 0 ? null : Math.Round(conductRiskRows.Average(x => x.CompositeScore), 2),
            OverdueFilingCount = overdueCount,
            HighRiskEntityCount = rankings.Count(x => x.RiskBand == "HIGH"),
            ExaminationPriorityRanking = rankings.OrderByDescending(x => x.Score).Take(25).ToList(),
            SystemicIndicators = systemicIndicators,
            DataSourcesUsed = dataSources.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private async Task<RegulatorExecutionContext> EnsureRegulatorAccessAsync(
        MetadataDbContext db,
        string? fallbackRegulatorId,
        string? fallbackRegulatorAgency,
        CancellationToken ct)
    {
        if (!_tenantContext.CurrentTenantId.HasValue)
        {
            throw new InvalidOperationException("A regulator tenant context is required for RegulatorIQ cross-tenant access.");
        }

        var tenant = await db.Tenants
            .AsNoTracking()
            .Where(x => x.TenantId == _tenantContext.CurrentTenantId.Value)
            .Select(x => new { x.TenantId, x.TenantType, x.TenantSlug, x.TenantName })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("The current tenant context could not be resolved.");

        if (tenant.TenantType != TenantType.Regulator)
        {
            throw new UnauthorizedAccessException("RegulatorIQ cross-tenant access is restricted to regulator tenants.");
        }

        var principal = _httpContextAccessor.HttpContext?.User;
        var regulatorAgency = principal?.FindFirst("RegulatorCode")?.Value
            ?? fallbackRegulatorAgency
            ?? tenant.TenantSlug.ToUpperInvariant();
        var regulatorId = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal?.FindFirst("sub")?.Value
            ?? fallbackRegulatorId
            ?? "unknown-regulator";
        var regulatorRole = principal?.Claims
            .Where(x => x.Type == ClaimTypes.Role || x.Type == "role")
            .Select(x => x.Value)
            .FirstOrDefault()
            ?? "Regulator";

        return new RegulatorExecutionContext(
            tenant.TenantId,
            regulatorId,
            regulatorRole,
            regulatorAgency.Trim().ToUpperInvariant());
    }

    private async Task<List<EntityMatchCandidate>> FindEntityMatchesAsync(
        MetadataDbContext db,
        string searchTerm,
        string? licenceCategory,
        CancellationToken ct)
    {
        var normalized = NormalizeAlias(searchTerm);
        var regulatorAgency = await TryResolveRegulatorAgencyAsync(db, ct);
        var directory = await LoadEntityDirectoryAsync(db, licenceCategory, regulatorAgency, ct);
        if (directory.Count == 0)
        {
            return new List<EntityMatchCandidate>();
        }

        var aliasRows = await db.RegIqEntityAliases
            .Where(x => x.IsActive
                        && (string.IsNullOrWhiteSpace(licenceCategory) || x.LicenceCategory == licenceCategory)
                        && (string.IsNullOrWhiteSpace(regulatorAgency) || x.RegulatorAgency == regulatorAgency)
                        && (x.NormalizedAlias.Contains(normalized) || x.Alias.Contains(searchTerm) || x.CanonicalName.Contains(searchTerm)))
            .OrderBy(x => x.MatchPriority)
            .ToListAsync(ct);

        var updates = new List<RegIqEntityAlias>();
        var candidates = new Dictionary<Guid, EntityMatchCandidate>();

        foreach (var alias in aliasRows)
        {
            var matchedEntities = ResolveAliasCandidates(alias, directory);
            if (!alias.TenantId.HasValue && matchedEntities.Count == 1)
            {
                alias.TenantId = matchedEntities[0].TenantId;
                updates.Add(alias);
            }

            foreach (var entity in matchedEntities)
            {
                var score = ScoreAliasMatch(normalized, alias);
                MergeCandidate(candidates, entity, score);
            }
        }

        foreach (var entity in directory)
        {
            var score = ScoreInstitutionMatch(normalized, entity);
            if (score > 0m)
            {
                MergeCandidate(candidates, entity, score);
            }
        }

        if (updates.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return candidates.Values
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.InstitutionName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void MergeCandidate(
        IDictionary<Guid, EntityMatchCandidate> candidates,
        EntityDirectoryRow entity,
        decimal score)
    {
        if (!candidates.TryGetValue(entity.TenantId, out var existing) || score > existing.Score)
        {
            candidates[entity.TenantId] = new EntityMatchCandidate(
                entity.TenantId,
                entity.InstitutionId,
                entity.InstitutionName,
                entity.LicenceCategory,
                score);
        }
    }

    private static List<EntityDirectoryRow> ResolveAliasCandidates(RegIqEntityAlias alias, IReadOnlyList<EntityDirectoryRow> directory)
    {
        var canonical = NormalizeAlias(alias.CanonicalName);
        var holding = NormalizeAlias(alias.HoldingCompanyName);
        var results = new List<EntityDirectoryRow>();

        foreach (var entity in directory)
        {
            if (alias.TenantId.HasValue && entity.TenantId == alias.TenantId.Value)
            {
                results.Add(entity);
                continue;
            }

            if (entity.NormalizedInstitutionName == canonical
                || entity.NormalizedInstitutionName.Contains(canonical, StringComparison.Ordinal)
                || (!string.IsNullOrWhiteSpace(holding) && entity.NormalizedInstitutionName.Contains(holding, StringComparison.Ordinal)))
            {
                results.Add(entity);
            }
        }

        return results
            .DistinctBy(x => x.TenantId)
            .ToList();
    }

    private static decimal ScoreAliasMatch(string normalizedSearch, RegIqEntityAlias alias)
    {
        if (string.Equals(alias.NormalizedAlias, normalizedSearch, StringComparison.Ordinal))
        {
            return 1.0m;
        }

        if (alias.NormalizedAlias.StartsWith(normalizedSearch, StringComparison.Ordinal)
            || normalizedSearch.StartsWith(alias.NormalizedAlias, StringComparison.Ordinal))
        {
            return 0.95m;
        }

        if (alias.NormalizedAlias.Contains(normalizedSearch, StringComparison.Ordinal)
            || normalizedSearch.Contains(alias.NormalizedAlias, StringComparison.Ordinal))
        {
            return 0.88m;
        }

        return 0.80m;
    }

    private static decimal ScoreInstitutionMatch(string normalizedSearch, EntityDirectoryRow entity)
    {
        if (entity.NormalizedInstitutionName == normalizedSearch)
        {
            return 0.97m;
        }

        if (entity.NormalizedInstitutionName.Contains(normalizedSearch, StringComparison.Ordinal)
            || normalizedSearch.Contains(entity.NormalizedInstitutionName, StringComparison.Ordinal))
        {
            return 0.86m;
        }

        var tokenCoverage = ComputeTokenCoverage(normalizedSearch, entity.NormalizedInstitutionName);
        var similarity = ComputeSimilarity(normalizedSearch, entity.NormalizedInstitutionName);
        var score = Math.Max(tokenCoverage, similarity);
        return score >= 0.72m ? score : 0m;
    }

    private static decimal ComputeTokenCoverage(string normalizedSearch, string normalizedName)
    {
        var searchTokens = SplitTokens(normalizedSearch);
        if (searchTokens.Count == 0)
        {
            return 0m;
        }

        var hits = searchTokens.Count(x => normalizedName.Contains(x, StringComparison.Ordinal));
        return decimal.Round(0.60m + ((decimal)hits / searchTokens.Count * 0.25m), 4);
    }

    private static decimal ComputeSimilarity(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return 0m;
        }

        var distance = LevenshteinDistance(left, right);
        var longest = Math.Max(left.Length, right.Length);
        if (longest == 0)
        {
            return 0m;
        }

        var similarity = 1m - ((decimal)distance / longest);
        return decimal.Round(Math.Max(0m, 0.70m + (similarity * 0.20m)), 4);
    }

    private static int LevenshteinDistance(string left, string right)
    {
        var costs = new int[right.Length + 1];
        for (var j = 0; j < costs.Length; j++)
        {
            costs[j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            costs[0] = i;
            var lastValue = i - 1;

            for (var j = 1; j <= right.Length; j++)
            {
                var current = costs[j];
                var substitution = left[i - 1] == right[j - 1] ? lastValue : lastValue + 1;
                var insertion = costs[j] + 1;
                var deletion = costs[j - 1] + 1;
                costs[j] = Math.Min(substitution, Math.Min(insertion, deletion));
                lastValue = current;
            }
        }

        return costs[right.Length];
    }

    private async Task<List<EntityDirectoryRow>> LoadEntityDirectoryAsync(
        MetadataDbContext db,
        string? licenceCategory,
        string? regulatorAgency,
        CancellationToken ct)
    {
        var licenceRows = await db.TenantLicenceTypes
            .AsNoTracking()
            .Include(x => x.LicenceType)
            .Where(x => x.IsActive && x.LicenceType != null)
            .ToListAsync(ct);

        var licenceMap = licenceRows
            .GroupBy(x => x.TenantId)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(y => y.EffectiveDate).First());

        var institutions = await db.Institutions
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Join(
                db.Tenants.AsNoTracking().Where(x => x.Status == TenantStatus.Active && x.TenantType == TenantType.Institution),
                institution => institution.TenantId,
                tenant => tenant.TenantId,
                (institution, tenant) => new { institution, tenant })
            .ToListAsync(ct);

        return institutions
            .Select(x =>
            {
                licenceMap.TryGetValue(x.institution.TenantId, out var licenceRow);
                var licence = licenceRow?.LicenceType?.Code ?? x.institution.LicenseType ?? "UNKNOWN";
                var regulator = licenceRow?.LicenceType?.Regulator ?? regulatorAgency ?? string.Empty;
                return new EntityDirectoryRow(
                    x.institution.TenantId,
                    x.institution.Id,
                    x.institution.InstitutionName,
                    licence,
                    regulator,
                    ResolveInstitutionType(licence),
                    NormalizeAlias(x.institution.InstitutionName),
                    null);
            })
            .Where(x => string.IsNullOrWhiteSpace(licenceCategory) || string.Equals(x.LicenceCategory, licenceCategory, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(regulatorAgency) || string.Equals(x.RegulatorAgency, regulatorAgency, StringComparison.OrdinalIgnoreCase) || string.Equals(regulatorAgency, "NFIU", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task<string?> TryResolveRegulatorAgencyAsync(MetadataDbContext db, CancellationToken ct)
    {
        var claim = _httpContextAccessor.HttpContext?.User?.FindFirst("RegulatorCode")?.Value;
        if (!string.IsNullOrWhiteSpace(claim))
        {
            return claim.Trim().ToUpperInvariant();
        }

        if (!_tenantContext.CurrentTenantId.HasValue)
        {
            return null;
        }

        var tenant = await db.Tenants
            .AsNoTracking()
            .Where(x => x.TenantId == _tenantContext.CurrentTenantId.Value)
            .Select(x => new { x.TenantType, x.TenantSlug })
            .FirstOrDefaultAsync(ct);

        return tenant?.TenantType == TenantType.Regulator
            ? tenant.TenantSlug.ToUpperInvariant()
            : null;
    }

    private async Task<List<Dictionary<string, object>>> ExecuteTemplateAsync(
        MetadataDbContext db,
        string sqlTemplate,
        IReadOnlyDictionary<string, object> parameters,
        RegulatorExecutionContext access,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sqlTemplate;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = 60;

        var parameterNames = ExtractParameterNames(sqlTemplate);
        foreach (var parameterName in parameterNames)
        {
            var parameter = new SqlParameter($"@{parameterName}", ResolveParameterValue(parameterName, parameters, access));
            command.Parameters.Add(parameter);
        }

        var rows = new List<Dictionary<string, object>>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    private async Task<List<Guid>> ResolveEntitiesAccessedAsync(
        MetadataDbContext db,
        IReadOnlyDictionary<string, object> parameters,
        IReadOnlyList<Dictionary<string, object>> rows,
        string regulatorAgency,
        CancellationToken ct)
    {
        var values = new HashSet<Guid>();

        foreach (var key in new[] { "EntityTenantId", "TenantId", "PrimaryEntityTenantId", "ComparisonEntityTenantId", "EntityTenantIdA", "EntityTenantIdB" })
        {
            if (TryGetGuid(parameters, key, out var parameterGuid))
            {
                values.Add(parameterGuid);
            }
        }

        foreach (var row in rows)
        {
            foreach (var key in new[] { "tenant_id", "TenantId", "primary_entity_tenant_id", "PrimaryEntityTenantId" })
            {
                if (TryGetGuid(row, key, out var rowGuid))
                {
                    values.Add(rowGuid);
                }
            }

            if (TryGetInt(row, "institution_id", out var institutionId))
            {
                var tenantId = await db.Institutions
                    .AsNoTracking()
                    .Where(x => x.Id == institutionId)
                    .Select(x => (Guid?)x.TenantId)
                    .FirstOrDefaultAsync(ct);
                if (tenantId.HasValue)
                {
                    values.Add(tenantId.Value);
                }
            }
        }

        if (values.Count > 0)
        {
            return values.OrderBy(x => x).ToList();
        }

        return await ResolveSectorTenantScopeAsync(
            db,
            regulatorAgency,
            parameters.TryGetValue("LicenceCategory", out var licenceCategory) ? licenceCategory?.ToString() : null,
            ct);
    }

    private async Task<List<Guid>> ResolveSectorTenantScopeAsync(
        MetadataDbContext db,
        string regulatorAgency,
        string? licenceCategory,
        CancellationToken ct)
    {
        var entities = await LoadEntityDirectoryAsync(db, licenceCategory, regulatorAgency, ct);
        return entities.Select(x => x.TenantId).Distinct().OrderBy(x => x).ToList();
    }

    private async Task AppendAccessLogAsync(
        MetadataDbContext db,
        RegulatorExecutionContext access,
        string queryText,
        string responseSummary,
        string classificationLevel,
        IReadOnlyList<Guid> entitiesAccessed,
        IReadOnlyList<string> dataSourcesAccessed,
        IReadOnlyDictionary<string, object> filterContext,
        CancellationToken ct)
    {
        var accessLog = new RegIqAccessLog
        {
            RegulatorTenantId = access.RegulatorTenantId,
            RegulatorId = access.RegulatorId,
            RegulatorAgency = access.RegulatorAgency,
            RegulatorRole = access.RegulatorRole,
            QueryText = queryText,
            ResponseSummary = Truncate(responseSummary, 1000),
            ClassificationLevel = classificationLevel,
            EntitiesAccessedJson = JsonSerializer.Serialize(entitiesAccessed, JsonOptions),
            PrimaryEntityTenantId = entitiesAccessed.FirstOrDefault(),
            DataSourcesAccessedJson = JsonSerializer.Serialize(dataSourcesAccessed, JsonOptions),
            FilterContextJson = JsonSerializer.Serialize(filterContext, JsonOptions),
            IpAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            SessionId = _httpContextAccessor.HttpContext?.TraceIdentifier,
            AccessedAt = DateTime.UtcNow,
            RetainUntil = DateTime.UtcNow.AddYears(7)
        };

        db.RegIqAccessLogs.Add(accessLog);
        await db.SaveChangesAsync(ct);
        await AppendStandardAuditAsync(db, access, accessLog, ct);
    }

    private static async Task AppendStandardAuditAsync(
        MetadataDbContext db,
        RegulatorExecutionContext access,
        RegIqAccessLog accessLog,
        CancellationToken ct)
    {
        var previous = await db.AuditLog
            .Where(x => x.TenantId == access.RegulatorTenantId && x.SequenceNumber > 0)
            .OrderByDescending(x => x.SequenceNumber)
            .Select(x => new { x.SequenceNumber, x.Hash })
            .FirstOrDefaultAsync(ct);

        var sequenceNumber = (previous?.SequenceNumber ?? 0) + 1;
        var previousHash = previous?.Hash ?? "GENESIS";
        var performedAt = DateTime.UtcNow;
        var newValues = JsonSerializer.Serialize(new
        {
            accessLog.RegulatorId,
            accessLog.RegulatorAgency,
            accessLog.RegulatorRole,
            accessLog.QueryText,
            accessLog.ResponseSummary,
            accessLog.ClassificationLevel,
            accessLog.PrimaryEntityTenantId,
            accessLog.AccessedAt
        });

        var hash = AuditLogger.ComputeHash(
            sequenceNumber,
            "RegIqAccessLog",
            performedAt,
            access.RegulatorTenantId,
            access.RegulatorId,
            "RegIqAccessLog",
            accessLog.Id > int.MaxValue ? int.MaxValue : (int)accessLog.Id,
            "REGIQ_ACCESS",
            null,
            newValues,
            previousHash);

        db.AuditLog.Add(new AuditLogEntry
        {
            TenantId = access.RegulatorTenantId,
            EntityType = "RegIqAccessLog",
            EntityId = accessLog.Id > int.MaxValue ? int.MaxValue : (int)accessLog.Id,
            Action = "REGIQ_ACCESS",
            NewValues = newValues,
            PerformedBy = access.RegulatorId,
            PerformedAt = performedAt,
            IpAddress = accessLog.IpAddress,
            Hash = hash,
            PreviousHash = previousHash,
            SequenceNumber = sequenceNumber
        });

        await db.SaveChangesAsync(ct);
    }

    private async Task<List<SubmissionSnapshot>> LoadSnapshotsAsync(
        MetadataDbContext db,
        IReadOnlyList<Guid> tenantIds,
        string? moduleCode,
        CancellationToken ct)
    {
        var query = db.Submissions
            .AsNoTracking()
            .Include(x => x.ReturnPeriod)
            .ThenInclude(x => x!.Module)
            .Include(x => x.Institution)
            .Where(x => tenantIds.Contains(x.TenantId)
                        && AnomalySupport.AcceptedStatuses.Contains(x.Status)
                        && x.ReturnPeriod != null
                        && x.ReturnPeriod.Module != null);

        if (!string.IsNullOrWhiteSpace(moduleCode))
        {
            query = query.Where(x => x.ReturnPeriod!.Module!.ModuleCode == moduleCode);
        }

        var rows = await query.ToListAsync(ct);
        return rows.Select(ToSnapshot).ToList();
    }

    private static SubmissionSnapshot ToSnapshot(Submission submission)
    {
        var period = submission.ReturnPeriod!;
        var module = period.Module!;
        return new SubmissionSnapshot(
            submission.Id,
            submission.TenantId,
            submission.InstitutionId,
            submission.Institution?.InstitutionName ?? $"Institution {submission.InstitutionId}",
            module.ModuleCode,
            module.RegulatorCode,
            RegulatorAnalyticsSupport.FormatPeriodCode(period),
            PeriodSortKey(period),
            submission.SubmittedAt,
            AnomalySupport.ExtractSubmissionMetrics(submission.ParsedDataJson));
    }

    private static int PeriodSortKey(ReturnPeriod period)
    {
        if (period.Quarter is >= 1 and <= 4)
        {
            return (period.Year * 100) + (period.Quarter.Value * 3);
        }

        return (period.Year * 100) + period.Month;
    }

    private static List<RegIqMetricSnapshot> BuildKeyMetrics(SubmissionSnapshot? snapshot)
    {
        var results = new List<RegIqMetricSnapshot>();
        if (snapshot is null)
        {
            return results;
        }

        foreach (var metricCode in new[] { "carratio", "nplratio", "liquidityratio", "loandepositratio", "roa", "totalassets" })
        {
            results.Add(new RegIqMetricSnapshot
            {
                MetricCode = metricCode,
                MetricLabel = snapshot.Metrics.TryGetValue(metricCode, out var metric)
                    ? metric.FieldLabel
                    : MetricLabels.GetValueOrDefault(metricCode, metricCode),
                Value = snapshot.Metrics.TryGetValue(metricCode, out metric) ? metric.Value : null,
                PeriodCode = snapshot.PeriodCode,
                ModuleCode = snapshot.ModuleCode
            });
        }

        return results;
    }

    private static decimal? GetMetricValue(SubmissionSnapshot snapshot, string metricCode)
    {
        return snapshot.Metrics.TryGetValue(metricCode, out var metric)
            ? metric.Value
            : null;
    }

    private static List<RegIqAlertSummary> BuildActiveAlerts(
        IReadOnlyList<ForeSightAlert> foreSightAlerts,
        IReadOnlyList<SurveillanceAlertRowDto> surveillanceAlerts,
        AnomalyReport? anomaly,
        int overdueFilingCount)
    {
        var alerts = new List<RegIqAlertSummary>();
        alerts.AddRange(foreSightAlerts.Select(x => new RegIqAlertSummary
        {
            Source = "AI-04",
            AlertCode = x.AlertType,
            Severity = x.Severity,
            Title = x.Title,
            Detail = x.Body,
            TriggeredAt = x.DispatchedAt
        }));

        alerts.AddRange(surveillanceAlerts.Select(x => new RegIqAlertSummary
        {
            Source = "RG-38",
            AlertCode = x.AlertCode,
            Severity = x.Severity,
            Title = x.Title,
            Detail = x.Detail,
            TriggeredAt = x.DetectedAt
        }));

        if (anomaly is not null && anomaly.AlertCount > 0)
        {
            alerts.Add(new RegIqAlertSummary
            {
                Source = "AI-01",
                AlertCode = "ANOMALY_ALERTS",
                Severity = anomaly.AlertCount >= 3 ? "HIGH" : "MEDIUM",
                Title = $"Latest anomaly scan raised {anomaly.AlertCount} alert(s)",
                Detail = anomaly.NarrativeSummary,
                TriggeredAt = anomaly.AnalysedAt
            });
        }

        if (overdueFilingCount > 0)
        {
            alerts.Add(new RegIqAlertSummary
            {
                Source = "RG-12",
                AlertCode = "OVERDUE_FILINGS",
                Severity = overdueFilingCount >= 2 ? "HIGH" : "MEDIUM",
                Title = $"{overdueFilingCount} overdue filing(s)",
                Detail = "The institution has return periods past due without an accepted submission.",
                TriggeredAt = DateTime.UtcNow
            });
        }

        return alerts
            .OrderByDescending(x => SeverityRank(x.Severity))
            .ThenByDescending(x => x.TriggeredAt)
            .Take(12)
            .ToList();
    }

    private static List<RegIqDiscrepancySummary> BuildDiscrepancies(
        SubmissionSnapshot? snapshot,
        ComplianceHealthScore? chs,
        AnomalyReport? anomaly,
        RegIqPredictionSummary? filingRisk,
        RegIqConductRiskSummary? conductRisk,
        RegIqStrAdequacySummary? strAdequacy,
        int overdueFilingCount)
    {
        var discrepancies = new List<RegIqDiscrepancySummary>();

        var car = snapshot is null ? null : GetMetricValue(snapshot, "carratio");
        if (car.HasValue && car.Value < 15m && filingRisk is not null && filingRisk.RiskBand is "HIGH" or "CRITICAL")
        {
            discrepancies.Add(new RegIqDiscrepancySummary
            {
                DiscrepancyCode = "CAPITAL_FORECAST_CONVERGENCE",
                Title = "Capital stress confirmed across current and predictive data",
                Severity = "HIGH",
                Description = $"Current CAR is {car.Value:F2}% and the latest forecast remains in {filingRisk.RiskBand} risk band.",
                Source = "RG-07+AI-04"
            });
        }

        if (chs is not null && anomaly is not null && chs.DataQuality >= 80m && anomaly.OverallQualityScore < 65m)
        {
            discrepancies.Add(new RegIqDiscrepancySummary
            {
                DiscrepancyCode = "CHS_ANOMALY_DIVERGENCE",
                Title = "CHS and anomaly data quality signals diverge",
                Severity = "MEDIUM",
                Description = $"CHS data-quality pillar is {chs.DataQuality:F1}, while anomaly quality score is {anomaly.OverallQualityScore:F1}.",
                Source = "RG-32+AI-01"
            });
        }

        if (conductRisk is not null && conductRisk.CompositeScore >= 70m && strAdequacy is not null && (strAdequacy.StrDeviation ?? 0m) <= -1m)
        {
            discrepancies.Add(new RegIqDiscrepancySummary
            {
                DiscrepancyCode = "AML_STR_ADEQUACY",
                Title = "Elevated conduct risk and weak STR adequacy",
                Severity = "HIGH",
                Description = $"Conduct risk score is {conductRisk.CompositeScore:F1} with STR deviation at {strAdequacy.StrDeviation:F2}.",
                Source = "RG-38"
            });
        }

        if (overdueFilingCount > 0 && filingRisk is not null && filingRisk.RiskBand is "LOW" or "NONE")
        {
            discrepancies.Add(new RegIqDiscrepancySummary
            {
                DiscrepancyCode = "FILING_PREDICTION_MISS",
                Title = "Observed filing delinquency exceeds forecast signal",
                Severity = "MEDIUM",
                Description = $"There are {overdueFilingCount} overdue filing(s), but the latest filing-risk model is {filingRisk.RiskBand}.",
                Source = "RG-12+AI-04"
            });
        }

        return discrepancies;
    }

    private static List<RegIqFocusArea> BuildFocusAreas(
        EntityIntelligenceProfile profile,
        IReadOnlyList<RegIqInvestigationSummary> investigations,
        SectorIntelligenceSummary peerContext)
    {
        var focusAreas = new List<RegIqFocusArea>();

        var car = profile.KeyMetrics.FirstOrDefault(x => x.MetricCode == "carratio")?.Value;
        var npl = profile.KeyMetrics.FirstOrDefault(x => x.MetricCode == "nplratio")?.Value;
        var liquidity = profile.KeyMetrics.FirstOrDefault(x => x.MetricCode == "liquidityratio")?.Value;

        if (car.HasValue && car.Value < 15m)
        {
            focusAreas.Add(new RegIqFocusArea
            {
                Area = "Capital adequacy",
                Reason = $"Current CAR is {car.Value:F2}% against the 15% CBN reference threshold.",
                Priority = "HIGH"
            });
        }

        if (npl.HasValue && npl.Value > 5m)
        {
            focusAreas.Add(new RegIqFocusArea
            {
                Area = "Asset quality",
                Reason = $"NPL ratio is {npl.Value:F2}% and above the 5% supervisory warning threshold.",
                Priority = "HIGH"
            });
        }

        if (liquidity.HasValue && liquidity.Value < 30m)
        {
            focusAreas.Add(new RegIqFocusArea
            {
                Area = "Liquidity management",
                Reason = $"Liquidity ratio is {liquidity.Value:F2}% and below the 30% prudential floor.",
                Priority = "HIGH"
            });
        }

        if (profile.FinancialCrimeRisk is not null && profile.FinancialCrimeRisk.CompositeScore >= 65m)
        {
            focusAreas.Add(new RegIqFocusArea
            {
                Area = "AML/CFT controls",
                Reason = $"Conduct risk composite score is {profile.FinancialCrimeRisk.CompositeScore:F1} with {profile.FinancialCrimeRisk.ActiveAlertCount} active alert(s).",
                Priority = "HIGH"
            });
        }

        if (profile.Anomaly is not null && profile.Anomaly.TotalFindings >= 5)
        {
            focusAreas.Add(new RegIqFocusArea
            {
                Area = "Regulatory reporting quality",
                Reason = $"The latest anomaly scan recorded {profile.Anomaly.TotalFindings} findings and a quality score of {profile.Anomaly.QualityScore:F1}.",
                Priority = "MEDIUM"
            });
        }

        if (investigations.Count > 0)
        {
            focusAreas.Add(new RegIqFocusArea
            {
                Area = "Open investigations",
                Reason = $"{investigations.Count} open case(s) or unresolved surveillance investigations are linked to the institution.",
                Priority = "HIGH"
            });
        }

        if (peerContext.ExaminationPriorityRanking.Any()
            && peerContext.ExaminationPriorityRanking.OrderByDescending(x => x.Score).Take(5).Any(x => x.TenantId == profile.TenantId))
        {
            focusAreas.Add(new RegIqFocusArea
            {
                Area = "Peer outlier review",
                Reason = "The institution appears in the sector's top examination-priority cohort.",
                Priority = "MEDIUM"
            });
        }

        return focusAreas
            .OrderByDescending(x => SeverityRank(x.Priority))
            .ThenBy(x => x.Area, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<ComplianceHealthScore?> LoadLatestChsAsync(MetadataDbContext db, Guid tenantId, CancellationToken ct)
    {
        var snapshot = await db.ChsScoreSnapshots
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.ComputedAt)
            .FirstOrDefaultAsync(ct);
        if (snapshot is null)
        {
            return null;
        }

        var tenantName = await db.Tenants
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => x.TenantName)
            .FirstOrDefaultAsync(ct)
            ?? "Unknown";

        var licenceCategory = await db.TenantLicenceTypes
            .AsNoTracking()
            .Include(x => x.LicenceType)
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderByDescending(x => x.EffectiveDate)
            .Select(x => x.LicenceType!.Code)
            .FirstOrDefaultAsync(ct)
            ?? "UNKNOWN";

        return new ComplianceHealthScore
        {
            TenantId = tenantId,
            TenantName = tenantName,
            LicenceType = licenceCategory,
            OverallScore = snapshot.OverallScore,
            Rating = ComplianceHealthService.ToRating(snapshot.OverallScore),
            Trend = ChsTrend.Stable,
            FilingTimeliness = snapshot.FilingTimeliness,
            DataQuality = snapshot.DataQuality,
            RegulatoryCapital = snapshot.RegulatoryCapital,
            AuditGovernance = snapshot.AuditGovernance,
            Engagement = snapshot.Engagement,
            ComputedAt = snapshot.ComputedAt,
            PeriodLabel = snapshot.PeriodLabel
        };
    }

    private async Task<RegIqPredictionSummary?> LoadPredictionSummaryAsync(
        MetadataDbContext db,
        Guid tenantId,
        string modelCode,
        string label,
        CancellationToken ct)
    {
        var prediction = await db.ForeSightPredictions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ModelCode == modelCode && !x.IsSuppressed)
            .OrderByDescending(x => x.PredictionDate)
            .ThenByDescending(x => x.HorizonDate)
            .FirstOrDefaultAsync(ct);
        if (prediction is null)
        {
            return null;
        }

        return new RegIqPredictionSummary
        {
            ModelCode = modelCode,
            Label = label,
            PredictedValue = prediction.PredictedValue,
            ConfidenceScore = prediction.ConfidenceScore,
            RiskBand = prediction.RiskBand,
            PeriodCode = prediction.TargetPeriodCode,
            Explanation = prediction.Explanation,
            Recommendation = prediction.Recommendation
        };
    }

    private async Task<RegIqConductRiskSummary?> LoadConductRiskAsync(
        MetadataDbContext db,
        int institutionId,
        string regulatorAgency,
        CancellationToken ct)
    {
        var rows = await QueryAsync(
            db,
            """
            SELECT TOP (1)
                   CompositeScore,
                   RiskBand,
                   ActiveAlertCount,
                   PeriodCode,
                   ComputedAt
            FROM dbo.ConductRiskScores
            WHERE InstitutionId = @InstitutionId
              AND RegulatorCode = @RegulatorCode
            ORDER BY ComputedAt DESC;
            """,
            new Dictionary<string, object>
            {
                ["InstitutionId"] = institutionId,
                ["RegulatorCode"] = regulatorAgency
            },
            ct);

        if (rows.Count == 0)
        {
            return null;
        }

        var row = rows[0];
        return new RegIqConductRiskSummary
        {
            CompositeScore = ConvertToDecimal(row, "CompositeScore"),
            RiskBand = row.GetValueOrDefault("RiskBand")?.ToString() ?? "LOW",
            ActiveAlertCount = ConvertToInt(row, "ActiveAlertCount"),
            PeriodCode = row.GetValueOrDefault("PeriodCode")?.ToString(),
            ComputedAt = TryConvertDateTime(row.GetValueOrDefault("ComputedAt"))
        };
    }

    private async Task<RegIqStrAdequacySummary?> LoadStrAdequacyAsync(
        MetadataDbContext db,
        int institutionId,
        string regulatorAgency,
        CancellationToken ct)
    {
        var rows = await QueryAsync(
            db,
            """
            SELECT TOP (1)
                   PeriodCode,
                   STRFilingCount,
                   PeerAvgSTRCount,
                   STRDeviation,
                   StructuringAlertCount,
                   TFSFalsePositiveRate
            FROM dbo.AMLConductMetrics
            WHERE InstitutionId = @InstitutionId
              AND RegulatorCode = @RegulatorCode
            ORDER BY AsOfDate DESC, CreatedAt DESC;
            """,
            new Dictionary<string, object>
            {
                ["InstitutionId"] = institutionId,
                ["RegulatorCode"] = regulatorAgency
            },
            ct);

        if (rows.Count == 0)
        {
            return null;
        }

        var row = rows[0];
        return new RegIqStrAdequacySummary
        {
            PeriodCode = row.GetValueOrDefault("PeriodCode")?.ToString(),
            StrFilingCount = ConvertToInt(row, "STRFilingCount"),
            PeerAverageStrCount = TryConvertDecimal(row.GetValueOrDefault("PeerAvgSTRCount")),
            StrDeviation = TryConvertDecimal(row.GetValueOrDefault("STRDeviation")),
            StructuringAlertCount = ConvertToInt(row, "StructuringAlertCount"),
            TfsFalsePositiveRate = TryConvertDecimal(row.GetValueOrDefault("TFSFalsePositiveRate"))
        };
    }

    private async Task<List<SurveillanceAlertRowDto>> LoadSurveillanceAlertsAsync(
        MetadataDbContext db,
        int institutionId,
        string regulatorAgency,
        CancellationToken ct)
    {
        var rows = await QueryAsync(
            db,
            """
            SELECT TOP (10)
                   AlertCode,
                   Severity,
                   Title,
                   Detail,
                   DetectedAt
            FROM dbo.SurveillanceAlerts
            WHERE InstitutionId = @InstitutionId
              AND RegulatorCode = @RegulatorCode
            ORDER BY DetectedAt DESC;
            """,
            new Dictionary<string, object>
            {
                ["InstitutionId"] = institutionId,
                ["RegulatorCode"] = regulatorAgency
            },
            ct);

        return rows.Select(x => new SurveillanceAlertRowDto(
            x.GetValueOrDefault("AlertCode")?.ToString() ?? string.Empty,
            x.GetValueOrDefault("Severity")?.ToString() ?? "INFO",
            x.GetValueOrDefault("Title")?.ToString() ?? string.Empty,
            x.GetValueOrDefault("Detail")?.ToString(),
            TryConvertDateTime(x.GetValueOrDefault("DetectedAt"))))
            .ToList();
    }

    private async Task<int> LoadOverdueFilingCountAsync(
        MetadataDbContext db,
        Guid tenantId,
        string regulatorAgency,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow.Date;
        var periods = await db.ReturnPeriods
            .AsNoTracking()
            .Include(x => x.Module)
            .Where(x => x.TenantId == tenantId
                        && x.Module != null
                        && x.Module.RegulatorCode == regulatorAgency
                        && x.EffectiveDeadline < now)
            .ToListAsync(ct);

        if (periods.Count == 0)
        {
            return 0;
        }

        var periodIds = periods.Select(x => x.Id).ToList();
        var acceptedSubmissions = await db.Submissions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId
                        && periodIds.Contains(x.ReturnPeriodId)
                        && AnomalySupport.AcceptedStatuses.Contains(x.Status))
            .Select(x => x.ReturnPeriodId)
            .Distinct()
            .ToListAsync(ct);

        return periods.Count(x => !acceptedSubmissions.Contains(x.Id));
    }

    private async Task<int> LoadSectorOverdueCountAsync(
        MetadataDbContext db,
        IReadOnlyList<Guid> tenantIds,
        string regulatorAgency,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow.Date;
        var periods = await db.ReturnPeriods
            .AsNoTracking()
            .Include(x => x.Module)
            .Where(x => tenantIds.Contains(x.TenantId)
                        && x.Module != null
                        && x.Module.RegulatorCode == regulatorAgency
                        && x.EffectiveDeadline < now)
            .ToListAsync(ct);

        if (periods.Count == 0)
        {
            return 0;
        }

        var accepted = await db.Submissions
            .AsNoTracking()
            .Where(x => tenantIds.Contains(x.TenantId)
                        && periods.Select(y => y.Id).Contains(x.ReturnPeriodId)
                        && AnomalySupport.AcceptedStatuses.Contains(x.Status))
            .Select(x => x.ReturnPeriodId)
            .Distinct()
            .ToListAsync(ct);

        return periods.Count(x => !accepted.Contains(x.Id));
    }

    private async Task<List<ConductRiskRowDto>> LoadSectorConductRiskAsync(
        MetadataDbContext db,
        IReadOnlyList<int> institutionIds,
        string regulatorAgency,
        CancellationToken ct)
    {
        if (institutionIds.Count == 0)
        {
            return new List<ConductRiskRowDto>();
        }

        var idCsv = string.Join(",", institutionIds);
        var sql =
            $"""
            WITH ranked AS (
                SELECT InstitutionId,
                       CompositeScore,
                       RiskBand,
                       ActiveAlertCount,
                       PeriodCode,
                       ComputedAt,
                       ROW_NUMBER() OVER (PARTITION BY InstitutionId ORDER BY ComputedAt DESC) AS rn
                FROM dbo.ConductRiskScores
                WHERE RegulatorCode = @RegulatorCode
                  AND InstitutionId IN ({idCsv})
            )
            SELECT InstitutionId,
                   CompositeScore,
                   RiskBand,
                   ActiveAlertCount,
                   PeriodCode,
                   ComputedAt
            FROM ranked
            WHERE rn = 1;
            """;

        var rows = await QueryAsync(
            db,
            sql,
            new Dictionary<string, object> { ["RegulatorCode"] = regulatorAgency },
            ct);

        return rows.Select(x => new ConductRiskRowDto(
            ConvertToInt(x, "InstitutionId"),
            ConvertToDecimal(x, "CompositeScore"),
            x.GetValueOrDefault("RiskBand")?.ToString() ?? "LOW",
            ConvertToInt(x, "ActiveAlertCount"),
            x.GetValueOrDefault("PeriodCode")?.ToString(),
            TryConvertDateTime(x.GetValueOrDefault("ComputedAt"))))
            .ToList();
    }

    private async Task<List<RegIqSystemicIndicator>> LoadSystemicIndicatorsAsync(
        MetadataDbContext db,
        string regulatorAgency,
        string? licenceCategory,
        CancellationToken ct)
    {
        var institutionType = ResolveSystemicInstitutionType(licenceCategory);
        var rows = await QueryAsync(
            db,
            """
            SELECT TOP (5)
                   PeriodCode,
                   SectorAvgCAR,
                   SectorAvgNPL,
                   SectorAvgLCR,
                   HighRiskEntityCount,
                   SystemicRiskScore,
                   SystemicRiskBand
            FROM meta.systemic_risk_indicators
            WHERE RegulatorCode = @RegulatorCode
              AND (@InstitutionType IS NULL OR InstitutionType = @InstitutionType)
            ORDER BY AsOfDate DESC, ComputedAt DESC;
            """,
            new Dictionary<string, object>
            {
                ["RegulatorCode"] = regulatorAgency,
                ["InstitutionType"] = institutionType ?? (object)DBNull.Value
            },
            ct);

        var indicators = new List<RegIqSystemicIndicator>();
        if (rows.Count == 0)
        {
            return indicators;
        }

        var latest = rows[0];
        indicators.Add(new RegIqSystemicIndicator
        {
            IndicatorCode = "SYSTEMIC_RISK_SCORE",
            Title = $"Systemic risk score for {latest["PeriodCode"]}",
            CurrentValue = TryConvertDecimal(latest.GetValueOrDefault("SystemicRiskScore")),
            Threshold = 60m,
            Severity = latest.GetValueOrDefault("SystemicRiskBand")?.ToString() ?? "LOW",
            AffectedEntities = ConvertToInt(latest, "HighRiskEntityCount"),
            Description = "Latest sector-wide systemic risk score and high-risk population."
        });

        indicators.Add(new RegIqSystemicIndicator
        {
            IndicatorCode = "SECTOR_AVG_CAR",
            Title = "Sector average CAR",
            CurrentValue = TryConvertDecimal(latest.GetValueOrDefault("SectorAvgCAR")),
            Threshold = 15m,
            Severity = "INFO",
            AffectedEntities = ConvertToInt(latest, "HighRiskEntityCount"),
            Description = "Average capital adequacy ratio for the current systemic window."
        });

        indicators.Add(new RegIqSystemicIndicator
        {
            IndicatorCode = "SECTOR_AVG_NPL",
            Title = "Sector average NPL ratio",
            CurrentValue = TryConvertDecimal(latest.GetValueOrDefault("SectorAvgNPL")),
            Threshold = 5m,
            Severity = "INFO",
            AffectedEntities = ConvertToInt(latest, "HighRiskEntityCount"),
            Description = "Average non-performing loan ratio for the current systemic window."
        });

        return indicators;
    }

    private async Task<List<RegIqInvestigationSummary>> LoadInvestigationsAsync(
        MetadataDbContext db,
        int institutionId,
        string regulatorAgency,
        CancellationToken ct)
    {
        var cases = await QueryAsync(
            db,
            """
            SELECT TOP (10)
                   CaseReference,
                   Status,
                   Summary,
                   PriorityScore,
                   ReceivedAt
            FROM dbo.WhistleblowerReports
            WHERE RegulatorCode = @RegulatorCode
              AND AllegedInstitutionId = @InstitutionId
              AND Status IN ('RECEIVED','UNDER_REVIEW','REFERRED')
            ORDER BY PriorityScore DESC, ReceivedAt DESC;
            """,
            new Dictionary<string, object>
            {
                ["RegulatorCode"] = regulatorAgency,
                ["InstitutionId"] = institutionId
            },
            ct);

        var alerts = await LoadSurveillanceAlertsAsync(db, institutionId, regulatorAgency, ct);

        var investigations = cases.Select(x => new RegIqInvestigationSummary
        {
            InvestigationType = "Whistleblower",
            Reference = x.GetValueOrDefault("CaseReference")?.ToString() ?? string.Empty,
            Status = x.GetValueOrDefault("Status")?.ToString() ?? string.Empty,
            Summary = x.GetValueOrDefault("Summary")?.ToString() ?? string.Empty,
            PriorityScore = ConvertToInt(x, "PriorityScore"),
            OpenedAt = TryConvertDateTime(x.GetValueOrDefault("ReceivedAt"))
        }).ToList();

        investigations.AddRange(alerts.Select(x => new RegIqInvestigationSummary
        {
            InvestigationType = "SurveillanceAlert",
            Reference = x.AlertCode,
            Status = "OPEN",
            Summary = x.Title,
            PriorityScore = SeverityRank(x.Severity) * 10,
            OpenedAt = x.DetectedAt
        }));

        return investigations
            .OrderByDescending(x => x.PriorityScore)
            .ThenByDescending(x => x.OpenedAt)
            .Take(15)
            .ToList();
    }

    private async Task<List<RegIqMetricTrendSeries>> LoadMetricTrendsAsync(
        MetadataDbContext db,
        Guid tenantId,
        string regulatorAgency,
        CancellationToken ct)
    {
        var moduleCode = ResolveModuleCode(regulatorAgency);
        var snapshots = await LoadSnapshotsAsync(db, new List<Guid> { tenantId }, moduleCode, ct);
        var ordered = snapshots
            .OrderBy(x => x.PeriodSortKey)
            .ThenBy(x => x.SubmittedAt)
            .ToList();

        var series = new List<RegIqMetricTrendSeries>();
        foreach (var metricCode in new[] { "carratio", "nplratio", "liquidityratio" })
        {
            series.Add(new RegIqMetricTrendSeries
            {
                MetricCode = metricCode,
                MetricLabel = MetricLabels.GetValueOrDefault(metricCode, metricCode),
                Points = ordered
                    .Where(x => x.Metrics.ContainsKey(metricCode))
                    .TakeLast(6)
                    .Select(x => new RegIqMetricTrendPoint
                    {
                        PeriodCode = x.PeriodCode,
                        Value = x.Metrics[metricCode].Value,
                        SubmittedAt = x.SubmittedAt
                    })
                    .ToList()
            });
        }

        return series;
    }

    private async Task<List<Dictionary<string, object>>> QueryAsync(
        MetadataDbContext db,
        string sql,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = 60;

        foreach (var pair in parameters)
        {
            command.Parameters.Add(new SqlParameter($"@{pair.Key}", pair.Value ?? DBNull.Value));
        }

        var rows = new List<Dictionary<string, object>>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static IReadOnlyList<string> ExtractParameterNames(string sqlTemplate)
    {
        return Regex.Matches(sqlTemplate, @"(?<!@)@([A-Za-z][A-Za-z0-9_]*)", RegexOptions.CultureInvariant)
            .Select(x => x.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static object ResolveParameterValue(
        string parameterName,
        IReadOnlyDictionary<string, object> parameters,
        RegulatorExecutionContext access)
    {
        if (TryGetParameter(parameters, parameterName, out var value))
        {
            return value ?? DBNull.Value;
        }

        return parameterName switch
        {
            "RegulatorId" => access.RegulatorId,
            "RegulatorAgency" => access.RegulatorAgency,
            "RegulatorRole" => access.RegulatorRole,
            "Limit" => 25,
            _ => DBNull.Value
        };
    }

    private static bool TryGetParameter(IReadOnlyDictionary<string, object> parameters, string parameterName, out object? value)
    {
        foreach (var pair in parameters)
        {
            if (string.Equals(NormalizeParameterName(pair.Key), NormalizeParameterName(parameterName), StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static string NormalizeParameterName(string parameterName)
    {
        return parameterName.Trim().TrimStart('@');
    }

    private static void ValidateReadOnlySql(string sqlTemplate)
    {
        var trimmed = sqlTemplate.TrimStart();
        if (!(trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("RegulatorIQ query templates must begin with SELECT or WITH.");
        }

        foreach (var token in ReadOnlyForbiddenTokens)
        {
            if (Regex.IsMatch(sqlTemplate, $@"\b{token}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                throw new InvalidOperationException($"RegulatorIQ query template contains forbidden token '{token}'.");
            }
        }
    }

    private static string ResolveModuleCode(string regulatorAgency)
    {
        return DefaultModuleByRegulator.TryGetValue(regulatorAgency, out var moduleCode)
            ? moduleCode
            : string.Empty;
    }

    private static void EnforceRegulatorCoverage(string regulatorAgency, string entityRegulatorAgency)
    {
        if (string.Equals(regulatorAgency, "NFIU", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(regulatorAgency, entityRegulatorAgency, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Regulator {regulatorAgency} is not permitted to access entities supervised by {entityRegulatorAgency}.");
        }
    }

    private static string ResolveInstitutionType(string licenceCategory)
    {
        return licenceCategory.ToUpperInvariant() switch
        {
            "COMMERCIAL_BANK" or "MERCHANT_BANK" => "DMB",
            "MICROFINANCE_BANK_NATIONAL" or "MICROFINANCE_BANK_STATE" or "MICROFINANCE_BANK_UNIT" => "MFB",
            "GENERAL_INSURANCE" or "LIFE_INSURANCE" or "COMPOSITE_INSURANCE" => "INSURER",
            "BROKER_DEALER" or "ISSUING_HOUSE" or "FUND_MANAGER" => "CMO",
            "BDC" => "BDC",
            _ => licenceCategory
        };
    }

    private static string? ResolveSystemicInstitutionType(string? licenceCategory)
    {
        if (string.IsNullOrWhiteSpace(licenceCategory))
        {
            return null;
        }

        return ResolveInstitutionType(licenceCategory);
    }

    private static string NormalizeAlias(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static List<string> SplitTokens(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new List<string>();
        }

        var tokens = Regex.Matches(normalized, @"[a-z0-9]{2,}")
            .Select(x => x.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (tokens.Count == 0)
        {
            tokens.Add(normalized);
        }

        return tokens;
    }

    private static List<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static string BuildSummary(string action, int rowCount)
    {
        return $"{action} returned {rowCount} row(s).";
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static int SeverityRank(string? severity)
    {
        return (severity ?? string.Empty).ToUpperInvariant() switch
        {
            "CRITICAL" => 4,
            "HIGH" => 3,
            "WARNING" => 2,
            "MEDIUM" => 2,
            "INFO" => 1,
            "LOW" => 1,
            _ => 0
        };
    }

    private static bool TryGetGuid(IReadOnlyDictionary<string, object> values, string key, out Guid guid)
    {
        if (values.TryGetValue(key, out var raw))
        {
            return TryConvertGuid(raw, out guid);
        }

        guid = Guid.Empty;
        return false;
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, object> values, string key, out int number)
    {
        if (values.TryGetValue(key, out var raw))
        {
            if (raw is int direct)
            {
                number = direct;
                return true;
            }

            if (raw is long longValue && longValue <= int.MaxValue && longValue >= int.MinValue)
            {
                number = (int)longValue;
                return true;
            }

            if (int.TryParse(raw?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                return true;
            }
        }

        number = 0;
        return false;
    }

    private static bool TryConvertGuid(object? raw, out Guid guid)
    {
        switch (raw)
        {
            case Guid direct:
                guid = direct;
                return true;

            case string text when Guid.TryParse(text, out guid):
                return true;

            default:
                guid = Guid.Empty;
                return false;
        }
    }

    private static decimal ConvertToDecimal(IReadOnlyDictionary<string, object> row, string key)
    {
        return TryConvertDecimal(row.GetValueOrDefault(key)) ?? 0m;
    }

    private static decimal? TryConvertDecimal(object? raw)
    {
        return raw switch
        {
            DBNull => null,
            decimal decimalValue => decimalValue,
            double doubleValue => (decimal)doubleValue,
            float floatValue => (decimal)floatValue,
            int intValue => intValue,
            long longValue => longValue,
            string text when decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static int ConvertToInt(IReadOnlyDictionary<string, object> row, string key)
    {
        return row.GetValueOrDefault(key) switch
        {
            DBNull => 0,
            int intValue => intValue,
            long longValue when longValue <= int.MaxValue && longValue >= int.MinValue => (int)longValue,
            short shortValue => shortValue,
            byte byteValue => byteValue,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0
        };
    }

    private static DateTime? TryConvertDateTime(object? raw)
    {
        return raw switch
        {
            DBNull => null,
            DateTime dateTime => dateTime,
            DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime,
            string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) => parsed,
            _ => null
        };
    }

    private sealed record RegulatorExecutionContext(
        Guid RegulatorTenantId,
        string RegulatorId,
        string RegulatorRole,
        string RegulatorAgency);

    private sealed record EntityDirectoryRow(
        Guid TenantId,
        int InstitutionId,
        string InstitutionName,
        string LicenceCategory,
        string RegulatorAgency,
        string InstitutionType,
        string NormalizedInstitutionName,
        string? HoldingCompanyName);

    private sealed record EntityMatchCandidate(
        Guid TenantId,
        int InstitutionId,
        string InstitutionName,
        string LicenceCategory,
        decimal Score);

    private sealed record SubmissionSnapshot(
        int SubmissionId,
        Guid TenantId,
        int InstitutionId,
        string InstitutionName,
        string ModuleCode,
        string RegulatorCode,
        string PeriodCode,
        int PeriodSortKey,
        DateTime SubmittedAt,
        Dictionary<string, AnomalySupport.MetricPoint> Metrics);

    private sealed record ConductRiskRowDto(
        int InstitutionId,
        decimal CompositeScore,
        string RiskBand,
        int ActiveAlertCount,
        string? PeriodCode,
        DateTime? ComputedAt);

    private sealed record SurveillanceAlertRowDto(
        string AlertCode,
        string Severity,
        string Title,
        string? Detail,
        DateTime? DetectedAt);
}
