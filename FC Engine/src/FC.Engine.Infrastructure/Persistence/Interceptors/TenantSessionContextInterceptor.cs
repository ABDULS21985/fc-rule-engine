using System.Data.Common;
using FC.Engine.Domain.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FC.Engine.Infrastructure.Persistence.Interceptors;

/// <summary>
/// EF Core connection interceptor that sets SQL Server SESSION_CONTEXT('TenantId')
/// when a connection is opened. This enables Row-Level Security for all EF Core queries.
/// </summary>
public class TenantSessionContextInterceptor : DbConnectionInterceptor
{
    private readonly ITenantContext _tenantContext;

    public TenantSessionContextInterceptor(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (_tenantContext.CurrentTenantId.HasValue)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "EXEC sp_set_session_context @key=N'TenantId', @value=@tid, @read_only=1";
            var param = new SqlParameter("@tid", _tenantContext.CurrentTenantId.Value);
            cmd.Parameters.Add(param);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public override void ConnectionOpened(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        if (_tenantContext.CurrentTenantId.HasValue)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "EXEC sp_set_session_context @key=N'TenantId', @value=@tid, @read_only=1";
            var param = new SqlParameter("@tid", _tenantContext.CurrentTenantId.Value);
            cmd.Parameters.Add(param);
            cmd.ExecuteNonQuery();
        }
    }
}
