using FC.Engine.Infrastructure.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.Metadata;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FC.Engine.Infrastructure.Tests.Services;

public class TenantOnboardingServiceTests : IDisposable
{
    private readonly MetadataDbContext _db;
    private readonly TenantOnboardingService _sut;

    public TenantOnboardingServiceTests()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new MetadataDbContext(options);

        // Seed licence types and modules required for onboarding
        SeedReferenceData();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var entitlementService = new EntitlementService(_db, cache, NullLogger<EntitlementService>.Instance);
        _sut = new TenantOnboardingService(_db, entitlementService, NullLogger<TenantOnboardingService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private void SeedReferenceData()
    {
        var fcLicence = new LicenceType
        {
            Code = "FC",
            Name = "Finance Company",
            Regulator = "CBN",
            IsActive = true,
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow
        };
        var bdcLicence = new LicenceType
        {
            Code = "BDC",
            Name = "Bureau De Change",
            Regulator = "CBN",
            IsActive = true,
            DisplayOrder = 2,
            CreatedAt = DateTime.UtcNow
        };
        _db.LicenceTypes.AddRange(fcLicence, bdcLicence);
        _db.SaveChanges();

        var fcModule = new Module
        {
            ModuleCode = "FC_RETURNS",
            ModuleName = "FC Returns",
            RegulatorCode = "CBN",
            SheetCount = 103,
            IsActive = true,
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow
        };
        var bdcModule = new Module
        {
            ModuleCode = "BDC_CBN",
            ModuleName = "BDC Returns",
            RegulatorCode = "CBN",
            SheetCount = 8,
            IsActive = true,
            DisplayOrder = 2,
            CreatedAt = DateTime.UtcNow
        };
        _db.Modules.AddRange(fcModule, bdcModule);
        _db.SaveChanges();

        _db.LicenceModuleMatrix.Add(new LicenceModuleMatrix
        {
            LicenceTypeId = fcLicence.Id,
            ModuleId = fcModule.Id,
            IsRequired = true,
            IsOptional = false
        });
        _db.LicenceModuleMatrix.Add(new LicenceModuleMatrix
        {
            LicenceTypeId = bdcLicence.Id,
            ModuleId = bdcModule.Id,
            IsRequired = true,
            IsOptional = false
        });
        _db.SaveChanges();
    }

    private static TenantOnboardingRequest CreateValidRequest(
        string tenantName = "Acme Finance",
        string adminEmail = "admin@acme.com") => new()
    {
        TenantName = tenantName,
        TenantType = TenantType.Institution,
        ContactEmail = "info@acme.com",
        LicenceTypeCodes = new List<string> { "FC" },
        AdminEmail = adminEmail,
        AdminFullName = "Admin User",
        InstitutionCode = "ACME001",
        InstitutionName = "Acme Finance Ltd",
        InstitutionType = "FC"
    };

    // ── Tests ──

    [Fact]
    public async Task OnboardTenant_ValidRequest_CreatesAllRecords()
    {
        var request = CreateValidRequest();

        var result = await _sut.OnboardTenant(request);

        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.TenantId.Should().NotBeEmpty();
        result.TenantSlug.Should().NotBeNullOrWhiteSpace();
        result.InstitutionId.Should().BeGreaterThan(0);
        result.AdminTemporaryPassword.Should().NotBeNullOrWhiteSpace();
        result.ActivatedModules.Should().Contain("FC_RETURNS");
    }

    [Fact]
    public async Task OnboardTenant_CreatesTenantInActiveState()
    {
        var request = CreateValidRequest();

        var result = await _sut.OnboardTenant(request);

        var tenant = await _db.Tenants.FindAsync(result.TenantId);
        tenant.Should().NotBeNull();
        tenant!.Status.Should().Be(TenantStatus.Active);
        tenant.TenantName.Should().Be("Acme Finance");
        tenant.TenantType.Should().Be(TenantType.Institution);
    }

    [Fact]
    public async Task OnboardTenant_CreatesInstitution()
    {
        var request = CreateValidRequest();

        var result = await _sut.OnboardTenant(request);

        var institution = await _db.Institutions.FirstOrDefaultAsync(i => i.Id == result.InstitutionId);
        institution.Should().NotBeNull();
        institution!.InstitutionCode.Should().Be("ACME001");
        institution.InstitutionName.Should().Be("Acme Finance Ltd");
        institution.TenantId.Should().Be(result.TenantId);
        institution.EntityType.Should().Be(EntityType.HeadOffice);
    }

    [Fact]
    public async Task OnboardTenant_CreatesAdminUser()
    {
        var request = CreateValidRequest();

        var result = await _sut.OnboardTenant(request);

        var user = await _db.Set<InstitutionUser>()
            .FirstOrDefaultAsync(u => u.Email == "admin@acme.com");
        user.Should().NotBeNull();
        user!.Role.Should().Be(InstitutionRole.Admin);
        user.IsActive.Should().BeTrue();
        user.MustChangePassword.Should().BeTrue();
        user.TenantId.Should().Be(result.TenantId);
    }

    [Fact]
    public async Task OnboardTenant_AssignsLicenceTypes()
    {
        var request = CreateValidRequest();

        var result = await _sut.OnboardTenant(request);

        var licences = await _db.TenantLicenceTypes
            .Where(tlt => tlt.TenantId == result.TenantId)
            .ToListAsync();
        licences.Should().HaveCount(1);
        licences[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task OnboardTenant_CreatesWelcomeNotification()
    {
        var request = CreateValidRequest();

        var result = await _sut.OnboardTenant(request);

        var notification = await _db.PortalNotifications
            .FirstOrDefaultAsync(n => n.TenantId == result.TenantId);
        notification.Should().NotBeNull();
        notification!.Title.Should().Be("Welcome to RegOS");
        notification.Type.Should().Be(NotificationType.SystemAnnouncement);
        notification.Message.Should().Contain("filing period");
    }

    [Fact]
    public async Task OnboardTenant_CreatesReturnPeriods()
    {
        var request = CreateValidRequest();

        var result = await _sut.OnboardTenant(request);

        result.Success.Should().BeTrue();
        result.ReturnPeriodsCreated.Should().BeGreaterThan(0);
        var periods = await _db.ReturnPeriods
            .Where(rp => rp.TenantId == result.TenantId)
            .ToListAsync();
        periods.Should().NotBeEmpty();
        periods.Should().AllSatisfy(p => p.IsOpen.Should().BeTrue());
    }

    [Fact]
    public async Task OnboardTenant_ReturnPeriods_MatchModuleFrequencies()
    {
        var request = CreateValidRequest();

        var result = await _sut.OnboardTenant(request);

        var periods = await _db.ReturnPeriods
            .Where(rp => rp.TenantId == result.TenantId)
            .ToListAsync();
        // FC_RETURNS module has Monthly default frequency
        periods.Should().OnlyContain(p => p.Frequency == "Monthly");
        // Should create 12 rolling monthly periods
        periods.Should().HaveCount(12);
    }

    [Fact]
    public void GeneratePeriodsForFrequency_Monthly_Returns12Periods()
    {
        var referenceDate = new DateTime(2026, 3, 15);
        var periods = TenantOnboardingService.GeneratePeriodsForFrequency("Monthly", referenceDate);

        periods.Should().HaveCount(12);
        periods[0].Should().Be((2026, 3));
        periods[1].Should().Be((2026, 4));
        periods[2].Should().Be((2026, 5));
        periods[^1].Should().Be((2027, 2));
    }

    [Fact]
    public void GeneratePeriodsForFrequency_Quarterly_Returns4Periods()
    {
        var referenceDate = new DateTime(2026, 2, 15);
        var periods = TenantOnboardingService.GeneratePeriodsForFrequency("Quarterly", referenceDate);

        periods.Should().HaveCount(4);
        periods[0].Should().Be((2026, 3));  // Q1 end
        periods[1].Should().Be((2026, 6));  // Q2 end
        periods[2].Should().Be((2026, 9));  // Q3 end
        periods[3].Should().Be((2026, 12)); // Q4 end
    }

    [Fact]
    public void GeneratePeriodsForFrequency_Annual_Returns1Period()
    {
        var referenceDate = new DateTime(2026, 6, 1);
        var periods = TenantOnboardingService.GeneratePeriodsForFrequency("Annual", referenceDate);

        periods.Should().HaveCount(1);
        periods[0].Should().Be((2026, 12));
    }

    [Fact]
    public async Task OnboardTenant_DuplicateEmail_ReturnsError()
    {
        var request1 = CreateValidRequest("Org1", "dupe@example.com");
        await _sut.OnboardTenant(request1);

        var request2 = CreateValidRequest("Org2", "dupe@example.com");
        var result = await _sut.OnboardTenant(request2);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainMatch("*already registered*");
    }

    [Fact]
    public async Task OnboardTenant_InvalidLicenceCode_ReturnsError()
    {
        var request = CreateValidRequest();
        request.LicenceTypeCodes = new List<string> { "INVALID_CODE" };

        var result = await _sut.OnboardTenant(request);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainMatch("*Invalid licence type*");
    }

    [Fact]
    public async Task OnboardTenant_GeneratesUniqueSlug()
    {
        var request1 = CreateValidRequest("Test Org", "admin1@test.com");
        request1.InstitutionCode = "INST1";
        var result1 = await _sut.OnboardTenant(request1);

        var request2 = CreateValidRequest("Test Org", "admin2@test.com");
        request2.InstitutionCode = "INST2";
        var result2 = await _sut.OnboardTenant(request2);

        result1.TenantSlug.Should().NotBe(result2.TenantSlug);
    }

    [Fact]
    public async Task OnboardTenant_DuplicateProvidedSlug_ReturnsError()
    {
        var request1 = CreateValidRequest("First Org", "first-admin@test.com");
        request1.TenantSlug = "fixed-tenant-slug";
        request1.InstitutionCode = "FS001";
        await _sut.OnboardTenant(request1);

        var request2 = CreateValidRequest("Second Org", "second-admin@test.com");
        request2.TenantSlug = "fixed-tenant-slug";
        request2.InstitutionCode = "FS002";
        var result = await _sut.OnboardTenant(request2);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainMatch("*slug*already*");
    }

    [Fact]
    public async Task OnboardTenant_MultipleLicences_ResolvesAllModules()
    {
        var request = CreateValidRequest();
        request.LicenceTypeCodes = new List<string> { "FC", "BDC" };

        var result = await _sut.OnboardTenant(request);

        result.Success.Should().BeTrue();
        result.ActivatedModules.Should().Contain("FC_RETURNS");
        result.ActivatedModules.Should().Contain("BDC_CBN");
    }

    [Fact]
    public void GenerateTemporaryPassword_MeetsComplexityRequirements()
    {
        var password = TenantOnboardingService.GenerateTemporaryPassword();

        password.Length.Should().Be(16);
        password.Should().MatchRegex("[A-Z]");
        password.Should().MatchRegex("[a-z]");
        password.Should().MatchRegex("[0-9]");
        password.Should().MatchRegex("[!@#$%&*]");
    }

    [Fact]
    public void GenerateTemporaryPassword_ProducesDifferentPasswords()
    {
        var passwords = Enumerable.Range(0, 10)
            .Select(_ => TenantOnboardingService.GenerateTemporaryPassword())
            .ToHashSet();

        passwords.Count.Should().BeGreaterOrEqualTo(9); // Allow for extremely rare collision
    }
}
