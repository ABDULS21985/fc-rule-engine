using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Tests.Services;

public class CapitalPackCatalogServiceTests
{
    [Fact]
    public async Task MaterializeAsync_Persists_Capital_Pack_Sections_And_Loads_Them_Back()
    {
        await using var db = CreateDb();
        var sut = new CapitalPackCatalogService(db);

        var pack = new List<CapitalPackSectionInput>
        {
            new()
            {
                SectionCode = "CAP-01",
                SectionName = "Capital Watchlist & Intervention",
                RowCount = 3,
                Signal = "Watch",
                Coverage = "3 watchlist row(s).",
                Commentary = "Supervisory intervention remains active for three institutions.",
                RecommendedAction = "Escalate the leading capital cases."
            },
            new()
            {
                SectionCode = "CAP-02",
                SectionName = "CAR Projection Horizon",
                RowCount = 8,
                Signal = "Ready",
                Coverage = "8 quarter(s) projected.",
                Commentary = "The projected capital trajectory remains inside the threshold horizon.",
                RecommendedAction = "Refresh assumptions next quarter."
            }
        };

        var materialized = await sut.MaterializeAsync(pack);
        var loaded = await sut.LoadAsync();

        materialized.Sections.Should().HaveCount(2);
        materialized.Sections.Should().ContainSingle(x => x.SectionCode == "CAP-01" && x.Signal == "Watch");
        loaded.Sections.Should().HaveCount(2);
        loaded.Sections.Should().ContainSingle(x => x.SectionCode == "CAP-02" && x.RowCount == 8);

        var persisted = await db.CapitalPackSheets.AsNoTracking().ToListAsync();
        persisted.Should().HaveCount(2);
        persisted.Should().Contain(x => x.SectionCode == "CAP-01" && x.Signal == "Watch");
    }

    private static MetadataDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MetadataDbContext(options);
    }
}
