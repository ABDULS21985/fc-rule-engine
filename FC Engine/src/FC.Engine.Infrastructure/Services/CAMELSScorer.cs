using Dapper;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

/// <summary>
/// Scores institutions using the CBN CAMELS framework.
/// Weighting: C=20% A=20% M=15% E=20% L=15% S=10%.
/// Composite score 1.0–1.5 → Green, 1.5–2.5 → Amber, 2.5–3.5 → Red, >3.5 → Critical.
/// </summary>
public sealed class CAMELSScorer : ICAMELSScorer
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<CAMELSScorer> _log;

    public CAMELSScorer(IDbConnectionFactory db, ILogger<CAMELSScorer> log)
    {
        _db = db; _log = log;
    }

    public async Task<CAMELSResult> ScoreInstitutionAsync(
        int institutionId, string periodCode,
        Guid computationRunId, CancellationToken ct = default)
    {
        using var conn = await _db.CreateConnectionAsync(null, ct);

        var m = await conn.QuerySingleOrDefaultAsync<PrudentialMetricRow>(
            """
            SELECT InstitutionId,
                   InstitutionType,
                   PeriodCode,
                   AsOfDate,
                   CAR,
                   Tier1Ratio,
                   NPLRatio,
                   ProvisioningCoverage,
                   ROA,
                   NIM,
                   CIR,
                   LCR,
                   NSFR,
                   DepositConcentration,
                   FXExposureRatio,
                   InterestRateSensitivity,
                   RelatedPartyLendingRatio,
                   ComplianceScore,
                   LateFilingCount,
                   AuditOpinionCode,
                   TotalAssets
            FROM meta.prudential_metrics
            WHERE InstitutionId = @Id
              AND PeriodCode = @Period
            """,
            new { Id = institutionId, Period = periodCode });

        if (m is null)
            throw new InvalidOperationException(
                $"No metrics for institution {institutionId} period {periodCode}");

        var minCAR = PrudentialThresholds.GetMinCAR(m.InstitutionType);

        // ── C — Capital Adequacy ──────────────────────────────────────────
        int cScore = m.CAR switch
        {
            null => 3,
            var v when v >= (decimal)(minCAR + 5) => 1,
            var v when v >= (decimal)(minCAR + 2) => 2,
            var v when v >= (decimal)minCAR        => 3,
            var v when v >= (decimal)(minCAR - 3)  => 4,
            _                                      => 5
        };

        // ── A — Asset Quality ─────────────────────────────────────────────
        int aScore = m.NPLRatio switch
        {
            null   => 3,
            <= 2   => 1,
            <= 5   => 2,
            <= 10  => 3,
            <= 20  => 4,
            _      => 5
        };
        if (m.ProvisioningCoverage < 50 && aScore < 4) aScore++;

        // ── M — Management ────────────────────────────────────────────────
        int mScore = 2;
        if (m.LateFilingCount >= 2) mScore++;
        if (m.AuditOpinionCode is "QUALIFIED") mScore++;
        if (m.AuditOpinionCode is "ADVERSE" or "DISCLAIMER") mScore = 5;
        if (m.RelatedPartyLendingRatio > 5) mScore++;
        if (m.ComplianceScore < 60) mScore++;
        mScore = Math.Min(5, mScore);
        if (m.ComplianceScore >= 90 && mScore > 1) mScore--;

        // ── E — Earnings ──────────────────────────────────────────────────
        int eScore = m.ROA switch
        {
            null  => 3,
            > 2   => 1,
            > 1   => 2,
            > 0   => 3,
            > -1  => 4,
            _     => 5
        };
        if (m.CIR > 80 && eScore < 4) eScore++;
        if (m.NIM < 3 && eScore < 4) eScore++;

        // ── L — Liquidity ─────────────────────────────────────────────────
        int lScore = m.LCR switch
        {
            null   => 3,
            >= 150 => 1,
            >= 120 => 2,
            >= 100 => 3,
            >= 90  => 4,
            _      => 5
        };
        if (m.NSFR < 100 && lScore < 4) lScore++;
        if (m.DepositConcentration > 30 && lScore < 4) lScore++;

        // ── S — Sensitivity to Market Risk ────────────────────────────────
        int sScore = m.FXExposureRatio switch
        {
            null                          => 2,
            var v when Math.Abs(v!.Value) < 5    => 1,
            var v when Math.Abs(v!.Value) < 10   => 2,
            var v when Math.Abs(v!.Value) < 15   => 3,
            var v when Math.Abs(v!.Value) < 20   => 4,
            _                             => 5
        };
        if (m.InterestRateSensitivity > 10 && sScore < 4) sScore++;

        // ── Composite (weighted average) ──────────────────────────────────
        var composite = Math.Round(
            cScore * CamelsWeights.Capital +
            aScore * CamelsWeights.AssetQuality +
            mScore * CamelsWeights.Management +
            eScore * CamelsWeights.Earnings +
            lScore * CamelsWeights.Liquidity +
            sScore * CamelsWeights.Sensitivity, 2);

        var band = composite switch
        {
            <= 1.5 => RiskBand.Green,
            <= 2.5 => RiskBand.Amber,
            <= 3.5 => RiskBand.Red,
            _      => RiskBand.Critical
        };

        // Persist / upsert rating
        await conn.ExecuteAsync(
            """
            MERGE meta.camels_ratings AS target
            USING (VALUES (@InstId, @Period)) AS src (InstitutionId, PeriodCode)
            ON target.InstitutionId = src.InstitutionId
               AND target.PeriodCode = src.PeriodCode
            WHEN MATCHED THEN
                UPDATE SET CapitalScore=@C, AssetQualityScore=@A, ManagementScore=@M,
                           EarningsScore=@E, LiquidityScore=@L, SensitivityScore=@S,
                           CompositeScore=@Composite, RiskBand=@Band,
                           TotalAssets=@Assets, ComputationRunId=@RunId,
                           ComputedAt=SYSUTCDATETIME(),
                           RegulatorCode=(SELECT RegulatorCode FROM meta.prudential_metrics
                                          WHERE InstitutionId=@InstId AND PeriodCode=@Period),
                           AsOfDate=(SELECT AsOfDate FROM meta.prudential_metrics
                                     WHERE InstitutionId=@InstId AND PeriodCode=@Period)
            WHEN NOT MATCHED THEN
                INSERT (InstitutionId, RegulatorCode, PeriodCode, AsOfDate,
                        CapitalScore, AssetQualityScore, ManagementScore,
                        EarningsScore, LiquidityScore, SensitivityScore,
                        CompositeScore, RiskBand, TotalAssets, ComputationRunId)
                VALUES (@InstId,
                        (SELECT RegulatorCode FROM meta.prudential_metrics
                         WHERE InstitutionId=@InstId AND PeriodCode=@Period),
                        @Period,
                        (SELECT AsOfDate FROM meta.prudential_metrics
                         WHERE InstitutionId=@InstId AND PeriodCode=@Period),
                        @C, @A, @M, @E, @L, @S, @Composite, @Band, @Assets, @RunId);
            """,
            new { InstId = institutionId, Period = periodCode,
                  C = cScore, A = aScore, M = mScore,
                  E = eScore, L = lScore, S = sScore,
                  Composite = composite,
                  Band = band.ToString().ToUpperInvariant(),
                  Assets = m.TotalAssets, RunId = computationRunId });

        _log.LogDebug(
            "CAMELS scored: Institution={Id} Period={Period} Composite={Score} Band={Band}",
            institutionId, periodCode, composite, band);

        return new CAMELSResult(institutionId, periodCode,
            cScore, aScore, mScore, eScore, lScore, sScore,
            composite, band, m.TotalAssets);
    }

    public async Task<IReadOnlyList<CAMELSResult>> ScoreSectorAsync(
        string regulatorCode, string institutionType,
        string periodCode, Guid computationRunId, CancellationToken ct = default)
    {
        using var conn = await _db.CreateConnectionAsync(null, ct);

        var institutionIds = (await conn.QueryAsync<int>(
            """
            SELECT DISTINCT InstitutionId FROM meta.prudential_metrics
            WHERE  RegulatorCode = @Regulator
              AND  InstitutionType = @Type
              AND  PeriodCode = @Period
            """,
            new { Regulator = regulatorCode, Type = institutionType,
                  Period = periodCode })).ToList();

        var results = new List<CAMELSResult>();
        foreach (var id in institutionIds)
        {
            try
            {
                results.Add(await ScoreInstitutionAsync(id, periodCode, computationRunId, ct));
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "CAMELS scoring skipped for institution {Id}: {Message}",
                    id, ex.Message);
            }
        }
        return results;
    }

    private sealed class PrudentialMetricRow
    {
        public int InstitutionId { get; set; }
        public string InstitutionType { get; set; } = string.Empty;
        public string PeriodCode { get; set; } = string.Empty;
        public DateTime AsOfDate { get; set; }
        public decimal? CAR { get; set; }
        public decimal? Tier1Ratio { get; set; }
        public decimal? NPLRatio { get; set; }
        public decimal? ProvisioningCoverage { get; set; }
        public decimal? ROA { get; set; }
        public decimal? NIM { get; set; }
        public decimal? CIR { get; set; }
        public decimal? LCR { get; set; }
        public decimal? NSFR { get; set; }
        public decimal? DepositConcentration { get; set; }
        public decimal? FXExposureRatio { get; set; }
        public decimal? InterestRateSensitivity { get; set; }
        public decimal? RelatedPartyLendingRatio { get; set; }
        public decimal? ComplianceScore { get; set; }
        public int? LateFilingCount { get; set; }
        public string? AuditOpinionCode { get; set; }
        public decimal? TotalAssets { get; set; }
    }
}
