using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.BackgroundJobs;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FC.Engine.Infrastructure.Tests.Services;

public class SubscriptionServiceTests
{
    private static MetadataDbContext CreateDbContext(string? databaseName = null)
    {
        var dbName = string.IsNullOrWhiteSpace(databaseName) ? Guid.NewGuid().ToString() : databaseName;
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new MetadataDbContext(options);
    }

    private static Tenant CreateTenant(MetadataDbContext db, string slug)
    {
        var tenant = Tenant.Create($"Tenant {slug}", slug, TenantType.Institution, $"{slug}@mail.test");
        tenant.Activate();
        db.Tenants.Add(tenant);
        db.SaveChanges();
        return tenant;
    }

    private static Tenant CreatePartnerTenant(MetadataDbContext db, string slug)
    {
        var tenant = Tenant.Create($"Partner {slug}", slug, TenantType.WhiteLabelPartner, $"{slug}@partner.test");
        tenant.Activate();
        db.Tenants.Add(tenant);
        db.SaveChanges();
        return tenant;
    }

    private static void SeedPlansAndModules(MetadataDbContext db)
    {
        var starter = new SubscriptionPlan
        {
            PlanCode = "STARTER",
            PlanName = "Starter",
            Tier = 1,
            MaxModules = 2,
            MaxUsersPerEntity = 10,
            MaxEntities = 1,
            MaxApiCallsPerMonth = 0,
            MaxStorageMb = 500,
            BasePriceMonthly = 150000,
            BasePriceAnnual = 1500000,
            TrialDays = 14,
            Features = "[\"dashboard_basic\"]",
            IsActive = true,
            DisplayOrder = 1
        };

        var enterprise = new SubscriptionPlan
        {
            PlanCode = "ENTERPRISE",
            PlanName = "Enterprise",
            Tier = 3,
            MaxModules = 10,
            MaxUsersPerEntity = 50,
            MaxEntities = 10,
            MaxApiCallsPerMonth = 1000,
            MaxStorageMb = 5000,
            BasePriceMonthly = 750000,
            BasePriceAnnual = 7500000,
            TrialDays = 14,
            Features = "[\"all_features\"]",
            IsActive = true,
            DisplayOrder = 2
        };

        db.SubscriptionPlans.AddRange(starter, enterprise);

        var modules = new[]
        {
            new Module { ModuleCode = "FC_RETURNS", ModuleName = "FC Returns", RegulatorCode = "CBN", IsActive = true, DisplayOrder = 1, DefaultFrequency = "Monthly", SheetCount = 10 },
            new Module { ModuleCode = "BDC_CBN", ModuleName = "BDC", RegulatorCode = "CBN", IsActive = true, DisplayOrder = 2, DefaultFrequency = "Monthly", SheetCount = 10 },
            new Module { ModuleCode = "NDIC_RETURNS", ModuleName = "NDIC", RegulatorCode = "NDIC", IsActive = true, DisplayOrder = 3, DefaultFrequency = "Quarterly", SheetCount = 10 },
            new Module { ModuleCode = "NFIU_AML", ModuleName = "NFIU", RegulatorCode = "NFIU", IsActive = true, DisplayOrder = 4, DefaultFrequency = "Monthly", SheetCount = 10 },
            new Module { ModuleCode = "DMB_BASEL3", ModuleName = "DMB", RegulatorCode = "CBN", IsActive = true, DisplayOrder = 5, DefaultFrequency = "Monthly", SheetCount = 10 }
        };

        db.Modules.AddRange(modules);

        var fc = new LicenceType { Code = "FC", Name = "Finance", Regulator = "CBN", IsActive = true, DisplayOrder = 1, CreatedAt = DateTime.UtcNow };
        var bdc = new LicenceType { Code = "BDC", Name = "BDC", Regulator = "CBN", IsActive = true, DisplayOrder = 2, CreatedAt = DateTime.UtcNow };
        db.LicenceTypes.AddRange(fc, bdc);
        db.SaveChanges();

        var moduleLookup = db.Modules.ToDictionary(m => m.ModuleCode, m => m.Id);

        db.LicenceModuleMatrix.AddRange(
            new LicenceModuleMatrix { LicenceTypeId = fc.Id, ModuleId = moduleLookup["FC_RETURNS"], IsRequired = true, IsOptional = false },
            new LicenceModuleMatrix { LicenceTypeId = fc.Id, ModuleId = moduleLookup["NDIC_RETURNS"], IsRequired = true, IsOptional = false },
            new LicenceModuleMatrix { LicenceTypeId = fc.Id, ModuleId = moduleLookup["NFIU_AML"], IsRequired = true, IsOptional = false },
            new LicenceModuleMatrix { LicenceTypeId = fc.Id, ModuleId = moduleLookup["DMB_BASEL3"], IsRequired = false, IsOptional = true },
            new LicenceModuleMatrix { LicenceTypeId = bdc.Id, ModuleId = moduleLookup["BDC_CBN"], IsRequired = true, IsOptional = false }
        );

        var starterId = db.SubscriptionPlans.Single(p => p.PlanCode == "STARTER").Id;
        var enterpriseId = db.SubscriptionPlans.Single(p => p.PlanCode == "ENTERPRISE").Id;

        db.PlanModulePricing.AddRange(
            new PlanModulePricing { PlanId = starterId, ModuleId = moduleLookup["FC_RETURNS"], PriceMonthly = 0, PriceAnnual = 0, IsIncludedInBase = true },
            new PlanModulePricing { PlanId = starterId, ModuleId = moduleLookup["BDC_CBN"], PriceMonthly = 50000, PriceAnnual = 500000, IsIncludedInBase = false },
            new PlanModulePricing { PlanId = starterId, ModuleId = moduleLookup["NDIC_RETURNS"], PriceMonthly = 40000, PriceAnnual = 400000, IsIncludedInBase = false },
            new PlanModulePricing { PlanId = starterId, ModuleId = moduleLookup["NFIU_AML"], PriceMonthly = 40000, PriceAnnual = 400000, IsIncludedInBase = false },

            new PlanModulePricing { PlanId = enterpriseId, ModuleId = moduleLookup["FC_RETURNS"], PriceMonthly = 0, PriceAnnual = 0, IsIncludedInBase = true },
            new PlanModulePricing { PlanId = enterpriseId, ModuleId = moduleLookup["BDC_CBN"], PriceMonthly = 30000, PriceAnnual = 300000, IsIncludedInBase = false },
            new PlanModulePricing { PlanId = enterpriseId, ModuleId = moduleLookup["NDIC_RETURNS"], PriceMonthly = 25000, PriceAnnual = 250000, IsIncludedInBase = false },
            new PlanModulePricing { PlanId = enterpriseId, ModuleId = moduleLookup["NFIU_AML"], PriceMonthly = 25000, PriceAnnual = 250000, IsIncludedInBase = false },
            new PlanModulePricing { PlanId = enterpriseId, ModuleId = moduleLookup["DMB_BASEL3"], PriceMonthly = 100000, PriceAnnual = 1000000, IsIncludedInBase = false }
        );

        db.SaveChanges();
    }

