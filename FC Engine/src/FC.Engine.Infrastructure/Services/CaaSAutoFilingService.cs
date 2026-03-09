using Azure.Security.KeyVault.Secrets;
using Cronos;
using Dapper;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

/// <summary>
/// Extract → Validate → Submit auto-filing pipeline driven by CaaSAutoFilingSchedules.
/// Credentials are retrieved at runtime from Azure Key Vault.
/// Phase transitions: EXTRACT → VALIDATE → SUBMIT → COMPLETE (or FAILED at any phase).
/// </summary>
public sealed class CaaSAutoFilingService : ICaaSAutoFilingService
{
    private readonly IDbConnectionFactory _db;
    private readonly ICoreBankingAdapterFactory _cbFactory;
    private readonly ICaaSService _caas;
    private readonly ISubmissionOrchestrator _submission;
    private readonly ICaaSWebhookDispatcher _webhook;
    private readonly SecretClient? _secretClient;
    private readonly ILogger<CaaSAutoFilingService> _log;

    public CaaSAutoFilingService(
        IDbConnectionFactory db,
        ICoreBankingAdapterFactory cbFactory,
        ICaaSService caas,
        ISubmissionOrchestrator submission,
        ICaaSWebhookDispatcher webhook,
        ILogger<CaaSAutoFilingService> log,
        SecretClient? secretClient = null)
    {
        _db           = db;
        _cbFactory    = cbFactory;
        _caas         = caas;
        _submission   = submission;
        _webhook      = webhook;
        _secretClient = secretClient;
        _log          = log;
    }

