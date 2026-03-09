using System.Data;
using Dapper;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

/// <summary>
/// Propagates entity failures via interbank exposures (BFS) and deposit flight.
/// Max cascade depth = 5 rounds (R-08).
/// </summary>
public sealed class ContagionCascadeEngine : IContagionCascadeEngine
{
    private const int MaxCascadeRounds = 5;
    private readonly IDbConnectionFactory _db;
    private readonly IMacroShockTransmitter _transmitter;
    private readonly ILogger<ContagionCascadeEngine> _log;

    public ContagionCascadeEngine(
        IDbConnectionFactory db,
        IMacroShockTransmitter transmitter,
        ILogger<ContagionCascadeEngine> log)
    {
        _db = db; _transmitter = transmitter; _log = log;
    }

    public async Task<(
        IReadOnlyList<EntityShockResult> AdditionalFailures,
        IReadOnlyList<ContagionEvent>    Events,
        int                              RoundsExecuted)>
        CascadeAsync(
            IReadOnlyList<EntityShockResult> round0Results,
            string regulatorCode, string periodCode,
            long runId, CancellationToken ct = default)
    {
        using var conn = await _db.OpenAsync(ct);

        // Load interbank exposure graph for this period (R-04: parameterised queries)
        var allEdges = (await conn.QueryAsync<ExposureEdge>(
            """
            SELECT ie.LendingInstitutionId, ie.BorrowingInstitutionId,
                   ie.ExposureAmount, ie.ExposureType
            FROM   InterbankExposures ie
            JOIN   Institutions i ON i.Id = ie.LendingInstitutionId
            WHERE  i.RegulatorCode = @Regulator AND ie.PeriodCode = @Period
            """,
            new { Regulator = regulatorCode, Period = periodCode })).ToList();

        // Load pre-stress snapshots for contagion shock application
        var snapshots = (await conn.QueryAsync<PrudentialMetricSnapshot>(
            """
            SELECT pm.InstitutionId, i.InstitutionType, pm.RegulatorCode, pm.PeriodCode,
                   ISNULL(pm.CAR,0)   AS CAR,   ISNULL(pm.NPLRatio,0) AS NPL,
                   ISNULL(pm.LCR,0)   AS LCR,   ISNULL(pm.NSFR,0)    AS NSFR,
                   ISNULL(pm.ROA,0)   AS ROA,
                   ISNULL(pm.TotalAssets,0)            AS TotalAssets,
                   ISNULL(pm.TotalDeposits,0)          AS TotalDeposits,
                   ISNULL(pm.OilSectorExposurePct,0)   AS OilSectorExposurePct,
                   ISNULL(pm.AgriExposurePct,0)        AS AgriExposurePct,
                   ISNULL(pm.FXLoansAssetPct,0)        AS FXLoansAssetPct,
                   ISNULL(pm.BondPortfolioAssetPct,0)  AS BondPortfolioAssetPct,
                   ISNULL(pm.DepositConcentration,0)   AS TopDepositorConcentration
            FROM   PrudentialMetrics pm
            JOIN   Institutions i ON i.Id = pm.InstitutionId
            WHERE  i.RegulatorCode = @Regulator AND pm.PeriodCode = @Period
            """,
            new { Regulator = regulatorCode, Period = periodCode }))
            .ToDictionary(s => s.InstitutionId);

        // Build lender → borrowers adjacency (for deposit-flight channel)
        var lenderGraph = allEdges
            .GroupBy(e => e.LendingInstitutionId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => (e.BorrowingInstitutionId, e.ExposureAmount, e.ExposureType)).ToList());