    private static void AssignLicence(MetadataDbContext db, Guid tenantId, string code)
    {
        var licence = db.LicenceTypes.Single(l => l.Code == code);
        db.TenantLicenceTypes.Add(new TenantLicenceType
        {
            TenantId = tenantId,
            LicenceTypeId = licence.Id,
            EffectiveDate = DateTime.UtcNow.Date,
            IsActive = true
        });
        db.SaveChanges();
    }

    private static SubscriptionService CreateSut(
        MetadataDbContext db,
        Mock<IEntitlementService> entitlementMock,
        SubscriptionModuleEntitlementBootstrapService? entitlementBootstrapService = null)
    {
        entitlementMock
            .Setup(x => x.InvalidateCache(It.IsAny<Guid>()))
            .Returns(Task.CompletedTask);

        return new SubscriptionService(
            db,
            entitlementMock.Object,
            NullLogger<SubscriptionService>.Instance,
            null,
            null,
            entitlementBootstrapService);
    }

    [Fact]
    public async Task Starter_Limits_To_2_Modules()
    {
        using var db = CreateDbContext();
        SeedPlansAndModules(db);
        var tenant = CreateTenant(db, "starter-limit");
        AssignLicence(db, tenant.TenantId, "FC");
        AssignLicence(db, tenant.TenantId, "BDC");

        var ent = new Mock<IEntitlementService>();
        var sut = CreateSut(db, ent);
        await sut.CreateSubscription(tenant.TenantId, "STARTER", BillingFrequency.Monthly);

        await sut.ActivateModule(tenant.TenantId, "FC_RETURNS");
        await sut.ActivateModule(tenant.TenantId, "BDC_CBN");

        var act = () => sut.ActivateModule(tenant.TenantId, "NDIC_RETURNS");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Module limit reached*");
    }

