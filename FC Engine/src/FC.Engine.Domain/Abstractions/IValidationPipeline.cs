namespace FC.Engine.Domain.Abstractions;

/// <summary>
/// CaaS validation pipeline — validates a flat field map against a module template.
/// Implementations adapt the existing multi-phase ValidationOrchestrator.
/// </summary>
public interface IValidationPipeline
{
    Task<CaaSValidationReport> ValidateAsync(
        int institutionId,
        string moduleCode,
        string periodCode,
        Dictionary<string, object?> fields,
        CancellationToken ct = default);
}

public sealed class CaaSValidationReport
{
    public IReadOnlyList<CaaSViolation> Violations { get; init; } = Array.Empty<CaaSViolation>();
    public int TotalFields { get; init; }
    public Dictionary<string, object?> ComputedValues { get; init; } = new();
}

public sealed class CaaSViolation
{
    public string FieldCode { get; init; } = string.Empty;
    public string FieldLabel { get; init; } = string.Empty;
    public string ErrorCode { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;  // "ERROR" | "WARNING"
}
