using System.Globalization;
using System.Text;
using System.Text.Json;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Domain.Validation;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Admin.Services;

public sealed class PlatformIntelligenceService
{
    private static readonly IReadOnlyList<CapitalActionTemplate> CapitalActionTemplates =
    [
        new("COLLATERAL", "Collateral optimisation", "Tighten eligible collateral recognition and credit risk mitigation on existing exposures.", "RWA", 0m, 4.5m, 0m, 0.9m),
        new("REBALANCE", "Portfolio rebalance", "Tilt new origination toward lower-density exposures and trim high-weight asset growth.", "RWA", 0m, 6.5m, -0.3m, 1.2m),
        new("OFFBAL", "Off-balance sheet management", "Restructure contingent exposures and clean up commitments that inflate effective RWA density.", "RWA", 0m, 3.8m, 0m, 0.7m),
        new("ISSUANCE", "AT1 / Tier 2 issuance", "Inject regulatory capital rapidly to arrest buffer erosion in the next quarter.", "Capital", 10m, 0m, 0m, 14.5m),
        new("DIVIDEND", "Dividend restraint", "Retain a greater share of quarterly earnings to rebuild buffers organically.", "Earnings", 0m, 0m, 1.25m, 0.3m)
    ];

    private static readonly IReadOnlyList<SanctionsWatchlistSource> WatchlistSources =
    [
        new("UN", "UN Security Council Consolidated List", "Daily", "active"),
        new("OFAC", "OFAC SDN List", "Daily", "active"),
        new("EU", "EU Consolidated List", "Daily", "active"),
        new("UK", "UK HMT Sanctions List", "Daily", "active"),
        new("NFIU", "Nigeria NFIU / EFCC Domestic List", "Daily", "active"),
        new("INTERPOL", "Interpol Red Notices", "Daily", "active"),
        new("PEP", "Politically Exposed Persons", "Daily", "active")
    ];

    private static readonly IReadOnlyList<SanctionsWatchlistEntry> WatchlistEntries =
    [
        new("UN", "AL-QAIDA", ["AL QAIDA", "QAEDA", "ALQAIDA"], "entity", "critical"),
        new("UN", "ISLAMIC STATE", ["ISIS", "ISIL", "DAESH"], "entity", "critical"),
        new("OFAC", "BOKO HARAM", ["JAMA'ATU AHLIS SUNNA LIDDA'AWATI WAL-JIHAD"], "entity", "critical"),
        new("OFAC", "HEZBOLLAH", ["HIZBALLAH", "HIZBULLAH"], "entity", "critical"),
        new("EU", "HAMAS", ["HARAKAT AL-MUQAWAMA AL-ISLAMIYYA"], "entity", "critical"),
        new("UK", "WAGNER GROUP", ["PMC WAGNER", "WAGNER"], "entity", "high"),
        new("NFIU", "TERROR FINANCE WATCH", ["TFS HIGH RISK COUNTERPARTY"], "entity", "high"),
        new("INTERPOL", "JOSE RODRIGUEZ", ["JOSE MANUEL RODRIGUEZ"], "person", "high"),
        new("INTERPOL", "MARIA IVANOVA", ["MARIA PETROVNA IVANOVA"], "person", "high"),
        new("PEP", "MINISTER OF FINANCE", ["HONOURABLE MINISTER OF FINANCE"], "person", "medium"),
        new("PEP", "CENTRAL BANK GOVERNOR", ["GOVERNOR OF THE CENTRAL BANK"], "person", "medium"),
        new("UN", "ANSARU", ["JAMA'ATU ANSARUL MUSLIMINA FI BILADIS SUDAN"], "entity", "critical"),
        new("OFAC", "AL-SHABAAB", ["AL SHABAAB", "HARAKAT AL SHABAAB AL MUJAHIDEEN"], "entity", "critical"),
        new("EU", "LASHKAR-E-TAIBA", ["LASHKAR E TAIBA", "LET"], "entity", "critical"),
        new("UK", "ISLAMIC JIHAD", ["PALESTINIAN ISLAMIC JIHAD"], "entity", "high"),
        new("NFIU", "DOMESTIC AML WATCH SUBJECT", ["NIGERIA AML WATCH SUBJECT"], "person", "high")
    ];

    private static readonly IReadOnlyList<ModelInventorySeed> ModelInventoryCatalog =
    [
        new("ECL", "IFRS 9 Expected Credit Loss", "Tier 1", "Credit Risk", "MFB_IFR", ["ecl", "pd", "lgd", "ead"]),
        new("CAR", "Capital Adequacy Ratio Engine", "Tier 1", "Prudential Reporting", "MFB_CAP", ["car", "capital", "tier1", "tier2", "rwa"]),
        new("LCR", "Liquidity Coverage Ratio Engine", "Tier 1", "Treasury", "N/A", ["lcr", "hqla", "outflow"]),
        new("NSFR", "Net Stable Funding Ratio Engine", "Tier 2", "Treasury", "N/A", ["nsfr", "asf", "rsf"]),
        new("STRESS", "Sector Stress Testing Framework", "Tier 1", "Supervisory Analytics", "RG-11", ["stress", "shock", "scenario"]),
        new("CLIMATE", "Climate Risk Scenario Model", "Tier 2", "Climate Risk", "RG-11", ["climate", "esg", "transition", "stranded"])
    ];

    private readonly MetadataDbContext _db;
    private readonly KnowledgeGraphCatalogService _knowledgeGraphCatalog;
    private readonly CapitalPackCatalogService _capitalPackCatalog;
    private readonly OpsResiliencePackCatalogService _opsResiliencePackCatalog;
    private readonly ModelRiskPackCatalogService _modelRiskPackCatalog;
    private readonly SanctionsWatchlistCatalogService _sanctionsWatchlistCatalog;
    private readonly SanctionsWorkflowStoreService _sanctionsWorkflowStore;
    private readonly ModelApprovalWorkflowStoreService _modelApprovalWorkflowStore;
    private readonly ResilienceAssessmentStoreService _resilienceAssessmentStore;

    public PlatformIntelligenceService(
        MetadataDbContext db,
        KnowledgeGraphCatalogService knowledgeGraphCatalog,
        CapitalPackCatalogService capitalPackCatalog,
        OpsResiliencePackCatalogService opsResiliencePackCatalog,
        ModelRiskPackCatalogService modelRiskPackCatalog,
        SanctionsWatchlistCatalogService sanctionsWatchlistCatalog,
        SanctionsWorkflowStoreService sanctionsWorkflowStore,
        ModelApprovalWorkflowStoreService modelApprovalWorkflowStore,
        ResilienceAssessmentStoreService resilienceAssessmentStore)
    {
        _db = db;
        _knowledgeGraphCatalog = knowledgeGraphCatalog;
        _capitalPackCatalog = capitalPackCatalog;
        _opsResiliencePackCatalog = opsResiliencePackCatalog;
        _modelRiskPackCatalog = modelRiskPackCatalog;
        _sanctionsWatchlistCatalog = sanctionsWatchlistCatalog;
        _sanctionsWorkflowStore = sanctionsWorkflowStore;
        _modelApprovalWorkflowStore = modelApprovalWorkflowStore;
        _resilienceAssessmentStore = resilienceAssessmentStore;
    }

    public async Task<PlatformIntelligenceWorkspace> GetWorkspaceAsync(CancellationToken ct = default)
    {
        var modules = await _db.Modules
            .AsNoTracking()
            .ToListAsync(ct);

        var templates = await _db.ReturnTemplates
            .AsNoTracking()
            .ToListAsync(ct);

        var versions = await _db.TemplateVersions
            .AsNoTracking()
            .ToListAsync(ct);

        var activeVersionIds = versions
            .GroupBy(x => x.TemplateId)
            .Select(g => g
                .OrderByDescending(v => v.Status == TemplateStatus.Published)
                .ThenByDescending(v => v.VersionNumber)
                .First().Id)
            .ToHashSet();

        var fields = await _db.TemplateFields
            .AsNoTracking()
            .Where(x => activeVersionIds.Contains(x.TemplateVersionId))
            .ToListAsync(ct);

        var formulas = await _db.IntraSheetFormulas
            .AsNoTracking()
            .Where(x => activeVersionIds.Contains(x.TemplateVersionId) && x.IsActive)
            .ToListAsync(ct);

        var crossSheetRules = await _db.CrossSheetRules
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(ct);

        var businessRules = await _db.BusinessRules
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(ct);

        var licenceTypes = await _db.LicenceTypes
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(ct);

        var licenceModules = await _db.LicenceModuleMatrix
            .AsNoTracking()
            .ToListAsync(ct);

        var tenantLicences = await _db.TenantLicenceTypes
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(ct);

        var returnPeriods = await _db.ReturnPeriods
            .AsNoTracking()
            .ToListAsync(ct);

        var institutions = await _db.Institutions
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(ct);

        var latestChs = await GetLatestChsSnapshotsAsync(ct);

        var incidents = await _db.DataBreachIncidents
            .AsNoTracking()
            .OrderByDescending(x => x.DetectedAt)
            .Take(12)
            .ToListAsync(ct);

        var cyberAssets = await _db.CyberAssets
            .AsNoTracking()
            .ToListAsync(ct);

        var dependencies = await _db.CyberAssetDependencies
            .AsNoTracking()
            .ToListAsync(ct);

        var rcaRecords = await _db.RootCauseAnalysisRecords
            .AsNoTracking()
            .ToListAsync(ct);

        var securityAlerts = await _db.SecurityAlerts
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(12)
            .ToListAsync(ct);

        var auditLog = await _db.AuditLog
            .AsNoTracking()
            .OrderByDescending(x => x.PerformedAt)
            .Take(400)
            .ToListAsync(ct);

        var fieldChanges = await _db.FieldChangeHistory
            .AsNoTracking()
            .OrderByDescending(x => x.ChangedAt)
            .Take(400)
            .ToListAsync(ct);

        var submissions = await _db.Submissions
            .AsNoTracking()
            .Include(x => x.ValidationReport)
            .OrderByDescending(x => x.SubmittedAt)
            .Take(2500)
            .ToListAsync(ct);

        var policyScenarios = await _db.PolicyScenarios
            .AsNoTracking()
            .ToListAsync(ct);

        var impactRuns = await _db.ImpactAssessmentRuns
            .AsNoTracking()
            .Include(x => x.Scenario)
            .OrderByDescending(x => x.CreatedAt)
            .Take(400)
            .ToListAsync(ct);

        var historicalImpact = await _db.HistoricalImpactTracking
            .AsNoTracking()
            .ToListAsync(ct);

        var moduleById = modules.ToDictionary(x => x.Id);
        var templateById = templates.ToDictionary(x => x.Id);
        var versionById = versions.ToDictionary(x => x.Id);

        var fieldRows = fields
            .Select(field =>
            {
                var version = versionById[field.TemplateVersionId];
                var template = templateById[version.TemplateId];
                moduleById.TryGetValue(template.ModuleId ?? 0, out var module);
                return new FieldLineageSource(field, version, template, module);
            })
            .ToList();

        var referencedRows = fieldRows
            .Where(x => !string.IsNullOrWhiteSpace(x.Field.RegulatoryReference))
            .ToList();

        var obligationRows = BuildObligationRows(modules, templates, licenceTypes, licenceModules, tenantLicences, returnPeriods);
        var institutionObligationRows = BuildInstitutionObligationRows(modules, templates, licenceTypes, licenceModules, institutions, returnPeriods, submissions);
        var impactRows = BuildImpactRows(referencedRows, formulas, crossSheetRules, businessRules, institutions, tenantLicences, licenceTypes, licenceModules, modules, templates);
        var ontologyCoverageRows = BuildOntologyCoverageRows(referencedRows, obligationRows, institutionObligationRows);
        var requirementRegisterRows = BuildRequirementRegisterRows(referencedRows, institutionObligationRows);
        var impactPropagationRows = BuildImpactPropagationRows(referencedRows, impactRows, institutionObligationRows);
        var allLineageRows = referencedRows
            .OrderBy(x => x.Module?.RegulatorCode)
            .ThenBy(x => x.Template.ReturnCode)
            .ThenBy(x => x.Field.DisplayName)
            .Select(x => new KnowledgeGraphLineageRow
            {
                NavigatorKey = BuildKnowledgeNavigatorKey(x.Field.RegulatoryReference ?? string.Empty, x.Template.ReturnCode, x.Field.FieldName),
                RegulatorCode = x.Module?.RegulatorCode ?? "N/A",
                ModuleCode = x.Module?.ModuleCode ?? "N/A",
                ReturnCode = x.Template.ReturnCode,
                TemplateName = x.Template.Name,
                FieldName = x.Field.DisplayName,
                FieldCode = x.Field.FieldName,
                RegulatoryReference = x.Field.RegulatoryReference ?? string.Empty
            })
            .ToList();
        var lineageRows = allLineageRows.Take(12).ToList();
        var knowledgeNavigatorDetails = BuildKnowledgeNavigatorDetails(lineageRows, institutionObligationRows, submissions, impactRows, institutions);
        var knowledgeDossierPack = BuildKnowledgeDossierRows(
            ontologyCoverageRows,
            requirementRegisterRows,
            obligationRows,
            institutionObligationRows,
            impactPropagationRows,
            knowledgeNavigatorDetails);
        var knowledgeCatalog = await _knowledgeGraphCatalog.MaterializeAsync(
            BuildKnowledgeGraphCatalogRequest(
                ontologyCoverageRows,
                requirementRegisterRows,
                allLineageRows,
                obligationRows,
                institutionObligationRows),
            ct);
        var sanctionsCatalog = await _sanctionsWatchlistCatalog.MaterializeAsync(
            BuildSanctionsCatalogRequest(),
            ct);
        var sanctionsWorkflowState = await _sanctionsWorkflowStore.LoadAsync(ct);
        var modelApprovalWorkflowState = await _modelApprovalWorkflowStore.LoadAsync(ct);
        var resilienceAssessmentState = await _resilienceAssessmentStore.LoadAsync(ct);
        var capitalPackCatalog = await _capitalPackCatalog.LoadAsync(ct);
        var capitalRows = BuildCapitalRows(latestChs, institutions);
        var hotspotRows = BuildDependencyHotspots(cyberAssets, dependencies);
        var resilienceActions = BuildResilienceActionRows(incidents, securityAlerts, hotspotRows);
        var resilienceTests = BuildResilienceTestingRows(policyScenarios, impactRuns);
        var importantBusinessServices = BuildImportantBusinessServices(cyberAssets, dependencies, incidents, securityAlerts, resilienceTests);
        var impactToleranceRows = BuildImpactToleranceRows(importantBusinessServices);
        var thirdPartyRiskRows = BuildThirdPartyProviderRiskRows(cyberAssets, dependencies, importantBusinessServices, resilienceTests);
        var businessContinuityPlans = BuildBusinessContinuityPlanRows(importantBusinessServices, resilienceTests, resilienceActions);
        var changeManagementControls = BuildResilienceChangeManagementControlRows(auditLog, fieldChanges);
        var resilienceGaps = BuildResilienceGapRows(cyberAssets, dependencies, hotspotRows, incidents, securityAlerts, rcaRecords, resilienceTests, resilienceActions);
        var resilienceIncidentTimelines = BuildResilienceIncidentTimelines(incidents, rcaRecords);
        var recoveryTimeTestingRows = BuildRecoveryTimeTestingRows(importantBusinessServices, resilienceTests, resilienceIncidentTimelines);
        var cyberAssessmentRows = BuildCyberResilienceAssessmentRows(cyberAssets, importantBusinessServices, securityAlerts, hotspotRows, resilienceTests, resilienceIncidentTimelines, recoveryTimeTestingRows);
        var resilienceBoardSummary = BuildResilienceBoardSummary(resilienceGaps, resilienceActions, resilienceTests, incidents);
        var opsResilienceReturnPack = BuildOpsResilienceReturnPackRows(
            importantBusinessServices,
            impactToleranceRows,
            resilienceTests,
            thirdPartyRiskRows,
            incidents,
            businessContinuityPlans,
            cyberAssessmentRows,
            changeManagementControls,
            recoveryTimeTestingRows,
            resilienceBoardSummary);
        var opsResiliencePackCatalog = await _opsResiliencePackCatalog.MaterializeAsync(
            opsResilienceReturnPack
                .Select(x => new OpsResiliencePackSheetInput
                {
                    SheetCode = x.SheetCode,
                    SheetName = x.SheetName,
                    RowCount = x.RowCount,
                    Signal = x.Signal,
                    Coverage = x.Coverage,
                    Commentary = x.Commentary,
                    RecommendedAction = x.RecommendedAction
                })
                .ToList(),
            ct);
        var modelInventory = BuildModelInventory(fields, formulas, templates, modules, submissions, policyScenarios, historicalImpact);
        var modelChanges = BuildModelChangeRows(auditLog, fieldChanges);
        var modelValidationCalendar = BuildModelValidationCalendar(modelInventory);
        var modelPerformanceRows = BuildModelPerformanceRows(modelInventory, submissions, policyScenarios, historicalImpact);
        var modelBacktestingRows = BuildModelBacktestingRows(modelInventory, submissions, policyScenarios, historicalImpact);
        var modelMonitoringRows = BuildModelMonitoringSummaryRows(modelInventory, modelPerformanceRows, modelBacktestingRows);
        var modelApprovalQueue = BuildModelApprovalQueue(modelInventory, modelChanges);
        var modelRiskAppetiteRows = BuildModelRiskAppetiteRows(modelInventory, modelValidationCalendar, modelPerformanceRows, modelApprovalQueue);
        var modelRiskReportingPack = BuildModelRiskReportingRows(modelInventory, modelValidationCalendar, modelPerformanceRows, modelChanges, modelRiskAppetiteRows);
        var modelRiskReturnPack = BuildModelRiskReturnPackRows(
            modelInventory,
            modelValidationCalendar,
            modelPerformanceRows,
            modelBacktestingRows,
            modelMonitoringRows,
            modelChanges,
            modelApprovalQueue,
            modelRiskAppetiteRows,
            modelRiskReportingPack);
        var modelRiskPackCatalog = await _modelRiskPackCatalog.MaterializeAsync(
            modelRiskReturnPack
                .Select(x => new ModelRiskPackSheetInput
                {
                    SheetCode = x.SheetCode,
                    SheetName = x.SheetName,
                    RowCount = x.RowCount,
                    Signal = x.Signal,
                    Coverage = x.Coverage,
                    Commentary = x.Commentary,
                    RecommendedAction = x.RecommendedAction
                })
                .ToList(),
            ct);
        var institutionScorecards = BuildInstitutionScorecards(institutions, institutionObligationRows, capitalRows, incidents, securityAlerts, modelChanges);
        var activityTimeline = BuildActivityTimeline(submissions, institutions, incidents, securityAlerts, auditLog, fieldChanges);
        var interventions = BuildInterventionQueue(institutionObligationRows, capitalRows, resilienceActions, modelChanges);
        var institutionDetails = BuildInstitutionDetails(
            institutionScorecards,
            institutionObligationRows,
            submissions,
            activityTimeline,
            capitalRows,
            institutions);

        return new PlatformIntelligenceWorkspace
        {
            GeneratedAt = DateTime.UtcNow,
            Hero = new PlatformIntelligenceHero
            {
                KnowledgeGraphNodes = knowledgeCatalog.NodeCount > 0 ? knowledgeCatalog.NodeCount : referencedRows.Count,
                ActiveCapitalAlerts = capitalRows.Count(x => x.CapitalScore < 60m),
                WatchlistSources = sanctionsCatalog.SourceCount,
                OpenResilienceIncidents = incidents.Count(x => !x.RemediatedAt.HasValue),
                ModelsUnderGovernance = modelInventory.Count,
                PriorityInterventions = interventions.Count(x => x.Priority is "Critical" or "High")
            },
            KnowledgeGraph = new KnowledgeGraphSnapshot
            {
                RegulatorCount = modules.Select(x => x.RegulatorCode).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                ModuleCount = modules.Count,
                TemplateCount = templates.Count,
                FieldCount = fields.Count,
                ReferencedFieldCount = referencedRows.Count,
                RequirementCount = referencedRows
                    .Select(x => x.Field.RegulatoryReference)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                ObligationCount = obligationRows.Count,
                ImpactSurfaceCount = impactRows.Count,
                PersistedNodeCount = knowledgeCatalog.NodeCount,
                PersistedEdgeCount = knowledgeCatalog.EdgeCount,
                CatalogMaterializedAt = knowledgeCatalog.MaterializedAt,
                DossierReadyCount = knowledgeDossierPack.Count(x => x.Signal == "Current"),
                DossierAttentionCount = knowledgeDossierPack.Count(x => x.Signal is "Critical" or "Watch"),
                CatalogNodeTypes = knowledgeCatalog.NodeTypes
                    .Select(x => new KnowledgeGraphCatalogTypeRow { Type = x.Type, Count = x.Count })
                    .ToList(),
                CatalogEdgeTypes = knowledgeCatalog.EdgeTypes
                    .Select(x => new KnowledgeGraphCatalogTypeRow { Type = x.Type, Count = x.Count })
                    .ToList(),
                InstitutionOptions = BuildInstitutionOptions(institutionObligationRows),
                OntologyCoverage = ontologyCoverageRows,
                RequirementRegister = requirementRegisterRows,
                Lineage = lineageRows,
                NavigatorDetails = knowledgeNavigatorDetails,
                Obligations = obligationRows.Take(12).ToList(),
                InstitutionObligations = institutionObligationRows,
                ImpactSurfaces = impactRows.Take(10).ToList(),
                ImpactPropagation = impactPropagationRows,
                DossierPack = knowledgeDossierPack
            },
            Capital = new CapitalManagementSnapshot
            {
                AverageCapitalScore = latestChs.Count == 0 ? 0m : Math.Round(latestChs.Average(x => x.RegulatoryCapital), 1),
                MedianCapitalScore = latestChs.Count == 0 ? 0m : Median(latestChs.Select(x => x.RegulatoryCapital)),
                CapitalWatchlistCount = capitalRows.Count(x => x.CapitalScore < 60m),
                ActiveScenarioCount = policyScenarios.Count(x => x.Status != PolicyStatus.Enacted),
                ActionTemplates = CapitalActionTemplates.ToList(),
                Watchlist = capitalRows.Take(10).ToList(),
                ReturnPack = capitalPackCatalog.Sections.ToList(),
                ReturnPackAttentionCount = capitalPackCatalog.Sections.Count(x => x.Signal is "Critical" or "Watch"),
                ReturnPackMaterializedAt = capitalPackCatalog.MaterializedAt
            },
            Sanctions = new SanctionsSnapshot
            {
                SourceCount = sanctionsCatalog.SourceCount,
                EntryCount = sanctionsCatalog.EntryCount,
                LastUpdatedAt = sanctionsCatalog.MaterializedAt,
                PersistedFalsePositiveCount = sanctionsWorkflowState.FalsePositiveLibrary.Count,
                PersistedReviewAuditCount = sanctionsWorkflowState.AuditTrail.Count,
                LastReviewedAt = sanctionsWorkflowState.AuditTrail.FirstOrDefault()?.ReviewedAtUtc,
                TfsLinkedFieldCount = fields.Count(x =>
                    !string.IsNullOrWhiteSpace(x.FieldName)
                    && (x.FieldName.Contains("tfs", StringComparison.OrdinalIgnoreCase)
                        || x.FieldName.Contains("sanction", StringComparison.OrdinalIgnoreCase)
                        || x.FieldName.Contains("watchlist", StringComparison.OrdinalIgnoreCase))),
                Sources = sanctionsCatalog.Sources
                    .Select(x => new SanctionsWatchlistSource(
                        x.SourceCode,
                        x.SourceName,
                        x.RefreshCadence,
                        x.Status,
                        x.EntryCount,
                        x.MaterializedAt))
                    .ToList()
            },
            Resilience = new OperationalResilienceSnapshot
            {
                CriticalAssetCount = cyberAssets.Count(x => IsCriticalAsset(x.Criticality)),
                DependencyEdgeCount = dependencies.Count,
                OpenIncidentCount = incidents.Count(x => !x.RemediatedAt.HasValue),
                RcaCoveragePercent = incidents.Count == 0 ? 100m : Math.Round(rcaRecords.Count / (decimal)incidents.Count * 100m, 1),
                GapScore = resilienceBoardSummary.GapScore,
                PersistedAssessmentResponseCount = resilienceAssessmentState.Responses.Count,
                LastAssessmentUpdatedAt = resilienceAssessmentState.Responses
                    .OrderByDescending(x => x.AnsweredAtUtc)
                    .FirstOrDefault()?.AnsweredAtUtc,
                ReturnPackReadyCount = opsResiliencePackCatalog.Sheets.Count(x => x.Signal == "Current"),
                ReturnPackAttentionCount = opsResiliencePackCatalog.Sheets.Count(x => x.Signal is "Critical" or "Watch"),
                ReturnPackMaterializedAt = opsResiliencePackCatalog.MaterializedAt,
                CyberAssessmentScore = cyberAssessmentRows.Count == 0 ? 0m : Math.Round(cyberAssessmentRows.Average(x => x.Score), 1),
                CyberAssessmentCriticalCount = cyberAssessmentRows.Count(x => x.Signal == "Critical"),
                ImportantServiceCount = importantBusinessServices.Count,
                ImpactToleranceWatchCount = impactToleranceRows.Count(x => x.Signal is "Critical" or "Watch"),
                ThirdPartyProviderCount = thirdPartyRiskRows.Count,
                ThirdPartyConcentrationCount = thirdPartyRiskRows.Count(x => x.Signal is "Critical" or "Watch"),
                BusinessContinuityRiskCount = businessContinuityPlans.Count(x => x.Status is "Critical" or "Watch"),
                RecoveryTimeWatchCount = recoveryTimeTestingRows.Count(x => x.Signal == "Watch"),
                RecoveryTimeBreachCount = recoveryTimeTestingRows.Count(x => x.Signal == "Breach"),
                ChangeControlReviewCount = changeManagementControls.Count(x => x.Status is "Critical" or "Watch"),
                Hotspots = hotspotRows.Take(8).ToList(),
                BusinessServices = importantBusinessServices,
                ImpactTolerances = impactToleranceRows,
                CyberAssessment = cyberAssessmentRows,
                ReturnPack = opsResiliencePackCatalog.Sheets
                    .Select(x => new OpsResilienceSheetRow
                    {
                        SheetCode = x.SheetCode,
                        SheetName = x.SheetName,
                        RowCount = x.RowCount,
                        Signal = x.Signal,
                        Coverage = x.Coverage,
                        Commentary = x.Commentary,
                        RecommendedAction = x.RecommendedAction
                    })
                    .ToList(),
                ThirdPartyRegister = thirdPartyRiskRows,
                BusinessContinuityPlans = businessContinuityPlans,
                RecoveryTimeTests = recoveryTimeTestingRows,
                ChangeManagementControls = changeManagementControls,
                GapAnalysis = resilienceGaps,
                BoardSummary = resilienceBoardSummary,
                TestingSchedule = resilienceTests,
                ActionTracker = resilienceActions,
                RecentIncidents = incidents.Select(x => new ResilienceIncidentRow
                {
                    IncidentKey = BuildResilienceIncidentKey(x),
                    Title = x.Title,
                    Severity = x.Severity.ToString(),
                    Status = x.Status.ToString(),
                    DetectedAt = x.DetectedAt,
                    ContainedAt = x.ContainedAt,
                    RemediatedAt = x.RemediatedAt
                }).ToList(),
                IncidentTimelines = resilienceIncidentTimelines,
                RecentSecurityAlerts = securityAlerts.Select(x => new SecurityAlertRow
                {
                    Title = x.Title,
                    AlertType = x.AlertType,
                    Severity = x.Severity,
                    Status = x.Status,
                    CreatedAt = x.CreatedAt
                }).ToList()
            },
            ModelRisk = new ModelRiskSnapshot
            {
                InventoryCount = modelInventory.Count,
                DueValidationCount = modelInventory.Count(x => x.ValidationStatus is "Due" or "Overdue"),
                PerformanceAlertCount = modelInventory.Count(x => x.PerformanceStatus is "Watch" or "Alert"),
                BacktestingCoverageCount = modelBacktestingRows.Count(x => x.SampleSize > 0),
                StabilityWatchCount = modelMonitoringRows.Count(x => x.Signal is "Watch" or "Alert"),
                CalibrationReviewCount = modelMonitoringRows.Count(x => x.CalibrationStatus is "Review" or "Alert"),
                AverageAccuracyScore = historicalImpact.Count == 0
                    ? null
                    : Math.Round(historicalImpact.Where(x => x.AccuracyScore.HasValue).DefaultIfEmpty().Average(x => x?.AccuracyScore ?? 0m), 1),
                ChangeReviewCount = modelChanges.Count(x => x.ReviewSignal is "Critical" or "Review"),
                ApprovalQueueCount = modelApprovalQueue.Count,
                RiskAppetiteScore = modelRiskAppetiteRows.Count == 0 ? 0m : Math.Round(modelRiskAppetiteRows.Average(x => x.RiskScore), 1),
                InAppetiteCount = modelRiskAppetiteRows.Count(x => x.AppetiteStatus == "Within Appetite"),
                WatchCount = modelRiskAppetiteRows.Count(x => x.AppetiteStatus == "Watch"),
                BreachCount = modelRiskAppetiteRows.Count(x => x.AppetiteStatus == "Breach"),
                ReturnPackReadyCount = modelRiskPackCatalog.Sheets.Count(x => x.Signal == "Current"),
                ReturnPackAttentionCount = modelRiskPackCatalog.Sheets.Count(x => x.Signal is "Critical" or "Watch"),
                ReturnPackMaterializedAt = modelRiskPackCatalog.MaterializedAt,
                PersistedApprovalAuditCount = modelApprovalWorkflowState.AuditTrail.Count,
                LastApprovalWorkflowChangeAt = modelApprovalWorkflowState.AuditTrail.FirstOrDefault()?.ChangedAtUtc,
                ValidationCalendar = modelValidationCalendar,
                PerformanceEvidence = modelPerformanceRows,
                Backtesting = modelBacktestingRows,
                MonitoringSummary = modelMonitoringRows,
                AppetiteRegister = modelRiskAppetiteRows,
                ReportingPack = modelRiskReportingPack,
                ReturnPack = modelRiskPackCatalog.Sheets
                    .Select(x => new ModelRiskSheetRow
                    {
                        SheetCode = x.SheetCode,
                        SheetName = x.SheetName,
                        RowCount = x.RowCount,
                        Signal = x.Signal,
                        Coverage = x.Coverage,
                        Commentary = x.Commentary,
                        RecommendedAction = x.RecommendedAction
                    })
                    .ToList(),
                ApprovalQueue = modelApprovalQueue,
                RecentChanges = modelChanges,
                Inventory = modelInventory
            },
            InstitutionScorecards = institutionScorecards,
            InstitutionDetails = institutionDetails,
            ActivityTimeline = activityTimeline,
            Interventions = interventions
        };
    }

