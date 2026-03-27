using Dapper;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed class ConductRiskScorer : IConductRiskScorer
{
    private readonly IDbConnectionFactory _db;
    private readonly IRegulatorTenantResolver _tenantResolver;
    private readonly ILogger<ConductRiskScorer> _log;

    private static readonly IReadOnlyDictionary<string, double> Weights = new Dictionary<string, double>
    {
        ["MarketAbuse"] = 0.30,
        ["AMLEffectiveness"] = 0.25,
        ["InsuranceConduct"] = 0.15,
        ["CustomerConduct"] = 0.15,
        ["Governance"] = 0.10,
        ["SanctionHistory"] = 0.05
    };

    public ConductRiskScorer(
        IDbConnectionFactory db,
        IRegulatorTenantResolver tenantResolver,
        ILogger<ConductRiskScorer> log)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _log = log;
    }

    public async Task<IReadOnlyList<ConductScoreComponents>> ScoreSectorAsync(
        string regulatorCode,
        string periodCode,
        Guid computationRunId,
        CancellationToken ct = default)
    {
        var context = await _tenantResolver.ResolveAsync(regulatorCode, ct);
        using var conn = await _db.CreateConnectionAsync(context.TenantId, ct);

        var institutionIds = (await conn.QueryAsync<int>(
            """
            SELECT DISTINCT InstitutionId
            FROM (
                SELECT InstitutionId
                FROM dbo.SurveillanceAlerts
                WHERE TenantId = @TenantId
                  AND RegulatorCode = @RegulatorCode
                  AND InstitutionId IS NOT NULL
                UNION
                SELECT InstitutionId
                FROM dbo.BDCFXTransactions
                WHERE TenantId = @TenantId
                  AND RegulatorCode = @RegulatorCode
                UNION
                SELECT InstitutionId
                FROM dbo.CMOTradeReports
                WHERE TenantId = @TenantId
                  AND RegulatorCode = @RegulatorCode
                UNION
                SELECT InstitutionId
                FROM dbo.AMLConductMetrics
                WHERE TenantId = @TenantId
                  AND RegulatorCode = @RegulatorCode
                UNION
                SELECT InstitutionId
                FROM dbo.InsuranceConductMetrics
                WHERE TenantId = @TenantId
                  AND RegulatorCode = @RegulatorCode
                UNION
                SELECT InstitutionId
                FROM meta.submission_items
                WHERE RegulatorCode = @RegulatorCode
            ) q
            ORDER BY InstitutionId
            """,
            new { TenantId = context.TenantId, RegulatorCode = context.RegulatorCode })).ToList();

        var results = new List<ConductScoreComponents>(institutionIds.Count);
        foreach (var institutionId in institutionIds)
        {
            var score = await ScoreInstitutionAsync(institutionId, context.RegulatorCode, periodCode, computationRunId, ct);
            results.Add(score);
        }

        _log.LogInformation(
            "Conduct risk scoring completed. Regulator={RegulatorCode} Period={PeriodCode} Entities={Count}",
            context.RegulatorCode,
            periodCode,
            results.Count);

        return results;
    }

    public async Task<ConductScoreComponents> ScoreInstitutionAsync(
        int institutionId,
        string regulatorCode,
        string periodCode,
        Guid computationRunId,
        CancellationToken ct = default)
    {
        var context = await _tenantResolver.ResolveAsync(regulatorCode, ct);
        using var conn = await _db.CreateConnectionAsync(context.TenantId, ct);

        var marketAlerts = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM dbo.SurveillanceAlerts sa
            LEFT JOIN dbo.SurveillanceAlertResolutions sr ON sr.AlertId = sa.Id
            WHERE sa.TenantId = @TenantId
              AND sa.RegulatorCode = @RegulatorCode
              AND sa.InstitutionId = @InstitutionId
              AND sa.Category IN ('BDC_FX', 'CMO')
              AND sa.Severity IN ('HIGH', 'CRITICAL')
              AND sr.AlertId IS NULL
              AND sa.DetectedAt >= DATEADD(MONTH, -6, SYSUTCDATETIME())
            """,
            new
            {
                TenantId = context.TenantId,
                RegulatorCode = context.RegulatorCode,
                InstitutionId = institutionId
            });

        var marketAbuseScore = Math.Min(100d, marketAlerts * 70d);

        var amlMetrics = await conn.QuerySingleOrDefaultAsync<AmlMetricsRow>(
            """
            SELECT STRDeviation,
                   StructuringAlertCount,
                   TFSFalsePositiveRate,
                   STRFilingCount,
                   CustomerComplaintCount,
                   ComplaintResolutionRate,
                   InstitutionType
            FROM dbo.AMLConductMetrics
            WHERE TenantId = @TenantId
              AND InstitutionId = @InstitutionId
              AND RegulatorCode = @RegulatorCode
              AND PeriodCode = @PeriodCode
            """,
            new
            {
                TenantId = context.TenantId,
                InstitutionId = institutionId,
                RegulatorCode = context.RegulatorCode,
                PeriodCode = periodCode
            });

        var amlScore = 0d;
        if (amlMetrics is not null)
        {
            if (amlMetrics.STRDeviation <= -3m)
            {
                amlScore = 80;
            }
            else if (amlMetrics.STRDeviation <= -2m)
            {
                amlScore = 50;
            }

            amlScore += Math.Min(20d, amlMetrics.StructuringAlertCount * 4d);

            if (amlMetrics.TFSFalsePositiveRate > 0.95m
                || (amlMetrics.TFSFalsePositiveRate < 0.05m && amlMetrics.STRFilingCount > 0))
            {
                amlScore += 20d;
            }

            amlScore = Math.Min(100d, amlScore);
        }

        var insuranceMetrics = await conn.QuerySingleOrDefaultAsync<InsuranceMetricsRow>(
            """
            SELECT ClaimsRatio,
                   RelatedPartyReinsurancePct,
                   PremiumUnderReportingGap,
                   GrossPremiumExpected,
                   ComplaintCount
            FROM dbo.InsuranceConductMetrics
            WHERE TenantId = @TenantId
              AND InstitutionId = @InstitutionId
              AND RegulatorCode = @RegulatorCode
              AND PeriodCode = @PeriodCode
            """,
            new
            {
                TenantId = context.TenantId,
                InstitutionId = institutionId,
                RegulatorCode = context.RegulatorCode,
                PeriodCode = periodCode
            });

        var insuranceScore = 0d;
        if (insuranceMetrics is not null)
        {
            if (insuranceMetrics.ClaimsRatio < 20m)
            {
                insuranceScore += 60d;
            }
            else if (insuranceMetrics.ClaimsRatio < 30m)
            {
                insuranceScore += 30d;
            }
            else if (insuranceMetrics.ClaimsRatio < 40m)
            {
                insuranceScore += 10d;
            }

            if (insuranceMetrics.RelatedPartyReinsurancePct > 50m)
            {
                insuranceScore += 40d;
            }
            else if (insuranceMetrics.RelatedPartyReinsurancePct > 30m)
            {
                insuranceScore += 20d;
            }

            if (insuranceMetrics.GrossPremiumExpected > 0)
            {
                var gapPct = insuranceMetrics.PremiumUnderReportingGap / insuranceMetrics.GrossPremiumExpected * 100m;
                if (gapPct > 30m)
                {
                    insuranceScore += 30d;
                }
                else if (gapPct > 15m)
                {
                    insuranceScore += 15d;
                }
            }

            insuranceScore = Math.Min(100d, insuranceScore);
        }

        var customerComplaints = (amlMetrics?.CustomerComplaintCount ?? 0) + (insuranceMetrics?.ComplaintCount ?? 0);
        var customerConductScore = Math.Min(100d, customerComplaints * 5d);

        var lateFilings = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM meta.submission_items
            WHERE InstitutionId = @InstitutionId
              AND RegulatorCode = @RegulatorCode
              AND Status = 'OVERDUE'
              AND CreatedAt >= DATEADD(MONTH, -12, SYSUTCDATETIME())
            """,
            new { InstitutionId = institutionId, RegulatorCode = context.RegulatorCode });

        var governanceScore = Math.Min(100d, lateFilings * 20d);

        var sanctionCount = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM meta.supervisory_actions
            WHERE InstitutionId = @InstitutionId
              AND RegulatorCode = @RegulatorCode
              AND ActionType IN ('WARNING_LETTER', 'ENFORCEMENT_ORDER', 'MONETARY_PENALTY')
              AND CreatedAt >= DATEADD(MONTH, -24, SYSUTCDATETIME())
            """,
            new { InstitutionId = institutionId, RegulatorCode = context.RegulatorCode });

        var sanctionScore = Math.Min(100d, sanctionCount * 30d);

        var composite = Math.Round(
            marketAbuseScore * Weights["MarketAbuse"]
            + amlScore * Weights["AMLEffectiveness"]
            + insuranceScore * Weights["InsuranceConduct"]
            + customerConductScore * Weights["CustomerConduct"]
            + governanceScore * Weights["Governance"]
            + sanctionScore * Weights["SanctionHistory"],
            2);

        var band = composite switch
        {
            >= 70d => ConductRiskBand.Critical,
            >= 45d => ConductRiskBand.High,
            >= 20d => ConductRiskBand.Medium,
            _ => ConductRiskBand.Low
        };

        var activeAlerts = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM dbo.SurveillanceAlerts sa
            LEFT JOIN dbo.SurveillanceAlertResolutions sr ON sr.AlertId = sa.Id
            WHERE sa.TenantId = @TenantId
              AND sa.RegulatorCode = @RegulatorCode
              AND sa.InstitutionId = @InstitutionId
              AND sr.AlertId IS NULL
            """,
            new
            {
                TenantId = context.TenantId,
                RegulatorCode = context.RegulatorCode,
                InstitutionId = institutionId
            });

        var version = await conn.ExecuteScalarAsync<int>(
            """
            SELECT ISNULL(MAX(ScoreVersion), 0) + 1
            FROM dbo.ConductRiskScores
            WHERE TenantId = @TenantId
              AND InstitutionId = @InstitutionId
              AND RegulatorCode = @RegulatorCode
              AND PeriodCode = @PeriodCode
            """,
            new
            {
                TenantId = context.TenantId,
                InstitutionId = institutionId,
                RegulatorCode = context.RegulatorCode,
                PeriodCode = periodCode
            });

        var institutionType = amlMetrics?.InstitutionType
            ?? await conn.ExecuteScalarAsync<string?>(
                """
                SELECT TOP 1 InstitutionType
                FROM (
                    SELECT InstitutionType FROM dbo.CMOTradeReports WHERE TenantId = @TenantId AND InstitutionId = @InstitutionId
                    UNION ALL
                    SELECT InstitutionType FROM dbo.AMLConductMetrics WHERE TenantId = @TenantId AND InstitutionId = @InstitutionId
                    UNION ALL
                    SELECT InstitutionType FROM dbo.InsuranceConductMetrics WHERE TenantId = @TenantId AND InstitutionId = @InstitutionId
                ) q
                """,
                new { TenantId = context.TenantId, InstitutionId = institutionId })
            ?? await conn.ExecuteScalarAsync<string?>(
                "SELECT NULLIF(LicenseType, '') FROM dbo.institutions WHERE Id = @InstitutionId",
                new { InstitutionId = institutionId })
            ?? "ALL";

        await conn.ExecuteAsync(
            """
            INSERT INTO dbo.ConductRiskScores
                (TenantId, InstitutionId, RegulatorCode, InstitutionType, PeriodCode, ScoreVersion,
                 MarketAbuseScore, AMLEffectivenessScore, InsuranceConductScore,
                 CustomerConductScore, GovernanceScore, SanctionHistoryScore,
                 CompositeScore, RiskBand, ActiveAlertCount, ComputationRunId)
            VALUES
                (@TenantId, @InstitutionId, @RegulatorCode, @InstitutionType, @PeriodCode, @ScoreVersion,
                 @MarketAbuseScore, @AMLEffectivenessScore, @InsuranceConductScore,
                 @CustomerConductScore, @GovernanceScore, @SanctionHistoryScore,
                 @CompositeScore, @RiskBand, @ActiveAlertCount, @ComputationRunId)
            """,
            new
            {
                TenantId = context.TenantId,
                InstitutionId = institutionId,
                RegulatorCode = context.RegulatorCode,
                InstitutionType = institutionType,
                PeriodCode = periodCode,
                ScoreVersion = version,
                MarketAbuseScore = marketAbuseScore,
                AMLEffectivenessScore = amlScore,
                InsuranceConductScore = insuranceScore,
                CustomerConductScore = customerConductScore,
                GovernanceScore = governanceScore,
                SanctionHistoryScore = sanctionScore,
                CompositeScore = composite,
                RiskBand = band.ToString().ToUpperInvariant(),
                ActiveAlertCount = activeAlerts,
                ComputationRunId = computationRunId
            });

        return new ConductScoreComponents(
            institutionId,
            periodCode,
            marketAbuseScore,
            amlScore,
            insuranceScore,
            customerConductScore,
            governanceScore,
            sanctionScore,
            composite,
            band,
            activeAlerts);
    }

    private sealed record AmlMetricsRow(
        decimal? STRDeviation,
        int StructuringAlertCount,
        decimal? TFSFalsePositiveRate,
        int STRFilingCount,
        int CustomerComplaintCount,
        decimal? ComplaintResolutionRate,
        string InstitutionType
    );

    private sealed record InsuranceMetricsRow(
        decimal? ClaimsRatio,
        decimal? RelatedPartyReinsurancePct,
        decimal? PremiumUnderReportingGap,
        decimal? GrossPremiumExpected,
        int ComplaintCount
    );
}
