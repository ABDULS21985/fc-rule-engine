using System.Data;

namespace FC.Engine.Domain.Abstractions;

/// <summary>
/// Factory for creating database connections with tenant SESSION_CONTEXT set.
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>
    /// Creates and opens a SqlConnection with SESSION_CONTEXT('TenantId') set.
    /// If tenantId is null (PlatformAdmin), SESSION_CONTEXT is not set, allowing RLS bypass.
    /// </summary>
    Task<IDbConnection> CreateConnectionAsync(Guid? tenantId, CancellationToken ct = default);
}

public static class DbConnectionFactoryExtensions
{
    /// <summary>
    /// Opens a connection without tenant context — used by CaaS services which
    /// isolate by PartnerId rather than TenantId row-level security.
    /// </summary>
    public static Task<IDbConnection> OpenAsync(
        this IDbConnectionFactory factory, CancellationToken ct = default)
        => factory.CreateConnectionAsync(null, ct);
}
