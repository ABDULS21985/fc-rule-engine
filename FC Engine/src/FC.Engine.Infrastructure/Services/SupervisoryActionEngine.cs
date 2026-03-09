using System.Data;
using Dapper;
using FC.Engine.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

/// <summary>
/// Generates, issues, escalates, and closes supervisory regulatory actions.
/// Every state change is written to the immutable supervisory_action_audit_log.
/// Letter content follows the CBN Banking Supervision Department template.
/// </summary>
public sealed class SupervisoryActionEngine : ISupervisoryActionEngine
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<SupervisoryActionEngine> _log;

    private static readonly Dictionary<string, int> _defaultEscalationLevel = new()
    {
        ["LOW"]      = 1,
        ["MEDIUM"]   = 1,
        ["HIGH"]     = 2,
        ["CRITICAL"] = 3
    };

    private static readonly string[] _escalationTitles =
    {
        "",
        "Analyst",
        "Senior Examiner",
        "Director, Supervision",
        "Governor, CBN"
    };

    public SupervisoryActionEngine(IDbConnectionFactory db, ILogger<SupervisoryActionEngine> log)
    {
        _db = db; _log = log;
    }

    // ── Auto-generate actions for a computation run ──────────────────────────
    public async Task<IReadOnlyList<long>> GenerateActionsForRunAsync(
        Guid computationRunId, string regulatorCode, CancellationToken ct = default)
    {
        using var conn = await _db.CreateConnectionAsync(null, ct);

        var triggers = (await conn.QueryAsync<NewTriggerRow>(
            """
            SELECT t.Id         AS TriggerId,
                   t.InstitutionId,
                   t.EWICode,
                   t.Severity,
                   t.PeriodCode,
                   t.RegulatorCode,
                   t.IsSystemic,
                   t.TriggerValue,
                   t.ThresholdValue,
                   d.EWIName,
                   d.RemediationGuidance,
                   i.InstitutionName
            FROM   meta.ewi_triggers t
            JOIN   meta.ewi_definitions d ON d.EWICode = t.EWICode
            LEFT JOIN institutions i ON i.Id = t.InstitutionId
            WHERE  t.ComputationRunId = @RunId
              AND  t.IsActive = 1
              AND  t.Severity IN ('HIGH','CRITICAL')
              AND  NOT EXISTS (
                  SELECT 1 FROM meta.supervisory_actions sa
                  WHERE  sa.EWITriggerId = t.Id
                    AND  sa.Status NOT IN ('CLOSED')
              )
            """,
            new { RunId = computationRunId })).ToList();

        var actionIds = new List<long>();

        foreach (var trigger in triggers)
        {
            var actionType      = trigger.Severity == "CRITICAL" ? "WARNING_LETTER" : "ADVISORY_LETTER";
            var escalationLevel = _defaultEscalationLevel.GetValueOrDefault(trigger.Severity, 1);
            var title           = $"{trigger.Severity} Alert: {trigger.EWIName} — " +
                                  $"{trigger.InstitutionName ?? "Sector-Wide"} ({trigger.PeriodCode})";

            var actionId = await conn.ExecuteScalarAsync<long>(
                """
                INSERT INTO meta.supervisory_actions
                    (InstitutionId, RegulatorCode, EWITriggerId, ActionType,
                     Severity, Title, Status, EscalationLevel, DueDate)
                OUTPUT INSERTED.Id
                VALUES (@InstId, @Regulator, @TriggerId, @ActionType,
                        @Severity, @Title, 'DRAFT', @EscLevel,
                        DATEADD(DAY, @DueDays, CAST(SYSUTCDATETIME() AS DATE)))
                """,
                new { InstId = trigger.InstitutionId, Regulator = trigger.RegulatorCode,
                      TriggerId = trigger.TriggerId, ActionType = actionType,
                      Severity = trigger.Severity, Title = title,
                      EscLevel = escalationLevel,
                      DueDays = trigger.Severity == "CRITICAL" ? 7 : 30 });

            var letter = BuildLetterContent(trigger, actionType);
            await conn.ExecuteAsync(
                "UPDATE meta.supervisory_actions SET LetterContent=@Letter WHERE Id=@Id",
                new { Letter = letter, Id = actionId });

            await WriteAuditAsync(conn, actionId, trigger.InstitutionId,
                trigger.RegulatorCode, "ACTION_CREATED",
                new { trigger.EWICode, trigger.Severity, actionType }, userId: null);

            actionIds.Add(actionId);

            _log.LogInformation(
                "Supervisory action created: ActionId={Id} EWI={EWI} Institution={Inst} " +
                "Severity={Sev} EscLevel={Esc}",
                actionId, trigger.EWICode, trigger.InstitutionName,
                trigger.Severity, escalationLevel);
        }

        return actionIds;
    }

    // ── Generate letter content ──────────────────────────────────────────────
    public async Task<string> GenerateLetterContentAsync(
        long supervisoryActionId, CancellationToken ct = default)
    {
        using var conn = await _db.CreateConnectionAsync(null, ct);

        var row = await conn.QuerySingleOrDefaultAsync<ActionDetailRow>(
            """
            SELECT sa.Id, sa.InstitutionId, sa.RegulatorCode, sa.EWITriggerId,
                   sa.ActionType, sa.Severity, sa.Title,
                   t.EWICode, t.TriggerValue, t.ThresholdValue, t.PeriodCode,
                   d.EWIName, d.RemediationGuidance,
                   i.InstitutionName,
                   ISNULL(i.LicenseType,'') AS InstitutionType
            FROM   meta.supervisory_actions sa
            JOIN   meta.ewi_triggers t ON t.Id = sa.EWITriggerId
            JOIN   meta.ewi_definitions d ON d.EWICode = t.EWICode
            LEFT JOIN institutions i ON i.Id = sa.InstitutionId
            WHERE  sa.Id = @Id
            """,
            new { Id = supervisoryActionId });

        if (row is null)
            throw new KeyNotFoundException($"Supervisory action {supervisoryActionId} not found.");

        var trigger = new NewTriggerRow(
            TriggerId: row.EWITriggerId, InstitutionId: row.InstitutionId,
            EWICode: row.EWICode, Severity: row.Severity, PeriodCode: row.PeriodCode,
            RegulatorCode: row.RegulatorCode, IsSystemic: false,
            EWIName: row.EWIName, RemediationGuidance: row.RemediationGuidance,
            InstitutionName: row.InstitutionName,
            TriggerValue: row.TriggerValue, ThresholdValue: row.ThresholdValue);

        var letter = BuildLetterContent(trigger, row.ActionType);

        await conn.ExecuteAsync(
            "UPDATE meta.supervisory_actions SET LetterContent=@Letter, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id",
            new { Letter = letter, Id = supervisoryActionId });

        return letter;
    }

    // ── Issue action ─────────────────────────────────────────────────────────
    public async Task IssueActionAsync(
        long supervisoryActionId, int issuedByUserId, CancellationToken ct = default)
    {
        using var conn = await _db.CreateConnectionAsync(null, ct);

        var affected = await conn.ExecuteAsync(
            """
            UPDATE meta.supervisory_actions
            SET    Status = 'ISSUED', IssuedAt = SYSUTCDATETIME(),
                   IssuedByUserId = @UserId, UpdatedAt = SYSUTCDATETIME()
            WHERE  Id = @Id AND Status = 'DRAFT'
            """,
            new { Id = supervisoryActionId, UserId = issuedByUserId });

        if (affected == 0)
            throw new InvalidOperationException(
                $"Action {supervisoryActionId} cannot be issued (not in DRAFT status).");

        var (instId, regCode) = await GetActionContextAsync(conn, supervisoryActionId);
        await WriteAuditAsync(conn, supervisoryActionId, instId, regCode,
            "LETTER_ISSUED", new { issuedByUserId }, issuedByUserId);

        _log.LogInformation("Supervisory action issued: ActionId={Id} IssuedBy={User}",
            supervisoryActionId, issuedByUserId);
    }

    // ── Escalate action ──────────────────────────────────────────────────────
    public async Task EscalateActionAsync(
        long supervisoryActionId, int escalatedByUserId,
        string reason, CancellationToken ct = default)
    {
        using var conn = await _db.CreateConnectionAsync(null, ct);

        var current = await conn.QuerySingleOrDefaultAsync<EscalationRow>(
            "SELECT EscalationLevel, InstitutionId, RegulatorCode FROM meta.supervisory_actions WHERE Id=@Id",
            new { Id = supervisoryActionId });

        if (current is null)
            throw new KeyNotFoundException($"Action {supervisoryActionId} not found.");

        if (current.EscalationLevel >= 4)
            throw new InvalidOperationException("Action is already escalated to Governor level.");

        var newLevel = current.EscalationLevel + 1;

        await conn.ExecuteAsync(
            """
            UPDATE meta.supervisory_actions
            SET    EscalationLevel = @Level, Status = 'ESCALATED', UpdatedAt = SYSUTCDATETIME()
            WHERE  Id = @Id
            """,
            new { Level = newLevel, Id = supervisoryActionId });

        await WriteAuditAsync(conn, supervisoryActionId,
            current.InstitutionId, current.RegulatorCode, "ESCALATED",
            new { from = current.EscalationLevel, to = newLevel,
                  escalatedTo = _escalationTitles[newLevel], reason },
            escalatedByUserId);

        _log.LogWarning("Action escalated: ActionId={Id} Level={Level} ({Title})",
            supervisoryActionId, newLevel, _escalationTitles[newLevel]);
    }

    // ── Record remediation update ────────────────────────────────────────────
    public async Task RecordRemediationUpdateAsync(
        long supervisoryActionId, string updateJson,
        int updatedByUserId, CancellationToken ct = default)
    {
        using var conn = await _db.CreateConnectionAsync(null, ct);

        await conn.ExecuteAsync(
            """
            UPDATE meta.supervisory_actions
            SET    Status = 'IN_REMEDIATION', RemediationPlanJson = @Plan,
                   UpdatedAt = SYSUTCDATETIME()
            WHERE  Id = @Id
            """,
            new { Plan = updateJson, Id = supervisoryActionId });

        var (instId, regCode) = await GetActionContextAsync(conn, supervisoryActionId);
        await WriteAuditAsync(conn, supervisoryActionId, instId, regCode,
            "REMEDIATION_UPDATED", updateJson, updatedByUserId);
    }

    // ── Close action ─────────────────────────────────────────────────────────
    public async Task CloseActionAsync(
        long supervisoryActionId, int closedByUserId,
        string closureReason, CancellationToken ct = default)
    {
        using var conn = await _db.CreateConnectionAsync(null, ct);

        await conn.ExecuteAsync(
            "UPDATE meta.supervisory_actions SET Status='CLOSED', UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id",
            new { Id = supervisoryActionId });

        var (instId, regCode) = await GetActionContextAsync(conn, supervisoryActionId);
        await WriteAuditAsync(conn, supervisoryActionId, instId, regCode,
            "CLOSED", new { closedByUserId, closureReason }, closedByUserId);

        _log.LogInformation("Supervisory action closed: ActionId={Id} ClosedBy={User}",
            supervisoryActionId, closedByUserId);
    }

    // ── Letter builder ────────────────────────────────────────────────────────
    private static string BuildLetterContent(NewTriggerRow trigger, string actionType)
    {
        var today         = DateOnly.FromDateTime(DateTime.UtcNow);
        var refNo         = $"CBN/BSD/{today.Year}/{trigger.EWICode}/{trigger.TriggerId}";
        var entityLine    = trigger.IsSystemic
            ? "ALL DEPOSIT MONEY BANKS AND FINANCIAL INSTITUTIONS"
            : trigger.InstitutionName?.ToUpperInvariant() ?? "THE BOARD AND MANAGEMENT";
        var letterHeading = actionType == "WARNING_LETTER"
            ? "NOTICE OF REGULATORY CONCERN"
            : "ADVISORY NOTICE";

        var metricLine = trigger.TriggerValue.HasValue && trigger.ThresholdValue.HasValue
            ? $"The reported value of {trigger.TriggerValue:F2}% has breached the regulatory " +
              $"threshold of {trigger.ThresholdValue:F2}%."
            : $"The Early Warning Indicator '{trigger.EWIName}' has been triggered.";

        return $"""
CENTRAL BANK OF NIGERIA
BANKING SUPERVISION DEPARTMENT

{refNo}                                               {today:dd MMMM yyyy}

{entityLine}
RE: {letterHeading} — {trigger.EWIName.ToUpperInvariant()} ({trigger.PeriodCode})

Dear Sir/Madam,

1. BACKGROUND

The Central Bank of Nigeria (CBN) has, pursuant to its mandate under the Banks and Other Financial Institutions Act (BOFIA) 2020, conducted a review of prudential returns submitted for the period {trigger.PeriodCode}. This review has identified the following supervisory concern warranting your immediate attention.

2. INDICATOR TRIGGERED

Early Warning Indicator: {trigger.EWIName}
Severity Classification: {trigger.Severity}
Reporting Period: {trigger.PeriodCode}

{metricLine}

This indicator signals a material deviation from expected prudential standards and requires prompt remedial action to protect the stability of your institution and the broader financial system.

3. REGULATORY EXPECTATIONS

The CBN expects your institution to:

(a) Acknowledge receipt of this notice within five (5) working days of the date hereof;
(b) Submit a detailed Root Cause Analysis and Management Response within fourteen (14) calendar days;
(c) Implement the remediation actions outlined below and provide a structured Remediation Action Plan (RAP) with quarterly milestones;
(d) Assign a designated Board-level sponsor for oversight of the remediation process.

4. REQUIRED REMEDIATION ACTIONS

{trigger.RemediationGuidance ?? "Submit a comprehensive remediation plan addressing the identified risk area within the timelines stated above."}

5. ESCALATION NOTICE

Please be advised that failure to comply with the requirements of this notice within the stipulated timeframes shall result in escalation of this matter to the Director of Banking Supervision and may attract the imposition of regulatory sanctions as provided for under BOFIA 2020 and CBN's Regulation on the Scope of Banking Activities and Ancillary Matters.

6. CONTACT

All correspondence in response to this notice should be addressed to:

Director, Banking Supervision Department
Central Bank of Nigeria
33 Tafawa Balewa Way, Central Business District
Abuja, Federal Capital Territory

and copied electronically to your designated supervisory examiner at the CBN.

Yours faithfully,

_______________________________
DIRECTOR, BANKING SUPERVISION
For: GOVERNOR, CENTRAL BANK OF NIGERIA

cc: Board Chairman
    External Auditors (for information)
    File
""";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static async Task WriteAuditAsync(
        IDbConnection conn,
        long actionId, int institutionId, string regulatorCode,
        string eventType, object? detail, int? userId)
    {
        await conn.ExecuteAsync(
            """
            INSERT INTO meta.supervisory_action_audit_log
                (SupervisoryActionId, InstitutionId, RegulatorCode, EventType, Detail, PerformedByUserId)
            VALUES (@ActionId, @InstId, @Regulator, @EventType, @Detail, @UserId)
            """,
            new { ActionId = actionId, InstId = institutionId, Regulator = regulatorCode,
                  EventType = eventType,
                  Detail = detail is string s ? s
                      : System.Text.Json.JsonSerializer.Serialize(detail),
                  UserId = userId });
    }

    private static async Task<(int InstitutionId, string RegulatorCode)>
        GetActionContextAsync(IDbConnection conn, long actionId)
    {
        var row = await conn.QuerySingleAsync<(int, string)>(
            "SELECT InstitutionId, RegulatorCode FROM meta.supervisory_actions WHERE Id=@Id",
            new { Id = actionId });
        return row;
    }

    // Row types
    private sealed record NewTriggerRow(
        long TriggerId, int InstitutionId, string EWICode, string Severity,
        string PeriodCode, string RegulatorCode, bool IsSystemic,
        string EWIName, string? RemediationGuidance, string? InstitutionName,
        decimal? TriggerValue = null, decimal? ThresholdValue = null);

    private sealed record ActionDetailRow(
        long Id, int InstitutionId, string RegulatorCode, long EWITriggerId,
        string ActionType, string Severity, string Title,
        string EWICode, decimal? TriggerValue, decimal? ThresholdValue,
        string PeriodCode, string EWIName, string? RemediationGuidance,
        string? InstitutionName, string? InstitutionType);

    private sealed record EscalationRow(
        int EscalationLevel, int InstitutionId, string RegulatorCode);
}
