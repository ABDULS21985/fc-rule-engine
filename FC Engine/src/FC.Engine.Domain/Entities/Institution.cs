namespace FC.Engine.Domain.Entities;

public class Institution
{
    public int Id { get; set; }

    /// <summary>FK to Tenant. Every institution belongs to exactly one tenant.</summary>
    public Guid TenantId { get; set; }

    public string InstitutionCode { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public string? LicenseType { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    // ── FI Portal Extensions ──

    public string? ContactEmail { get; set; }

    public string? ContactPhone { get; set; }

    public string? Address { get; set; }

    /// <summary>Maximum number of FI Portal users allowed. Default 10.</summary>
    public int MaxUsersAllowed { get; set; } = 10;

    /// <summary>Subscription tier: Basic, Standard, Premium.</summary>
    public string SubscriptionTier { get; set; } = "Basic";

    /// <summary>Timestamp of the most recent submission from this institution.</summary>
    public DateTime? LastSubmissionAt { get; set; }

    /// <summary>
    /// When true, submissions from this institution require Checker approval
    /// before being officially accepted (maker-checker workflow).
    /// </summary>
    public bool MakerCheckerEnabled { get; set; }

    /// <summary>JSON-serialized portal preferences for this institution.</summary>
    public string? SettingsJson { get; set; }

    // ── Navigation (FI Portal) ──

    /// <summary>FI Portal users belonging to this institution.</summary>
    public List<InstitutionUser> Users { get; set; } = new();

    /// <summary>The tenant that owns this institution.</summary>
    public Tenant? Tenant { get; set; }
}
