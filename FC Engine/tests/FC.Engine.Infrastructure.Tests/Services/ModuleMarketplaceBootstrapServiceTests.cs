using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FC.Engine.Infrastructure.Tests.Services;

public class ModuleMarketplaceBootstrapServiceTests
{
    [Fact]
    public async Task EnsurePricingAsync_Creates_Pricing_For_New_Modules_Across_Plans()
    {
        await using var db = CreateDbContext(nameof(EnsurePricingAsync_Creates_Pricing_For_New_Modules_Across_Plans));
        await SeedModulesAsync(db);
        await SeedPlansAsync(db);

        var sut = new ModuleMarketplaceBootstrapService(db, NullLogger<ModuleMarketplaceBootstrapService>.Instance);

        var result = await sut.EnsurePricingAsync();

        result.PricingCreated.Should().Be(12);
        result.PricingUpdated.Should().Be(0);

        var pricing = await db.PlanModulePricing
            .Include(x => x.Plan)
            .Include(x => x.Module)
            .ToListAsync();

        pricing.Should().ContainSingle(x => x.Plan!.PlanCode == "STARTER" && x.Module!.ModuleCode == "OPS_RESILIENCE" && x.PriceMonthly == 70000m && x.PriceAnnual == 700000m && !x.IsIncludedInBase);
        pricing.Should().ContainSingle(x => x.Plan!.PlanCode == "PROFESSIONAL" && x.Module!.ModuleCode == "MODEL_RISK" && x.PriceMonthly == 70000m && x.PriceAnnual == 700000m && !x.IsIncludedInBase);
        pricing.Should().ContainSingle(x => x.Plan!.PlanCode == "REGULATOR" && x.Module!.ModuleCode == "OPS_RESILIENCE" && x.PriceMonthly == 0m && x.IsIncludedInBase);
        pricing.Should().ContainSingle(x => x.Plan!.PlanCode == "WHITE_LABEL" && x.Module!.ModuleCode == "MODEL_RISK" && x.PriceMonthly == 0m && x.IsIncludedInBase);
    }

    [Fact]
    public async Task EnsurePricingAsync_Is_Idempotent_And_Repairs_Stale_Prices()
    {
        await using var db = CreateDbContext(nameof(EnsurePricingAsync_Is_Idempotent_And_Repairs_Stale_Prices));
        await SeedModulesAsync(db);
        await SeedPlansAsync(db);

        var starterId = await db.SubscriptionPlans.Where(x => x.PlanCode == "STARTER").Select(x => x.Id).SingleAsync();
        var opsModuleId = await db.Modules.Where(x => x.ModuleCode == "OPS_RESILIENCE").Select(x => x.Id).SingleAsync();

        db.PlanModulePricing.Add(new PlanModulePricing
        {
            PlanId = starterId,
            ModuleId = opsModuleId,
            PriceMonthly = 1m,
            PriceAnnual = 10m,
            IsIncludedInBase = true
        });
        await db.SaveChangesAsync();

        var sut = new ModuleMarketplaceBootstrapService(db, NullLogger<ModuleMarketplaceBootstrapService>.Instance);

        var first = await sut.EnsurePricingAsync();
        var second = await sut.EnsurePricingAsync();

        first.PricingCreated.Should().Be(11);
        first.PricingUpdated.Should().Be(1);
        second.PricingCreated.Should().Be(0);
        second.PricingUpdated.Should().Be(0);

        var repaired = await db.PlanModulePricing
            .Include(x => x.Plan)
            .Include(x => x.Module)
            .SingleAsync(x => x.Plan!.PlanCode == "STARTER" && x.Module!.ModuleCode == "OPS_RESILIENCE");

        repaired.PriceMonthly.Should().Be(70000m);
        repaired.PriceAnnual.Should().Be(700000m);
        repaired.IsIncludedInBase.Should().BeFalse();
    }

    private static async Task SeedModulesAsync(MetadataDbContext db)
    {
        db.Modules.AddRange(
            new Module
            {
                ModuleCode = "OPS_RESILIENCE",
                ModuleName = "Operational Resilience & ICT Risk",
                RegulatorCode = "CBN",
                SheetCount = 10,
                DefaultFrequency = "Quarterly",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new Module
            {
                ModuleCode = "MODEL_RISK",
                ModuleName = "Model Risk Management & Validation",
                RegulatorCode = "CBN",
                SheetCount = 9,
                DefaultFrequency = "Quarterly",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

        await db.SaveChangesAsync();
    }

    private static async Task SeedPlansAsync(MetadataDbContext db)
    {
        db.SubscriptionPlans.AddRange(
            new SubscriptionPlan { PlanCode = "STARTER", PlanName = "Starter", Tier = 1, MaxModules = 5, MaxUsersPerEntity = 10, MaxEntities = 1, BasePriceMonthly = 1m, BasePriceAnnual = 10m, IsActive = true },
            new SubscriptionPlan { PlanCode = "PROFESSIONAL", PlanName = "Professional", Tier = 3, MaxModules = 25, MaxUsersPerEntity = 25, MaxEntities = 5, BasePriceMonthly = 1m, BasePriceAnnual = 10m, IsActive = true },
            new SubscriptionPlan { PlanCode = "ENTERPRISE", PlanName = "Enterprise", Tier = 10, MaxModules = 100, MaxUsersPerEntity = 100, MaxEntities = 50, BasePriceMonthly = 1m, BasePriceAnnual = 10m, IsActive = true },
            new SubscriptionPlan { PlanCode = "GROUP", PlanName = "Group", Tier = 50, MaxModules = 200, MaxUsersPerEntity = 200, MaxEntities = 200, BasePriceMonthly = 1m, BasePriceAnnual = 10m, IsActive = true },
            new SubscriptionPlan { PlanCode = "REGULATOR", PlanName = "Regulator", Tier = 999, MaxModules = 999, MaxUsersPerEntity = 999, MaxEntities = 999, BasePriceMonthly = 0m, BasePriceAnnual = 0m, IsActive = true },
            new SubscriptionPlan { PlanCode = "WHITE_LABEL", PlanName = "White Label", Tier = 999, MaxModules = 999, MaxUsersPerEntity = 999, MaxEntities = 999, BasePriceMonthly = 0m, BasePriceAnnual = 0m, IsActive = true, Features = "[\"all_features\"]" });

        await db.SaveChangesAsync();
    }

    private static MetadataDbContext CreateDbContext(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new MetadataDbContext(options);
    }
}