    [Fact]
    public async Task Enterprise_Allows_10_Modules()
    {
        using var db = CreateDbContext();
        SeedPlansAndModules(db);
        var tenant = CreateTenant(db, "enterprise-limit");
        AssignLicence(db, tenant.TenantId, "FC");
        AssignLicence(db, tenant.TenantId, "BDC");

        var ent = new Mock<IEntitlementService>();
        var sut = CreateSut(db, ent);
        await sut.CreateSubscription(tenant.TenantId, "ENTERPRISE", BillingFrequency.Monthly);

        await sut.ActivateModule(tenant.TenantId, "FC_RETURNS");
        await sut.ActivateModule(tenant.TenantId, "BDC_CBN");
        await sut.ActivateModule(tenant.TenantId, "NDIC_RETURNS");

        var sub = await sut.GetActiveSubscription(tenant.TenantId);
        sub.Modules.Count(m => m.IsActive).Should().Be(3);
    }

    [Fact]
    public async Task Cannot_Activate_Without_Licence()
    {
        using var db = CreateDbContext();
        SeedPlansAndModules(db);
        var tenant = CreateTenant(db, "without-licence");
        AssignLicence(db, tenant.TenantId, "BDC");

        var ent = new Mock<IEntitlementService>();
        var sut = CreateSut(db, ent);
        await sut.CreateSubscription(tenant.TenantId, "ENTERPRISE", BillingFrequency.Monthly);

        var act = () => sut.ActivateModule(tenant.TenantId, "DMB_BASEL3");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not eligible*");
    }

    [Fact]
    public async Task Cannot_Activate_Not_On_Plan()
    {
        using var db = CreateDbContext();
        SeedPlansAndModules(db);
        var tenant = CreateTenant(db, "not-on-plan");
        AssignLicence(db, tenant.TenantId, "FC");

        var ent = new Mock<IEntitlementService>();
        var sut = CreateSut(db, ent);
        await sut.CreateSubscription(tenant.TenantId, "STARTER", BillingFrequency.Monthly);

        var act = () => sut.ActivateModule(tenant.TenantId, "DMB_BASEL3");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not available on plan*");
    }

    [Fact]
    public async Task CreateSubscription_Auto_Activates_Included_Base_Modules()
    {
        using var db = CreateDbContext();
        SeedPlansAndModules(db);
        var tenant = CreateTenant(db, "auto-included-create");
        AssignLicence(db, tenant.TenantId, "FC");

        var ent = new Mock<IEntitlementService>();
        var entitlementBootstrap = new SubscriptionModuleEntitlementBootstrapService(
            db,
            ent.Object,
            NullLogger<SubscriptionModuleEntitlementBootstrapService>.Instance);
        var sut = CreateSut(db, ent, entitlementBootstrap);

        var subscription = await sut.CreateSubscription(tenant.TenantId, "STARTER", BillingFrequency.Monthly);

        var activeModules = await db.SubscriptionModules
            .Include(x => x.Module)
            .Where(x => x.SubscriptionId == subscription.Id && x.IsActive)
            .ToListAsync();

        activeModules.Should().ContainSingle(x =>
            x.Module!.ModuleCode == "FC_RETURNS"
            && x.PriceMonthly == 0m
            && x.PriceAnnual == 0m);
        ent.Verify(x => x.InvalidateCache(tenant.TenantId), Times.AtLeastOnce());
    }

    [Fact]
    public async Task UpgradePlan_Auto_Activates_Newly_Included_Base_Modules()
    {
        using var db = CreateDbContext();
        SeedPlansAndModules(db);
        var tenant = CreateTenant(db, "auto-included-upgrade");
        AssignLicence(db, tenant.TenantId, "FC");

        var enterpriseId = db.SubscriptionPlans.Single(x => x.PlanCode == "ENTERPRISE").Id;
        var dmbModuleId = db.Modules.Single(x => x.ModuleCode == "DMB_BASEL3").Id;
        var dmbPricing = db.PlanModulePricing.Single(x => x.PlanId == enterpriseId && x.ModuleId == dmbModuleId);
        dmbPricing.PriceMonthly = 0m;
        dmbPricing.PriceAnnual = 0m;
        dmbPricing.IsIncludedInBase = true;
        db.SaveChanges();

        var ent = new Mock<IEntitlementService>();
        var entitlementBootstrap = new SubscriptionModuleEntitlementBootstrapService(
            db,
            ent.Object,
            NullLogger<SubscriptionModuleEntitlementBootstrapService>.Instance);
        var sut = CreateSut(db, ent, entitlementBootstrap);

        await sut.CreateSubscription(tenant.TenantId, "STARTER", BillingFrequency.Monthly);
        await sut.UpgradePlan(tenant.TenantId, "ENTERPRISE");

        var sub = await sut.GetActiveSubscription(tenant.TenantId);
        sub.Plan!.PlanCode.Should().Be("ENTERPRISE");
        sub.Modules.Should().Contain(x => x.IsActive && x.Module!.ModuleCode == "FC_RETURNS");
        sub.Modules.Should().Contain(x => x.IsActive && x.Module!.ModuleCode == "DMB_BASEL3" && x.PriceMonthly == 0m && x.PriceAnnual == 0m);
    }

