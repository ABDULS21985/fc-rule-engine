using Dapper;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FC.Engine.Integration.Tests.MultiTenancy;

/// <summary>
/// RG-02 integration tests for entitlement resolution and tenant onboarding.
/// These tests target a real SQL Server database (same env contract as other integration tests).
/// </summary>
public class EntitlementAndOnboardingTests : IAsyncLifetime
{
    private string _connectionString = null!;
    private readonly List<Guid> _createdTenantIds = new();

    public Task InitializeAsync()
    {
        return InitializeConnectionAsync();
    }

    private async Task InitializeConnectionAsync()
    {
        _connectionString = await TestSqlConnectionResolver.ResolveAsync();
    }

    public async Task DisposeAsync()
    {
        if (_createdTenantIds.Count == 0)
        {
            return;
        }

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        // Cleanup test data in child->parent order.
        await conn.ExecuteAsync(@"
            DELETE FROM dbo.portal_notifications WHERE TenantId IN @TenantIds;
            DELETE FROM dbo.return_periods WHERE TenantId IN @TenantIds;
            DELETE FROM meta.institution_users WHERE TenantId IN @TenantIds;
            DELETE FROM dbo.subscription_modules WHERE SubscriptionId IN (SELECT Id FROM dbo.subscriptions WHERE TenantId IN @TenantIds);
            DELETE FROM dbo.subscriptions WHERE TenantId IN @TenantIds;
            DELETE FROM dbo.institutions WHERE TenantId IN @TenantIds;
            DELETE FROM dbo.tenant_licence_types WHERE TenantId IN @TenantIds;
            DELETE FROM dbo.tenants WHERE TenantId IN @TenantIds;
        ", new { TenantIds = _createdTenantIds.Distinct().ToArray() });
    }

    [Fact]
    public async Task BDC_Tenant_Can_Access_BDC_And_NFIU_But_Not_DMB()
    {
        var tenantId = await CreateTenantAsync("rg02-int-bdc");
        await AssignLicenceAsync(tenantId, "BDC");
        await CreateSubscriptionAndActivateRequiredModulesAsync(tenantId);

        await using var db = CreateDbContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new EntitlementService(db, cache, NullLogger<EntitlementService>.Instance);

        var entitlement = await sut.ResolveEntitlements(tenantId);

        entitlement.ActiveModules.Select(m => m.ModuleCode)
            .Should().Contain(new[] { "BDC_CBN", "NFIU_AML" });
        entitlement.ActiveModules.Select(m => m.ModuleCode)
            .Should().NotContain("DMB_BASEL3");
    }

    [Fact]
    public async Task DMB_Tenant_Gets_Required_Modules_Automatically()
    {
        var tenantId = await CreateTenantAsync("rg02-int-dmb");
        await AssignLicenceAsync(tenantId, "DMB");
        await CreateSubscriptionAndActivateRequiredModulesAsync(tenantId);

        await using var db = CreateDbContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new EntitlementService(db, cache, NullLogger<EntitlementService>.Instance);

        var entitlement = await sut.ResolveEntitlements(tenantId);

        entitlement.ActiveModules.Should().Contain(m => m.ModuleCode == "DMB_BASEL3" && m.IsRequired);
        entitlement.ActiveModules.Should().Contain(m => m.ModuleCode == "NDIC_RETURNS" && m.IsRequired);
        entitlement.ActiveModules.Should().Contain(m => m.ModuleCode == "NFIU_AML" && m.IsRequired);
    }

    [Fact]
    public async Task Multi_Licence_Tenant_Gets_Union_Of_Modules()
    {
        var tenantId = await CreateTenantAsync("rg02-int-multi");
        await AssignLicenceAsync(tenantId, "BDC");
        await AssignLicenceAsync(tenantId, "FC");
        await CreateSubscriptionAndActivateRequiredModulesAsync(tenantId);

        await using var db = CreateDbContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new EntitlementService(db, cache, NullLogger<EntitlementService>.Instance);

        var entitlement = await sut.ResolveEntitlements(tenantId);

        entitlement.ActiveModules.Select(m => m.ModuleCode)
            .Should().Contain(new[] { "FC_RETURNS", "BDC_CBN", "NFIU_AML", "NDIC_RETURNS" });
    }

    [Fact]
    public async Task Entitlement_Cache_Invalidates_On_Licence_Change()
    {
        var tenantId = await CreateTenantAsync("rg02-int-cache");
        await AssignLicenceAsync(tenantId, "BDC");
        await CreateSubscriptionAndActivateRequiredModulesAsync(tenantId);

        await using var db = CreateDbContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new EntitlementService(db, cache, NullLogger<EntitlementService>.Instance);

        var before = await sut.ResolveEntitlements(tenantId);
        before.ActiveModules.Select(m => m.ModuleCode).Should().Contain("BDC_CBN");
        before.ActiveModules.Select(m => m.ModuleCode).Should().NotContain("FC_RETURNS");

        await AssignLicenceAsync(tenantId, "FC");
        await ActivateModuleAsync(tenantId, "FC_RETURNS");
        await sut.InvalidateCache(tenantId);

        var after = await sut.ResolveEntitlements(tenantId);
        after.ActiveModules.Select(m => m.ModuleCode).Should().Contain(new[] { "BDC_CBN", "FC_RETURNS" });
    }

    [Fact]
    public async Task Onboard_BDC_Tenant_Creates_All_Required_Records()
    {
        await using var db = CreateDbContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var entitlementService = new EntitlementService(db, cache, NullLogger<EntitlementService>.Instance);
        var subscriptionService = new SubscriptionService(db, entitlementService, NullLogger<SubscriptionService>.Instance);
        var sut = new TenantOnboardingService(
            db,
            entitlementService,
            subscriptionService,
            NullLogger<TenantOnboardingService>.Instance);

        var slug = $"rg02-bdc-{Guid.NewGuid():N}"[..20];
        var request = new TenantOnboardingRequest
        {
            TenantName = "RG02 BDC Tenant",
            TenantSlug = slug,
            TenantType = TenantType.Institution,
            ContactEmail = "rg02-bdc@test.local",
            LicenceTypeCodes = new List<string> { "BDC" },
            SubscriptionPlanCode = "STARTER",
            AdminEmail = $"admin-{Guid.NewGuid():N}@test.local",
            AdminFullName = "RG02 Admin",
            InstitutionCode = $"BDC{Guid.NewGuid():N}"[..10],
            InstitutionName = "RG02 BDC Institution",
            InstitutionType = "BDC"
        };

        var result = await sut.OnboardTenant(request);

        result.Success.Should().BeTrue($"errors: {string.Join(" | ", result.Errors)}");
        result.TenantId.Should().NotBeEmpty();
        _createdTenantIds.Add(result.TenantId);

        var tenant = await db.Tenants.FindAsync(result.TenantId);
        tenant.Should().NotBeNull();
        tenant!.Status.Should().Be(TenantStatus.Active);

        var institution = await db.Institutions.FirstOrDefaultAsync(i => i.Id == result.InstitutionId);
        institution.Should().NotBeNull();

        var adminUser = await db.InstitutionUsers
            .FirstOrDefaultAsync(u => u.TenantId == result.TenantId && u.Email == request.AdminEmail);
        adminUser.Should().NotBeNull();
        adminUser!.Role.Should().Be(InstitutionRole.Admin);

        var tenantLicences = await db.TenantLicenceTypes.Where(t => t.TenantId == result.TenantId).ToListAsync();
        tenantLicences.Should().ContainSingle();

        result.ActivatedModules.Should().Contain("BDC_CBN");
    }

    [Fact]
    public async Task Onboard_Rolls_Back_On_Duplicate_Slug()
    {
        await using var db = CreateDbContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var entitlementService = new EntitlementService(db, cache, NullLogger<EntitlementService>.Instance);
        var subscriptionService = new SubscriptionService(db, entitlementService, NullLogger<SubscriptionService>.Instance);
        var sut = new TenantOnboardingService(
            db,
            entitlementService,
            subscriptionService,
            NullLogger<TenantOnboardingService>.Instance);

        var fixedSlug = $"rg02-dup-{Guid.NewGuid():N}"[..20];

        var first = await sut.OnboardTenant(new TenantOnboardingRequest
        {
            TenantName = "Duplicate Slug Org One",
            TenantSlug = fixedSlug,
            TenantType = TenantType.Institution,
            ContactEmail = "dup-one@test.local",
            LicenceTypeCodes = new List<string> { "BDC" },
            AdminEmail = $"dup-one-{Guid.NewGuid():N}@test.local",
            AdminFullName = "Dup One",
            InstitutionCode = $"DUP{Guid.NewGuid():N}"[..10],
            InstitutionName = "Dup Institution One",
            InstitutionType = "BDC"
        });

        first.Success.Should().BeTrue($"errors: {string.Join(" | ", first.Errors)}");
        _createdTenantIds.Add(first.TenantId);

        var secondAdminEmail = $"dup-two-{Guid.NewGuid():N}@test.local";
        var second = await sut.OnboardTenant(new TenantOnboardingRequest
        {
            TenantName = "Duplicate Slug Org Two",
            TenantSlug = fixedSlug,
            TenantType = TenantType.Institution,
            ContactEmail = "dup-two@test.local",
            LicenceTypeCodes = new List<string> { "BDC" },
            AdminEmail = secondAdminEmail,
            AdminFullName = "Dup Two",
            InstitutionCode = $"DUP{Guid.NewGuid():N}"[..10],
            InstitutionName = "Dup Institution Two",
            InstitutionType = "BDC"
        });

        second.Success.Should().BeFalse();
        second.Errors.Should().Contain(e => e.Contains("slug", StringComparison.OrdinalIgnoreCase));

        var slugTenantCount = await db.Tenants.CountAsync(t => t.TenantSlug == fixedSlug);
        slugTenantCount.Should().Be(1);

        var secondAdminExists = await db.InstitutionUsers.AnyAsync(u => u.Email == secondAdminEmail);
        secondAdminExists.Should().BeFalse();
    }

    private MetadataDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseSqlServer(_connectionString)
            .Options;

        return new MetadataDbContext(options);
    }

