using FC.Engine.Admin.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Text.Json;
using Xunit;

namespace FC.Engine.Admin.Tests.Services;

public class PlatformAdminServiceTests
{
    [Fact]
    public async Task GetTenantList_And_Detail_Surface_Entitlement_Audit_Activity()
    {
        await using var db = CreateDbContext(nameof(GetTenantList_And_Detail_Surface_Entitlement_Audit_Activity));

        var tenant = Tenant.Create("Tenant Gamma", "tenant-gamma", TenantType.Institution, "gamma@example.com");
        tenant.Activate();
        db.Tenants.Add(tenant);

        var plan = new SubscriptionPlan
        {
            PlanCode = "ENTERPRISE",
            PlanName = "Enterprise",
            Tier = 3,
            MaxModules = 50,
            MaxUsersPerEntity = 100,
            MaxEntities = 20,
            BasePriceMonthly = 100000m,
            BasePriceAnnual = 1000000m,
            IsActive = true,
            Features = "[\"all_features\"]"
        };
        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync();

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

        db.AuditLog.AddRange(
            new AuditLogEntry
            {
                TenantId = null,
                EntityType = "Tenant",
                EntityId = 0,
                Action = "TenantLicenceAssigned",
                NewValues = JsonSerializer.Serialize(new { TenantId = tenant.TenantId }),
                PerformedBy = "platform-admin",
                PerformedAt = new DateTime(2026, 3, 11, 8, 0, 0, DateTimeKind.Utc),
                Hash = "hash-1",
                PreviousHash = "GENESIS",
                SequenceNumber = 1
            },
            new AuditLogEntry
            {
                TenantId = null,
                EntityType = "Tenant",
                EntityId = 0,
                Action = "TenantModulesReconciled",
                NewValues = JsonSerializer.Serialize(new { TenantId = tenant.TenantId }),
                PerformedBy = "platform-admin",
                PerformedAt = new DateTime(2026, 3, 11, 9, 30, 0, DateTimeKind.Utc),
                Hash = "hash-2",
                PreviousHash = "hash-1",
                SequenceNumber = 2
            });
        await db.SaveChangesAsync();

        var dashboardMock = new Mock<IDashboardService>();
        dashboardMock
            .Setup(x => x.GetAdminDashboard(tenant.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminDashboardData
            {
                Usage = new SubscriptionUsageMetrics(),
                Billing = new BillingSummaryMetrics()
            });

        var sut = new PlatformAdminService(
            db,
            dashboardMock.Object,
            Mock.Of<ISubscriptionService>(),
            Mock.Of<ITemplateRepository>(),
            null!,
            Mock.Of<IAuditLogger>(),
            Mock.Of<INotificationOrchestrator>(),
            Mock.Of<IFeatureFlagService>());

        var list = await sut.GetTenantList(new PlatformTenantListQuery());
        var detail = await sut.GetTenantDetail(tenant.TenantId);

        var listRow = list.Tenants.Single(x => x.TenantId == tenant.TenantId);
        listRow.LastEntitlementAction.Should().Be("Modules Reconciled");
        listRow.LastEntitlementActionAt.Should().Be(new DateTime(2026, 3, 11, 9, 30, 0, DateTimeKind.Utc));

        detail.Should().NotBeNull();
        detail!.LastReconciledAt.Should().Be(new DateTime(2026, 3, 11, 9, 30, 0, DateTimeKind.Utc));
        detail.EntitlementActivity.Should().HaveCount(2);
        detail.EntitlementActivity[0].Action.Should().Be("Modules Reconciled");
        detail.EntitlementActivity[1].Action.Should().Be("Licence Assigned");
    }

    [Fact]
    public async Task GetTenantList_Computes_And_Filters_Pending_Reconciliation_Modules()
    {
        await using var db = CreateDbContext(nameof(GetTenantList_Computes_And_Filters_Pending_Reconciliation_Modules));

        var healthyTenant = Tenant.Create("Healthy Tenant", "healthy-tenant", TenantType.Institution, "healthy@example.com");
        healthyTenant.Activate();
        var driftedTenant = Tenant.Create("Drifted Tenant", "drifted-tenant", TenantType.Institution, "drifted@example.com");
        driftedTenant.Activate();
        db.Tenants.AddRange(healthyTenant, driftedTenant);

        var plan = new SubscriptionPlan
        {
            PlanCode = "ENTERPRISE",
            PlanName = "Enterprise",
            Tier = 3,
            MaxModules = 50,
            MaxUsersPerEntity = 100,
            MaxEntities = 20,
            BasePriceMonthly = 100000m,
            BasePriceAnnual = 1000000m,
            IsActive = true,
            Features = "[\"all_features\"]"
        };
        db.SubscriptionPlans.Add(plan);

        var licenceType = new LicenceType
        {
            Code = "OPS",
            Name = "Operational Resilience",
            Regulator = "CBN",
            IsActive = true,
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow
        };
        db.LicenceTypes.Add(licenceType);

        var activeModule = new Module
        {
            ModuleCode = "OPS_RESILIENCE",
            ModuleName = "Operational Resilience & ICT Risk",
            RegulatorCode = "CBN",
            DefaultFrequency = "Quarterly",
            SheetCount = 10,
            IsActive = true,
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow
        };

        var pendingModule = new Module
        {
            ModuleCode = "MODEL_RISK",
            ModuleName = "Model Risk Management",
            RegulatorCode = "CBN",
            DefaultFrequency = "Quarterly",
            SheetCount = 9,
            IsActive = true,
            DisplayOrder = 2,
            CreatedAt = DateTime.UtcNow
        };

        db.Modules.AddRange(activeModule, pendingModule);
        await db.SaveChangesAsync();

        db.TenantLicenceTypes.AddRange(
            new TenantLicenceType
            {
                TenantId = healthyTenant.TenantId,
                LicenceTypeId = licenceType.Id,
                RegistrationNumber = "HT-001",
                EffectiveDate = DateTime.UtcNow.Date,
                IsActive = true
            },
            new TenantLicenceType
            {
                TenantId = driftedTenant.TenantId,
                LicenceTypeId = licenceType.Id,
                RegistrationNumber = "DT-001",
                EffectiveDate = DateTime.UtcNow.Date,
                IsActive = true
            });

        db.LicenceModuleMatrix.AddRange(
            new LicenceModuleMatrix
            {
                LicenceTypeId = licenceType.Id,
                ModuleId = activeModule.Id,
                IsRequired = true,
                IsOptional = false
            },
            new LicenceModuleMatrix
            {
                LicenceTypeId = licenceType.Id,
                ModuleId = pendingModule.Id,
                IsRequired = false,
                IsOptional = true
            });

        db.PlanModulePricing.AddRange(
            new PlanModulePricing
            {
                PlanId = plan.Id,
                ModuleId = activeModule.Id,
                PriceMonthly = 0m,
                PriceAnnual = 0m,
                IsIncludedInBase = true
            },
            new PlanModulePricing
            {
                PlanId = plan.Id,
                ModuleId = pendingModule.Id,
                PriceMonthly = 0m,
                PriceAnnual = 0m,
                IsIncludedInBase = true
            });

        var healthySubscription = new Subscription
        {
            TenantId = healthyTenant.TenantId,
            PlanId = plan.Id,
            BillingFrequency = BillingFrequency.Monthly,
            CurrentPeriodStart = DateTime.UtcNow.Date,
            CurrentPeriodEnd = DateTime.UtcNow.Date.AddMonths(1)
        };
        healthySubscription.Activate();

        var driftedSubscription = new Subscription
        {
            TenantId = driftedTenant.TenantId,
            PlanId = plan.Id,
            BillingFrequency = BillingFrequency.Monthly,
            CurrentPeriodStart = DateTime.UtcNow.Date,
            CurrentPeriodEnd = DateTime.UtcNow.Date.AddMonths(1)
        };
        driftedSubscription.Activate();

        db.Subscriptions.AddRange(healthySubscription, driftedSubscription);
        await db.SaveChangesAsync();

        db.SubscriptionModules.AddRange(
            new SubscriptionModule
            {
                SubscriptionId = healthySubscription.Id,
                ModuleId = activeModule.Id,
                PriceMonthly = 0m,
                PriceAnnual = 0m,
                IsActive = true
            },
            new SubscriptionModule
            {
                SubscriptionId = healthySubscription.Id,
                ModuleId = pendingModule.Id,
                PriceMonthly = 0m,
                PriceAnnual = 0m,
                IsActive = true
            },
            new SubscriptionModule
            {
                SubscriptionId = driftedSubscription.Id,
                ModuleId = activeModule.Id,
                PriceMonthly = 0m,
                PriceAnnual = 0m,
                IsActive = true
            });
        await db.SaveChangesAsync();

        var dashboardMock = new Mock<IDashboardService>();
        dashboardMock
            .Setup(x => x.GetAdminDashboard(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminDashboardData
            {
                Usage = new SubscriptionUsageMetrics(),
                Billing = new BillingSummaryMetrics()
            });

        var sut = new PlatformAdminService(
            db,
            dashboardMock.Object,
            Mock.Of<ISubscriptionService>(),
            Mock.Of<ITemplateRepository>(),
            null!,
            Mock.Of<IAuditLogger>(),
            Mock.Of<INotificationOrchestrator>(),
            Mock.Of<IFeatureFlagService>());

        var allRows = await sut.GetTenantList(new PlatformTenantListQuery());

        allRows.Tenants.Should().HaveCount(2);
        allRows.Tenants.Single(x => x.TenantId == healthyTenant.TenantId).PendingReconciliationModules.Should().Be(0);
        allRows.Tenants.Single(x => x.TenantId == driftedTenant.TenantId).PendingReconciliationModules.Should().Be(1);

        var filteredRows = await sut.GetTenantList(new PlatformTenantListQuery { OnlyNeedsReconciliation = true });

        filteredRows.Tenants.Should().ContainSingle();
        filteredRows.Tenants[0].TenantId.Should().Be(driftedTenant.TenantId);
        filteredRows.Tenants[0].PendingReconciliationModules.Should().Be(1);
    }

    [Fact]
    public async Task GetTenantDetail_Returns_Module_Entitlement_Register_With_Reconciliation_Gaps()
    {
        await using var db = CreateDbContext(nameof(GetTenantDetail_Returns_Module_Entitlement_Register_With_Reconciliation_Gaps));

        var tenant = Tenant.Create("Tenant Beta", "tenant-beta", TenantType.Institution, "beta@example.com");
        tenant.Activate();
        db.Tenants.Add(tenant);

        var plan = new SubscriptionPlan
        {
            PlanCode = "ENTERPRISE",
            PlanName = "Enterprise",
            Tier = 3,
            MaxModules = 50,
            MaxUsersPerEntity = 100,
            MaxEntities = 20,
            BasePriceMonthly = 100000m,
            BasePriceAnnual = 1000000m,
            IsActive = true,
            Features = "[\"all_features\"]"
        };
        db.SubscriptionPlans.Add(plan);

        var licenceType = new LicenceType
        {
            Code = "OPS",
            Name = "Operational Resilience",
            Regulator = "CBN",
            IsActive = true,
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow
        };
        db.LicenceTypes.Add(licenceType);

        var activeModule = new Module
        {
            ModuleCode = "OPS_RESILIENCE",
            ModuleName = "Operational Resilience & ICT Risk",
            RegulatorCode = "CBN",
            DefaultFrequency = "Quarterly",
            SheetCount = 10,
            IsActive = true,
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow
        };

        var pendingModule = new Module
        {
            ModuleCode = "MODEL_RISK",
            ModuleName = "Model Risk Management",
            RegulatorCode = "CBN",
            DefaultFrequency = "Quarterly",
            SheetCount = 9,
            IsActive = true,
            DisplayOrder = 2,
            CreatedAt = DateTime.UtcNow
        };

        db.Modules.AddRange(activeModule, pendingModule);
        await db.SaveChangesAsync();

        db.TenantLicenceTypes.Add(new TenantLicenceType
        {
            TenantId = tenant.TenantId,
            LicenceTypeId = licenceType.Id,
            RegistrationNumber = "OPS-001",
            EffectiveDate = DateTime.UtcNow.Date,
            IsActive = true
        });

        db.LicenceModuleMatrix.AddRange(
            new LicenceModuleMatrix
            {
                LicenceTypeId = licenceType.Id,
                ModuleId = activeModule.Id,
                IsRequired = true,
                IsOptional = false
            },
            new LicenceModuleMatrix
            {
                LicenceTypeId = licenceType.Id,
                ModuleId = pendingModule.Id,
                IsRequired = false,
                IsOptional = true
            });

        db.PlanModulePricing.AddRange(
            new PlanModulePricing
            {
                PlanId = plan.Id,
                ModuleId = activeModule.Id,
                PriceMonthly = 0m,
                PriceAnnual = 0m,
                IsIncludedInBase = true
            },
            new PlanModulePricing
            {
                PlanId = plan.Id,
                ModuleId = pendingModule.Id,
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

        db.SubscriptionModules.Add(new SubscriptionModule
        {
            SubscriptionId = subscription.Id,
            ModuleId = activeModule.Id,
            PriceMonthly = 0m,
            PriceAnnual = 0m,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var dashboardMock = new Mock<IDashboardService>();
        dashboardMock
            .Setup(x => x.GetAdminDashboard(tenant.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminDashboardData
            {
                Usage = new SubscriptionUsageMetrics(),
                Billing = new BillingSummaryMetrics()
            });

        var sut = new PlatformAdminService(
            db,
            dashboardMock.Object,
            Mock.Of<ISubscriptionService>(),
            Mock.Of<ITemplateRepository>(),
            null!,
            Mock.Of<IAuditLogger>(),
            Mock.Of<INotificationOrchestrator>(),
            Mock.Of<IFeatureFlagService>());

        var result = await sut.GetTenantDetail(tenant.TenantId);

        result.Should().NotBeNull();
        result!.CurrentModuleEntitlements.Should().HaveCount(2);

        var activeRow = result.CurrentModuleEntitlements.Single(x => x.ModuleCode == "OPS_RESILIENCE");
        activeRow.Status.Should().Be("Active");
        activeRow.IsActive.Should().BeTrue();
        activeRow.IsIncludedInBase.Should().BeTrue();
        activeRow.IsLicenceEligible.Should().BeTrue();
        activeRow.Coverage.Should().Contain("OPS (Required)");

        var pendingRow = result.CurrentModuleEntitlements.Single(x => x.ModuleCode == "MODEL_RISK");
        pendingRow.Status.Should().Be("Pending Reconciliation");
        pendingRow.IsActive.Should().BeFalse();
        pendingRow.IsIncludedInBase.Should().BeTrue();
        pendingRow.IsLicenceEligible.Should().BeTrue();
        pendingRow.NextAction.Should().Contain("Run entitlement reconciliation");
        pendingRow.Coverage.Should().Contain("OPS (Optional)");
    }

    private static MetadataDbContext CreateDbContext(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new MetadataDbContext(options);
    }
}
