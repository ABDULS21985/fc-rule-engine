using Dapper;
using FC.Engine.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using System.Data;

namespace FC.Engine.Infrastructure.Services;

/// <summary>
/// Compliance-as-a-Service orchestrator — implements all CaaS API endpoints.
/// Partner isolation is enforced by filtering every query by PartnerId.
/// </summary>
public sealed class CaaSService : ICaaSService
{
    private readonly IDbConnectionFactory _db;
    private readonly IValidationPipeline _validation;
    private readonly ITemplateEngine _templateEngine;
    private readonly ISubmissionOrchestrator _submission;
    private readonly ICaaSWebhookDispatcher _webhook;
    private readonly ILogger<CaaSService> _log;

    public CaaSService(
        IDbConnectionFactory db,
        IValidationPipeline validation,
        ITemplateEngine templateEngine,
        ISubmissionOrchestrator submission,
        ICaaSWebhookDispatcher webhook,
        ILogger<CaaSService> log)
    {
        _db = db;
        _validation = validation;
        _templateEngine = templateEngine;
        _submission = submission;
        _webhook = webhook;
        _log = log;
    }

    // ── Validate ─────────────────────────────────────────────────────────────
    public async Task<CaaSValidateResponse> ValidateAsync(
        ResolvedPartner partner,
        CaaSValidateRequest request,
        Guid requestId,
        CancellationToken ct = default)
    {
        EnsureModuleAllowed(partner, request.ModuleCode);

        var report = await _validation.ValidateAsync(
            partner.InstitutionId, request.ModuleCode,
            request.PeriodCode, request.Fields, ct);

        var errors = report.Violations
            .Where(v => v.Severity == "ERROR")
            .Select(v => new CaaSFieldError(
                v.FieldCode, v.FieldLabel, v.ErrorCode, v.Message, "ERROR"))
            .ToList();

        var warnings = report.Violations
            .Where(v => v.Severity == "WARNING")
            .Select(v => new CaaSFieldError(
                v.FieldCode, v.FieldLabel, v.ErrorCode, v.Message, "WARNING"))
            .ToList();

        var score = ComputeComplianceScore(errors.Count, warnings.Count, report.TotalFields);

        string? sessionToken = null;
        if (request.PersistSession)
        {
            sessionToken = await CreateValidationSessionAsync(
                partner.PartnerId, request, report, errors.Count == 0, ct);
        }

        if (errors.Count > 0)
        {
            await _webhook.EnqueueAsync(partner.PartnerId,
                WebhookEventType.ValidationFailed,
                new { requestId, request.ModuleCode, request.PeriodCode, errorCount = errors.Count }, ct);
        }

        _log.LogInformation(
            "CaaS validate: Partner={Partner} Module={Module} Period={Period} " +
            "IsValid={IsValid} Errors={Errors} RequestId={RequestId}",
            partner.PartnerCode, request.ModuleCode, request.PeriodCode,
            errors.Count == 0, errors.Count, requestId);

        return new CaaSValidateResponse(
            IsValid: errors.Count == 0,
            SessionToken: sessionToken,
            ErrorCount: errors.Count,
            WarningCount: warnings.Count,
            Errors: errors,
            Warnings: warnings,
            ComplianceScore: score,
            RequestId: requestId);
    }

