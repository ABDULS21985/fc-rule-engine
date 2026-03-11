using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FC.Engine.Infrastructure.Tests.Services;

public class SubscriptionModuleEntitlementBootstrapServiceTests
{
    [Fact]
    public async Task EnsureIncludedModulesAsync_Creates_Missing_Included_Modules_For_Entitled_Subscriptions()
    {
        await using var db = CreateDbContext(nameof(EnsureIncludedModulesAsync_Creates_Missing_Included_Modules_For_Entitled_Subscriptions));
        var seed = await SeedTenantSubscriptionAsync(db, "REGULATOR");

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

        var opsModuleId = await db.Modules.Where(x => x.ModuleCode == "OPS_RESILIENCE").Select(x => x.Id).SingleAsync();
        var riskModuleId = await db.Modules.Where(x => x.ModuleCode == "MODEL_RISK").Select(x => x.Id).SingleAsync();

        db.LicenceModuleMatrix.AddRange(
            new LicenceModuleMatrix { LicenceTypeId = seed.LicenceTypeId, ModuleId = opsModuleId, IsRequired = false, IsOptional = true },
            new LicenceModuleMatrix { LicenceTypeId = seed.LicenceTypeId, ModuleId = riskModuleId, IsRequired = false, IsOptional = true });

        db.PlanModulePricing.AddRange(
            new PlanModulePricing { PlanId = seed.PlanId, ModuleId = opsModuleId, PriceMonthly = 0m, PriceAnnual = 0m, IsIncludedInBase = true },
            new PlanModulePricing { PlanId = seed.PlanId, ModuleId = riskModuleId, PriceMonthly = 60000m, PriceAnnual = 600000m, IsIncludedInBase = false });
        await db.SaveChangesAsync();

        var entitlementService = new RecordingEntitlementService();
        var sut = new SubscriptionModuleEntitlementBootstrapService(
            db,
            entitlementService,
            NullLogger<SubscriptionModuleEntitlementBootstrapService>.Instance);

        var result = await sut.EnsureIncludedModulesAsync();

        result.ModulesCreated.Should().Be(1);
        result.ModulesReactivated.Should().Be(0);
        result.ModulesUpdated.Should().Be(0);
        result.ModulesDeactivated.Should().Be(0);
        result.TenantsTouched.Should().Be(1);

        var activeModules = await db.SubscriptionModules
            .Include(x => x.Module)
            .Where(x => x.SubscriptionId == seed.SubscriptionId)
            .ToListAsync();

        activeModules.Should().ContainSingle(x =>
            x.Module!.ModuleCode == "OPS_RESILIENCE"
            && x.IsActive
            && x.PriceMonthly == 0m
            && x.PriceAnnual == 0m);

        entitlementService.InvalidatedTenantIds.Should().ContainSingle().Which.Should().Be(seed.TenantId);
    }

