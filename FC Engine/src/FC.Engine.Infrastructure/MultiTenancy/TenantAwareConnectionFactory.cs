using System.Data;
using Dapper;
using FC.Engine.Domain.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace FC.Engine.Infrastructure.MultiTenancy;

/// <summary>
/// Creates database connections with SQL Server SESSION_CONTEXT('TenantId') set.
/// This enables Row-Level Security filtering at the database engine level.
/// </summary>
public class TenantAwareConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public TenantAwareConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("FcEngine")
            ?? throw new InvalidOperationException("Connection string 'FcEngine' not found");
    }

    public async Task<IDbConnection> CreateConnectionAsync(Guid? tenantId, CancellationToken ct = default)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        if (tenantId.HasValue)
        {
            await connection.ExecuteAsync(
                "EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId, @read_only=1",
                new { tenantId = tenantId.Value });
        }

        return connection;
    }
}