    // ── Submit ───────────────────────────────────────────────────────────────
    public async Task<CaaSSubmitResponse> SubmitAsync(
        ResolvedPartner partner,
        CaaSSubmitRequest request,
        Guid requestId,
        CancellationToken ct = default)
    {
        Dictionary<string, object?> fields;
        string moduleCode;
        string periodCode;

        if (request.SessionToken is not null)
        {
            var session = await GetValidSessionAsync(
                partner.PartnerId, request.SessionToken, ct);

            if (session is null)
                return SubmitFail(requestId, "Session token is invalid or expired.");

            if (session.IsValid != true)
                return SubmitFail(requestId, "Session has validation errors — cannot submit.");

            fields     = System.Text.Json.JsonSerializer
                .Deserialize<Dictionary<string, object?>>(session.SubmittedData)!;
            moduleCode = session.ModuleCode;
            periodCode = session.PeriodCode;
        }
        else
        {
            if (request.ModuleCode is null || request.PeriodCode is null || request.Fields is null)
                return SubmitFail(requestId,
                    "ModuleCode, PeriodCode, and Fields are required when no SessionToken is provided.");

            var validateReq = new CaaSValidateRequest(
                request.ModuleCode, request.PeriodCode, request.Fields, PersistSession: false);
            var validation = await ValidateAsync(partner, validateReq, requestId, ct);

            if (!validation.IsValid)
                return SubmitFail(requestId,
                    $"Validation failed with {validation.ErrorCount} error(s). " +
                    "Use /validate to review errors before submitting.");

            fields     = request.Fields;
            moduleCode = request.ModuleCode;
            periodCode = request.PeriodCode;
        }

        EnsureModuleAllowed(partner, moduleCode);

        var returnInstanceId = await CreateReturnInstanceAsync(
            partner.InstitutionId, moduleCode, periodCode, fields,
            request.SubmittedByExternalUserId, ct);

        var submissionResult = await _submission.SubmitBatchAsync(
            partner.InstitutionId,
            request.RegulatorCode,
            new[] { (int)returnInstanceId },
            request.SubmittedByExternalUserId,
            ct);

        if (request.SessionToken is not null)
        {
            await MarkSessionConvertedAsync(
                partner.PartnerId, request.SessionToken, returnInstanceId, ct);
        }

        if (submissionResult.Success)
        {
            await _webhook.EnqueueAsync(partner.PartnerId,
                WebhookEventType.FilingCompleted,
                new
                {
                    requestId, moduleCode, periodCode,
                    batchReference = submissionResult.BatchReference,
                    receiptReference = submissionResult.Receipt?.ReceiptReference,
                    returnInstanceId
                }, ct);

            _log.LogInformation(
                "CaaS submit: Partner={Partner} Module={Module} Period={Period} " +
                "BatchRef={BatchRef} RequestId={RequestId}",
                partner.PartnerCode, moduleCode, periodCode,
                submissionResult.BatchReference, requestId);
        }

        return submissionResult.Success
            ? new CaaSSubmitResponse(
                Success: true,
                ReturnInstanceId: returnInstanceId,
                BatchId: submissionResult.BatchId,
                BatchReference: submissionResult.BatchReference,
                ReceiptReference: submissionResult.Receipt?.ReceiptReference,
                ErrorMessage: null,
                RequestId: requestId)
            : SubmitFail(requestId, submissionResult.ErrorMessage ?? "Submission failed.");
    }

    // ── GetTemplate ──────────────────────────────────────────────────────────
    public async Task<CaaSTemplateResponse> GetTemplateAsync(
        ResolvedPartner partner,
        string moduleCode,
        Guid requestId,
        CancellationToken ct = default)
    {
        EnsureModuleAllowed(partner, moduleCode);

        var template = await _templateEngine.GetTemplateAsync(
            partner.InstitutionId, moduleCode, ct);

        var fields = template.Fields.Select(f => new CaaSFieldDefinition(
            FieldCode: f.Code,
            FieldLabel: f.Label,
            DataType: f.DataType,
            IsRequired: f.IsRequired,
            ValidationRule: f.ValidationRule,
            MinValue: f.MinValue,
            MaxValue: f.MaxValue,
            Description: f.Description)).ToList();

        var formulas = template.Formulas.Select(f => new CaaSFormula(
            FormulaCode: f.Code,
            Description: f.Description,
            Expression: f.Expression)).ToList();

        return new CaaSTemplateResponse(
            ModuleCode: moduleCode,
            ModuleName: template.ModuleName,
            RegulatorCode: template.RegulatorCode,
            PeriodType: template.PeriodType,
            Fields: fields,
            Formulas: formulas,
            RequestId: requestId);
    }