    public async Task<SanctionsScreeningRun> ScreenSubjectsAsync(IEnumerable<string> subjects, double threshold, CancellationToken ct = default)
    {
        var cleanedSubjects = subjects
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var catalog = await LoadSanctionsCatalogAsync(ct);
        var sourceNameByCode = catalog.Sources.ToDictionary(x => x.SourceCode, x => x.SourceName, StringComparer.OrdinalIgnoreCase);
        var watchlistEntries = catalog.Entries
            .Select(x => new SanctionsWatchlistEntry(
                x.SourceCode,
                x.PrimaryName,
                x.Aliases,
                x.Category,
                x.RiskLevel))
            .ToList();

        if (watchlistEntries.Count == 0)
        {
            return new SanctionsScreeningRun
            {
                ThresholdPercent = Math.Round(threshold * 100d, 0),
                ScreenedAt = DateTime.UtcNow,
                TotalSubjects = cleanedSubjects.Count,
                MatchCount = 0,
                TfsPreview = new SanctionsTfsPreview(),
                Results = cleanedSubjects
                    .Select(subject => new SanctionsScreeningResultRow
                    {
                        Subject = subject,
                        Disposition = "Clear",
                        MatchScore = 0d,
                        MatchedName = "No material match",
                        SourceCode = "N/A",
                        SourceName = "N/A",
                        Category = "clear",
                        RiskLevel = "low"
                    })
                    .ToList()
            };
        }

        var results = new List<SanctionsScreeningResultRow>();
        foreach (var subject in cleanedSubjects)
        {
            var bestMatch = watchlistEntries
                .Select(entry => ScoreSubject(subject, entry))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Entry.SourceCode, StringComparer.OrdinalIgnoreCase)
                .First();

            var disposition = bestMatch.ExactMatch
                ? "True Match"
                : bestMatch.Score >= threshold
                    ? "Potential Match"
                    : "Clear";

            results.Add(new SanctionsScreeningResultRow
            {
                Subject = subject,
                Disposition = disposition,
                MatchScore = Math.Round(bestMatch.Score * 100d, 1),
                MatchedName = disposition == "Clear" ? "No material match" : bestMatch.MatchedAlias,
                SourceCode = disposition == "Clear" ? "N/A" : bestMatch.Entry.SourceCode,
                SourceName = disposition == "Clear"
                    ? "N/A"
                    : sourceNameByCode.GetValueOrDefault(bestMatch.Entry.SourceCode, bestMatch.Entry.SourceCode),
                Category = disposition == "Clear" ? "clear" : bestMatch.Entry.Category,
                RiskLevel = disposition == "Clear" ? "low" : bestMatch.Entry.RiskLevel
            });
        }

