using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Entities;

/// <summary>
/// A user belonging to a financial institution who accesses the FI Portal.
/// Separate from PortalUser (Admin portal users).
/// </summary>
public class InstitutionUser
{
    public int Id { get; set; }

    /// <summary>FK to Tenant for RLS.</summary>
    public Guid TenantId { get; set; }

    /// <summary>FK to Institution.</summary>
    public int InstitutionId { get; set; }

    /// <summary>Unique across all institutions.</summary>
    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public InstitutionRole Role { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Force password change on first login.</summary>
    public bool MustChangePassword { get; set; } = true;

    /// <summary>Preferred UI language for localised field labels/help text.</summary>
    public string PreferredLanguage { get; set; } = "en";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }

    public string? LastLoginIp { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletionReason { get; set; }

    public int FailedLoginAttempts { get; set; }

    /// <summary>Account lockout — null means not locked.</summary>
    public DateTime? LockedUntil { get; set; }

    // Navigation
    public Institution? Institution { get; set; }
}
