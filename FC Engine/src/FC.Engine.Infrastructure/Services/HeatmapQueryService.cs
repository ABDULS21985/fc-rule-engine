using Dapper;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;

namespace FC.Engine.Infrastructure.Services;

/// <summary>
/// Read-model query service for the regulator portal.
/// Provides sector heatmap data, institution EWI history, and Pearson correlation matrix.
/// </summary>
public sealed class HeatmapQueryService : IHeatmapQueryService
{
    private readonly IDbConnectionFactory _db;

    public HeatmapQueryService(IDbConnectionFactory db) => _db = db;

    public async Task<IReadOnlyList<HeatmapCell>> GetSectorHeatmapAsync(
        string regulatorCode, string periodCode,
        string? institutionTypeFilter, CancellationToken ct = default)
    {
        using var conn = await _db.CreateConnectionAsync(null, ct);

        var rows = await conn.QueryAsync<HeatmapRow>(
            """
            SELECT cr.InstitutionId,
                   ISNULL(i.InstitutionName, CAST(cr.InstitutionId AS NVARCHAR)) AS InstitutionName,
                   ISNULL(pm.InstitutionType, ISNULL(i.LicenseType,'UNKNOWN')) AS InstitutionType,
                   cr.CompositeScore,
                   cr.RiskBand,
                   ISNULL(cr.TotalAssets, 0) AS TotalAssets,
                   (SELECT COUNT(*) FROM meta.ewi_triggers t
                    WHERE  t.InstitutionId = cr.InstitutionId
                      AND  t.IsActive = 1) AS ActiveEWICount,
                   CAST(CASE WHEN EXISTS (
                       SELECT 1 FROM meta.ewi_triggers t
                       WHERE  t.InstitutionId = cr.InstitutionId
                         AND  t.IsActive = 1
                         AND  t.Severity = 'CRITICAL')
                   THEN 1 ELSE 0 END AS BIT) AS HasCriticalEWI
            FROM   meta.camels_ratings cr
            LEFT JOIN institutions i ON i.Id = cr.InstitutionId
            LEFT JOIN meta.prudential_metrics pm
                   ON pm.InstitutionId = cr.InstitutionId
                  AND pm.PeriodCode = cr.PeriodCode
            WHERE  cr.RegulatorCode = @Regulator
              AND  cr.PeriodCode = @Period
              AND  (@TypeFilter IS NULL OR pm.InstitutionType = @TypeFilter)
            ORDER BY cr.CompositeScore DESC
            """,
            new { Regulator = regulatorCode, Period = periodCode,
                  TypeFilter = institutionTypeFilter });

        return rows.Select(r => new HeatmapCell(
            r.InstitutionId, r.InstitutionName, r.InstitutionType,
            (double)r.CompositeScore,
            Enum.Parse<RiskBand>(r.RiskBand, ignoreCase: true),
            r.TotalAssets, r.ActiveEWICount, r.HasCriticalEWI)).ToList();
    }

    public async Task<IReadOnlyList<EWITriggerRow>> GetInstitutionEWIHistoryAsync(
        int institutionId, string regulatorCode, int periods, CancellationToken ct = default)
    {
        using var conn = await _db.CreateConnectionAsync(null, ct);

        var rows = await conn.QueryAsync<EwiTriggerHistoryRow>(
            """
            SELECT TOP (@Periods)
                   t.Id         AS TriggerId,
                   t.EWICode,
                   d.EWIName,
                   d.CAMELSComponent,
                   t.Severity,
                   t.TriggerValue,
                   t.ThresholdValue,
                   t.TrendData  AS TrendDataJson,
                   t.IsActive,
                   t.TriggeredAt,
                   t.ClearedAt
            FROM   meta.ewi_triggers t
            JOIN   meta.ewi_definitions d ON d.EWICode = t.EWICode
            WHERE  t.InstitutionId = @InstId
              AND  t.RegulatorCode = @Regulator
            ORDER BY t.TriggeredAt DESC
            """,
            new { InstId = institutionId, Regulator = regulatorCode, Periods = periods });

        return rows.Select(row => new EWITriggerRow(
                row.TriggerId,
                row.EWICode,
                row.EWIName,
                row.CAMELSComponent,
                row.Severity,
                row.TriggerValue,
                row.ThresholdValue,
                row.TrendDataJson,
                row.IsActive,
                row.TriggeredAt,
                row.ClearedAt))
            .ToList();
    }

    public async Task<double[][]> GetCorrelationMatrixAsync(
        string regulatorCode, string institutionType,
        string periodCode, CancellationToken ct = default)
    {
        using var conn = await _db.CreateConnectionAsync(null, ct);

        var rows = await conn.QueryAsync<CorrelationRow>(
            """
            SELECT pm.InstitutionId, pm.PeriodCode,
                   ISNULL(pm.CAR, 0) AS CAR
            FROM   meta.prudential_metrics pm
            WHERE  pm.RegulatorCode = @Regulator
              AND  pm.InstitutionType = @Type
              AND  pm.AsOfDate >= DATEADD(MONTH, -24,
                       (SELECT MAX(AsOfDate) FROM meta.prudential_metrics p2
                        WHERE p2.RegulatorCode = @Regulator
                          AND p2.InstitutionType = @Type
                          AND p2.PeriodCode = @Period))
            ORDER BY pm.InstitutionId, pm.AsOfDate
            """,
            new { Regulator = regulatorCode, Type = institutionType, Period = periodCode });

        var grouped = rows
            .GroupBy(r => r.InstitutionId)
            .ToDictionary(g => g.Key,
                g => g.OrderBy(r => r.PeriodCode).Select(r => (double)r.CAR).ToArray());

        var ids = grouped.Keys.OrderBy(x => x).ToList();
        int n   = ids.Count;

        if (n == 0) return Array.Empty<double[]>();

        var matrix = new double[n][];
        for (int i = 0; i < n; i++)
        {
            matrix[i] = new double[n];
            for (int j = 0; j < n; j++)
            {
                if (i == j) { matrix[i][j] = 1.0; continue; }
                matrix[i][j] = Math.Round(
                    PearsonCorrelation(grouped[ids[i]], grouped[ids[j]]), 4);
            }
        }

        return matrix;
    }

    private static double PearsonCorrelation(double[] x, double[] y)
    {
        var n = Math.Min(x.Length, y.Length);
        if (n < 2) return 0.0;

        var meanX = x.Take(n).Average();
        var meanY = y.Take(n).Average();

        double cov = 0, varX = 0, varY = 0;
        for (int i = 0; i < n; i++)
        {
            var dx = x[i] - meanX;
            var dy = y[i] - meanY;
            cov  += dx * dy;
            varX += dx * dx;
            varY += dy * dy;
        }

        var denom = Math.Sqrt(varX * varY);
        return denom < 1e-12 ? 0.0 : cov / denom;
    }

    private sealed record HeatmapRow(
        int InstitutionId, string InstitutionName, string InstitutionType,
        decimal CompositeScore, string RiskBand, decimal TotalAssets,
        int ActiveEWICount, bool HasCriticalEWI);

    private sealed record EwiTriggerHistoryRow(
        long TriggerId,
        string EWICode,
        string EWIName,
        string CAMELSComponent,
        string Severity,
        decimal? TriggerValue,
        decimal? ThresholdValue,
        string? TrendDataJson,
        bool IsActive,
        DateTimeOffset TriggeredAt,
        DateTimeOffset? ClearedAt);

    private sealed record CorrelationRow(
        int InstitutionId, string PeriodCode, decimal CAR);
}