    // ── GetDeadlines ─────────────────────────────────────────────────────────
    public async Task<CaaSDeadlinesResponse> GetDeadlinesAsync(
        ResolvedPartner partner,
        Guid requestId,
        CancellationToken ct = default)
    {
        using var conn = await OpenConnectionAsync(ct);

        var rows = await conn.QueryAsync<FilingDeadlineRow>(
            """
            SELECT fd.ModuleCode, m.ModuleName, fd.PeriodCode,
                   fd.DeadlineDate, m.RegulatorCode
            FROM   FilingDeadlines fd
            JOIN   ReturnModules m ON m.Code = fd.ModuleCode
            WHERE  fd.DeadlineDate >= CAST(SYSUTCDATETIME() AS DATE)
              AND  fd.DeadlineDate <= DATEADD(DAY, 90, CAST(SYSUTCDATETIME() AS DATE))
              AND  fd.ModuleCode IN (
                       SELECT value FROM OPENJSON(
                           (SELECT AllowedModuleCodes FROM CaaSPartners WHERE Id = @PartnerId)))
            ORDER BY fd.DeadlineDate ASC
            """,
            new { PartnerId = partner.PartnerId });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var deadlines = rows.Select(r =>
        {
            var dl = DateOnly.FromDateTime(r.DeadlineDate);
            return new CaaSDeadline(
                ModuleCode: r.ModuleCode,
                ModuleName: r.ModuleName,
                PeriodCode: r.PeriodCode,
                DeadlineDate: dl,
                DaysRemaining: dl.DayNumber - today.DayNumber,
                IsOverdue: dl < today,
                RegulatorCode: r.RegulatorCode);
        }).ToList();

        var approaching = deadlines.Where(d => d.DaysRemaining is >= 0 and <= 7).ToList();
        if (approaching.Count > 0)
            await _webhook.EnqueueAsync(partner.PartnerId,
                WebhookEventType.DeadlineApproaching,
                new
                {
                    deadlines = approaching.Select(d => new
                        { d.ModuleCode, d.PeriodCode, d.DeadlineDate, d.DaysRemaining })
                }, ct);

        return new CaaSDeadlinesResponse(deadlines, requestId);
    }

    // ── GetScore ─────────────────────────────────────────────────────────────
    public async Task<CaaSScoreResponse> GetScoreAsync(
        ResolvedPartner partner,
        CaaSScoreRequest request,
        Guid requestId,
        CancellationToken ct = default)
    {
        using var conn = await OpenConnectionAsync(ct);

        var periodFilter = request.PeriodCode ?? GetCurrentPeriodCode();

        var rows = await conn.QueryAsync<ModuleScoreRow>(
            """
            SELECT m.Code              AS ModuleCode,
                   m.ModuleName,
                   COUNT(ri.Id)        AS TotalReturns,
                   SUM(CASE WHEN ri.Status = 'APPROVED' THEN 1 ELSE 0 END) AS Approved,
                   SUM(CASE WHEN ri.Status = 'OVERDUE'  THEN 1 ELSE 0 END) AS Overdue,
                   SUM(CASE WHEN ri.Status = 'PENDING'
                         OR ri.Status = 'DRAFT'          THEN 1 ELSE 0 END) AS Pending,
                   ISNULL(SUM(ve.ErrorCount), 0)                            AS ValidationErrors
            FROM   ReturnModules m
            LEFT JOIN ReturnInstances ri
                   ON ri.ModuleCode = m.Code
                  AND ri.InstitutionId = @InstitutionId
                  AND ri.ReportingPeriod = @Period
            LEFT JOIN (
                SELECT ReturnInstanceId, COUNT(*) AS ErrorCount
                FROM   ValidationResults
                WHERE  Severity = 'ERROR'
                GROUP BY ReturnInstanceId
            ) ve ON ve.ReturnInstanceId = ri.Id
            WHERE  m.Code IN (
                       SELECT value FROM OPENJSON(
                           (SELECT AllowedModuleCodes FROM CaaSPartners WHERE Id = @PartnerId)))
            GROUP BY m.Code, m.ModuleName
            """,
            new { InstitutionId = partner.InstitutionId,
                  Period = periodFilter, PartnerId = partner.PartnerId });

        var moduleScores = rows.Select(r =>
        {
            var score = r.TotalReturns == 0
                ? 100.0
                : Math.Max(0, 100.0 - (r.Overdue * 20) - (r.ValidationErrors * 2) - (r.Pending * 5));
            return new CaaSModuleScore(
                r.ModuleCode, r.ModuleName, Math.Round(score, 1),
                r.Pending, r.Overdue, r.ValidationErrors);
        }).ToList();

        var overall = moduleScores.Count == 0
            ? 100.0
            : Math.Round(moduleScores.Average(s => s.Score), 1);

        var rating = overall switch
        {
            >= 95 => "EXCELLENT",
            >= 80 => "GOOD",
            >= 65 => "SATISFACTORY",
            >= 50 => "NEEDS_ATTENTION",
            _     => "CRITICAL"
        };

        await _webhook.EnqueueAsync(partner.PartnerId, WebhookEventType.ScoreUpdated,
            new { requestId, overall, rating, period = periodFilter }, ct);

        return new CaaSScoreResponse(overall, rating, moduleScores, requestId);
    }

