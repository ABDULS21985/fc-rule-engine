using System.Data;
using System.Text.Json;
using Dapper;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Migrator;

public sealed class EndToEndDemoSeedService
{
    private const string DemoPrefix = "[DEMO]";
    private const string DemoWhistleblowerPrefix = "WBDEMO-";

    private static readonly int[] DemoStressScenarioIds = [4, 8];
    private static readonly string[] DemoInstitutionCodes = ["FC001", "FC002", "CASHCODE", "ACCESSBA", "BUZZWALL"];

    private readonly MetadataDbContext _db;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly DemoCredentialSeedService _credentialSeeder;
    private readonly DmbDemoWorkspaceService _dmbDemoWorkspaceService;
    private readonly ITenantAccessContextResolver _tenantAccessContextResolver;
    private readonly ICAMELSScorer _camelsScorer;
    private readonly IEWIEngine _ewiEngine;
    private readonly ISystemicRiskAggregator _systemicRiskAggregator;
    private readonly IContagionAnalyzer _contagionAnalyzer;
    private readonly IStressTestOrchestrator _stressTestOrchestrator;
    private readonly IPolicyScenarioService _policyScenarioService;
    private readonly IConsultationService _consultationService;
    private readonly ICostBenefitAnalyser _costBenefitAnalyser;
    private readonly IPolicyDecisionService _policyDecisionService;
    private readonly IHistoricalImpactTracker _historicalImpactTracker;
    private readonly ISurveillanceOrchestrator _surveillanceOrchestrator;
    private readonly IExaminationWorkspaceService _examinationWorkspaceService;
    private readonly ILogger<EndToEndDemoSeedService> _logger;

    public EndToEndDemoSeedService(
        MetadataDbContext db,
        IDbConnectionFactory connectionFactory,
        DemoCredentialSeedService credentialSeeder,
        DmbDemoWorkspaceService dmbDemoWorkspaceService,
        ITenantAccessContextResolver tenantAccessContextResolver,
        ICAMELSScorer camelsScorer,
        IEWIEngine ewiEngine,
        ISystemicRiskAggregator systemicRiskAggregator,
        IContagionAnalyzer contagionAnalyzer,
        IStressTestOrchestrator stressTestOrchestrator,
        IPolicyScenarioService policyScenarioService,
        IConsultationService consultationService,
        ICostBenefitAnalyser costBenefitAnalyser,
        IPolicyDecisionService policyDecisionService,
        IHistoricalImpactTracker historicalImpactTracker,
        ISurveillanceOrchestrator surveillanceOrchestrator,
        IExaminationWorkspaceService examinationWorkspaceService,
        ILogger<EndToEndDemoSeedService> logger)
    {
        _db = db;
        _connectionFactory = connectionFactory;
        _credentialSeeder = credentialSeeder;
        _dmbDemoWorkspaceService = dmbDemoWorkspaceService;
        _tenantAccessContextResolver = tenantAccessContextResolver;
        _camelsScorer = camelsScorer;
        _ewiEngine = ewiEngine;
        _systemicRiskAggregator = systemicRiskAggregator;
        _contagionAnalyzer = contagionAnalyzer;
        _stressTestOrchestrator = stressTestOrchestrator;
        _policyScenarioService = policyScenarioService;
        _consultationService = consultationService;
        _costBenefitAnalyser = costBenefitAnalyser;
        _policyDecisionService = policyDecisionService;
        _historicalImpactTracker = historicalImpactTracker;
        _surveillanceOrchestrator = surveillanceOrchestrator;
        _examinationWorkspaceService = examinationWorkspaceService;
        _logger = logger;
    }

    public async Task<EndToEndDemoSeedResult> SeedAsync(
        string sharedPassword,
        string templatesDirectory,
        CancellationToken ct = default)
    {
        var credentials = await _credentialSeeder.SeedAsync(sharedPassword, ct);
        await _dmbDemoWorkspaceService.PrepareAsync(templatesDirectory, sharedPassword, ct);

        var regulatorTenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantSlug == "cbn", ct)
            ?? throw new InvalidOperationException("CBN regulator tenant was not found.");

        var accessContext = await _tenantAccessContextResolver.TryResolveAsync(regulatorTenant.TenantId, ct: ct)
            ?? throw new InvalidOperationException("Unable to resolve CBN regulator context.");

        if (string.IsNullOrWhiteSpace(accessContext.RegulatorCode) || !accessContext.RegulatorId.HasValue)
        {
            throw new InvalidOperationException("CBN regulator context is incomplete.");
        }

        var regulatorCode = accessContext.RegulatorCode;
        var regulatorId = accessContext.RegulatorId.Value;
        var demoPeriods = BuildDemoPeriods(DateOnly.FromDateTime(DateTime.UtcNow));
        var currentPeriod = demoPeriods[^1];
        var institutions = await LoadInstitutionsAsync(ct);
        var regulatorAdminUser = await ResolvePortalUserAsync("cbnadmin", ct);

        await CleanupPolicyDemoDataAsync(ct);
        await CleanupExaminationDemoDataAsync(ct);
        await CleanupWhistleblowerDemoDataAsync(ct);
        await CleanupDerivedRiskDataAsync(regulatorCode, currentPeriod.PeriodCode, institutions.Keys.ToArray(), ct);

        var prudentialSeeds = BuildPrudentialMetricSeeds(regulatorCode, demoPeriods);
        await UpsertPrudentialMetricsAsync(prudentialSeeds, ct);

        var interbankSeeds = BuildInterbankExposureSeeds(regulatorCode, currentPeriod);
        await UpsertInterbankExposuresAsync(interbankSeeds, ct);

        await SeedConductInputsAsync(regulatorTenant.TenantId, regulatorCode, currentPeriod, ct);
        var surveillanceRun = await _surveillanceOrchestrator.RunCycleAsync(regulatorCode, currentPeriod.PeriodCode, ct);

        var currentTypes = prudentialSeeds
            .Where(x => x.PeriodCode == currentPeriod.PeriodCode)
            .Select(x => x.InstitutionType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var institutionType in currentTypes)
        {
            await _camelsScorer.ScoreSectorAsync(regulatorCode, institutionType, currentPeriod.PeriodCode, Guid.NewGuid(), ct);
        }

        await _ewiEngine.RunFullCycleAsync(regulatorCode, currentPeriod.PeriodCode, ct);

        foreach (var institutionType in currentTypes)
        {
            await _systemicRiskAggregator.AggregateAsync(regulatorCode, institutionType, currentPeriod.PeriodCode, Guid.NewGuid(), ct);
        }

        await _contagionAnalyzer.AnalyzeAsync(regulatorCode, currentPeriod.PeriodCode, Guid.NewGuid(), ct);

        var stressRunsCreated = await EnsureStressRunsAsync(regulatorCode, currentPeriod.PeriodCode, regulatorAdminUser.Id, ct);
        var whistleblowerCasesSeeded = await SeedWhistleblowerCasesAsync(regulatorTenant.TenantId, regulatorCode, ct);
        var policyScenariosSeeded = await SeedPolicyLifecycleAsync(regulatorId, regulatorAdminUser.Id, currentPeriod, institutions, ct);
        var examinationProjectsSeeded = await SeedExaminationWorkspaceAsync(regulatorTenant.TenantId, regulatorCode, regulatorAdminUser.Id, institutions, ct);

        _logger.LogInformation(
            "End-to-end demo seed completed. Regulator={RegulatorCode} Period={PeriodCode} Institutions={InstitutionCount}",
            regulatorCode,
            currentPeriod.PeriodCode,
            institutions.Count);

