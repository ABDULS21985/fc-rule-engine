using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Tests.Services;

public class CapitalPlanningScenarioStoreServiceTests
{
    [Fact]
    public async Task SaveAsync_Persists_And_Updates_Latest_Capital_Planning_Scenario()
    {
        await using var db = CreateDb();
        var sut = new CapitalPlanningScenarioStoreService(db);

        await sut.SaveAsync(new CapitalPlanningScenarioCommand
        {
            CurrentCarPercent = 17.5m,
            CurrentRwaBn = 125m,
            QuarterlyRwaGrowthPercent = 4.2m,
            QuarterlyRetainedEarningsBn = 2.5m,
            CapitalActionBn = 8m,
            MinimumRequirementPercent = 10m,
            ConservationBufferPercent = 2.5m,
            CountercyclicalBufferPercent = 0.5m,
            DsibBufferPercent = 1m,
            RwaOptimisationPercent = 3m,
            TargetCarPercent = 19.5m,
            Cet1CostPercent = 18m,
            At1CostPercent = 14m,
            Tier2CostPercent = 11m,
            MaxAt1SharePercent = 30m,
            MaxTier2SharePercent = 35m,
            StepPercent = 5m,
            SavedAtUtc = new DateTime(2026, 3, 11, 12, 0, 0, DateTimeKind.Utc)
        });

        var updated = await sut.SaveAsync(new CapitalPlanningScenarioCommand
        {
            CurrentCarPercent = 18m,
            CurrentRwaBn = 130m,
            QuarterlyRwaGrowthPercent = 4m,
            QuarterlyRetainedEarningsBn = 2.8m,
            CapitalActionBn = 10m,
            MinimumRequirementPercent = 10m,
            ConservationBufferPercent = 2.5m,
            CountercyclicalBufferPercent = 1m,
            DsibBufferPercent = 1m,
            RwaOptimisationPercent = 4m,
            TargetCarPercent = 20m,
            Cet1CostPercent = 18m,
            At1CostPercent = 14m,
            Tier2CostPercent = 11m,
            MaxAt1SharePercent = 35m,
            MaxTier2SharePercent = 40m,
            StepPercent = 5m,
            SavedAtUtc = new DateTime(2026, 3, 11, 13, 0, 0, DateTimeKind.Utc)
        });

        var loaded = await sut.LoadAsync();
        var history = await sut.LoadHistoryAsync(10);

        updated.CurrentCarPercent.Should().Be(18m);
        loaded.Should().NotBeNull();
        loaded!.CurrentRwaBn.Should().Be(130m);
        loaded.CountercyclicalBufferPercent.Should().Be(1m);
        loaded.TargetCarPercent.Should().Be(20m);
        loaded.SavedAtUtc.Should().Be(new DateTime(2026, 3, 11, 13, 0, 0, DateTimeKind.Utc));
        history.Should().HaveCount(2);
        history[0].SavedAtUtc.Should().Be(new DateTime(2026, 3, 11, 13, 0, 0, DateTimeKind.Utc));
        history[0].TargetCarPercent.Should().Be(20m);
        history[1].SavedAtUtc.Should().Be(new DateTime(2026, 3, 11, 12, 0, 0, DateTimeKind.Utc));
        history[1].CurrentCarPercent.Should().Be(17.5m);

        var persisted = await db.CapitalPlanningScenarios.AsNoTracking().ToListAsync();
        persisted.Should().HaveCount(1);
        persisted.Single().ScenarioKey.Should().Be("LATEST");

        var persistedHistory = await db.CapitalPlanningScenarioHistory.AsNoTracking().ToListAsync();
        persistedHistory.Should().HaveCount(2);
    }

    private static MetadataDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MetadataDbContext(options);
    }
}