    [Fact]
    public async Task Deactivation_Removes_From_Entitlements()
    {
        using var db = CreateDbContext();
        SeedPlansAndModules(db);
        var tenant = CreateTenant(db, "deactivate");
        AssignLicence(db, tenant.TenantId, "FC");

        var ent = new Mock<IEntitlementService>();
        var sut = CreateSut(db, ent);
        await sut.CreateSubscription(tenant.TenantId, "ENTERPRISE", BillingFrequency.Monthly);

        await sut.ActivateModule(tenant.TenantId, "DMB_BASEL3");
        await sut.DeactivateModule(tenant.TenantId, "DMB_BASEL3");

        var sub = await sut.GetActiveSubscription(tenant.TenantId);
        sub.Modules.Single(m => m.Module!.ModuleCode == "DMB_BASEL3").IsActive.Should().BeFalse();
        ent.Verify(x => x.InvalidateCache(tenant.TenantId), Times.AtLeast(2));
    }

    [Fact]
    public async Task Invoice_Includes_7_5_Percent_VAT()
    {
        using var db = CreateDbContext();
        SeedPlansAndModules(db);
        var tenant = CreateTenant(db, "vat");
        AssignLicence(db, tenant.TenantId, "BDC");

        var ent = new Mock<IEntitlementService>();
        var sut = CreateSut(db, ent);
        await sut.CreateSubscription(tenant.TenantId, "STARTER", BillingFrequency.Monthly);
        await sut.ActivateModule(tenant.TenantId, "BDC_CBN");

        var invoice = await sut.GenerateInvoice(tenant.TenantId);

        invoice.VatRate.Should().Be(0.0750m);
        invoice.VatAmount.Should().Be(decimal.Round(invoice.Subtotal * 0.0750m, 2, MidpointRounding.AwayFromZero));
        invoice.TotalAmount.Should().Be(invoice.Subtotal + invoice.VatAmount);
    }

    [Fact]
    public async Task Invoice_Number_Format_INV_SLUG_YYYYMM_SEQ()
    {
        using var db = CreateDbContext();
        SeedPlansAndModules(db);
        var tenant = CreateTenant(db, "invoice-format");
        AssignLicence(db, tenant.TenantId, "BDC");

        var ent = new Mock<IEntitlementService>();
        var sut = CreateSut(db, ent);
        await sut.CreateSubscription(tenant.TenantId, "STARTER", BillingFrequency.Monthly);

        var invoice = await sut.GenerateInvoice(tenant.TenantId);
        invoice.InvoiceNumber.Should().MatchRegex("^INV-INVOICE-FORMAT-\\d{6}-\\d{4}$");
    }

    [Fact]
    public async Task Invoice_Has_BasePlan_Plus_Module_Lines()
    {
        using var db = CreateDbContext();
        SeedPlansAndModules(db);
        var tenant = CreateTenant(db, "invoice-lines");
        AssignLicence(db, tenant.TenantId, "BDC");

        var ent = new Mock<IEntitlementService>();
        var sut = CreateSut(db, ent);
        await sut.CreateSubscription(tenant.TenantId, "STARTER", BillingFrequency.Monthly);
        await sut.ActivateModule(tenant.TenantId, "BDC_CBN");

        var invoice = await sut.GenerateInvoice(tenant.TenantId);

        invoice.LineItems.Should().Contain(li => li.LineType == "BasePlan");
        invoice.LineItems.Should().Contain(li => li.LineType == "Module" && li.ModuleId.HasValue);
    }

