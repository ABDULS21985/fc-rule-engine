namespace FC.Engine.Domain.Abstractions;

// ── Partner Resolution (resolved from API key in middleware) ──────────────────

public sealed record ResolvedPartner(
    int PartnerId,
    string PartnerCode,
    int InstitutionId,
    PartnerTier Tier,
    string Environment,
    IReadOnlyList<string> AllowedModuleCodes);

// ── Tier Enums & Constants ────────────────────────────────────────────────────

public enum PartnerTier { Starter, Growth, Enterprise }

public enum CaaSEnvironment { Live, Test }

public static class RateLimitThresholds
{
    public static int GetRequestsPerMinute(PartnerTier tier) => tier switch
    {
        PartnerTier.Starter    => 100,
        PartnerTier.Growth     => 1_000,
        PartnerTier.Enterprise => 10_000,
        _                      => 100
    };
}

// ── Request / Response DTOs ──────────────────────────────────────────────────

public sealed record CaaSValidateRequest(
    string ModuleCode,
    string PeriodCode,
    Dictionary<string, object?> Fields,
    bool PersistSession = false);

public sealed record CaaSValidateResponse(
    bool IsValid,
    string? SessionToken,
    int ErrorCount,
    int WarningCount,
    IReadOnlyList<CaaSFieldError> Errors,
    IReadOnlyList<CaaSFieldError> Warnings,
    double ComplianceScore,
    Guid RequestId);

public sealed record CaaSFieldError(
    string FieldCode,
    string FieldLabel,
    string ErrorCode,
    string Message,
    string Severity);

public sealed record CaaSSubmitRequest(
    string? SessionToken,
    string? ModuleCode,
    string? PeriodCode,
    Dictionary<string, object?>? Fields,
    string RegulatorCode,
    int SubmittedByExternalUserId);

public sealed record CaaSSubmitResponse(
    bool Success,
    long? ReturnInstanceId,
    long? BatchId,
    string? BatchReference,
    string? ReceiptReference,
    string? ErrorMessage,
    Guid RequestId);

public sealed record CaaSTemplateResponse(
    string ModuleCode,
    string ModuleName,
    string RegulatorCode,
    string PeriodType,
    IReadOnlyList<CaaSFieldDefinition> Fields,
    IReadOnlyList<CaaSFormula> Formulas,
    Guid RequestId);

public sealed record CaaSFieldDefinition(
    string FieldCode,
    string FieldLabel,
    string DataType,
    bool IsRequired,
    string? ValidationRule,
    decimal? MinValue,
    decimal? MaxValue,
    string? Description);

public sealed record CaaSFormula(
    string FormulaCode,
    string Description,
    string Expression);

public sealed record CaaSDeadlinesResponse(
    IReadOnlyList<CaaSDeadline> Upcoming,
    Guid RequestId);

public sealed record CaaSDeadline(
    string ModuleCode,
    string ModuleName,
    string PeriodCode,
    DateOnly DeadlineDate,
    int DaysRemaining,
    bool IsOverdue,
    string RegulatorCode);

public sealed record CaaSScoreRequest(
    string? PeriodCode);

public sealed record CaaSScoreResponse(
    double OverallScore,
    string Rating,
    IReadOnlyList<CaaSModuleScore> ByModule,
    Guid RequestId);

public sealed record CaaSModuleScore(
    string ModuleCode,
    string ModuleName,
    double Score,
    int PendingReturns,
    int OverdueReturns,
    int ValidationErrors);

public sealed record CaaSChangesResponse(
    IReadOnlyList<CaaSRegulatoryChange> Changes,
    Guid RequestId);

public sealed record CaaSRegulatoryChange(
    string ChangeId,
    string RegulatorCode,
    string ModuleCode,
    string Title,
    string Summary,
    DateOnly EffectiveDate,
    string Severity);

public sealed record CaaSSimulateRequest(
    string ModuleCode,
    string PeriodCode,
    Dictionary<string, object?> Fields,
    IReadOnlyList<CaaSScenario> Scenarios);

public sealed record CaaSScenario(
    string ScenarioName,
    Dictionary<string, object?> FieldOverrides);

public sealed record CaaSSimulateResponse(
    IReadOnlyList<CaaSScenarioResult> Results,
    Guid RequestId);

public sealed record CaaSScenarioResult(
    string ScenarioName,
    bool IsValid,
    double ComplianceScore,
    IReadOnlyList<CaaSFieldError> Errors,
    Dictionary<string, object?> ComputedValues);

// ── Main CaaS Service Interface ───────────────────────────────────────────────

/// <summary>
/// Implements all CaaS API operations for external partner access.
/// </summary>
public interface ICaaSService
{
    Task<CaaSValidateResponse> ValidateAsync(
        ResolvedPartner partner,
        CaaSValidateRequest request,
        Guid requestId,
        CancellationToken ct = default);

    Task<CaaSSubmitResponse> SubmitAsync(
        ResolvedPartner partner,
        CaaSSubmitRequest request,
        Guid requestId,
        CancellationToken ct = default);

    Task<CaaSTemplateResponse> GetTemplateAsync(
        ResolvedPartner partner,
        string moduleCode,
        Guid requestId,
        CancellationToken ct = default);

    Task<CaaSDeadlinesResponse> GetDeadlinesAsync(
        ResolvedPartner partner,
        Guid requestId,
        CancellationToken ct = default);

    Task<CaaSScoreResponse> GetScoreAsync(
        ResolvedPartner partner,
        CaaSScoreRequest request,
        Guid requestId,
        CancellationToken ct = default);

    Task<CaaSChangesResponse> GetChangesAsync(
        ResolvedPartner partner,
        Guid requestId,
        CancellationToken ct = default);

    Task<CaaSSimulateResponse> SimulateAsync(
        ResolvedPartner partner,
        CaaSSimulateRequest request,
        Guid requestId,
        CancellationToken ct = default);
}

// ── Custom Exceptions ─────────────────────────────────────────────────────────

public sealed class CaaSModuleNotEntitledException : Exception
{
    public CaaSModuleNotEntitledException(string message) : base(message) { }
}

public sealed class CaaSRateLimitExceededException : Exception
{
    public int RetryAfterSeconds { get; }
    public CaaSRateLimitExceededException(int retryAfter)
        : base($"Rate limit exceeded. Retry after {retryAfter}s.")
    {
        RetryAfterSeconds = retryAfter;
    }
}

public sealed class CaaSSessionExpiredException : Exception
{
    public CaaSSessionExpiredException() : base("Validation session has expired or been used.") { }
}