    // ── GetChanges ───────────────────────────────────────────────────────────
    public async Task<CaaSChangesResponse> GetChangesAsync(
        ResolvedPartner partner,
        Guid requestId,
        CancellationToken ct = default)
    {
        using var conn = await OpenConnectionAsync(ct);

        var rows = await conn.QueryAsync<RegulatoryChangeRow>(
            """
            SELECT rc.Id           AS ChangeId,
                   rc.RegulatorCode,
                   rc.ModuleCode,
                   rc.Title,
                   rc.Summary,
                   rc.EffectiveDate,
                   rc.Severity
            FROM   RegulatoryChanges rc
            WHERE  rc.EffectiveDate >= DATEADD(DAY, -30, CAST(SYSUTCDATETIME() AS DATE))
              AND  rc.IsPublished = 1
              AND  rc.ModuleCode IN (
                       SELECT value FROM OPENJSON(
                           (SELECT AllowedModuleCodes FROM CaaSPartners WHERE Id = @PartnerId)))
            ORDER BY rc.EffectiveDate DESC
            """,
            new { PartnerId = partner.PartnerId });

        var changes = rows.Select(r => new CaaSRegulatoryChange(
            ChangeId: r.ChangeId,
            RegulatorCode: r.RegulatorCode,
            ModuleCode: r.ModuleCode,
            Title: r.Title,
            Summary: r.Summary,
            EffectiveDate: DateOnly.FromDateTime(r.EffectiveDate),
            Severity: r.Severity)).ToList();

        if (changes.Any(c => c.Severity == "MAJOR"))
            await _webhook.EnqueueAsync(partner.PartnerId, WebhookEventType.ChangesDetected,
                new
                {
                    requestId,
                    majorCount = changes.Count(c => c.Severity == "MAJOR"),
                    changes = changes.Where(c => c.Severity == "MAJOR").Take(5)
                }, ct);

        return new CaaSChangesResponse(changes, requestId);
    }

    // ── Simulate ─────────────────────────────────────────────────────────────
    public async Task<CaaSSimulateResponse> SimulateAsync(
        ResolvedPartner partner,
        CaaSSimulateRequest request,
        Guid requestId,
        CancellationToken ct = default)
    {
        EnsureModuleAllowed(partner, request.ModuleCode);

        var results = new List<CaaSScenarioResult>();

        foreach (var scenario in request.Scenarios)
        {
            var scenarioFields = new Dictionary<string, object?>(request.Fields);
            foreach (var (key, value) in scenario.FieldOverrides)
                scenarioFields[key] = value;

            var report = await _validation.ValidateAsync(
                partner.InstitutionId, request.ModuleCode,
                request.PeriodCode, scenarioFields, ct);

            var errors = report.Violations
                .Where(v => v.Severity == "ERROR")
                .Select(v => new CaaSFieldError(
                    v.FieldCode, v.FieldLabel, v.ErrorCode, v.Message, "ERROR"))
                .ToList();

            var score = ComputeComplianceScore(
                errors.Count,
                report.Violations.Count(v => v.Severity == "WARNING"),
                report.TotalFields);

            results.Add(new CaaSScenarioResult(
                ScenarioName: scenario.ScenarioName,
                IsValid: errors.Count == 0,
                ComplianceScore: score,
                Errors: errors,
                ComputedValues: report.ComputedValues));
        }

        return new CaaSSimulateResponse(results, requestId);
    }

    // ── Private helpers ───────────────────────────────────────────────────────
    private static void EnsureModuleAllowed(ResolvedPartner partner, string moduleCode)
    {
        if (!partner.AllowedModuleCodes.Contains(moduleCode, StringComparer.OrdinalIgnoreCase))
            throw new CaaSModuleNotEntitledException(
                $"Partner '{partner.PartnerCode}' is not entitled to module '{moduleCode}'.");
    }

