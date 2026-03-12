using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Tests.Services;

public class PlatformOperationsCatalogServiceTests
{
    [Fact]
    public async Task MaterializeAsync_Persists_Operations_Snapshot_And_Loads_It_Back()
    {
        await using var db = CreateDb();
        var sut = new PlatformOperationsCatalogService(db);

        var dueDate = DateTime.UtcNow.AddDays(2);
        var happenedAt = DateTime.UtcNow.AddHours(-6);

        var input = new PlatformOperationsCatalogInput
        {
            Interventions =
            [
                new PlatformInterventionInput
                {
                    Domain = "Rollout",
                    Subject = "OPS_RESILIENCE entitlement drift",
                    Signal = "Three enterprise tenants remain unreconciled.",
                    Priority = "Critical",
                    NextAction = "Run entitlement reconciliation and confirm activation.",
                    DueDate = dueDate,
                    OwnerLane = "Platform Operations"
                }
            ],
            Timeline =
            [
                new PlatformActivityTimelineInput
                {
                    TenantId = Guid.NewGuid(),
                    InstitutionId = 11,
                    Domain = "Marketplace",
                    Title = "Tenant modules reconciled",
                    Detail = "OPS_RESILIENCE and MODEL_RISK were activated for an enterprise tenant.",
                    HappenedAt = happenedAt,
                    Severity = "High"
                }
            ]
        };

        var materialized = await sut.MaterializeAsync(input);
        var loaded = await sut.LoadAsync();

        materialized.MaterializedAt.Should().NotBeNull();
        materialized.Interventions.Should().ContainSingle(x =>
            x.Domain == "Rollout"
            && x.Priority == "Critical"
            && x.OwnerLane == "Platform Operations");
        materialized.Timeline.Should().ContainSingle(x =>
            x.Domain == "Marketplace"
            && x.InstitutionId == 11
            && x.Severity == "High");

        loaded.MaterializedAt.Should().Be(materialized.MaterializedAt);
        loaded.Interventions.Should().ContainSingle(x =>
            x.Subject == "OPS_RESILIENCE entitlement drift"
            && x.Signal.Contains("enterprise tenants", StringComparison.OrdinalIgnoreCase));
        loaded.Timeline.Should().ContainSingle(x =>
            x.Title == "Tenant modules reconciled"
            && x.Detail.Contains("MODEL_RISK", StringComparison.OrdinalIgnoreCase));

        (await db.PlatformInterventions.AsNoTracking().CountAsync()).Should().Be(1);
        (await db.PlatformActivityTimeline.AsNoTracking().CountAsync()).Should().Be(1);
    }

    private static MetadataDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MetadataDbContext(options);
    }
}
