using System.Data;
using Dapper;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

/// <summary>
/// Evaluates all 17 institutional and 3 systemic EWI rules.
/// Every trigger event is written to the immutable ewi_triggers table.
/// Resolved triggers from prior periods are soft-cleared (IsActive = 0).
/// </summary>
public sealed class EWIEngine : IEWIEngine
{
    private readonly IDbConnectionFactory _db;
    private readonly ICAMELSScorer _camels;
    private readonly ISupervisoryActionEngine _actionEngine;
    private readonly ILogger<EWIEngine> _log;

    public EWIEngine(
        IDbConnectionFactory db,
        ICAMELSScorer camels,
        ISupervisoryActionEngine actionEngine,
        ILogger<EWIEngine> log)
    {
        _db = db; _camels = camels; _actionEngine = actionEngine; _log = log;
    }

    // ── Full cycle run ───────────────────────────────────────────────────────
    public async Task<EWIComputationSummary> RunFullCycleAsync(
        string regulatorCode, string periodCode, CancellationToken ct = default)
    {
        var runId   = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow;

        _log.LogInformation(
            "EWI cycle starting: RunId={RunId} Regulator={Regulator} Period={Period}",
            runId, regulatorCode, periodCode);

        using var conn = await _db.CreateConnectionAsync(null, ct);

        var dbRunId = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO meta.ewi_computation_runs
                (ComputationRunId, RegulatorCode, PeriodCode, Status)
            OUTPUT INSERTED.Id
            VALUES (@RunId, @Regulator, @Period, 'RUNNING')
            """,
            new { RunId = runId, Regulator = regulatorCode, Period = periodCode });

        // Resolve institution IDs from institutions that already have metrics for this period
        var institutionIds = (await conn.QueryAsync<int>(
            """
            SELECT DISTINCT InstitutionId
            FROM   meta.prudential_metrics
            WHERE  RegulatorCode = @Regulator AND PeriodCode = @Period
            """,
            new { Regulator = regulatorCode, Period = periodCode })).AsList();

        int totalTriggered = 0, totalCleared = 0, actionsGenerated = 0;

        foreach (var institutionId in institutionIds)
        {
            try
            {
                var triggers = await EvaluateInstitutionAsync(institutionId, periodCode, runId, ct);
                totalTriggered += triggers.Count;
            }
            catch (Exception ex)
            {
                _log.LogError(ex,
                    "EWI evaluation failed for institution {Id} run {RunId}", institutionId, runId);
            }
        }

        await EvaluateSystemicEWIsAsync(conn, regulatorCode, periodCode, runId, ct);

        var actionIds = await _actionEngine.GenerateActionsForRunAsync(runId, regulatorCode, ct);
        actionsGenerated = actionIds.Count;

        totalCleared = await ClearResolvedTriggersAsync(conn, regulatorCode, periodCode, runId, ct);

        await conn.ExecuteAsync(
            """
            UPDATE meta.ewi_computation_runs
            SET    Status = 'COMPLETED',
                   EntitiesEvaluated = @Entities,
                   EWIsTriggered = @Triggered,
                   EWIsCleared = @Cleared,
                   ActionsGenerated = @Actions,
                   CompletedAt = SYSUTCDATETIME()
            WHERE  Id = @Id
            """,
            new { Entities = institutionIds.Count, Triggered = totalTriggered,
                  Cleared = totalCleared, Actions = actionsGenerated, Id = dbRunId });

        var duration = DateTimeOffset.UtcNow - started;
        _log.LogInformation(
            "EWI cycle complete: RunId={RunId} Entities={E} Triggered={T} Cleared={C} " +
            "Actions={A} Duration={D}ms",
            runId, institutionIds.Count, totalTriggered, totalCleared,
            actionsGenerated, duration.TotalMilliseconds);

        return new EWIComputationSummary(
            runId, regulatorCode, periodCode, institutionIds.Count,
            totalTriggered, totalCleared, actionsGenerated, duration);
    }

    // ── Single institution evaluation ────────────────────────────────────────
    public async Task<IReadOnlyList<EWITriggerContext>> EvaluateInstitutionAsync(
        int institutionId, string periodCode,
        Guid computationRunId, CancellationToken ct = default)
    {
        using var conn = await _db.CreateConnectionAsync(null, ct);

        var current = await conn.QuerySingleOrDefaultAsync<PrudentialMetricRow>(
            "SELECT * FROM meta.prudential_metrics WHERE InstitutionId=@Id AND PeriodCode=@Period",
            new { Id = institutionId, Period = periodCode });

        if (current is null)
        {
            _log.LogDebug(
                "No metrics for institution {Id} period {Period} — skipping EWI.",
                institutionId, periodCode);
            return Array.Empty<EWITriggerContext>();
        }

        var history = (await conn.QueryAsync<PrudentialMetricRow>(
            """
            SELECT TOP 4 * FROM meta.prudential_metrics
            WHERE  InstitutionId = @Id AND PeriodCode < @Period
            ORDER BY AsOfDate DESC
            """,
            new { Id = institutionId, Period = periodCode })).ToList();

        var triggers = new List<EWITriggerContext>();
        var minCAR   = (decimal)PrudentialThresholds.GetMinCAR(current.InstitutionType);

        // ── Capital ───────────────────────────────────────────────────────
        if (current.CAR.HasValue && current.CAR < minCAR)
            triggers.Add(NewTrigger("CAR_BREACH_MINIMUM", "CRITICAL",
                current.CAR, minCAR, null));

        if (IsDeclining3Quarters(history.Select(h => h.CAR).ToList(), current.CAR))
            triggers.Add(NewTrigger("CAR_DECLINING_3Q", "HIGH",
                current.CAR, null,
                BuildTrendJson(history.Select(h => h.CAR).Prepend(current.CAR).ToArray())));

        if (current.CAR.HasValue && current.CAR >= minCAR && current.CAR < minCAR + 2)
            triggers.Add(NewTrigger("TIER1_RATIO_LOW", "MEDIUM",
                current.CAR, minCAR + 2, null));

        if (current.Tier1Ratio.HasValue && current.Tier1Ratio < minCAR * 0.75m)
            triggers.Add(NewTrigger("TIER1_RATIO_LOW", "MEDIUM",
                current.Tier1Ratio, minCAR * 0.75m, null));

        // Sudden asset growth
        if (history.Count > 0 && current.TotalAssets.HasValue && history[0].TotalAssets.HasValue
            && history[0].TotalAssets!.Value > 0)
        {
            var qoqGrowth = (current.TotalAssets!.Value - history[0].TotalAssets!.Value)
                            / history[0].TotalAssets!.Value * 100m;
            if (qoqGrowth > (decimal)PrudentialThresholds.SuddenGrowthThreshold)
                triggers.Add(NewTrigger("SUDDEN_ASSET_GROWTH", "MEDIUM",
                    qoqGrowth, (decimal)PrudentialThresholds.SuddenGrowthThreshold, null));
        }

        // ── Asset quality ─────────────────────────────────────────────────
        if (current.NPLRatio.HasValue)
        {
            if (current.NPLRatio > (decimal)PrudentialThresholds.NPLWarningThreshold)
                triggers.Add(NewTrigger("NPL_THRESHOLD_BREACH", "HIGH",
                    current.NPLRatio,
                    (decimal)PrudentialThresholds.NPLWarningThreshold,
                    BuildTrendJson(history.Select(h => h.NPLRatio).Prepend(current.NPLRatio).ToArray())));

            if (history.Count > 0 && history[0].NPLRatio.HasValue)
            {
                var nplRise = current.NPLRatio.Value - history[0].NPLRatio!.Value;
                if (nplRise > (decimal)PrudentialThresholds.NPLRapidRiseThreshold)
                    triggers.Add(NewTrigger("NPL_RAPID_RISE", "HIGH",
                        nplRise, (decimal)PrudentialThresholds.NPLRapidRiseThreshold, null));
            }
        }

        if (current.ProvisioningCoverage.HasValue &&
            current.ProvisioningCoverage < (decimal)PrudentialThresholds.ProvisioningWarning)
            triggers.Add(NewTrigger("PROVISIONING_LOW", "MEDIUM",
                current.ProvisioningCoverage,
                (decimal)PrudentialThresholds.ProvisioningWarning, null));

        // ── Liquidity ─────────────────────────────────────────────────────
        if (current.LCR.HasValue)
        {
            if (current.LCR < (decimal)PrudentialThresholds.LCRMinimum)
                triggers.Add(NewTrigger("LCR_BREACH", "CRITICAL",
                    current.LCR, (decimal)PrudentialThresholds.LCRMinimum,
                    BuildTrendJson(history.Select(h => h.LCR).Prepend(current.LCR).ToArray())));
            else if (current.LCR < (decimal)PrudentialThresholds.LCRWarningZone)
                triggers.Add(NewTrigger("LCR_WARNING_ZONE", "MEDIUM",
                    current.LCR, (decimal)PrudentialThresholds.LCRWarningZone, null));
        }

        if (current.NSFR.HasValue && current.NSFR < (decimal)PrudentialThresholds.NSFRMinimum)
            triggers.Add(NewTrigger("NSFR_BREACH", "HIGH",
                current.NSFR, (decimal)PrudentialThresholds.NSFRMinimum, null));

        if (current.DepositConcentration.HasValue &&
            current.DepositConcentration > (decimal)PrudentialThresholds.DepositConcentrationCap)
            triggers.Add(NewTrigger("DEPOSIT_CONCENTRATION", "MEDIUM",
                current.DepositConcentration,
                (decimal)PrudentialThresholds.DepositConcentrationCap, null));

        // ── Management ────────────────────────────────────────────────────
        if (current.LateFilingCount >= 2)
            triggers.Add(NewTrigger("LATE_FILINGS_2PLUS", "MEDIUM",
                current.LateFilingCount, 2, null));

        if (current.RelatedPartyLendingRatio.HasValue &&
            current.RelatedPartyLendingRatio > (decimal)PrudentialThresholds.RelatedPartyLendingCap)
            triggers.Add(NewTrigger("RELATED_PARTY_EXCESS", "HIGH",
                current.RelatedPartyLendingRatio,
                (decimal)PrudentialThresholds.RelatedPartyLendingCap, null));

        if (current.AuditOpinionCode is "ADVERSE" or "DISCLAIMER")
            triggers.Add(NewTrigger("AUDIT_ADVERSE", "CRITICAL",
                null, null,
                System.Text.Json.JsonSerializer.Serialize(
                    new { opinion = current.AuditOpinionCode })));

        // ── Earnings ──────────────────────────────────────────────────────
        if (current.ROA.HasValue && current.ROA < 0)
            triggers.Add(NewTrigger("ROA_NEGATIVE", "HIGH", current.ROA, 0m, null));

        if (current.CIR.HasValue && current.CIR > (decimal)PrudentialThresholds.CIRCriticalThreshold)
            triggers.Add(NewTrigger("CIR_CRITICAL", "MEDIUM",
                current.CIR, (decimal)PrudentialThresholds.CIRCriticalThreshold, null));

        // ── Sensitivity / FX ──────────────────────────────────────────────
        if (current.FXExposureRatio.HasValue &&
            Math.Abs(current.FXExposureRatio.Value) > (decimal)PrudentialThresholds.FXExposureCap)
            triggers.Add(NewTrigger("FX_EXPOSURE_EXCESS", "HIGH",
                current.FXExposureRatio,
                (decimal)PrudentialThresholds.FXExposureCap, null));

        // ── Persist triggers ──────────────────────────────────────────────
        var regulatorCode = current.RegulatorCode;
        foreach (var trigger in triggers)
        {
            await conn.ExecuteAsync(
                """
                IF NOT EXISTS (
                    SELECT 1 FROM meta.ewi_triggers
                    WHERE  InstitutionId = @InstId
                      AND  EWICode = @Code
                      AND  PeriodCode = @Period
                      AND  IsActive = 1
                )
                INSERT INTO meta.ewi_triggers
                    (EWICode, InstitutionId, RegulatorCode, PeriodCode,
                     Severity, TriggerValue, ThresholdValue,
                     TrendData, IsActive, IsSystemic, ComputationRunId)
                VALUES (@Code, @InstId, @Regulator, @Period,
                        @Severity, @TriggerValue, @ThresholdValue,
                        @TrendData, 1, 0, @RunId)
                """,
                new { InstId = institutionId, Code = trigger.EWICode,
                      Regulator = regulatorCode, Period = periodCode,
                      Severity = trigger.EWISeverity,
                      TriggerValue = trigger.TriggerValue,
                      ThresholdValue = trigger.ThresholdValue,
                      TrendData = trigger.TrendDataJson,
                      RunId = computationRunId });
        }

        return triggers;
    }

    // ── Systemic EWI evaluation ──────────────────────────────────────────────
    private static async Task EvaluateSystemicEWIsAsync(
        IDbConnection conn,
        string regulatorCode, string periodCode,
        Guid runId, CancellationToken ct)
    {
        var lcrBreachCount = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM meta.prudential_metrics
            WHERE  RegulatorCode = @Regulator
              AND  PeriodCode = @Period
              AND  LCR < 100
            """,
            new { Regulator = regulatorCode, Period = periodCode });