    [Fact]
    public async Task EnsureIncludedModulesAsync_Reactivates_And_Repairs_Stale_Subscription_Modules_Idempotently()
    {
        await using var db = CreateDbContext(nameof(EnsureIncludedModulesAsync_Reactivates_And_Repairs_Stale_Subscription_Modules_Idempotently));
        var seed = await SeedTenantSubscriptionAsync(db, "WHITE_LABEL", allFeatures: true);

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

        var opsModuleId = await db.Modules.Where(x => x.ModuleCode == "OPS_RESILIENCE").Select(x => x.Id).SingleAsync();
        var riskModuleId = await db.Modules.Where(x => x.ModuleCode == "MODEL_RISK").Select(x => x.Id).SingleAsync();

        db.LicenceModuleMatrix.AddRange(
            new LicenceModuleMatrix { LicenceTypeId = seed.LicenceTypeId, ModuleId = opsModuleId, IsRequired = false, IsOptional = true },
            new LicenceModuleMatrix { LicenceTypeId = seed.LicenceTypeId, ModuleId = riskModuleId, IsRequired = false, IsOptional = true });

        db.PlanModulePricing.AddRange(
            new PlanModulePricing { PlanId = seed.PlanId, ModuleId = opsModuleId, PriceMonthly = 0m, PriceAnnual = 0m, IsIncludedInBase = true },
            new PlanModulePricing { PlanId = seed.PlanId, ModuleId = riskModuleId, PriceMonthly = 0m, PriceAnnual = 0m, IsIncludedInBase = true });

        db.SubscriptionModules.AddRange(
            new SubscriptionModule
            {
                SubscriptionId = seed.SubscriptionId,
                ModuleId = opsModuleId,
                PriceMonthly = 10m,
                PriceAnnual = 100m,
                IsActive = false,
                DeactivatedAt = DateTime.UtcNow.AddDays(-2)
            },
            new SubscriptionModule
            {
                SubscriptionId = seed.SubscriptionId,
                ModuleId = riskModuleId,
                PriceMonthly = 5m,
                PriceAnnual = 50m,
                IsActive = true,
                DeactivatedAt = DateTime.UtcNow.AddDays(-1)
            });
        await db.SaveChangesAsync();

        var entitlementService = new RecordingEntitlementService();
        var sut = new SubscriptionModuleEntitlementBootstrapService(
            db,
            entitlementService,
            NullLogger<SubscriptionModuleEntitlementBootstrapService>.Instance);

        var first = await sut.EnsureIncludedModulesAsync();
        var second = await sut.EnsureIncludedModulesAsync();

        first.ModulesCreated.Should().Be(0);
        first.ModulesReactivated.Should().Be(1);
        first.ModulesUpdated.Should().Be(1);
        first.ModulesDeactivated.Should().Be(0);
        first.TenantsTouched.Should().Be(1);

        second.ModulesCreated.Should().Be(0);
        second.ModulesReactivated.Should().Be(0);
        second.ModulesUpdated.Should().Be(0);
        second.ModulesDeactivated.Should().Be(0);
        second.TenantsTouched.Should().Be(0);

        var modules = await db.SubscriptionModules
            .Where(x => x.SubscriptionId == seed.SubscriptionId)
            .OrderBy(x => x.ModuleId)
            .ToListAsync();

        modules.Should().OnlyContain(x => x.IsActive);
        modules.Should().OnlyContain(x => x.PriceMonthly == 0m && x.PriceAnnual == 0m);
        modules.Should().OnlyContain(x => x.DeactivatedAt == null);
        entitlementService.InvalidatedTenantIds.Should().ContainSingle().Which.Should().Be(seed.TenantId);
    }

