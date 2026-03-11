using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Tests.Services;

public class ModelInventoryCatalogServiceTests
{
    [Fact]
    public async Task MaterializeAsync_Persists_Model_Inventory_Definitions_And_Loads_Them_Back()
    {
        await using var db = CreateDb();
        var sut = new ModelInventoryCatalogService(db);

        var definitions = new List<ModelInventoryDefinitionInput>
        {
            new()
            {
                ModelCode = "ECL",
                ModelName = "IFRS 9 Expected Credit Loss",
                Tier = "Tier 1",
                Owner = "Credit Risk",
                ReturnHint = "MFB_IFR",
                MatchTerms = ["ecl", "pd", "lgd", "ead"]
            },
            new()
            {
                ModelCode = "CAR",
                ModelName = "Capital Adequacy Ratio Engine",
                Tier = "Tier 1",
                Owner = "Prudential Reporting",
                ReturnHint = "MFB_CAP",
                MatchTerms = ["car", "capital", "tier1", "tier2", "rwa"]
            }
        };

        var materialized = await sut.MaterializeAsync(definitions);
        var loaded = await sut.LoadAsync();

        materialized.Definitions.Should().HaveCount(2);
        loaded.Definitions.Should().ContainSingle(x => x.ModelCode == "CAR" && x.Owner == "Prudential Reporting");

        var persisted = await db.ModelInventoryDefinitions.AsNoTracking().ToListAsync();
        persisted.Should().HaveCount(2);
        persisted.Should().Contain(x => x.ModelCode == "ECL");
    }

    private static MetadataDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MetadataDbContext(options);
    }
}
