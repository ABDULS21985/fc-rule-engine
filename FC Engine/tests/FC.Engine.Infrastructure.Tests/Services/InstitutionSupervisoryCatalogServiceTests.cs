using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Tests.Services;

public class InstitutionSupervisoryCatalogServiceTests
{
    [Fact]
    public async Task MaterializeAsync_Persists_Scorecards_And_Details_And_Loads_Them_Back()
    {
        await using var db = CreateDb();
        var sut = new InstitutionSupervisoryCatalogService(db);

        var input = new InstitutionSupervisoryCatalogInput
        {
            Scorecards =
            [
                new InstitutionSupervisoryScorecardInput
                {
                    InstitutionId = 11,
                    TenantId = Guid.NewGuid(),
                    InstitutionName = "Sample Bureau De Change Limited",
                    LicenceType = "BDC",
                    OverdueObligations = 2,
                    DueSoonObligations = 1,
                    CapitalScore = 58.4m,
                    OpenResilienceIncidents = 1,
                    OpenSecurityAlerts = 3,
                    ModelReviewItems = 2,
                    Priority = "Critical",
                    Summary = "Capital, filing, and cyber pressure remain elevated."
                }
            ],
            Details =
            [
                new InstitutionSupervisoryDetailInput
                {
                    InstitutionId = 11,
                    TenantId = Guid.NewGuid(),
                    InstitutionName = "Sample Bureau De Change Limited",
                    InstitutionCode = "BDC-011",
                    LicenceType = "BDC",
                    Priority = "Critical",
                    Summary = "Capital, filing, and cyber pressure remain elevated.",
                    CapitalScore = 58.4m,
                    CapitalAlert = "Capital buffer erosion breached watch threshold.",
                    OverdueObligations = 2,
                    DueSoonObligations = 1,
                    OpenResilienceIncidents = 1,
                    OpenSecurityAlerts = 3,
                    ModelReviewItems = 2,
                    TopObligationsJson = """[{"ReturnCode":"BDC_FXV","Status":"Overdue"}]""",
                    RecentSubmissionsJson = """[{"SubmissionId":1051,"ReturnCode":"BDC_FXV","Status":"Accepted","SubmittedAt":"2026-03-10T20:34:00Z"}]""",
                    RecentActivityJson = """[{"Domain":"Marketplace","Title":"Tenant modules reconciled","Detail":"OPS_RESILIENCE activated","HappenedAt":"2026-03-11T09:00:00Z","Severity":"Medium"}]"""
                }
            ]
        };

        var materialized = await sut.MaterializeAsync(input);
        var loaded = await sut.LoadAsync();

        materialized.MaterializedAt.Should().NotBeNull();
        materialized.Scorecards.Should().ContainSingle(x =>
            x.InstitutionId == 11
            && x.Priority == "Critical"
            && x.CapitalScore == 58.4m);
        materialized.Details.Should().ContainSingle(x =>
            x.InstitutionCode == "BDC-011"
            && x.TopObligationsJson.Contains("BDC_FXV", StringComparison.OrdinalIgnoreCase));

        loaded.MaterializedAt.Should().Be(materialized.MaterializedAt);
        loaded.Scorecards.Should().ContainSingle(x =>
            x.InstitutionName == "Sample Bureau De Change Limited"
            && x.OpenSecurityAlerts == 3);
        loaded.Details.Should().ContainSingle(x =>
            x.CapitalAlert.Contains("buffer erosion", StringComparison.OrdinalIgnoreCase)
            && x.RecentActivityJson.Contains("OPS_RESILIENCE", StringComparison.OrdinalIgnoreCase));

        (await db.InstitutionSupervisoryScorecards.AsNoTracking().CountAsync()).Should().Be(1);
        (await db.InstitutionSupervisoryDetails.AsNoTracking().CountAsync()).Should().Be(1);
    }

    private static MetadataDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MetadataDbContext(options);
    }
}
