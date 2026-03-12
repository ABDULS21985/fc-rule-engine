using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Tests.Services;

public class KnowledgeGraphCatalogServiceTests
{
    [Fact]
    public async Task MaterializeAsync_Persists_Deduplicated_Node_And_Edge_Catalog()
    {
        await using var db = CreateDb();
        var sut = new KnowledgeGraphCatalogService(db);

        var now = DateTime.UtcNow.Date.AddDays(7);
        var request = new KnowledgeGraphCatalogMaterializationRequest
        {
            Regulators =
            [
                new KnowledgeGraphCatalogRegulatorInput
                {
                    RegulatorCode = "CBN",
                    DisplayName = "Central Bank of Nigeria",
                    ModuleCount = 1,
                    RequirementCount = 2,
                    InstitutionCount = 1,
                    ModuleCodes = ["RG08"]
                }
            ],
            Requirements =
            [
                new KnowledgeGraphCatalogRequirementInput
                {
                    RegulatoryReference = "RG-08.1",
                    RegulationFamily = "RG-08",
                    RegulatorCode = "CBN",
                    ModuleCode = "RG08",
                    FiledViaReturns = ["BDC_FXV"],
                    InstitutionCount = 1,
                    FieldCount = 2,
                    FrequencyProfile = "Monthly",
                    NextDeadline = now
                },
                new KnowledgeGraphCatalogRequirementInput
                {
                    RegulatoryReference = "RG-08.2",
                    RegulationFamily = "RG-08",
                    RegulatorCode = "CBN",
                    ModuleCode = "RG08",
                    FiledViaReturns = ["BDC_FXV"],
                    InstitutionCount = 1,
                    FieldCount = 1,
                    FrequencyProfile = "Monthly",
                    NextDeadline = now
                }
            ],
            Lineage =
            [
                new KnowledgeGraphCatalogLineageInput
                {
                    RegulatorCode = "CBN",
                    ModuleCode = "RG08",
                    ReturnCode = "BDC_FXV",
                    TemplateName = "BDC FX Position",
                    FieldName = "Net Open Position",
                    FieldCode = "net_open_position",
                    RegulatoryReference = "RG-08.1"
                },
                new KnowledgeGraphCatalogLineageInput
                {
                    RegulatorCode = "CBN",
                    ModuleCode = "RG08",
                    ReturnCode = "BDC_FXV",
                    TemplateName = "BDC FX Position",
                    FieldName = "Total Assets",
                    FieldCode = "total_assets",
                    RegulatoryReference = "RG-08.1"
                },
                new KnowledgeGraphCatalogLineageInput
                {
                    RegulatorCode = "CBN",
                    ModuleCode = "RG08",
                    ReturnCode = "BDC_FXV",
                    TemplateName = "BDC FX Position",
                    FieldName = "Capital Base",
                    FieldCode = "capital_base",
                    RegulatoryReference = "RG-08.2"
                }
            ],
            Obligations =
            [
                new KnowledgeGraphCatalogObligationInput
                {
                    LicenceType = "BDC",
                    RegulatorCode = "CBN",
                    ModuleCode = "RG08",
                    ReturnCode = "BDC_FXV",
                    Frequency = "Monthly",
                    NextDeadline = now
                }
            ],
            InstitutionObligations =
            [
                new KnowledgeGraphCatalogInstitutionObligationInput
                {
                    InstitutionKey = "11",
                    InstitutionName = "Sample Bureau De Change",
                    LicenceType = "BDC",
                    RegulatorCode = "CBN",
                    ReturnCode = "BDC_FXV",
                    Status = "Filed",
                    NextDeadline = now
                }
            ]
        };

        var result = await sut.MaterializeAsync(request);

        result.NodeCount.Should().Be(10);
        result.EdgeCount.Should().Be(15);
        result.NodeTypes.Should().ContainEquivalentOf(new KnowledgeGraphCatalogTypeCount("Field", 3));
        result.EdgeTypes.Should().ContainEquivalentOf(new KnowledgeGraphCatalogTypeCount("CAPTURED_BY", 3));

        var persistedNodes = await db.KnowledgeGraphNodes.AsNoTracking().ToListAsync();
        var persistedEdges = await db.KnowledgeGraphEdges.AsNoTracking().ToListAsync();

        persistedNodes.Should().HaveCount(10);
        persistedEdges.Should().HaveCount(15);
        persistedNodes.Should().Contain(x => x.NodeKey == "RETURN:BDCFXV" && x.NodeType == "Return");
        persistedEdges.Should().Contain(x =>
            x.EdgeType == "MUST_FILE"
            && x.SourceNodeKey == "LICENCE:BDC"
            && x.TargetNodeKey == "RETURN:BDCFXV");
    }

