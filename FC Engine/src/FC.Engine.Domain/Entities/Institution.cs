using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Entities;

public class Institution
{
    public int Id { get; set; }

    /// <summary>FK to Tenant. Every institution belongs to exactly one tenant.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Regulatory jurisdiction this institution belongs to.</summary>
    public int JurisdictionId { get; set; } = 1;

    public string InstitutionCode { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public string? LicenseType { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    // ── Hierarchy ──

    /// <summary>Self-referencing FK for organisational hierarchy (HoldingGroup → Subsidiary → Branch).</summary>
    public int? ParentInstitutionId { get; set; }

    /// <summary>Entity type within the hierarchy.</summary>
    public EntityType EntityType { get; set; } = EntityType.HeadOffice;

    /// <summary>Branch code for branch-level entities.</summary>
    public string? BranchCode { get; set; }

    /// <summary>Physical location or region.</summary>
    public string? Location { get; set; }

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

    // ── Navigation ──

    /// <summary>FI Portal users belonging to this institution.</summary>
    public List<InstitutionUser> Users { get; set; } = new();

    /// <summary>The tenant that owns this institution.</summary>
    public Tenant? Tenant { get; set; }

    /// <summary>Regulatory jurisdiction profile (country/currency/timezone).</summary>
    public Jurisdiction? Jurisdiction { get; set; }

    /// <summary>Parent institution in the hierarchy.</summary>
    public Institution? ParentInstitution { get; set; }

    /// <summary>Child institutions (subsidiaries, branches).</summary>
    public List<Institution> ChildInstitutions { get; set; } = new();
}