        // Build borrower → lenders adjacency (for interbank loss channel)
        var borrowerGraph = allEdges
            .GroupBy(e => e.BorrowingInstitutionId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => (e.LendingInstitutionId, e.ExposureAmount)).ToList());

        var failedIds = new HashSet<int>(
            round0Results
                .Where(r => r.IsInsolvent || r.BreachesCAR)
                .Select(r => r.InstitutionId));

        var allResults       = round0Results.ToDictionary(r => r.InstitutionId);
        var additionalFailed = new List<EntityShockResult>();
        var allEvents        = new List<ContagionEvent>();
        int roundsExecuted   = 0;

        for (int round = 1; round <= MaxCascadeRounds && failedIds.Count > 0; round++)
        {
            var newFailures = new HashSet<int>();
            var roundEvents = new List<ContagionEvent>();

            foreach (var failedId in failedIds)
            {
                // ── Channel 1: Interbank exposure loss ──────────────────
                if (borrowerGraph.TryGetValue(failedId, out var lenders))
                {
                    foreach (var (lenderId, amount) in lenders)
                    {
                        if (failedIds.Contains(lenderId) || newFailures.Contains(lenderId))
                            continue;

                        if (!snapshots.TryGetValue(lenderId, out var snap)) continue;

                        // 40% LGD assumption on interbank placements (R-08)
                        var loss = amount * 0.40m;
                        var estimatedRWA = snap.TotalAssets * 0.78m;
                        var carImpact = estimatedRWA > 0 ? loss / estimatedRWA * 100m : 0m;

                        var currentResult = allResults.GetValueOrDefault(lenderId);
                        var effectivePostCAR = (currentResult?.PostCAR ?? snap.CAR) - carImpact;
                        var minCAR = snap.InstitutionType == "DMB" ? 15.0m : 10.0m;

                        roundEvents.Add(new ContagionEvent(
                            ContagionRound: round,
                            FailingInstitutionId: failedId,
                            AffectedInstitutionId: lenderId,
                            ExposureAmount: amount,
                            ExposureType: "PLACEMENT",
                            TransmissionType: "INTERBANK"));

                        if (effectivePostCAR < minCAR)
                        {
                            newFailures.Add(lenderId);
                            var contagionResult = (currentResult ?? BuildResultFromSnapshot(snap)) with
                            {
                                PostCAR           = effectivePostCAR,
                                BreachesCAR       = true,
                                IsInsolvent       = effectivePostCAR < 0,
                                IsContagionVictim = true,
                                ContagionRound    = round,
                                FailureCauseCode  = "INTERBANK"
                            };
                            allResults[lenderId] = contagionResult;
                            additionalFailed.Add(contagionResult);
                        }
                    }
                }

                // ── Channel 2: Deposit flight / liquidity squeeze ────────
                if (lenderGraph.TryGetValue(failedId, out var borrowers))
                {
                    if (!snapshots.TryGetValue(failedId, out var failedSnap)) continue;
                    var depositFlightAmount =
                        failedSnap.TotalDeposits *
                        failedSnap.TopDepositorConcentration / 100m * 0.25m;

                    foreach (var (borrowerId, amount, expType) in borrowers)
                    {
                        if (failedIds.Contains(borrowerId) || newFailures.Contains(borrowerId)) continue;
                        if (!snapshots.TryGetValue(borrowerId, out var borrowerSnap)) continue;

                        var lcrImpact = borrowerSnap.TotalAssets > 0
                            ? (double)(amount / borrowerSnap.TotalAssets) * -15.0
                            : 0.0;

                        var currentResult = allResults.GetValueOrDefault(borrowerId);
                        var effectivePostLCR = (double)(currentResult?.PostLCR ?? borrowerSnap.LCR) + lcrImpact;

                        roundEvents.Add(new ContagionEvent(
                            round, failedId, borrowerId,
                            depositFlightAmount, expType, "DEPOSIT_FLIGHT"));

                        if (effectivePostLCR < 100.0)
                        {
                            var updated = (currentResult ?? BuildResultFromSnapshot(borrowerSnap))
                                with { PostLCR = (decimal)effectivePostLCR, BreachesLCR = true };
                            allResults[borrowerId] = updated;
                        }
                    }
                }
            }

            // Persist contagion events (R-04)
            foreach (var evt in roundEvents)
            {
                await conn.ExecuteAsync(
                    """
                    INSERT INTO StressTestContagionEvents
                        (RunId, ContagionRound, FailingInstitutionId, AffectedInstitutionId,
                         ExposureAmount, ExposureType, TransmissionType)
                    VALUES (@RunId, @Round, @Failing, @Affected, @Amount, @Type, @Trans)
                    """,
                    new { RunId = runId, Round = round, Failing = evt.FailingInstitutionId,
                          Affected = evt.AffectedInstitutionId, Amount = evt.ExposureAmount,
                          Type = evt.ExposureType, Trans = evt.TransmissionType });
            }

            allEvents.AddRange(roundEvents);
            roundsExecuted = round;

            if (newFailures.Count == 0) break;
            failedIds = newFailures;

            _log.LogInformation(
                "Contagion round {Round}: {New} new failures, {Events} events",
                round, newFailures.Count, roundEvents.Count);
        }

        return (additionalFailed, allEvents, roundsExecuted);
    }

    private static EntityShockResult BuildResultFromSnapshot(PrudentialMetricSnapshot s)
        => new(s.InstitutionId, s.InstitutionType,
               s.CAR, s.NPL, s.LCR, s.NSFR, s.ROA, s.TotalAssets, s.TotalDeposits,
               s.CAR, s.NPL, s.LCR, s.NSFR, s.ROA, 0m, 0m,
               false, false, false, false, 0m, 0m);

    private sealed record ExposureEdge(
        int LendingInstitutionId, int BorrowingInstitutionId,
        decimal ExposureAmount, string ExposureType);
}
