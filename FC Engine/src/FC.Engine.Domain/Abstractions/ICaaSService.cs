using FC.Engine.Domain.Entities;

namespace FC.Engine.Domain.Abstractions;

public interface ICaaSService
{
    /// <summary>Validate data against a module template without persisting.</summary>
    Task<CaaSValidationResponse> ValidateAsync(
        Guid tenantId, CaaSValidateRequest request, CancellationToken ct = default);

    /// <summary>Submit a complete return via API.</summary>
    Task<CaaSSubmitResponse> SubmitReturnAsync(
        Guid tenantId, int institutionId, CaaSSubmitRequest request, CancellationToken ct = default);

    /// <summary>Get template structure for a specific module.</summary>
    Task<CaaSTemplateResponse?> GetTemplateStructureAsync(
        Guid tenantId, string moduleCode, CancellationToken ct = default);

    /// <summary>Get filing deadlines for all entitled modules.</summary>
    Task<List<CaaSDeadlineItem>> GetDeadlinesAsync(
        Guid tenantId, CancellationToken ct = default);

    /// <summary>Compute compliance health score.</summary>
    Task<CaaSScoreResponse> GetComplianceScoreAsync(
        Guid tenantId, int institutionId, CaaSScoreRequest request, CancellationToken ct = default);

    /// <summary>Get regulatory changes affecting this institution.</summary>
    Task<List<CaaSRegulatoryChange>> GetRegulatoryChangesAsync(
        Guid tenantId, string? moduleCode, CancellationToken ct = default);

    /// <summary>Run a scenario simulation (validate without persisting).</summary>
    Task<CaaSSimulateResponse> SimulateAsync(
        Guid tenantId, CaaSSimulateRequest request, CancellationToken ct = default);
}

// ─── Request / Response DTOs ────────────────────────────────────────

public class CaaSValidateRequest
{
    public string ModuleCode { get; set; } = string.Empty;
    public string ReturnCode { get; set; } = string.Empty;
    public List<Dictionary<string, object?>> Records { get; set; } = new();
}

public class CaaSValidationResponse
{
    public bool IsValid { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public List<CaaSValidationError> Errors { get; set; } = new();
    public double? ComplianceScorePreview { get; set; }
}

public class CaaSValidationError
{
    public string RuleId { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? ExpectedValue { get; set; }
    public string? ActualValue { get; set; }
}

public class CaaSSubmitRequest
{
    public string ReturnCode { get; set; } = string.Empty;
    public string PeriodCode { get; set; } = string.Empty;
    public string? InstitutionCode { get; set; }
    public List<Dictionary<string, object?>> Records { get; set; } = new();
    public bool AutoApprove { get; set; }
}

public class CaaSSubmitResponse
{
    public bool Success { get; set; }
    public int SubmissionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public long ProcessingDurationMs { get; set; }
    public CaaSValidationResponse? ValidationResult { get; set; }
}

public class CaaSTemplateResponse
{
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public List<CaaSTemplateReturn> Returns { get; set; } = new();
}

public class CaaSTemplateReturn
{
    public string ReturnCode { get; set; } = string.Empty;
    public string ReturnName { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public List<CaaSFieldDefinition> Fields { get; set; } = new();
    public List<CaaSFormulaDefinition> Formulas { get; set; } = new();
}

public class CaaSFieldDefinition
{
    public string FieldName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public string? MinValue { get; set; }
    public string? MaxValue { get; set; }
    public int? MaxLength { get; set; }
    public string? AllowedValues { get; set; }
    public string? Description { get; set; }
}

public class CaaSFormulaDefinition
{
    public string RuleId { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class CaaSDeadlineItem
{
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string ReturnCode { get; set; } = string.Empty;
    public string PeriodCode { get; set; } = string.Empty;
    public DateTime Deadline { get; set; }
    public string RagStatus { get; set; } = string.Empty;
    public int? DaysRemaining { get; set; }
}

public class CaaSScoreRequest
{
    public string? ModuleCode { get; set; }
    public string? PeriodCode { get; set; }
}

public class CaaSScoreResponse
{
    public double OverallScore { get; set; }
    public string Rating { get; set; } = string.Empty; // Excellent, Good, Fair, Poor
    public CaaSScoreBreakdown Breakdown { get; set; } = new();
}

public class CaaSScoreBreakdown
{
    public double ValidationPassRate { get; set; }
    public double DeadlineAdherence { get; set; }
    public double CompletenessScore { get; set; }
    public int TotalSubmissions { get; set; }
    public int CleanSubmissions { get; set; }
    public int LateSubmissions { get; set; }
}

public class CaaSRegulatoryChange
{
    public string ModuleCode { get; set; } = string.Empty;
    public string ReturnCode { get; set; } = string.Empty;
    public int FromVersion { get; set; }
    public int ToVersion { get; set; }
    public string ChangeType { get; set; } = string.Empty; // FieldAdded, FieldRemoved, RuleChanged
    public string Description { get; set; } = string.Empty;
    public DateTime EffectiveDate { get; set; }
}

public class CaaSSimulateRequest
{
    public string ReturnCode { get; set; } = string.Empty;
    public string? ScenarioName { get; set; }
    public List<Dictionary<string, object?>> Records { get; set; } = new();
    public Dictionary<string, object?>? Overrides { get; set; }
}

public class CaaSSimulateResponse
{
    public string ScenarioName { get; set; } = string.Empty;
    public CaaSValidationResponse ValidationResult { get; set; } = new();
    public double? ProjectedComplianceScore { get; set; }
    public List<string> Recommendations { get; set; } = new();
}