        return new SanctionsScreeningRun
        {
            ThresholdPercent = Math.Round(threshold * 100d, 0),
            ScreenedAt = DateTime.UtcNow,
            TotalSubjects = cleanedSubjects.Count,
            MatchCount = results.Count(x => x.Disposition != "Clear"),
            TfsPreview = BuildTfsPreview(results),
            Results = results
        };
    }

    public async Task<SanctionsTransactionScreeningResult> ScreenTransactionAsync(
        SanctionsTransactionScreeningRequest request,
        CancellationToken ct = default)
    {
        var subjectMap = new List<(string Role, string Name)>
        {
            ("Originator", request.OriginatorName),
            ("Beneficiary", request.BeneficiaryName),
            ("Counterparty", request.CounterpartyName)
        }
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .ToList();

        var threshold = request.HighRisk ? 0.82d : 0.88d;
        var screeningRun = await ScreenSubjectsAsync(subjectMap.Select(x => x.Name), threshold, ct);

        var roleByName = subjectMap
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Role, StringComparer.OrdinalIgnoreCase);

        var partyResults = screeningRun.Results
            .Select(row => new SanctionsTransactionPartyResult
            {
                PartyRole = roleByName.GetValueOrDefault(row.Subject, "Party"),
                PartyName = row.Subject,
                Disposition = row.Disposition,
                MatchScore = row.MatchScore,
                MatchedName = row.MatchedName,
                SourceCode = row.SourceCode,
                RiskLevel = row.RiskLevel
            })
            .ToList();

        var blockDecision = partyResults.Any(x =>
            x.Disposition == "True Match"
            || (x.MatchScore >= 94d && x.RiskLevel is "critical" or "high"));
        var escalateDecision = !blockDecision && partyResults.Any(x => x.Disposition == "Potential Match");
        var controlDecision = blockDecision
            ? "Block"
            : escalateDecision
                ? "Escalate"
                : "Clear";

        var narrative = controlDecision switch
        {
            "Block" => "A material sanctions hit was detected on the transaction path. Stop settlement, preserve evidence, and route immediately to compliance and operations control.",
            "Escalate" => "Potential sanctions overlap remains on at least one transaction party. Hold for analyst review before release or filing.",
            _ => "No sanctions hit crossed the configured real-time threshold for this transaction. The payment path can proceed with standard monitoring."
        };

        return new SanctionsTransactionScreeningResult
        {
            TransactionReference = request.TransactionReference,
            Amount = request.Amount,
            Currency = request.Currency,
            Channel = request.Channel,
            ThresholdPercent = screeningRun.ThresholdPercent,
            HighRisk = request.HighRisk,
            ControlDecision = controlDecision,
            Narrative = narrative,
            RequiresStrDraft = blockDecision,
            PartyResults = partyResults
        };
    }

    public Task<SanctionsWorkflowState> GetSanctionsWorkflowStateAsync(CancellationToken ct = default) =>
        _sanctionsWorkflowStore.LoadAsync(ct);

    public Task RecordSanctionsDecisionAsync(SanctionsWorkflowDecisionCommand command, CancellationToken ct = default) =>
        _sanctionsWorkflowStore.RecordDecisionAsync(command, ct);

    public Task<ModelApprovalWorkflowState> GetModelApprovalWorkflowStateAsync(CancellationToken ct = default) =>
        _modelApprovalWorkflowStore.LoadAsync(ct);

    public Task RecordModelApprovalStageAsync(ModelApprovalWorkflowCommand command, CancellationToken ct = default) =>
        _modelApprovalWorkflowStore.RecordStageChangeAsync(command, ct);

    public Task<ResilienceAssessmentState> GetResilienceAssessmentStateAsync(CancellationToken ct = default) =>
        _resilienceAssessmentStore.LoadAsync(ct);

    public Task RecordResilienceAssessmentResponseAsync(ResilienceAssessmentResponseCommand command, CancellationToken ct = default) =>
        _resilienceAssessmentStore.RecordResponseAsync(command, ct);

    public Task ResetResilienceAssessmentAsync(CancellationToken ct = default) =>
        _resilienceAssessmentStore.ResetAsync(ct);

    public Task<CapitalPackCatalogState> GetCapitalPackCatalogStateAsync(CancellationToken ct = default) =>
        _capitalPackCatalog.LoadAsync(ct);

    public Task<CapitalPackCatalogState> MaterializeCapitalPackAsync(
        IReadOnlyList<CapitalPackSectionInput> sections,
        CancellationToken ct = default) =>
        _capitalPackCatalog.MaterializeAsync(sections, ct);

    private async Task<SanctionsCatalogState> LoadSanctionsCatalogAsync(CancellationToken ct)
    {
        var catalog = await _sanctionsWatchlistCatalog.LoadAsync(ct);
        if (catalog.Entries.Count > 0 && catalog.Sources.Count > 0)
        {
            return catalog;
        }

        await _sanctionsWatchlistCatalog.MaterializeAsync(BuildSanctionsCatalogRequest(), ct);
        return await _sanctionsWatchlistCatalog.LoadAsync(ct);
    }

    private async Task<List<ChsScoreSnapshot>> GetLatestChsSnapshotsAsync(CancellationToken ct)
    {
        var snapshots = await _db.ChsScoreSnapshots
            .AsNoTracking()
            .OrderByDescending(x => x.ComputedAt)
            .ToListAsync(ct);

        return snapshots
            .GroupBy(x => x.TenantId)
            .Select(g => g.First())
            .ToList();
    }

    private static List<KnowledgeGraphObligationRow> BuildObligationRows(
        IReadOnlyList<Module> modules,
        IReadOnlyList<ReturnTemplate> templates,
        IReadOnlyList<LicenceType> licenceTypes,
        IReadOnlyList<LicenceModuleMatrix> licenceModules,
        IReadOnlyList<TenantLicenceType> tenantLicences,
        IReadOnlyList<ReturnPeriod> returnPeriods)
    {
        var moduleById = modules.ToDictionary(x => x.Id);
        var templateMap = templates
            .Where(x => x.ModuleId.HasValue)
            .GroupBy(x => x.ModuleId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.ReturnCode, StringComparer.OrdinalIgnoreCase).ToList());

        var tenantCountsByLicence = tenantLicences
            .GroupBy(x => x.LicenceTypeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.TenantId).Distinct().Count());

        var rows = new List<KnowledgeGraphObligationRow>();
        foreach (var entry in licenceModules.Where(x => x.IsRequired))
        {
            if (!moduleById.TryGetValue(entry.ModuleId, out var module))
            {
                continue;
            }

            var licence = licenceTypes.FirstOrDefault(x => x.Id == entry.LicenceTypeId);
            if (licence is null)
            {
                continue;
            }

            var nextDeadline = returnPeriods
                .Where(x => x.ModuleId == module.Id && x.EffectiveDeadline >= DateTime.UtcNow.AddDays(-3))
                .OrderBy(x => x.EffectiveDeadline)
                .Select(x => (DateTime?)x.EffectiveDeadline)
                .FirstOrDefault()
                ?? EstimateDeadline(module.DefaultFrequency);

            if (!templateMap.TryGetValue(module.Id, out var moduleTemplates))
            {
                continue;
            }

            foreach (var template in moduleTemplates.Take(2))
            {
                rows.Add(new KnowledgeGraphObligationRow
                {
                    LicenceType = licence.Code,
                    RegulatorCode = module.RegulatorCode,
                    ModuleCode = module.ModuleCode,
                    ReturnCode = template.ReturnCode,
                    Frequency = template.Frequency.ToString(),
                    AffectedTenants = tenantCountsByLicence.GetValueOrDefault(licence.Id),
                    NextDeadline = nextDeadline
                });
            }
        }

        return rows
            .OrderByDescending(x => x.AffectedTenants)
            .ThenBy(x => x.NextDeadline)
            .ThenBy(x => x.ModuleCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<KnowledgeGraphInstitutionObligationRow> BuildInstitutionObligationRows(
        IReadOnlyList<Module> modules,
        IReadOnlyList<ReturnTemplate> templates,
        IReadOnlyList<LicenceType> licenceTypes,
        IReadOnlyList<LicenceModuleMatrix> licenceModules,
        IReadOnlyList<Institution> institutions,
        IReadOnlyList<ReturnPeriod> returnPeriods,
        IReadOnlyList<Submission> submissions)
    {
        var moduleById = modules.ToDictionary(x => x.Id);
        var templatesByModule = templates
            .Where(x => x.ModuleId.HasValue)
            .GroupBy(x => x.ModuleId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.ReturnCode, StringComparer.OrdinalIgnoreCase).ToList());

        var periodsByTenantModule = returnPeriods
            .Where(x => x.ModuleId.HasValue)
            .GroupBy(x => (x.TenantId, ModuleId: x.ModuleId!.Value))
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.EffectiveDeadline).ToList());

        var submissionsByInstitutionAndReturn = submissions
            .GroupBy(x => SubmissionLookupKey(x.InstitutionId, x.ReturnCode))
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.SubmittedAt).ToList());

        var headOffices = institutions
            .Where(x => x.IsActive && (x.EntityType == EntityType.HeadOffice || !x.ParentInstitutionId.HasValue))
            .OrderBy(x => x.InstitutionName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = new List<KnowledgeGraphInstitutionObligationRow>();
        foreach (var institution in headOffices)
        {
            if (string.IsNullOrWhiteSpace(institution.LicenseType))
            {
                continue;
            }

            var matchingLicenceIds = licenceTypes
                .Where(x =>
                    x.IsActive &&
                    (institution.LicenseType.Equals(x.Code, StringComparison.OrdinalIgnoreCase)
                     || institution.LicenseType.Equals(x.Name, StringComparison.OrdinalIgnoreCase)))
                .Select(x => x.Id)
                .ToHashSet();

            if (matchingLicenceIds.Count == 0)
            {
                continue;
            }

            var requiredModules = licenceModules
                .Where(x => x.IsRequired && matchingLicenceIds.Contains(x.LicenceTypeId))
                .Select(x => x.ModuleId)
                .Distinct()
                .ToList();

            foreach (var moduleId in requiredModules)
            {
                if (!moduleById.TryGetValue(moduleId, out var module) || !templatesByModule.TryGetValue(moduleId, out var moduleTemplates))
                {
                    continue;
                }

                periodsByTenantModule.TryGetValue((institution.TenantId, moduleId), out var tenantPeriods);
                var relevantPeriod = SelectRelevantPeriod(tenantPeriods);

                foreach (var template in moduleTemplates)
                {
                    submissionsByInstitutionAndReturn.TryGetValue(SubmissionLookupKey(institution.Id, template.ReturnCode), out var submissionHistory);
                    var currentSubmission = relevantPeriod is null
                        ? null
                        : submissionHistory?.FirstOrDefault(x => x.ReturnPeriodId == relevantPeriod.Id);
                    var latestSubmission = submissionHistory?.FirstOrDefault();

                    rows.Add(new KnowledgeGraphInstitutionObligationRow
                    {
                        InstitutionId = institution.Id,
                        TenantId = institution.TenantId,
                        InstitutionName = institution.InstitutionName,
                        LicenceType = institution.LicenseType,
                        RegulatorCode = module.RegulatorCode,
                        ModuleCode = module.ModuleCode,
                        ReturnCode = template.ReturnCode,
                        Frequency = template.Frequency.ToString(),
                        Status = ResolveObligationStatus(relevantPeriod, currentSubmission),
                        LatestSubmissionStatus = (currentSubmission ?? latestSubmission)?.Status.ToString(),
                        LastSubmittedAt = (currentSubmission ?? latestSubmission)?.SubmittedAt,
                        NextDeadline = relevantPeriod?.EffectiveDeadline ?? EstimateDeadline(template.Frequency.ToString()),
                        PeriodLabel = relevantPeriod is null ? "Pending calendar" : FormatPeriodLabel(relevantPeriod)
                    });
                }
            }
        }

        return rows
            .OrderByDescending(x => ObligationSeverityRank(x.Status))
            .ThenBy(x => x.NextDeadline)
            .ThenBy(x => x.InstitutionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ReturnCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<KnowledgeGraphInstitutionOption> BuildInstitutionOptions(
        IReadOnlyList<KnowledgeGraphInstitutionObligationRow> rows)
    {
        return rows
            .GroupBy(x => new { x.InstitutionId, x.TenantId, x.InstitutionName, x.LicenceType })
            .Select(g => new KnowledgeGraphInstitutionOption
            {
                InstitutionId = g.Key.InstitutionId,
                TenantId = g.Key.TenantId,
                InstitutionName = g.Key.InstitutionName,
                LicenceType = g.Key.LicenceType,
                ActiveObligations = g.Count(),
                FiledCount = g.Count(x => x.Status is "Filed" or "Pending Approval" or "In Progress"),
                OverdueCount = g.Count(x => x.Status is "Overdue" or "Attention Required"),
                RegulatorCount = g.Select(x => x.RegulatorCode).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            })
            .OrderByDescending(x => x.OverdueCount)
            .ThenBy(x => x.InstitutionName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<KnowledgeGraphImpactRow> BuildImpactRows(
        IReadOnlyList<FieldLineageSource> referencedRows,
        IReadOnlyList<IntraSheetFormula> formulas,
        IReadOnlyList<CrossSheetRule> crossSheetRules,
        IReadOnlyList<BusinessRule> businessRules,
        IReadOnlyList<Institution> institutions,
        IReadOnlyList<TenantLicenceType> tenantLicences,
        IReadOnlyList<LicenceType> licenceTypes,
        IReadOnlyList<LicenceModuleMatrix> licenceModules,
        IReadOnlyList<Module> modules,
        IReadOnlyList<ReturnTemplate> templates)
    {
        var templateByReturnCode = templates.ToDictionary(x => x.ReturnCode, StringComparer.OrdinalIgnoreCase);
        var moduleById = modules.ToDictionary(x => x.Id);
        var tenantCountByLicence = tenantLicences
            .GroupBy(x => x.LicenceTypeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.TenantId).Distinct().Count());
        var licencesByModule = licenceModules
            .GroupBy(x => x.ModuleId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.LicenceTypeId).Distinct().ToList());

        var formulaCountByTemplateVersion = formulas
            .GroupBy(x => x.TemplateVersionId)
            .ToDictionary(g => g.Key, g => g.Count());

        var rows = referencedRows
            .GroupBy(x => x.Field.RegulatoryReference!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var impactedTemplateIds = group.Select(x => x.Template.Id).Distinct().ToList();
                var impactedModuleIds = group.Select(x => x.Template.ModuleId).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
                var formulaHits = group.Sum(x => formulaCountByTemplateVersion.GetValueOrDefault(x.Version.Id));

                var crossHits = crossSheetRules.Count(rule =>
                    impactedModuleIds.Contains(rule.ModuleId ?? -1)
                    || impactedModuleIds.Contains(rule.SourceModuleId ?? -1)
                    || impactedModuleIds.Contains(rule.TargetModuleId ?? -1)
                    || impactedTemplateIds.Any(id =>
                    {
                        var template = templates.FirstOrDefault(t => t.Id == id);
                        return template is not null
                               && (!string.IsNullOrWhiteSpace(rule.SourceTemplateCode) && rule.SourceTemplateCode.Equals(template.ReturnCode, StringComparison.OrdinalIgnoreCase)
                                   || !string.IsNullOrWhiteSpace(rule.TargetTemplateCode) && rule.TargetTemplateCode.Equals(template.ReturnCode, StringComparison.OrdinalIgnoreCase));
                    }));

                var businessHits = businessRules.Count(rule =>
                    string.IsNullOrWhiteSpace(rule.AppliesToTemplates)
                        ? false
                        : impactedTemplateIds.Any(id =>
                        {
                            var template = templates.FirstOrDefault(t => t.Id == id);
                            return template is not null && rule.AppliesToTemplates.Contains(template.ReturnCode, StringComparison.OrdinalIgnoreCase);
                        }));

                var affectedTenantCount = impactedModuleIds
                    .SelectMany(id => licencesByModule.GetValueOrDefault(id) ?? [])
                    .Distinct()
                    .Sum(licenceId => tenantCountByLicence.GetValueOrDefault(licenceId));

                var affectedInstitutionCount = institutions.Count(inst =>
                    impactedModuleIds.Any(moduleId =>
                        (licencesByModule.GetValueOrDefault(moduleId) ?? [])
                        .Select(id => licenceTypes.FirstOrDefault(x => x.Id == id))
                        .Where(x => x is not null)
                        .Any(licence =>
                            string.Equals(inst.LicenseType, licence!.Code, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(inst.LicenseType, licence.Name, StringComparison.OrdinalIgnoreCase))));

                return new KnowledgeGraphImpactRow
                {
                    RegulatoryReference = group.Key,
                    ImpactedTemplates = impactedTemplateIds.Count,
                    ImpactedFields = group.Count(),
                    ImpactedFormulas = formulaHits,
                    ImpactedCrossSheetRules = crossHits,
                    ImpactedBusinessRules = businessHits,
                    AffectedTenants = affectedTenantCount,
                    AffectedInstitutions = affectedInstitutionCount
                };
            })
            .OrderByDescending(x => x.ImpactedFields)
            .ThenByDescending(x => x.AffectedTenants)
            .ThenBy(x => x.RegulatoryReference, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return rows;
    }

    private static List<KnowledgeGraphImpactPropagationRow> BuildImpactPropagationRows(
        IReadOnlyList<FieldLineageSource> referencedRows,
        IReadOnlyList<KnowledgeGraphImpactRow> impactRows,
        IReadOnlyList<KnowledgeGraphInstitutionObligationRow> institutionObligations)
    {
        return referencedRows
            .GroupBy(x => x.Field.RegulatoryReference!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var returnCounts = group
                    .GroupBy(x => x.Template.ReturnCode, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new
                    {
                        ReturnCode = g.Key,
                        Count = g.Count(),
                        NavigatorKey = BuildKnowledgeNavigatorKey(group.Key, g.Key, g.First().Field.FieldName)
                    })
                    .OrderByDescending(x => x.Count)
                    .ThenBy(x => x.ReturnCode, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var returnCodes = returnCounts.Select(x => x.ReturnCode).ToList();
                var relatedObligations = institutionObligations
                    .Where(x => returnCodes.Contains(x.ReturnCode, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                var impact = impactRows.FirstOrDefault(x => x.RegulatoryReference.Equals(group.Key, StringComparison.OrdinalIgnoreCase));
                var filingAttentionCount = relatedObligations.Count(x => x.Status is "Overdue" or "Attention Required" or "Due Soon");
                var filedInstitutionCount = relatedObligations.Count(x => x.Status is "Filed" or "Pending Approval" or "In Progress");
                var nextDeadline = relatedObligations.Count == 0
                    ? EstimateDeadline(group.First().Template.Frequency.ToString())
                    : relatedObligations.Min(x => x.NextDeadline);
                var ruleSurface = (impact?.ImpactedFormulas ?? 0) + (impact?.ImpactedCrossSheetRules ?? 0) + (impact?.ImpactedBusinessRules ?? 0);
                var affectedInstitutionCount = relatedObligations.Select(x => x.InstitutionId).Distinct().Count();

                var signal = filingAttentionCount > 0
                             || ruleSurface >= 8
                             || affectedInstitutionCount >= 18
                    ? "Critical"
                    : nextDeadline <= DateTime.UtcNow.AddDays(14)
                      || ruleSurface >= 4
                      || affectedInstitutionCount >= 8
                        ? "Watch"
                        : "Current";

                var primaryReturn = returnCounts.FirstOrDefault()?.ReturnCode ?? group.First().Template.ReturnCode;
                var primaryNavigatorKey = returnCounts.FirstOrDefault()?.NavigatorKey
                                          ?? BuildKnowledgeNavigatorKey(group.Key, primaryReturn, group.First().Field.FieldName);

                return new KnowledgeGraphImpactPropagationRow
                {
                    NavigatorKey = primaryNavigatorKey,
                    RegulatoryReference = group.Key,
                    RegulationFamily = ResolveRegulationFamily(group.Key),
                    RegulatorCode = group.First().Module?.RegulatorCode ?? "N/A",
                    PrimaryReturnCode = primaryReturn,
                    ImpactedTemplates = impact?.ImpactedTemplates ?? returnCounts.Count,
                    ImpactedFields = impact?.ImpactedFields ?? group.Count(),
                    RuleSurfaceCount = ruleSurface,
                    AffectedInstitutions = affectedInstitutionCount,
                    FilingAttentionCount = filingAttentionCount,
                    FiledInstitutionCount = filedInstitutionCount,
                    NextDeadline = nextDeadline,
                    Signal = signal,
                    Commentary = $"{returnCounts.Count} return path(s), {(impact?.ImpactedFields ?? group.Count())} field(s), and {affectedInstitutionCount} institution(s) sit under this reference.",
                    RecommendedAction = signal switch
                    {
                        "Critical" => "Run impact review now, assign affected returns for remediation, and notify institutions with filing pressure.",
                        "Watch" => "Validate downstream templates and rules, then refresh guidance before the next filing deadline.",
                        _ => "Keep the reference under routine lineage monitoring and include it in the next metadata review."
                    }
                };
            })
            .OrderByDescending(x => KnowledgeImpactPriorityRank(x.Signal))
            .ThenByDescending(x => x.FilingAttentionCount)
            .ThenByDescending(x => x.AffectedInstitutions)
            .ThenByDescending(x => x.RuleSurfaceCount)
            .ThenBy(x => x.NextDeadline)
            .ThenBy(x => x.RegulatoryReference, StringComparer.OrdinalIgnoreCase)
            .Take(18)
            .ToList();
    }

    private static List<KnowledgeGraphOntologyCoverageRow> BuildOntologyCoverageRows(
        IReadOnlyList<FieldLineageSource> referencedRows,
        IReadOnlyList<KnowledgeGraphObligationRow> obligations,
        IReadOnlyList<KnowledgeGraphInstitutionObligationRow> institutionObligations)
    {
        return referencedRows
            .GroupBy(x => x.Module?.RegulatorCode ?? "N/A", StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var regulatorCode = group.Key;
                var returns = group
                    .Select(x => x.Template.ReturnCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var relatedObligations = obligations
                    .Where(x => x.RegulatorCode.Equals(regulatorCode, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var relatedInstitutionRows = institutionObligations
                    .Where(x => x.RegulatorCode.Equals(regulatorCode, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var regulationFamilies = group
                    .Select(x => ResolveRegulationFamily(x.Field.RegulatoryReference ?? string.Empty))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var primaryPath = group
                    .GroupBy(x => x.Template.ReturnCode, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(x => x.Count())
                    .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.Key)
                    .FirstOrDefault()
                    ?? "N/A";

                return new KnowledgeGraphOntologyCoverageRow
                {
                    RegulatorCode = regulatorCode,
                    RegulationFamilyCount = regulationFamilies.Count,
                    RequirementCount = group
                        .Select(x => x.Field.RegulatoryReference)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(),
                    ModuleCount = group
                        .Select(x => x.Module?.ModuleCode)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(),
                    ReturnCount = returns.Count,
                    InstitutionCount = relatedInstitutionRows
                        .Select(x => x.InstitutionId)
                        .Distinct()
                        .Count(),
                    ObligationCount = relatedObligations.Count,
                    PrimaryFilingPath = primaryPath
                };
            })
            .OrderByDescending(x => x.RequirementCount)
            .ThenBy(x => x.RegulatorCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<KnowledgeGraphRequirementRegisterRow> BuildRequirementRegisterRows(
        IReadOnlyList<FieldLineageSource> referencedRows,
        IReadOnlyList<KnowledgeGraphInstitutionObligationRow> institutionObligations)
    {
        return referencedRows
            .GroupBy(x => x.Field.RegulatoryReference!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var returnCodes = group
                    .Select(x => x.Template.ReturnCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var obligationRows = institutionObligations
                    .Where(x => returnCodes.Contains(x.ReturnCode, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                var appliesTo = obligationRows
                    .Select(x => x.LicenceType)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var frequencyProfile = obligationRows
                    .GroupBy(x => x.Frequency, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(x => x.Count())
                    .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.Key)
                    .FirstOrDefault()
                    ?? group.First().Template.Frequency.ToString();

                return new KnowledgeGraphRequirementRegisterRow
                {
                    RegulatoryReference = group.Key,
                    RegulationFamily = ResolveRegulationFamily(group.Key),
                    RegulatorCode = group.First().Module?.RegulatorCode ?? "N/A",
                    ModuleCode = group.First().Module?.ModuleCode ?? "N/A",
                    FiledViaReturns = string.Join(", ", returnCodes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(3)),
                    AppliesToLicenceTypes = appliesTo.Count == 0
                        ? "Unmapped"
                        : string.Join(", ", appliesTo.Take(3)),
                    InstitutionCount = obligationRows.Select(x => x.InstitutionId).Distinct().Count(),
                    FieldCount = group.Count(),
                    FrequencyProfile = frequencyProfile,
                    NextDeadline = obligationRows.Count == 0
                        ? EstimateDeadline(frequencyProfile)
                        : obligationRows.Min(x => x.NextDeadline)
                };
            })
            .OrderByDescending(x => x.InstitutionCount)
            .ThenByDescending(x => x.FieldCount)
            .ThenBy(x => x.RegulatoryReference, StringComparer.OrdinalIgnoreCase)
            .Take(18)
            .ToList();
    }

    private static List<KnowledgeGraphNavigatorDetail> BuildKnowledgeNavigatorDetails(
        IReadOnlyList<KnowledgeGraphLineageRow> lineageRows,
        IReadOnlyList<KnowledgeGraphInstitutionObligationRow> institutionObligations,
        IReadOnlyList<Submission> submissions,
        IReadOnlyList<KnowledgeGraphImpactRow> impactRows,
        IReadOnlyList<Institution> institutions)
    {
        var institutionById = institutions.ToDictionary(x => x.Id);
        var submissionsByReturn = submissions
            .GroupBy(x => x.ReturnCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.SubmittedAt).ToList(), StringComparer.OrdinalIgnoreCase);

        return lineageRows
            .Select(lineage =>
            {
                var relatedObligations = institutionObligations
                    .Where(x => x.ReturnCode.Equals(lineage.ReturnCode, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => ObligationSeverityRank(x.Status))
                    .ThenBy(x => x.NextDeadline)
                    .Take(8)
                    .ToList();

                var impact = impactRows.FirstOrDefault(x =>
                    x.RegulatoryReference.Equals(lineage.RegulatoryReference, StringComparison.OrdinalIgnoreCase));

                submissionsByReturn.TryGetValue(lineage.ReturnCode, out var returnSubmissions);

                return new KnowledgeGraphNavigatorDetail
                {
                    NavigatorKey = lineage.NavigatorKey,
                    RegulatorCode = lineage.RegulatorCode,
                    ModuleCode = lineage.ModuleCode,
                    ReturnCode = lineage.ReturnCode,
                    TemplateName = lineage.TemplateName,
                    FieldName = lineage.FieldName,
                    FieldCode = lineage.FieldCode,
                    RegulatoryReference = lineage.RegulatoryReference,
                    ImpactedTemplates = impact?.ImpactedTemplates ?? 1,
                    ImpactedFields = impact?.ImpactedFields ?? 1,
                    RuleSurfaceCount = (impact?.ImpactedFormulas ?? 0) + (impact?.ImpactedCrossSheetRules ?? 0) + (impact?.ImpactedBusinessRules ?? 0),
                    AffectedTenantCount = impact?.AffectedTenants ?? relatedObligations.Select(x => x.TenantId).Distinct().Count(),
                    AffectedInstitutionCount = impact?.AffectedInstitutions ?? relatedObligations.Select(x => x.InstitutionId).Distinct().Count(),
                    FiledInstitutionCount = relatedObligations.Count(x => x.Status is "Filed" or "Pending Approval" or "In Progress"),
                    AffectedInstitutions = relatedObligations
                        .Select(x => new KnowledgeGraphNavigatorInstitutionRow
                        {
                            InstitutionId = x.InstitutionId,
                            InstitutionName = x.InstitutionName,
                            LicenceType = x.LicenceType,
                            Status = x.Status,
                            LatestSubmissionStatus = x.LatestSubmissionStatus,
                            LastSubmittedAt = x.LastSubmittedAt,
                            NextDeadline = x.NextDeadline
                        })
                        .ToList(),
                    RecentSubmissions = (returnSubmissions ?? [])
                        .Take(6)
                        .Select(x => new KnowledgeGraphNavigatorSubmissionRow
                        {
                            SubmissionId = x.Id,
                            InstitutionName = institutionById.TryGetValue(x.InstitutionId, out var institution)
                                ? institution.InstitutionName
                                : $"Institution #{x.InstitutionId}",
                            Status = x.Status.ToString(),
                            SubmittedAt = x.SubmittedAt
                        })
                        .ToList()
                };
            })
            .ToList();
    }

    private static List<KnowledgeGraphDossierRow> BuildKnowledgeDossierRows(
        IReadOnlyList<KnowledgeGraphOntologyCoverageRow> ontologyCoverage,
        IReadOnlyList<KnowledgeGraphRequirementRegisterRow> requirementRegister,
        IReadOnlyList<KnowledgeGraphObligationRow> obligations,
        IReadOnlyList<KnowledgeGraphInstitutionObligationRow> institutionObligations,
        IReadOnlyList<KnowledgeGraphImpactPropagationRow> impactPropagation,
        IReadOnlyList<KnowledgeGraphNavigatorDetail> navigatorDetails)
    {
        var criticalImpactRows = impactPropagation.Count(x => x.Signal == "Critical");
        var watchImpactRows = impactPropagation.Count(x => x.Signal == "Watch");
        var overdueInstitutionRows = institutionObligations.Count(x => x.Status is "Overdue" or "Attention Required");
        var dueSoonInstitutionRows = institutionObligations.Count(x => x.Status == "Due Soon");
        var navigatorCoverage = navigatorDetails.Count == 0
            ? 0m
            : Math.Round(navigatorDetails.Count(x => x.AffectedInstitutions.Count > 0 || x.RecentSubmissions.Count > 0) * 100m / navigatorDetails.Count, 1);

        return
        [
            new KnowledgeGraphDossierRow
            {
                SectionCode = "KG-01",
                SectionName = "Regulatory Ontology Coverage",
                RowCount = ontologyCoverage.Count,
                Signal = ontologyCoverage.Count == 0 ? "Watch" : "Current",
                Coverage = $"{ontologyCoverage.Count} regulator coverage row(s) across {ontologyCoverage.Sum(x => x.RequirementCount)} mapped requirement(s).",
                Commentary = ontologyCoverage.Count == 0
                    ? "No regulator ontology coverage is currently materialized."
                    : $"{ontologyCoverage.Count(x => x.ObligationCount == 0)} regulator path(s) have coverage but no derived obligation path.",
                RecommendedAction = "Review regulator-to-module coverage and confirm primary filing paths before dossier sign-off."
            },
            new KnowledgeGraphDossierRow
            {
                SectionCode = "KG-02",
                SectionName = "Requirement Register",
                RowCount = requirementRegister.Count,
                Signal = requirementRegister.Count == 0 ? "Watch" : requirementRegister.Any(x => x.NextDeadline <= DateTime.UtcNow.AddDays(14)) ? "Watch" : "Current",
                Coverage = $"{requirementRegister.Count} requirement row(s) with {requirementRegister.Count(x => x.NextDeadline <= DateTime.UtcNow.AddDays(14))} deadline(s) inside 14 days.",
                Commentary = requirementRegister.Count == 0
                    ? "No requirement-level implementation register is currently materialized."
                    : $"{requirementRegister.Count(x => x.InstitutionCount == 0)} requirement row(s) currently have no institution coverage.",
                RecommendedAction = "Validate requirement-to-return lineage and near-term deadlines before publishing the dossier register."
            },
            new KnowledgeGraphDossierRow
            {
                SectionCode = "KG-03",
                SectionName = "Licence Obligation Matrix",
                RowCount = obligations.Count,
                Signal = obligations.Count == 0 ? "Watch" : "Current",
                Coverage = $"{obligations.Count} licence obligation row(s) across {obligations.Select(x => x.LicenceType).Distinct(StringComparer.OrdinalIgnoreCase).Count()} licence profile(s).",
                Commentary = obligations.Count == 0
                    ? "No licence-driven obligations are currently derived."
                    : $"{obligations.Count(x => x.NextDeadline <= DateTime.UtcNow.AddDays(14))} obligation row(s) fall inside the next 14-day filing window.",
                RecommendedAction = "Confirm licence applicability and filing frequencies before using the obligation matrix as a supervisory baseline."
            },
            new KnowledgeGraphDossierRow
            {
                SectionCode = "KG-04",
                SectionName = "Institution Obligation Register",
                RowCount = institutionObligations.Count,
                Signal = overdueInstitutionRows > 0 ? "Critical" : dueSoonInstitutionRows > 0 ? "Watch" : "Current",
                Coverage = $"{institutionObligations.Count} institution obligation row(s); {overdueInstitutionRows} overdue/attention and {dueSoonInstitutionRows} due soon.",
                Commentary = institutionObligations.Count == 0
                    ? "No institution-specific obligation register is currently materialized."
                    : $"{institutionObligations.Count(x => x.Status is "Filed" or "Pending Approval" or "In Progress")} row(s) already have live filing activity.",
                RecommendedAction = "Use the institution register to clear overdue obligations and reconcile filing activity before dossier issuance."
            },
            new KnowledgeGraphDossierRow
            {
                SectionCode = "KG-05",
                SectionName = "Impact Propagation Register",
                RowCount = impactPropagation.Count,
                Signal = criticalImpactRows > 0 ? "Critical" : watchImpactRows > 0 ? "Watch" : "Current",
                Coverage = $"{impactPropagation.Count} impact row(s); {criticalImpactRows} critical and {watchImpactRows} watch path(s).",
                Commentary = impactPropagation.Count == 0
                    ? "No ranked impact propagation rows are currently available."
                    : $"{impactPropagation.Sum(x => x.FilingAttentionCount)} filing-attention item(s) sit under the current regulatory change surface.",
                RecommendedAction = "Review critical impact paths, update downstream templates and rules, and notify affected institutions where pressure is highest."
            },
            new KnowledgeGraphDossierRow
            {
                SectionCode = "KG-06",
                SectionName = "Compliance Navigator Readiness",
                RowCount = navigatorDetails.Count,
                Signal = navigatorCoverage < 60m ? "Watch" : "Current",
                Coverage = $"{navigatorDetails.Count} navigator trace row(s) with {navigatorCoverage.ToString("0.0", CultureInfo.InvariantCulture)}% institution or filing evidence coverage.",
                Commentary = navigatorDetails.Count == 0
                    ? "No compliance navigator traces are currently materialized."
                    : $"{navigatorDetails.Count(x => x.RuleSurfaceCount > 0)} trace row(s) currently reach downstream rule surfaces.",
                RecommendedAction = "Complete trace enrichment for uncovered navigator rows so regulation-to-filing drilldown is reliable for examiners."
            }
        ];
    }

    private static List<CapitalWatchlistRow> BuildCapitalRows(
        IReadOnlyList<ChsScoreSnapshot> latestChs,
        IReadOnlyList<Institution> institutions)
    {
        var institutionNamesByTenant = institutions
            .GroupBy(x => x.TenantId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.EntityType == EntityType.HeadOffice)
                    .ThenBy(x => x.InstitutionName, StringComparer.OrdinalIgnoreCase)
                    .First());

        return latestChs
            .OrderBy(x => x.RegulatoryCapital)
            .Select(x => new CapitalWatchlistRow
            {
                TenantId = x.TenantId,
                InstitutionId = institutionNamesByTenant.GetValueOrDefault(x.TenantId)?.Id,
                InstitutionName = institutionNamesByTenant.TryGetValue(x.TenantId, out var institution)
                    ? institution.InstitutionName
                    : x.TenantId.ToString("N")[..8].ToUpperInvariant(),
                CapitalScore = Math.Round(x.RegulatoryCapital, 1),
                OverallScore = Math.Round(x.OverallScore, 1),
                Alert = x.RegulatoryCapital < 45m
                    ? "Immediate capital restoration planning required."
                    : x.RegulatoryCapital < 60m
                        ? "Buffer erosion detected. Model RWA reduction and capital actions."
                        : "Healthy capital posture."
            })
            .ToList();
    }

    private static SanctionsTfsPreview BuildTfsPreview(IReadOnlyList<SanctionsScreeningResultRow> results)
    {
        var potentialMatches = results.Count(x => x.Disposition == "Potential Match");
        var confirmedMatches = results.Count(x => x.Disposition == "True Match");
        var assetsFrozen = results.Count(x => x.Disposition == "True Match" && x.RiskLevel is "critical" or "high");
        var flagged = confirmedMatches + potentialMatches;

        return new SanctionsTfsPreview
        {
            ScreeningCount = results.Count,
            MatchesFound = flagged,
            PotentialMatches = potentialMatches,
            ConfirmedMatches = confirmedMatches,
            FalsePositiveCount = 0,
            AssetsFrozenCount = assetsFrozen,
            FalsePositiveRatePercent = 0d,
            WatchlistSources = results
                .Where(x => x.Disposition != "Clear" && !string.Equals(x.SourceCode, "N/A", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.SourceCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            StrDraftRequired = confirmedMatches > 0,
            Narrative = confirmedMatches > 0
                ? "Confirmed screening hits should flow into TFS reporting and STR review before submission."
                : potentialMatches > 0
                    ? "Potential matches require analyst review before TFS metrics are finalized."
                    : "No reportable sanctions hits in the current screening set."
        };
    }

    private static string BuildKnowledgeNavigatorKey(string regulatoryReference, string returnCode, string fieldCode) =>
        $"{regulatoryReference}|{returnCode}|{fieldCode}";

    private static KnowledgeGraphCatalogMaterializationRequest BuildKnowledgeGraphCatalogRequest(
        IReadOnlyList<KnowledgeGraphOntologyCoverageRow> ontologyCoverageRows,
        IReadOnlyList<KnowledgeGraphRequirementRegisterRow> requirementRegisterRows,
        IReadOnlyList<KnowledgeGraphLineageRow> allLineageRows,
        IReadOnlyList<KnowledgeGraphObligationRow> obligationRows,
        IReadOnlyList<KnowledgeGraphInstitutionObligationRow> institutionObligationRows)
    {
        var regulatorInputs = ontologyCoverageRows
            .Select(row => new KnowledgeGraphCatalogRegulatorInput
            {
                RegulatorCode = row.RegulatorCode,
                DisplayName = row.RegulatorCode,
                ModuleCount = row.ModuleCount,
                RequirementCount = row.RequirementCount,
                InstitutionCount = row.InstitutionCount,
                ModuleCodes = allLineageRows
                    .Where(x => string.Equals(x.RegulatorCode, row.RegulatorCode, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.ModuleCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .ToList();

        var requirementInputs = requirementRegisterRows
            .Select(row => new KnowledgeGraphCatalogRequirementInput
            {
                RegulatoryReference = row.RegulatoryReference,
                RegulationFamily = row.RegulationFamily,
                RegulatorCode = row.RegulatorCode,
                ModuleCode = row.ModuleCode,
                FiledViaReturns = SplitCsvList(row.FiledViaReturns),
                InstitutionCount = row.InstitutionCount,
                FieldCount = row.FieldCount,
                FrequencyProfile = row.FrequencyProfile,
                NextDeadline = row.NextDeadline
            })
            .ToList();

        var lineageInputs = allLineageRows
            .Select(row => new KnowledgeGraphCatalogLineageInput
            {
                RegulatorCode = row.RegulatorCode,
                ModuleCode = row.ModuleCode,
                ReturnCode = row.ReturnCode,
                TemplateName = row.TemplateName,
                FieldName = row.FieldName,
                FieldCode = row.FieldCode,
                RegulatoryReference = row.RegulatoryReference
            })
            .ToList();

        var obligationInputs = obligationRows
            .Select(row => new KnowledgeGraphCatalogObligationInput
            {
                LicenceType = row.LicenceType,
                RegulatorCode = row.RegulatorCode,
                ModuleCode = row.ModuleCode,
                ReturnCode = row.ReturnCode,
                Frequency = row.Frequency,
                NextDeadline = row.NextDeadline
            })
            .ToList();

        var institutionObligationInputs = institutionObligationRows
            .Select(row => new KnowledgeGraphCatalogInstitutionObligationInput
            {
                InstitutionKey = row.InstitutionId.ToString(CultureInfo.InvariantCulture),
                InstitutionName = row.InstitutionName,
                LicenceType = row.LicenceType,
                RegulatorCode = row.RegulatorCode,
                ReturnCode = row.ReturnCode,
                Status = row.Status,
                NextDeadline = row.NextDeadline
            })
            .ToList();

        return new KnowledgeGraphCatalogMaterializationRequest
        {
            Regulators = regulatorInputs,
            Requirements = requirementInputs,
            Lineage = lineageInputs,
            Obligations = obligationInputs,
            InstitutionObligations = institutionObligationInputs
        };
    }

    private static SanctionsCatalogMaterializationRequest BuildSanctionsCatalogRequest()
    {
        return new SanctionsCatalogMaterializationRequest
        {
            Sources = WatchlistSources
                .Select(x => new SanctionsCatalogSourceInput
                {
                    SourceCode = x.Code,
                    SourceName = x.Name,
                    RefreshCadence = x.RefreshCadence,
                    Status = x.Status
                })
                .ToList(),
            Entries = WatchlistEntries
                .Select(x => new SanctionsCatalogEntryInput
                {
                    SourceCode = x.SourceCode,
                    PrimaryName = x.PrimaryName,
                    Aliases = x.Aliases.ToList(),
                    Category = x.Category,
                    RiskLevel = x.RiskLevel
                })
                .ToList()
        };
    }

    private static List<string> SplitCsvList(string value) =>
        value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string BuildResilienceIncidentKey(DataBreachIncident incident) =>
        $"{incident.Id}|{incident.DetectedAt:O}";

    private static List<DependencyHotspotRow> BuildDependencyHotspots(
        IReadOnlyList<CyberAsset> assets,
        IReadOnlyList<CyberAssetDependency> dependencies)
    {
        var assetById = assets.ToDictionary(x => x.Id);

        return dependencies
            .GroupBy(x => x.DependsOnAssetId)
            .Select(g =>
            {
                assetById.TryGetValue(g.Key, out var asset);
                return new DependencyHotspotRow
                {
                    AssetName = asset?.DisplayName ?? "Unknown dependency",
                    AssetType = asset?.AssetType ?? "Unknown",
                    Criticality = asset?.Criticality ?? "unknown",
                    DependentCount = g.Select(x => x.AssetId).Distinct().Count()
                };
            })
            .OrderByDescending(x => x.DependentCount)
            .ThenByDescending(x => CriticalityRank(x.Criticality))
            .ToList();
    }

    private static List<ImportantBusinessServiceRow> BuildImportantBusinessServices(
        IReadOnlyList<CyberAsset> cyberAssets,
        IReadOnlyList<CyberAssetDependency> dependencies,
        IReadOnlyList<DataBreachIncident> incidents,
        IReadOnlyList<SecurityAlert> securityAlerts,
        IReadOnlyList<ResilienceTestingRow> resilienceTests)
    {
        var dependencyCounts = dependencies
            .GroupBy(x => x.DependsOnAssetId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.AssetId).Distinct().Count());

        var candidateAssets = cyberAssets
            .Where(asset => IsCriticalAsset(asset.Criticality) || dependencyCounts.GetValueOrDefault(asset.Id) >= 2)
            .OrderByDescending(asset => CriticalityRank(asset.Criticality))
            .ThenByDescending(asset => dependencyCounts.GetValueOrDefault(asset.Id))
            .ThenBy(asset => asset.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        if (candidateAssets.Count == 0)
        {
            candidateAssets = cyberAssets
                .OrderByDescending(asset => CriticalityRank(asset.Criticality))
                .ThenBy(asset => asset.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();
        }

        return candidateAssets
            .Select(asset =>
            {
                var serviceName = ResolveImportantBusinessServiceName(asset);
                var dependencyCount = dependencyCounts.GetValueOrDefault(asset.Id);
                var relatedSignals = CountRelatedServiceSignals(asset, incidents, securityAlerts);
                var linkedTests = FindLinkedResilienceTests(serviceName, asset.AssetType, resilienceTests);
                var latestTest = linkedTests
                    .OrderByDescending(x => x.LastRunAt ?? DateTime.MinValue)
                    .FirstOrDefault();
                var maximumDisruptionMinutes = EstimateMaximumDisruptionMinutes(asset.Criticality, dependencyCount);
                var recoveryTargetMinutes = Math.Max(30, (int)Math.Round(maximumDisruptionMinutes * 0.75m, MidpointRounding.AwayFromZero));

                return new ImportantBusinessServiceRow
                {
                    AssetId = asset.Id,
                    ServiceName = serviceName,
                    ServiceOwner = ResolveBusinessServiceOwner(asset.AssetType),
                    SupportingAsset = asset.DisplayName,
                    AssetType = asset.AssetType,
                    Criticality = Capitalize(asset.Criticality),
                    DependencyCount = dependencyCount,
                    EventSignalCount = relatedSignals,
                    MaximumDisruptionMinutes = maximumDisruptionMinutes,
                    RecoveryTargetMinutes = recoveryTargetMinutes,
                    LatestTestOutcome = latestTest?.Outcome ?? "Awaiting Run",
                    Status = ResolveImportantServiceStatus(asset.Criticality, dependencyCount, relatedSignals, latestTest?.Outcome),
                    Commentary = $"{dependencyCount} mapped dependent path(s), {relatedSignals} incident or alert signal(s), {linkedTests.Count} linked resilience test(s)."
                };
            })
            .OrderByDescending(x => CriticalityRank(x.Criticality))
            .ThenByDescending(x => x.DependencyCount)
            .ThenBy(x => x.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<ThirdPartyProviderRiskRow> BuildThirdPartyProviderRiskRows(
        IReadOnlyList<CyberAsset> cyberAssets,
        IReadOnlyList<CyberAssetDependency> dependencies,
        IReadOnlyList<ImportantBusinessServiceRow> services,
        IReadOnlyList<ResilienceTestingRow> resilienceTests)
    {
        var dependentAssetsByProvider = dependencies
            .GroupBy(x => x.DependsOnAssetId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.AssetId).Distinct().ToHashSet());

        var candidateAssets = cyberAssets
            .Select(asset => new
            {
                Asset = asset,
                ProviderName = ResolveThirdPartyProviderName(asset),
                ProviderType = ResolveThirdPartyProviderType(asset)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.ProviderName))
            .ToList();

        return candidateAssets
            .GroupBy(x => x.ProviderName!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var assetIds = group.Select(x => x.Asset.Id).ToHashSet();
                var dependentAssetIds = group
                    .SelectMany(x => dependentAssetsByProvider.GetValueOrDefault(x.Asset.Id) ?? [])
                    .ToHashSet();

                var linkedServices = services
                    .Where(service => assetIds.Contains(service.AssetId) || dependentAssetIds.Contains(service.AssetId))
                    .GroupBy(service => service.AssetId)
                    .Select(x => x.First())
                    .OrderByDescending(x => CriticalityRank(x.Criticality))
                    .ThenByDescending(x => x.DependencyCount)
                    .ThenBy(x => x.ServiceName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var providerAssets = group
                    .Select(x => x.Asset.DisplayName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var latestTestOutcome = ResolveThirdPartyLatestOutcome(linkedServices, resilienceTests);
                var criticalServiceCount = linkedServices.Count(x => CriticalityRank(x.Criticality) >= 2);
                var signal = ResolveThirdPartySignal(criticalServiceCount, linkedServices.Count, dependentAssetIds.Count, latestTestOutcome);

                return new ThirdPartyProviderRiskRow
                {
                    ProviderName = group.Key,
                    ProviderType = group
                        .Select(x => x.ProviderType)
                        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                        ?? "Third-Party Service",
                    CriticalServiceCount = criticalServiceCount,
                    ServiceCount = linkedServices.Count,
                    DependentAssetCount = dependentAssetIds.Count,
                    HighestCriticality = linkedServices.Count == 0
                        ? group
                            .OrderByDescending(x => CriticalityRank(x.Asset.Criticality))
                            .Select(x => Capitalize(x.Asset.Criticality))
                            .FirstOrDefault() ?? "Medium"
                        : linkedServices
                            .OrderByDescending(x => CriticalityRank(x.Criticality))
                            .Select(x => x.Criticality)
                            .First(),
                    LatestTestOutcome = latestTestOutcome,
                    Signal = signal,
                    ProviderAssets = providerAssets,
                    LinkedServices = linkedServices.Select(x => x.ServiceName).Take(6).ToList(),
                    Commentary = BuildThirdPartyCommentary(group.Key, linkedServices, dependentAssetIds.Count, latestTestOutcome),
                    RecommendedAction = signal switch
                    {
                        "Critical" => "Design alternate provider capacity, validate exit paths, and run failover testing now.",
                        "Watch" => "Confirm concentration limits, current contracts, and schedule a targeted third-party resilience exercise.",
                        _ => "Maintain ongoing vendor monitoring and refresh continuity evidence."
                    }
                };
            })
            .Where(x => x.ServiceCount > 0 || x.DependentAssetCount >= 2)
            .OrderByDescending(x => CriticalityRank(x.HighestCriticality))
            .ThenByDescending(x => x.Signal == "Critical")
            .ThenByDescending(x => x.Signal == "Watch")
            .ThenByDescending(x => x.CriticalServiceCount)
            .ThenByDescending(x => x.DependentAssetCount)
            .ThenBy(x => x.ProviderName, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static List<ImpactToleranceRow> BuildImpactToleranceRows(
        IReadOnlyList<ImportantBusinessServiceRow> services)
    {
        return services
            .Select(service =>
            {
                var signal = service.Status switch
                {
                    "Attention Required" => "Critical",
                    "Watch" => "Watch",
                    _ => "Current"
                };

                return new ImpactToleranceRow
                {
                    ServiceName = service.ServiceName,
                    MaximumDisruptionMinutes = service.MaximumDisruptionMinutes,
                    RecoveryTargetMinutes = service.RecoveryTargetMinutes,
                    DependencyCount = service.DependencyCount,
                    LatestTestOutcome = service.LatestTestOutcome,
                    Signal = signal,
                    Commentary = service.Status == "Attention Required"
                        ? "Tolerance is at risk because recent signal quality or resilience evidence is below target."
                        : service.Status == "Watch"
                            ? "Tolerance remains within range but needs closer resilience testing and dependency follow-up."
                            : "Current tolerance and recovery objectives remain inside the expected resilience posture."
                };
            })
            .OrderByDescending(x => x.Signal == "Critical")
            .ThenByDescending(x => x.Signal == "Watch")
            .ThenBy(x => x.MaximumDisruptionMinutes)
            .ToList();
    }

    private static List<BusinessContinuityPlanRow> BuildBusinessContinuityPlanRows(
        IReadOnlyList<ImportantBusinessServiceRow> services,
        IReadOnlyList<ResilienceTestingRow> resilienceTests,
        IReadOnlyList<ResilienceActionRow> resilienceActions)
    {
        return services
            .Select(service =>
            {
                var linkedTests = FindLinkedResilienceTests(service.ServiceName, service.AssetType, resilienceTests)
                    .OrderByDescending(x => x.LastRunAt ?? DateTime.MinValue)
                    .ToList();

                var latestTest = linkedTests.FirstOrDefault();
                var continuityActions = resilienceActions.Count(action =>
                    action.Workstream is "Incident" or "Concentration Test"
                    && (action.Title.Contains(service.ServiceName, StringComparison.OrdinalIgnoreCase)
                        || service.Commentary.Contains(action.Title, StringComparison.OrdinalIgnoreCase)));

                var reviewCadenceDays = CriticalityRank(service.Criticality) >= 3 ? 90 : 120;
                var lastExercisedAt = latestTest?.LastRunAt;
                var nextReviewDue = (lastExercisedAt ?? DateTime.UtcNow.Date.AddDays(-reviewCadenceDays)).AddDays(reviewCadenceDays);

                var status = service.Status == "Attention Required"
                             || latestTest?.Outcome is "Weaknesses Found" or "Control Gap" or "Escalate"
                             || (!lastExercisedAt.HasValue && CriticalityRank(service.Criticality) >= 2)
                    ? "Critical"
                    : service.Status == "Watch"
                      || latestTest?.Outcome is "Awaiting Run" or "In Progress"
                      || nextReviewDue < DateTime.UtcNow
                        ? "Watch"
                        : "Current";

                return new BusinessContinuityPlanRow
                {
                    ServiceName = service.ServiceName,
                    OwnerLane = service.ServiceOwner,
                    RecoveryTargetMinutes = service.RecoveryTargetMinutes,
                    MaximumDisruptionMinutes = service.MaximumDisruptionMinutes,
                    Status = status,
                    LastExercisedAt = lastExercisedAt,
                    NextReviewDue = nextReviewDue,
                    LatestExerciseOutcome = latestTest?.Outcome ?? service.LatestTestOutcome,
                    OpenActionCount = continuityActions,
                    Commentary = lastExercisedAt.HasValue
                        ? $"Latest continuity evidence ran on {lastExercisedAt.Value:dd MMM yyyy}; {continuityActions} linked remediation action(s) remain open."
                        : $"No recent continuity exercise is attached; {continuityActions} linked remediation action(s) remain open."
                };
            })
            .OrderByDescending(x => x.Status == "Critical")
            .ThenByDescending(x => x.Status == "Watch")
            .ThenBy(x => x.NextReviewDue)
            .ThenBy(x => x.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<ChangeManagementControlRow> BuildResilienceChangeManagementControlRows(
        IReadOnlyList<AuditLogEntry> auditLog,
        IReadOnlyList<FieldChangeHistory> fieldChanges)
    {
        var windowStart = DateTime.UtcNow.AddDays(-30);
        var recentAudit = auditLog.Where(x => x.PerformedAt >= windowStart).ToList();
        var recentFieldChanges = fieldChanges.Where(x => x.ChangedAt >= windowStart).ToList();

        var categories = new[]
        {
            new
            {
                Area = "Template and Schema Governance",
                AuditEntries = recentAudit.Where(x =>
                    x.EntityType.Contains("template", StringComparison.OrdinalIgnoreCase)
                    || x.EntityType.Contains("version", StringComparison.OrdinalIgnoreCase)
                    || x.EntityType.Contains("ddl", StringComparison.OrdinalIgnoreCase)
                    || x.EntityType.Contains("migration", StringComparison.OrdinalIgnoreCase)).ToList(),
                FieldEntries = new List<FieldChangeHistory>(),
                Action = "Confirm release evidence, rollback readiness, and published-version sign-off for structural changes."
            },
            new
            {
                Area = "Validation Logic Controls",
                AuditEntries = recentAudit.Where(x =>
                    x.EntityType.Contains("formula", StringComparison.OrdinalIgnoreCase)
                    || x.EntityType.Contains("rule", StringComparison.OrdinalIgnoreCase)).ToList(),
                FieldEntries = new List<FieldChangeHistory>(),
                Action = "Validate rule changes with impact analysis, parallel evidence, and reviewer sign-off."
            },
            new
            {
                Area = "Submission and Data Capture Controls",
                AuditEntries = recentAudit.Where(x =>
                    x.EntityType.Contains("submission", StringComparison.OrdinalIgnoreCase)
                    || x.EntityType.Contains("return", StringComparison.OrdinalIgnoreCase)
                    || x.EntityType.Contains("data", StringComparison.OrdinalIgnoreCase)).ToList(),
                FieldEntries = recentFieldChanges.ToList(),
                Action = "Challenge unexpected field-level changes, manual overrides, and unstable ingestion paths."
            },
            new
            {
                Area = "Security and Access Controls",
                AuditEntries = recentAudit.Where(x =>
                    x.EntityType.Contains("user", StringComparison.OrdinalIgnoreCase)
                    || x.EntityType.Contains("role", StringComparison.OrdinalIgnoreCase)
                    || x.EntityType.Contains("permission", StringComparison.OrdinalIgnoreCase)
                    || x.EntityType.Contains("auth", StringComparison.OrdinalIgnoreCase)
                    || x.EntityType.Contains("mfa", StringComparison.OrdinalIgnoreCase)).ToList(),
                FieldEntries = new List<FieldChangeHistory>(),
                Action = "Review privileged changes, role updates, and emergency access events against approval evidence."
            },
            new
            {
                Area = "Infrastructure and Resilience Controls",
                AuditEntries = recentAudit.Where(x =>
                    x.EntityType.Contains("cyber", StringComparison.OrdinalIgnoreCase)
                    || x.EntityType.Contains("data_source", StringComparison.OrdinalIgnoreCase)
                    || x.EntityType.Contains("datasource", StringComparison.OrdinalIgnoreCase)
                    || x.EntityType.Contains("webhook", StringComparison.OrdinalIgnoreCase)
                    || x.EntityType.Contains("integration", StringComparison.OrdinalIgnoreCase)
                    || x.EntityType.Contains("partner", StringComparison.OrdinalIgnoreCase)).ToList(),
                FieldEntries = new List<FieldChangeHistory>(),
                Action = "Confirm infrastructure changes have continuity testing, fallback paths, and recorded ownership."
            }
        };

        return categories
            .Select(category =>
            {
                var latestAudit = category.AuditEntries.OrderByDescending(x => x.PerformedAt).FirstOrDefault();
                var latestField = category.FieldEntries.OrderByDescending(x => x.ChangedAt).FirstOrDefault();
                var latestChangedAt = new[] { latestAudit?.PerformedAt, latestField?.ChangedAt }
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .DefaultIfEmpty()
                    .Max();

                var emergencyAudits = category.AuditEntries.Count(x =>
                    x.Action.Contains("delete", StringComparison.OrdinalIgnoreCase)
                    || x.Action.Contains("rollback", StringComparison.OrdinalIgnoreCase)
                    || x.Action.Contains("override", StringComparison.OrdinalIgnoreCase)
                    || x.Action.Contains("emergency", StringComparison.OrdinalIgnoreCase));

                var systemOverrides = category.FieldEntries.Count(x =>
                    x.ChangeSource.Equals("System", StringComparison.OrdinalIgnoreCase)
                    || x.ChangeSource.Equals("Computed", StringComparison.OrdinalIgnoreCase));

                var status = emergencyAudits + systemOverrides >= 4
                             || (latestChangedAt != default && latestChangedAt >= DateTime.UtcNow.AddDays(-7) && emergencyAudits > 0)
                    ? "Critical"
                    : emergencyAudits + systemOverrides > 0
                      || category.AuditEntries.Count + category.FieldEntries.Count >= 20
                        ? "Watch"
                        : "Current";

                return new ChangeManagementControlRow
                {
                    ControlArea = category.Area,
                    ChangeCount = category.AuditEntries.Count + category.FieldEntries.Count,
                    ElevatedChangeCount = emergencyAudits + systemOverrides,
                    LatestChangeAt = latestChangedAt == default ? null : latestChangedAt,
                    Status = status,
                    Commentary = category.Area switch
                    {
                        "Submission and Data Capture Controls" => $"{category.FieldEntries.Count} field-level changes and {category.AuditEntries.Count} related audit event(s) were recorded in the last 30 days.",
                        _ => $"{category.AuditEntries.Count} relevant audit event(s) were recorded in the last 30 days."
                    },
                    RecommendedAction = category.Action
                };
            })
            .OrderByDescending(x => x.Status == "Critical")
            .ThenByDescending(x => x.Status == "Watch")
            .ThenByDescending(x => x.ElevatedChangeCount)
            .ThenByDescending(x => x.ChangeCount)
            .ToList();
    }

    private static List<RecoveryTimeTestingRow> BuildRecoveryTimeTestingRows(
        IReadOnlyList<ImportantBusinessServiceRow> services,
        IReadOnlyList<ResilienceTestingRow> resilienceTests,
        IReadOnlyList<ResilienceIncidentTimelineRow> incidentTimelines)
    {
        return services
            .Select(service =>
            {
                var linkedTests = FindLinkedResilienceTests(service.ServiceName, service.AssetType, resilienceTests)
                    .Where(x => x.TestType is "Business Continuity" or "Recovery" or "Operational Resilience" or "Cyber Resilience")
                    .OrderByDescending(x => x.LastRunAt ?? DateTime.MinValue)
                    .ToList();

                var latestTest = linkedTests.FirstOrDefault();
                var matchTokens = ResolveMatchTokens(service.ServiceName, service.SupportingAsset);
                var incidentTimeline = incidentTimelines
                    .Where(x => matchTokens.Any(token => x.Title.Contains(token, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(x => x.DetectionToRecoverHours.HasValue)
                    .ThenByDescending(x => x.RcaGeneratedAt ?? DateTime.MinValue)
                    .FirstOrDefault();

                var incidentRecoveryMinutes = incidentTimeline?.DetectionToRecoverHours.HasValue == true
                    ? (int?)Math.Round(incidentTimeline.DetectionToRecoverHours.Value * 60m, MidpointRounding.AwayFromZero)
                    : null;

                var proxyRecoveryMinutes = incidentRecoveryMinutes.HasValue
                    ? incidentRecoveryMinutes
                    : EstimateRecoveryProxyMinutes(service.RecoveryTargetMinutes, latestTest?.Outcome, latestTest?.Status);

                var evidenceSource = incidentRecoveryMinutes.HasValue
                    ? "Observed incident recovery"
                    : latestTest?.LastRunAt.HasValue == true
                        ? "Scenario recovery proxy"
                        : "Tolerance only";

                var latestOutcome = incidentRecoveryMinutes.HasValue
                    ? incidentTimeline?.CurrentPhase ?? "Recovered"
                    : latestTest?.Outcome ?? "Awaiting Evidence";

                var lastEvidenceAt = incidentTimeline?.Steps
                    .FirstOrDefault(x => x.Stage == "Recovered")
                    ?.OccurredAt ?? latestTest?.LastRunAt;

                var cadenceDays = CriticalityRank(service.Criticality) >= 3 ? 90 : 120;
                var nextTestDue = (lastEvidenceAt ?? DateTime.UtcNow.Date.AddDays(-cadenceDays)).AddDays(cadenceDays);
                var varianceMinutes = proxyRecoveryMinutes.HasValue
                    ? (int?)(proxyRecoveryMinutes.Value - service.RecoveryTargetMinutes)
                    : null;

                var signal = incidentTimeline?.CurrentPhase == "Recovery Open"
                             || latestOutcome is "Weaknesses Found" or "Control Gap" or "Escalate"
                             || (varianceMinutes.HasValue && varianceMinutes.Value > Math.Max(45, service.RecoveryTargetMinutes / 4))
                    ? "Breach"
                    : !lastEvidenceAt.HasValue
                      || nextTestDue < DateTime.UtcNow
                      || latestOutcome is "Awaiting Run" or "In Progress" or "Awaiting Evidence"
                      || (varianceMinutes.HasValue && varianceMinutes.Value > 0)
                        ? "Watch"
                        : "Current";

                var commentary = evidenceSource switch
                {
                    "Observed incident recovery" => $"Recovery evidence came from incident response telemetry against an RTO of {service.RecoveryTargetMinutes} minutes.",
                    "Scenario recovery proxy" => $"Recovery evidence is inferred from the latest resilience exercise outcome against an RTO of {service.RecoveryTargetMinutes} minutes.",
                    _ => $"No direct recovery evidence is attached yet; the service is currently running on its declared tolerance profile."
                };

                return new RecoveryTimeTestingRow
                {
                    ServiceName = service.ServiceName,
                    OwnerLane = service.ServiceOwner,
                    RecoveryTargetMinutes = service.RecoveryTargetMinutes,
                    MaximumDisruptionMinutes = service.MaximumDisruptionMinutes,
                    TestedRecoveryMinutes = proxyRecoveryMinutes,
                    VarianceMinutes = varianceMinutes,
                    LatestOutcome = latestOutcome,
                    EvidenceSource = evidenceSource,
                    LastEvidenceAt = lastEvidenceAt,
                    NextTestDue = nextTestDue,
                    Signal = signal,
                    Commentary = commentary,
                    RecommendedAction = signal switch
                    {
                        "Breach" => "Run targeted recovery challenge now, validate failover evidence, and escalate remediation through board oversight.",
                        "Watch" => "Refresh recovery testing evidence before the next review date and confirm service recovery runbooks remain current.",
                        _ => "Maintain current recovery cadence and include the service in the next scheduled resilience review."
                    }
                };
            })
            .OrderByDescending(x => x.Signal == "Breach")
            .ThenByDescending(x => x.Signal == "Watch")
            .ThenBy(x => x.NextTestDue)
            .ThenBy(x => x.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<CyberResilienceAssessmentRow> BuildCyberResilienceAssessmentRows(
        IReadOnlyList<CyberAsset> cyberAssets,
        IReadOnlyList<ImportantBusinessServiceRow> services,
        IReadOnlyList<SecurityAlert> securityAlerts,
        IReadOnlyList<DependencyHotspotRow> hotspots,
        IReadOnlyList<ResilienceTestingRow> resilienceTests,
        IReadOnlyList<ResilienceIncidentTimelineRow> incidentTimelines,
        IReadOnlyList<RecoveryTimeTestingRow> recoveryTimeTests)
    {
        var criticalAssets = cyberAssets.Where(x => IsCriticalAsset(x.Criticality)).ToList();
        var openAlerts = securityAlerts
            .Where(x => x.Status.Equals("open", StringComparison.OrdinalIgnoreCase)
                     || x.Status.Equals("investigating", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var cyberTests = resilienceTests
            .Where(x => x.TestType.Equals("Cyber Resilience", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.LastRunAt ?? DateTime.MinValue)
            .ToList();

        var latestCyberTest = cyberTests.FirstOrDefault();
        var coveredCriticalAssets = criticalAssets.Count(asset => services.Any(service => service.AssetId == asset.Id));

        var authAssetCount = cyberAssets.Count(asset => MatchesAnyToken($"{asset.DisplayName} {asset.AssetType}", "identity", "auth", "mfa", "directory", "access", "iam"));
        var authAlertCount = openAlerts.Count(alert => MatchesAnyToken($"{alert.AlertType} {alert.Title} {alert.Description}", "identity", "auth", "mfa", "access", "privilege", "login", "credential"));
        var authCriticalCount = openAlerts.Count(alert =>
            alert.Severity is "critical" or "high"
            && MatchesAnyToken($"{alert.AlertType} {alert.Title} {alert.Description}", "identity", "auth", "mfa", "access", "privilege", "login", "credential"));

        var networkHotspots = hotspots.Count(x => MatchesAnyToken($"{x.AssetName} {x.AssetType}", "network", "firewall", "gateway", "switch", "edge", "perimeter", "security"));
        var networkAlertCount = openAlerts.Count(alert => MatchesAnyToken($"{alert.AlertType} {alert.Title} {alert.Description}", "network", "ddos", "gateway", "switch", "firewall", "perimeter", "latency", "availability"));

        var detectionCriticalCount = openAlerts.Count(x => x.Severity is "critical" or "high");
        var detectionOpenCount = openAlerts.Count;

        var recoveryBreachCount = recoveryTimeTests.Count(x => x.Signal == "Breach");
        var recoveryWatchCount = recoveryTimeTests.Count(x => x.Signal == "Watch");
        var recoveryAssetCount = cyberAssets.Count(asset => MatchesAnyToken($"{asset.DisplayName} {asset.AssetType}", "backup", "recovery", "storage", "archive", "data centre", "data center", "replication"));

        var openIncidents = incidentTimelines.Count(x => x.CurrentPhase != "Closed");
        var openCriticalIncidents = incidentTimelines.Count(x => x.CurrentPhase != "Closed" && (x.Severity is "CRITICAL" or "HIGH"));
        var missingRcaCount = incidentTimelines.Count(x => !x.RcaGeneratedAt.HasValue);

        var rows = new List<CyberResilienceAssessmentRow>();

        var assetCoverageScore = criticalAssets.Count == 0
            ? 60m
            : Math.Round(Math.Clamp(coveredCriticalAssets / (decimal)criticalAssets.Count * 100m, 25m, 100m), 1);
        rows.Add(new CyberResilienceAssessmentRow
        {
            Domain = "Asset Security Coverage",
            Score = assetCoverageScore,
            Signal = assetCoverageScore < 55m || criticalAssets.Count - coveredCriticalAssets >= 3 ? "Critical" : assetCoverageScore < 75m ? "Watch" : "Current",
            EvidenceCount = criticalAssets.Count,
            LeadIndicator = $"{coveredCriticalAssets} of {criticalAssets.Count} critical asset(s) are mapped into important-service oversight.",
            Commentary = criticalAssets.Count == 0
                ? "No critical cyber assets are currently classified, so coverage confidence is limited."
                : $"{criticalAssets.Count - coveredCriticalAssets} critical asset(s) remain outside the current service-oversight mapping.",
            RecommendedAction = "Complete cyber asset-to-service mapping and challenge any unmanaged critical assets in the next resilience review."
        });

        var identityScore = Math.Round(Math.Clamp(88m - (authCriticalCount * 16m) - ((authAlertCount - authCriticalCount) * 7m) - (authAssetCount == 0 ? 10m : 0m), 20m, 100m), 1);
        rows.Add(new CyberResilienceAssessmentRow
        {
            Domain = "Identity & Access Controls",
            Score = identityScore,
            Signal = authCriticalCount > 0 || identityScore < 55m ? "Critical" : authAlertCount > 0 || identityScore < 75m ? "Watch" : "Current",
            EvidenceCount = authAlertCount,
            LeadIndicator = $"{authAssetCount} identity-linked asset(s) and {authAlertCount} open access-control alert(s) are currently tracked.",
            Commentary = authAlertCount == 0
                ? "No open identity or privilege-related alerts are currently outstanding."
                : $"{authCriticalCount} high-severity access-control alert(s) remain open and require control validation.",
            RecommendedAction = "Review MFA, privileged access, and credential-control evidence against current alert activity."
        });

        var detectionScore = Math.Round(Math.Clamp(96m - (detectionCriticalCount * 14m) - ((detectionOpenCount - detectionCriticalCount) * 5m) - (latestCyberTest?.Outcome is "Weaknesses Found" or "Control Gap" or "Escalate" ? 12m : 0m), 20m, 100m), 1);
        rows.Add(new CyberResilienceAssessmentRow
        {
            Domain = "Threat Detection & Monitoring",
            Score = detectionScore,
            Signal = detectionCriticalCount > 0 || detectionScore < 55m ? "Critical" : detectionOpenCount > 0 || detectionScore < 75m ? "Watch" : "Current",
            EvidenceCount = detectionOpenCount,
            LeadIndicator = $"{detectionOpenCount} open security alert(s) and {cyberTests.Count} cyber-resilience test record(s) shape this domain.",
            Commentary = latestCyberTest is null
                ? "No dedicated cyber-resilience scenario has been run recently."
                : $"Latest cyber assessment outcome is {latestCyberTest.Outcome} with status {latestCyberTest.Status}.",
            RecommendedAction = "Tune monitoring coverage, close outstanding high-severity alerts, and rerun cyber scenario testing where evidence is stale."
        });

        var networkScore = Math.Round(Math.Clamp(92m - (networkHotspots * 12m) - (networkAlertCount * 7m), 20m, 100m), 1);
        rows.Add(new CyberResilienceAssessmentRow
        {
            Domain = "Network & Perimeter Resilience",
            Score = networkScore,
            Signal = networkHotspots >= 2 || networkScore < 55m ? "Critical" : networkHotspots > 0 || networkAlertCount > 0 || networkScore < 75m ? "Watch" : "Current",
            EvidenceCount = networkHotspots + networkAlertCount,
            LeadIndicator = $"{networkHotspots} network/perimeter hotspot(s) and {networkAlertCount} related alert(s) are active.",
            Commentary = networkHotspots == 0
                ? "No material network concentration hotspot is currently highlighted."
                : "Perimeter and network dependency concentration requires failover and isolation evidence.",
            RecommendedAction = "Challenge perimeter redundancy, alternate routing, and isolation controls on concentrated network paths."
        });

        var recoveryScore = Math.Round(Math.Clamp(94m - (recoveryBreachCount * 18m) - (recoveryWatchCount * 8m) - (recoveryAssetCount == 0 ? 8m : 0m), 20m, 100m), 1);
        rows.Add(new CyberResilienceAssessmentRow
        {
            Domain = "Recovery & Data Protection",
            Score = recoveryScore,
            Signal = recoveryBreachCount > 0 || recoveryScore < 55m ? "Critical" : recoveryWatchCount > 0 || recoveryScore < 75m ? "Watch" : "Current",
            EvidenceCount = recoveryBreachCount + recoveryWatchCount,
            LeadIndicator = $"{recoveryBreachCount} recovery breach(es), {recoveryWatchCount} watch item(s), and {recoveryAssetCount} recovery-linked asset(s) are tracked.",
            Commentary = recoveryBreachCount == 0
                ? "Recovery-time evidence is broadly within tolerance or on watch only."
                : "At least one service has breached its stated recovery target and needs targeted remediation.",
            RecommendedAction = "Validate backup, failover, and recovery execution evidence against declared RTO and data-protection objectives."
        });

        var incidentScore = Math.Round(Math.Clamp(95m - (openCriticalIncidents * 16m) - ((openIncidents - openCriticalIncidents) * 7m) - (missingRcaCount * 5m), 20m, 100m), 1);
        rows.Add(new CyberResilienceAssessmentRow
        {
            Domain = "Incident Response & RCA",
            Score = incidentScore,
            Signal = openCriticalIncidents > 0 || incidentScore < 55m ? "Critical" : openIncidents > 0 || missingRcaCount > 0 || incidentScore < 75m ? "Watch" : "Current",
            EvidenceCount = openIncidents,
            LeadIndicator = $"{openIncidents} open incident(s) and {missingRcaCount} missing RCA artifact(s) are currently recorded.",
            Commentary = openIncidents == 0
                ? "No active cyber incident is currently open."
                : "Open cyber incidents or missing RCA evidence are reducing confidence in operational containment discipline.",
            RecommendedAction = "Close containment gaps, complete RCA on unresolved cases, and feed recommendations into the resilience action queue."
        });

        return rows
            .OrderByDescending(x => x.Signal == "Critical")
            .ThenByDescending(x => x.Signal == "Watch")
            .ThenBy(x => x.Score)
            .ToList();
    }

    private static List<OpsResilienceSheetRow> BuildOpsResilienceReturnPackRows(
        IReadOnlyList<ImportantBusinessServiceRow> businessServices,
        IReadOnlyList<ImpactToleranceRow> impactTolerances,
        IReadOnlyList<ResilienceTestingRow> resilienceTests,
        IReadOnlyList<ThirdPartyProviderRiskRow> thirdPartyRegister,
        IReadOnlyList<DataBreachIncident> incidents,
        IReadOnlyList<BusinessContinuityPlanRow> businessContinuityPlans,
        IReadOnlyList<CyberResilienceAssessmentRow> cyberAssessment,
        IReadOnlyList<ChangeManagementControlRow> changeManagementControls,
        IReadOnlyList<RecoveryTimeTestingRow> recoveryTimeTests,
        ResilienceBoardSummary boardSummary)
    {
        return
        [
            new()
            {
                SheetCode = "OPS-01",
                SheetName = "Important Business Services Inventory",
                RowCount = businessServices.Count,
                Signal = businessServices.Any(x => x.Status == "Attention Required") ? "Critical" : businessServices.Any(x => x.Status == "Watch") ? "Watch" : "Current",
                Coverage = $"{businessServices.Count} service row(s) with {businessServices.Count(x => x.Status == "Attention Required")} attention-required item(s).",
                Commentary = businessServices.Count == 0
                    ? "No important business services are currently derived from cyber assets and dependencies."
                    : $"{businessServices.Count(x => x.Criticality.Equals("critical", StringComparison.OrdinalIgnoreCase))} critical service(s) are represented in the inventory.",
                RecommendedAction = "Review service ownership, criticality, and supporting asset mapping before publishing the inventory sheet."
            },
            new()
            {
                SheetCode = "OPS-02",
                SheetName = "Impact Tolerance Definitions",
                RowCount = impactTolerances.Count,
                Signal = impactTolerances.Any(x => x.Signal == "Critical") ? "Critical" : impactTolerances.Any(x => x.Signal == "Watch") ? "Watch" : "Current",
                Coverage = $"{impactTolerances.Count} tolerance row(s) with {impactTolerances.Count(x => x.Signal is "Critical" or "Watch")} watch item(s).",
                Commentary = impactTolerances.Count == 0
                    ? "No impact tolerances are currently attached to important services."
                    : $"{impactTolerances.Count(x => x.DependencyCount >= 3)} service(s) have elevated dependency concentration against their tolerance profile.",
                RecommendedAction = "Challenge declared maximum disruption and recovery targets against current dependency and testing evidence."
            },
            new()
            {
                SheetCode = "OPS-03",
                SheetName = "Scenario Testing Results",
                RowCount = resilienceTests.Count,
                Signal = resilienceTests.Any(x => x.Outcome is "Weaknesses Found" or "Control Gap" or "Escalate" || x.Status is "Overdue" or "Due")
                    ? "Critical"
                    : resilienceTests.Any(x => x.Status is "Running" or "Scheduled" || x.Outcome is "Awaiting Run" or "In Progress")
                        ? "Watch"
                        : "Current",
                Coverage = $"{resilienceTests.Count} test row(s); {resilienceTests.Count(x => x.Status is "Overdue" or "Due")} due or overdue.",
                Commentary = resilienceTests.Count == 0
                    ? "No resilience testing schedule is currently materialized."
                    : $"{resilienceTests.Count(x => x.Outcome is "Weaknesses Found" or "Control Gap")} test outcome(s) indicate control weaknesses.",
                RecommendedAction = "Refresh overdue scenarios and attach remediation evidence for any failed or weak outcomes."
            },
            new()
            {
                SheetCode = "OPS-04",
                SheetName = "ICT Third-Party Risk Register",
                RowCount = thirdPartyRegister.Count,
                Signal = thirdPartyRegister.Any(x => x.Signal == "Critical") ? "Critical" : thirdPartyRegister.Any(x => x.Signal == "Watch") ? "Watch" : "Current",
                Coverage = $"{thirdPartyRegister.Count} provider row(s) with {thirdPartyRegister.Count(x => x.Signal is "Critical" or "Watch")} concentration watch item(s).",
                Commentary = thirdPartyRegister.Count == 0
                    ? "No third-party provider rows are currently inferred from the dependency map."
                    : $"{thirdPartyRegister.Count(x => x.CriticalServiceCount >= 2)} provider(s) support multiple critical services.",
                RecommendedAction = "Validate provider concentration, substitution plans, and failover evidence before filing the ICT risk register."
            },
            new()
            {
                SheetCode = "OPS-05",
                SheetName = "Incident Management Reporting",
                RowCount = incidents.Count,
                Signal = incidents.Any(x => !x.RemediatedAt.HasValue && (x.Severity is DataBreachSeverity.CRITICAL or DataBreachSeverity.HIGH))
                    ? "Critical"
                    : incidents.Any(x => !x.RemediatedAt.HasValue)
                        ? "Watch"
                        : "Current",
                Coverage = $"{incidents.Count} incident row(s); {incidents.Count(x => !x.RemediatedAt.HasValue)} still open.",
                Commentary = incidents.Count == 0
                    ? "No operational resilience incidents are currently recorded."
                    : $"{incidents.Count(x => x.ContainedAt.HasValue && !x.RemediatedAt.HasValue)} incident(s) are contained but still under recovery.",
                RecommendedAction = "Confirm open incident lifecycle evidence, RCA linkage, and escalation history before publishing the incident sheet."
            },
            new()
            {
                SheetCode = "OPS-06",
                SheetName = "Business Continuity Plan Status",
                RowCount = businessContinuityPlans.Count,
                Signal = businessContinuityPlans.Any(x => x.Status == "Critical") ? "Critical" : businessContinuityPlans.Any(x => x.Status == "Watch") ? "Watch" : "Current",
                Coverage = $"{businessContinuityPlans.Count} BCP row(s) with {businessContinuityPlans.Count(x => x.Status is "Critical" or "Watch")} requiring challenge.",
                Commentary = businessContinuityPlans.Count == 0
                    ? "No business continuity plan rows are currently derived."
                    : $"{businessContinuityPlans.Count(x => !x.LastExercisedAt.HasValue)} service(s) lack recent exercise evidence.",
                RecommendedAction = "Update continuity review cadence, attach exercise evidence, and close overdue plan remediation items."
            },
            new()
            {
                SheetCode = "OPS-07",
                SheetName = "Cyber Resilience Assessment",
                RowCount = cyberAssessment.Count,
                Signal = cyberAssessment.Any(x => x.Signal == "Critical") ? "Critical" : cyberAssessment.Any(x => x.Signal == "Watch") ? "Watch" : "Current",
                Coverage = $"{cyberAssessment.Count} cyber domain row(s) with {cyberAssessment.Count(x => x.Signal == "Critical")} critical domain(s).",
                Commentary = cyberAssessment.Count == 0
                    ? "No cyber resilience domain assessment is currently derived."
                    : $"{cyberAssessment.OrderBy(x => x.Score).First().Domain} is the weakest current cyber domain.",
                RecommendedAction = "Review cyber assessment domains, challenge weak scores, and assign remediation to the relevant control owners."
            },
            new()
            {
                SheetCode = "OPS-08",
                SheetName = "Change Management Controls",
                RowCount = changeManagementControls.Count,
                Signal = changeManagementControls.Any(x => x.Status == "Critical") ? "Critical" : changeManagementControls.Any(x => x.Status == "Watch") ? "Watch" : "Current",
                Coverage = $"{changeManagementControls.Count} control row(s) with {changeManagementControls.Count(x => x.ElevatedChangeCount > 0)} elevated-change cluster(s).",
                Commentary = changeManagementControls.Count == 0
                    ? "No change-management evidence is currently derived from audit and field history."
                    : $"{changeManagementControls.Sum(x => x.ElevatedChangeCount)} elevated operational change(s) are in the current review window.",
                RecommendedAction = "Validate release governance, approval evidence, and rollback readiness for elevated operational changes."
            },
            new()
            {
                SheetCode = "OPS-09",
                SheetName = "Recovery Time Testing",
                RowCount = recoveryTimeTests.Count,
                Signal = recoveryTimeTests.Any(x => x.Signal == "Breach") ? "Critical" : recoveryTimeTests.Any(x => x.Signal == "Watch") ? "Watch" : "Current",
                Coverage = $"{recoveryTimeTests.Count} recovery row(s) with {recoveryTimeTests.Count(x => x.Signal == "Breach")} breach(es).",
                Commentary = recoveryTimeTests.Count == 0
                    ? "No recovery-time testing evidence is currently derived."
                    : $"{recoveryTimeTests.Count(x => !x.TestedRecoveryMinutes.HasValue)} service(s) still rely on tolerance-only evidence.",
                RecommendedAction = "Re-run recovery tests for breached services and attach dated recovery evidence against declared RTO targets."
            },
            new()
            {
                SheetCode = "OPS-10",
                SheetName = "Board Resilience Oversight",
                RowCount = 1,
                Signal = boardSummary.CriticalIssues > 0 ? "Critical" : boardSummary.OverdueActions > 0 || boardSummary.DueTests > 0 ? "Watch" : "Current",
                Coverage = $"{boardSummary.CriticalIssues} critical issue(s), {boardSummary.OverdueActions} overdue action(s), {boardSummary.DueTests} due test(s).",
                Commentary = boardSummary.Narrative,
                RecommendedAction = "Use the board oversight sheet to confirm escalations, action ownership, and upcoming test commitments."
            }
        ];
    }

    private static List<ResilienceIncidentTimelineRow> BuildResilienceIncidentTimelines(
        IReadOnlyList<DataBreachIncident> incidents,
        IReadOnlyList<RootCauseAnalysisRecord> rcaRecords)
    {
        return incidents
            .OrderByDescending(x => x.DetectedAt)
            .Take(12)
            .Select(incident =>
            {
                var rcaRecord = rcaRecords
                    .FirstOrDefault(x => string.Equals(x.IncidentId.ToString(), incident.Id.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
                    ?? rcaRecords.FirstOrDefault(x => x.RootCauseSummary.Contains(incident.Title, StringComparison.OrdinalIgnoreCase));

                var steps = new List<ResilienceIncidentTimelineStep>
                {
                    new()
                    {
                        Stage = "Detected",
                        State = "Complete",
                        OccurredAt = incident.DetectedAt,
                        ElapsedHours = 0m,
                        Commentary = "Incident entered the operational resilience workflow."
                    },
                    new()
                    {
                        Stage = "Contained",
                        State = incident.ContainedAt.HasValue ? "Complete" : "Open",
                        OccurredAt = incident.ContainedAt,
                        ElapsedHours = incident.ContainedAt.HasValue
                            ? Math.Round((decimal)(incident.ContainedAt.Value - incident.DetectedAt).TotalHours, 1)
                            : null,
                        Commentary = incident.ContainedAt.HasValue
                            ? "Containment controls were applied to stop the spread or further business impact."
                            : "Containment evidence is still outstanding."
                    },
                    new()
                    {
                        Stage = "Recovered",
                        State = incident.RemediatedAt.HasValue ? "Complete" : incident.ContainedAt.HasValue ? "In Progress" : "Open",
                        OccurredAt = incident.RemediatedAt,
                        ElapsedHours = incident.RemediatedAt.HasValue
                            ? Math.Round((decimal)(incident.RemediatedAt.Value - incident.DetectedAt).TotalHours, 1)
                            : null,
                        Commentary = incident.RemediatedAt.HasValue
                            ? "Recovery and remediation were recorded as complete."
                            : "Recovery remains open and should stay on the resilience action queue."
                    },
                    new()
                    {
                        Stage = "RCA",
                        State = rcaRecord is null ? "Open" : "Complete",
                        OccurredAt = rcaRecord?.GeneratedAt,
                        ElapsedHours = rcaRecord is null
                            ? null
                            : Math.Round((decimal)(rcaRecord.GeneratedAt - incident.DetectedAt).TotalHours, 1),
                        Commentary = rcaRecord is null
                            ? "Root-cause analysis has not yet been attached."
                            : "Root-cause analysis evidence is attached for board and examiner review."
                    }
                };

                var currentPhase = incident.RemediatedAt.HasValue
                    ? "Closed"
                    : incident.ContainedAt.HasValue
                        ? "Recovery Open"
                        : "Containment Pending";

                var narrative = currentPhase switch
                {
                    "Closed" => $"Lifecycle completed from detection through remediation in {steps[2].ElapsedHours?.ToString("0.0", CultureInfo.InvariantCulture) ?? "N/A"} hour(s).",
                    "Recovery Open" => "Incident is contained but recovery remains open; board oversight should track remediation evidence.",
                    _ => "Incident remains in early lifecycle stages and requires containment plus RCA attention."
                };

                return new ResilienceIncidentTimelineRow
                {
                    IncidentKey = BuildResilienceIncidentKey(incident),
                    Title = incident.Title,
                    Severity = incident.Severity.ToString(),
                    Status = incident.Status.ToString(),
                    CurrentPhase = currentPhase,
                    DetectionToContainHours = steps[1].ElapsedHours,
                    DetectionToRecoverHours = steps[2].ElapsedHours,
                    RcaGeneratedAt = rcaRecord?.GeneratedAt,
                    Narrative = narrative,
                    Steps = steps
                };
            })
            .ToList();
    }

    private static List<ResilienceActionRow> BuildResilienceActionRows(
        IReadOnlyList<DataBreachIncident> incidents,
        IReadOnlyList<SecurityAlert> securityAlerts,
        IReadOnlyList<DependencyHotspotRow> hotspots)
    {
        var incidentActions = incidents.Select(incident =>
        {
            var dueDate = incident.DetectedAt.AddDays(incident.Severity switch
            {
                DataBreachSeverity.CRITICAL => 3,
                DataBreachSeverity.HIGH => 7,
                DataBreachSeverity.MEDIUM => 14,
                _ => 21
            });

            return new ResilienceActionRow
            {
                Workstream = "Incident",
                Title = incident.Title,
                Action = incident.RemediatedAt.HasValue
                    ? "Validate closure evidence and update board oversight log."
                    : "Complete remediation and document recovery controls.",
                OwnerLane = incident.Severity is DataBreachSeverity.CRITICAL or DataBreachSeverity.HIGH
                    ? "Crisis / Technology"
                    : "Operational Risk",
                DueDate = dueDate,
                Status = incident.RemediatedAt.HasValue
                    ? "Complete"
                    : dueDate < DateTime.UtcNow
                        ? "Overdue"
                        : "Open",
                Severity = incident.Severity.ToString()
            };
        });

        var alertActions = securityAlerts
            .Where(x => x.Status.Equals("open", StringComparison.OrdinalIgnoreCase)
                        || x.Status.Equals("investigating", StringComparison.OrdinalIgnoreCase))
            .Take(4)
            .Select(alert => new ResilienceActionRow
            {
                Workstream = "Cyber Alert",
                Title = alert.Title,
                Action = "Validate compensating controls and confirm incident linkage or closure.",
                OwnerLane = "Cyber Defence",
                DueDate = alert.CreatedAt.AddDays(alert.Severity.Equals("critical", StringComparison.OrdinalIgnoreCase) ? 2 : 5),
                Status = alert.CreatedAt.AddDays(5) < DateTime.UtcNow ? "Overdue" : "Open",
                Severity = alert.Severity
            });

        var hotspotActions = hotspots
            .Where(x => x.DependentCount >= 2)
            .Take(4)
            .Select(hotspot => new ResilienceActionRow
            {
                Workstream = "Concentration Test",
                Title = hotspot.AssetName,
                Action = "Run failover and dependency concentration exercise for this critical service path.",
                OwnerLane = "Resilience Testing",
                DueDate = DateTime.UtcNow.Date.AddDays(hotspot.Criticality.Equals("critical", StringComparison.OrdinalIgnoreCase) ? 7 : 14),
                Status = "Scheduled",
                Severity = hotspot.Criticality
            });

        return incidentActions
            .Concat(alertActions)
            .Concat(hotspotActions)
            .OrderByDescending(x => ResiliencePriorityRank(x.Status, x.Severity))
            .ThenBy(x => x.DueDate)
            .Take(12)
            .ToList();
    }

    private static List<ResilienceTestingRow> BuildResilienceTestingRows(
        IReadOnlyList<PolicyScenario> policyScenarios,
        IReadOnlyList<ImpactAssessmentRun> impactRuns)
    {
        var resilienceScenarioIds = policyScenarios
            .Where(IsResilienceScenario)
            .Select(x => x.Id)
            .ToHashSet();

        if (resilienceScenarioIds.Count == 0)
        {
            resilienceScenarioIds = policyScenarios
                .Where(x => x.PolicyDomain == PolicyDomain.RiskManagement)
                .Select(x => x.Id)
                .ToHashSet();
        }

        if (resilienceScenarioIds.Count == 0)
        {
            return [];
        }

        var latestRunByScenario = impactRuns
            .Where(x => resilienceScenarioIds.Contains(x.ScenarioId))
            .GroupBy(x => x.ScenarioId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CompletedAt ?? x.StartedAt ?? x.CreatedAt).First());

        return policyScenarios
            .Where(x => resilienceScenarioIds.Contains(x.Id))
            .OrderByDescending(x => x.UpdatedAt)
            .Select(scenario =>
            {
                latestRunByScenario.TryGetValue(scenario.Id, out var latestRun);

                var lastRunAt = latestRun?.CompletedAt ?? latestRun?.StartedAt;
                var nextDueAt = (lastRunAt ?? scenario.UpdatedAt).AddDays(90);
                var status = latestRun?.Status switch
                {
                    ImpactRunStatus.Running => "Running",
                    ImpactRunStatus.Pending => "Scheduled",
                    ImpactRunStatus.Failed => "Overdue",
                    ImpactRunStatus.Completed when nextDueAt < DateTime.UtcNow => "Due",
                    ImpactRunStatus.Completed => "Complete",
                    _ when nextDueAt < DateTime.UtcNow => "Due",
                    _ => "Scheduled"
                };

                var outcome = latestRun?.Status switch
                {
                    ImpactRunStatus.Completed when latestRun.EntitiesWouldBreach > 0 => "Weaknesses Found",
                    ImpactRunStatus.Completed when latestRun.EntitiesAlreadyBreaching > 0 => "Control Gap",
                    ImpactRunStatus.Completed => "Stable",
                    ImpactRunStatus.Failed => "Escalate",
                    ImpactRunStatus.Running => "In Progress",
                    _ => "Awaiting Run"
                };

                return new ResilienceTestingRow
                {
                    ScenarioTitle = scenario.Title,
                    TestType = ResolveResilienceTestType(scenario),
                    Scope = scenario.TargetEntityTypes,
                    Status = status,
                    Outcome = outcome,
                    LastRunAt = lastRunAt,
                    NextDueAt = nextDueAt,
                    EntitiesEvaluated = latestRun?.TotalEntitiesEvaluated ?? 0
                };
            })
            .OrderByDescending(x => ResilienceTestPriorityRank(x.Status, x.Outcome))
            .ThenBy(x => x.NextDueAt)
            .Take(10)
            .ToList();
    }

    private static List<ResilienceGapRow> BuildResilienceGapRows(
        IReadOnlyList<CyberAsset> cyberAssets,
        IReadOnlyList<CyberAssetDependency> dependencies,
        IReadOnlyList<DependencyHotspotRow> hotspots,
        IReadOnlyList<DataBreachIncident> incidents,
        IReadOnlyList<SecurityAlert> securityAlerts,
        IReadOnlyList<RootCauseAnalysisRecord> rcaRecords,
        IReadOnlyList<ResilienceTestingRow> resilienceTests,
        IReadOnlyList<ResilienceActionRow> resilienceActions)
    {
        var criticalAssets = cyberAssets.Where(x => IsCriticalAsset(x.Criticality)).ToList();
        var mappedCriticalAssets = criticalAssets.Count == 0
            ? 0
            : criticalAssets.Count(asset => dependencies.Any(dep => dep.AssetId == asset.Id || dep.DependsOnAssetId == asset.Id));

        var serviceMappingScore = criticalAssets.Count == 0
            ? 55m
            : Math.Round(Math.Clamp(mappedCriticalAssets / (decimal)criticalAssets.Count * 100m, 20m, 100m), 1);

        var concentratedHotspots = hotspots.Where(x => x.DependentCount >= 2).ToList();
        var concentrationPenalty = concentratedHotspots.Sum(x => x.DependentCount >= 4 ? 18m : x.DependentCount >= 3 ? 12m : 7m);
        var concentrationScore = Math.Round(Math.Clamp(100m - concentrationPenalty, 25m, 100m), 1);

        var openIncidents = incidents.Where(x => !x.RemediatedAt.HasValue).ToList();
        var incidentPenalty = openIncidents.Sum(x => x.Severity switch
        {
            DataBreachSeverity.CRITICAL => 22m,
            DataBreachSeverity.HIGH => 14m,
            DataBreachSeverity.MEDIUM => 8m,
            _ => 4m
        });
        var incidentScore = Math.Round(Math.Clamp(100m - incidentPenalty, 20m, 100m), 1);

        var rcaCoverageScore = incidents.Count == 0
            ? 100m
            : Math.Round(Math.Clamp(rcaRecords.Count / (decimal)incidents.Count * 100m, 20m, 100m), 1);

        var overdueTests = resilienceTests.Count(x => x.Status is "Due" or "Overdue");
        var weakTests = resilienceTests.Count(x => x.Outcome is "Weaknesses Found" or "Control Gap" or "Escalate");
        var testingScore = Math.Round(Math.Clamp(100m - (overdueTests * 16m) - (weakTests * 10m), 20m, 100m), 1);

        var overdueActions = resilienceActions.Count(x => x.Status == "Overdue");
        var openActions = resilienceActions.Count(x => x.Status is "Open" or "Scheduled");
        var boardScore = Math.Round(Math.Clamp(100m - (overdueActions * 18m) - (openActions * 4m), 20m, 100m), 1);

        return
        [
            new ResilienceGapRow
            {
                Domain = "Critical Service Mapping",
                Score = serviceMappingScore,
                Signal = ResolveGapSignal(serviceMappingScore),
                Commentary = criticalAssets.Count == 0
                    ? "No critical assets are tagged yet, so mapping coverage is limited."
                    : $"{mappedCriticalAssets}/{criticalAssets.Count} critical assets are linked into the dependency map.",
                NextAction = "Validate important business services and confirm every critical asset has a mapped dependency path."
            },
            new ResilienceGapRow
            {
                Domain = "Third-Party Concentration",
                Score = concentrationScore,
                Signal = ResolveGapSignal(concentrationScore),
                Commentary = concentratedHotspots.Count == 0
                    ? "No concentrated dependency hotspots are currently visible."
                    : $"{concentratedHotspots.Count} dependency hotspot(s) need alternate provider or failover planning.",
                NextAction = "Review the highest-concentration service paths and design substitution or diversification controls."
            },
            new ResilienceGapRow
            {
                Domain = "Incident Response",
                Score = incidentScore,
                Signal = ResolveGapSignal(incidentScore),
                Commentary = openIncidents.Count == 0
                    ? "No open operational incidents are currently unresolved."
                    : $"{openIncidents.Count} incident(s) remain open across resilience operations.",
                NextAction = "Close remediation gaps on open incidents and tighten containment-to-recovery timelines."
            },
            new ResilienceGapRow
            {
                Domain = "Root Cause Discipline",
                Score = rcaCoverageScore,
                Signal = ResolveGapSignal(rcaCoverageScore),
                Commentary = incidents.Count == 0
                    ? "Incident population is currently empty, so RCA coverage is complete by default."
                    : $"{rcaRecords.Count}/{incidents.Count} incidents have an attached RCA record.",
                NextAction = "Backfill RCA coverage and capture board-facing lessons learned for unresolved events."
            },
            new ResilienceGapRow
            {
                Domain = "Testing Cadence",
                Score = testingScore,
                Signal = ResolveGapSignal(testingScore),
                Commentary = resilienceTests.Count == 0
                    ? "No resilience testing schedule is currently materialized."
                    : $"{overdueTests} test(s) are due or overdue; {weakTests} recent test result(s) found weaknesses.",
                NextAction = "Run overdue resilience tests and remediate weak outcomes before the next review cycle."
            },
            new ResilienceGapRow
            {
                Domain = "Board Oversight",
                Score = boardScore,
                Signal = ResolveGapSignal(boardScore),
                Commentary = resilienceActions.Count == 0
                    ? "No board-level resilience actions are currently open."
                    : $"{overdueActions} action(s) are overdue and {openActions} remain in flight.",
                NextAction = "Escalate overdue board actions and refresh the oversight log with owners and evidence."
            }
        ];
    }

    private static ResilienceBoardSummary BuildResilienceBoardSummary(
        IReadOnlyList<ResilienceGapRow> gapRows,
        IReadOnlyList<ResilienceActionRow> resilienceActions,
        IReadOnlyList<ResilienceTestingRow> resilienceTests,
        IReadOnlyList<DataBreachIncident> incidents)
    {
        var gapScore = gapRows.Count == 0 ? 0m : Math.Round(gapRows.Average(x => x.Score), 1);
        var overdueActions = resilienceActions.Count(x => x.Status == "Overdue");
        var dueTests = resilienceTests.Count(x => x.Status is "Due" or "Overdue");
        var criticalIncidents = incidents.Count(x => !x.RemediatedAt.HasValue && x.Severity is DataBreachSeverity.CRITICAL or DataBreachSeverity.HIGH);
        var weakestAreas = gapRows
            .OrderBy(x => x.Score)
            .Take(2)
            .Select(x => x.Domain)
            .ToList();

        var narrative = criticalIncidents > 0
            ? $"{criticalIncidents} severe incident(s) remain open; board oversight should prioritize {string.Join(" and ", weakestAreas)}."
            : overdueActions > 0 || dueTests > 0
                ? $"Board oversight should focus on {overdueActions} overdue action(s) and {dueTests} due test(s), especially in {string.Join(" and ", weakestAreas)}."
                : $"Resilience posture is stable overall; continue monitoring {string.Join(" and ", weakestAreas)} for emerging weakness.";

        return new ResilienceBoardSummary
        {
            GapScore = gapScore,
            CriticalIssues = criticalIncidents,
            OverdueActions = overdueActions,
            DueTests = dueTests,
            Narrative = narrative
        };
    }

    private static List<ModelInventoryRow> BuildModelInventory(
        IReadOnlyList<TemplateField> fields,
        IReadOnlyList<IntraSheetFormula> formulas,
        IReadOnlyList<ReturnTemplate> templates,
        IReadOnlyList<Module> modules,
        IReadOnlyList<Submission> submissions,
        IReadOnlyList<PolicyScenario> policyScenarios,
        IReadOnlyList<HistoricalImpactTracking> historicalImpact)
    {
        var rows = new List<ModelInventoryRow>();
        foreach (var seed in ModelInventoryCatalog)
        {
            var fieldCoverage = fields.Count(field =>
                seed.MatchTerms.Any(term =>
                    field.FieldName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || field.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (field.HelpText?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)));

            var formulaCoverage = formulas.Count(formula =>
                formula.RuleName.Contains(seed.ModelCode, StringComparison.OrdinalIgnoreCase)
                || seed.MatchTerms.Any(term =>
                    formula.RuleName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || formula.TargetFieldName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (formula.CustomExpression?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)));

            var templateCoverage = templates.Count(template =>
                template.ReturnCode.Contains(seed.ReturnHint, StringComparison.OrdinalIgnoreCase)
                || template.Name.Contains(seed.ModelName, StringComparison.OrdinalIgnoreCase)
                || seed.MatchTerms.Any(term => template.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));

            var submissionSample = submissions
                .Where(sub => sub.ReturnCode.Contains(seed.ReturnHint, StringComparison.OrdinalIgnoreCase)
                              || seed.MatchTerms.Any(term => sub.ReturnCode.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var passRate = submissionSample.Count == 0
                ? (decimal?)null
                : Math.Round(submissionSample.Count(x => x.ValidationReport is not null && x.ValidationReport.IsValid) / (decimal)submissionSample.Count * 100m, 1);

            var accuracyScore = seed.ModelCode is "STRESS" or "CLIMATE"
                ? historicalImpact.Where(x => x.AccuracyScore.HasValue).Select(x => x.AccuracyScore!.Value).DefaultIfEmpty().Average()
                : passRate;

            var lastEvidenceAt = seed.ModelCode is "STRESS" or "CLIMATE"
                ? policyScenarios.OrderByDescending(x => x.UpdatedAt).Select(x => (DateTime?)x.UpdatedAt).FirstOrDefault()
                : submissionSample.OrderByDescending(x => x.SubmittedAt).Select(x => (DateTime?)x.SubmittedAt).FirstOrDefault()
                  ?? templates
                      .Where(x => x.ReturnCode.Contains(seed.ReturnHint, StringComparison.OrdinalIgnoreCase))
                      .OrderByDescending(x => x.UpdatedAt)
                      .Select(x => (DateTime?)x.UpdatedAt)
                      .FirstOrDefault();

            var validationCycleMonths = seed.Tier == "Tier 1" ? 12 : 24;
            var nextValidationDue = (lastEvidenceAt ?? DateTime.UtcNow).AddMonths(validationCycleMonths);
            var validationStatus = nextValidationDue < DateTime.UtcNow.Date
                ? "Overdue"
                : nextValidationDue < DateTime.UtcNow.Date.AddDays(60)
                    ? "Due"
                    : "Current";

            var performanceStatus = !accuracyScore.HasValue
                ? "Monitor"
                : accuracyScore.Value < 70m
                    ? "Alert"
                    : accuracyScore.Value < 85m
                        ? "Watch"
                        : "Stable";

            rows.Add(new ModelInventoryRow
            {
                ModelCode = seed.ModelCode,
                ModelName = seed.ModelName,
                Tier = seed.Tier,
                Owner = seed.Owner,
                CoverageArtifacts = fieldCoverage + formulaCoverage + templateCoverage,
                LastEvidenceAt = lastEvidenceAt,
                NextValidationDue = nextValidationDue,
                ValidationStatus = validationStatus,
                PerformanceStatus = performanceStatus,
                PerformanceScore = accuracyScore.HasValue ? Math.Round(accuracyScore.Value, 1) : null
            });
        }

        return rows
            .OrderBy(x => x.NextValidationDue)
            .ThenByDescending(x => x.Tier, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<ModelValidationScheduleRow> BuildModelValidationCalendar(
        IReadOnlyList<ModelInventoryRow> inventory)
    {
        return inventory
            .Select(row =>
            {
                var daysUntilDue = (row.NextValidationDue.Date - DateTime.UtcNow.Date).Days;
                var validationType = row.Tier == "Tier 1" ? "Annual full-scope" : "Biennial targeted";
                var focus = row.PerformanceStatus switch
                {
                    "Alert" => "Outcome analysis, sensitivity review, and management challenge.",
                    "Watch" => "Backtesting refresh and threshold recalibration.",
                    _ when row.CoverageArtifacts < 8 => "Implementation testing and data integrity walkthrough.",
                    _ => row.Tier == "Tier 1"
                        ? "Conceptual soundness, implementation testing, and outcome analysis."
                        : "Targeted data integrity and monitoring refresh."
                };

                return new ModelValidationScheduleRow
                {
                    ModelCode = row.ModelCode,
                    ModelName = row.ModelName,
                    Tier = row.Tier,
                    Owner = row.Owner,
                    ValidationType = validationType,
                    LastEvidenceAt = row.LastEvidenceAt,
                    NextValidationDue = row.NextValidationDue,
                    DaysUntilDue = daysUntilDue,
                    Status = row.ValidationStatus,
                    Focus = focus
                };
            })
            .OrderBy(x => x.NextValidationDue)
            .ThenByDescending(x => x.Tier, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<ModelPerformanceEvidenceRow> BuildModelPerformanceRows(
        IReadOnlyList<ModelInventoryRow> inventory,
        IReadOnlyList<Submission> submissions,
        IReadOnlyList<PolicyScenario> policyScenarios,
        IReadOnlyList<HistoricalImpactTracking> historicalImpact)
    {
        var scenarioById = policyScenarios.ToDictionary(x => x.Id);

        return inventory
            .Select(row =>
            {
                var seed = ModelInventoryCatalog.First(x => x.ModelCode == row.ModelCode);

                if (row.ModelCode is "STRESS" or "CLIMATE")
                {
                    var tracking = historicalImpact
                        .Where(entry =>
                            scenarioById.TryGetValue(entry.ScenarioId, out var scenario)
                            && ResolveScenarioModelCode(scenario) == row.ModelCode)
                        .OrderByDescending(x => x.TrackingDate)
                        .ToList();

                    var latest = tracking.FirstOrDefault();
                    var avgAccuracy = tracking.Count == 0
                        ? (decimal?)null
                        : Math.Round(tracking.Where(x => x.AccuracyScore.HasValue).Select(x => x.AccuracyScore!.Value).DefaultIfEmpty().Average(), 1);

                    return new ModelPerformanceEvidenceRow
                    {
                        ModelCode = row.ModelCode,
                        ModelName = row.ModelName,
                        EvidenceType = "Backtesting",
                        MeasuredAt = latest?.CreatedAt,
                        SampleSize = tracking.Count,
                        MetricLabel = "Accuracy",
                        MetricValue = avgAccuracy,
                        Signal = row.PerformanceStatus,
                        Commentary = latest is null
                            ? "No tracked post-enactment evidence is currently available."
                            : $"Predicted breaches {latest.PredictedBreachCount}, actual {latest.ActualBreachCount}, variance {(latest.BreachCountVariance?.ToString("0.00", CultureInfo.InvariantCulture) ?? "N/A")}."
                    };
                }

                var relevantSubmissions = submissions
                    .Where(sub => MatchesModelSeed(sub.ReturnCode, seed))
                    .OrderByDescending(sub => sub.SubmittedAt)
                    .ToList();

                var validCount = relevantSubmissions.Count(x => x.ValidationReport is not null && x.ValidationReport.IsValid);
                var latestSubmission = relevantSubmissions.FirstOrDefault();

                return new ModelPerformanceEvidenceRow
                {
                    ModelCode = row.ModelCode,
                    ModelName = row.ModelName,
                    EvidenceType = "Implementation testing",
                    MeasuredAt = latestSubmission?.SubmittedAt ?? row.LastEvidenceAt,
                    SampleSize = relevantSubmissions.Count,
                    MetricLabel = "Pass rate",
                    MetricValue = row.PerformanceScore,
                    Signal = row.PerformanceStatus,
                    Commentary = relevantSubmissions.Count == 0
                        ? "No recent filing sample is linked to this model."
                        : $"{validCount}/{relevantSubmissions.Count} sampled submissions validated successfully."
                };
            })
            .OrderByDescending(x => PerformanceSignalRank(x.Signal))
            .ThenBy(x => x.ModelCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<ModelBacktestingRow> BuildModelBacktestingRows(
        IReadOnlyList<ModelInventoryRow> inventory,
        IReadOnlyList<Submission> submissions,
        IReadOnlyList<PolicyScenario> policyScenarios,
        IReadOnlyList<HistoricalImpactTracking> historicalImpact)
    {
        var scenarioById = policyScenarios.ToDictionary(x => x.Id);

        return inventory
            .Select(row =>
            {
                var seed = ModelInventoryCatalog.First(x => x.ModelCode == row.ModelCode);

                if (row.ModelCode is "STRESS" or "CLIMATE")
                {
                    var tracking = historicalImpact
                        .Where(entry =>
                            scenarioById.TryGetValue(entry.ScenarioId, out var scenario)
                            && ResolveScenarioModelCode(scenario) == row.ModelCode)
                        .OrderByDescending(x => x.TrackingDate)
                        .ToList();

                    var latest = tracking.FirstOrDefault();
                    var scenarioSignal = latest?.BreachCountVariance switch
                    {
                        <= -0.2m or >= 0.2m => "Alert",
                        <= -0.1m or >= 0.1m => "Watch",
                        _ => row.PerformanceStatus
                    };

                    return new ModelBacktestingRow
                    {
                        ModelCode = row.ModelCode,
                        ModelName = row.ModelName,
                        BacktestType = "Predicted vs Actual",
                        SampleSize = tracking.Count,
                        PredictedLabel = "Predicted breaches",
                        PredictedValue = latest?.PredictedBreachCount.ToString(CultureInfo.InvariantCulture) ?? "N/A",
                        ActualLabel = "Actual breaches",
                        ActualValue = latest?.ActualBreachCount.ToString(CultureInfo.InvariantCulture) ?? "N/A",
                        VarianceText = latest?.BreachCountVariance?.ToString("0.00", CultureInfo.InvariantCulture) ?? "N/A",
                        Signal = scenarioSignal,
                        Commentary = latest is null
                            ? "No post-enactment backtesting record is currently available."
                            : $"Latest tracking recorded {latest.PredictedBreachCount} predicted breaches versus {latest.ActualBreachCount} actual breaches, with accuracy {(latest.AccuracyScore?.ToString("0.0", CultureInfo.InvariantCulture) ?? "N/A")}."
                    };
                }

                var relevantSubmissions = submissions
                    .Where(sub => MatchesModelSeed(sub.ReturnCode, seed))
                    .OrderByDescending(sub => sub.SubmittedAt)
                    .ToList();

                var validCount = relevantSubmissions.Count(x => x.ValidationReport is not null && x.ValidationReport.IsValid);
                var benchmark = row.Tier == "Tier 1" ? 95m : 90m;
                var observed = row.PerformanceScore;
                var variance = observed.HasValue ? observed.Value - benchmark : (decimal?)null;
                var varianceSignal = variance switch
                {
                    <= -20m => "Alert",
                    <= -8m => "Watch",
                    null => "Monitor",
                    _ => row.PerformanceStatus
                };

                return new ModelBacktestingRow
                {
                    ModelCode = row.ModelCode,
                    ModelName = row.ModelName,
                    BacktestType = "Benchmark backtest",
                    SampleSize = relevantSubmissions.Count,
                    PredictedLabel = "Benchmark %",
                    PredictedValue = benchmark.ToString("0.0", CultureInfo.InvariantCulture),
                    ActualLabel = "Observed %",
                    ActualValue = observed?.ToString("0.0", CultureInfo.InvariantCulture) ?? "N/A",
                    VarianceText = variance?.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) ?? "N/A",
                    Signal = varianceSignal,
                    Commentary = relevantSubmissions.Count == 0
                        ? "No recent filing sample is linked to this model for benchmark backtesting."
                        : $"{validCount}/{relevantSubmissions.Count} sampled submissions validated successfully against the configured benchmark."
                };
            })
            .OrderByDescending(x => PerformanceSignalRank(x.Signal))
            .ThenBy(x => x.ModelCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<ModelMonitoringSummaryRow> BuildModelMonitoringSummaryRows(
        IReadOnlyList<ModelInventoryRow> inventory,
        IReadOnlyList<ModelPerformanceEvidenceRow> performanceEvidence,
        IReadOnlyList<ModelBacktestingRow> backtestingRows)
    {
        var evidenceByCode = performanceEvidence.ToDictionary(x => x.ModelCode, StringComparer.OrdinalIgnoreCase);
        var backtestingByCode = backtestingRows.ToDictionary(x => x.ModelCode, StringComparer.OrdinalIgnoreCase);

        return inventory
            .Select(model =>
            {
                evidenceByCode.TryGetValue(model.ModelCode, out var evidence);
                backtestingByCode.TryGetValue(model.ModelCode, out var backtesting);

                var focus = model.ModelCode switch
                {
                    "ECL" => "Outcome analysis and impairment drift",
                    "CAR" or "LCR" or "NSFR" => "Implementation stability and prudential monitoring",
                    "STRESS" or "CLIMATE" => "Scenario backtesting and calibration",
                    _ => "Monitoring refresh"
                };

                var isScenarioModel = model.ModelCode is "STRESS" or "CLIMATE";
                var stabilityLabel = isScenarioModel ? "Breach variance" : "Invalid rate";
                var stabilityValue = isScenarioModel
                    ? backtesting?.VarianceText ?? "N/A"
                    : model.PerformanceScore.HasValue
                        ? (100m - model.PerformanceScore.Value).ToString("0.0", CultureInfo.InvariantCulture)
                        : "N/A";

                var accuracyLabel = isScenarioModel ? "Accuracy" : (evidence?.MetricLabel ?? "Pass rate");
                var accuracyValue = evidence?.MetricValue?.ToString("0.0", CultureInfo.InvariantCulture)
                                    ?? model.PerformanceScore?.ToString("0.0", CultureInfo.InvariantCulture)
                                    ?? "N/A";

                var calibrationStatus = backtesting?.Signal switch
                {
                    "Alert" => "Alert",
                    "Watch" => "Review",
                    _ when model.ValidationStatus == "Overdue" => "Alert",
                    _ when model.ValidationStatus == "Due" || model.PerformanceStatus == "Watch" => "Review",
                    _ => "Current"
                };

                return new ModelMonitoringSummaryRow
                {
                    ModelCode = model.ModelCode,
                    ModelName = model.ModelName,
                    MonitoringFocus = focus,
                    StabilityLabel = stabilityLabel,
                    StabilityValue = stabilityValue,
                    AccuracyLabel = accuracyLabel,
                    AccuracyValue = accuracyValue,
                    CalibrationStatus = calibrationStatus,
                    Signal = model.PerformanceStatus,
                    Commentary = evidence?.Commentary
                        ?? backtesting?.Commentary
                        ?? "No recent monitoring evidence is currently attached."
                };
            })
            .OrderByDescending(x => PerformanceSignalRank(x.Signal))
            .ThenByDescending(x => x.CalibrationStatus == "Alert")
            .ThenByDescending(x => x.CalibrationStatus == "Review")
            .ThenBy(x => x.ModelCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<ModelApprovalQueueRow> BuildModelApprovalQueue(
        IReadOnlyList<ModelInventoryRow> inventory,
        IReadOnlyList<ModelChangeRow> modelChanges)
    {
        return modelChanges
            .Where(x => x.ReviewSignal is "Critical" or "Review")
            .Select(change =>
            {
                var model = ResolveModelForChange(change, inventory);
                var currentStage = change.ReviewSignal == "Critical" ? "Validation Team" : "Model Owner";
                var dueDate = change.ChangedAt.Date.AddDays(change.ReviewSignal == "Critical" ? 5 : 10);

                return new ModelApprovalQueueRow
                {
                    WorkflowKey = $"{model.ModelCode}|{change.Artifact}|{change.ChangedAt.Ticks}",
                    ModelCode = model.ModelCode,
                    ModelName = model.ModelName,
                    Artifact = change.Artifact,
                    ChangeType = change.ChangeType,
                    Owner = model.Owner,
                    CurrentStage = currentStage,
                    DueDate = dueDate,
                    ImpactPackage = change.ReviewSignal == "Critical"
                        ? "Parallel run, validation note, committee pack, and board challenge memo required."
                        : "Owner attestation, impact note, and validation evidence refresh required.",
                    ReviewSignal = change.ReviewSignal
                };
            })
            .OrderByDescending(x => x.ReviewSignal == "Critical")
            .ThenBy(x => x.DueDate)
            .Take(12)
            .ToList();
    }

    private static List<ModelRiskAppetiteRow> BuildModelRiskAppetiteRows(
        IReadOnlyList<ModelInventoryRow> inventory,
        IReadOnlyList<ModelValidationScheduleRow> validationCalendar,
        IReadOnlyList<ModelPerformanceEvidenceRow> performanceEvidence,
        IReadOnlyList<ModelApprovalQueueRow> approvalQueue)
    {
        var validationByCode = validationCalendar.ToDictionary(x => x.ModelCode, StringComparer.OrdinalIgnoreCase);
        var performanceByCode = performanceEvidence.ToDictionary(x => x.ModelCode, StringComparer.OrdinalIgnoreCase);
        var queueByModel = approvalQueue
            .GroupBy(x => x.ModelCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        return inventory
            .Select(model =>
            {
                validationByCode.TryGetValue(model.ModelCode, out var validation);
                performanceByCode.TryGetValue(model.ModelCode, out var performance);
                var workflowItems = queueByModel.GetValueOrDefault(model.ModelCode) ?? [];
                var pendingWorkflow = workflowItems.Any(x => x.CurrentStage is not "Approved" and not "Rejected");

                var score = 100m;
                score -= model.ValidationStatus switch
                {
                    "Overdue" => 35m,
                    "Due" => 18m,
                    _ => 0m
                };
                score -= model.PerformanceStatus switch
                {
                    "Alert" => 32m,
                    "Watch" => 16m,
                    "Monitor" => 8m,
                    _ => 0m
                };
                score -= workflowItems.Any(x => x.ReviewSignal == "Critical") ? 14m : pendingWorkflow ? 8m : 0m;
                score -= model.CoverageArtifacts < 8 ? 10m : model.CoverageArtifacts < 14 ? 4m : 0m;
                score = Math.Round(Math.Clamp(score, 20m, 100m), 1);

                var appetiteStatus = model.ValidationStatus == "Overdue" || model.PerformanceStatus == "Alert"
                    ? "Breach"
                    : model.ValidationStatus == "Due" || model.PerformanceStatus == "Watch" || pendingWorkflow
                        ? "Watch"
                        : "Within Appetite";

                var reportingStatus = appetiteStatus switch
                {
                    "Breach" => "Hold",
                    "Watch" when pendingWorkflow => "Hold",
                    "Watch" => "Review",
                    _ => "Ready"
                };

                var nextAction = appetiteStatus switch
                {
                    "Breach" => "Escalate to model risk committee, refresh validation evidence, and halt reporting reliance until remediation is accepted.",
                    "Watch" when pendingWorkflow => "Clear queued approvals and refresh the supporting validation or impact package.",
                    "Watch" => "Run targeted validation refresh and monitor performance drift ahead of the next filing cycle.",
                    _ => "Maintain periodic monitoring and include the model in the next reporting pack."
                };

                return new ModelRiskAppetiteRow
                {
                    ModelCode = model.ModelCode,
                    ModelName = model.ModelName,
                    Tier = model.Tier,
                    Owner = model.Owner,
                    AppetiteStatus = appetiteStatus,
                    ReportingStatus = reportingStatus,
                    RiskScore = score,
                    ValidationStatus = validation?.Status ?? model.ValidationStatus,
                    PerformanceSignal = performance?.Signal ?? model.PerformanceStatus,
                    PendingWorkflowStage = workflowItems.FirstOrDefault(x => x.CurrentStage is not "Approved" and not "Rejected")?.CurrentStage ?? "Clear",
                    NextValidationDue = model.NextValidationDue,
                    LastEvidenceAt = performance?.MeasuredAt ?? model.LastEvidenceAt,
                    NextAction = nextAction
                };
            })
            .OrderByDescending(x => ModelAppetitePriorityRank(x.AppetiteStatus))
            .ThenBy(x => x.RiskScore)
            .ThenBy(x => x.NextValidationDue)
            .ToList();
    }

    private static List<ModelRiskReportingRow> BuildModelRiskReportingRows(
        IReadOnlyList<ModelInventoryRow> inventory,
        IReadOnlyList<ModelValidationScheduleRow> validationCalendar,
        IReadOnlyList<ModelPerformanceEvidenceRow> performanceEvidence,
        IReadOnlyList<ModelChangeRow> modelChanges,
        IReadOnlyList<ModelRiskAppetiteRow> appetiteRows)
    {
        var overdueValidations = validationCalendar.Count(x => x.Status == "Overdue");
        var dueValidations = validationCalendar.Count(x => x.Status == "Due");
        var alertPerformance = performanceEvidence.Count(x => x.Signal == "Alert");
        var watchPerformance = performanceEvidence.Count(x => x.Signal == "Watch");
        var reviewChanges = modelChanges.Count(x => x.ReviewSignal is "Critical" or "Review");
        var breaches = appetiteRows.Count(x => x.AppetiteStatus == "Breach");
        var watch = appetiteRows.Count(x => x.AppetiteStatus == "Watch");

        return
        [
            new ModelRiskReportingRow
            {
                Section = "Inventory Summary",
                MetricValue = inventory.Count.ToString(CultureInfo.InvariantCulture),
                Signal = inventory.Count == 0 ? "Watch" : "Current",
                Commentary = inventory.Count == 0
                    ? "No governed regulatory models are currently catalogued."
                    : $"{inventory.Count(x => x.Tier == "Tier 1")} Tier 1 and {inventory.Count(x => x.Tier != "Tier 1")} lower-tier models are in scope."
            },
            new ModelRiskReportingRow
            {
                Section = "Validation Status",
                MetricValue = overdueValidations > 0
                    ? $"{overdueValidations} overdue"
                    : $"{dueValidations} due",
                Signal = overdueValidations > 0 ? "Breach" : dueValidations > 0 ? "Watch" : "Current",
                Commentary = overdueValidations > 0
                    ? $"{overdueValidations} model validation(s) are overdue and should be escalated in the reporting pack."
                    : dueValidations > 0
                        ? $"{dueValidations} validation(s) are approaching deadline within the reporting horizon."
                        : "All governed models remain within the current validation window."
            },
            new ModelRiskReportingRow
            {
                Section = "Performance Metrics",
                MetricValue = alertPerformance > 0
                    ? $"{alertPerformance} alert"
                    : $"{watchPerformance} watch",
                Signal = alertPerformance > 0 ? "Breach" : watchPerformance > 0 ? "Watch" : "Current",
                Commentary = alertPerformance > 0
                    ? $"{alertPerformance} model(s) show degraded performance beyond appetite."
                    : watchPerformance > 0
                        ? $"{watchPerformance} model(s) need closer performance monitoring before the next filing cycle."
                        : "Current backtesting and implementation evidence remains inside the accepted range."
            },
            new ModelRiskReportingRow
            {
                Section = "Material Changes",
                MetricValue = reviewChanges.ToString(CultureInfo.InvariantCulture),
                Signal = reviewChanges >= 4 ? "Watch" : "Current",
                Commentary = reviewChanges == 0
                    ? "No recent material model-affecting changes require disclosure."
                    : $"{reviewChanges} recent model-affecting change(s) need inclusion in the governance update."
            },
            new ModelRiskReportingRow
            {
                Section = "Risk Appetite Compliance",
                MetricValue = $"{breaches} breach / {watch} watch",
                Signal = breaches > 0 ? "Breach" : watch > 0 ? "Watch" : "Current",
                Commentary = breaches > 0
                    ? $"{breaches} model(s) are outside appetite and should be highlighted in the supervisory return pack."
                    : watch > 0
                        ? $"{watch} model(s) are on watch and should be tracked in the next model risk report."
                        : "All governed models currently sit within the defined operating appetite."
            }
        ];
    }

    private static List<ModelRiskSheetRow> BuildModelRiskReturnPackRows(
        IReadOnlyList<ModelInventoryRow> inventory,
        IReadOnlyList<ModelValidationScheduleRow> validationCalendar,
        IReadOnlyList<ModelPerformanceEvidenceRow> performanceEvidence,
        IReadOnlyList<ModelBacktestingRow> backtestingRows,
        IReadOnlyList<ModelMonitoringSummaryRow> monitoringRows,
        IReadOnlyList<ModelChangeRow> modelChanges,
        IReadOnlyList<ModelApprovalQueueRow> approvalQueue,
        IReadOnlyList<ModelRiskAppetiteRow> appetiteRows,
        IReadOnlyList<ModelRiskReportingRow> reportingPack)
    {
        var criticalChanges = modelChanges.Count(x => x.ReviewSignal == "Critical");
        var reviewChanges = modelChanges.Count(x => x.ReviewSignal is "Critical" or "Review");
        var overdueValidations = validationCalendar.Count(x => x.Status == "Overdue");
        var dueValidations = validationCalendar.Count(x => x.Status == "Due");
        var performanceAlerts = performanceEvidence.Count(x => x.Signal == "Alert");
        var performanceWatch = performanceEvidence.Count(x => x.Signal == "Watch");
        var backtestingAlerts = backtestingRows.Count(x => x.Signal == "Alert");
        var backtestingWatch = backtestingRows.Count(x => x.Signal == "Watch");
        var monitoringAlerts = monitoringRows.Count(x => x.Signal == "Alert" || x.CalibrationStatus == "Alert");
        var monitoringWatch = monitoringRows.Count(x => x.Signal == "Watch" || x.CalibrationStatus == "Review");
        var queueCritical = approvalQueue.Count(x => x.ReviewSignal == "Critical");
        var appetiteBreaches = appetiteRows.Count(x => x.AppetiteStatus == "Breach");
        var appetiteWatch = appetiteRows.Count(x => x.AppetiteStatus == "Watch");

        return
        [
            new ModelRiskSheetRow
            {
                SheetCode = "MRM-01",
                SheetName = "Model Inventory Summary",
                RowCount = inventory.Count,
                Signal = inventory.Count == 0 ? "Watch" : "Current",
                Coverage = $"{inventory.Count} model row(s) across {inventory.Count(x => x.Tier == "Tier 1")} Tier 1 and {inventory.Count(x => x.Tier != "Tier 1")} lower-tier model(s).",
                Commentary = inventory.Count == 0
                    ? "No governed regulatory models are currently catalogued."
                    : $"{inventory.Count(x => x.CoverageArtifacts < 8)} model(s) have thin evidence coverage and should be challenged.",
                RecommendedAction = "Confirm the inventory baseline, ownership, and evidence coverage before finalizing the return pack."
            },
            new ModelRiskSheetRow
            {
                SheetCode = "MRM-02",
                SheetName = "Validation Status",
                RowCount = validationCalendar.Count,
                Signal = overdueValidations > 0 ? "Critical" : dueValidations > 0 ? "Watch" : "Current",
                Coverage = $"{validationCalendar.Count} validation row(s); {overdueValidations} overdue and {dueValidations} due.",
                Commentary = overdueValidations > 0
                    ? $"{overdueValidations} validation(s) are overdue and already outside governance tolerance."
                    : dueValidations > 0
                        ? $"{dueValidations} validation(s) are approaching deadline within the reporting horizon."
                        : "All governed models remain within the current validation window.",
                RecommendedAction = "Escalate overdue validations and refresh the plan for models due inside the next review cycle."
            },
            new ModelRiskSheetRow
            {
                SheetCode = "MRM-03",
                SheetName = "Performance Metrics",
                RowCount = performanceEvidence.Count,
                Signal = performanceAlerts > 0 ? "Critical" : performanceWatch > 0 ? "Watch" : "Current",
                Coverage = $"{performanceEvidence.Count} performance row(s); {performanceAlerts} alert and {performanceWatch} watch item(s).",
                Commentary = performanceEvidence.Count == 0
                    ? "No performance evidence is currently attached to the pack."
                    : $"{performanceEvidence.Count(x => x.MetricValue.HasValue)} row(s) include measured performance metrics.",
                RecommendedAction = "Refresh implementation and outcome evidence for any models sitting on watch or alert status."
            },
            new ModelRiskSheetRow
            {
                SheetCode = "MRM-04",
                SheetName = "Backtesting & Outcome Analysis",
                RowCount = backtestingRows.Count,
                Signal = backtestingAlerts > 0 ? "Critical" : backtestingWatch > 0 ? "Watch" : "Current",
                Coverage = $"{backtestingRows.Count} backtesting row(s); {backtestingAlerts} alert and {backtestingWatch} watch variance signal(s).",
                Commentary = backtestingRows.Count == 0
                    ? "No backtesting evidence is currently materialized."
                    : $"{backtestingRows.Count(x => x.SampleSize > 0)} model(s) have explicit backtesting sample coverage.",
                RecommendedAction = "Run targeted backtesting refresh and challenge any variance-driven alert models before pack sign-off."
            },
            new ModelRiskSheetRow
            {
                SheetCode = "MRM-05",
                SheetName = "Monitoring Summary",
                RowCount = monitoringRows.Count,
                Signal = monitoringAlerts > 0 ? "Critical" : monitoringWatch > 0 ? "Watch" : "Current",
                Coverage = $"{monitoringRows.Count} monitoring row(s); {monitoringAlerts} alert and {monitoringWatch} review item(s).",
                Commentary = monitoringRows.Count == 0
                    ? "No monitoring summary is currently attached to the pack."
                    : $"{monitoringRows.Count(x => x.CalibrationStatus == "Alert")} model(s) need immediate calibration attention.",
                RecommendedAction = "Use the monitoring sheet to prioritize stability, calibration, and drift review across governed models."
            },
            new ModelRiskSheetRow
            {
                SheetCode = "MRM-06",
                SheetName = "Material Model Changes",
                RowCount = modelChanges.Count,
                Signal = criticalChanges > 0 ? "Critical" : reviewChanges > 0 ? "Watch" : "Current",
                Coverage = $"{modelChanges.Count} change row(s); {criticalChanges} critical and {Math.Max(0, reviewChanges - criticalChanges)} review-grade item(s).",
                Commentary = modelChanges.Count == 0
                    ? "No material model-affecting changes are currently recorded."
                    : $"{reviewChanges} change(s) require governance attention in the current reporting window.",
                RecommendedAction = "Validate rationale, impact notes, and parallel-run evidence for all material model changes."
            },
            new ModelRiskSheetRow
            {
                SheetCode = "MRM-07",
                SheetName = "Approval Workflow",
                RowCount = approvalQueue.Count,
                Signal = queueCritical > 0 ? "Critical" : approvalQueue.Count > 0 ? "Watch" : "Current",
                Coverage = $"{approvalQueue.Count} workflow row(s); {queueCritical} carry critical review signal.",
                Commentary = approvalQueue.Count == 0
                    ? "No material model changes are queued for formal approval."
                    : $"{approvalQueue.Count(x => x.CurrentStage == "Board Review")} item(s) are awaiting board review.",
                RecommendedAction = "Clear blocked approval stages and confirm committee or board packs are complete for queued model changes."
            },
            new ModelRiskSheetRow
            {
                SheetCode = "MRM-08",
                SheetName = "Model Risk Appetite Compliance",
                RowCount = appetiteRows.Count,
                Signal = appetiteBreaches > 0 ? "Critical" : appetiteWatch > 0 ? "Watch" : "Current",
                Coverage = $"{appetiteRows.Count} appetite row(s); {appetiteBreaches} breach and {appetiteWatch} watch model(s).",
                Commentary = appetiteRows.Count == 0
                    ? "No appetite register is currently available."
                    : $"{appetiteRows.Count(x => x.ReportingStatus == "Hold")} model(s) are currently held from clean reporting status.",
                RecommendedAction = "Escalate breaches and clear watch items before the supervisory model-risk pack is issued."
            },
            new ModelRiskSheetRow
            {
                SheetCode = "MRM-09",
                SheetName = "Supervisory Reporting Pack",
                RowCount = reportingPack.Count,
                Signal = reportingPack.Any(x => x.Signal == "Breach") ? "Critical" : reportingPack.Any(x => x.Signal == "Watch") ? "Watch" : "Current",
                Coverage = $"{reportingPack.Count} reporting section row(s) assembled for supervisory disclosure.",
                Commentary = reportingPack.Count == 0
                    ? "No reporting-pack summary rows are currently assembled."
                    : $"{reportingPack.Count(x => x.Signal == "Breach")} reporting section(s) are already in breach posture.",
                RecommendedAction = "Finalize the reporting narrative, reconcile sheet metrics, and clear any breached sections before pack issuance."
            }
        ];
    }

    private static List<ModelChangeRow> BuildModelChangeRows(
        IReadOnlyList<AuditLogEntry> auditLog,
        IReadOnlyList<FieldChangeHistory> fieldChanges)
    {
        var auditRows = auditLog
            .Where(entry =>
                entry.EntityType.Contains("template", StringComparison.OrdinalIgnoreCase)
                || entry.EntityType.Contains("formula", StringComparison.OrdinalIgnoreCase)
                || entry.EntityType.Contains("rule", StringComparison.OrdinalIgnoreCase)
                || entry.EntityType.Contains("validation", StringComparison.OrdinalIgnoreCase))
            .Select(entry => new ModelChangeRow
            {
                TenantId = entry.TenantId,
                Area = ResolveModelChangeArea(entry.EntityType),
                Artifact = $"{entry.EntityType} #{entry.EntityId}",
                ChangeType = entry.Action,
                PerformedBy = entry.PerformedBy,
                ChangedAt = entry.PerformedAt,
                ReviewSignal = ResolveModelReviewSignal(entry.EntityType, entry.Action)
            });

        var fieldRows = fieldChanges
            .Where(change =>
                change.ChangeSource.Equals("Computed", StringComparison.OrdinalIgnoreCase)
                || change.ChangeSource.Equals("System", StringComparison.OrdinalIgnoreCase)
                || change.ChangeSource.Equals("Import", StringComparison.OrdinalIgnoreCase))
            .Select(change => new ModelChangeRow
            {
                TenantId = change.TenantId,
                Area = "Data Inputs",
                Artifact = $"{change.ReturnCode}.{change.FieldName}",
                ChangeType = change.ChangeSource,
                PerformedBy = change.ChangedBy,
                ChangedAt = change.ChangedAt,
                ReviewSignal = change.ChangeSource.Equals("System", StringComparison.OrdinalIgnoreCase) ? "Review" : "Monitor"
            });

        return auditRows
            .Concat(fieldRows)
            .OrderByDescending(x => ModelReviewRank(x.ReviewSignal))
            .ThenByDescending(x => x.ChangedAt)
            .Take(16)
            .ToList();
    }

    private static List<InstitutionScorecardRow> BuildInstitutionScorecards(
        IReadOnlyList<Institution> institutions,
        IReadOnlyList<KnowledgeGraphInstitutionObligationRow> institutionObligations,
        IReadOnlyList<CapitalWatchlistRow> capitalRows,
        IReadOnlyList<DataBreachIncident> incidents,
        IReadOnlyList<SecurityAlert> securityAlerts,
        IReadOnlyList<ModelChangeRow> modelChanges)
    {
        var headOffices = institutions
            .Where(x => x.IsActive && (x.EntityType == EntityType.HeadOffice || !x.ParentInstitutionId.HasValue))
            .OrderBy(x => x.InstitutionName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var obligationByInstitution = institutionObligations
            .GroupBy(x => x.InstitutionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var capitalByTenant = capitalRows
            .Where(x => x.TenantId != Guid.Empty)
            .GroupBy(x => x.TenantId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.CapitalScore).First());

        var incidentByTenant = incidents
            .Where(x => x.TenantId.HasValue)
            .GroupBy(x => x.TenantId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var openAlertsByTenant = securityAlerts
            .Where(x => x.Status.Equals("open", StringComparison.OrdinalIgnoreCase)
                        || x.Status.Equals("investigating", StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.TenantId)
            .ToDictionary(g => g.Key, g => g.Count());

        var modelReviewByTenant = modelChanges
            .Where(x => x.TenantId.HasValue && x.ReviewSignal is "Critical" or "Review")
            .GroupBy(x => x.TenantId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var rows = new List<InstitutionScorecardRow>();
        foreach (var institution in headOffices)
        {
            obligationByInstitution.TryGetValue(institution.Id, out var obligations);
            capitalByTenant.TryGetValue(institution.TenantId, out var capital);
            incidentByTenant.TryGetValue(institution.TenantId, out var tenantIncidents);
            openAlertsByTenant.TryGetValue(institution.TenantId, out var openAlertCount);
            modelReviewByTenant.TryGetValue(institution.TenantId, out var modelReviewCount);

            var overdue = obligations?.Count(x => x.Status is "Overdue" or "Attention Required") ?? 0;
            var dueSoon = obligations?.Count(x => x.Status == "Due Soon") ?? 0;
            var openIncidents = tenantIncidents?.Count(x => !x.RemediatedAt.HasValue) ?? 0;
            var criticalIncidents = tenantIncidents?.Count(x => x.Severity == DataBreachSeverity.CRITICAL && !x.RemediatedAt.HasValue) ?? 0;

            var priority = ResolveInstitutionPriority(overdue, dueSoon, capital?.CapitalScore, criticalIncidents, modelReviewCount);

            rows.Add(new InstitutionScorecardRow
            {
                InstitutionId = institution.Id,
                TenantId = institution.TenantId,
                InstitutionName = institution.InstitutionName,
                LicenceType = institution.LicenseType ?? "Unknown",
                OverdueObligations = overdue,
                DueSoonObligations = dueSoon,
                CapitalScore = capital?.CapitalScore,
                OpenResilienceIncidents = openIncidents,
                OpenSecurityAlerts = openAlertCount,
                ModelReviewItems = modelReviewCount,
                Priority = priority,
                Summary = BuildInstitutionSummary(overdue, dueSoon, capital?.CapitalScore, openIncidents, modelReviewCount)
            });
        }

        return rows
            .OrderByDescending(x => InstitutionPriorityRank(x.Priority))
            .ThenByDescending(x => x.OverdueObligations)
            .ThenBy(x => x.InstitutionName, StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();
    }

    private static List<ActivityTimelineRow> BuildActivityTimeline(
        IReadOnlyList<Submission> submissions,
        IReadOnlyList<Institution> institutions,
        IReadOnlyList<DataBreachIncident> incidents,
        IReadOnlyList<SecurityAlert> securityAlerts,
        IReadOnlyList<AuditLogEntry> auditLog,
        IReadOnlyList<FieldChangeHistory> fieldChanges)
    {
        var institutionById = institutions.ToDictionary(x => x.Id);

        var submissionItems = submissions.Take(18).Select(submission =>
        {
            institutionById.TryGetValue(submission.InstitutionId, out var institution);
            return new ActivityTimelineRow
            {
                TenantId = submission.TenantId,
                InstitutionId = submission.InstitutionId,
                Domain = "Submission",
                Title = $"{submission.ReturnCode} {submission.Status}",
                Detail = institution is null
                    ? $"Submission #{submission.Id}"
                    : $"{institution.InstitutionName} · Submission #{submission.Id}",
                HappenedAt = submission.SubmittedAt,
                Severity = submission.Status switch
                {
                    SubmissionStatus.Rejected or SubmissionStatus.ApprovalRejected => "High",
                    SubmissionStatus.PendingApproval => "Medium",
                    _ => "Low"
                }
            };
        });

        var incidentItems = incidents.Select(incident => new ActivityTimelineRow
        {
            TenantId = incident.TenantId,
            Domain = "Resilience",
            Title = incident.Title,
            Detail = $"{incident.Status} · {incident.Severity}",
            HappenedAt = incident.DetectedAt,
            Severity = incident.Severity switch
            {
                DataBreachSeverity.CRITICAL => "Critical",
                DataBreachSeverity.HIGH => "High",
                DataBreachSeverity.MEDIUM => "Medium",
                _ => "Low"
            }
        });

        var alertItems = securityAlerts.Select(alert => new ActivityTimelineRow
        {
            TenantId = alert.TenantId,
            Domain = "Cyber",
            Title = alert.Title,
            Detail = $"{alert.AlertType} · {alert.Status}",
            HappenedAt = alert.CreatedAt,
            Severity = Capitalize(alert.Severity)
        });

        var auditItems = auditLog.Select(entry => new ActivityTimelineRow
        {
            TenantId = entry.TenantId,
            Domain = "Governance",
            Title = $"{entry.EntityType} {entry.Action}",
            Detail = $"Performed by {entry.PerformedBy}",
            HappenedAt = entry.PerformedAt,
            Severity = entry.Action.Contains("delete", StringComparison.OrdinalIgnoreCase)
                || entry.Action.Contains("rollback", StringComparison.OrdinalIgnoreCase)
                ? "High"
                : "Medium"
        });

        var fieldItems = fieldChanges.Select(change => new ActivityTimelineRow
        {
            Domain = "Data",
            TenantId = change.TenantId,
            Title = $"{change.ReturnCode}.{change.FieldName}",
            Detail = $"{change.ChangeSource} by {change.ChangedBy}",
            HappenedAt = change.ChangedAt,
            Severity = change.ChangeSource.Equals("System", StringComparison.OrdinalIgnoreCase) ? "Medium" : "Low"
        });

        return submissionItems
            .Concat(incidentItems)
            .Concat(alertItems)
            .Concat(auditItems)
            .Concat(fieldItems)
            .OrderByDescending(x => x.HappenedAt)
            .Take(24)
            .ToList();
    }

    private static List<InstitutionIntelligenceDetail> BuildInstitutionDetails(
        IReadOnlyList<InstitutionScorecardRow> scorecards,
        IReadOnlyList<KnowledgeGraphInstitutionObligationRow> institutionObligations,
        IReadOnlyList<Submission> submissions,
        IReadOnlyList<ActivityTimelineRow> activityTimeline,
        IReadOnlyList<CapitalWatchlistRow> capitalRows,
        IReadOnlyList<Institution> institutions)
    {
        var capitalByInstitution = capitalRows
            .Where(x => x.InstitutionId.HasValue)
            .GroupBy(x => x.InstitutionId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var institutionById = institutions.ToDictionary(x => x.Id);
        var obligationsByInstitution = institutionObligations
            .GroupBy(x => x.InstitutionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var submissionsByInstitution = submissions
            .GroupBy(x => x.InstitutionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.SubmittedAt).ToList());

        return scorecards
            .Select(scorecard =>
            {
                capitalByInstitution.TryGetValue(scorecard.InstitutionId, out var capital);
                obligationsByInstitution.TryGetValue(scorecard.InstitutionId, out var obligations);
                submissionsByInstitution.TryGetValue(scorecard.InstitutionId, out var submissionHistory);
                institutionById.TryGetValue(scorecard.InstitutionId, out var institution);

                var recentActivity = activityTimeline
                    .Where(x => (x.InstitutionId.HasValue && x.InstitutionId.Value == scorecard.InstitutionId)
                                || (x.TenantId.HasValue && x.TenantId.Value == scorecard.TenantId))
                    .OrderByDescending(x => x.HappenedAt)
                    .Take(8)
                    .ToList();

                return new InstitutionIntelligenceDetail
                {
                    InstitutionId = scorecard.InstitutionId,
                    TenantId = scorecard.TenantId,
                    InstitutionName = scorecard.InstitutionName,
                    InstitutionCode = institution?.InstitutionCode ?? string.Empty,
                    LicenceType = scorecard.LicenceType,
                    Priority = scorecard.Priority,
                    Summary = scorecard.Summary,
                    CapitalScore = scorecard.CapitalScore,
                    CapitalAlert = capital?.Alert ?? "No capital watchlist issue currently detected.",
                    OverdueObligations = scorecard.OverdueObligations,
                    DueSoonObligations = scorecard.DueSoonObligations,
                    OpenResilienceIncidents = scorecard.OpenResilienceIncidents,
                    OpenSecurityAlerts = scorecard.OpenSecurityAlerts,
                    ModelReviewItems = scorecard.ModelReviewItems,
                    TopObligations = obligations?
                        .OrderByDescending(x => x.Status is "Overdue" or "Attention Required")
                        .ThenBy(x => x.NextDeadline)
                        .Take(8)
                        .ToList()
                        ?? [],
                    RecentSubmissions = submissionHistory?
                        .Take(8)
                        .Select(x => new InstitutionSubmissionSummaryRow
                        {
                            SubmissionId = x.Id,
                            ReturnCode = x.ReturnCode,
                            Status = x.Status.ToString(),
                            SubmittedAt = x.SubmittedAt
                        })
                        .ToList()
                        ?? [],
                    RecentActivity = recentActivity
                };
            })
            .ToList();
    }

    private static List<InterventionQueueRow> BuildInterventionQueue(
        IReadOnlyList<KnowledgeGraphInstitutionObligationRow> institutionObligations,
        IReadOnlyList<CapitalWatchlistRow> capitalRows,
        IReadOnlyList<ResilienceActionRow> resilienceActions,
        IReadOnlyList<ModelChangeRow> modelChanges)
    {
        var filingInterventions = institutionObligations
            .Where(x => x.Status is "Overdue" or "Attention Required" or "Due Soon")
            .Select(x => new InterventionQueueRow
            {
                Domain = "Filing",
                Subject = x.InstitutionName,
                Signal = $"{x.ReturnCode} is {x.Status}",
                Priority = x.Status switch
                {
                    "Overdue" => "Critical",
                    "Attention Required" => "High",
                    _ => "Medium"
                },
                NextAction = x.Status == "Attention Required"
                    ? "Resolve validation or approval blocker and refile."
                    : "Escalate filing follow-up and confirm submission plan.",
                DueDate = x.NextDeadline,
                OwnerLane = "Supervision Ops"
            });

        var capitalInterventions = capitalRows
            .Where(x => x.CapitalScore < 60m)
            .Select(x => new InterventionQueueRow
            {
                Domain = "Capital",
                Subject = x.InstitutionName,
                Signal = $"Capital score at {x.CapitalScore:0.0}",
                Priority = x.CapitalScore < 45m ? "Critical" : "High",
                NextAction = x.Alert,
                DueDate = DateTime.UtcNow.Date.AddDays(x.CapitalScore < 45m ? 7 : 21),
                OwnerLane = "Prudential Supervision"
            });

        var resilienceInterventions = resilienceActions
            .Where(x => x.Status != "Complete")
            .Select(x => new InterventionQueueRow
            {
                Domain = "Resilience",
                Subject = x.Title,
                Signal = $"{x.Workstream} {x.Status.ToLowerInvariant()}",
                Priority = x.Status switch
                {
                    "Overdue" => "Critical",
                    "Open" => "High",
                    _ => "Medium"
                },
                NextAction = x.Action,
                DueDate = x.DueDate,
                OwnerLane = x.OwnerLane
            });

        var modelInterventions = modelChanges
            .Where(x => x.ReviewSignal is "Critical" or "Review")
            .Select(x => new InterventionQueueRow
            {
                Domain = "Model Risk",
                Subject = x.Artifact,
                Signal = $"{x.ChangeType} in {x.Area}",
                Priority = x.ReviewSignal == "Critical" ? "High" : "Medium",
                NextAction = x.ReviewSignal == "Critical"
                    ? "Run validation review, impact analysis, and approval evidence check."
                    : "Confirm governance sign-off and parallel-run evidence.",
                DueDate = x.ChangedAt.Date.AddDays(x.ReviewSignal == "Critical" ? 5 : 10),
                OwnerLane = "Model Governance"
            });

        return filingInterventions
            .Concat(capitalInterventions)
            .Concat(resilienceInterventions)
            .Concat(modelInterventions)
            .OrderByDescending(x => InterventionPriorityRank(x.Priority))
            .ThenBy(x => x.DueDate)
            .Take(24)
            .ToList();
    }

    private static DateTime EstimateDeadline(string frequency)
    {
        var now = DateTime.UtcNow;
        return frequency.Trim().ToLowerInvariant() switch
        {
            "weekly" => now.Date.AddDays(7),
            "monthly" => new DateTime(now.Year, now.Month, 1).AddMonths(1).AddDays(29),
            "quarterly" => new DateTime(now.Year, (((now.Month - 1) / 3) + 1) * 3, 1).AddMonths(1).AddDays(44),
            "semiannual" => now.Date.AddDays(60),
            "annual" => new DateTime(now.Year, 12, 31).AddDays(90),
            _ => now.Date.AddDays(30)
        };
    }

    private static ReturnPeriod? SelectRelevantPeriod(IReadOnlyList<ReturnPeriod>? periods)
    {
        if (periods is null || periods.Count == 0)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        return periods
                   .Where(x => x.EffectiveDeadline >= now.AddDays(-30))
                   .OrderBy(x => x.EffectiveDeadline)
                   .FirstOrDefault()
               ?? periods.OrderByDescending(x => x.EffectiveDeadline).First();
    }

    private static string ResolveObligationStatus(ReturnPeriod? period, Submission? submission)
    {
        if (submission is not null)
        {
            return submission.Status switch
            {
                SubmissionStatus.Rejected or SubmissionStatus.ApprovalRejected => "Attention Required",
                SubmissionStatus.PendingApproval => "Pending Approval",
                SubmissionStatus.Draft or SubmissionStatus.Parsing or SubmissionStatus.Validating => "In Progress",
                _ => "Filed"
            };
        }

        if (period is null)
        {
            return "Schedule Pending";
        }

        if (period.EffectiveDeadline < DateTime.UtcNow)
        {
            return "Overdue";
        }

        if (period.EffectiveDeadline <= DateTime.UtcNow.AddDays(7))
        {
            return "Due Soon";
        }

        return period.IsOpen ? "Open" : "Upcoming";
    }

    private static string FormatPeriodLabel(ReturnPeriod period)
    {
        if (period.Quarter.HasValue)
        {
            return $"{period.Year} Q{period.Quarter.Value}";
        }

        if (period.Month > 0)
        {
            return $"{CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(period.Month)} {period.Year}";
        }

        return $"{period.Frequency} {period.Year}";
    }

    private static int ObligationSeverityRank(string status) => status switch
    {
        "Overdue" => 5,
        "Attention Required" => 4,
        "Due Soon" => 3,
        "Pending Approval" => 2,
        "In Progress" => 2,
        "Open" => 1,
        "Upcoming" => 0,
        "Filed" => -1,
        _ => -2
    };

    private static string SubmissionLookupKey(int institutionId, string returnCode) =>
        $"{institutionId}|{returnCode.Trim().ToUpperInvariant()}";

    private static string ResolveRegulationFamily(string regulatoryReference)
    {
        if (string.IsNullOrWhiteSpace(regulatoryReference))
        {
            return string.Empty;
        }

        var tokens = regulatoryReference
            .Split(['-', '/', '.', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (tokens.Count == 0)
        {
            return regulatoryReference.Trim().ToUpperInvariant();
        }

        if (tokens.Count >= 2 && tokens[1].All(char.IsDigit))
        {
            return $"{tokens[0].ToUpperInvariant()}-{tokens[1]}";
        }

        if (tokens.Count >= 2 && tokens[0].Length <= 6 && tokens[1].Length <= 10)
        {
            return $"{tokens[0].ToUpperInvariant()}-{tokens[1].ToUpperInvariant()}";
        }

        return tokens[0].ToUpperInvariant();
    }

    private static string ResolveImportantBusinessServiceName(CyberAsset asset)
    {
        var combined = $"{asset.DisplayName} {asset.AssetType}";

        if (combined.Contains("report", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("return", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("submission", StringComparison.OrdinalIgnoreCase))
        {
            return "Regulatory Reporting Operations";
        }

        if (combined.Contains("treasury", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("fx", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("payment", StringComparison.OrdinalIgnoreCase))
        {
            return "Treasury and FX Services";
        }

        if (combined.Contains("identity", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("auth", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("iam", StringComparison.OrdinalIgnoreCase))
        {
            return "Identity and Access Services";
        }

        if (combined.Contains("network", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("connect", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("gateway", StringComparison.OrdinalIgnoreCase))
        {
            return "Connectivity and Gateway Services";
        }

        if (combined.Contains("backup", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("recovery", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("storage", StringComparison.OrdinalIgnoreCase))
        {
            return "Recovery and Data Protection Services";
        }

        if (combined.Contains("database", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("data", StringComparison.OrdinalIgnoreCase))
        {
            return "Regulatory Data Platform";
        }

        return string.IsNullOrWhiteSpace(asset.AssetType)
            ? asset.DisplayName
            : $"{Capitalize(asset.AssetType)} Services";
    }

    private static string ResolveBusinessServiceOwner(string assetType)
    {
        if (assetType.Contains("database", StringComparison.OrdinalIgnoreCase)
            || assetType.Contains("data", StringComparison.OrdinalIgnoreCase)
            || assetType.Contains("report", StringComparison.OrdinalIgnoreCase))
        {
            return "Regulatory Operations";
        }

        if (assetType.Contains("identity", StringComparison.OrdinalIgnoreCase)
            || assetType.Contains("auth", StringComparison.OrdinalIgnoreCase)
            || assetType.Contains("security", StringComparison.OrdinalIgnoreCase))
        {
            return "Cyber Security";
        }

        if (assetType.Contains("network", StringComparison.OrdinalIgnoreCase)
            || assetType.Contains("gateway", StringComparison.OrdinalIgnoreCase))
        {
            return "Infrastructure";
        }

        return "Technology and Operations";
    }

    private static string? ResolveThirdPartyProviderName(CyberAsset asset)
    {
        var metadataProvider = TryExtractMetadataValue(asset.MetadataJson, "providerName", "provider", "vendor", "supplier", "partner");
        if (!string.IsNullOrWhiteSpace(metadataProvider))
        {
            return metadataProvider.Trim();
        }

        var combined = $"{asset.DisplayName} {asset.AssetType}";
        return combined.ToUpperInvariant() switch
        {
            var value when value.Contains("AWS") || value.Contains("AMAZON") => "Amazon Web Services",
            var value when value.Contains("AZURE") || value.Contains("MICROSOFT") => "Microsoft Azure",
            var value when value.Contains("GCP") || value.Contains("GOOGLE CLOUD") || value.Contains("GOOGLE") => "Google Cloud",
            var value when value.Contains("ORACLE") => "Oracle",
            var value when value.Contains("SWIFT") => "SWIFT",
            var value when value.Contains("NIBSS") => "NIBSS",
            var value when value.Contains("INTERSWITCH") => "Interswitch",
            var value when value.Contains("FLUTTERWAVE") => "Flutterwave",
            var value when value.Contains("CLOUDFLARE") => "Cloudflare",
            var value when value.Contains("MAINONE") => "MainOne",
            var value when value.Contains("RACKCENTRE") => "Rack Centre",
            _ when IsLikelyThirdPartyAsset(asset) => string.IsNullOrWhiteSpace(asset.DisplayName) ? Capitalize(asset.AssetType) : asset.DisplayName.Trim(),
            _ => null
        };
    }

    private static string ResolveThirdPartyProviderType(CyberAsset asset)
    {
        var combined = $"{asset.DisplayName} {asset.AssetType}";
        if (combined.Contains("cloud", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("aws", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("azure", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("google", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("oracle", StringComparison.OrdinalIgnoreCase))
        {
            return "Cloud / Hosting";
        }

        if (combined.Contains("gateway", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("switch", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("payment", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("swift", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("nibss", StringComparison.OrdinalIgnoreCase))
        {
            return "Network / Payments";
        }

        if (combined.Contains("security", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("identity", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("auth", StringComparison.OrdinalIgnoreCase))
        {
            return "Security Service";
        }

        if (combined.Contains("backup", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("storage", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("recovery", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("data center", StringComparison.OrdinalIgnoreCase))
        {
            return "Recovery / Data Centre";
        }

        return "Third-Party Service";
    }

    private static int CountRelatedServiceSignals(
        CyberAsset asset,
        IReadOnlyList<DataBreachIncident> incidents,
        IReadOnlyList<SecurityAlert> securityAlerts)
    {
        var tokens = ResolveMatchTokens(asset.DisplayName, asset.AssetType);
        var incidentCount = incidents.Count(incident =>
            tokens.Any(token =>
                incident.Title.Contains(token, StringComparison.OrdinalIgnoreCase)
                || incident.Description.Contains(token, StringComparison.OrdinalIgnoreCase)));

        var alertCount = securityAlerts.Count(alert =>
            tokens.Any(token =>
                alert.Title.Contains(token, StringComparison.OrdinalIgnoreCase)
                || alert.AlertType.Contains(token, StringComparison.OrdinalIgnoreCase)));

        return incidentCount + alertCount;
    }

    private static List<ResilienceTestingRow> FindLinkedResilienceTests(
        string serviceName,
        string assetType,
        IReadOnlyList<ResilienceTestingRow> resilienceTests)
    {
        var tokens = ResolveMatchTokens(serviceName, assetType);
        var matches = resilienceTests
            .Where(test => tokens.Any(token =>
                test.ScenarioTitle.Contains(token, StringComparison.OrdinalIgnoreCase)
                || test.TestType.Contains(token, StringComparison.OrdinalIgnoreCase)
                || test.Scope.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (matches.Count > 0)
        {
            return matches;
        }

        var fallbackType = assetType.Contains("identity", StringComparison.OrdinalIgnoreCase)
                           || assetType.Contains("security", StringComparison.OrdinalIgnoreCase)
                           || assetType.Contains("network", StringComparison.OrdinalIgnoreCase)
            ? "Cyber Resilience"
            : assetType.Contains("backup", StringComparison.OrdinalIgnoreCase)
              || assetType.Contains("storage", StringComparison.OrdinalIgnoreCase)
              || assetType.Contains("recovery", StringComparison.OrdinalIgnoreCase)
                ? "Recovery"
                : "Operational Resilience";

        return resilienceTests
            .Where(test => test.TestType.Equals(fallbackType, StringComparison.OrdinalIgnoreCase)
                        || test.TestType.Equals("Business Continuity", StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
    }

    private static string ResolveThirdPartyLatestOutcome(
        IReadOnlyList<ImportantBusinessServiceRow> linkedServices,
        IReadOnlyList<ResilienceTestingRow> resilienceTests)
    {
        var outcomes = linkedServices
            .Select(x => x.LatestTestOutcome)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (outcomes.Count == 0)
        {
            outcomes = resilienceTests
                .Where(x => x.TestType == "Third-Party")
                .Select(x => x.Outcome)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(3)
                .ToList();
        }

        if (outcomes.Any(x => x is "Weaknesses Found" or "Control Gap" or "Escalate"))
        {
            return outcomes.First(x => x is "Weaknesses Found" or "Control Gap" or "Escalate");
        }

        if (outcomes.Any(x => x is "Awaiting Run" or "In Progress"))
        {
            return outcomes.First(x => x is "Awaiting Run" or "In Progress");
        }

        return outcomes.FirstOrDefault() ?? "Awaiting Run";
    }

    private static string ResolveThirdPartySignal(
        int criticalServiceCount,
        int serviceCount,
        int dependentAssetCount,
        string latestTestOutcome)
    {
        if (latestTestOutcome is "Weaknesses Found" or "Control Gap" or "Escalate"
            || criticalServiceCount >= 2
            || dependentAssetCount >= 4
            || serviceCount >= 4)
        {
            return "Critical";
        }

        if (latestTestOutcome is "Awaiting Run" or "In Progress"
            || criticalServiceCount >= 1
            || dependentAssetCount >= 2
            || serviceCount >= 2)
        {
            return "Watch";
        }

        return "Current";
    }

    private static string BuildThirdPartyCommentary(
        string providerName,
        IReadOnlyList<ImportantBusinessServiceRow> linkedServices,
        int dependentAssetCount,
        string latestTestOutcome)
    {
        var serviceSummary = linkedServices.Count == 0
            ? "No important business service is directly linked yet."
            : linkedServices.Count == 1
                ? $"{linkedServices[0].ServiceName} currently depends on this provider."
                : $"{linkedServices.Count} important services depend on this provider, led by {string.Join(", ", linkedServices.Take(2).Select(x => x.ServiceName))}.";

        return $"{serviceSummary} {dependentAssetCount} dependent asset path(s) currently trace to {providerName}. Latest evidence: {latestTestOutcome}.";
    }

    private static List<string> ResolveMatchTokens(string primary, string secondary)
    {
        return $"{primary} {secondary}"
            .Split([' ', '-', '_', '/', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool MatchesAnyToken(string value, params string[] tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static int EstimateMaximumDisruptionMinutes(string? criticality, int dependencyCount)
    {
        var baseline = criticality?.ToLowerInvariant() switch
        {
            "critical" => 120,
            "high" => 240,
            "medium" => 480,
            _ => 720
        };

        var dependencyModifier = dependencyCount switch
        {
            >= 4 => 0.55m,
            3 => 0.7m,
            2 => 0.85m,
            _ => 1m
        };

        return Math.Max(30, (int)Math.Round(baseline * dependencyModifier, MidpointRounding.AwayFromZero));
    }

    private static bool IsLikelyThirdPartyAsset(CyberAsset asset)
    {
        var combined = $"{asset.DisplayName} {asset.AssetType}";
        return combined.Contains("cloud", StringComparison.OrdinalIgnoreCase)
               || combined.Contains("vendor", StringComparison.OrdinalIgnoreCase)
               || combined.Contains("provider", StringComparison.OrdinalIgnoreCase)
               || combined.Contains("gateway", StringComparison.OrdinalIgnoreCase)
               || combined.Contains("switch", StringComparison.OrdinalIgnoreCase)
               || combined.Contains("swift", StringComparison.OrdinalIgnoreCase)
               || combined.Contains("nibss", StringComparison.OrdinalIgnoreCase)
               || combined.Contains("saas", StringComparison.OrdinalIgnoreCase)
               || combined.Contains("host", StringComparison.OrdinalIgnoreCase)
               || combined.Contains("data center", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryExtractMetadataValue(string? metadataJson, params string[] keys)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            return TryExtractMetadataValue(document.RootElement, keys);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryExtractMetadataValue(JsonElement element, IReadOnlyList<string> keys)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (keys.Any(key => property.Name.Equals(key, StringComparison.OrdinalIgnoreCase)))
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    var nested = TryExtractMetadataValue(property.Value, ["name", "provider", "vendor"]);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }
            }

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                var nested = TryExtractMetadataValue(property.Value, keys);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string ResolveImportantServiceStatus(
        string? criticality,
        int dependencyCount,
        int signalCount,
        string? latestOutcome)
    {
        if (signalCount >= 2 || latestOutcome is "Weaknesses Found" or "Control Gap" or "Escalate")
        {
            return "Attention Required";
        }

        if (dependencyCount >= 3
            || string.Equals(criticality, "critical", StringComparison.OrdinalIgnoreCase)
            || latestOutcome is "Awaiting Run" or "In Progress")
        {
            return "Watch";
        }

        return "Current";
    }

    private static int? EstimateRecoveryProxyMinutes(int targetMinutes, string? outcome, string? status)
    {
        if (targetMinutes <= 0)
        {
            return null;
        }

        var multiplier = outcome switch
        {
            "Weaknesses Found" => 1.35m,
            "Control Gap" => 1.5m,
            "Escalate" => 1.7m,
            "In Progress" => 1.1m,
            "Awaiting Run" => 1.15m,
            "Stable" => 0.9m,
            _ => status switch
            {
                "Running" => 1.05m,
                "Scheduled" => 1.1m,
                "Complete" => 0.95m,
                _ => 1m
            }
        };

        return Math.Max(15, (int)Math.Round(targetMinutes * multiplier, MidpointRounding.AwayFromZero));
    }

    private static bool IsCriticalAsset(string? value) =>
        value is not null
        && (value.Equals("high", StringComparison.OrdinalIgnoreCase)
            || value.Equals("critical", StringComparison.OrdinalIgnoreCase));

    private static int CriticalityRank(string? criticality) => criticality?.ToLowerInvariant() switch
    {
        "critical" => 3,
        "high" => 2,
        "medium" => 1,
        _ => 0
    };

    private static int ResiliencePriorityRank(string status, string severity)
    {
        var statusRank = status switch
        {
            "Overdue" => 4,
            "Open" => 3,
            "Scheduled" => 2,
            "Complete" => 1,
            _ => 0
        };

        return statusRank * 10 + CriticalityRank(severity);
    }

    private static string ResolveModelChangeArea(string entityType) => entityType.ToLowerInvariant() switch
    {
        var value when value.Contains("formula") => "Model Logic",
        var value when value.Contains("rule") => "Validation Logic",
        var value when value.Contains("template") => "Template Structure",
        _ => "Governance"
    };

    private static string ResolveModelReviewSignal(string entityType, string action)
    {
        if (entityType.Contains("formula", StringComparison.OrdinalIgnoreCase)
            || entityType.Contains("cross_sheet_rule", StringComparison.OrdinalIgnoreCase)
            || entityType.Contains("business_rule", StringComparison.OrdinalIgnoreCase))
        {
            return "Critical";
        }

        if (action.Contains("delete", StringComparison.OrdinalIgnoreCase)
            || action.Contains("rollback", StringComparison.OrdinalIgnoreCase))
        {
            return "Review";
        }

        return "Monitor";
    }

    private static int ModelReviewRank(string reviewSignal) => reviewSignal switch
    {
        "Critical" => 3,
        "Review" => 2,
        _ => 1
    };

    private static int InterventionPriorityRank(string priority) => priority switch
    {
        "Critical" => 4,
        "High" => 3,
        "Medium" => 2,
        _ => 1
    };

    private static string ResolveInstitutionPriority(
        int overdue,
        int dueSoon,
        decimal? capitalScore,
        int criticalIncidents,
        int modelReviewCount)
    {
        if (overdue > 0 || (capitalScore.HasValue && capitalScore.Value < 45m) || criticalIncidents > 0)
        {
            return "Critical";
        }

        if (dueSoon > 0 || (capitalScore.HasValue && capitalScore.Value < 60m) || modelReviewCount > 0)
        {
            return "High";
        }

        return "Monitor";
    }

    private static int InstitutionPriorityRank(string priority) => priority switch
    {
        "Critical" => 3,
        "High" => 2,
        _ => 1
    };

    private static string BuildInstitutionSummary(
        int overdue,
        int dueSoon,
        decimal? capitalScore,
        int openIncidents,
        int modelReviewCount)
    {
        var fragments = new List<string>();
        if (overdue > 0)
        {
            fragments.Add($"{overdue} overdue obligation(s)");
        }
        else if (dueSoon > 0)
        {
            fragments.Add($"{dueSoon} obligation(s) due soon");
        }

        if (capitalScore.HasValue)
        {
            fragments.Add($"capital {capitalScore.Value:0.0}");
        }

        if (openIncidents > 0)
        {
            fragments.Add($"{openIncidents} open resilience incident(s)");
        }

        if (modelReviewCount > 0)
        {
            fragments.Add($"{modelReviewCount} governance review item(s)");
        }

        return fragments.Count == 0
            ? "No immediate cross-track pressure detected."
            : string.Join(" · ", fragments);
    }

    private static string Capitalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    private static decimal Median(IEnumerable<decimal> values)
    {
        var ordered = values.OrderBy(x => x).ToList();
        if (ordered.Count == 0)
        {
            return 0m;
        }

        var midpoint = ordered.Count / 2;
        if (ordered.Count % 2 == 0)
        {
            return Math.Round((ordered[midpoint - 1] + ordered[midpoint]) / 2m, 1);
        }

        return Math.Round(ordered[midpoint], 1);
    }

    private static SanctionsScoredMatch ScoreSubject(string subject, SanctionsWatchlistEntry entry)
    {
        var aliases = new[] { entry.PrimaryName }.Concat(entry.Aliases).ToList();
        var best = aliases
            .Select(alias => new
            {
                Alias = alias,
                Normalized = Normalize(alias),
                JaroWinkler = JaroWinkler(Normalize(subject), Normalize(alias)),
                Levenshtein = Similarity(Normalize(subject), Normalize(alias)),
                Soundex = Soundex(Normalize(subject)) == Soundex(Normalize(alias))
            })
            .OrderByDescending(x => x.JaroWinkler)
            .ThenByDescending(x => x.Levenshtein)
            .First();

        var score = Math.Max(best.JaroWinkler, best.Levenshtein);
        if (best.Soundex)
        {
            score = Math.Max(score, 0.88d);
        }

        return new SanctionsScoredMatch(
            entry,
            best.Alias,
            score,
            string.Equals(Normalize(subject), best.Normalized, StringComparison.Ordinal));
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(value.Length);
        foreach (var character in value.ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer.Append(character);
            }
        }

        return buffer.ToString();
    }

    private static double Similarity(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return 0d;
        }

        var distance = LevenshteinDistance(a, b);
        return 1d - (double)distance / Math.Max(a.Length, b.Length);
    }

    private static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var cost = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
        {
            cost[i, 0] = i;
        }

        for (var j = 0; j <= b.Length; j++)
        {
            cost[0, j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = a[i - 1] == b[j - 1] ? 0 : 1;
                cost[i, j] = Math.Min(
                    Math.Min(cost[i - 1, j] + 1, cost[i, j - 1] + 1),
                    cost[i - 1, j - 1] + substitution);
            }
        }

        return cost[a.Length, b.Length];
    }

    private static double JaroWinkler(string source, string target)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
        {
            return 0d;
        }

        if (source == target)
        {
            return 1d;
        }

        var matchDistance = Math.Max(source.Length, target.Length) / 2 - 1;
        var sourceMatches = new bool[source.Length];
        var targetMatches = new bool[target.Length];

        var matches = 0;
        for (var i = 0; i < source.Length; i++)
        {
            var start = Math.Max(0, i - matchDistance);
            var end = Math.Min(i + matchDistance + 1, target.Length);

            for (var j = start; j < end; j++)
            {
                if (targetMatches[j] || source[i] != target[j])
                {
                    continue;
                }

                sourceMatches[i] = true;
                targetMatches[j] = true;
                matches++;
                break;
            }
        }

        if (matches == 0)
        {
            return 0d;
        }

        var transpositions = 0;
        for (int i = 0, k = 0; i < source.Length; i++)
        {
            if (!sourceMatches[i])
            {
                continue;
            }

            while (!targetMatches[k])
            {
                k++;
            }

            if (source[i] != target[k])
            {
                transpositions++;
            }

            k++;
        }

        var jaro = ((matches / (double)source.Length)
                    + (matches / (double)target.Length)
                    + ((matches - transpositions / 2d) / matches))
                   / 3d;

        var prefixLength = 0;
        for (var i = 0; i < Math.Min(4, Math.Min(source.Length, target.Length)); i++)
        {
            if (source[i] != target[i])
            {
                break;
            }

            prefixLength++;
        }

        return jaro + prefixLength * 0.1d * (1d - jaro);
    }

    private static string Soundex(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var first = input[0];
        var lastCode = EncodeSoundex(first);
        var result = new StringBuilder(4);
        result.Append(first);

        for (var i = 1; i < input.Length && result.Length < 4; i++)
        {
            var code = EncodeSoundex(input[i]);
            if (code == '0' || code == lastCode)
            {
                lastCode = code;
                continue;
            }

            result.Append(code);
            lastCode = code;
        }

        while (result.Length < 4)
        {
            result.Append('0');
        }

        return result.ToString();
    }

    private static char EncodeSoundex(char value) => value switch
    {
        'B' or 'F' or 'P' or 'V' => '1',
        'C' or 'G' or 'J' or 'K' or 'Q' or 'S' or 'X' or 'Z' => '2',
        'D' or 'T' => '3',
        'L' => '4',
        'M' or 'N' => '5',
        'R' => '6',
        _ => '0'
    };

    private readonly record struct FieldLineageSource(
        TemplateField Field,
        TemplateVersion Version,
        ReturnTemplate Template,
        Module? Module);

    private readonly record struct ModelInventorySeed(
        string ModelCode,
        string ModelName,
        string Tier,
        string Owner,
        string ReturnHint,
        IReadOnlyList<string> MatchTerms);

    private readonly record struct SanctionsScoredMatch(
        SanctionsWatchlistEntry Entry,
        string MatchedAlias,
        double Score,
        bool ExactMatch);

    private static bool IsResilienceScenario(PolicyScenario scenario) =>
        scenario.PolicyDomain == PolicyDomain.RiskManagement
        || scenario.Title.Contains("resilien", StringComparison.OrdinalIgnoreCase)
        || scenario.Title.Contains("cyber", StringComparison.OrdinalIgnoreCase)
        || scenario.Title.Contains("recovery", StringComparison.OrdinalIgnoreCase)
        || scenario.Title.Contains("outage", StringComparison.OrdinalIgnoreCase)
        || scenario.Title.Contains("continuity", StringComparison.OrdinalIgnoreCase)
        || (scenario.Description?.Contains("resilien", StringComparison.OrdinalIgnoreCase) ?? false)
        || (scenario.Description?.Contains("cyber", StringComparison.OrdinalIgnoreCase) ?? false)
        || (scenario.Description?.Contains("outage", StringComparison.OrdinalIgnoreCase) ?? false);

    private static string ResolveResilienceTestType(PolicyScenario scenario)
    {
        var title = $"{scenario.Title} {scenario.Description}";
        if (title.Contains("cyber", StringComparison.OrdinalIgnoreCase))
        {
            return "Cyber Resilience";
        }

        if (title.Contains("continuity", StringComparison.OrdinalIgnoreCase) || title.Contains("recovery", StringComparison.OrdinalIgnoreCase))
        {
            return "Business Continuity";
        }

        if (title.Contains("third", StringComparison.OrdinalIgnoreCase) || title.Contains("vendor", StringComparison.OrdinalIgnoreCase))
        {
            return "Third-Party";
        }

        if (title.Contains("outage", StringComparison.OrdinalIgnoreCase) || title.Contains("failover", StringComparison.OrdinalIgnoreCase))
        {
            return "Recovery";
        }

        return "Operational Resilience";
    }

    private static string ResolveScenarioModelCode(PolicyScenario scenario)
    {
        var title = $"{scenario.Title} {scenario.Description}";
        return title.Contains("climate", StringComparison.OrdinalIgnoreCase)
               || title.Contains("transition", StringComparison.OrdinalIgnoreCase)
               || title.Contains("esg", StringComparison.OrdinalIgnoreCase)
            ? "CLIMATE"
            : "STRESS";
    }

    private static bool MatchesModelSeed(string returnCode, ModelInventorySeed seed) =>
        returnCode.Contains(seed.ReturnHint, StringComparison.OrdinalIgnoreCase)
        || seed.MatchTerms.Any(term => returnCode.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static int ResilienceTestPriorityRank(string status, string outcome)
    {
        if (status is "Overdue" or "Due")
        {
            return 4;
        }

        if (outcome is "Weaknesses Found" or "Control Gap" or "Escalate")
        {
            return 3;
        }

        if (status == "Running")
        {
            return 2;
        }

        return 1;
    }

    private static int PerformanceSignalRank(string signal) => signal switch
    {
        "Alert" => 4,
        "Watch" => 3,
        "Monitor" => 2,
        _ => 1
    };

    private static int KnowledgeImpactPriorityRank(string signal) => signal switch
    {
        "Critical" => 3,
        "Watch" => 2,
        _ => 1
    };

    private static int ModelAppetitePriorityRank(string appetiteStatus) => appetiteStatus switch
    {
        "Breach" => 3,
        "Watch" => 2,
        _ => 1
    };

    private static ModelInventoryRow ResolveModelForChange(
        ModelChangeRow change,
        IReadOnlyList<ModelInventoryRow> inventory)
    {
        var haystack = $"{change.Area} {change.Artifact} {change.ChangeType}";
        var match = inventory.FirstOrDefault(model =>
            haystack.Contains(model.ModelCode, StringComparison.OrdinalIgnoreCase)
            || haystack.Contains(model.ModelName, StringComparison.OrdinalIgnoreCase)
            || haystack.Contains(model.Owner, StringComparison.OrdinalIgnoreCase));

        return match
               ?? inventory.FirstOrDefault(model => model.PerformanceStatus is "Alert" or "Watch")
               ?? inventory.First();
    }

    private static string ResolveGapSignal(decimal score) => score switch
    {
        < 50m => "Critical",
        < 70m => "Watch",
        _ => "Healthy"
    };
}

public sealed class PlatformIntelligenceWorkspace
{
    public DateTime GeneratedAt { get; set; }
    public PlatformIntelligenceHero Hero { get; set; } = new();
    public KnowledgeGraphSnapshot KnowledgeGraph { get; set; } = new();
    public CapitalManagementSnapshot Capital { get; set; } = new();
    public SanctionsSnapshot Sanctions { get; set; } = new();
    public OperationalResilienceSnapshot Resilience { get; set; } = new();
    public ModelRiskSnapshot ModelRisk { get; set; } = new();
    public List<InstitutionScorecardRow> InstitutionScorecards { get; set; } = [];
    public List<InstitutionIntelligenceDetail> InstitutionDetails { get; set; } = [];
    public List<ActivityTimelineRow> ActivityTimeline { get; set; } = [];
    public List<InterventionQueueRow> Interventions { get; set; } = [];
}

public sealed class PlatformIntelligenceHero
{
    public int KnowledgeGraphNodes { get; set; }
    public int ActiveCapitalAlerts { get; set; }
    public int WatchlistSources { get; set; }
    public int OpenResilienceIncidents { get; set; }
    public int ModelsUnderGovernance { get; set; }
    public int PriorityInterventions { get; set; }
}

public sealed class KnowledgeGraphSnapshot
{
    public int RegulatorCount { get; set; }
    public int ModuleCount { get; set; }
    public int TemplateCount { get; set; }
    public int FieldCount { get; set; }
    public int ReferencedFieldCount { get; set; }
    public int RequirementCount { get; set; }
    public int ObligationCount { get; set; }
    public int ImpactSurfaceCount { get; set; }
    public int PersistedNodeCount { get; set; }
    public int PersistedEdgeCount { get; set; }
    public DateTime? CatalogMaterializedAt { get; set; }
    public int DossierReadyCount { get; set; }
    public int DossierAttentionCount { get; set; }
    public List<KnowledgeGraphCatalogTypeRow> CatalogNodeTypes { get; set; } = [];
    public List<KnowledgeGraphCatalogTypeRow> CatalogEdgeTypes { get; set; } = [];
    public List<KnowledgeGraphInstitutionOption> InstitutionOptions { get; set; } = [];
    public List<KnowledgeGraphOntologyCoverageRow> OntologyCoverage { get; set; } = [];
    public List<KnowledgeGraphRequirementRegisterRow> RequirementRegister { get; set; } = [];
    public List<KnowledgeGraphLineageRow> Lineage { get; set; } = [];
    public List<KnowledgeGraphNavigatorDetail> NavigatorDetails { get; set; } = [];
    public List<KnowledgeGraphObligationRow> Obligations { get; set; } = [];
    public List<KnowledgeGraphInstitutionObligationRow> InstitutionObligations { get; set; } = [];
    public List<KnowledgeGraphImpactRow> ImpactSurfaces { get; set; } = [];
    public List<KnowledgeGraphImpactPropagationRow> ImpactPropagation { get; set; } = [];
    public List<KnowledgeGraphDossierRow> DossierPack { get; set; } = [];
}

public sealed class KnowledgeGraphOntologyCoverageRow
{
    public string RegulatorCode { get; set; } = string.Empty;
    public int RegulationFamilyCount { get; set; }
    public int RequirementCount { get; set; }
    public int ModuleCount { get; set; }
    public int ReturnCount { get; set; }
    public int InstitutionCount { get; set; }
    public int ObligationCount { get; set; }
    public string PrimaryFilingPath { get; set; } = string.Empty;
}

public sealed class KnowledgeGraphRequirementRegisterRow
{
    public string RegulatoryReference { get; set; } = string.Empty;
    public string RegulationFamily { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = string.Empty;
    public string FiledViaReturns { get; set; } = string.Empty;
    public string AppliesToLicenceTypes { get; set; } = string.Empty;
    public int InstitutionCount { get; set; }
    public int FieldCount { get; set; }
    public string FrequencyProfile { get; set; } = string.Empty;
    public DateTime NextDeadline { get; set; }
}

public sealed class KnowledgeGraphLineageRow
{
    public string NavigatorKey { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = string.Empty;
    public string ReturnCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string FieldCode { get; set; } = string.Empty;
    public string RegulatoryReference { get; set; } = string.Empty;
}

public sealed class KnowledgeGraphNavigatorDetail
{
    public string NavigatorKey { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = string.Empty;
    public string ReturnCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string FieldCode { get; set; } = string.Empty;
    public string RegulatoryReference { get; set; } = string.Empty;
    public int ImpactedTemplates { get; set; }
    public int ImpactedFields { get; set; }
    public int RuleSurfaceCount { get; set; }
    public int AffectedTenantCount { get; set; }
    public int AffectedInstitutionCount { get; set; }
    public int FiledInstitutionCount { get; set; }
    public List<KnowledgeGraphNavigatorInstitutionRow> AffectedInstitutions { get; set; } = [];
    public List<KnowledgeGraphNavigatorSubmissionRow> RecentSubmissions { get; set; } = [];
}

public sealed class KnowledgeGraphNavigatorInstitutionRow
{
    public int InstitutionId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string LicenceType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? LatestSubmissionStatus { get; set; }
    public DateTime? LastSubmittedAt { get; set; }
    public DateTime NextDeadline { get; set; }
}

public sealed class KnowledgeGraphNavigatorSubmissionRow
{
    public int SubmissionId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
}

public sealed class KnowledgeGraphObligationRow
{
    public string LicenceType { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = string.Empty;
    public string ReturnCode { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public int AffectedTenants { get; set; }
    public DateTime NextDeadline { get; set; }
}

public sealed class KnowledgeGraphInstitutionOption
{
    public int InstitutionId { get; set; }
    public Guid TenantId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string LicenceType { get; set; } = string.Empty;
    public int ActiveObligations { get; set; }
    public int FiledCount { get; set; }
    public int OverdueCount { get; set; }
    public int RegulatorCount { get; set; }
}

public sealed class KnowledgeGraphInstitutionObligationRow
{
    public int InstitutionId { get; set; }
    public Guid TenantId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string LicenceType { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = string.Empty;
    public string ReturnCode { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? LatestSubmissionStatus { get; set; }
    public DateTime? LastSubmittedAt { get; set; }
    public DateTime NextDeadline { get; set; }
    public string PeriodLabel { get; set; } = string.Empty;
}

public sealed class KnowledgeGraphImpactRow
{
    public string RegulatoryReference { get; set; } = string.Empty;
    public int ImpactedTemplates { get; set; }
    public int ImpactedFields { get; set; }
    public int ImpactedFormulas { get; set; }
    public int ImpactedCrossSheetRules { get; set; }
    public int ImpactedBusinessRules { get; set; }
    public int AffectedTenants { get; set; }
    public int AffectedInstitutions { get; set; }
}

public sealed class KnowledgeGraphImpactPropagationRow
{
    public string NavigatorKey { get; set; } = string.Empty;
    public string RegulatoryReference { get; set; } = string.Empty;
    public string RegulationFamily { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public string PrimaryReturnCode { get; set; } = string.Empty;
    public int ImpactedTemplates { get; set; }
    public int ImpactedFields { get; set; }
    public int RuleSurfaceCount { get; set; }
    public int AffectedInstitutions { get; set; }
    public int FilingAttentionCount { get; set; }
    public int FiledInstitutionCount { get; set; }
    public DateTime NextDeadline { get; set; }
    public string Signal { get; set; } = string.Empty;
    public string Commentary { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
}

public sealed class KnowledgeGraphDossierRow
{
    public string SectionCode { get; set; } = string.Empty;
    public string SectionName { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public string Signal { get; set; } = string.Empty;
    public string Coverage { get; set; } = string.Empty;
    public string Commentary { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
}

public sealed class KnowledgeGraphCatalogTypeRow
{
    public string Type { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class CapitalManagementSnapshot
{
    public decimal AverageCapitalScore { get; set; }
    public decimal MedianCapitalScore { get; set; }
    public int CapitalWatchlistCount { get; set; }
    public int ActiveScenarioCount { get; set; }
    public List<CapitalActionTemplate> ActionTemplates { get; set; } = [];
    public List<CapitalWatchlistRow> Watchlist { get; set; } = [];
    public List<CapitalPackSectionState> ReturnPack { get; set; } = [];
    public int ReturnPackAttentionCount { get; set; }
    public DateTime? ReturnPackMaterializedAt { get; set; }
}

public sealed class CapitalWatchlistRow
{
    public Guid TenantId { get; set; }
    public int? InstitutionId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public decimal CapitalScore { get; set; }
    public decimal OverallScore { get; set; }
    public string Alert { get; set; } = string.Empty;
}

public sealed class CapitalActionTemplate
{
    public CapitalActionTemplate(
        string code,
        string title,
        string summary,
        string primaryLever,
        decimal capitalActionBn,
        decimal rwaOptimisationPercent,
        decimal quarterlyRetainedEarningsDeltaBn,
        decimal estimatedAnnualCostPercent)
    {
        Code = code;
        Title = title;
        Summary = summary;
        PrimaryLever = primaryLever;
        CapitalActionBn = capitalActionBn;
        RwaOptimisationPercent = rwaOptimisationPercent;
        QuarterlyRetainedEarningsDeltaBn = quarterlyRetainedEarningsDeltaBn;
        EstimatedAnnualCostPercent = estimatedAnnualCostPercent;
    }

    public string Code { get; }
    public string Title { get; }
    public string Summary { get; }
    public string PrimaryLever { get; }
    public decimal CapitalActionBn { get; }
    public decimal RwaOptimisationPercent { get; }
    public decimal QuarterlyRetainedEarningsDeltaBn { get; }
    public decimal EstimatedAnnualCostPercent { get; }
}

public sealed class SanctionsSnapshot
{
    public int SourceCount { get; set; }
    public int EntryCount { get; set; }
    public DateTime LastUpdatedAt { get; set; }
    public int PersistedFalsePositiveCount { get; set; }
    public int PersistedReviewAuditCount { get; set; }
    public DateTime? LastReviewedAt { get; set; }
    public int TfsLinkedFieldCount { get; set; }
    public List<SanctionsWatchlistSource> Sources { get; set; } = [];
}

public sealed class SanctionsWatchlistSource
{
    public SanctionsWatchlistSource(
        string code,
        string name,
        string refreshCadence,
        string status,
        int entryCount = 0,
        DateTime? lastSyncedAt = null)
    {
        Code = code;
        Name = name;
        RefreshCadence = refreshCadence;
        Status = status;
        EntryCount = entryCount;
        LastSyncedAt = lastSyncedAt;
    }

    public string Code { get; }
    public string Name { get; }
    public string RefreshCadence { get; }
    public string Status { get; }
    public int EntryCount { get; }
    public DateTime? LastSyncedAt { get; }
}

public sealed class SanctionsWatchlistEntry
{
    public SanctionsWatchlistEntry(string sourceCode, string primaryName, IReadOnlyList<string> aliases, string category, string riskLevel)
    {
        SourceCode = sourceCode;
        PrimaryName = primaryName;
        Aliases = aliases;
        Category = category;
        RiskLevel = riskLevel;
    }

    public string SourceCode { get; }
    public string PrimaryName { get; }
    public IReadOnlyList<string> Aliases { get; }
    public string Category { get; }
    public string RiskLevel { get; }
}

public sealed class SanctionsScreeningRun
{
    public double ThresholdPercent { get; set; }
    public DateTime ScreenedAt { get; set; }
    public int TotalSubjects { get; set; }
    public int MatchCount { get; set; }
    public SanctionsTfsPreview TfsPreview { get; set; } = new();
    public List<SanctionsScreeningResultRow> Results { get; set; } = [];
}

public sealed class SanctionsTransactionScreeningRequest
{
    public string TransactionReference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "NGN";
    public string Channel { get; set; } = "Wire";
    public bool HighRisk { get; set; }
    public string OriginatorName { get; set; } = string.Empty;
    public string BeneficiaryName { get; set; } = string.Empty;
    public string CounterpartyName { get; set; } = string.Empty;
}

public sealed class SanctionsTransactionScreeningResult
{
    public string TransactionReference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public double ThresholdPercent { get; set; }
    public bool HighRisk { get; set; }
    public string ControlDecision { get; set; } = string.Empty;
    public string Narrative { get; set; } = string.Empty;
    public bool RequiresStrDraft { get; set; }
    public List<SanctionsTransactionPartyResult> PartyResults { get; set; } = [];
}

public sealed class SanctionsTransactionPartyResult
{
    public string PartyRole { get; set; } = string.Empty;
    public string PartyName { get; set; } = string.Empty;
    public string Disposition { get; set; } = string.Empty;
    public double MatchScore { get; set; }
    public string MatchedName { get; set; } = string.Empty;
    public string SourceCode { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
}

public sealed class SanctionsScreeningResultRow
{
    public string Subject { get; set; } = string.Empty;
    public string Disposition { get; set; } = string.Empty;
    public double MatchScore { get; set; }
    public string MatchedName { get; set; } = string.Empty;
    public string SourceCode { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
}

public sealed class SanctionsTfsPreview
{
    public int ScreeningCount { get; set; }
    public int MatchesFound { get; set; }
    public int PotentialMatches { get; set; }
    public int ConfirmedMatches { get; set; }
    public int FalsePositiveCount { get; set; }
    public int AssetsFrozenCount { get; set; }
    public double FalsePositiveRatePercent { get; set; }
    public List<string> WatchlistSources { get; set; } = [];
    public bool StrDraftRequired { get; set; }
    public string Narrative { get; set; } = string.Empty;
}

public sealed class OperationalResilienceSnapshot
{
    public int CriticalAssetCount { get; set; }
    public int DependencyEdgeCount { get; set; }
    public int OpenIncidentCount { get; set; }
    public decimal RcaCoveragePercent { get; set; }
    public decimal GapScore { get; set; }
    public int PersistedAssessmentResponseCount { get; set; }
    public DateTime? LastAssessmentUpdatedAt { get; set; }
    public int ReturnPackReadyCount { get; set; }
    public int ReturnPackAttentionCount { get; set; }
    public DateTime? ReturnPackMaterializedAt { get; set; }
    public decimal CyberAssessmentScore { get; set; }
    public int CyberAssessmentCriticalCount { get; set; }
    public int ImportantServiceCount { get; set; }
    public int ImpactToleranceWatchCount { get; set; }
    public int ThirdPartyProviderCount { get; set; }
    public int ThirdPartyConcentrationCount { get; set; }
    public int BusinessContinuityRiskCount { get; set; }
    public int RecoveryTimeWatchCount { get; set; }
    public int RecoveryTimeBreachCount { get; set; }
    public int ChangeControlReviewCount { get; set; }
    public List<OpsResilienceSheetRow> ReturnPack { get; set; } = [];
    public List<ImportantBusinessServiceRow> BusinessServices { get; set; } = [];
    public List<ImpactToleranceRow> ImpactTolerances { get; set; } = [];
    public List<CyberResilienceAssessmentRow> CyberAssessment { get; set; } = [];
    public List<ThirdPartyProviderRiskRow> ThirdPartyRegister { get; set; } = [];
    public List<BusinessContinuityPlanRow> BusinessContinuityPlans { get; set; } = [];
    public List<RecoveryTimeTestingRow> RecoveryTimeTests { get; set; } = [];
    public List<ChangeManagementControlRow> ChangeManagementControls { get; set; } = [];
    public List<DependencyHotspotRow> Hotspots { get; set; } = [];
    public List<ResilienceGapRow> GapAnalysis { get; set; } = [];
    public ResilienceBoardSummary BoardSummary { get; set; } = new();
    public List<ResilienceTestingRow> TestingSchedule { get; set; } = [];
    public List<ResilienceActionRow> ActionTracker { get; set; } = [];
    public List<ResilienceIncidentRow> RecentIncidents { get; set; } = [];
    public List<ResilienceIncidentTimelineRow> IncidentTimelines { get; set; } = [];
    public List<SecurityAlertRow> RecentSecurityAlerts { get; set; } = [];
}

public sealed class ImportantBusinessServiceRow
{
    public Guid AssetId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string ServiceOwner { get; set; } = string.Empty;
    public string SupportingAsset { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public string Criticality { get; set; } = string.Empty;
    public int DependencyCount { get; set; }
    public int EventSignalCount { get; set; }
    public int MaximumDisruptionMinutes { get; set; }
    public int RecoveryTargetMinutes { get; set; }
    public string LatestTestOutcome { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Commentary { get; set; } = string.Empty;
}

public sealed class ThirdPartyProviderRiskRow
{
    public string ProviderName { get; set; } = string.Empty;
    public string ProviderType { get; set; } = string.Empty;
    public int ServiceCount { get; set; }
    public int CriticalServiceCount { get; set; }
    public int DependentAssetCount { get; set; }
    public string HighestCriticality { get; set; } = string.Empty;
    public string LatestTestOutcome { get; set; } = string.Empty;
    public string Signal { get; set; } = string.Empty;
    public List<string> ProviderAssets { get; set; } = [];
    public List<string> LinkedServices { get; set; } = [];
    public string Commentary { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
}

public sealed class ImpactToleranceRow
{
    public string ServiceName { get; set; } = string.Empty;
    public int MaximumDisruptionMinutes { get; set; }
    public int RecoveryTargetMinutes { get; set; }
    public int DependencyCount { get; set; }
    public string LatestTestOutcome { get; set; } = string.Empty;
    public string Signal { get; set; } = string.Empty;
    public string Commentary { get; set; } = string.Empty;
}

public sealed class BusinessContinuityPlanRow
{
    public string ServiceName { get; set; } = string.Empty;
    public string OwnerLane { get; set; } = string.Empty;
    public int RecoveryTargetMinutes { get; set; }
    public int MaximumDisruptionMinutes { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? LastExercisedAt { get; set; }
    public DateTime NextReviewDue { get; set; }
    public string LatestExerciseOutcome { get; set; } = string.Empty;
    public int OpenActionCount { get; set; }
    public string Commentary { get; set; } = string.Empty;
}

public sealed class ChangeManagementControlRow
{
    public string ControlArea { get; set; } = string.Empty;
    public int ChangeCount { get; set; }
    public int ElevatedChangeCount { get; set; }
    public DateTime? LatestChangeAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Commentary { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
}

public sealed class OpsResilienceSheetRow
{
    public string SheetCode { get; set; } = string.Empty;
    public string SheetName { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public string Signal { get; set; } = string.Empty;
    public string Coverage { get; set; } = string.Empty;
    public string Commentary { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
}

public sealed class CyberResilienceAssessmentRow
{
    public string Domain { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public string Signal { get; set; } = string.Empty;
    public int EvidenceCount { get; set; }
    public string LeadIndicator { get; set; } = string.Empty;
    public string Commentary { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
}

public sealed class RecoveryTimeTestingRow
{
    public string ServiceName { get; set; } = string.Empty;
    public string OwnerLane { get; set; } = string.Empty;
    public int RecoveryTargetMinutes { get; set; }
    public int MaximumDisruptionMinutes { get; set; }
    public int? TestedRecoveryMinutes { get; set; }
    public int? VarianceMinutes { get; set; }
    public string LatestOutcome { get; set; } = string.Empty;
    public string EvidenceSource { get; set; } = string.Empty;
    public DateTime? LastEvidenceAt { get; set; }
    public DateTime NextTestDue { get; set; }
    public string Signal { get; set; } = string.Empty;
    public string Commentary { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
}

public sealed class DependencyHotspotRow
{
    public string AssetName { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public string Criticality { get; set; } = string.Empty;
    public int DependentCount { get; set; }
}

public sealed class ResilienceIncidentRow
{
    public string IncidentKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; }
    public DateTime? ContainedAt { get; set; }
    public DateTime? RemediatedAt { get; set; }
}

public sealed class ResilienceIncidentTimelineRow
{
    public string IncidentKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CurrentPhase { get; set; } = string.Empty;
    public decimal? DetectionToContainHours { get; set; }
    public decimal? DetectionToRecoverHours { get; set; }
    public DateTime? RcaGeneratedAt { get; set; }
    public string Narrative { get; set; } = string.Empty;
    public List<ResilienceIncidentTimelineStep> Steps { get; set; } = [];
}

public sealed class ResilienceIncidentTimelineStep
{
    public string Stage { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime? OccurredAt { get; set; }
    public decimal? ElapsedHours { get; set; }
    public string Commentary { get; set; } = string.Empty;
}

public sealed class SecurityAlertRow
{
    public string Title { get; set; } = string.Empty;
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class ResilienceActionRow
{
    public string Workstream { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string OwnerLane { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
}

public sealed class ResilienceGapRow
{
    public string Domain { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public string Signal { get; set; } = string.Empty;
    public string Commentary { get; set; } = string.Empty;
    public string NextAction { get; set; } = string.Empty;
}

public sealed class ResilienceBoardSummary
{
    public decimal GapScore { get; set; }
    public int CriticalIssues { get; set; }
    public int OverdueActions { get; set; }
    public int DueTests { get; set; }
    public string Narrative { get; set; } = string.Empty;
}

public sealed class ResilienceTestingRow
{
    public string ScenarioTitle { get; set; } = string.Empty;
    public string TestType { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public DateTime? LastRunAt { get; set; }
    public DateTime NextDueAt { get; set; }
    public int EntitiesEvaluated { get; set; }
}

public sealed class ModelRiskSnapshot
{
    public int InventoryCount { get; set; }
    public int DueValidationCount { get; set; }
    public int PerformanceAlertCount { get; set; }
    public int BacktestingCoverageCount { get; set; }
    public int StabilityWatchCount { get; set; }
    public int CalibrationReviewCount { get; set; }
    public int ChangeReviewCount { get; set; }
    public int ApprovalQueueCount { get; set; }
    public decimal? AverageAccuracyScore { get; set; }
    public decimal RiskAppetiteScore { get; set; }
    public int InAppetiteCount { get; set; }
    public int WatchCount { get; set; }
    public int BreachCount { get; set; }
    public int ReturnPackReadyCount { get; set; }
    public int ReturnPackAttentionCount { get; set; }
    public DateTime? ReturnPackMaterializedAt { get; set; }
    public int PersistedApprovalAuditCount { get; set; }
    public DateTime? LastApprovalWorkflowChangeAt { get; set; }
    public List<ModelValidationScheduleRow> ValidationCalendar { get; set; } = [];
    public List<ModelPerformanceEvidenceRow> PerformanceEvidence { get; set; } = [];
    public List<ModelBacktestingRow> Backtesting { get; set; } = [];
    public List<ModelMonitoringSummaryRow> MonitoringSummary { get; set; } = [];
    public List<ModelRiskAppetiteRow> AppetiteRegister { get; set; } = [];
    public List<ModelRiskReportingRow> ReportingPack { get; set; } = [];
    public List<ModelRiskSheetRow> ReturnPack { get; set; } = [];
    public List<ModelApprovalQueueRow> ApprovalQueue { get; set; } = [];
    public List<ModelChangeRow> RecentChanges { get; set; } = [];
    public List<ModelInventoryRow> Inventory { get; set; } = [];
}

public sealed class ModelInventoryRow
{
    public string ModelCode { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public int CoverageArtifacts { get; set; }
    public DateTime? LastEvidenceAt { get; set; }
    public DateTime NextValidationDue { get; set; }
    public string ValidationStatus { get; set; } = string.Empty;
    public string PerformanceStatus { get; set; } = string.Empty;
    public decimal? PerformanceScore { get; set; }
}

public sealed class ModelValidationScheduleRow
{
    public string ModelCode { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string ValidationType { get; set; } = string.Empty;
    public DateTime? LastEvidenceAt { get; set; }
    public DateTime NextValidationDue { get; set; }
    public int DaysUntilDue { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Focus { get; set; } = string.Empty;
}

public sealed class ModelPerformanceEvidenceRow
{
    public string ModelCode { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string EvidenceType { get; set; } = string.Empty;
    public DateTime? MeasuredAt { get; set; }
    public int SampleSize { get; set; }
    public string MetricLabel { get; set; } = string.Empty;
    public decimal? MetricValue { get; set; }
    public string Signal { get; set; } = string.Empty;
    public string Commentary { get; set; } = string.Empty;
}

public sealed class ModelBacktestingRow
{
    public string ModelCode { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string BacktestType { get; set; } = string.Empty;
    public int SampleSize { get; set; }
    public string PredictedLabel { get; set; } = string.Empty;
    public string PredictedValue { get; set; } = string.Empty;
    public string ActualLabel { get; set; } = string.Empty;
    public string ActualValue { get; set; } = string.Empty;
    public string VarianceText { get; set; } = string.Empty;
    public string Signal { get; set; } = string.Empty;
    public string Commentary { get; set; } = string.Empty;
}

public sealed class ModelMonitoringSummaryRow
{
    public string ModelCode { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string MonitoringFocus { get; set; } = string.Empty;
    public string StabilityLabel { get; set; } = string.Empty;
    public string StabilityValue { get; set; } = string.Empty;
    public string AccuracyLabel { get; set; } = string.Empty;
    public string AccuracyValue { get; set; } = string.Empty;
    public string CalibrationStatus { get; set; } = string.Empty;
    public string Signal { get; set; } = string.Empty;
    public string Commentary { get; set; } = string.Empty;
}

public sealed class ModelRiskAppetiteRow
{
    public string ModelCode { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string AppetiteStatus { get; set; } = string.Empty;
    public string ReportingStatus { get; set; } = string.Empty;
    public decimal RiskScore { get; set; }
    public string ValidationStatus { get; set; } = string.Empty;
    public string PerformanceSignal { get; set; } = string.Empty;
    public string PendingWorkflowStage { get; set; } = string.Empty;
    public DateTime NextValidationDue { get; set; }
    public DateTime? LastEvidenceAt { get; set; }
    public string NextAction { get; set; } = string.Empty;
}

public sealed class ModelRiskReportingRow
{
    public string Section { get; set; } = string.Empty;
    public string MetricValue { get; set; } = string.Empty;
    public string Signal { get; set; } = string.Empty;
    public string Commentary { get; set; } = string.Empty;
}

public sealed class ModelRiskSheetRow
{
    public string SheetCode { get; set; } = string.Empty;
    public string SheetName { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public string Signal { get; set; } = string.Empty;
    public string Coverage { get; set; } = string.Empty;
    public string Commentary { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
}

public sealed class ModelApprovalQueueRow
{
    public string WorkflowKey { get; set; } = string.Empty;
    public string ModelCode { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string Artifact { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string CurrentStage { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public string ImpactPackage { get; set; } = string.Empty;
    public string ReviewSignal { get; set; } = string.Empty;
}

public sealed class ModelChangeRow
{
    public Guid? TenantId { get; set; }
    public string Area { get; set; } = string.Empty;
    public string Artifact { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty;
    public string PerformedBy { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public string ReviewSignal { get; set; } = string.Empty;
}

public sealed class InstitutionScorecardRow
{
    public int InstitutionId { get; set; }
    public Guid TenantId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string LicenceType { get; set; } = string.Empty;
    public int OverdueObligations { get; set; }
    public int DueSoonObligations { get; set; }
    public decimal? CapitalScore { get; set; }
    public int OpenResilienceIncidents { get; set; }
    public int OpenSecurityAlerts { get; set; }
    public int ModelReviewItems { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

public sealed class ActivityTimelineRow
{
    public Guid? TenantId { get; set; }
    public int? InstitutionId { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public DateTime HappenedAt { get; set; }
    public string Severity { get; set; } = string.Empty;
}

public sealed class InstitutionIntelligenceDetail
{
    public int InstitutionId { get; set; }
    public Guid TenantId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string InstitutionCode { get; set; } = string.Empty;
    public string LicenceType { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public decimal? CapitalScore { get; set; }
    public string CapitalAlert { get; set; } = string.Empty;
    public int OverdueObligations { get; set; }
    public int DueSoonObligations { get; set; }
    public int OpenResilienceIncidents { get; set; }
    public int OpenSecurityAlerts { get; set; }
    public int ModelReviewItems { get; set; }
    public List<KnowledgeGraphInstitutionObligationRow> TopObligations { get; set; } = [];
    public List<InstitutionSubmissionSummaryRow> RecentSubmissions { get; set; } = [];
    public List<ActivityTimelineRow> RecentActivity { get; set; } = [];
}

public sealed class InstitutionSubmissionSummaryRow
{
    public int SubmissionId { get; set; }
    public string ReturnCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
}

public sealed class InterventionQueueRow
{
    public string Domain { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Signal { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string NextAction { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public string OwnerLane { get; set; } = string.Empty;
}