    [Fact]
    public async Task Sequential_Invoice_Numbers()
    {
        using var db = CreateDbContext();
        SeedPlansAndModules(db);
        var tenant = CreateTenant(db, "invoice-seq");
        AssignLicence(db, tenant.TenantId, "BDC");

        var ent = new Mock<IEntitlementService>();
        var sut = CreateSut(db, ent);
        await sut.CreateSubscription(tenant.TenantId, "STARTER", BillingFrequency.Monthly);

        var i1 = await sut.GenerateInvoice(tenant.TenantId);
        var i2 = await sut.GenerateInvoice(tenant.TenantId);

        i1.InvoiceNumber.Should().NotBe(i2.InvoiceNumber);
        i2.InvoiceNumber.EndsWith("0002").Should().BeTrue();
    }

    [Fact]
    public async Task Included_Module_Not_Billed_Separately()
    {
        using var db = CreateDbContext();
        SeedPlansAndModules(db);
        var tenant = CreateTenant(db, "included-module");
        AssignLicence(db, tenant.TenantId, "FC");

        var ent = new Mock<IEntitlementService>();
        var sut = CreateSut(db, ent);
        await sut.CreateSubscription(tenant.TenantId, "STARTER", BillingFrequency.Monthly);
        await sut.ActivateModule(tenant.TenantId, "FC_RETURNS");

        var invoice = await sut.GenerateInvoice(tenant.TenantId);

        invoice.LineItems.Should().ContainSingle(li => li.LineType == "BasePlan");
        invoice.LineItems.Should().NotContain(li => li.LineType == "Module" && li.ModuleId.HasValue);
    }

    [Fact]
    public async Task Overdue_Marks_Subscription_PastDue()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(databaseName);
        SeedPlansAndModules(db);
        var tenant = CreateTenant(db, "overdue");
        AssignLicence(db, tenant.TenantId, "BDC");

        var entitlementMock = new Mock<IEntitlementService>();
        var sut = CreateSut(db, entitlementMock);
        var subscription = await sut.CreateSubscription(tenant.TenantId, "STARTER", BillingFrequency.Monthly);
        subscription.Activate();

