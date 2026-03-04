using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Entities;

public class ValidationReport
{
    public int Id { get; set; }

    /// <summary>FK to Tenant for RLS.</summary>
    public Guid TenantId { get; set; }

    public int SubmissionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinalizedAt { get; set; }

    private readonly List<ValidationError> _errors = new();
    public IReadOnlyList<ValidationError> Errors => _errors.AsReadOnly();

    public bool IsValid => !_errors.Any(e => e.Severity == ValidationSeverity.Error);
    public bool HasWarnings => _errors.Any(e => e.Severity == ValidationSeverity.Warning);
    public bool HasErrors => _errors.Any(e => e.Severity == ValidationSeverity.Error);
    public int ErrorCount => _errors.Count(e => e.Severity == ValidationSeverity.Error);
    public int WarningCount => _errors.Count(e => e.Severity == ValidationSeverity.Warning);

    public static ValidationReport Create(int submissionId)
    {
        return new ValidationReport
        {
            SubmissionId = submissionId,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void AddError(ValidationError error) => _errors.Add(error);

    public void AddErrors(IEnumerable<ValidationError> errors) => _errors.AddRange(errors);

    public void FinalizeAt(DateTime timestamp) => FinalizedAt = timestamp;
}
