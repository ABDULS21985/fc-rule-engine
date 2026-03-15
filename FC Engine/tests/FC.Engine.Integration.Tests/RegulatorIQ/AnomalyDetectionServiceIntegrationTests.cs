using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FC.Engine.Integration.Tests.RegulatorIQ;

[Collection("RegulatorIqIntegration")]
public sealed class AnomalyDetectionServiceIntegrationTests : IClassFixture<RegulatorIqFixture>
{
    private readonly RegulatorIqFixture _fixture;

    public AnomalyDetectionServiceIntegrationTests(RegulatorIqFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetSectorSummaryAsync_WhenReportsTableIsMissing_ReturnsEmptyList()
    {
        await using var db = _fixture.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync(
            """
IF OBJECT_ID(N'dbo.TenantSecurityPolicy', N'SP') IS NOT NULL
BEGIN
    DROP SECURITY POLICY dbo.TenantSecurityPolicy;
END;

IF OBJECT_ID(N'[meta].[anomaly_findings]', N'U') IS NOT NULL
BEGIN
    DROP TABLE [meta].[anomaly_findings];
END;

IF OBJECT_ID(N'[meta].[anomaly_reports]', N'U') IS NOT NULL
BEGIN
    DROP TABLE [meta].[anomaly_reports];
END;
""");

        var service = CreateService(db);

        var summary = await service.GetSectorSummaryAsync("CBN", null, "2026-Q1");

        summary.Should().BeEmpty();

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task GetSectorSummaryAsync_WhenFindingsTableIsMissing_ReturnsRowsWithZeroUnacknowledgedCounts()
    {
        await using var db = _fixture.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync(
            """
IF OBJECT_ID(N'dbo.TenantSecurityPolicy', N'SP') IS NOT NULL
BEGIN
    DROP SECURITY POLICY dbo.TenantSecurityPolicy;
END;

IF OBJECT_ID(N'[meta].[anomaly_findings]', N'U') IS NOT NULL
BEGIN
    DROP TABLE [meta].[anomaly_findings];
END;
""");

        var service = CreateService(db);

        var summary = await service.GetSectorSummaryAsync("CBN", null, "2026-Q1");

        summary.Should().NotBeEmpty();
        summary.Should().AllSatisfy(row => row.UnacknowledgedCount.Should().Be(0));

        await transaction.RollbackAsync();
    }

    private static AnomalyDetectionService CreateService(MetadataDbContext db)
    {
        return new AnomalyDetectionService(
            db,
            new NoopAnomalyModelTrainingService(),
            new NoopAuditLogger(),
            NullLogger<AnomalyDetectionService>.Instance);
    }

    private sealed class NoopAuditLogger : IAuditLogger
    {
        public Task Log(
            string entityType,
            int entityId,
            string action,
            object? oldValues,
            object? newValues,
            string performedBy,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task Log(
            string entityType,
            string entityRef,
            string action,
            object? oldValues,
            object? newValues,
            string performedBy,
            Guid? explicitTenantId = null,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoopAnomalyModelTrainingService : IAnomalyModelTrainingService
    {
        public Task<AnomalyModelVersion> TrainModuleModelAsync(
            string moduleCode,
            string initiatedBy,
            bool promoteImmediately = false,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task PromoteModelAsync(int modelVersionId, string promotedBy, CancellationToken ct = default) => Task.CompletedTask;

        public Task RollbackModelAsync(string moduleCode, string rolledBackBy, CancellationToken ct = default) => Task.CompletedTask;

        public Task<List<AnomalyModelTrainingSummary>> GetModelHistoryAsync(string moduleCode, CancellationToken ct = default) =>
            Task.FromResult(new List<AnomalyModelTrainingSummary>());
    }
}