    public async Task<CaaSAutoFilingRun> ExecuteScheduleAsync(
        int scheduleId, CancellationToken ct = default)
    {
        using var conn = await _db.OpenAsync(ct);

        var schedule = await conn.QuerySingleOrDefaultAsync<AutoFilingScheduleRow>(
            """
            SELECT s.Id, s.PartnerId, s.ModuleCode, s.CoreBankingConnectionId,
                   s.AutoSubmitIfClean, s.NotifyEmails, s.CronExpression,
                   c.SystemType, c.BaseUrl, c.DatabaseServer,
                   c.CredentialSecretName, c.FieldMappingJson,
                   p.InstitutionId, p.PartnerCode, p.Tier,
                   p.AllowedModuleCodes, p.WebhookUrl
            FROM   CaaSAutoFilingSchedules s
            JOIN   CaaSCoreBankingConnections c ON c.Id = s.CoreBankingConnectionId
            JOIN   CaaSPartners p ON p.Id = s.PartnerId
            WHERE  s.Id = @ScheduleId AND s.IsActive = 1
            """,
            new { ScheduleId = scheduleId });

        if (schedule is null)
            throw new KeyNotFoundException($"Schedule {scheduleId} not found or inactive.");

        var periodCode = DerivePeriodCode(schedule.CronExpression);

        // Insert run record
        var runId = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO CaaSAutoFilingRuns
                (ScheduleId, PartnerId, ModuleCode, PeriodCode, Phase)
            OUTPUT INSERTED.Id
            VALUES (@ScheduleId, @PartnerId, @ModuleCode, @Period, 'EXTRACT')
            """,
            new { ScheduleId = scheduleId, PartnerId = schedule.PartnerId,
                  ModuleCode = schedule.ModuleCode, Period = periodCode });

        _log.LogInformation(
            "Auto-filing run started: RunId={RunId} Schedule={ScheduleId} " +
            "Module={Module} Period={Period}",
            runId, scheduleId, schedule.ModuleCode, periodCode);

        // ── Phase 1: Extract ─────────────────────────────────────────────
        CoreBankingExtractionResult extraction;
        try
        {
            if (_secretClient is null)
                throw new InvalidOperationException("KeyVault is not configured. Set KeyVault:Uri to enable auto-filing.");

            var credential = await _secretClient.GetSecretAsync(
                schedule.CredentialSecretName, cancellationToken: ct);

            var cbConfig = new CoreBankingConnectionConfig(
                SystemType:       schedule.SystemType,
                BaseUrl:          schedule.BaseUrl,
                DatabaseServer:   schedule.DatabaseServer,
                Credential:       credential.Value.Value,
                FieldMappingJson: schedule.FieldMappingJson);

            var cbSystem = Enum.Parse<CoreBankingSystem>(schedule.SystemType, ignoreCase: true);
            var adapter  = _cbFactory.GetAdapter(cbSystem);

            extraction = await adapter.ExtractReturnDataAsync(
                schedule.ModuleCode, periodCode, cbConfig, ct);

            await conn.ExecuteAsync(
                "UPDATE CaaSAutoFilingRuns SET Phase='VALIDATE' WHERE Id=@Id",
                new { Id = runId });

            await _webhook.EnqueueAsync(schedule.PartnerId,
                WebhookEventType.ExtractionCompleted,
                new { runId, schedule.ModuleCode, periodCode,
                      fieldCount    = extraction.ExtractedFields.Count,
                      unmappedCount = extraction.UnmappedFields.Count }, ct);
        }
        catch (Exception ex)
        {
            await FailRun(conn, runId, scheduleId, $"Extraction failed: {ex.Message}", ct);
            return await GetRunAsync(conn, runId);
        }

        // ── Phase 2: Validate ────────────────────────────────────────────
        var partner = new ResolvedPartner(
            PartnerId:          schedule.PartnerId,
            PartnerCode:        schedule.PartnerCode,
            InstitutionId:      schedule.InstitutionId,
            Tier:               Enum.Parse<PartnerTier>(schedule.Tier, ignoreCase: true),
            Environment:        "LIVE",
            AllowedModuleCodes: System.Text.Json.JsonSerializer
                .Deserialize<string[]>(schedule.AllowedModuleCodes ?? "[]")!);

        var requestId   = Guid.NewGuid();
        var validateReq = new CaaSValidateRequest(
            ModuleCode:     schedule.ModuleCode,
            PeriodCode:     periodCode,
            Fields:         extraction.ExtractedFields,
            PersistSession: true);

        CaaSValidateResponse validation;
        try
        {
            validation = await _caas.ValidateAsync(partner, validateReq, requestId, ct);

            await conn.ExecuteAsync(
                """
                UPDATE CaaSAutoFilingRuns
                SET    Phase = @Phase, IsClean = @IsClean
                WHERE  Id = @Id
                """,
                new { Phase   = validation.IsValid ? "SUBMIT" : "FAILED",
                      IsClean = validation.IsValid, Id = runId });
        }
        catch (Exception ex)
        {
            await FailRun(conn, runId, scheduleId, $"Validation error: {ex.Message}", ct);
            return await GetRunAsync(conn, runId);
        }

        if (!validation.IsValid)
        {
            // Hold — notify compliance officer via webhook
            await conn.ExecuteAsync(
                """
                UPDATE CaaSAutoFilingRuns
                SET    Phase='FAILED', ErrorMessage=@Error, CompletedAt=SYSUTCDATETIME()
                WHERE  Id=@Id
                """,
                new { Error = $"{validation.ErrorCount} validation error(s). " +
                              "Return held pending correction.",
                      Id = runId });

            await _webhook.EnqueueAsync(schedule.PartnerId,
                WebhookEventType.AutoFilingHeld,
                new { runId, schedule.ModuleCode, periodCode,
                      errorCount   = validation.ErrorCount,
                      errors       = validation.Errors.Take(10),
                      notifyEmails = schedule.NotifyEmails }, ct);

            _log.LogWarning(
                "Auto-filing held: RunId={RunId} Errors={Count}",
                runId, validation.ErrorCount);

            return await GetRunAsync(conn, runId);
        }

        // ── Phase 3: Submit (only if AutoSubmitIfClean = true) ───────────
        if (!schedule.AutoSubmitIfClean)
        {
            await conn.ExecuteAsync(
                """
                UPDATE CaaSAutoFilingRuns
                SET    Phase='COMPLETE', CompletedAt=SYSUTCDATETIME()
                WHERE  Id=@Id
                """,
                new { Id = runId });

            _log.LogInformation(
                "Auto-filing extracted & validated (manual submit required): RunId={RunId}",
                runId);
            return await GetRunAsync(conn, runId);
        }

        try
        {
            var submitReq = new CaaSSubmitRequest(
                SessionToken:              validation.SessionToken,
                ModuleCode:                null, PeriodCode: null, Fields: null,
                RegulatorCode:             await GetRegulatorCodeAsync(conn, schedule.ModuleCode, ct),
                SubmittedByExternalUserId: 0);  // system submission

            var submitResult = await _caas.SubmitAsync(partner, submitReq, requestId, ct);

            await conn.ExecuteAsync(
                """
                UPDATE CaaSAutoFilingRuns
                SET    Phase='COMPLETE', ReturnInstanceId=@ReturnId,
                       BatchId=@BatchId, CompletedAt=SYSUTCDATETIME()
                WHERE  Id=@Id
                """,
                new { ReturnId = submitResult.ReturnInstanceId,
                      BatchId  = submitResult.BatchId, Id = runId });

            await conn.ExecuteAsync(
                """
                UPDATE CaaSAutoFilingSchedules
                SET    LastRunAt=SYSUTCDATETIME(), LastRunStatus='SUCCESS',
                       NextRunAt=@NextRun
                WHERE  Id=@ScheduleId
                """,
                new { NextRun = ComputeNextRun(schedule.CronExpression),
                      ScheduleId = scheduleId });

            _log.LogInformation(
                "Auto-filing complete: RunId={RunId} BatchRef={BatchRef}",
                runId, submitResult.BatchReference);
        }
        catch (Exception ex)
        {
            await FailRun(conn, runId, scheduleId, $"Submission failed: {ex.Message}", ct);
        }

        return await GetRunAsync(conn, runId);
    }

    public async Task<IReadOnlyList<CaaSAutoFilingRun>> GetRunHistoryAsync(
        int partnerId, int scheduleId, int page, int pageSize, CancellationToken ct = default)
    {
        using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<CaaSAutoFilingRun>(
            """
            SELECT Id, ScheduleId, PartnerId, ModuleCode, PeriodCode,
                   Phase, ValidationSessionId, ReturnInstanceId, BatchId,
                   IsClean, ErrorMessage, StartedAt, CompletedAt
            FROM   CaaSAutoFilingRuns
            WHERE  ScheduleId = @ScheduleId AND PartnerId = @PartnerId
            ORDER BY StartedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """,
            new { ScheduleId = scheduleId, PartnerId = partnerId,
                  Offset = (page - 1) * pageSize, PageSize = pageSize });
        return rows.ToList();
    }

    // ── Helpers ──────────────────────────────────────────────────────────
    private static async Task FailRun(
        System.Data.IDbConnection conn, long runId, int scheduleId,
        string error, CancellationToken ct)
    {
        await conn.ExecuteAsync(
            """
            UPDATE CaaSAutoFilingRuns
            SET    Phase='FAILED', ErrorMessage=@Error, CompletedAt=SYSUTCDATETIME()
            WHERE  Id=@Id
            """,
            new { Error = error[..Math.Min(2000, error.Length)], Id = runId });

        await conn.ExecuteAsync(
            "UPDATE CaaSAutoFilingSchedules SET LastRunStatus='FAILED' WHERE Id=@Id",
            new { Id = scheduleId });
    }

    private static async Task<CaaSAutoFilingRun> GetRunAsync(
        System.Data.IDbConnection conn, long runId)
        => await conn.QuerySingleAsync<CaaSAutoFilingRun>(
            "SELECT * FROM CaaSAutoFilingRuns WHERE Id=@Id", new { Id = runId });

    private static async Task<string> GetRegulatorCodeAsync(
        System.Data.IDbConnection conn, string moduleCode, CancellationToken ct)
        => await conn.ExecuteScalarAsync<string>(
               "SELECT RegulatorCode FROM ReturnModules WHERE Code=@Code",
               new { Code = moduleCode })
           ?? throw new InvalidOperationException($"Module {moduleCode} not found.");

    private static string DerivePeriodCode(string cronExpression)
    {
        // Monthly cron → previous month's period code (most CBN returns are for prior month)
        var now = DateTime.UtcNow.AddMonths(-1);
        return $"{now.Year}-{now.Month:D2}";
    }

    private static DateTime ComputeNextRun(string cronExpression)
    {
        var schedule = CronExpression.Parse(cronExpression);
        return schedule.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc)
            ?? DateTime.UtcNow.AddMonths(1);
    }

    private sealed record AutoFilingScheduleRow(
        int Id, int PartnerId, string ModuleCode, int CoreBankingConnectionId,
        bool AutoSubmitIfClean, string? NotifyEmails, string CronExpression,
        string SystemType, string? BaseUrl, string? DatabaseServer,
        string CredentialSecretName, string FieldMappingJson,
        int InstitutionId, string PartnerCode, string Tier,
        string? AllowedModuleCodes, string? WebhookUrl);
}
