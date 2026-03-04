using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace FC.Engine.Integration.Tests.MultiTenancy;

/// <summary>
/// Cross-tenant isolation integration tests.
/// These tests prove that SQL Server Row-Level Security
/// prevents data leakage between tenants.
/// Requires a real SQL Server instance with RLS enabled.
/// </summary>
public class TenantIsolationTests : IAsyncLifetime
{
    private string _connectionString = null!;
    private Guid _tenantAId;
    private Guid _tenantBId;

    public async Task InitializeAsync()
    {
        // Use test configuration or environment variable
        _connectionString = Environment.GetEnvironmentVariable("FCENGINE_TEST_CONNSTRING")
            ?? "Server=localhost,1433;Database=FcEngine_Test;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True";

        _tenantAId = Guid.NewGuid();
        _tenantBId = Guid.NewGuid();

        // Create test tenants
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(@"
            IF NOT EXISTS (SELECT 1 FROM dbo.tenants WHERE TenantId = @TenantAId)
                INSERT INTO dbo.tenants (TenantId, TenantName, TenantSlug, TenantStatus, CreatedAt, UpdatedAt)
                VALUES (@TenantAId, 'Test Tenant A', 'test-tenant-a', 'Active', SYSUTCDATETIME(), SYSUTCDATETIME());

            IF NOT EXISTS (SELECT 1 FROM dbo.tenants WHERE TenantId = @TenantBId)
                INSERT INTO dbo.tenants (TenantId, TenantName, TenantSlug, TenantStatus, CreatedAt, UpdatedAt)
                VALUES (@TenantBId, 'Test Tenant B', 'test-tenant-b', 'Active', SYSUTCDATETIME(), SYSUTCDATETIME());
        ", new { TenantAId = _tenantAId, TenantBId = _tenantBId });
    }

    public async Task DisposeAsync()
    {
        // Cleanup test data (without SESSION_CONTEXT so RLS allows access)
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(@"
            DELETE FROM dbo.return_submissions WHERE TenantId IN (@TenantAId, @TenantBId);
            DELETE FROM dbo.institutions WHERE TenantId IN (@TenantAId, @TenantBId);
            DELETE FROM dbo.tenants WHERE TenantId IN (@TenantAId, @TenantBId);
        ", new { TenantAId = _tenantAId, TenantBId = _tenantBId });
    }

    [Fact]
    public async Task TenantA_Cannot_See_TenantB_Data()
    {
        // ═══════════════════════════════════════════════════════════
        // Arrange: Create institutions for both tenants
        // ═══════════════════════════════════════════════════════════
        await using var setupConn = new SqlConnection(_connectionString);
        await setupConn.OpenAsync();

        // Insert institution for Tenant A
        var institutionAId = await setupConn.ExecuteScalarAsync<int>(@"
            INSERT INTO dbo.institutions (TenantId, InstitutionCode, InstitutionName, IsActive, CreatedAt)
            VALUES (@TenantId, 'INST-A', 'Institution A', 1, SYSUTCDATETIME());
            SELECT SCOPE_IDENTITY();
        ", new { TenantId = _tenantAId });

        // Insert institution for Tenant B
        var institutionBId = await setupConn.ExecuteScalarAsync<int>(@"
            INSERT INTO dbo.institutions (TenantId, InstitutionCode, InstitutionName, IsActive, CreatedAt)
            VALUES (@TenantId, 'INST-B', 'Institution B', 1, SYSUTCDATETIME());
            SELECT SCOPE_IDENTITY();
        ", new { TenantId = _tenantBId });

        // ═══════════════════════════════════════════════════════════
        // Act: Query with Tenant A context
        // ═══════════════════════════════════════════════════════════
        await using var tenantAConn = await CreateTenantConnectionAsync(_tenantAId);
        var tenantAInstitutions = (await tenantAConn.QueryAsync<dynamic>(
            "SELECT Id, InstitutionCode, TenantId FROM dbo.institutions")).ToList();

        // ═══════════════════════════════════════════════════════════
        // Assert: Tenant A sees ONLY its own institution
        // ═══════════════════════════════════════════════════════════
        tenantAInstitutions.Should().HaveCount(1);
        ((int)tenantAInstitutions[0].Id).Should().Be(institutionAId);

        // ═══════════════════════════════════════════════════════════
        // Act: Query with Tenant B context
        // ═══════════════════════════════════════════════════════════
        await using var tenantBConn = await CreateTenantConnectionAsync(_tenantBId);
        var tenantBInstitutions = (await tenantBConn.QueryAsync<dynamic>(
            "SELECT Id, InstitutionCode, TenantId FROM dbo.institutions")).ToList();

        // ═══════════════════════════════════════════════════════════
        // Assert: Tenant B sees ONLY its own institution
        // ═══════════════════════════════════════════════════════════
        tenantBInstitutions.Should().HaveCount(1);
        ((int)tenantBInstitutions[0].Id).Should().Be(institutionBId);

        // ═══════════════════════════════════════════════════════════
        // Act: Query with PlatformAdmin context (no SESSION_CONTEXT)
        // ═══════════════════════════════════════════════════════════
        await using var adminConn = new SqlConnection(_connectionString);
        await adminConn.OpenAsync();
        var allInstitutions = (await adminConn.QueryAsync<dynamic>(
            "SELECT Id, InstitutionCode, TenantId FROM dbo.institutions WHERE TenantId IN (@TenantAId, @TenantBId)",
            new { TenantAId = _tenantAId, TenantBId = _tenantBId })).ToList();

        // ═══════════════════════════════════════════════════════════
        // Assert: PlatformAdmin sees ALL institutions
        // ═══════════════════════════════════════════════════════════
        allInstitutions.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task TenantA_Cannot_Update_TenantB_Data()
    {
        // Arrange: Create institution for Tenant B
        await using var setupConn = new SqlConnection(_connectionString);
        await setupConn.OpenAsync();

        var institutionBId = await setupConn.ExecuteScalarAsync<int>(@"
            INSERT INTO dbo.institutions (TenantId, InstitutionCode, InstitutionName, IsActive, CreatedAt)
            VALUES (@TenantId, 'INST-B-UPD', 'Institution B Update Test', 1, SYSUTCDATETIME());
            SELECT SCOPE_IDENTITY();
        ", new { TenantId = _tenantBId });

        // Act: Try to UPDATE Tenant B's institution from Tenant A's context
        await using var tenantAConn = await CreateTenantConnectionAsync(_tenantAId);
        var rowsAffected = await tenantAConn.ExecuteAsync(
            "UPDATE dbo.institutions SET InstitutionName = 'HACKED' WHERE Id = @Id",
            new { Id = institutionBId });

        // Assert: BLOCK predicate prevents the update — 0 rows affected
        rowsAffected.Should().Be(0);

        // Verify data is unchanged
        await using var verifyConn = new SqlConnection(_connectionString);
        await verifyConn.OpenAsync();
        var name = await verifyConn.ExecuteScalarAsync<string>(
            "SELECT InstitutionName FROM dbo.institutions WHERE Id = @Id",
            new { Id = institutionBId });
        name.Should().Be("Institution B Update Test");
    }

    [Fact]
    public async Task TenantA_Cannot_Delete_TenantB_Data()
    {
        // Arrange: Create institution for Tenant B
        await using var setupConn = new SqlConnection(_connectionString);
        await setupConn.OpenAsync();

        var institutionBId = await setupConn.ExecuteScalarAsync<int>(@"
            INSERT INTO dbo.institutions (TenantId, InstitutionCode, InstitutionName, IsActive, CreatedAt)
            VALUES (@TenantId, 'INST-B-DEL', 'Institution B Delete Test', 1, SYSUTCDATETIME());
            SELECT SCOPE_IDENTITY();
        ", new { TenantId = _tenantBId });

        // Act: Try to DELETE Tenant B's institution from Tenant A's context
        await using var tenantAConn = await CreateTenantConnectionAsync(_tenantAId);
        var rowsAffected = await tenantAConn.ExecuteAsync(
            "DELETE FROM dbo.institutions WHERE Id = @Id",
            new { Id = institutionBId });

        // Assert: BLOCK predicate prevents the delete — 0 rows affected
        rowsAffected.Should().Be(0);

        // Verify data still exists
        await using var verifyConn = new SqlConnection(_connectionString);
        await verifyConn.OpenAsync();
        var exists = await verifyConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.institutions WHERE Id = @Id",
            new { Id = institutionBId });
        exists.Should().Be(1);
    }

    private async Task<SqlConnection> CreateTenantConnectionAsync(Guid tenantId)
    {
        var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId",
            new { tenantId });
        return conn;
    }
}