    private async Task<Guid> CreateTenantAsync(string slugPrefix)
    {
        await using var db = CreateDbContext();

        var slug = $"{slugPrefix}-{Guid.NewGuid():N}"[..Math.Min(30, slugPrefix.Length + 9)];
        var tenant = Tenant.Create($"Tenant {slugPrefix}", slug, TenantType.Institution, "integration@test.local");
        tenant.Activate();

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        _createdTenantIds.Add(tenant.TenantId);
        return tenant.TenantId;
    }

    private async Task AssignLicenceAsync(Guid tenantId, string licenceCode)
    {
        await using var db = CreateDbContext();

        var licenceId = await db.LicenceTypes
            .Where(lt => lt.Code == licenceCode)
            .Select(lt => lt.Id)
            .SingleAsync();

        var exists = await db.TenantLicenceTypes
            .AnyAsync(t => t.TenantId == tenantId && t.LicenceTypeId == licenceId);
        if (exists)
        {
            return;
        }

        db.TenantLicenceTypes.Add(new TenantLicenceType
        {
            TenantId = tenantId,
            LicenceTypeId = licenceId,
            EffectiveDate = DateTime.UtcNow.Date,
            IsActive = true
        });

        await db.SaveChangesAsync();
    }

    private async Task CreateSubscriptionAndActivateRequiredModulesAsync(Guid tenantId)
    {
        await using var db = CreateDbContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var entitlementService = new EntitlementService(db, cache, NullLogger<EntitlementService>.Instance);
        var subscriptionService = new SubscriptionService(db, entitlementService, NullLogger<SubscriptionService>.Instance);

        // GROUP guarantees pricing availability for all seeded modules in RG-03.
        await subscriptionService.CreateSubscription(tenantId, "GROUP", BillingFrequency.Monthly);

        var licenceTypeIds = await db.TenantLicenceTypes
            .Where(t => t.TenantId == tenantId && t.IsActive)
            .Select(t => t.LicenceTypeId)
            .ToListAsync();

        var requiredModuleCodes = await db.LicenceModuleMatrix
            .Where(m => licenceTypeIds.Contains(m.LicenceTypeId) && m.IsRequired)
            .Join(db.Modules, m => m.ModuleId, mod => mod.Id, (m, mod) => mod.ModuleCode)
            .Distinct()
            .ToListAsync();

        foreach (var moduleCode in requiredModuleCodes)
        {
            await subscriptionService.ActivateModule(tenantId, moduleCode);
        }

        await entitlementService.InvalidateCache(tenantId);
    }

    private async Task ActivateModuleAsync(Guid tenantId, string moduleCode)
    {
        await using var db = CreateDbContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var entitlementService = new EntitlementService(db, cache, NullLogger<EntitlementService>.Instance);
        var subscriptionService = new SubscriptionService(db, entitlementService, NullLogger<SubscriptionService>.Instance);
        await subscriptionService.ActivateModule(tenantId, moduleCode);
        await entitlementService.InvalidateCache(tenantId);
    }
}
