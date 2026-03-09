using Dapper;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

/// <summary>
/// Aggregates sector-wide prudential metrics into systemic risk indicators per institution type.
/// Systemic risk score (0–100) is a weighted sum of entity breach fractions.
/// </summary>
public sealed class SystemicRiskAggregatorService : ISystemicRiskAggregator
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<SystemicRiskAggregatorService> _log;

    public SystemicRiskAggregatorService(IDbConnectionFactory db, ILogger<SystemicRiskAggregatorService> log)
    {
        _db = db; _log = log;
    }

    public async Task<SystemicRiskIndicators> AggregateAsync(
        string regulatorCode, string institutionType,
        string periodCode, Guid computationRunId, CancellationToken ct = default)
    {
        using var conn = await _db.CreateConnectionAsync(null, ct);

        var metrics = (await conn.QueryAsync<SectorMetricRow>(
            """
            SELECT pm.CAR, pm.NPLRatio, pm.LCR, pm.ROA, pm.TotalAssets,
                   cr.RiskBand
            FROM   meta.prudential_metrics pm
            LEFT JOIN meta.camels_ratings cr
                   ON cr.InstitutionId = pm.InstitutionId
                  AND cr.PeriodCode = pm.PeriodCode
            WHERE  pm.RegulatorCode = @Regulator
              AND  pm.InstitutionType = @Type
              AND  pm.PeriodCode = @Period
            """,
            new { Regulator = regulatorCode, Type = institutionType, Period = periodCode }))
            .ToList();

        if (metrics.Count == 0)
        {
            _log.LogWarning(
                "No metrics for systemic aggregation: {Regulator}/{Type}/{Period}",
                regulatorCode, institutionType, periodCode);
            return BuildEmptyIndicators(regulatorCode, institutionType, periodCode, computationRunId);
        }

        var minCAR   = (decimal)PrudentialThresholds.GetMinCAR(institutionType);

        var avgCAR = AverageOrNull(metrics.Where(m => m.CAR.HasValue).Select(m => m.CAR!.Value));
        var avgNPL = AverageOrNull(metrics.Where(m => m.NPLRatio.HasValue).Select(m => m.NPLRatio!.Value));
        var avgLCR = AverageOrNull(metrics.Where(m => m.LCR.HasValue).Select(m => m.LCR!.Value));
        var avgROA = AverageOrNull(metrics.Where(m => m.ROA.HasValue).Select(m => m.ROA!.Value));

        int breachCAR = metrics.Count(m => m.CAR.HasValue && m.CAR < minCAR);
        int breachNPL = metrics.Count(m => m.NPLRatio.HasValue &&
                                           m.NPLRatio > (decimal)PrudentialThresholds.NPLWarningThreshold);
        int breachLCR = metrics.Count(m => m.LCR.HasValue &&
                                           m.LCR < (decimal)PrudentialThresholds.LCRMinimum);
        int highRisk  = metrics.Count(m => m.RiskBand is "RED" or "CRITICAL");

        var aggInterbank = await conn.ExecuteScalarAsync<decimal?>(
            """
            SELECT SUM(ExposureAmount)
            FROM   meta.interbank_exposures
            WHERE  RegulatorCode = @Regulator AND PeriodCode = @Period
            """,
            new { Regulator = regulatorCode, Period = periodCode });

        double systemicScore = 0;
        systemicScore += (double)breachCAR / metrics.Count * 35;
        systemicScore += (double)breachNPL / metrics.Count * 25;
        systemicScore += (double)breachLCR / metrics.Count * 25;
        systemicScore += (double)highRisk  / metrics.Count * 15;
        systemicScore  = Math.Min(100, Math.Round(systemicScore * 100, 1));

        var band = systemicScore switch
        {
            < 20  => "LOW",
            < 40  => "MODERATE",
            < 70  => "HIGH",
            _     => "SEVERE"
        };

        var asOfDate = await conn.ExecuteScalarAsync<DateTime>(
            """
            SELECT MAX(AsOfDate) FROM meta.prudential_metrics
            WHERE  RegulatorCode = @Regulator
              AND  InstitutionType = @Type
              AND  PeriodCode = @Period
            """,
            new { Regulator = regulatorCode, Type = institutionType, Period = periodCode });

        await conn.ExecuteAsync(
            """
            MERGE meta.systemic_risk_indicators AS t
            USING (VALUES (@Regulator, @Type, @Period))
                AS s (RegulatorCode, InstitutionType, PeriodCode)
            ON t.RegulatorCode = s.RegulatorCode
               AND t.InstitutionType = s.InstitutionType
               AND t.PeriodCode = s.PeriodCode
            WHEN MATCHED THEN
                UPDATE SET EntityCount=@Count, SectorAvgCAR=@CAR, SectorAvgNPL=@NPL,
                           SectorAvgLCR=@LCR, SectorAvgROA=@ROA,
                           EntitiesBreachingCAR=@BrCAR, EntitiesBreachingNPL=@BrNPL,
                           EntitiesBreachingLCR=@BrLCR, HighRiskEntityCount=@HighRisk,
                           SystemicRiskScore=@Score, SystemicRiskBand=@Band,
                           AggregateInterbankExposure=@Interbank,
                           ComputationRunId=@RunId, ComputedAt=SYSUTCDATETIME(),
                           AsOfDate=@AsOf
            WHEN NOT MATCHED THEN
                INSERT (RegulatorCode, InstitutionType, PeriodCode, AsOfDate,
                        EntityCount, SectorAvgCAR, SectorAvgNPL, SectorAvgLCR, SectorAvgROA,
                        EntitiesBreachingCAR, EntitiesBreachingNPL, EntitiesBreachingLCR,
                        HighRiskEntityCount, SystemicRiskScore, SystemicRiskBand,
                        AggregateInterbankExposure, ComputationRunId)
                VALUES (@Regulator, @Type, @Period, @AsOf, @Count,
                        @CAR, @NPL, @LCR, @ROA, @BrCAR, @BrNPL, @BrLCR,
                        @HighRisk, @Score, @Band, @Interbank, @RunId);
            """,
            new { Regulator = regulatorCode, Type = institutionType, Period = periodCode,
                  AsOf = asOfDate, Count = metrics.Count,
                  CAR = avgCAR, NPL = avgNPL, LCR = avgLCR, ROA = avgROA,
                  BrCAR = breachCAR, BrNPL = breachNPL, BrLCR = breachLCR,
                  HighRisk = highRisk, Score = (decimal)systemicScore, Band = band,
                  Interbank = aggInterbank, RunId = computationRunId });

        return new SystemicRiskIndicators
        {
            RegulatorCode = regulatorCode, InstitutionType = institutionType,
            PeriodCode = periodCode, EntityCount = metrics.Count,
            SectorAvgCAR = avgCAR, SectorAvgNPL = avgNPL,
            SectorAvgLCR = avgLCR, SectorAvgROA = avgROA,
            EntitiesBreachingCAR = breachCAR, EntitiesBreachingNPL = breachNPL,
            EntitiesBreachingLCR = breachLCR, HighRiskEntityCount = highRisk,
            SystemicRiskScore = (decimal)systemicScore, SystemicRiskBand = band,
            AggregateInterbankExposure = aggInterbank,
            ComputationRunId = computationRunId, ComputedAt = DateTimeOffset.UtcNow
        };
    }

    private static SystemicRiskIndicators BuildEmptyIndicators(
        string regulatorCode, string institutionType,
        string periodCode, Guid runId) => new()
    {
        RegulatorCode = regulatorCode, InstitutionType = institutionType,
        PeriodCode = periodCode, EntityCount = 0,
        SystemicRiskScore = 0, SystemicRiskBand = "LOW",
        ComputationRunId = runId, ComputedAt = DateTimeOffset.UtcNow
    };

    private static decimal? AverageOrNull(IEnumerable<decimal> source)
    {
        var list = source.ToList();
        return list.Count == 0 ? null : list.Average();
    }

    private sealed record SectorMetricRow(
        decimal? CAR, decimal? NPLRatio, decimal? LCR,
        decimal? ROA, decimal? TotalAssets, string? RiskBand);
}
