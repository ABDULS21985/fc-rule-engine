using System.Data;
using Dapper;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed class StressTestOrchestrator : IStressTestOrchestrator
{
    private readonly IDbConnectionFactory _db;
    private readonly IMacroShockTransmitter _transmitter;
    private readonly IContagionCascadeEngine _contagion;
    private readonly INDICExposureCalculator _ndic;
    private readonly ILogger<StressTestOrchestrator> _log;

    public StressTestOrchestrator(
        IDbConnectionFactory db,
        IMacroShockTransmitter transmitter,
        IContagionCascadeEngine contagion,
        INDICExposureCalculator ndic,
        ILogger<StressTestOrchestrator> log)
    {
        _db = db; _transmitter = transmitter;
        _contagion = contagion; _ndic = ndic; _log = log;
    }

    public async Task<StressTestRunSummary> RunAsync(
        string regulatorCode, int scenarioId,
        string periodCode, string timeHorizon,
        int initiatedByUserId, CancellationToken ct = default)
    {
        var started = DateTimeOffset.UtcNow;
        using var conn = await _db.OpenAsync(ct);

        // Create run record (R-06: immutable run results)
        var runId = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO StressTestRuns
                (RegulatorCode, ScenarioId, PeriodCode, TimeHorizon,
                 Status, InitiatedByUserId)
            OUTPUT INSERTED.Id
            VALUES (@Regulator, @ScenarioId, @Period, @Horizon, 'RUNNING', @User)
            """,
            new { Regulator = regulatorCode, ScenarioId = scenarioId,
                  Period = periodCode, Horizon = timeHorizon, User = initiatedByUserId });

        var runGuid = await conn.ExecuteScalarAsync<Guid>(
            "SELECT RunGuid FROM StressTestRuns WHERE Id=@Id", new { Id = runId });

        _log.LogInformation(
            "Stress test run started: RunId={Id} Guid={Guid} Scenario={Scen} Period={P}",
            runId, runGuid, scenarioId, periodCode);

        try
        {
            var scenarioParams = await LoadScenarioParametersAsync(conn, scenarioId);
            var snapshots      = await LoadSnapshotsAsync(conn, regulatorCode, periodCode);

            _log.LogInformation("Loaded {Count} entity snapshots for stress test.", snapshots.Count);

            var round0Results = new List<EntityShockResult>();
            foreach (var snapshot in snapshots)
            {
                var effectiveParams = ResolveParameters(scenarioParams, snapshot.InstitutionType);
                var result = _transmitter.ApplyShock(snapshot, effectiveParams);

                if (result.BreachesCAR || result.IsInsolvent)
                {
                    var (insurable, uninsurable) = await _ndic.ComputeAsync(
                        snapshot.InstitutionId, periodCode, ct);
                    result = result with
                    {
                        InsurableDeposits   = insurable,
                        UninsurableDeposits = uninsurable
                    };
                }

                round0Results.Add(result);
            }

            var (contagionFailures, _, roundsExecuted) =
                await _contagion.CascadeAsync(round0Results, regulatorCode, periodCode, runId, ct);

            var finalResults = MergeResults(round0Results, contagionFailures);
            await PersistEntityResultsAsync(conn, runId, regulatorCode, finalResults, ct);

            var aggregates = ComputeSectorAggregates(finalResults);
            await PersistSectorAggregatesAsync(conn, runId, aggregates, ct);

            var resilience = ComputeResilienceScore(finalResults);

            var summary = await BuildSummaryAsync(
                conn, runId, runGuid, scenarioId, periodCode, timeHorizon,
                finalResults.Count, roundsExecuted, resilience, aggregates, started, ct);

            await conn.ExecuteAsync(
                """
                UPDATE StressTestRuns
                SET    Status='COMPLETED', EntitiesShocked=@Entities,
                       ContagionRounds=@Rounds, SystemicResilienceScore=@Score,
                       ExecutiveSummary=@Summary, CompletedAt=SYSUTCDATETIME()
                WHERE  Id=@Id
                """,
                new { Entities = finalResults.Count, Rounds = roundsExecuted,
                      Score = resilience, Summary = summary.ExecutiveSummary, Id = runId });

            _log.LogInformation(
                "Stress test complete: RunId={Id} Entities={E} Rounds={R} Score={S:F1}",
                runId, finalResults.Count, roundsExecuted, resilience);

            return summary;
        }
        catch (Exception ex)
        {
            await conn.ExecuteAsync(
                "UPDATE StressTestRuns SET Status='FAILED', ErrorMessage=@Err WHERE Id=@Id",
                new { Err = ex.Message[..Math.Min(2000, ex.Message.Length)], Id = runId });
            _log.LogError(ex, "Stress test run {Id} failed.", runId);
            throw;
        }
    }

    public async Task<StressTestRunSummary?> GetRunSummaryAsync(
        Guid runGuid, string? regulatorCode = null, CancellationToken ct = default)
    {
        using var conn = await _db.OpenAsync(ct);

        var run = await conn.QuerySingleOrDefaultAsync<RunRow>(
            """
            SELECT r.Id, r.RunGuid, r.ScenarioId, r.PeriodCode, r.TimeHorizon,
                   r.EntitiesShocked, r.ContagionRounds,
                   r.SystemicResilienceScore, r.ExecutiveSummary,
                   r.StartedAt, r.CompletedAt,
                   s.ScenarioCode, s.ScenarioName
            FROM   StressTestRuns r
            JOIN   StressScenarios s ON s.Id = r.ScenarioId
            WHERE  r.RunGuid = @Guid
              AND  (@RegulatorCode IS NULL OR r.RegulatorCode = @RegulatorCode)
            """,
            new { Guid = runGuid, RegulatorCode = regulatorCode });

        if (run is null) return null;

        var aggregates = (await conn.QueryAsync<SectorStressAggregate>(
            """
            SELECT InstitutionType, EntityCount,
                   PreAvgCAR, PreAvgNPL, PreAvgLCR,
                   PostAvgCAR, PostAvgNPL, PostAvgLCR,
                   EntitiesBreachingCAR, EntitiesBreachingLCR,
                   EntitiesInsolvent, EntitiesContagionVictims,
                   TotalCapitalShortfall, TotalAdditionalProvisions,
                   TotalInsurableDepositsAtRisk, TotalUninsurableDepositsAtRisk
            FROM   StressTestSectorAggregates WHERE RunId=@Id
            """,
            new { Id = run.Id })).ToList();

        var score = (double)(run.SystemicResilienceScore ?? 50m);
        var startedAt = AsUtc(run.StartedAt);
        var completedAt = run.CompletedAt.HasValue ? AsUtc(run.CompletedAt.Value) : DateTimeOffset.UtcNow;
        return new StressTestRunSummary(
            RunId: run.Id, RunGuid: run.RunGuid,
            ScenarioCode: run.ScenarioCode, ScenarioName: run.ScenarioName,
            PeriodCode: run.PeriodCode, TimeHorizon: run.TimeHorizon,
            EntitiesShocked: run.EntitiesShocked, ContagionRounds: run.ContagionRounds,
            SystemicResilienceScore: score, ResilienceRating: GetResilienceRating(score),
            BySector: aggregates,
            TotalCapitalShortfallNgn: aggregates.Sum(a => a.TotalCapitalShortfall),
            TotalNDICExposureAtRisk:  aggregates.Sum(a => a.TotalInsurableDepositsAtRisk),
            Duration: completedAt - startedAt,
            ExecutiveSummary: run.ExecutiveSummary);
    }

    public async Task<IReadOnlyList<EntityShockResult>> GetEntityResultsAsync(
        long runId, string? institutionTypeFilter, CancellationToken ct = default)
    {
        using var conn = await _db.OpenAsync(ct);
        return (await conn.QueryAsync<EntityShockResult>(
            """
            SELECT InstitutionId, InstitutionType,
                   PreCAR, PreNPL, PreLCR, PreNSFR, PreROA, PreTotalAssets, PreTotalDeposits,
                   PostCAR, PostNPL, PostLCR, PostNSFR, PostROA,
                   PostCapitalShortfall, AdditionalProvisions,
                   BreachesCAR, BreachesLCR, BreachesNSFR, IsInsolvent,
                   InsurableDeposits, UninsurableDeposits
            FROM   StressTestEntityResults
            WHERE  RunId = @RunId
              AND  (@TypeFilter IS NULL OR InstitutionType = @TypeFilter)
            ORDER BY PostCAR ASC
            """,
            new { RunId = runId, TypeFilter = institutionTypeFilter }))
            .ToList();
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static async Task<Dictionary<string, ResolvedShockParameters>>
        LoadScenarioParametersAsync(IDbConnection conn, int scenarioId)
    {
        var rows = await conn.QueryAsync<ScenarioParamRow>(
            """
            SELECT Id,
                   ScenarioId,
                   InstitutionType,
                   ISNULL(GDPGrowthShock, 0) AS GDPGrowthShock,
                   ISNULL(OilPriceShockPct, 0) AS OilPriceShockPct,
                   ISNULL(FXDepreciationPct, 0) AS FXDepreciationPct,
                   ISNULL(InflationShockPp, 0) AS InflationShockPp,
                   ISNULL(InterestRateShockBps, 0) AS InterestRateShockBps,
                   ISNULL(TradeVolumeShockPct, 0) AS TradeVolumeShockPct,
                   ISNULL(RemittanceShockPct, 0) AS RemittanceShockPct,
                   ISNULL(FDIShockPct, 0) AS FDIShockPct,
                   ISNULL(CarbonTaxUSDPerTon, 0) AS CarbonTaxUSDPerTon,
                   PhysicalRiskHazardCode,
                   ISNULL(StrandedAssetsPct, 0) AS StrandedAssetsPct,
                   ISNULL(CARDeltaPerGDPPp, 0) AS CARDeltaPerGDPPp,
                   ISNULL(NPLDeltaPerGDPPp, 0) AS NPLDeltaPerGDPPp,
                   ISNULL(LCRDeltaPerRateHike100, 0) AS LCRDeltaPerRateHike100,
                   ISNULL(CARDeltaPerFXPct, 0) AS CARDeltaPerFXPct,
                   ISNULL(NPLDeltaPerFXPct, 0) AS NPLDeltaPerFXPct,
                   ISNULL(CARDeltaPerOilPct, 0) AS CARDeltaPerOilPct,
                   ISNULL(NPLDeltaPerOilPct, 0) AS NPLDeltaPerOilPct,
                   ISNULL(LCRDeltaPerCyber, 0) AS LCRDeltaPerCyber,
                   ISNULL(DepositOutflowPctCyber, 0) AS DepositOutflowPctCyber
            FROM StressScenarioParameters
            WHERE ScenarioId = @Id
            """,
            new { Id = scenarioId });

        return rows.ToDictionary(r => r.InstitutionType, r => new ResolvedShockParameters(
            ScenarioId: scenarioId,
            InstitutionType: r.InstitutionType,
            GDPGrowthShock: r.GDPGrowthShock,
            OilPriceShockPct: r.OilPriceShockPct,
            FXDepreciationPct: r.FXDepreciationPct,
            InflationShockPp: r.InflationShockPp,
            InterestRateShockBps: r.InterestRateShockBps,
            TradeVolumeShockPct: r.TradeVolumeShockPct,
            RemittanceShockPct: r.RemittanceShockPct,
            FDIShockPct: r.FDIShockPct,
            CarbonTaxUSDPerTon: r.CarbonTaxUSDPerTon,
            StrandedAssetsPct: r.StrandedAssetsPct,
            PhysicalRiskHazardCode: r.PhysicalRiskHazardCode,
            CARDeltaPerGDPPp: r.CARDeltaPerGDPPp,
            NPLDeltaPerGDPPp: r.NPLDeltaPerGDPPp,
            LCRDeltaPerRateHike100: r.LCRDeltaPerRateHike100,
            CARDeltaPerFXPct: r.CARDeltaPerFXPct,
            NPLDeltaPerFXPct: r.NPLDeltaPerFXPct,
            CARDeltaPerOilPct: r.CARDeltaPerOilPct,
            NPLDeltaPerOilPct: r.NPLDeltaPerOilPct,
            LCRDeltaPerCyber: r.LCRDeltaPerCyber,
            DepositOutflowPctCyber: r.DepositOutflowPctCyber));
    }

    private static async Task<List<PrudentialMetricSnapshot>> LoadSnapshotsAsync(
        IDbConnection conn, string regulatorCode, string periodCode)
    {
        return (await conn.QueryAsync<PrudentialMetricSnapshot>(
            """
            SELECT pm.InstitutionId, pm.InstitutionType, pm.RegulatorCode, pm.PeriodCode,
                   ISNULL(pm.CAR,0)  AS CAR,   ISNULL(pm.NPLRatio,0) AS NPL,
                   ISNULL(pm.LCR,0)  AS LCR,   ISNULL(pm.NSFR,0)    AS NSFR,
                   ISNULL(pm.ROA,0)  AS ROA,
                   ISNULL(pm.TotalAssets,0)           AS TotalAssets,
                   ISNULL(pm.TotalDeposits,0)          AS TotalDeposits,
                   ISNULL(pm.OilSectorExposurePct,0)   AS OilSectorExposurePct,
                   ISNULL(pm.AgriExposurePct,0)        AS AgriExposurePct,
                   ISNULL(pm.FXLoansAssetPct,0)        AS FXLoansAssetPct,
                   ISNULL(pm.BondPortfolioAssetPct,0)  AS BondPortfolioAssetPct,
                   ISNULL(pm.DepositConcentration,0)   AS TopDepositorConcentration
            FROM   meta.prudential_metrics pm
            WHERE  pm.RegulatorCode = @Regulator
              AND  pm.PeriodCode = @Period
            """,
            new { Regulator = regulatorCode, Period = periodCode })).ToList();
    }

    private static ResolvedShockParameters ResolveParameters(
        Dictionary<string, ResolvedShockParameters> byType, string institutionType)
    {
        return byType.TryGetValue(institutionType, out var specific)
            ? specific
            : byType.TryGetValue("ALL", out var all)
                ? all with { InstitutionType = institutionType }
                : new ResolvedShockParameters(
                    ScenarioId: 0, InstitutionType: institutionType,
                    0m, 0m, 0m, 0m, 0,
                    0m, 0m, 0m,
                    0m, 0m, null,
                    -0.30m, 0.45m, -0.08m, -0.10m, 0.15m, -0.08m, 0.20m, 0m, 0m);
    }

    private static List<EntityShockResult> MergeResults(
        List<EntityShockResult> round0,
        IReadOnlyList<EntityShockResult> contagion)
    {
        var merged = round0.ToDictionary(r => r.InstitutionId);
        foreach (var c in contagion)
            merged[c.InstitutionId] = c;
        return merged.Values.ToList();
    }

    private static async Task PersistEntityResultsAsync(
        IDbConnection conn, long runId, string regulatorCode,
        List<EntityShockResult> results, CancellationToken ct)
    {
        foreach (var r in results)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO StressTestEntityResults
                    (RunId, InstitutionId, RegulatorCode, InstitutionType,
                     PreCAR, PreNPL, PreLCR, PreNSFR, PreROA, PreTotalAssets, PreTotalDeposits,
                     PostCAR, PostNPL, PostLCR, PostNSFR, PostROA,
                     PostCapitalShortfall, AdditionalProvisions,
                     DeltaCAR, DeltaNPL, DeltaLCR,
                     BreachesCAR, BreachesLCR, BreachesNSFR, IsInsolvent,
                     IsContagionVictim, InsurableDeposits, UninsurableDeposits)
                VALUES (@RunId, @InstId, @Regulator, @Type,
                        @PreCAR, @PreNPL, @PreLCR, @PreNSFR, @PreROA, @PreAssets, @PreDep,
                        @PostCAR, @PostNPL, @PostLCR, @PostNSFR, @PostROA,
                        @Shortfall, @Provisions,
                        @DeltaCAR, @DeltaNPL, @DeltaLCR,
                        @BrCAR, @BrLCR, @BrNSFR, @Insolv,
                        @ContagVic, @Ins, @Unins)
                """,
                new { RunId = runId, InstId = r.InstitutionId, Regulator = regulatorCode,
                      Type = r.InstitutionType,
                      PreCAR = r.PreCAR, PreNPL = r.PreNPL, PreLCR = r.PreLCR,
                      PreNSFR = r.PreNSFR, PreROA = r.PreROA,
                      PreAssets = r.PreTotalAssets, PreDep = r.PreTotalDeposits,
                      PostCAR = r.PostCAR, PostNPL = r.PostNPL, PostLCR = r.PostLCR,
                      PostNSFR = r.PostNSFR, PostROA = r.PostROA,
                      Shortfall = r.PostCapitalShortfall, Provisions = r.AdditionalProvisions,
                      DeltaCAR = r.PostCAR - r.PreCAR,
                      DeltaNPL = r.PostNPL - r.PreNPL,
                      DeltaLCR = r.PostLCR - r.PreLCR,
                      BrCAR = r.BreachesCAR, BrLCR = r.BreachesLCR,
                      BrNSFR = r.BreachesNSFR, Insolv = r.IsInsolvent,
                      ContagVic = r.IsContagionVictim,
                      Ins = r.InsurableDeposits, Unins = r.UninsurableDeposits });
        }
    }

    private static List<SectorStressAggregate> ComputeSectorAggregates(
        List<EntityShockResult> results)
    {
        return results
            .GroupBy(r => r.InstitutionType)
            .Select(g =>
            {
                var list = g.ToList();
                return new SectorStressAggregate(
                    InstitutionType: g.Key,
                    EntityCount: list.Count,
                    PreAvgCAR:  Avg(list, r => r.PreCAR),
                    PreAvgNPL:  Avg(list, r => r.PreNPL),
                    PreAvgLCR:  Avg(list, r => r.PreLCR),
                    PostAvgCAR: Avg(list, r => r.PostCAR),
                    PostAvgNPL: Avg(list, r => r.PostNPL),
                    PostAvgLCR: Avg(list, r => r.PostLCR),
                    EntitiesBreachingCAR: list.Count(r => r.BreachesCAR),
                    EntitiesBreachingLCR: list.Count(r => r.BreachesLCR),
                    EntitiesInsolvent:    list.Count(r => r.IsInsolvent),
                    EntitiesContagionVictims: list.Count(r => r.IsContagionVictim),
                    TotalCapitalShortfall:     list.Sum(r => r.PostCapitalShortfall),
                    TotalAdditionalProvisions: list.Sum(r => r.AdditionalProvisions),
                    TotalInsurableDepositsAtRisk:   list.Sum(r => r.InsurableDeposits),
                    TotalUninsurableDepositsAtRisk: list.Sum(r => r.UninsurableDeposits));
            }).ToList();
    }

    private static async Task PersistSectorAggregatesAsync(
        IDbConnection conn, long runId,
        List<SectorStressAggregate> aggregates, CancellationToken ct)
    {
        foreach (var a in aggregates)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO StressTestSectorAggregates
                    (RunId, InstitutionType, EntityCount,
                     PreAvgCAR, PreAvgNPL, PreAvgLCR,
                     PostAvgCAR, PostAvgNPL, PostAvgLCR,
                     EntitiesBreachingCAR, EntitiesBreachingLCR,
                     EntitiesInsolvent, EntitiesContagionVictims,
                     TotalCapitalShortfall, TotalAdditionalProvisions,
                     TotalInsurableDepositsAtRisk, TotalUninsurableDepositsAtRisk)
                VALUES (@RunId, @Type, @Count,
                        @PreCAR, @PreNPL, @PreLCR,
                        @PostCAR, @PostNPL, @PostLCR,
                        @BrCAR, @BrLCR, @Insolv, @ContagVic,
                        @Shortfall, @Provisions, @Ins, @Unins)
                """,
                new { RunId = runId, Type = a.InstitutionType, Count = a.EntityCount,
                      PreCAR = a.PreAvgCAR, PreNPL = a.PreAvgNPL, PreLCR = a.PreAvgLCR,
                      PostCAR = a.PostAvgCAR, PostNPL = a.PostAvgNPL, PostLCR = a.PostAvgLCR,
                      BrCAR = a.EntitiesBreachingCAR, BrLCR = a.EntitiesBreachingLCR,
                      Insolv = a.EntitiesInsolvent, ContagVic = a.EntitiesContagionVictims,
                      Shortfall = a.TotalCapitalShortfall, Provisions = a.TotalAdditionalProvisions,
                      Ins = a.TotalInsurableDepositsAtRisk,
                      Unins = a.TotalUninsurableDepositsAtRisk });
        }
    }

    private static double ComputeResilienceScore(List<EntityShockResult> results)
    {
        if (results.Count == 0) return 100.0;
        double insolventShare  = (double)results.Count(r => r.IsInsolvent)        / results.Count;
        double carBreachShare  = (double)results.Count(r => r.BreachesCAR)        / results.Count;
        double lcrBreachShare  = (double)results.Count(r => r.BreachesLCR)        / results.Count;
        double contagionShare  = (double)results.Count(r => r.IsContagionVictim)  / results.Count;
        var score = 100.0 - insolventShare * 50 - carBreachShare * 25
                          - lcrBreachShare * 15 - contagionShare * 10;
        return Math.Max(0, Math.Round(score, 1));
    }

    private async Task<StressTestRunSummary> BuildSummaryAsync(
        IDbConnection conn, long runId, Guid runGuid,
        int scenarioId, string periodCode, string timeHorizon,
        int entityCount, int contagionRounds, double resilienceScore,
        List<SectorStressAggregate> aggregates,
        DateTimeOffset started, CancellationToken ct)
    {
        var scenarioRow = await conn.QuerySingleAsync<(string Code, string Name)>(
            "SELECT ScenarioCode, ScenarioName FROM StressScenarios WHERE Id=@Id",
            new { Id = scenarioId });

        var totalShortfall = aggregates.Sum(a => a.TotalCapitalShortfall);
        var totalNdic      = aggregates.Sum(a => a.TotalInsurableDepositsAtRisk);
        var ndicCapacity   = await _ndic.GetNDICFundCapacityAsync(ct);
        var rating         = GetResilienceRating(resilienceScore);

        var execSummary = BuildExecutiveSummary(
            scenarioRow.Name, periodCode, entityCount, aggregates,
            totalShortfall, totalNdic, ndicCapacity, resilienceScore, rating, contagionRounds);

        await conn.ExecuteAsync(
            "UPDATE StressTestRuns SET ExecutiveSummary=@S WHERE Id=@Id",
            new { S = execSummary, Id = runId });

        return new StressTestRunSummary(
            RunId: runId, RunGuid: runGuid,
            ScenarioCode: scenarioRow.Code, ScenarioName: scenarioRow.Name,
            PeriodCode: periodCode, TimeHorizon: timeHorizon,
            EntitiesShocked: entityCount, ContagionRounds: contagionRounds,
            SystemicResilienceScore: resilienceScore, ResilienceRating: rating,
            BySector: aggregates,
            TotalCapitalShortfallNgn: totalShortfall,
            TotalNDICExposureAtRisk: totalNdic,
            Duration: DateTimeOffset.UtcNow - started,
            ExecutiveSummary: execSummary);
    }

    private static string BuildExecutiveSummary(
        string scenarioName, string periodCode, int entityCount,
        List<SectorStressAggregate> aggregates,
        decimal totalShortfall, decimal totalNdic, decimal ndicCapacity,
        double resilienceScore, string rating, int contagionRounds)
    {
        var totalBreachCAR = aggregates.Sum(a => a.EntitiesBreachingCAR);
        var totalInsolvent = aggregates.Sum(a => a.EntitiesInsolvent);
        var ndicCoveragePct = ndicCapacity > 0
            ? Math.Round((double)(totalNdic / ndicCapacity) * 100, 1) : 0.0;

        return $"""
The {scenarioName} stress test applied to {entityCount} supervised entities
as of {periodCode} yields a Systemic Resilience Score of {resilienceScore:F1}/100 ({rating}).

Under this scenario, {totalBreachCAR} entities ({(double)totalBreachCAR / Math.Max(1, entityCount) * 100:F1}%)
breach minimum capital requirements. {totalInsolvent} entities become technically insolvent,
requiring an aggregate capital injection of ₦{totalShortfall:N0}M to restore compliance.
Contagion propagated across {contagionRounds} rounds via interbank exposure networks.

NDIC insurable deposits at risk total ₦{totalNdic:N0}M, representing
{ndicCoveragePct:F1}% of the Deposit Insurance Fund capacity.

Key sector-level findings:
{string.Join("\n", aggregates.Select(a =>
    $"  • {a.InstitutionType}: {a.EntitiesBreachingCAR}/{a.EntityCount} breach CAR | " +
    $"PostAvgCAR {a.PostAvgCAR:F1}% vs Pre {a.PreAvgCAR:F1}% | " +
    $"₦{a.TotalCapitalShortfall:N0}M shortfall"))}

Policy recommendation: {GetPolicyRecommendation(rating, totalBreachCAR, entityCount)}
""";
    }

    private static string GetPolicyRecommendation(string rating, int breachCount, int total)
    {
        double pct = total > 0 ? (double)breachCount / total * 100 : 0;
        return rating switch
        {
            "RESILIENT" =>
                "Maintain current macro-prudential stance. Consider releasing countercyclical capital buffer.",
            "ADEQUATE" =>
                $"{pct:F0}% of entities breach CAR. Activate enhanced supervisory monitoring. " +
                "Recommend forward-looking ICAAP review for top-quintile risk entities.",
            "VULNERABLE" =>
                $"Significant stress evident ({pct:F0}% CAR breaches). " +
                "Recommend mandatory capital planning submissions. Activate early intervention thresholds.",
            _ =>
                $"Critical system-wide stress detected ({pct:F0}% CAR breaches). " +
                "Recommend emergency capital surcharge, suspension of dividends, " +
                "activation of the CBN Financial Stability Committee, and NDIC contingency planning."
        };
    }

    private static string GetResilienceRating(double score) => score switch
    {
        >= 80 => "RESILIENT",
        >= 60 => "ADEQUATE",
        >= 40 => "VULNERABLE",
        _     => "CRITICAL"
    };

    private static decimal Avg(List<EntityShockResult> list, Func<EntityShockResult, decimal> sel)
        => list.Count == 0 ? 0m : list.Average(sel);

    private static DateTimeOffset AsUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    // ── Row types ─────────────────────────────────────────────────────────────
    private sealed record RunRow(
        long Id, Guid RunGuid, int ScenarioId, string PeriodCode,
        string TimeHorizon, int EntitiesShocked, int ContagionRounds,
        decimal? SystemicResilienceScore, string? ExecutiveSummary,
        DateTime StartedAt, DateTime? CompletedAt,
        string ScenarioCode, string ScenarioName);

    private sealed record ScenarioParamRow(
        int Id, int ScenarioId, string InstitutionType,
        decimal GDPGrowthShock, decimal OilPriceShockPct,
        decimal FXDepreciationPct, decimal InflationShockPp,
        int InterestRateShockBps,
        decimal TradeVolumeShockPct, decimal RemittanceShockPct, decimal FDIShockPct,
        decimal CarbonTaxUSDPerTon, string? PhysicalRiskHazardCode,
        decimal StrandedAssetsPct,
        decimal CARDeltaPerGDPPp, decimal NPLDeltaPerGDPPp,
        decimal LCRDeltaPerRateHike100, decimal CARDeltaPerFXPct,
        decimal NPLDeltaPerFXPct, decimal CARDeltaPerOilPct,
        decimal NPLDeltaPerOilPct, decimal LCRDeltaPerCyber,
        decimal DepositOutflowPctCyber);
}
