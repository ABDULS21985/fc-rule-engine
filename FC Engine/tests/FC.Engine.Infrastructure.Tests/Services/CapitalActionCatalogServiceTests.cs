using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Tests.Services;

public class CapitalActionCatalogServiceTests
{
    [Fact]
    public async Task MaterializeAsync_Persists_Action_Templates_And_Loads_Them_Back()
    {
        await using var db = CreateDb();
        var sut = new CapitalActionCatalogService(db);

        var templates = new List<CapitalActionTemplateInput>
        {
            new()
            {
                Code = "COLLATERAL",
                Title = "Collateral optimisation",
                Summary = "Tighten eligible collateral recognition.",
                PrimaryLever = "RWA",
                CapitalActionBn = 0m,
                RwaOptimisationPercent = 4.5m,
                QuarterlyRetainedEarningsDeltaBn = 0m,
                EstimatedAnnualCostPercent = 0.9m
            },
            new()
            {
                Code = "ISSUANCE",
                Title = "AT1 / Tier 2 issuance",
                Summary = "Inject regulatory capital rapidly.",
                PrimaryLever = "Capital",
                CapitalActionBn = 10m,
                RwaOptimisationPercent = 0m,
                QuarterlyRetainedEarningsDeltaBn = 0m,
                EstimatedAnnualCostPercent = 14.5m
            }
        };

        var materialized = await sut.MaterializeAsync(templates);
        var loaded = await sut.LoadAsync();

        materialized.Templates.Should().HaveCount(2);
        loaded.Templates.Should().ContainSingle(x => x.Code == "ISSUANCE" && x.CapitalActionBn == 10m);

        var persisted = await db.CapitalActionTemplates.AsNoTracking().ToListAsync();
        persisted.Should().HaveCount(2);
        persisted.Should().Contain(x => x.Code == "COLLATERAL" && x.PrimaryLever == "RWA");
    }

    private static MetadataDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MetadataDbContext(options);
    }
}
