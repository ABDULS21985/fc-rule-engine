namespace FC.Engine.Domain.Entities;

public class Institution
{
    public int Id { get; set; }
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

    // ── Navigation (FI Portal) ──

    /// <summary>FI Portal users belonging to this institution.</summary>
    public List<InstitutionUser> Users { get; set; } = new();
}