    private async Task<string> CreateValidationSessionAsync(
        int partnerId,
        CaaSValidateRequest request,
        CaaSValidationReport report,
        bool isValid,
        CancellationToken ct)
    {
        var token = Convert.ToHexString(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .ToLowerInvariant();

        using var conn = await OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO CaaSValidationSessions
                (PartnerId, SessionToken, ModuleCode, PeriodCode,
                 SubmittedData, ValidationResult, IsValid, ExpiresAt)
            VALUES (@PartnerId, @Token, @ModuleCode, @PeriodCode,
                    @Data, @Result, @IsValid,
                    DATEADD(HOUR, 24, SYSUTCDATETIME()))
            """,
            new
            {
                PartnerId = partnerId,
                Token = token,
                ModuleCode = request.ModuleCode,
                PeriodCode = request.PeriodCode,
                Data   = System.Text.Json.JsonSerializer.Serialize(request.Fields),
                Result = System.Text.Json.JsonSerializer.Serialize(report),
                IsValid = isValid
            });

        return token;
    }

    private async Task<ValidationSessionRow?> GetValidSessionAsync(
        int partnerId, string sessionToken, CancellationToken ct)
    {
        using var conn = await OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ValidationSessionRow>(
            """
            SELECT Id, PartnerId, SessionToken, ModuleCode, PeriodCode,
                   SubmittedData, IsValid
            FROM   CaaSValidationSessions
            WHERE  SessionToken = @Token
              AND  PartnerId = @PartnerId
              AND  ExpiresAt > SYSUTCDATETIME()
              AND  ConvertedToReturnId IS NULL
            """,
            new { Token = sessionToken, PartnerId = partnerId });
    }

    private async Task MarkSessionConvertedAsync(
        int partnerId, string sessionToken, long returnInstanceId, CancellationToken ct)
    {
        using var conn = await OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE CaaSValidationSessions
            SET    ConvertedToReturnId = @ReturnId, UpdatedAt = SYSUTCDATETIME()
            WHERE  SessionToken = @Token AND PartnerId = @PartnerId
            """,
            new { ReturnId = returnInstanceId, Token = sessionToken, PartnerId = partnerId });
    }

    private async Task<long> CreateReturnInstanceAsync(
        int institutionId, string moduleCode, string periodCode,
        Dictionary<string, object?> fields, int submittedBy, CancellationToken ct)
    {
        using var conn = await OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO ReturnInstances
                (InstitutionId, ModuleCode, ReturnVersion, ReportingPeriod,
                 Status, FieldDataJson, CreatedBy, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@InstitutionId, @ModuleCode, 1, @Period,
                    'APPROVED', @Fields, @CreatedBy, SYSUTCDATETIME())
            """,
            new
            {
                InstitutionId = institutionId,
                ModuleCode = moduleCode,
                Period = periodCode,
                Fields = System.Text.Json.JsonSerializer.Serialize(fields),
                CreatedBy = submittedBy
            });
    }

    private static double ComputeComplianceScore(int errors, int warnings, int totalFields)
    {
        if (totalFields == 0) return 100.0;
        var deductions = errors * 10.0 + warnings * 2.0;
        return Math.Max(0, Math.Round(100.0 - deductions / totalFields * 10, 1));
    }

    private static string GetCurrentPeriodCode()
    {
        var now = DateTime.UtcNow;
        return $"{now.Year}-{now.Month:D2}";
    }

    private static CaaSSubmitResponse SubmitFail(Guid requestId, string error)
        => new(false, null, null, null, null, error, requestId);

    private async Task<IDbConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var conn = await _db.OpenAsync(ct)
            ?? throw new InvalidOperationException("Database connection factory returned null.");

        using var command = conn.CreateCommand();
        if (command is null)
        {
            throw new InvalidOperationException("Database connection does not support command creation.");
        }

        return conn;
    }

    // ── Private row types ─────────────────────────────────────────────────────
    private sealed record FilingDeadlineRow(
        string ModuleCode, string ModuleName, string PeriodCode,
        DateTime DeadlineDate, string RegulatorCode);

    private sealed record ModuleScoreRow(
        string ModuleCode, string ModuleName, int TotalReturns,
        int Approved, int Overdue, int Pending, int ValidationErrors);

    private sealed record RegulatoryChangeRow(
        string ChangeId, string RegulatorCode, string ModuleCode,
        string Title, string Summary, DateTime EffectiveDate, string Severity);

    private sealed record ValidationSessionRow(
        long Id, int PartnerId, string SessionToken,
        string ModuleCode, string PeriodCode, string SubmittedData, bool? IsValid);
}
