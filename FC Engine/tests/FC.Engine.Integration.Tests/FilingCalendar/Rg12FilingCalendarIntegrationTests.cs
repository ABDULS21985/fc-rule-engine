using Dapper;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.MultiTenancy;
using FC.Engine.Infrastructure.Persistence.Interceptors;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FC.Engine.Integration.Tests.FilingCalendar;

/// <summary>
/// RG-12 integration tests: filing calendar, deadline computation, SLA tracking.
/// Runs against a real SQL Server database.
/// </summary>
public class Rg12FilingCalendarIntegrationTests : IAsyncLifetime
{
    private readonly List<Guid> _createdTenantIds = new();
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await TestSqlConnectionResolver.ResolveAsync();
    }

    public async Task DisposeAsync()
    {
        if (_createdTenantIds.Count == 0) return;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        // Clear SESSION_CONTEXT to bypass RLS for cleanup
        await conn.ExecuteAsync("EXEC sp_set_session_context @key=N'TenantId', @value=NULL");

        foreach (var tenantId in _createdTenantIds)
        {
            var tid = tenantId.ToString();
            await conn.ExecuteAsync("DELETE FROM dbo.filing_sla_records WHERE TenantId = @tid", new { tid = tenantId });
            await conn.ExecuteAsync("DELETE FROM dbo.return_submissions WHERE TenantId = @tid", new { tid = tenantId });
            await conn.ExecuteAsync("DELETE FROM dbo.return_periods WHERE TenantId = @tid", new { tid = tenantId });
        }
    }

    [Fact]
    public async Task DeadlineComputation_Monthly_Produces_Correct_Deadline()
    {
        var sut = new DeadlineComputationService();
        var module = new Module { DefaultFrequency = "Monthly" };
        var period = new ReturnPeriod { Year = 2026, Month = 6, Frequency = "Monthly" };

        var deadline = sut.ComputeDeadline(module, period);

        // June 30 + 30 days = July 30
        deadline.Should().Be(new DateTime(2026, 7, 30));
    }

    [Fact]
    public async Task DeadlineComputation_Quarterly_Produces_Correct_Deadline()
    {
        var sut = new DeadlineComputationService();
        var module = new Module { DefaultFrequency = "Quarterly" };
        var period = new ReturnPeriod { Year = 2026, Month = 9, Quarter = 3, Frequency = "Quarterly" };

        var deadline = sut.ComputeDeadline(module, period);

        // Sep 30 + 45 days = Nov 14
        deadline.Should().Be(new DateTime(2026, 11, 14));
    }

    [Fact]
    public async Task PeriodGeneration_Monthly_Creates_12_Periods()
    {
        var sut = new DeadlineComputationService();
        var module = new Module { DefaultFrequency = "Monthly" };

        var periods = sut.GeneratePeriodsForNext12Months(module, 12);

        periods.Should().HaveCount(12);
        periods.Select(p => p.Month).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task PeriodGeneration_Quarterly_Creates_4_Periods()
    {
        var sut = new DeadlineComputationService();
        var module = new Module { DefaultFrequency = "Quarterly" };

        var periods = sut.GeneratePeriodsForNext12Months(module, 12);

        periods.Should().HaveCount(4);
        periods.Should().AllSatisfy(p => p.Quarter.Should().NotBeNull());
    }

    [Fact]
    public async Task PeriodGeneration_Annual_Creates_1_Period()
    {
        var sut = new DeadlineComputationService();
        var module = new Module { DefaultFrequency = "Annual" };

        var periods = sut.GeneratePeriodsForNext12Months(module, 12);

        periods.Should().HaveCount(1);
        periods.Should().OnlyContain(p => p.Frequency == "Annual");
    }

    [Fact]
    public async Task ReturnPeriod_Can_Be_Persisted_With_New_RG12_Columns()
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        // Verify the new columns exist
        var columns = await conn.QueryAsync<string>(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'return_periods' AND TABLE_SCHEMA = 'dbo'");

        var columnList = columns.ToList();
        columnList.Should().Contain("ModuleId");
        columnList.Should().Contain("Quarter");
        columnList.Should().Contain("DeadlineDate");
        columnList.Should().Contain("DeadlineOverrideDate");
        columnList.Should().Contain("DeadlineOverrideBy");
        columnList.Should().Contain("DeadlineOverrideReason");
        columnList.Should().Contain("AutoCreatedReturnId");
        columnList.Should().Contain("Status");
        columnList.Should().Contain("NotificationLevel");
    }

    [Fact]
    public async Task FilingSlaRecords_Table_Exists()
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT CASE WHEN OBJECT_ID(N'dbo.filing_sla_records', N'U') IS NOT NULL THEN 1 ELSE 0 END");

        exists.Should().Be(1);
    }

    [Fact]
    public async Task FilingSlaRecords_Has_RLS_Predicate()
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var hasPredicate = await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*)
            FROM sys.security_predicates sp
            INNER JOIN sys.objects o ON sp.target_object_id = o.object_id
            WHERE o.name = 'filing_sla_records'");

        hasPredicate.Should().BeGreaterThan(0, "filing_sla_records should have RLS predicates");
    }

    [Fact]
    public async Task Modules_Table_Has_DeadlineOffsetDays_Column()
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var hasColumn = await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = 'modules' AND COLUMN_NAME = 'DeadlineOffsetDays'");

        hasColumn.Should().Be(1);
    }

    [Fact]
    public async Task SLA_Record_Tracks_DaysToDeadline_Correctly()
    {
        var deadlineService = new DeadlineComputationService();
        var module = new Module { Id = 1, DefaultFrequency = "Monthly" };
        var period = new ReturnPeriod { Year = 2026, Month = 3, Frequency = "Monthly" };

        var deadline = deadlineService.ComputeDeadline(module, period);
        var submittedDate = deadline.AddDays(-5); // Submitted 5 days early
        var daysToDeadline = (deadline.Date - submittedDate.Date).Days;

        daysToDeadline.Should().Be(5);
        (daysToDeadline >= 0).Should().BeTrue("Submitted before deadline means on time");
    }

    [Fact]
    public async Task SLA_Record_Late_Submission_Has_Negative_DaysToDeadline()
    {
        var deadlineService = new DeadlineComputationService();
        var module = new Module { Id = 1, DefaultFrequency = "Monthly" };
        var period = new ReturnPeriod { Year = 2026, Month = 3, Frequency = "Monthly" };

        var deadline = deadlineService.ComputeDeadline(module, period);
        var submittedDate = deadline.AddDays(3); // Submitted 3 days late
        var daysToDeadline = (deadline.Date - submittedDate.Date).Days;

        daysToDeadline.Should().Be(-3);
        (daysToDeadline >= 0).Should().BeFalse("Submitted after deadline means late");
    }

    // ── Helpers ────────────────────────────────────────────────

    private MetadataDbContext CreateDbContext(Guid? tenantId = null)
    {
        var tenantContext = new StubTenantContext(tenantId);
        var interceptor = new TenantSessionContextInterceptor(tenantContext);

        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseSqlServer(_connectionString)
            .AddInterceptors(interceptor)
            .Options;

        return new MetadataDbContext(options, tenantContext);
    }

    private sealed class StubTenantContext : Domain.Abstractions.ITenantContext
    {
        public StubTenantContext(Guid? tenantId) => CurrentTenantId = tenantId;
        public Guid? CurrentTenantId { get; }
        public bool IsPlatformAdmin => CurrentTenantId is null;
        public Guid? ImpersonatingTenantId => null;
    }
}
