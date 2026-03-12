using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Tests.Services;

public class MarketplaceRolloutCatalogServiceTests
{
    [Fact]
    public async Task MaterializeAsync_Persists_Rollout_Snapshot_And_Loads_It_Back()
    {
        await using var db = CreateDb();
        var sut = new MarketplaceRolloutCatalogService(db);

        var input = new MarketplaceRolloutCatalogInput
        {
            Modules =
            [
                new MarketplaceRolloutModuleInput
                {
                    ModuleCode = "OPS_RESILIENCE",
                    ModuleName = "Operational Resilience & ICT Risk",
                    EligibleTenants = 6,
                    ActiveEntitlements = 4,
                    PendingEntitlements = 2,
                    StaleTenants = 1,
                    IncludedBasePlans = 4,
                    AddOnPlans = 2,
                    AdoptionRatePercent = 66.7m,
                    Signal = "Watch",
                    Commentary = "Two tenants are still missing included-base activation.",
                    RecommendedAction = "Run reconciliation and confirm plan coverage."
                }
            ],
            PlanCoverage =
            [
                new MarketplaceRolloutPlanCoverageInput
                {
                    ModuleCode = "MODEL_RISK",
                    ModuleName = "RG-50 Model Risk",
                    PlanCode = "ENTERPRISE",
                    PlanName = "Enterprise",
                    CoverageMode = "Included",
                    EligibleTenants = 3,
                    ActiveEntitlements = 2,
                    PendingEntitlements = 1,
                    PriceMonthly = 0m,
                    PriceAnnual = 0m,
                    Signal = "Watch",
                    Commentary = "One enterprise tenant remains unreconciled."
                }
            ],
            ReconciliationQueue =
            [
                new MarketplaceRolloutQueueInput
                {
                    TenantId = Guid.NewGuid(),
                    TenantName = "Sample Financial Group",
                    PlanCode = "ENTERPRISE",
                    PlanName = "Enterprise",
                    PendingModuleCount = 2,
                    PendingModules = "OPS_RESILIENCE, MODEL_RISK",
                    State = "Stale",
                    Signal = "Critical",
                    LastEntitlementAction = "Modules Reconciled",
                    LastEntitlementActionAt = DateTime.UtcNow.AddDays(-9),
                    RecommendedAction = "Run reconciliation now."
                }
            ]
        };

        var materialized = await sut.MaterializeAsync(input);
        var loaded = await sut.LoadAsync();

        materialized.Modules.Should().ContainSingle(x => x.ModuleCode == "OPS_RESILIENCE" && x.PendingEntitlements == 2);
        materialized.PlanCoverage.Should().ContainSingle(x => x.PlanCode == "ENTERPRISE" && x.ModuleCode == "MODEL_RISK");
        materialized.ReconciliationQueue.Should().ContainSingle(x => x.State == "Stale" && x.PendingModuleCount == 2);

        loaded.Modules.Should().ContainSingle(x => x.ModuleCode == "OPS_RESILIENCE" && x.Signal == "Watch");
        loaded.PlanCoverage.Should().ContainSingle(x => x.PlanCode == "ENTERPRISE" && x.Signal == "Watch");
        loaded.ReconciliationQueue.Should().ContainSingle(x => x.TenantName == "Sample Financial Group" && x.Signal == "Critical");

        (await db.MarketplaceRolloutModules.AsNoTracking().CountAsync()).Should().Be(1);
        (await db.MarketplaceRolloutPlanCoverage.AsNoTracking().CountAsync()).Should().Be(1);
        (await db.MarketplaceRolloutReconciliationQueue.AsNoTracking().CountAsync()).Should().Be(1);
    }

    private static MetadataDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MetadataDbContext(options);
    }
}