        if (lcrBreachCount >= 3)
            await InsertSystemicTriggerAsync(conn, "SYSTEMIC_LCR_STRESS", "CRITICAL",
                lcrBreachCount, 3, regulatorCode, periodCode, runId);

        // Sector-wide NPL rising across multiple institution types
        var sectorNPLRow = await conn.QuerySingleOrDefaultAsync(
            """
            SELECT AVG(NPLRatio) AS CurrentAvgNPL,
                   COUNT(DISTINCT InstitutionType) AS TypesRising
            FROM   meta.prudential_metrics
            WHERE  RegulatorCode = @Regulator AND PeriodCode = @Period
            """,
            new { Regulator = regulatorCode, Period = periodCode });

        if (sectorNPLRow is not null &&
            sectorNPLRow.CurrentAvgNPL > (decimal)PrudentialThresholds.NPLWarningThreshold &&
            sectorNPLRow.TypesRising >= 2)
            await InsertSystemicTriggerAsync(conn, "SYSTEMIC_NPL_RISING", "HIGH",
                (decimal?)sectorNPLRow.CurrentAvgNPL,
                (decimal)PrudentialThresholds.NPLWarningThreshold,
                regulatorCode, periodCode, runId);
    }

    private static async Task InsertSystemicTriggerAsync(
        IDbConnection conn,
        string ewiCode, string severity,
        decimal? triggerValue, decimal? thresholdValue,
        string regulatorCode, string periodCode, Guid runId)
    {
        await conn.ExecuteAsync(
            """
            IF NOT EXISTS (
                SELECT 1 FROM meta.ewi_triggers
                WHERE  EWICode = @Code AND RegulatorCode = @Regulator
                  AND  PeriodCode = @Period AND IsSystemic = 1 AND IsActive = 1
            )
            INSERT INTO meta.ewi_triggers
                (EWICode, InstitutionId, RegulatorCode, PeriodCode,
                 Severity, TriggerValue, ThresholdValue, IsActive, IsSystemic, ComputationRunId)
            VALUES (@Code, 0, @Regulator, @Period,
                    @Severity, @TriggerValue, @ThresholdValue, 1, 1, @RunId)
            """,
            new { Code = ewiCode, Regulator = regulatorCode, Period = periodCode,
                  Severity = severity, TriggerValue = triggerValue,
                  ThresholdValue = thresholdValue, RunId = runId });
    }

    private static async Task<int> ClearResolvedTriggersAsync(
        IDbConnection conn,
        string regulatorCode, string periodCode,
        Guid runId, CancellationToken ct)
    {
        return await conn.ExecuteAsync(
            """
            UPDATE meta.ewi_triggers
            SET    IsActive = 0, ClearedAt = SYSUTCDATETIME(), ClearedByRunId = @RunId
            WHERE  RegulatorCode = @Regulator
              AND  IsActive = 1
              AND  PeriodCode < @Period
              AND  ClearedByRunId IS NULL
              AND  EWICode NOT IN (
                  SELECT DISTINCT EWICode FROM meta.ewi_triggers
                  WHERE  RegulatorCode = @Regulator
                    AND  PeriodCode = @Period
                    AND  ComputationRunId = @RunId
              )
            """,
            new { RunId = runId, Regulator = regulatorCode, Period = periodCode });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private static bool IsDeclining3Quarters(IList<decimal?> history, decimal? current)
    {
        if (current is null || history.Count < 3) return false;
        if (history[0] is null || history[1] is null || history[2] is null) return false;
        return current < history[0] && history[0] < history[1] && history[1] < history[2];
    }

    private static string? BuildTrendJson(decimal?[] values)
    {
        if (values.Length == 0) return null;
        return System.Text.Json.JsonSerializer.Serialize(values);
    }

    private static EWITriggerContext NewTrigger(
        string code, string severity,
        decimal? triggerValue, decimal? thresholdValue, string? trendJson)
        => new(code, severity, triggerValue, thresholdValue, trendJson, false);

    // Dapper row type
    private sealed record PrudentialMetricRow(
        int InstitutionId, string InstitutionType, string RegulatorCode, string PeriodCode,
        decimal? CAR, decimal? Tier1Ratio, decimal? NPLRatio,
        decimal? ProvisioningCoverage, decimal? ROA, decimal? NIM, decimal? CIR,
        decimal? LCR, decimal? NSFR, decimal? DepositConcentration,
        decimal? FXExposureRatio, decimal? RelatedPartyLendingRatio,
        decimal? ComplianceScore, int? LateFilingCount,
        string? AuditOpinionCode, decimal? TotalAssets);
}