    [Fact]
    public async Task LoadAsync_Returns_Persisted_Node_And_Edge_State()
    {
        await using var db = CreateDb();
        var sut = new KnowledgeGraphCatalogService(db);

        var now = DateTime.UtcNow.Date.AddDays(7);
        await sut.MaterializeAsync(new KnowledgeGraphCatalogMaterializationRequest
        {
            Regulators =
            [
                new KnowledgeGraphCatalogRegulatorInput
                {
                    RegulatorCode = "CBN",
                    DisplayName = "Central Bank of Nigeria",
                    ModuleCount = 1,
                    RequirementCount = 1,
                    InstitutionCount = 1,
                    ModuleCodes = ["RG08"]
                }
            ],
            Requirements =
            [
                new KnowledgeGraphCatalogRequirementInput
                {
                    RegulatoryReference = "RG-08.1",
                    RegulationFamily = "RG-08",
                    RegulatorCode = "CBN",
                    ModuleCode = "RG08",
                    FiledViaReturns = ["BDC_FXV"],
                    InstitutionCount = 1,
                    FieldCount = 1,
                    FrequencyProfile = "Monthly",
                    NextDeadline = now
                }
            ],
            Lineage =
            [
                new KnowledgeGraphCatalogLineageInput
                {
                    RegulatorCode = "CBN",
                    ModuleCode = "RG08",
                    ReturnCode = "BDC_FXV",
                    TemplateName = "BDC FX Position",
                    FieldName = "Net Open Position",
                    FieldCode = "net_open_position",
                    RegulatoryReference = "RG-08.1"
                }
            ],
            Obligations =
            [
                new KnowledgeGraphCatalogObligationInput
                {
                    LicenceType = "BDC",
                    RegulatorCode = "CBN",
                    ModuleCode = "RG08",
                    ReturnCode = "BDC_FXV",
                    Frequency = "Monthly",
                    NextDeadline = now
                }
            ],
            InstitutionObligations =
            [
                new KnowledgeGraphCatalogInstitutionObligationInput
                {
                    InstitutionKey = "11",
                    InstitutionName = "Sample Bureau De Change",
                    LicenceType = "BDC",
                    RegulatorCode = "CBN",
                    ReturnCode = "BDC_FXV",
                    Status = "Filed",
                    NextDeadline = now
                }
            ]
        });

        var loaded = await sut.LoadAsync();

        loaded.MaterializedAt.Should().NotBeNull();
        loaded.NodeCount.Should().Be(7);
        loaded.EdgeCount.Should().Be(9);
        loaded.NodeTypes.Should().ContainEquivalentOf(new KnowledgeGraphCatalogTypeCount("Requirement", 1));
        loaded.EdgeTypes.Should().ContainEquivalentOf(new KnowledgeGraphCatalogTypeCount("CAPTURED_BY", 1));
        loaded.Nodes.Should().ContainSingle(x => x.NodeKey == "FIELD:BDCFXVNETOPENPOSITION" && x.NodeType == "Field");
        loaded.Edges.Should().ContainSingle(x =>
            x.EdgeType == "FILED_VIA"
            && x.SourceNodeKey == "REQUIREMENT:RG081"
            && x.TargetNodeKey == "RETURN:BDCFXV");
    }

    private static MetadataDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new MetadataDbContext(options);
    }
}