        return new EndToEndDemoSeedResult
        {
            Credentials = credentials,
            RegulatorCode = regulatorCode,
            CurrentPeriodCode = currentPeriod.PeriodCode,
            PrudentialRowsUpserted = prudentialSeeds.Count,
            InterbankExposureRowsUpserted = interbankSeeds.Count,
            StressRunsCreated = stressRunsCreated,
            PolicyScenariosSeeded = policyScenariosSeeded,
            WhistleblowerCasesSeeded = whistleblowerCasesSeeded,
            ExaminationProjectsSeeded = examinationProjectsSeeded,
            SurveillanceRun = surveillanceRun
        };
    }

    private async Task<Dictionary<int, DemoInstitution>> LoadInstitutionsAsync(CancellationToken ct)
    {
        var rows = await _db.Institutions
            .AsNoTracking()
            .Where(x => DemoInstitutionCodes.AsEnumerable().Contains(x.InstitutionCode))
            .Select(x => new DemoInstitution(
                x.Id,
                x.InstitutionCode,
                x.InstitutionName,
                x.LicenseType ?? string.Empty,
                x.TenantId))
            .ToListAsync(ct);

        return rows.ToDictionary(x => x.Id);
    }

    private async Task<PortalUser> ResolvePortalUserAsync(string username, CancellationToken ct)
    {
        return await _db.PortalUsers
            .FirstOrDefaultAsync(x => x.Username == username, ct)
            ?? throw new InvalidOperationException($"Portal user '{username}' was not found.");
    }

    private async Task CleanupDerivedRiskDataAsync(
        string regulatorCode,
        string periodCode,
        IReadOnlyCollection<int> institutionIds,
        CancellationToken ct)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(null, ct);

        var demoRunIds = (await conn.QueryAsync<long>(
            """
            SELECT Id
            FROM StressTestRuns
            WHERE RegulatorCode = @RegulatorCode
              AND PeriodCode = @PeriodCode
              AND ScenarioId IN @ScenarioIds
            """,
            new { RegulatorCode = regulatorCode, PeriodCode = periodCode, ScenarioIds = DemoStressScenarioIds }))
            .ToArray();

        if (demoRunIds.Length > 0)
        {
            await conn.ExecuteAsync("DELETE FROM StressTestContagionEvents WHERE RunId IN @RunIds;", new { RunIds = demoRunIds });
            await conn.ExecuteAsync("DELETE FROM StressTestSectorAggregates WHERE RunId IN @RunIds;", new { RunIds = demoRunIds });
            await conn.ExecuteAsync("DELETE FROM StressTestEntityResults WHERE RunId IN @RunIds;", new { RunIds = demoRunIds });
            await conn.ExecuteAsync("DELETE FROM StressTestRuns WHERE Id IN @RunIds;", new { RunIds = demoRunIds });
        }

        await conn.ExecuteAsync(
            """
            DELETE sa
            FROM meta.supervisory_actions sa
            INNER JOIN meta.ewi_triggers t ON t.Id = sa.EWITriggerId
            WHERE t.RegulatorCode = @RegulatorCode
              AND t.PeriodCode = @PeriodCode;

            DELETE FROM meta.ewi_triggers
            WHERE RegulatorCode = @RegulatorCode
              AND PeriodCode = @PeriodCode;

            DELETE FROM meta.ewi_computation_runs
            WHERE RegulatorCode = @RegulatorCode
              AND PeriodCode = @PeriodCode;

            DELETE FROM meta.systemic_risk_indicators
            WHERE RegulatorCode = @RegulatorCode
              AND PeriodCode = @PeriodCode;

            DELETE FROM meta.contagion_analysis_results
            WHERE PeriodCode = @PeriodCode
              AND InstitutionId IN @InstitutionIds;

            DELETE FROM meta.camels_ratings
            WHERE PeriodCode = @PeriodCode
              AND InstitutionId IN @InstitutionIds;

            DELETE FROM dbo.ConductRiskScores
            WHERE RegulatorCode = @RegulatorCode
              AND PeriodCode = @PeriodCode
              AND InstitutionId IN @InstitutionIds;
            """,
            new { RegulatorCode = regulatorCode, PeriodCode = periodCode, InstitutionIds = institutionIds });

        var alertIds = (await conn.QueryAsync<long>(
            """
            SELECT Id
            FROM dbo.SurveillanceAlerts
            WHERE RegulatorCode = @RegulatorCode
              AND PeriodCode = @PeriodCode
            """,
            new { RegulatorCode = regulatorCode, PeriodCode = periodCode }))
            .ToArray();

        if (alertIds.Length > 0)
        {
            await conn.ExecuteAsync(
                "DELETE FROM dbo.SurveillanceAlertResolutions WHERE AlertId IN @AlertIds;",
                new { AlertIds = alertIds });
            await conn.ExecuteAsync(
                "DELETE FROM dbo.SurveillanceAlerts WHERE Id IN @AlertIds;",
                new { AlertIds = alertIds });
        }
    }

    private async Task CleanupPolicyDemoDataAsync(CancellationToken ct)
    {
        var scenarioIds = await _db.PolicyScenarios
            .AsNoTracking()
            .Where(x => x.Title.StartsWith(DemoPrefix))
            .Select(x => x.Id)
            .ToListAsync(ct);

        if (scenarioIds.Count == 0)
        {
            return;
        }

        using var conn = await _connectionFactory.CreateConnectionAsync(null, ct);

        var consultationIds = (await conn.QueryAsync<long>(
            "SELECT Id FROM dbo.consultation_rounds WHERE ScenarioId IN @ScenarioIds;",
            new { ScenarioIds = scenarioIds }))
            .ToArray();

        var feedbackIds = consultationIds.Length == 0
            ? []
            : (await conn.QueryAsync<long>(
                "SELECT Id FROM dbo.consultation_feedback WHERE ConsultationId IN @ConsultationIds;",
                new { ConsultationIds = consultationIds }))
                .ToArray();

        var runIds = (await conn.QueryAsync<long>(
            "SELECT Id FROM dbo.impact_assessment_runs WHERE ScenarioId IN @ScenarioIds;",
            new { ScenarioIds = scenarioIds }))
            .ToArray();

        await conn.ExecuteAsync("DELETE FROM dbo.historical_impact_tracking WHERE ScenarioId IN @ScenarioIds;", new { ScenarioIds = scenarioIds });
        await conn.ExecuteAsync("DELETE FROM dbo.policy_decisions WHERE ScenarioId IN @ScenarioIds;", new { ScenarioIds = scenarioIds });

        if (consultationIds.Length > 0)
        {
            await conn.ExecuteAsync("DELETE FROM dbo.feedback_aggregations WHERE ConsultationId IN @ConsultationIds;", new { ConsultationIds = consultationIds });
            if (feedbackIds.Length > 0)
            {
                await conn.ExecuteAsync("DELETE FROM dbo.provision_feedback WHERE FeedbackId IN @FeedbackIds;", new { FeedbackIds = feedbackIds });
            }

            await conn.ExecuteAsync("DELETE FROM dbo.consultation_feedback WHERE ConsultationId IN @ConsultationIds;", new { ConsultationIds = consultationIds });
            await conn.ExecuteAsync("DELETE FROM dbo.consultation_provisions WHERE ConsultationId IN @ConsultationIds;", new { ConsultationIds = consultationIds });
            await conn.ExecuteAsync("DELETE FROM dbo.consultation_rounds WHERE Id IN @ConsultationIds;", new { ConsultationIds = consultationIds });
        }

        if (runIds.Length > 0)
        {
            await conn.ExecuteAsync("DELETE FROM dbo.cost_benefit_analyses WHERE RunId IN @RunIds;", new { RunIds = runIds });
            await conn.ExecuteAsync("DELETE FROM dbo.entity_impact_results WHERE RunId IN @RunIds;", new { RunIds = runIds });
            await conn.ExecuteAsync("DELETE FROM dbo.impact_assessment_runs WHERE Id IN @RunIds;", new { RunIds = runIds });
        }

        await conn.ExecuteAsync("DELETE FROM dbo.policy_parameters WHERE ScenarioId IN @ScenarioIds;", new { ScenarioIds = scenarioIds });
        await conn.ExecuteAsync("DELETE FROM dbo.policy_audit_log WHERE ScenarioId IN @ScenarioIds;", new { ScenarioIds = scenarioIds });
        await conn.ExecuteAsync("DELETE FROM dbo.policy_scenarios WHERE Id IN @ScenarioIds;", new { ScenarioIds = scenarioIds });
    }

    private async Task CleanupExaminationDemoDataAsync(CancellationToken ct)
    {
        var projectIds = await _db.ExaminationProjects
            .AsNoTracking()
            .Where(x => x.Name.StartsWith(DemoPrefix))
            .Select(x => x.Id)
            .ToListAsync(ct);

        if (projectIds.Count == 0)
        {
            return;
        }

        using var conn = await _connectionFactory.CreateConnectionAsync(null, ct);
        await conn.ExecuteAsync("DELETE FROM dbo.examination_evidence_files WHERE ProjectId IN @ProjectIds;", new { ProjectIds = projectIds });
        await conn.ExecuteAsync("DELETE FROM dbo.examination_evidence_requests WHERE ProjectId IN @ProjectIds;", new { ProjectIds = projectIds });
        await conn.ExecuteAsync("DELETE FROM dbo.examination_findings WHERE ProjectId IN @ProjectIds;", new { ProjectIds = projectIds });
        await conn.ExecuteAsync("DELETE FROM dbo.examination_annotations WHERE ProjectId IN @ProjectIds;", new { ProjectIds = projectIds });
        await conn.ExecuteAsync("DELETE FROM dbo.examination_projects WHERE Id IN @ProjectIds;", new { ProjectIds = projectIds });
    }

    private async Task CleanupWhistleblowerDemoDataAsync(CancellationToken ct)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(null, ct);
        var reportIds = (await conn.QueryAsync<long>(
            """
            SELECT Id
            FROM dbo.WhistleblowerReports
            WHERE CaseReference LIKE @Prefix
            """,
            new { Prefix = $"{DemoWhistleblowerPrefix}%" }))
            .ToArray();

        if (reportIds.Length == 0)
        {
            return;
        }

        await conn.ExecuteAsync(
            "DELETE FROM dbo.WhistleblowerCaseEvents WHERE WhistleblowerReportId IN @ReportIds;",
            new { ReportIds = reportIds });
        await conn.ExecuteAsync(
            "DELETE FROM dbo.WhistleblowerReports WHERE Id IN @ReportIds;",
            new { ReportIds = reportIds });
    }

    private async Task UpsertPrudentialMetricsAsync(
        IReadOnlyList<DemoPrudentialMetricSeed> seeds,
        CancellationToken ct)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(null, ct);

        foreach (var seed in seeds)
        {
            await conn.ExecuteAsync(
                """
                MERGE meta.prudential_metrics AS target
                USING (
                    VALUES (
                        @InstitutionId, @RegulatorCode, @InstitutionType, @AsOfDate, @PeriodCode,
                        @CAR, @Tier1Ratio, @Tier2Capital, @RWA, @TotalAssets, @TotalDeposits,
                        @NPLRatio, @GrossNPL, @GrossLoans, @ProvisioningCoverage, @OilSectorExposurePct, @AgriExposurePct,
                        @ROA, @ROE, @NIM, @CIR, @LCR, @NSFR, @LiquidAssetsRatio, @DepositConcentration,
                        @FXExposureRatio, @FXLoansAssetPct, @BondPortfolioAssetPct, @InterestRateSensitivity,
                        @ComplianceScore, @LateFilingCount, @AuditOpinionCode, @RelatedPartyLendingRatio
                    )
                ) AS source (
                    InstitutionId, RegulatorCode, InstitutionType, AsOfDate, PeriodCode,
                    CAR, Tier1Ratio, Tier2Capital, RWA, TotalAssets, TotalDeposits,
                    NPLRatio, GrossNPL, GrossLoans, ProvisioningCoverage, OilSectorExposurePct, AgriExposurePct,
                    ROA, ROE, NIM, CIR, LCR, NSFR, LiquidAssetsRatio, DepositConcentration,
                    FXExposureRatio, FXLoansAssetPct, BondPortfolioAssetPct, InterestRateSensitivity,
                    ComplianceScore, LateFilingCount, AuditOpinionCode, RelatedPartyLendingRatio
                )
                ON target.InstitutionId = source.InstitutionId
                   AND target.PeriodCode = source.PeriodCode
                WHEN MATCHED THEN
                    UPDATE SET
                        RegulatorCode = source.RegulatorCode,
                        InstitutionType = source.InstitutionType,
                        AsOfDate = source.AsOfDate,
                        CAR = source.CAR,
                        Tier1Ratio = source.Tier1Ratio,
                        Tier2Capital = source.Tier2Capital,
                        RWA = source.RWA,
                        TotalAssets = source.TotalAssets,
                        TotalDeposits = source.TotalDeposits,
                        NPLRatio = source.NPLRatio,
                        GrossNPL = source.GrossNPL,
                        GrossLoans = source.GrossLoans,
                        ProvisioningCoverage = source.ProvisioningCoverage,
                        OilSectorExposurePct = source.OilSectorExposurePct,
                        AgriExposurePct = source.AgriExposurePct,
                        ROA = source.ROA,
                        ROE = source.ROE,
                        NIM = source.NIM,
                        CIR = source.CIR,
                        LCR = source.LCR,
                        NSFR = source.NSFR,
                        LiquidAssetsRatio = source.LiquidAssetsRatio,
                        DepositConcentration = source.DepositConcentration,
                        FXExposureRatio = source.FXExposureRatio,
                        FXLoansAssetPct = source.FXLoansAssetPct,
                        BondPortfolioAssetPct = source.BondPortfolioAssetPct,
                        InterestRateSensitivity = source.InterestRateSensitivity,
                        ComplianceScore = source.ComplianceScore,
                        LateFilingCount = source.LateFilingCount,
                        AuditOpinionCode = source.AuditOpinionCode,
                        RelatedPartyLendingRatio = source.RelatedPartyLendingRatio
                WHEN NOT MATCHED THEN
                    INSERT (
                        InstitutionId, RegulatorCode, InstitutionType, AsOfDate, PeriodCode,
                        CAR, Tier1Ratio, Tier2Capital, RWA, TotalAssets, TotalDeposits,
                        NPLRatio, GrossNPL, GrossLoans, ProvisioningCoverage, OilSectorExposurePct, AgriExposurePct,
                        ROA, ROE, NIM, CIR, LCR, NSFR, LiquidAssetsRatio, DepositConcentration,
                        FXExposureRatio, FXLoansAssetPct, BondPortfolioAssetPct, InterestRateSensitivity,
                        ComplianceScore, LateFilingCount, AuditOpinionCode, RelatedPartyLendingRatio
                    )
                    VALUES (
                        source.InstitutionId, source.RegulatorCode, source.InstitutionType, source.AsOfDate, source.PeriodCode,
                        source.CAR, source.Tier1Ratio, source.Tier2Capital, source.RWA, source.TotalAssets, source.TotalDeposits,
                        source.NPLRatio, source.GrossNPL, source.GrossLoans, source.ProvisioningCoverage, source.OilSectorExposurePct, source.AgriExposurePct,
                        source.ROA, source.ROE, source.NIM, source.CIR, source.LCR, source.NSFR, source.LiquidAssetsRatio, source.DepositConcentration,
                        source.FXExposureRatio, source.FXLoansAssetPct, source.BondPortfolioAssetPct, source.InterestRateSensitivity,
                        source.ComplianceScore, source.LateFilingCount, source.AuditOpinionCode, source.RelatedPartyLendingRatio
                    );
                """,
                seed);
        }
    }

    private async Task UpsertInterbankExposuresAsync(
        IReadOnlyList<DemoInterbankExposureSeed> seeds,
        CancellationToken ct)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(null, ct);

        foreach (var seed in seeds)
        {
            await conn.ExecuteAsync(
                """
                MERGE meta.interbank_exposures AS target
                USING (
                    VALUES (@LendingInstitutionId, @BorrowingInstitutionId, @RegulatorCode, @PeriodCode, @ExposureAmount, @ExposureType, @AsOfDate)
                ) AS source (
                    LendingInstitutionId, BorrowingInstitutionId, RegulatorCode, PeriodCode, ExposureAmount, ExposureType, AsOfDate
                )
                ON target.LendingInstitutionId = source.LendingInstitutionId
                   AND target.BorrowingInstitutionId = source.BorrowingInstitutionId
                   AND target.ExposureType = source.ExposureType
                   AND target.PeriodCode = source.PeriodCode
                WHEN MATCHED THEN
                    UPDATE SET
                        RegulatorCode = source.RegulatorCode,
                        ExposureAmount = source.ExposureAmount,
                        AsOfDate = source.AsOfDate
                WHEN NOT MATCHED THEN
                    INSERT (
                        LendingInstitutionId, BorrowingInstitutionId, RegulatorCode, PeriodCode, ExposureAmount, ExposureType, AsOfDate
                    )
                    VALUES (
                        source.LendingInstitutionId, source.BorrowingInstitutionId, source.RegulatorCode,
                        source.PeriodCode, source.ExposureAmount, source.ExposureType, source.AsOfDate
                    );
                """,
                seed);
        }
    }

    private async Task SeedConductInputsAsync(
        Guid regulatorTenantId,
        string regulatorCode,
        DemoPeriod currentPeriod,
        CancellationToken ct)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(null, ct);

        await conn.ExecuteAsync(
            """
            DELETE FROM dbo.CMOTradeReports
            WHERE TenantId = @TenantId
              AND PeriodCode = @PeriodCode
              AND SecurityCode LIKE 'DEMO-%';

            DELETE FROM dbo.CorporateAnnouncements
            WHERE TenantId = @TenantId
              AND SourceReference = 'DEMO-SEED';

            DELETE FROM dbo.BDCFXTransactions
            WHERE TenantId = @TenantId
              AND PeriodCode = @PeriodCode
              AND InstitutionId IN (3, 1002);

            DELETE FROM dbo.AMLConductMetrics
            WHERE TenantId = @TenantId
              AND PeriodCode = @PeriodCode
              AND InstitutionId IN (1, 2, 3, 4, 1002);

            DELETE FROM dbo.InsuranceConductMetrics
            WHERE TenantId = @TenantId
              AND PeriodCode = @PeriodCode
              AND InstitutionId IN (1, 2, 4);
            """,
            new { TenantId = regulatorTenantId, PeriodCode = currentPeriod.PeriodCode });

        foreach (var seed in BuildBdcTransactionSeeds(regulatorTenantId, regulatorCode, currentPeriod))
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO dbo.BDCFXTransactions
                    (TenantId, InstitutionId, RegulatorCode, TransactionDate, PeriodCode,
                     BuyCurrency, SellCurrency, BuyRate, SellRate, BuyVolumeUSD, SellVolumeUSD,
                     CBNMidRate, CBNBandUpper, CBNBandLower, CounterpartyId)
                VALUES
                    (@TenantId, @InstitutionId, @RegulatorCode, @TransactionDate, @PeriodCode,
                     @BuyCurrency, @SellCurrency, @BuyRate, @SellRate, @BuyVolumeUSD, @SellVolumeUSD,
                     @CBNMidRate, @CBNBandUpper, @CBNBandLower, @CounterpartyId);
                """,
                seed);
        }

        foreach (var seed in BuildCorporateAnnouncementSeeds(regulatorTenantId, regulatorCode, currentPeriod))
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO dbo.CorporateAnnouncements
                    (TenantId, RegulatorCode, SecurityCode, SecurityName, AnnouncementType,
                     AnnouncementDate, DisclosureDeadline, SourceReference)
                VALUES
                    (@TenantId, @RegulatorCode, @SecurityCode, @SecurityName, @AnnouncementType,
                     @AnnouncementDate, @DisclosureDeadline, @SourceReference);
                """,
                seed);
        }

        foreach (var seed in BuildCmoTradeSeeds(regulatorTenantId, regulatorCode, currentPeriod))
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO dbo.CMOTradeReports
                    (TenantId, InstitutionId, RegulatorCode, InstitutionType, SecurityCode, SecurityName,
                     TradeDate, PeriodCode, TradeType, Quantity, Price, TradeValueNGN, ClientId,
                     ReportedAt, TradeTimestamp, IsLate)
                VALUES
                    (@TenantId, @InstitutionId, @RegulatorCode, @InstitutionType, @SecurityCode, @SecurityName,
                     @TradeDate, @PeriodCode, @TradeType, @Quantity, @Price, @TradeValueNGN, @ClientId,
                     @ReportedAt, @TradeTimestamp, @IsLate);
                """,
                seed);
        }

        foreach (var seed in BuildAmlMetricSeeds(regulatorTenantId, regulatorCode, currentPeriod))
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO dbo.AMLConductMetrics
                    (TenantId, InstitutionId, RegulatorCode, InstitutionType, PeriodCode, AsOfDate,
                     STRFilingCount, CTRFilingCount, PeerAvgSTRCount, STRDeviation,
                     StructuringAlertCount, PEPAccountCount, PEPFlaggedActivityCount,
                     TFSScreeningCount, TFSFalsePositiveRate, TFSTruePositiveCount,
                     CustomerComplaintCount, ComplaintResolutionRate)
                VALUES
                    (@TenantId, @InstitutionId, @RegulatorCode, @InstitutionType, @PeriodCode, @AsOfDate,
                     @STRFilingCount, @CTRFilingCount, @PeerAvgSTRCount, @STRDeviation,
                     @StructuringAlertCount, @PEPAccountCount, @PEPFlaggedActivityCount,
                     @TFSScreeningCount, @TFSFalsePositiveRate, @TFSTruePositiveCount,
                     @CustomerComplaintCount, @ComplaintResolutionRate);
                """,
                seed);
        }

        foreach (var seed in BuildInsuranceMetricSeeds(regulatorTenantId, regulatorCode, currentPeriod))
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO dbo.InsuranceConductMetrics
                    (TenantId, InstitutionId, RegulatorCode, InstitutionType, PeriodCode, AsOfDate,
                     GrossClaimsNGN, GrossPremiumNGN, ClaimsRatio, PeerAvgClaimsRatio,
                     GrossPremiumReported, GrossPremiumExpected, PremiumUnderReportingGap,
                     ReinsuranceRecoveries, RelatedPartyReinsurancePct,
                     ComplaintCount, ClaimsDenialRate)
                VALUES
                    (@TenantId, @InstitutionId, @RegulatorCode, @InstitutionType, @PeriodCode, @AsOfDate,
                     @GrossClaimsNGN, @GrossPremiumNGN, @ClaimsRatio, @PeerAvgClaimsRatio,
                     @GrossPremiumReported, @GrossPremiumExpected, @PremiumUnderReportingGap,
                     @ReinsuranceRecoveries, @RelatedPartyReinsurancePct,
                     @ComplaintCount, @ClaimsDenialRate);
                """,
                seed);
        }
    }

    private async Task<int> EnsureStressRunsAsync(
        string regulatorCode,
        string periodCode,
        int initiatedByUserId,
        CancellationToken ct)
    {
        var created = 0;
        using var conn = await _connectionFactory.CreateConnectionAsync(null, ct);

        foreach (var scenarioId in DemoStressScenarioIds)
        {
            var existing = await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM StressTestRuns
                WHERE RegulatorCode = @RegulatorCode
                  AND PeriodCode = @PeriodCode
                  AND ScenarioId = @ScenarioId
                """,
                new { RegulatorCode = regulatorCode, PeriodCode = periodCode, ScenarioId = scenarioId });

            if (existing > 0)
            {
                continue;
            }

            await _stressTestOrchestrator.RunAsync(
                regulatorCode,
                scenarioId,
                periodCode,
                scenarioId == 8 ? "2Y" : "1Y",
                initiatedByUserId,
                ct);
            created++;
        }

        return created;
    }

    private async Task<int> SeedWhistleblowerCasesAsync(
        Guid regulatorTenantId,
        string regulatorCode,
        CancellationToken ct)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(null, ct);
        var regulatorUsers = await _db.PortalUsers
            .AsNoTracking()
            .Where(x => x.TenantId == regulatorTenantId)
            .ToDictionaryAsync(x => x.Username, x => x.Id, ct);

        var cbnAdminId = regulatorUsers.TryGetValue("cbnadmin", out var adminId) ? adminId : 0;
        var cbnApproverId = regulatorUsers.TryGetValue("cbnapprover", out var approverId) ? approverId : cbnAdminId;

        var seeds = new[]
        {
            new DemoWhistleblowerCaseSeed(
                $"{DemoWhistleblowerPrefix}-001",
                "wbdemo-access-001",
                4,
                "Access Bank Plc",
                "Fraud",
                "Anonymous complaint alleges systematic override of collateral exceptions for politically connected counterparties.",
                "Board credit committee packs and collateral waiver memos supplied by whistleblower.",
                "UNDER_REVIEW",
                cbnApproverId,
                92,
                new[]
                {
                    new DemoWhistleblowerEventSeed("RECEIVED", "Case received through the demo intake channel.", null),
                    new DemoWhistleblowerEventSeed("ASSIGNED", "Assigned to the prudential enforcement queue.", cbnAdminId),
                    new DemoWhistleblowerEventSeed("STATUS_UPDATED", "Status moved to under review pending field validation.", cbnApproverId)
                }),
            new DemoWhistleblowerCaseSeed(
                $"{DemoWhistleblowerPrefix}-002",
                "wbdemo-fc002-002",
                2,
                "Example Microfinance Bank",
                "ConsumerProtection",
                "Whistleblower reports undisclosed penalty charges being levied on dormant micro-loan accounts.",
                "Screenshots of customer ledgers and product schedule attached.",
                "RECEIVED",
                null,
                78,
                new[]
                {
                    new DemoWhistleblowerEventSeed("RECEIVED", "Case received and awaiting triage.", null)
                }),
            new DemoWhistleblowerCaseSeed(
                $"{DemoWhistleblowerPrefix}-003",
                "wbdemo-cashcode-003",
                3,
                "CASHCODE BDC LTD",
                "MarketAbuse",
                "Counterparty alleges deliberate round-tripping of FX positions to mask true exposure concentrations.",
                "Trade blotter extracts and counterparty chat transcripts noted in evidence description.",
                "REFERRED",
                cbnAdminId,
                88,
                new[]
                {
                    new DemoWhistleblowerEventSeed("RECEIVED", "Case received and logged.", null),
                    new DemoWhistleblowerEventSeed("ASSIGNED", "Assigned to market conduct lead.", cbnApproverId),
                    new DemoWhistleblowerEventSeed("STATUS_UPDATED", "Referred to investigation team for on-site review.", cbnAdminId)
                })
        };

        foreach (var seed in seeds)
        {
            var reportId = await conn.ExecuteScalarAsync<long>(
                """
                INSERT INTO dbo.WhistleblowerReports
                    (TenantId, CaseReference, AnonymousToken, RegulatorCode, AllegedInstitutionId,
                     AllegedInstitutionName, Category, Summary, EvidenceDescription,
                     Status, AssignedToUserId, PriorityScore)
                OUTPUT INSERTED.Id
                VALUES
                    (@TenantId, @CaseReference, @AnonymousToken, @RegulatorCode, @AllegedInstitutionId,
                     @AllegedInstitutionName, @Category, @Summary, @EvidenceDescription,
                     @Status, @AssignedToUserId, @PriorityScore);
                """,
                new
                {
                    TenantId = regulatorTenantId,
                    seed.CaseReference,
                    seed.AnonymousToken,
                    RegulatorCode = regulatorCode,
                    AllegedInstitutionId = seed.AllegedInstitutionId,
                    AllegedInstitutionName = seed.AllegedInstitutionName,
                    seed.Category,
                    seed.Summary,
                    seed.EvidenceDescription,
                    seed.Status,
                    AssignedToUserId = seed.AssignedToUserId,
                    seed.PriorityScore
                });

            foreach (var @event in seed.Events)
            {
                await conn.ExecuteAsync(
                    """
                    INSERT INTO dbo.WhistleblowerCaseEvents
                        (TenantId, RegulatorCode, WhistleblowerReportId, EventType, Note, PerformedByUserId)
                    VALUES
                        (@TenantId, @RegulatorCode, @WhistleblowerReportId, @EventType, @Note, @PerformedByUserId);
                    """,
                    new
                    {
                        TenantId = regulatorTenantId,
                        RegulatorCode = regulatorCode,
                        WhistleblowerReportId = reportId,
                        @event.EventType,
                        @event.Note,
                        PerformedByUserId = @event.PerformedByUserId
                    });
            }
        }

        return seeds.Length;
    }

    private async Task<int> SeedPolicyLifecycleAsync(
        int regulatorId,
        int regulatorUserId,
        DemoPeriod currentPeriod,
        IReadOnlyDictionary<int, DemoInstitution> institutions,
        CancellationToken ct)
    {
        var scenarioCount = 0;

        var draftScenarioId = await _policyScenarioService.CreateScenarioAsync(
            regulatorId,
            $"{DemoPrefix} BDC conduct remediation sandbox",
            "Draft policy sandbox for calibrating conduct and reporting standards for BDCs before simulation.",
            PolicyDomain.RiskManagement,
            "BDC",
            currentPeriod.AsOfDate,
            regulatorUserId,
            ct);
        scenarioCount++;

        var consultationScenarioId = await _policyScenarioService.CreateScenarioAsync(
            regulatorId,
            $"{DemoPrefix} FX spread consultation",
            "Consultation-ready proposal for tightening allowable BDC FX spreads and strengthening disclosure expectations.",
            PolicyDomain.FX,
            "BDC",
            currentPeriod.AsOfDate,
            regulatorUserId,
            ct);
        scenarioCount++;

        await _policyScenarioService.AddParameterAsync(consultationScenarioId, regulatorId, "MAX_FX_SPREAD", 3.50m, "BDC", regulatorUserId, ct);

        var consultationId = await _consultationService.CreateConsultationAsync(
            consultationScenarioId,
            regulatorId,
            $"{DemoPrefix} BDC FX spread roundtable",
            "Please provide implementation impacts, market-liquidity concerns, and suggested phase-in terms.",
            currentPeriod.AsOfDate.AddDays(21),
            [
                new ConsultationProvisionInput(1, "Revised spread cap", "Reduce the allowable retail BDC FX spread from 5.0% to 3.5%.", "MAX_FX_SPREAD"),
                new ConsultationProvisionInput(2, "Daily disclosure pack", "Require daily publication of retail buy/sell rates and end-of-day liquidity positions.", null),
                new ConsultationProvisionInput(3, "Escalation threshold", "Escalate repeated spread breaches above 4.5% into automatic supervisory review.", null)
            ],
            regulatorUserId,
            ct);

        await _consultationService.PublishConsultationAsync(consultationId, regulatorId, regulatorUserId, ct);
        var publishedConsultation = await _consultationService.GetConsultationAsync(consultationId, regulatorId, ct);

        var fc001Admin = await ResolveInstitutionUserAsync("admin", 1, ct);
        var cashcodeAdmin = await ResolveInstitutionUserAsync("cashcodeadmin", 3, ct);
        var accessAdmin = await ResolveInstitutionUserAsync("accessdemo", 4, ct);

        await _consultationService.SubmitFeedbackAsync(
            consultationId,
            1,
            FeedbackPosition.PartialSupport,
            "Supportive in principle, but smaller operators need a more explicit transition schedule.",
            BuildProvisionFeedback(publishedConsultation, ProvisionPosition.Amend, ProvisionPosition.Support, ProvisionPosition.Neutral),
            fc001Admin.Id,
            ct);

        await _consultationService.SubmitFeedbackAsync(
            consultationId,
            3,
            FeedbackPosition.Oppose,
            "Current market liquidity remains thin and the spread cap should be sequenced with funding market reforms.",
            BuildProvisionFeedback(publishedConsultation, ProvisionPosition.Oppose, ProvisionPosition.Amend, ProvisionPosition.Oppose),
            cashcodeAdmin.Id,
            ct);

        await _consultationService.SubmitFeedbackAsync(
            consultationId,
            4,
            FeedbackPosition.Support,
            "Supportive if disclosure requirements are standardised across channels and accompanied by a short dry run.",
            BuildProvisionFeedback(publishedConsultation, ProvisionPosition.Support, ProvisionPosition.Support, ProvisionPosition.Amend),
            accessAdmin.Id,
            ct);

        await _consultationService.CloseConsultationAsync(consultationId, regulatorId, regulatorUserId, ct);
        await _consultationService.AggregateFeedbackAsync(consultationId, regulatorId, regulatorUserId, ct);

        var simulatedScenarioId = await _policyScenarioService.CreateScenarioAsync(
            regulatorId,
            $"{DemoPrefix} DMB liquidity floor phase-in",
            "Liquidity simulation for a tighter LCR and NSFR floor across large deposit money banks.",
            PolicyDomain.Liquidity,
            "DMB",
            currentPeriod.AsOfDate,
            regulatorUserId,
            ct);
        scenarioCount++;

        await _policyScenarioService.AddParameterAsync(simulatedScenarioId, regulatorId, "MIN_LCR", 115m, "DMB", regulatorUserId, ct);
        await _policyScenarioService.AddParameterAsync(simulatedScenarioId, regulatorId, "MIN_NSFR", 105m, "DMB", regulatorUserId, ct);
        var simulatedRunId = await CreateImpactRunAsync(simulatedScenarioId, regulatorId, regulatorUserId, "DMB", currentPeriod, ct);
        await _costBenefitAnalyser.GenerateAnalysisAsync(simulatedRunId, regulatorId, regulatorUserId, ct);

        var enactedScenarioId = await _policyScenarioService.CreateScenarioAsync(
            regulatorId,
            $"{DemoPrefix} MFB capital restoration programme",
            "Enacted programme to raise the effective capital floor for weaker microfinance entities with a measured phase-in.",
            PolicyDomain.CapitalAdequacy,
            "MFB",
            currentPeriod.AsOfDate,
            regulatorUserId,
            ct);
        scenarioCount++;

        await _policyScenarioService.AddParameterAsync(enactedScenarioId, regulatorId, "MIN_CAR_MFB", 12m, "MFB", regulatorUserId, ct);
        await _policyScenarioService.AddParameterAsync(enactedScenarioId, regulatorId, "MIN_LIQUIDITY_RATIO", 105m, "MFB", regulatorUserId, ct);
        var enactedRunId = await CreateImpactRunAsync(enactedScenarioId, regulatorId, regulatorUserId, "MFB", currentPeriod, ct);
        await _costBenefitAnalyser.GenerateAnalysisAsync(enactedRunId, regulatorId, regulatorUserId, ct);

        var decisionId = await _policyDecisionService.RecordDecisionAsync(
            enactedScenarioId,
            regulatorId,
            DecisionType.EnactAmended,
            "Proceed with a six-month restoration programme focused on weaker microfinance entities, with monthly supervisory reporting on capital rebuild milestones.",
            currentPeriod.AsOfDate.AddMonths(-2),
            6,
            "CBN/PD/2026/014",
            regulatorUserId,
            ct);

        await _historicalImpactTracker.RunTrackingCycleAsync(ct);

        _logger.LogInformation(
            "Policy lifecycle demo seeded. DecisionId={DecisionId} DraftScenarioId={DraftScenarioId}",
            decisionId,
            draftScenarioId);

        return scenarioCount;
    }

    private static IReadOnlyList<ProvisionFeedbackInput> BuildProvisionFeedback(
        ConsultationDetail consultation,
        ProvisionPosition first,
        ProvisionPosition second,
        ProvisionPosition third)
    {
        var provisions = consultation.Provisions.OrderBy(x => x.ProvisionNumber).ToList();
        return
        [
            new ProvisionFeedbackInput(
                provisions[0].Id,
                first,
                first == ProvisionPosition.Oppose ? "The proposed calibration may reduce market depth without a phased transition." : "Provision is directionally appropriate with clearer implementation guidance.",
                first == ProvisionPosition.Amend ? "Introduce a 90-day transition window for existing operators." : null,
                "High immediate impact on smaller operators."),
            new ProvisionFeedbackInput(
                provisions[1].Id,
                second,
                second == ProvisionPosition.Oppose ? "Daily disclosure should align with a standard template to avoid inconsistent reporting." : "Disclosure pack is useful for comparability and market monitoring.",
                second == ProvisionPosition.Amend ? "Publish a regulator-issued disclosure template and portal upload route." : null,
                "Moderate operational uplift with manageable change cost."),
            new ProvisionFeedbackInput(
                provisions[2].Id,
                third,
                third == ProvisionPosition.Oppose ? "Automatic escalation should distinguish isolated breaches from persistent misconduct." : "Escalation threshold is reasonable if paired with supervisory judgement.",
                third == ProvisionPosition.Amend ? "Require two consecutive breaches before automatic case escalation." : null,
                "Supports earlier intervention on repeat offenders.")
        ];
    }

    private async Task<long> CreateImpactRunAsync(
        long scenarioId,
        int regulatorId,
        int userId,
        string targetEntityType,
        DemoPeriod currentPeriod,
        CancellationToken ct)
    {
        var scenario = await _db.PolicyScenarios
            .Include(x => x.Parameters)
            .FirstOrDefaultAsync(x => x.Id == scenarioId && x.RegulatorId == regulatorId, ct)
            ?? throw new InvalidOperationException($"Scenario {scenarioId} was not found.");

        var metrics = await _db.Database
            .SqlQueryRaw<ImpactMetricRow>(
                """
                SELECT i.Id AS InstitutionId,
                       i.InstitutionCode,
                       i.InstitutionName,
                       pm.InstitutionType,
                       pm.CAR,
                       pm.Tier1Ratio,
                       pm.LCR,
                       pm.NSFR
                FROM dbo.institutions i
                INNER JOIN meta.prudential_metrics pm ON pm.InstitutionId = i.Id
                WHERE pm.PeriodCode = {0}
                  AND ({1} = 'ALL' OR pm.InstitutionType = {1})
                ORDER BY i.InstitutionCode
                """,
                currentPeriod.PeriodCode,
                targetEntityType)
            .ToListAsync(ct);

        var nextRunNumber = await _db.ImpactAssessmentRuns
            .Where(x => x.ScenarioId == scenarioId)
            .MaxAsync(x => (int?)x.RunNumber, ct) ?? 0;

        var run = new ImpactAssessmentRun
        {
            ScenarioId = scenarioId,
            RegulatorId = regulatorId,
            RunNumber = nextRunNumber + 1,
            Status = ImpactRunStatus.Completed,
            SnapshotDate = currentPeriod.AsOfDate,
            CreatedByUserId = userId,
            StartedAt = DateTime.UtcNow.AddSeconds(-3),
            CompletedAt = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid()
        };

        _db.ImpactAssessmentRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        var compliant = 0;
        var wouldBreach = 0;
        var alreadyBreaching = 0;
        var notAffected = 0;
        decimal totalShortfall = 0m;
        decimal totalCost = 0m;

        foreach (var metric in metrics)
        {
            var details = new List<EntityImpactDetail>();
            var category = ImpactCategory.NotAffected;
            decimal? primaryMetric = null;
            decimal? primaryThreshold = null;
            decimal aggregateGap = 0m;

            foreach (var parameter in scenario.Parameters.OrderBy(x => x.DisplayOrder))
            {
                var currentValue = ResolveMetricValue(metric, parameter.ParameterCode);
                if (!currentValue.HasValue)
                {
                    details.Add(new EntityImpactDetail(
                        parameter.ParameterCode,
                        0m,
                        parameter.CurrentValue,
                        parameter.ProposedValue,
                        0m,
                        "NO_DATA"));
                    continue;
                }

                primaryMetric ??= currentValue.Value;
                primaryThreshold ??= parameter.ProposedValue;

                var gap = currentValue.Value - parameter.ProposedValue;
                aggregateGap += gap < 0 ? gap : 0;

                var status = currentValue.Value < parameter.CurrentValue
                    ? "ALREADY_BREACHING"
                    : currentValue.Value < parameter.ProposedValue
                        ? "WOULD_BREACH"
                        : "COMPLIANT";

                details.Add(new EntityImpactDetail(
                    parameter.ParameterCode,
                    currentValue.Value,
                    parameter.CurrentValue,
                    parameter.ProposedValue,
                    gap,
                    status));

                category = status switch
                {
                    "ALREADY_BREACHING" => ImpactCategory.AlreadyBreaching,
                    "WOULD_BREACH" when category != ImpactCategory.AlreadyBreaching => ImpactCategory.WouldBreach,
                    "COMPLIANT" when category == ImpactCategory.NotAffected => ImpactCategory.CurrentlyCompliant,
                    _ => category
                };
            }

            var cost = category == ImpactCategory.WouldBreach
                ? EstimateComplianceCost(metric.InstitutionType, Math.Abs(aggregateGap))
                : 0m;

            switch (category)
            {
                case ImpactCategory.CurrentlyCompliant:
                    compliant++;
                    break;
                case ImpactCategory.WouldBreach:
                    wouldBreach++;
                    totalShortfall += Math.Abs(aggregateGap);
                    totalCost += cost;
                    break;
                case ImpactCategory.AlreadyBreaching:
                    alreadyBreaching++;
                    totalShortfall += Math.Abs(aggregateGap);
                    break;
                default:
                    notAffected++;
                    break;
            }

            _db.EntityImpactResults.Add(new EntityImpactResult
            {
                RunId = run.Id,
                InstitutionId = metric.InstitutionId,
                InstitutionCode = metric.InstitutionCode,
                InstitutionName = metric.InstitutionName,
                EntityType = metric.InstitutionType,
                ImpactCategory = category,
                ParameterResults = JsonSerializer.Serialize(details),
                CurrentMetricValue = primaryMetric,
                ProposedThreshold = primaryThreshold,
                GapToCompliance = aggregateGap,
                EstimatedComplianceCost = cost
            });
        }

        run.TotalEntitiesEvaluated = metrics.Count;
        run.EntitiesCurrentlyCompliant = compliant;
        run.EntitiesWouldBreach = wouldBreach;
        run.EntitiesAlreadyBreaching = alreadyBreaching;
        run.EntitiesNotAffected = notAffected;
        run.AggregateCapitalShortfall = totalShortfall;
        run.AggregateComplianceCost = totalCost;
        run.ExecutionTimeMs = 2300;

        if (scenario.Status is PolicyStatus.ParametersSet or PolicyStatus.Simulated)
        {
            scenario.Status = PolicyStatus.Simulated;
            scenario.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return run.Id;
    }

    private static decimal? ResolveMetricValue(ImpactMetricRow metric, string parameterCode)
    {
        return parameterCode switch
        {
            "MIN_CAR" or "MIN_CAR_DSIB" or "MIN_CAR_MFB" => metric.CAR,
            "MIN_CET1" => metric.Tier1Ratio,
            "MIN_LCR" or "MIN_LIQUIDITY_RATIO" => metric.LCR,
            "MIN_NSFR" => metric.NSFR,
            _ => null
        };
    }

    private static decimal EstimateComplianceCost(string institutionType, decimal shortfall)
    {
        var multiplier = institutionType.ToUpperInvariant() switch
        {
            "DMB" => 600m,
            "MFB" => 20m,
            "BDC" => 8m,
            _ => 15m
        };

        return Math.Round(shortfall * multiplier, 2);
    }

    private async Task<InstitutionUser> ResolveInstitutionUserAsync(string username, int institutionId, CancellationToken ct)
    {
        return await _db.InstitutionUsers
            .FirstOrDefaultAsync(x => x.Username == username && x.InstitutionId == institutionId, ct)
            ?? throw new InvalidOperationException($"Institution user '{username}' for institution {institutionId} was not found.");
    }

    private async Task<int> SeedExaminationWorkspaceAsync(
        Guid regulatorTenantId,
        string regulatorCode,
        int regulatorUserId,
        IReadOnlyDictionary<int, DemoInstitution> institutions,
        CancellationToken ct)
    {
        var regulatorPortalUsers = await _db.PortalUsers
            .AsNoTracking()
            .Where(x => x.TenantId == regulatorTenantId)
            .OrderBy(x => x.Username)
            .ToListAsync(ct);

        var project = await _examinationWorkspaceService.CreateProject(
            regulatorTenantId,
            regulatorUserId,
            new ExaminationProjectCreateRequest
            {
                Name = $"{DemoPrefix} Joint prudential and conduct review",
                Scope = "Combined prudential, conduct, and thematic review covering Access Bank, CASHCODE, and Example MFB.",
                InstitutionIds = [4, 3, 2],
                PeriodFrom = DateTime.UtcNow.AddMonths(-12),
                PeriodTo = DateTime.UtcNow,
                TeamAssignments = regulatorPortalUsers
                    .Take(3)
                    .Select((user, index) => new ExaminationTeamAssignment
                    {
                        UserId = user.Id,
                        DisplayName = user.DisplayName,
                        Role = index switch
                        {
                            0 => "Team Lead",
                            1 => "Prudential Examiner",
                            _ => "Conduct Specialist"
                        }
                    })
                    .ToList(),
                Milestones =
                [
                    new ExaminationMilestone
                    {
                        Title = "Kick-off and scope confirmation",
                        Owner = "CBN Demo Admin",
                        DueAt = DateTime.UtcNow.AddDays(-10),
                        Completed = true
                    },
                    new ExaminationMilestone
                    {
                        Title = "Evidence request issuance",
                        Owner = "CBN Demo Approver",
                        DueAt = DateTime.UtcNow.AddDays(4),
                        Completed = false
                    },
                    new ExaminationMilestone
                    {
                        Title = "Executive close-out pack",
                        Owner = "CBN Demo Viewer",
                        DueAt = DateTime.UtcNow.AddDays(14),
                        Completed = false
                    }
                ]
            },
            ct);

        var latestSubmission = await _db.Submissions
            .AsNoTracking()
            .Include(x => x.ReturnPeriod)
                .ThenInclude(x => x!.Module)
            .Where(x => x.InstitutionId == 4
                        && x.ReturnPeriod != null
                        && x.ReturnPeriod.Module != null
                        && x.ReturnPeriod.Module.RegulatorCode == regulatorCode)
            .OrderByDescending(x => x.SubmittedAt ?? x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (latestSubmission is not null)
        {
            await _examinationWorkspaceService.AddAnnotation(
                regulatorTenantId,
                project.Id,
                latestSubmission.Id,
                latestSubmission.InstitutionId,
                "CAR",
                "Reviewer note: capital rebuild assumption should be reconciled to the latest prudential pack before field close-out.",
                regulatorUserId,
                ct);
        }

        var highRiskFinding = await _examinationWorkspaceService.CreateFinding(
            regulatorTenantId,
            regulatorCode,
            project.Id,
            new ExaminationFindingCreateRequest
            {
                SubmissionId = latestSubmission?.Id,
                InstitutionId = 4,
                Title = "Capital planning assumptions not aligned to stressed liquidity profile",
                RiskArea = "Capital adequacy",
                Observation = "Management capital restoration scenarios materially understate liquidity outflows under a severe market shock.",
                RiskRating = ExaminationRiskRating.High,
                Recommendation = "Require a refreshed ICAAP pack with stress assumptions aligned to the supervisory scenario library.",
                Status = ExaminationWorkflowStatus.ManagementResponseRequired,
                RemediationStatus = ExaminationRemediationStatus.AwaitingManagementResponse,
                ModuleCode = "DMB_BASEL3",
                PeriodLabel = "Q1 demo review",
                FieldCode = "CAR",
                ManagementResponseDeadline = DateTime.UtcNow.AddDays(7)
            },
            regulatorUserId,
            ct);

        var conductFinding = await _examinationWorkspaceService.CreateFinding(
            regulatorTenantId,
            regulatorCode,
            project.Id,
            new ExaminationFindingCreateRequest
            {
                InstitutionId = 3,
                Title = "Repeated FX spread breaches and incomplete end-of-day disclosures",
                RiskArea = "Conduct risk",
                Observation = "Daily review indicates repeated spread outliers without a corresponding management attestation trail.",
                RiskRating = ExaminationRiskRating.Medium,
                Recommendation = "Escalate to the conduct desk and require weekly remediation evidence.",
                Status = ExaminationWorkflowStatus.FindingDocumented,
                RemediationStatus = ExaminationRemediationStatus.Open,
                ModuleCode = "BDC_CBN",
                PeriodLabel = "Q1 demo review",
                ManagementResponseDeadline = DateTime.UtcNow.AddDays(10)
            },
            regulatorUserId,
            ct);

        await _examinationWorkspaceService.UpdateFinding(
            regulatorTenantId,
            project.Id,
            conductFinding.Id,
            new ExaminationFindingUpdateRequest
            {
                Status = ExaminationWorkflowStatus.InProgress,
                RemediationStatus = ExaminationRemediationStatus.Escalated,
                RiskRating = ExaminationRiskRating.High,
                Observation = conductFinding.Observation,
                Recommendation = conductFinding.Recommendation,
                ManagementResponseDeadline = conductFinding.ManagementResponseDeadline,
                ManagementResponse = "Preliminary remediation plan received from operations and treasury leadership.",
                ManagementActionPlan = "Submit daily spread monitoring file and branch-level control attestations for four weeks.",
                EvidenceReference = "Demo escalation note"
            },
            regulatorUserId,
            ct);

        var evidenceRequest = await _examinationWorkspaceService.CreateEvidenceRequest(
            regulatorTenantId,
            project.Id,
            new ExaminationEvidenceRequestCreateRequest
            {
                FindingId = highRiskFinding.Id,
                InstitutionId = 4,
                Title = "Refresh stressed capital plan support pack",
                RequestText = "Provide the latest board-approved ICAAP schedule, management stress deck, and evidence of treasury contingency funding assumptions.",
                RequestedItems =
                [
                    "Board-approved ICAAP deck",
                    "Treasury liquidity contingency playbook",
                    "Stress-testing model output extract"
                ],
                DueAt = DateTime.UtcNow.AddDays(5)
            },
            regulatorUserId,
            ct);

        try
        {
            await using var evidenceStream = new MemoryStream(
                System.Text.Encoding.UTF8.GetBytes("Demo examination evidence file for RegOS client walkthrough."));
            await _examinationWorkspaceService.UploadEvidence(
                regulatorTenantId,
                project.Id,
                highRiskFinding.Id,
                evidenceRequest.Id,
                latestSubmission?.Id,
                4,
                "demo-icaap-pack.txt",
                "text/plain",
                evidenceStream.Length,
                evidenceStream,
                ExaminationEvidenceKind.SupportingDocument,
                ExaminationEvidenceUploaderRole.Examiner,
                "Seeded by the end-to-end demo pack.",
                regulatorUserId,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Evidence upload failed during demo seed for examination project {ProjectId}.", project.Id);
        }

        if (institutions.TryGetValue(4, out var institution))
        {
            _logger.LogInformation(
                "Examination workspace seeded for institution {InstitutionCode} with project {ProjectId}.",
                institution.InstitutionCode,
                project.Id);
        }

        return 1;
    }

    private static List<DemoPeriod> BuildDemoPeriods(DateOnly today)
    {
        var quarterIndex = ((today.Month - 1) / 3) + 1;
        var periods = new List<DemoPeriod>(4);

        for (var offset = 3; offset >= 0; offset--)
        {
            var reference = today.ToDateTime(TimeOnly.MinValue).AddMonths(-3 * offset);
            var periodQuarter = ((reference.Month - 1) / 3) + 1;
            var code = $"{reference.Year}-Q{periodQuarter}";
            var asOf = offset == 0
                ? DateOnly.FromDateTime(today.ToDateTime(TimeOnly.MinValue))
                : QuarterEnd(reference.Year, periodQuarter);
            periods.Add(new DemoPeriod(code, asOf));
        }

        if (periods[^1].PeriodCode != $"{today.Year}-Q{quarterIndex}")
        {
            periods[^1] = new DemoPeriod($"{today.Year}-Q{quarterIndex}", today);
        }

        return periods;
    }

    private static DateOnly QuarterEnd(int year, int quarter)
    {
        var month = quarter * 3;
        var value = new DateTime(year, month, 1).AddMonths(1).AddDays(-1);
        return DateOnly.FromDateTime(value);
    }

    private static List<DemoPrudentialMetricSeed> BuildPrudentialMetricSeeds(
        string regulatorCode,
        IReadOnlyList<DemoPeriod> periods)
    {
        return
        [
            .. BuildPrudentialSeries(
                1, "FC", regulatorCode, periods,
                new PrudentialProfile(
                    15.2m, 12.8m, 12.5m, 10.8m, 42000m, 51000m, 61000m, 43000m,
                    3.2m, 4.9m, 76m, 61m, 1.8m, 1.1m, 3.8m, 3.1m, 61m, 74m,
                    128m, 114m, 110m, 102m, 26m, 29m, 7m, 11m, 7m, 11m,
                    16m, 19m, 4m, 7m, 90m, 78m, 0, 1, 2.2m, 3.8m, "UNQUAL")),
            .. BuildPrudentialSeries(
                2, "MFB", regulatorCode, periods,
                new PrudentialProfile(
                    13.0m, 9.6m, 10.9m, 8.1m, 15000m, 18400m, 23500m, 16600m,
                    4.0m, 8.7m, 72m, 48m, 1.6m, 0.4m, 4.4m, 2.5m, 64m, 83m,
                    119m, 101m, 108m, 96m, 24m, 33m, 6m, 14m, 5m, 9m,
                    12m, 18m, 3m, 8m, 74m, 58m, 1, 3, 2.8m, 6.2m, "QUALIFIED")),
            .. BuildPrudentialSeries(
                3, "BDC", regulatorCode, periods,
                new PrudentialProfile(
                    14.1m, 10.2m, 11.6m, 9.5m, 8200m, 9800m, 6200m, 4700m,
                    2.2m, 3.4m, 81m, 69m, 2.0m, 0.9m, 7.1m, 4.8m, 58m, 71m,
                    138m, 104m, 112m, 97m, 31m, 37m, 18m, 27m, 2m, 4m,
                    6m, 9m, 2m, 5m, 78m, 66m, 0, 1, 2.5m, 4.2m, "UNQUAL")),
            .. BuildPrudentialSeries(
                4, "DMB", regulatorCode, periods,
                new PrudentialProfile(
                    19.1m, 16.2m, 16.4m, 14.3m, 1350000m, 1560000m, 1780000m, 1210000m,
                    3.7m, 4.9m, 86m, 74m, 2.4m, 1.7m, 5.2m, 4.1m, 54m, 67m,
                    145m, 126m, 122m, 108m, 18m, 22m, 6m, 12m, 9m, 14m,
                    19m, 24m, 6m, 10m, 92m, 81m, 0, 1, 1.9m, 3.1m, "UNQUAL")),
            .. BuildPrudentialSeries(
                1002, "BDC", regulatorCode, periods,
                new PrudentialProfile(
                    12.8m, 7.8m, 9.9m, 6.3m, 6100m, 7200m, 4300m, 3200m,
                    5.3m, 12.4m, 58m, 34m, 0.9m, -0.6m, 3.9m, 1.8m, 63m, 92m,
                    118m, 88m, 103m, 84m, 34m, 42m, 22m, 33m, 4m, 8m,
                    5m, 11m, 2m, 6m, 68m, 47m, 2, 4, 3.6m, 7.5m, "ADVERSE"))
        ];
    }

    private static IEnumerable<DemoPrudentialMetricSeed> BuildPrudentialSeries(
        int institutionId,
        string institutionType,
        string regulatorCode,
        IReadOnlyList<DemoPeriod> periods,
        PrudentialProfile profile)
    {
        for (var index = 0; index < periods.Count; index++)
        {
            var position = periods.Count == 1 ? 1m : index / (decimal)(periods.Count - 1);
            var totalAssets = Interpolate(profile.StartTotalAssets, profile.EndTotalAssets, position);
            var grossLoans = Math.Round(totalAssets * 0.58m, 2);
            var nplRatio = Interpolate(profile.StartNplRatio, profile.EndNplRatio, position);
            var roa = Interpolate(profile.StartRoa, profile.EndRoa, position);
            var fxExposureRatio = Interpolate(profile.StartFxExposureRatio, profile.EndFxExposureRatio, position);
            var bondPortfolioAssetPct = Interpolate(profile.StartBondPortfolioAssetPct, profile.EndBondPortfolioAssetPct, position);
            var liquidAssetsRatio = Interpolate(profile.StartLiquidAssetsRatio, profile.EndLiquidAssetsRatio, position);
            var lateFilingCount = InterpolateInt(profile.StartLateFilingCount, profile.EndLateFilingCount, position);
            var relatedPartyLendingRatio = Interpolate(profile.StartRelatedPartyLendingRatio, profile.EndRelatedPartyLendingRatio, position);

            yield return new DemoPrudentialMetricSeed(
                institutionId,
                regulatorCode,
                institutionType,
                periods[index].AsOfDate.ToDateTime(TimeOnly.MinValue),
                periods[index].PeriodCode,
                Interpolate(profile.StartCar, profile.EndCar, position),
                Interpolate(profile.StartTier1Ratio, profile.EndTier1Ratio, position),
                Math.Round(totalAssets * 0.032m, 2),
                Math.Round(totalAssets * 0.71m, 2),
                totalAssets,
                Interpolate(profile.StartTotalDeposits, profile.EndTotalDeposits, position),
                nplRatio,
                Math.Round(grossLoans * nplRatio / 100m, 2),
                grossLoans,
                Interpolate(profile.StartProvisioningCoverage, profile.EndProvisioningCoverage, position),
                Interpolate(profile.StartOilExposurePct, profile.EndOilExposurePct, position),
                Interpolate(profile.StartAgriExposurePct, profile.EndAgriExposurePct, position),
                roa,
                EstimateRoe(institutionType, roa),
                Interpolate(profile.StartNim, profile.EndNim, position),
                Interpolate(profile.StartCir, profile.EndCir, position),
                Interpolate(profile.StartLcr, profile.EndLcr, position),
                Interpolate(profile.StartNsfr, profile.EndNsfr, position),
                liquidAssetsRatio,
                Interpolate(profile.StartDepositConcentration, profile.EndDepositConcentration, position),
                fxExposureRatio,
                EstimateFxLoansAssetPct(institutionType, fxExposureRatio, bondPortfolioAssetPct),
                bondPortfolioAssetPct,
                EstimateInterestRateSensitivity(institutionType, bondPortfolioAssetPct, liquidAssetsRatio),
                EstimateComplianceScore(institutionType, profile.AuditOpinionCode, lateFilingCount, relatedPartyLendingRatio, nplRatio),
                lateFilingCount,
                profile.AuditOpinionCode,
                relatedPartyLendingRatio);
        }
    }

    private static List<DemoInterbankExposureSeed> BuildInterbankExposureSeeds(string regulatorCode, DemoPeriod currentPeriod)
    {
        var asOfDate = currentPeriod.AsOfDate.ToDateTime(TimeOnly.MinValue);
        return
        [
            new DemoInterbankExposureSeed(4, 1, regulatorCode, currentPeriod.PeriodCode, 18000m, "PLACEMENT", asOfDate),
            new DemoInterbankExposureSeed(4, 2, regulatorCode, currentPeriod.PeriodCode, 12000m, "PLACEMENT", asOfDate),
            new DemoInterbankExposureSeed(1, 4, regulatorCode, currentPeriod.PeriodCode, 6000m, "PLACEMENT", asOfDate),
            new DemoInterbankExposureSeed(1, 2, regulatorCode, currentPeriod.PeriodCode, 3500m, "PLACEMENT", asOfDate),
            new DemoInterbankExposureSeed(2, 4, regulatorCode, currentPeriod.PeriodCode, 2000m, "PLACEMENT", asOfDate),
            new DemoInterbankExposureSeed(4, 3, regulatorCode, currentPeriod.PeriodCode, 2500m, "SETTLEMENT", asOfDate),
            new DemoInterbankExposureSeed(3, 1002, regulatorCode, currentPeriod.PeriodCode, 900m, "FX", asOfDate),
            new DemoInterbankExposureSeed(1002, 3, regulatorCode, currentPeriod.PeriodCode, 750m, "FX", asOfDate)
        ];
    }

    private static IReadOnlyList<DemoBdcTransactionSeed> BuildBdcTransactionSeeds(
        Guid tenantId,
        string regulatorCode,
        DemoPeriod currentPeriod)
    {
        var seeds = new List<DemoBdcTransactionSeed>();
        var endDate = currentPeriod.AsOfDate;
        var startDate = endDate.AddDays(-34);

        for (var offset = 0; offset < 35; offset++)
        {
            var date = startDate.AddDays(offset).ToDateTime(TimeOnly.MinValue);
            var midRate = 1504.5m + (offset % 4);
            var upper = midRate + 8m;
            var lower = midRate - 8m;

            var cashcodeVolume = offset == 30 ? 420000m : 24000m + (offset % 6) * 1800m;
            var buzzwallVolume = 21000m + (offset % 5) * 1500m;

            var reciprocalTrade = offset is 24 or 25 or 26;
            var outsideBand = offset >= 28;

            seeds.Add(new DemoBdcTransactionSeed(
                tenantId,
                3,
                regulatorCode,
                date,
                currentPeriod.PeriodCode,
                "USD",
                "NGN",
                midRate - 2.4m,
                midRate + 3.2m,
                reciprocalTrade ? 62000m : cashcodeVolume,
                reciprocalTrade ? 61000m : cashcodeVolume,
                midRate,
                upper,
                lower,
                reciprocalTrade ? 1002 : null));

            seeds.Add(new DemoBdcTransactionSeed(
                tenantId,
                1002,
                regulatorCode,
                date,
                currentPeriod.PeriodCode,
                "USD",
                "NGN",
                midRate - 1.8m,
                outsideBand ? upper + 24m : midRate + 4.8m,
                reciprocalTrade ? 60500m : buzzwallVolume,
                reciprocalTrade ? 61500m : buzzwallVolume,
                midRate,
                upper,
                lower,
                reciprocalTrade ? 3 : null));
        }

        return seeds;
    }

    private static IReadOnlyList<DemoCorporateAnnouncementSeed> BuildCorporateAnnouncementSeeds(
        Guid tenantId,
        string regulatorCode,
        DemoPeriod currentPeriod)
    {
        return
        [
            new DemoCorporateAnnouncementSeed(
                tenantId,
                regulatorCode,
                "DEMO-ALPHA",
                "Demo Alpha Plc",
                "EARNINGS_RELEASE",
                currentPeriod.AsOfDate.AddDays(-2).ToDateTime(TimeOnly.MinValue),
                currentPeriod.AsOfDate.AddDays(1).ToDateTime(TimeOnly.MinValue),
                "DEMO-SEED")
        ];
    }

    private static IReadOnlyList<DemoCmoTradeSeed> BuildCmoTradeSeeds(
        Guid tenantId,
        string regulatorCode,
        DemoPeriod currentPeriod)
    {
        var seeds = new List<DemoCmoTradeSeed>();
        var startDate = currentPeriod.AsOfDate.AddDays(-30);

        for (var offset = 0; offset < 24; offset++)
        {
            var tradeDate = startDate.AddDays(offset).ToDateTime(TimeOnly.MinValue);
            var tradeTime = tradeDate.AddHours(10);
            var preAnnouncementWindow = offset is 21 or 22 or 23;
            var accessQty = preAnnouncementWindow ? 6500m + (offset - 21) * 300m : 460m + (offset % 3) * 40m;

            seeds.Add(new DemoCmoTradeSeed(
                tenantId,
                4,
                regulatorCode,
                "CMO",
                "DEMO-ALPHA",
                "Demo Alpha Plc",
                tradeDate,
                currentPeriod.PeriodCode,
                "BUY",
                accessQty,
                14.75m,
                Math.Round(accessQty * 14.75m, 2),
                $"ACC-{offset:D3}",
                tradeTime.AddHours(1),
                tradeTime,
                false));

            var fc001Qty = offset < 18 ? 1100m : 240m;
            var late = offset is 8 or 13 or 17 or 22;
            var reportedAt = late ? tradeTime.AddHours(40) : tradeTime.AddHours(2);

            seeds.Add(new DemoCmoTradeSeed(
                tenantId,
                1,
                regulatorCode,
                "CMO",
                offset < 20 ? "DEMO-BETA" : "DEMO-GAMMA",
                offset < 20 ? "Demo Beta Power" : "Demo Gamma Breweries",
                tradeDate,
                currentPeriod.PeriodCode,
                "SELL",
                fc001Qty,
                offset < 20 ? 22.40m : 6.35m,
                Math.Round(fc001Qty * (offset < 20 ? 22.40m : 6.35m), 2),
                $"FC1-{offset:D3}",
                reportedAt,
                tradeTime,
                late));
        }

        return seeds;
    }

    private static IReadOnlyList<DemoAmlMetricSeed> BuildAmlMetricSeeds(
        Guid tenantId,
        string regulatorCode,
        DemoPeriod currentPeriod)
    {
        var asOfDate = currentPeriod.AsOfDate.ToDateTime(TimeOnly.MinValue);
        return
        [
            new DemoAmlMetricSeed(tenantId, 4, regulatorCode, "BANK", currentPeriod.PeriodCode, asOfDate, 14, 65, 8.8m, 0.60m, 2, 6, 1, 850, 0.12m, 8, 2, 0.96m),
            new DemoAmlMetricSeed(tenantId, 1, regulatorCode, "BANK", currentPeriod.PeriodCode, asOfDate, 11, 52, 8.8m, 0.25m, 3, 5, 1, 640, 0.18m, 6, 3, 0.92m),
            new DemoAmlMetricSeed(tenantId, 2, regulatorCode, "BANK", currentPeriod.PeriodCode, asOfDate, 1, 39, 8.8m, -2.35m, 1, 4, 0, 420, 0.02m, 1, 6, 0.74m),
            new DemoAmlMetricSeed(tenantId, 3, regulatorCode, "BANK", currentPeriod.PeriodCode, asOfDate, 4, 28, 8.8m, -0.85m, 2, 3, 1, 210, 0.98m, 0, 4, 0.70m),
            new DemoAmlMetricSeed(tenantId, 1002, regulatorCode, "BANK", currentPeriod.PeriodCode, asOfDate, 2, 31, 8.8m, -1.65m, 18, 2, 2, 260, 0.97m, 0, 7, 0.52m)
        ];
    }

    private static IReadOnlyList<DemoInsuranceMetricSeed> BuildInsuranceMetricSeeds(
        Guid tenantId,
        string regulatorCode,
        DemoPeriod currentPeriod)
    {
        var asOfDate = currentPeriod.AsOfDate.ToDateTime(TimeOnly.MinValue);
        return
        [
            new DemoInsuranceMetricSeed(tenantId, 4, regulatorCode, "INSURER", currentPeriod.PeriodCode, asOfDate, 185m, 430m, 43m, 31m, 426m, 430m, 4m, 28m, 8m, 2, 7m),
            new DemoInsuranceMetricSeed(tenantId, 1, regulatorCode, "INSURER", currentPeriod.PeriodCode, asOfDate, 102m, 305m, 33m, 31m, 286m, 300m, 14m, 22m, 24m, 4, 11m),
            new DemoInsuranceMetricSeed(tenantId, 2, regulatorCode, "INSURER", currentPeriod.PeriodCode, asOfDate, 18m, 112m, 16m, 31m, 70m, 100m, 30m, 16m, 52m, 9, 24m)
        ];
    }

    private static decimal Interpolate(decimal start, decimal end, decimal position)
        => Math.Round(start + ((end - start) * position), 4);

    private static int InterpolateInt(int start, int end, decimal position)
        => (int)Math.Round(start + ((end - start) * position), MidpointRounding.AwayFromZero);

    private static decimal EstimateRoe(string institutionType, decimal roa)
    {
        var leverage = institutionType.ToUpperInvariant() switch
        {
            "DMB" => 7.4m,
            "FC" => 6.1m,
            "MFB" => 5.3m,
            "BDC" => 4.6m,
            _ => 5.5m
        };

        return Math.Round(roa * leverage, 4);
    }

    private static decimal EstimateFxLoansAssetPct(
        string institutionType,
        decimal fxExposureRatio,
        decimal bondPortfolioAssetPct)
    {
        var multiplier = institutionType.ToUpperInvariant() switch
        {
            "DMB" => 0.88m,
            "FC" => 0.72m,
            "MFB" => 0.56m,
            "BDC" => 0.38m,
            _ => 0.60m
        };

        var estimate = Math.Abs(fxExposureRatio) * multiplier + (bondPortfolioAssetPct * 0.18m);
        return Math.Round(Math.Clamp(estimate, 1.0m, 35.0m), 4);
    }

    private static decimal EstimateInterestRateSensitivity(
        string institutionType,
        decimal bondPortfolioAssetPct,
        decimal liquidAssetsRatio)
    {
        var baseBuffer = institutionType.ToUpperInvariant() switch
        {
            "DMB" => 4.0m,
            "FC" => 3.0m,
            "MFB" => 2.5m,
            "BDC" => 2.0m,
            _ => 2.5m
        };

        var liquidityStress = Math.Max(0m, 100m - liquidAssetsRatio) * 0.12m;
        var estimate = (bondPortfolioAssetPct * 0.60m) + liquidityStress + baseBuffer;
        return Math.Round(Math.Clamp(estimate, 1.0m, 20.0m), 4);
    }

    private static decimal EstimateComplianceScore(
        string institutionType,
        string auditOpinionCode,
        int lateFilingCount,
        decimal relatedPartyLendingRatio,
        decimal nplRatio)
    {
        var startingPoint = institutionType.ToUpperInvariant() switch
        {
            "DMB" => 94m,
            "FC" => 91m,
            "MFB" => 88m,
            "BDC" => 86m,
            _ => 89m
        };

        var auditPenalty = auditOpinionCode.ToUpperInvariant() switch
        {
            "UNQUALIFIED" or "UNQUAL" => 0m,
            "QUALIFIED" => 12m,
            "ADVERSE" => 28m,
            "DISCLAIMER" => 34m,
            _ => 8m
        };

        var score = startingPoint
                    - (lateFilingCount * 6m)
                    - (Math.Max(0m, relatedPartyLendingRatio - 2m) * 2.5m)
                    - (Math.Max(0m, nplRatio - 4m) * 1.8m)
                    - auditPenalty;

        return Math.Round(Math.Clamp(score, 35.0m, 98.0m), 2);
    }

    private sealed record DemoInstitution(
        int Id,
        string InstitutionCode,
        string InstitutionName,
        string LicenseType,
        Guid TenantId);

    private sealed record DemoPeriod(string PeriodCode, DateOnly AsOfDate);

    private sealed record PrudentialProfile(
        decimal StartCar,
        decimal EndCar,
        decimal StartTier1Ratio,
        decimal EndTier1Ratio,
        decimal StartTotalAssets,
        decimal EndTotalAssets,
        decimal StartTotalDeposits,
        decimal EndTotalDeposits,
        decimal StartNplRatio,
        decimal EndNplRatio,
        decimal StartProvisioningCoverage,
        decimal EndProvisioningCoverage,
        decimal StartRoa,
        decimal EndRoa,
        decimal StartNim,
        decimal EndNim,
        decimal StartCir,
        decimal EndCir,
        decimal StartLcr,
        decimal EndLcr,
        decimal StartNsfr,
        decimal EndNsfr,
        decimal StartDepositConcentration,
        decimal EndDepositConcentration,
        decimal StartFxExposureRatio,
        decimal EndFxExposureRatio,
        decimal StartOilExposurePct,
        decimal EndOilExposurePct,
        decimal StartAgriExposurePct,
        decimal EndAgriExposurePct,
        decimal StartBondPortfolioAssetPct,
        decimal EndBondPortfolioAssetPct,
        decimal StartLiquidAssetsRatio,
        decimal EndLiquidAssetsRatio,
        int StartLateFilingCount,
        int EndLateFilingCount,
        decimal StartRelatedPartyLendingRatio,
        decimal EndRelatedPartyLendingRatio,
        string AuditOpinionCode);

    private sealed record DemoPrudentialMetricSeed(
        int InstitutionId,
        string RegulatorCode,
        string InstitutionType,
        DateTime AsOfDate,
        string PeriodCode,
        decimal CAR,
        decimal Tier1Ratio,
        decimal Tier2Capital,
        decimal RWA,
        decimal TotalAssets,
        decimal TotalDeposits,
        decimal NPLRatio,
        decimal GrossNPL,
        decimal GrossLoans,
        decimal ProvisioningCoverage,
        decimal OilSectorExposurePct,
        decimal AgriExposurePct,
        decimal ROA,
        decimal ROE,
        decimal NIM,
        decimal CIR,
        decimal LCR,
        decimal NSFR,
        decimal LiquidAssetsRatio,
        decimal DepositConcentration,
        decimal FXExposureRatio,
        decimal FXLoansAssetPct,
        decimal BondPortfolioAssetPct,
        decimal InterestRateSensitivity,
        decimal ComplianceScore,
        int LateFilingCount,
        string AuditOpinionCode,
        decimal RelatedPartyLendingRatio);

    private sealed record DemoInterbankExposureSeed(
        int LendingInstitutionId,
        int BorrowingInstitutionId,
        string RegulatorCode,
        string PeriodCode,
        decimal ExposureAmount,
        string ExposureType,
        DateTime AsOfDate);

    private sealed record DemoBdcTransactionSeed(
        Guid TenantId,
        int InstitutionId,
        string RegulatorCode,
        DateTime TransactionDate,
        string PeriodCode,
        string BuyCurrency,
        string SellCurrency,
        decimal BuyRate,
        decimal SellRate,
        decimal BuyVolumeUSD,
        decimal SellVolumeUSD,
        decimal CBNMidRate,
        decimal CBNBandUpper,
        decimal CBNBandLower,
        int? CounterpartyId);

    private sealed record DemoCorporateAnnouncementSeed(
        Guid TenantId,
        string RegulatorCode,
        string SecurityCode,
        string SecurityName,
        string AnnouncementType,
        DateTime AnnouncementDate,
        DateTime DisclosureDeadline,
        string SourceReference);

    private sealed record DemoCmoTradeSeed(
        Guid TenantId,
        int InstitutionId,
        string RegulatorCode,
        string InstitutionType,
        string SecurityCode,
        string SecurityName,
        DateTime TradeDate,
        string PeriodCode,
        string TradeType,
        decimal Quantity,
        decimal Price,
        decimal TradeValueNGN,
        string ClientId,
        DateTime ReportedAt,
        DateTime TradeTimestamp,
        bool IsLate);

    private sealed record DemoAmlMetricSeed(
        Guid TenantId,
        int InstitutionId,
        string RegulatorCode,
        string InstitutionType,
        string PeriodCode,
        DateTime AsOfDate,
        int STRFilingCount,
        int CTRFilingCount,
        decimal PeerAvgSTRCount,
        decimal STRDeviation,
        int StructuringAlertCount,
        int PEPAccountCount,
        int PEPFlaggedActivityCount,
        int TFSScreeningCount,
        decimal TFSFalsePositiveRate,
        int TFSTruePositiveCount,
        int CustomerComplaintCount,
        decimal ComplaintResolutionRate);

    private sealed record DemoInsuranceMetricSeed(
        Guid TenantId,
        int InstitutionId,
        string RegulatorCode,
        string InstitutionType,
        string PeriodCode,
        DateTime AsOfDate,
        decimal GrossClaimsNGN,
        decimal GrossPremiumNGN,
        decimal ClaimsRatio,
        decimal PeerAvgClaimsRatio,
        decimal GrossPremiumReported,
        decimal GrossPremiumExpected,
        decimal PremiumUnderReportingGap,
        decimal ReinsuranceRecoveries,
        decimal RelatedPartyReinsurancePct,
        int ComplaintCount,
        decimal ClaimsDenialRate);

    private sealed record DemoWhistleblowerCaseSeed(
        string CaseReference,
        string AnonymousToken,
        int? AllegedInstitutionId,
        string? AllegedInstitutionName,
        string Category,
        string Summary,
        string EvidenceDescription,
        string Status,
        int? AssignedToUserId,
        int PriorityScore,
        IReadOnlyList<DemoWhistleblowerEventSeed> Events);

    private sealed record DemoWhistleblowerEventSeed(
        string EventType,
        string Note,
        int? PerformedByUserId);

    private sealed record ImpactMetricRow(
        int InstitutionId,
        string InstitutionCode,
        string InstitutionName,
        string InstitutionType,
        decimal? CAR,
        decimal? Tier1Ratio,
        decimal? LCR,
        decimal? NSFR);
}

public sealed class EndToEndDemoSeedResult
{
    public DemoCredentialSeedResult Credentials { get; init; } = new();
    public string RegulatorCode { get; init; } = string.Empty;
    public string CurrentPeriodCode { get; init; } = string.Empty;
    public int PrudentialRowsUpserted { get; init; }
    public int InterbankExposureRowsUpserted { get; init; }
    public int StressRunsCreated { get; init; }
    public int PolicyScenariosSeeded { get; init; }
    public int WhistleblowerCasesSeeded { get; init; }
    public int ExaminationProjectsSeeded { get; init; }
    public SurveillanceRunResult? SurveillanceRun { get; init; }
}
