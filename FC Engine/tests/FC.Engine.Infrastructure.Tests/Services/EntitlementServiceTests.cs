using FC.Engine.Application.Services;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FC.Engine.Infrastructure.Tests.Services;

public class EntitlementServiceTests : IDisposable
{
    private readonly MetadataDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly EntitlementService _sut;

    public EntitlementServiceTests()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new MetadataDbContext(options);
        _cache = new MemoryCache(new MemoryCacheOptions());
        _sut = new EntitlementService(_db, _cache, NullLogger<EntitlementService>.Instance);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _db.Dispose();
    }

    // ── Helpers ──

    private Guid SeedTenantWithLicences(params string[] licenceCodes)
    {
        var tenant = Tenant.Create("Test", "test", TenantType.Institution);
        tenant.Activate();
        _db.Tenants.Add(tenant);
        _db.SaveChanges();

        foreach (var code in licenceCodes)
        {
            var lt = new LicenceType
            {
                Code = code,
                Name = $"{code} Licence",
                Regulator = "CBN",
                IsActive = true,
                DisplayOrder = 1,
                CreatedAt = DateTime.UtcNow
            };
            _db.LicenceTypes.Add(lt);
            _db.SaveChanges();

            _db.TenantLicenceTypes.Add(new TenantLicenceType
            {
                TenantId = tenant.TenantId,
                LicenceTypeId = lt.Id,
                EffectiveDate = DateTime.UtcNow.Date,
                IsActive = true
            });
        }
        _db.SaveChanges();

        return tenant.TenantId;
    }

    private void SeedModule(string moduleCode, string moduleName, string regulator, int sheetCount = 5)
    {
        _db.Modules.Add(new Module
        {
            ModuleCode = moduleCode,
            ModuleName = moduleName,
            RegulatorCode = regulator,
            SheetCount = sheetCount,
            IsActive = true,
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();
    }

    private void SeedMatrixEntry(string licenceCode, string moduleCode, bool isRequired = false)
    {
        var lt = _db.LicenceTypes.First(l => l.Code == licenceCode);
        var mod = _db.Modules.First(m => m.ModuleCode == moduleCode);

        _db.LicenceModuleMatrix.Add(new LicenceModuleMatrix
        {
            LicenceTypeId = lt.Id,
            ModuleId = mod.Id,
            IsRequired = isRequired,
            IsOptional = !isRequired
        });
        _db.SaveChanges();
    }

    // ── Tests ──

    [Fact]
    public async Task ResolveEntitlements_FcLicence_ReturnsFcReturnsModule()
    {
        SeedModule("FC_RETURNS", "FC Returns", "CBN", 103);
        var tenantId = SeedTenantWithLicences("FC");
        SeedMatrixEntry("FC", "FC_RETURNS", isRequired: true);

        var result = await _sut.ResolveEntitlements(tenantId);

        result.ActiveModules.Should().HaveCount(1);
        result.ActiveModules[0].ModuleCode.Should().Be("FC_RETURNS");
        result.ActiveModules[0].IsRequired.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveEntitlements_BdcLicence_ReturnsBdcAndNfiuModules()
    {
        SeedModule("BDC_CBN", "BDC Returns", "CBN", 8);
        SeedModule("NFIU_AML", "AML Returns", "NFIU", 6);
        var tenantId = SeedTenantWithLicences("BDC");
        SeedMatrixEntry("BDC", "BDC_CBN", isRequired: true);
        SeedMatrixEntry("BDC", "NFIU_AML", isRequired: true);

        var result = await _sut.ResolveEntitlements(tenantId);

        result.ActiveModules.Should().HaveCount(2);
        result.ActiveModules.Select(m => m.ModuleCode).Should()
            .BeEquivalentTo(new[] { "BDC_CBN", "NFIU_AML" });
    }

    [Fact]
    public async Task ResolveEntitlements_MultiLicence_ReturnsUnionOfModules()
    {
        SeedModule("FC_RETURNS", "FC Returns", "CBN", 103);
        SeedModule("BDC_CBN", "BDC Returns", "CBN", 8);
        SeedModule("NFIU_AML", "AML Returns", "NFIU", 6);

        var tenantId = SeedTenantWithLicences("FC", "BDC");
        SeedMatrixEntry("FC", "FC_RETURNS", isRequired: true);
        SeedMatrixEntry("FC", "NFIU_AML", isRequired: false);
        SeedMatrixEntry("BDC", "BDC_CBN", isRequired: true);
        SeedMatrixEntry("BDC", "NFIU_AML", isRequired: true);

        var result = await _sut.ResolveEntitlements(tenantId);

        result.ActiveModules.Should().HaveCount(3);
        // NFIU_AML should be required because at least one mapping has IsRequired=true
        result.ActiveModules.First(m => m.ModuleCode == "NFIU_AML").IsRequired.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveEntitlements_NoLicences_ReturnsEmptyModules()
    {
        var tenant = Tenant.Create("Empty", "empty", TenantType.Institution);
        tenant.Activate();
        _db.Tenants.Add(tenant);
        _db.SaveChanges();

        var result = await _sut.ResolveEntitlements(tenant.TenantId);

        result.ActiveModules.Should().BeEmpty();
        result.EligibleModules.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveEntitlements_CachesResult()
    {
        SeedModule("FC_RETURNS", "FC Returns", "CBN");
        var tenantId = SeedTenantWithLicences("FC");
        SeedMatrixEntry("FC", "FC_RETURNS");

        var first = await _sut.ResolveEntitlements(tenantId);
        var second = await _sut.ResolveEntitlements(tenantId);

        // Should return same cached object
        ReferenceEquals(first, second).Should().BeTrue();
    }

    [Fact]
    public async Task InvalidateCache_ClearsCache_NextCallReloads()
    {
        SeedModule("FC_RETURNS", "FC Returns", "CBN");
        var tenantId = SeedTenantWithLicences("FC");
        SeedMatrixEntry("FC", "FC_RETURNS");

        var first = await _sut.ResolveEntitlements(tenantId);
        await _sut.InvalidateCache(tenantId);
        var second = await _sut.ResolveEntitlements(tenantId);

        // After cache invalidation, should be a new object
        ReferenceEquals(first, second).Should().BeFalse();
    }

    [Fact]
    public async Task HasModuleAccess_ReturnsTrueForEntitledModule()
    {
        SeedModule("FC_RETURNS", "FC Returns", "CBN");
        var tenantId = SeedTenantWithLicences("FC");
        SeedMatrixEntry("FC", "FC_RETURNS");

        var result = await _sut.HasModuleAccess(tenantId, "FC_RETURNS");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasModuleAccess_ReturnsFalseForUnentitledModule()
    {
        SeedModule("FC_RETURNS", "FC Returns", "CBN");
        SeedModule("BDC_CBN", "BDC Returns", "CBN");
        var tenantId = SeedTenantWithLicences("FC");
        SeedMatrixEntry("FC", "FC_RETURNS");

        var result = await _sut.HasModuleAccess(tenantId, "BDC_CBN");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasModuleAccess_CaseInsensitive()
    {
        SeedModule("FC_RETURNS", "FC Returns", "CBN");
        var tenantId = SeedTenantWithLicences("FC");
        SeedMatrixEntry("FC", "FC_RETURNS");

        var result = await _sut.HasModuleAccess(tenantId, "fc_returns");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasFeatureAccess_ReturnsDefaultFeatures()
    {
        var tenant = Tenant.Create("Test", "test-feat", TenantType.Institution);
        tenant.Activate();
        _db.Tenants.Add(tenant);
        _db.SaveChanges();

        var xmlSub = await _sut.HasFeatureAccess(tenant.TenantId, "xml_submission");
        var validation = await _sut.HasFeatureAccess(tenant.TenantId, "validation");
        var reporting = await _sut.HasFeatureAccess(tenant.TenantId, "reporting");
        var unknown = await _sut.HasFeatureAccess(tenant.TenantId, "unknown_feature");

        xmlSub.Should().BeTrue();
        validation.Should().BeTrue();
        reporting.Should().BeTrue();
        unknown.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveEntitlements_NonExistentTenant_Throws()
    {
        var act = () => _sut.ResolveEntitlements(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }
}
