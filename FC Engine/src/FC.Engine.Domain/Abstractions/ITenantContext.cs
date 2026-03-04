namespace FC.Engine.Domain.Abstractions;

/// <summary>
/// Provides the current tenant context for the request.
/// Used to scope all database queries via SQL Server SESSION_CONTEXT.
/// </summary>
public interface ITenantContext
{
    /// <summary>Current tenant ID. Null for PlatformAdmin without impersonation.</summary>
    Guid? CurrentTenantId { get; }

    /// <summary>True if the current user is a PlatformAdmin (super-tenant role).</summary>
    bool IsPlatformAdmin { get; }

    /// <summary>Set when PlatformAdmin is impersonating a specific tenant.</summary>
    Guid? ImpersonatingTenantId { get; }
}
