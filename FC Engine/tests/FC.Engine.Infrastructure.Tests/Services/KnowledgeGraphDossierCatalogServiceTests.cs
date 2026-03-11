using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Tests.Services;

public class KnowledgeGraphDossierCatalogServiceTests
{
    [Fact]
    public async Task MaterializeAsync_Persists_Dossier_Sections_And_Loads_Them_Back()
    {
        await using var db = CreateDb();
        var sut = new KnowledgeGraphDossierCatalogService(db);

        var sections = new List<KnowledgeGraphDossierSectionInput>
        {
            new()
            {
                SectionCode = "KG-01",
                SectionName = "Regulatory Ontology Coverage",
                RowCount = 6,
                Signal = "Current",
                Coverage = "6 regulator rows.",
                Commentary = "Ontology coverage is available across the live regulator universe.",
                RecommendedAction = "Refresh the ontology after the next import cycle."
            },
            new()
            {
                SectionCode = "KG-05",
                SectionName = "Impact Propagation Register",
                RowCount = 12,
                Signal = "Watch",
                Coverage = "12 downstream change paths.",
                Commentary = "Several regulatory references remain close to filing deadlines.",
                RecommendedAction = "Prioritize the critical propagation paths for supervisory review."
            }
        };

        var materialized = await sut.MaterializeAsync(sections);
        var loaded = await sut.LoadAsync();

        materialized.Sections.Should().HaveCount(2);
        materialized.Sections.Should().ContainSingle(x => x.SectionCode == "KG-05" && x.Signal == "Watch");
        loaded.Sections.Should().ContainSingle(x => x.SectionCode == "KG-01" && x.RowCount == 6);

        var persisted = await db.KnowledgeGraphDossierSections.AsNoTracking().ToListAsync();
        persisted.Should().HaveCount(2);
        persisted.Should().Contain(x => x.SectionCode == "KG-01" && x.SectionName == "Regulatory Ontology Coverage");
    }

    private static MetadataDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MetadataDbContext(options);
    }
}
