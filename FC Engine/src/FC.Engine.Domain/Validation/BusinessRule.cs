using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Validation;

public class BusinessRule
{
    public int Id { get; set; }

    /// <summary>FK to Tenant for RLS.</summary>
    public Guid? TenantId { get; set; }

    public string RuleCode { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string RuleType { get; set; } = string.Empty;        // DateCheck, ThresholdCheck, Completeness, Custom
    public string? Expression { get; set; }
    public string? AppliesToTemplates { get; set; }              // JSON: ["MFCR 300"] or "*"
    public string? AppliesToFields { get; set; }                 // JSON: ["field_name"] or null
    public ValidationSeverity Severity { get; set; } = ValidationSeverity.Error;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}