        var invoice = await sut.GenerateInvoice(tenant.TenantId);
        invoice.Issue(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2).Date));
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddDbContext<MetadataDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddScoped(_ => entitlementMock.Object);
        using var sp = services.BuildServiceProvider();

        var job = new OverdueInvoiceJob(sp, NullLogger<OverdueInvoiceJob>.Instance);

        var method = typeof(OverdueInvoiceJob).GetMethod("ProcessOverdueFlows", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)method.Invoke(job, new object[] { CancellationToken.None })!;

        using var verifyDb = CreateDbContext(databaseName);
        var reloaded = await verifyDb.Subscriptions.FirstAsync(s => s.Id == subscription.Id);
        reloaded.Status.Should().Be(SubscriptionStatus.PastDue);
    }

    [Fact]
    public async Task Grace_Period_Expiry_Suspends()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(databaseName);
        SeedPlansAndModules(db);
        var tenant = CreateTenant(db, "grace-expiry");
        AssignLicence(db, tenant.TenantId, "BDC");

        var entitlementMock = new Mock<IEntitlementService>();
        var sut = CreateSut(db, entitlementMock);
        var subscription = await sut.CreateSubscription(tenant.TenantId, "STARTER", BillingFrequency.Monthly);
        subscription.Activate();
        subscription.MarkPastDue();
        subscription.GracePeriodEndsAt = DateTime.UtcNow.AddHours(-1);
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddDbContext<MetadataDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddScoped(_ => entitlementMock.Object);
        using var sp = services.BuildServiceProvider();
        var job = new OverdueInvoiceJob(sp, NullLogger<OverdueInvoiceJob>.Instance);
        var method = typeof(OverdueInvoiceJob).GetMethod("ProcessOverdueFlows", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)method.Invoke(job, new object[] { CancellationToken.None })!;

        using var verifyDb = CreateDbContext(databaseName);
        var reloaded = await verifyDb.Subscriptions.FirstAsync(s => s.Id == subscription.Id);
        reloaded.Status.Should().Be(SubscriptionStatus.Suspended);
    }

    [Fact]
    public async Task Trial_Expired_After_TrialDays()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(databaseName);
        SeedPlansAndModules(db);
        var tenant = CreateTenant(db, "trial-expiry");
        AssignLicence(db, tenant.TenantId, "BDC");

        var entitlementMock = new Mock<IEntitlementService>();
        var sut = CreateSut(db, entitlementMock);
        var subscription = await sut.CreateSubscription(tenant.TenantId, "STARTER", BillingFrequency.Monthly);
        subscription.TrialEndsAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddDbContext<MetadataDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddScoped(_ => entitlementMock.Object);
        using var sp = services.BuildServiceProvider();
        var job = new OverdueInvoiceJob(sp, NullLogger<OverdueInvoiceJob>.Instance);
        var method = typeof(OverdueInvoiceJob).GetMethod("ProcessOverdueFlows", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)method.Invoke(job, new object[] { CancellationToken.None })!;

        using var verifyDb = CreateDbContext(databaseName);
        var reloaded = await verifyDb.Subscriptions.FirstAsync(s => s.Id == subscription.Id);
        reloaded.Status.Should().Be(SubscriptionStatus.Expired);
    }

    [Fact]
    public async Task Direct_Billing_Records_Partner_Commission()
    {
        using var db = CreateDbContext();
        SeedPlansAndModules(db);

        var partner = CreatePartnerTenant(db, "direct-partner");
        var subTenant = CreateTenant(db, "direct-subtenant");
        subTenant.SetParentTenant(partner.TenantId);
        db.SaveChanges();

        db.PartnerConfigs.Add(new PartnerConfig
        {
            TenantId = partner.TenantId,
            PartnerTier = FC.Engine.Domain.Enums.PartnerTier.Gold,
            BillingModel = PartnerBillingModel.Direct,
            CommissionRate = 0.15m,
            MaxSubTenants = 25,
            AgreementSignedAt = DateTime.UtcNow,
            AgreementVersion = "v1"
        });
        db.SaveChanges();

        AssignLicence(db, subTenant.TenantId, "BDC");

        var ent = new Mock<IEntitlementService>();
        var sut = CreateSut(db, ent);
        await sut.CreateSubscription(subTenant.TenantId, "STARTER", BillingFrequency.Monthly);
        await sut.ActivateModule(subTenant.TenantId, "BDC_CBN");

        var invoice = await sut.GenerateInvoice(subTenant.TenantId);

        var record = await db.PartnerRevenueRecords
            .AsNoTracking()
            .SingleAsync(x => x.InvoiceId == invoice.Id);

        record.BillingModel.Should().Be(PartnerBillingModel.Direct);
        record.CommissionRate.Should().Be(0.15m);
        record.CommissionAmount.Should().Be(decimal.Round(record.GrossAmount * 0.15m, 2, MidpointRounding.AwayFromZero));
        record.WholesaleDiscountAmount.Should().Be(0m);
        invoice.LineItems.Should().NotContain(li => li.LineType == "PartnerDiscount");
    }

    [Fact]
    public async Task Reseller_Billing_Applies_Wholesale_Discount()
    {
        using var db = CreateDbContext();
        SeedPlansAndModules(db);

        var partner = CreatePartnerTenant(db, "reseller-partner");
        var subTenant = CreateTenant(db, "reseller-subtenant");
        subTenant.SetParentTenant(partner.TenantId);
        db.SaveChanges();

        db.PartnerConfigs.Add(new PartnerConfig
        {
            TenantId = partner.TenantId,
            PartnerTier = FC.Engine.Domain.Enums.PartnerTier.Platinum,
            BillingModel = PartnerBillingModel.Reseller,
            WholesaleDiscount = 0.30m,
            MaxSubTenants = 25,
            AgreementSignedAt = DateTime.UtcNow,
            AgreementVersion = "v1"
        });
        db.SaveChanges();

        AssignLicence(db, subTenant.TenantId, "BDC");

        var ent = new Mock<IEntitlementService>();
        var sut = CreateSut(db, ent);
        await sut.CreateSubscription(subTenant.TenantId, "STARTER", BillingFrequency.Monthly);
        await sut.ActivateModule(subTenant.TenantId, "BDC_CBN");

        var invoice = await sut.GenerateInvoice(subTenant.TenantId);

        invoice.LineItems.Should().Contain(li => li.LineType == "PartnerDiscount" && li.UnitPrice < 0);

        var record = await db.PartnerRevenueRecords
            .AsNoTracking()
            .SingleAsync(x => x.InvoiceId == invoice.Id);

        record.BillingModel.Should().Be(PartnerBillingModel.Reseller);
        record.WholesaleDiscountRate.Should().Be(0.30m);
        record.WholesaleDiscountAmount.Should().BeGreaterThan(0m);
        invoice.Subtotal.Should().Be(decimal.Round(record.GrossAmount - record.WholesaleDiscountAmount, 2, MidpointRounding.AwayFromZero));
    }
}
