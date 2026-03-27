using System.Data;
using System.Text.Json;
using Dapper;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed class BDCFXSurveillance : IBDCFXSurveillance
{
    private readonly IDbConnectionFactory _db;
    private readonly IRegulatorTenantResolver _tenantResolver;
    private readonly ILogger<BDCFXSurveillance> _log;

    public BDCFXSurveillance(
        IDbConnectionFactory db,
        IRegulatorTenantResolver tenantResolver,
        ILogger<BDCFXSurveillance> log)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _log = log;
    }

    public async Task<int> DetectRateManipulationAsync(
        string regulatorCode,
        string periodCode,
        Guid runId,
        CancellationToken ct = default)
    {
        var context = await _tenantResolver.ResolveAsync(regulatorCode, ct);
        using var conn = await _db.CreateConnectionAsync(context.TenantId, ct);
        var parameters = await RuleParamLoader.LoadAsync(conn, context.TenantId, "BDC_RATE_MANIPULATION", context.RegulatorCode, "BDC");

        var consecutiveDays = (int)RuleParamLoader.Get(parameters, "ConsecutiveDaysOutsideBand", 5m);
        var tolerance = RuleParamLoader.Get(parameters, "BandTolerancePct", 1.5m);

        var violators = await conn.QueryAsync<RateViolatorRow>(
            """
            WITH OutsideBand AS (
                SELECT t.InstitutionId,
                       t.TransactionDate,
                       t.SellRate,
                       t.CBNMidRate,
                       t.CBNBandUpper * (1 + @Tolerance / 100.0) AS EffectiveUpper,
                       t.CBNBandLower * (1 - @Tolerance / 100.0) AS EffectiveLower,
                       ROW_NUMBER() OVER (PARTITION BY t.InstitutionId ORDER BY t.TransactionDate) AS RnOutside
                FROM dbo.BDCFXTransactions t
                WHERE t.TenantId = @TenantId
                  AND t.RegulatorCode = @RegulatorCode
                  AND t.PeriodCode = @PeriodCode
                  AND (
                        t.SellRate > t.CBNBandUpper * (1 + @Tolerance / 100.0)
                        OR t.SellRate < t.CBNBandLower * (1 - @Tolerance / 100.0)
                      )
            ),
            Streaks AS (
                SELECT InstitutionId,
                       MIN(TransactionDate) AS FirstBreachDate,
                       MAX(TransactionDate) AS LastBreachDate,
                       COUNT(*) AS ConsecutiveCount,
                       MAX(SellRate) AS WorstRate,
                       AVG(CBNMidRate) AS AvgMidRate,
                       MAX(EffectiveUpper) AS BandUpper,
                       MIN(EffectiveLower) AS BandLower
                FROM OutsideBand
                GROUP BY InstitutionId, DATEADD(DAY, -RnOutside, TransactionDate)
            )
            SELECT InstitutionId,
                   ConsecutiveCount AS MaxConsecutive,
                   FirstBreachDate,
                   WorstRate,
                   AvgMidRate,
                   BandUpper,
                   BandLower
            FROM Streaks
            WHERE ConsecutiveCount >= @ConsecutiveDays
            """,
            new
            {
                TenantId = context.TenantId,
                RegulatorCode = context.RegulatorCode,
                PeriodCode = periodCode,
                Tolerance = tolerance,
                ConsecutiveDays = consecutiveDays
            });

        var count = 0;
        foreach (var violator in violators)
        {
            var deviationPct = violator.AvgMidRate > 0
                ? Math.Abs((violator.WorstRate - violator.AvgMidRate) / violator.AvgMidRate * 100m)
                : 0m;

            var severity = violator.MaxConsecutive >= consecutiveDays * 2 ? "CRITICAL" : "HIGH";
            var evidence = JsonSerializer.Serialize(new BDCRateAnomalyEvidence(
                violator.WorstRate,
                violator.AvgMidRate,
                violator.BandUpper,
                violator.BandLower,
                deviationPct,
                violator.MaxConsecutive,
                DateOnly.FromDateTime(violator.FirstBreachDate)));

            await AlertWriter.WriteAsync(
                conn,
                context.TenantId,
                "BDC_RATE_MANIPULATION",
                context.RegulatorCode,
                violator.InstitutionId,
                severity,
                "BDC_FX",
                $"FX Rate Manipulation Indicator - {violator.MaxConsecutive} consecutive days outside CBN band",
                $"Observed sell rate remained outside the CBN band (including {tolerance:F1}% tolerance) for {violator.MaxConsecutive} consecutive days.",
                evidence,
                periodCode,
                runId);

            count++;
        }

        _log.LogInformation(
            "BDC rate manipulation surveillance completed. Regulator={RegulatorCode} Period={PeriodCode} Alerts={Count}",
            context.RegulatorCode,
            periodCode,
            count);

        return count;
    }

    public async Task<int> DetectVolumeSpikesAsync(
        string regulatorCode,
        string periodCode,
        Guid runId,
        CancellationToken ct = default)
    {
        var context = await _tenantResolver.ResolveAsync(regulatorCode, ct);
        using var conn = await _db.CreateConnectionAsync(context.TenantId, ct);
        var parameters = await RuleParamLoader.LoadAsync(conn, context.TenantId, "BDC_VOLUME_SPIKE", context.RegulatorCode, "BDC");

        var zThreshold = RuleParamLoader.Get(parameters, "VolumeZScoreThreshold", 3m);
        var lookback = (int)RuleParamLoader.Get(parameters, "LookbackDays", 30m);

        var spikes = await conn.QueryAsync<VolumeSpikeRow>(
            $"""
            WITH VolumeStats AS (
                SELECT t.InstitutionId,
                       t.TransactionDate,
                       t.BuyVolumeUSD + t.SellVolumeUSD AS TotalVolume,
                       AVG(CAST(t.BuyVolumeUSD + t.SellVolumeUSD AS FLOAT)) OVER (
                           PARTITION BY t.InstitutionId
                           ORDER BY t.TransactionDate
                           ROWS BETWEEN {lookback} PRECEDING AND 1 PRECEDING
                       ) AS RollingAvg,
                       STDEV(CAST(t.BuyVolumeUSD + t.SellVolumeUSD AS FLOAT)) OVER (
                           PARTITION BY t.InstitutionId
                           ORDER BY t.TransactionDate
                           ROWS BETWEEN {lookback} PRECEDING AND 1 PRECEDING
                       ) AS RollingStDev
                FROM dbo.BDCFXTransactions t
                WHERE t.TenantId = @TenantId
                  AND t.RegulatorCode = @RegulatorCode
                  AND t.PeriodCode = @PeriodCode
            )
            SELECT InstitutionId,
                   TransactionDate,
                   CAST(TotalVolume AS DECIMAL(18,2)) AS TotalVolume,
                   CAST(RollingAvg AS DECIMAL(18,2)) AS RollingAvg,
                   CAST(ISNULL(RollingStDev, 0) AS DECIMAL(18,2)) AS RollingStDev,
                   CASE
                       WHEN ISNULL(RollingStDev, 0) > 0
                       THEN (TotalVolume - RollingAvg) / RollingStDev
                       ELSE 0
                   END AS ZScore
            FROM VolumeStats
            WHERE RollingAvg IS NOT NULL
              AND ISNULL(RollingStDev, 0) > 0
              AND ((TotalVolume - RollingAvg) / RollingStDev) >= @ZThreshold
            """,
            new
            {
                TenantId = context.TenantId,
                RegulatorCode = context.RegulatorCode,
                PeriodCode = periodCode,
                ZThreshold = zThreshold
            });

        var count = 0;
        foreach (var spike in spikes)
        {
            var severity = spike.ZScore >= (double)zThreshold * 1.5 ? "HIGH" : "MEDIUM";
            var evidence = JsonSerializer.Serialize(new VolumeAnomalyEvidence(
                spike.TotalVolume,
                spike.RollingAvg,
                spike.RollingStDev,
                spike.ZScore,
                DateOnly.FromDateTime(spike.TransactionDate)));

            await AlertWriter.WriteAsync(
                conn,
                context.TenantId,
                "BDC_VOLUME_SPIKE",
                context.RegulatorCode,
                spike.InstitutionId,
                severity,
                "BDC_FX",
                $"Abnormal FX volume spike detected (Z={spike.ZScore:F2})",
                $"Daily BDC volume was {spike.ZScore:F2} standard deviations above the rolling {lookback}-day baseline.",
                evidence,
                periodCode,
                runId);

            count++;
        }

        return count;
    }

    public async Task<int> DetectWashTradingAsync(
        string regulatorCode,
        string periodCode,
        Guid runId,
        CancellationToken ct = default)
    {
        var context = await _tenantResolver.ResolveAsync(regulatorCode, ct);
        using var conn = await _db.CreateConnectionAsync(context.TenantId, ct);
        var parameters = await RuleParamLoader.LoadAsync(conn, context.TenantId, "BDC_WASH_TRADE", context.RegulatorCode, "BDC");

        var windowDays = (int)RuleParamLoader.Get(parameters, "CircularTxnWindowDays", 7m);
        var minAmountUsd = RuleParamLoader.Get(parameters, "MinCircularAmountUSD", 50_000m);

        var circulars = await conn.QueryAsync<CircularTxnRow>(
            """
            SELECT a.InstitutionId AS InstitutionA,
                   a.CounterpartyId AS InstitutionB,
                   SUM(a.BuyVolumeUSD + a.SellVolumeUSD) AS TotalAtoB,
                   SUM(b.BuyVolumeUSD + b.SellVolumeUSD) AS TotalBtoA,
                   COUNT_BIG(*) AS TxnCount,
                   MIN(CASE WHEN a.TransactionDate < b.TransactionDate THEN a.TransactionDate ELSE b.TransactionDate END) AS WindowStart,
                   MAX(CASE WHEN a.TransactionDate > b.TransactionDate THEN a.TransactionDate ELSE b.TransactionDate END) AS WindowEnd
            FROM dbo.BDCFXTransactions a
            INNER JOIN dbo.BDCFXTransactions b
                ON b.TenantId = a.TenantId
               AND b.InstitutionId = a.CounterpartyId
               AND b.CounterpartyId = a.InstitutionId
               AND DATEDIFF(DAY, a.TransactionDate, b.TransactionDate) BETWEEN 0 AND @WindowDays
            WHERE a.TenantId = @TenantId
              AND a.RegulatorCode = @RegulatorCode
              AND a.PeriodCode = @PeriodCode
              AND a.CounterpartyId IS NOT NULL
            GROUP BY a.InstitutionId, a.CounterpartyId
            HAVING SUM(a.BuyVolumeUSD + a.SellVolumeUSD) >= @MinAmountUsd
               AND SUM(b.BuyVolumeUSD + b.SellVolumeUSD) >= @MinAmountUsd
            """,
            new
            {
                TenantId = context.TenantId,
                RegulatorCode = context.RegulatorCode,
                PeriodCode = periodCode,
                WindowDays = windowDays,
                MinAmountUsd = minAmountUsd
            });

        var count = 0;
        foreach (var circular in circulars)
        {
            if (circular.InstitutionA > circular.InstitutionB)
            {
                continue;
            }

            var evidence = JsonSerializer.Serialize(new WashTradeEvidence(
                circular.InstitutionA,
                circular.InstitutionB,
                circular.TotalAtoB + circular.TotalBtoA,
                checked((int)circular.TxnCount),
                DateOnly.FromDateTime(circular.WindowStart),
                DateOnly.FromDateTime(circular.WindowEnd)));

            await AlertWriter.WriteAsync(
                conn,
                context.TenantId,
                "BDC_WASH_TRADE",
                context.RegulatorCode,
                circular.InstitutionA,
                "HIGH",
                "BDC_FX",
                $"Potential circular FX activity between institutions {circular.InstitutionA} and {circular.InstitutionB}",
                $"Circular BDC transactions totalling USD {(circular.TotalAtoB + circular.TotalBtoA):N0} were observed within a {windowDays}-day window.",
                evidence,
                periodCode,
                runId);

            count++;
        }

        return count;
    }

    private sealed record RateViolatorRow(
        int InstitutionId,
        int MaxConsecutive,
        DateTime FirstBreachDate,
        decimal WorstRate,
        decimal AvgMidRate,
        decimal BandUpper,
        decimal BandLower
    );

    private sealed record VolumeSpikeRow(
        int InstitutionId,
        DateTime TransactionDate,
        decimal TotalVolume,
        decimal RollingAvg,
        decimal RollingStDev,
        double ZScore
    );

    private sealed class CircularTxnRow
    {
        public int InstitutionA { get; set; }
        public int InstitutionB { get; set; }
        public decimal TotalAtoB { get; set; }
        public decimal TotalBtoA { get; set; }
        public long TxnCount { get; set; }
        public DateTime WindowStart { get; set; }
        public DateTime WindowEnd { get; set; }
    }
}