    [Fact]
    public async Task EnsureIncludedModulesForTenantAsync_Deactivates_Modules_That_Are_No_Longer_Licence_Eligible()
    {
        await using var db = CreateDbContext(nameof(EnsureIncludedModulesForTenantAsync_Deactivates_Modules_That_Are_No_Longer_Licence_Eligible));
        var seed = await SeedTenantSubscriptionAsync(db, "ENTERPRISE");

        db.Modules.Add(new Module
        {
            ModuleCode = "DMB_BASEL3",
            ModuleName = "DMB Basel III",
            RegulatorCode = "CBN",
            SheetCount = 5,
            DefaultFrequency = "Monthly",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var moduleId = await db.Modules.Where(x => x.ModuleCode == "DMB_BASEL3").Select(x => x.Id).SingleAsync();

        db.LicenceModuleMatrix.Add(new LicenceModuleMatrix
        {
            LicenceTypeId = seed.LicenceTypeId,
            ModuleId = moduleId,
            IsRequired = false,
            IsOptional = true
        });

        db.PlanModulePricing.Add(new PlanModulePricing
        {
            PlanId = seed.PlanId,
            ModuleId = moduleId,
            PriceMonthly = 50000m,
            PriceAnnual = 500000m,
            IsIncludedInBase = false
        });

        db.SubscriptionModules.Add(new SubscriptionModule
        {
            SubscriptionId = seed.SubscriptionId,
            ModuleId = moduleId,
            PriceMonthly = 50000m,
            PriceAnnual = 500000m,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var activeTenantLicence = await db.TenantLicenceTypes
            .SingleAsync(x => x.TenantId == seed.TenantId && x.LicenceTypeId == seed.LicenceTypeId);
        activeTenantLicence.IsActive = false;
        activeTenantLicence.ExpiryDate = DateTime.UtcNow.Date;
        await db.SaveChangesAsync();

        var entitlementService = new RecordingEntitlementService();
        var sut = new SubscriptionModuleEntitlementBootstrapService(
            db,
            entitlementService,
            NullLogger<SubscriptionModuleEntitlementBootstrapService>.Instance);

        var result = await sut.EnsureIncludedModulesForTenantAsync(seed.TenantId);

        result.ModulesCreated.Should().Be(0);
        result.ModulesReactivated.Should().Be(0);
        result.ModulesUpdated.Should().Be(0);
        result.ModulesDeactivated.Should().Be(1);
        result.TenantsTouched.Should().Be(1);

        var module = await db.SubscriptionModules
            .SingleAsync(x => x.SubscriptionId == seed.SubscriptionId && x.ModuleId == moduleId);

        module.IsActive.Should().BeFalse();
        module.DeactivatedAt.Should().NotBeNull();
        entitlementService.InvalidatedTenantIds.Should().ContainSingle().Which.Should().Be(seed.TenantId);
    }

    private static async Task<SeededSubscription> SeedTenantSubscriptionAsync(
        MetadataDbContext db,
        string planCode,
        bool allFeatures = false)
    {
        var tenant = Tenant.Create($"{planCode} Tenant", $"{planCode.ToLowerInvariant()}-tenant", TenantType.Institution, $"{planCode.ToLowerInvariant()}@example.com");
        tenant.Activate();
        db.Tenants.Add(tenant);

        var licenceType = new LicenceType
        {
            Code = $"{planCode}_LIC",
            Name = $"{planCode} Licence",
            Regulator = "CBN",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.LicenceTypes.Add(licenceType);

        var plan = new SubscriptionPlan
        {
            PlanCode = planCode,
            PlanName = planCode,
            Tier = 10,
            MaxModules = 100,
            MaxUsersPerEntity = 100,
            MaxEntities = 10,
            BasePriceMonthly = 0m,
            BasePriceAnnual = 0m,
            IsActive = true,
            Features = allFeatures ? "[\"all_features\"]" : null
        };
        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync();

        db.TenantLicenceTypes.Add(new TenantLicenceType
        {
            TenantId = tenant.TenantId,
            LicenceTypeId = licenceType.Id,
            EffectiveDate = DateTime.UtcNow,
            IsActive = true
        });

        var subscription = new Subscription
        {
            TenantId = tenant.TenantId,
            PlanId = plan.Id,
            BillingFrequency = BillingFrequency.Monthly,
            CurrentPeriodStart = DateTime.UtcNow,
            CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1)
        };
        subscription.Activate();
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();

        return new SeededSubscription(tenant.TenantId, licenceType.Id, plan.Id, subscription.Id);
    }

    private static MetadataDbContext CreateDbContext(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new MetadataDbContext(options);
    }

    private sealed record SeededSubscription(Guid TenantId, int LicenceTypeId, int PlanId, int SubscriptionId);

    private sealed class RecordingEntitlementService : IEntitlementService
    {
        public List<Guid> InvalidatedTenantIds { get; } = new();

        public Task<TenantEntitlement> ResolveEntitlements(Guid tenantId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> HasModuleAccess(Guid tenantId, string moduleCode, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> HasFeatureAccess(Guid tenantId, string featureCode, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task InvalidateCache(Guid tenantId)
        {
            InvalidatedTenantIds.Add(tenantId);
            return Task.CompletedTask;
        }
    }
}
