using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Tests.Services;

public class SanctionsPackCatalogServiceTests
{
    [Fact]
    public async Task MaterializeAsync_Persists_Sanctions_Pack_Sections_And_Loads_Them_Back()
    {
        await using var db = CreateDb();
        var sut = new SanctionsPackCatalogService(db);

        var pack = new List<SanctionsPackSectionInput>
        {
            new()
            {
                SectionCode = "SAN-01",
                SectionName = "Watchlist Integration",
                RowCount = 7,
                Signal = "Ready",
                Coverage = "7 configured source row(s).",
                Commentary = "All watchlist sources are synchronized.",
                RecommendedAction = "Keep the daily refresh cadence active."
            },
            new()
            {
                SectionCode = "SAN-02",
                SectionName = "Alert Workflow",
                RowCount = 4,
                Signal = "Watch",
                Coverage = "4 reviewed alert row(s).",
                Commentary = "One alert still needs escalation.",
                RecommendedAction = "Complete analyst review and compliance escalation."
            }
        };

        var materialized = await sut.MaterializeAsync(pack);
        var loaded = await sut.LoadAsync();

        materialized.Sections.Should().HaveCount(2);
        materialized.Sections.Should().ContainSingle(x => x.SectionCode == "SAN-02" && x.Signal == "Watch");
        loaded.Sections.Should().HaveCount(2);
        loaded.Sections.Should().ContainSingle(x => x.SectionCode == "SAN-01" && x.RowCount == 7);

        var persisted = await db.SanctionsPackSections.AsNoTracking().ToListAsync();
        persisted.Should().HaveCount(2);
        persisted.Should().Contain(x => x.SectionCode == "SAN-02" && x.Signal == "Watch");
    }

    private static MetadataDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MetadataDbContext(options);
    }
}
