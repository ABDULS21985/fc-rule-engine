namespace FC.Engine.Domain.Enums;

/// <summary>
/// Roles for financial institution portal users.
/// </summary>
public enum InstitutionRole
{
    /// <summary>Can manage institution users, settings, and has full access.</summary>
    Admin = 0,

    /// <summary>Can create, upload, and edit submissions.</summary>
    Maker = 1,

    /// <summary>Can review and approve/reject submissions before final submit.</summary>
    Checker = 2,

    /// <summary>Read-only access to submissions and reports.</summary>
    Viewer = 3
}
