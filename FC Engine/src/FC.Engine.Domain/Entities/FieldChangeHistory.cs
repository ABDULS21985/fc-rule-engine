namespace FC.Engine.Domain.Entities;

/// <summary>
/// Tracks every field-level value change with before/after values,
/// change source, and user attribution.
/// </summary>
public class FieldChangeHistory
{
    public long Id { get; set; }

    /// <summary>FK to Tenant for RLS.</summary>
    public Guid TenantId { get; set; }

    public int SubmissionId { get; set; }
    public string ReturnCode { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;

    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    /// <summary>Manual, Import, Computed, or System.</summary>
    public string ChangeSource { get; set; } = "Manual";

    public string? SourceDetail { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
}
