using FC.Engine.Admin.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FC.Engine.Admin.Tests.Services;

public class TenantManagementServiceTests
{
    [Fact]
    public async Task PreviewAssignLicenceAsync_Shows_Module_Activation_Without_Mutating_Subscriptions()
    {
        await using var db = CreateDbContext(nameof(PreviewAssignLicenceAsync_Shows_Module_Activation_Without_Mutating_Subscriptions));
        var seed = await SeedTenantSubscriptionAsync(db);

        var sut = CreateSut(db);

        var preview = await sut.PreviewAssignLicenceAsync(seed.TenantId, seed.LicenceTypeId);

        preview.Operation.Should().Be("Assign");
        preview.ModulesToActivate.Should().Be(1);
        preview.ModulesToReactivate.Should().Be(0);
        preview.ModulesToReprice.Should().Be(0);
        preview.ModulesToDeactivate.Should().Be(0);
        preview.Modules.Should().ContainSingle(x => x.ModuleCode == "OPS_RESILIENCE" && x.Action == "Activate");

        (await db.SubscriptionModules.CountAsync()).Should().Be(0);
        (await db.TenantLicenceTypes.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PreviewRemoveLicenceAsync_Shows_Module_Deactivation_Without_Mutating_Subscriptions()
    {
        await using var db = CreateDbContext(nameof(PreviewRemoveLicenceAsync_Shows_Module_Deactivation_Without_Mutating_Subscriptions));
        var seed = await SeedTenantSubscriptionAsync(db);

        db.TenantLicenceTypes.Add(new TenantLicenceType
        {
            TenantId = seed.TenantId,
            LicenceTypeId = seed.LicenceTypeId,
            RegistrationNumber = "RC-100",
            EffectiveDate = DateTime.UtcNow.Date,
            IsActive = true
        });

        db.SubscriptionModules.Add(new SubscriptionModule
        {
            SubscriptionId = seed.SubscriptionId,
            ModuleId = seed.ModuleId,
            PriceMonthly = 0m,
            PriceAnnual = 0m,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        var preview = await sut.PreviewRemoveLicenceAsync(seed.TenantId, seed.LicenceTypeId);

        preview.Operation.Should().Be("Remove");
        preview.ModulesToDeactivate.Should().Be(1);
        preview.ModulesToActivate.Should().Be(0);
        preview.Modules.Should().ContainSingle(x => x.ModuleCode == "OPS_RESILIENCE" && x.Action == "Deactivate");

        var storedModule = await db.SubscriptionModules.SingleAsync(x => x.SubscriptionId == seed.SubscriptionId && x.ModuleId == seed.ModuleId);
        storedModule.IsActive.Should().BeTrue();

        var storedLicence = await db.TenantLicenceTypes.SingleAsync(x => x.TenantId == seed.TenantId && x.LicenceTypeId == seed.LicenceTypeId);
        storedLicence.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task AssignLicenceAsync_Activates_Included_Modules_And_Returns_Reconciliation()
    {
        await using var db = CreateDbContext(nameof(AssignLicenceAsync_Activates_Included_Modules_And_Returns_Reconciliation));
        var seed = await SeedTenantSubscriptionAsync(db);

        var entitlementMock = new Mock<IEntitlementService>();
        entitlementMock.Setup(x => x.InvalidateCache(It.IsAny<Guid>())).Returns(Task.CompletedTask);

        var bootstrap = new SubscriptionModuleEntitlementBootstrapService(
            db,
            entitlementMock.Object,
            NullLogger<SubscriptionModuleEntitlementBootstrapService>.Instance);

        var sut = new TenantManagementService(
            db,
            Mock.Of<ITenantOnboardingService>(),
            entitlementMock.Object,
            bootstrap,
            new RecordingAuditLogger(),
            new PlatformAdminTenantContext());

        var result = await sut.AssignLicenceAsync(seed.TenantId, seed.LicenceTypeId, "RC-100");

        result.TenantLicence.IsActive.Should().BeTrue();
        result.TenantLicence.RegistrationNumber.Should().Be("RC-100");
        result.Reconciliation.ModulesCreated.Should().Be(1);
        result.Reconciliation.ModulesDeactivated.Should().Be(0);

        var activeModule = await db.SubscriptionModules
            .Include(x => x.Module)
            .SingleAsync(x => x.SubscriptionId == seed.SubscriptionId && x.IsActive);

        activeModule.Module!.ModuleCode.Should().Be("OPS_RESILIENCE");
        activeModule.PriceMonthly.Should().Be(0m);
        activeModule.PriceAnnual.Should().Be(0m);

        entitlementMock.Verify(x => x.InvalidateCache(seed.TenantId), Times.AtLeastOnce());
    }

    [Fact]
    public async Task RemoveLicenceAsync_Deactivates_No_Longer_Eligible_Modules_And_Returns_Reconciliation()
    {
        await using var db = CreateDbContext(nameof(RemoveLicenceAsync_Deactivates_No_Longer_Eligible_Modules_And_Returns_Reconciliation));
        var seed = await SeedTenantSubscriptionAsync(db);

        db.TenantLicenceTypes.Add(new TenantLicenceType
        {
            TenantId = seed.TenantId,
            LicenceTypeId = seed.LicenceTypeId,
            RegistrationNumber = "RC-100",
            EffectiveDate = DateTime.UtcNow.Date,
            IsActive = true
        });

        db.SubscriptionModules.Add(new SubscriptionModule
        {
            SubscriptionId = seed.SubscriptionId,
            ModuleId = seed.ModuleId,
            PriceMonthly = 0m,
            PriceAnnual = 0m,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var entitlementMock = new Mock<IEntitlementService>();
        entitlementMock.Setup(x => x.InvalidateCache(It.IsAny<Guid>())).Returns(Task.CompletedTask);

        var bootstrap = new SubscriptionModuleEntitlementBootstrapService(
            db,
            entitlementMock.Object,
            NullLogger<SubscriptionModuleEntitlementBootstrapService>.Instance);

        var sut = new TenantManagementService(
            db,
            Mock.Of<ITenantOnboardingService>(),
            entitlementMock.Object,
            bootstrap,
            new RecordingAuditLogger(),
            new PlatformAdminTenantContext());

        var result = await sut.RemoveLicenceAsync(seed.TenantId, seed.LicenceTypeId);

        result.TenantLicence.IsActive.Should().BeFalse();
        result.TenantLicence.ExpiryDate.Should().NotBeNull();
        result.Reconciliation.ModulesDeactivated.Should().Be(1);

        var module = await db.SubscriptionModules.SingleAsync(x => x.SubscriptionId == seed.SubscriptionId && x.ModuleId == seed.ModuleId);
        module.IsActive.Should().BeFalse();
        module.DeactivatedAt.Should().NotBeNull();

        entitlementMock.Verify(x => x.InvalidateCache(seed.TenantId), Times.AtLeastOnce());
    }

    [Fact]
    public async Task ReconcileTenantModulesAsync_Activates_Pending_Included_Modules()
    {
        await using var db = CreateDbContext(nameof(ReconcileTenantModulesAsync_Activates_Pending_Included_Modules));
        var seed = await SeedTenantSubscriptionAsync(db);

        db.TenantLicenceTypes.Add(new TenantLicenceType
        {
            TenantId = seed.TenantId,
            LicenceTypeId = seed.LicenceTypeId,
            RegistrationNumber = "RC-100",
            EffectiveDate = DateTime.UtcNow.Date,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var entitlementMock = new Mock<IEntitlementService>();
        entitlementMock.Setup(x => x.InvalidateCache(It.IsAny<Guid>())).Returns(Task.CompletedTask);

        var bootstrap = new SubscriptionModuleEntitlementBootstrapService(
            db,
            entitlementMock.Object,
            NullLogger<SubscriptionModuleEntitlementBootstrapService>.Instance);

        var sut = new TenantManagementService(
            db,
            Mock.Of<ITenantOnboardingService>(),
            entitlementMock.Object,
            bootstrap,
            new RecordingAuditLogger(),
            new PlatformAdminTenantContext());

        var result = await sut.ReconcileTenantModulesAsync(seed.TenantId);

        result.ModulesCreated.Should().Be(1);
        result.ModulesReactivated.Should().Be(0);
        result.ModulesUpdated.Should().Be(0);
        result.ModulesDeactivated.Should().Be(0);
        result.TenantsTouched.Should().Be(1);

        var activeModule = await db.SubscriptionModules
            .Include(x => x.Module)
            .SingleAsync(x => x.SubscriptionId == seed.SubscriptionId && x.ModuleId == seed.ModuleId);

        activeModule.IsActive.Should().BeTrue();
        activeModule.Module!.ModuleCode.Should().Be("OPS_RESILIENCE");

        entitlementMock.Verify(x => x.InvalidateCache(seed.TenantId), Times.AtLeastOnce());
    }

    [Fact]
    public async Task ReconcileTenantModulesAsync_For_Multiple_Tenants_Aggregates_Results()
    {
        await using var db = CreateDbContext(nameof(ReconcileTenantModulesAsync_For_Multiple_Tenants_Aggregates_Results));
        var seedA = await SeedTenantSubscriptionAsync(db, "tenant-a", "OPS_A", "OPS_RESILIENCE_A");
        var seedB = await SeedTenantSubscriptionAsync(db, "tenant-b", "OPS_B", "OPS_RESILIENCE_B");

        db.TenantLicenceTypes.AddRange(
            new TenantLicenceType
            {
                TenantId = seedA.TenantId,
                LicenceTypeId = seedA.LicenceTypeId,
                RegistrationNumber = "RC-A",
                EffectiveDate = DateTime.UtcNow.Date,
                IsActive = true
            },
            new TenantLicenceType
            {
                TenantId = seedB.TenantId,
                LicenceTypeId = seedB.LicenceTypeId,
                RegistrationNumber = "RC-B",
                EffectiveDate = DateTime.UtcNow.Date,
                IsActive = true
            });
        await db.SaveChangesAsync();

        var entitlementMock = new Mock<IEntitlementService>();
        entitlementMock.Setup(x => x.InvalidateCache(It.IsAny<Guid>())).Returns(Task.CompletedTask);

        var bootstrap = new SubscriptionModuleEntitlementBootstrapService(
            db,
            entitlementMock.Object,
            NullLogger<SubscriptionModuleEntitlementBootstrapService>.Instance);

        var sut = new TenantManagementService(
            db,
            Mock.Of<ITenantOnboardingService>(),
            entitlementMock.Object,
            bootstrap,
            new RecordingAuditLogger(),
            new PlatformAdminTenantContext());

        var result = await sut.ReconcileTenantModulesAsync(new[] { seedA.TenantId, seedB.TenantId });

        result.RequestedTenants.Should().Be(2);
        result.ProcessedTenants.Should().Be(2);
        result.Reconciliation.ModulesCreated.Should().Be(2);
        result.Reconciliation.ModulesReactivated.Should().Be(0);
        result.Reconciliation.ModulesUpdated.Should().Be(0);
        result.Reconciliation.ModulesDeactivated.Should().Be(0);
        result.Reconciliation.TenantsTouched.Should().Be(2);

        (await db.SubscriptionModules.CountAsync()).Should().Be(2);
        entitlementMock.Verify(x => x.InvalidateCache(seedA.TenantId), Times.AtLeastOnce());
        entitlementMock.Verify(x => x.InvalidateCache(seedB.TenantId), Times.AtLeastOnce());
    }

    private static TenantManagementService CreateSut(MetadataDbContext db)
    {
        var entitlementMock = new Mock<IEntitlementService>();
        entitlementMock.Setup(x => x.InvalidateCache(It.IsAny<Guid>())).Returns(Task.CompletedTask);

        var bootstrap = new SubscriptionModuleEntitlementBootstrapService(
            db,
            entitlementMock.Object,
            NullLogger<SubscriptionModuleEntitlementBootstrapService>.Instance);

        return new TenantManagementService(
            db,
            Mock.Of<ITenantOnboardingService>(),
            entitlementMock.Object,
            bootstrap,
            new RecordingAuditLogger(),
            new PlatformAdminTenantContext());
    }

    private static async Task<SeededTenantData> SeedTenantSubscriptionAsync(
        MetadataDbContext db,
        string tenantSlug = "tenant-alpha",
        string licenceCode = "OPS",
        string moduleCode = "OPS_RESILIENCE")
    {
        var suffix = licenceCode.Replace("_", "-", StringComparison.Ordinal);
        var tenant = Tenant.Create($"Tenant {suffix}", tenantSlug, TenantType.Institution, $"{tenantSlug}@example.com");
        tenant.Activate();
        db.Tenants.Add(tenant);

        var licenceType = new LicenceType
        {
            Code = licenceCode,
            Name = $"Operational Resilience {suffix}",
            Regulator = "CBN",
            IsActive = true,
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow
        };
        db.LicenceTypes.Add(licenceType);

        var module = new Module
        {
            ModuleCode = moduleCode,
            ModuleName = $"Operational Resilience & ICT Risk {suffix}",
            RegulatorCode = "CBN",
            SheetCount = 10,
            DefaultFrequency = "Quarterly",
            IsActive = true,
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow
        };
        db.Modules.Add(module);

        var plan = new SubscriptionPlan
        {
            PlanCode = "REGULATOR",
            PlanName = "Regulator",
            Tier = 100,
            MaxModules = 999,
            MaxUsersPerEntity = 999,
            MaxEntities = 999,
            BasePriceMonthly = 0m,
            BasePriceAnnual = 0m,
            IsActive = true,
            Features = "[\"all_features\"]"
        };
        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync();

        db.LicenceModuleMatrix.Add(new LicenceModuleMatrix
        {
            LicenceTypeId = licenceType.Id,
            ModuleId = module.Id,
            IsRequired = false,
            IsOptional = true
        });

        db.PlanModulePricing.Add(new PlanModulePricing
        {
            PlanId = plan.Id,
            ModuleId = module.Id,
            PriceMonthly = 0m,
            PriceAnnual = 0m,
            IsIncludedInBase = true
        });

        var subscription = new Subscription
        {
            TenantId = tenant.TenantId,
            PlanId = plan.Id,
            BillingFrequency = BillingFrequency.Monthly,
            CurrentPeriodStart = DateTime.UtcNow.Date,
            CurrentPeriodEnd = DateTime.UtcNow.Date.AddMonths(1)
        };
        subscription.Activate();
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();

        return new SeededTenantData(tenant.TenantId, licenceType.Id, module.Id, subscription.Id);
    }

    private static MetadataDbContext CreateDbContext(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new MetadataDbContext(options);
    }

    private sealed record SeededTenantData(Guid TenantId, int LicenceTypeId, int ModuleId, int SubscriptionId);

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public Task Log(string entityType, int entityId, string action, object? oldValues, object? newValues, string performedBy, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class PlatformAdminTenantContext : ITenantContext
    {
        public Guid? CurrentTenantId => null;
        public bool IsPlatformAdmin => true;
        public Guid? ImpersonatingTenantId => null;
    }
}
