using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using OtpNet;

namespace FC.Engine.Infrastructure.Tests.Services;

public class MfaServiceTests
{
    private static (MfaService Service, MetadataDbContext Db, Guid TenantId) CreateSut()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new MetadataDbContext(options);
        var tenantId = Guid.NewGuid();

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupGet(x => x.CurrentTenantId).Returns(tenantId);
        tenantContext.SetupGet(x => x.IsPlatformAdmin).Returns(false);
        tenantContext.SetupGet(x => x.ImpersonatingTenantId).Returns((Guid?)null);

        var sut = new MfaService(db, tenantContext.Object);
        return (sut, db, tenantId);
    }

    [Fact]
    public async Task MFA_Setup_Generates_Valid_QR_Code()
    {
        var (sut, db, _) = CreateSut();
        var result = await sut.InitiateSetup(101, "InstitutionUser", "mfa.user@test.local");

        result.SecretKey.Should().NotBeNullOrWhiteSpace();
        result.QrCodeDataUri.Should().StartWith("data:image/png;base64,");
        result.Issuer.Should().Be("RegOS");

        var stored = await db.UserMfaConfigs.FirstOrDefaultAsync(x => x.UserId == 101 && x.UserType == "InstitutionUser");
        stored.Should().NotBeNull();
        stored!.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task MFA_Activation_Requires_Valid_TOTP_Code()
    {
        var (sut, _, _) = CreateSut();
        var setup = await sut.InitiateSetup(202, "InstitutionUser", "setup@test.local");

        var totp = new Totp(Base32Encoding.ToBytes(setup.SecretKey));
        var validCode = totp.ComputeTotp();

        var failed = await sut.ActivateWithVerification(202, "InstitutionUser", "000000");
        failed.Success.Should().BeFalse();

        var activated = await sut.ActivateWithVerification(202, "InstitutionUser", validCode);
        activated.Success.Should().BeTrue();
        activated.BackupCodes.Should().HaveCount(10);
    }

    [Fact]
    public async Task MFA_Backup_Code_Works_And_Is_Single_Use()
    {
        var (sut, db, _) = CreateSut();
        var setup = await sut.InitiateSetup(303, "InstitutionUser", "backup@test.local");
        var totp = new Totp(Base32Encoding.ToBytes(setup.SecretKey));
        var code = totp.ComputeTotp();
        var activated = await sut.ActivateWithVerification(303, "InstitutionUser", code);

        var backup = activated.BackupCodes.First();
        var firstUse = await sut.VerifyBackupCode(303, backup, "InstitutionUser");
        var secondUse = await sut.VerifyBackupCode(303, backup, "InstitutionUser");

        firstUse.Should().BeTrue();
        secondUse.Should().BeFalse();

        var persisted = await db.UserMfaConfigs.FirstAsync(x => x.UserId == 303);
        persisted.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task MFA_Required_For_Checker_Role()
    {
        var (sut, db, tenantId) = CreateSut();
        db.Tenants.Add(Tenant.Create("MFA Tenant", "mfa-tenant", TenantType.Institution));
        await db.SaveChangesAsync();

        var checkerRequired = await sut.IsMfaRequired(tenantId, "Checker");
        var viewerRequired = await sut.IsMfaRequired(tenantId, "Viewer");

        checkerRequired.Should().BeTrue();
        viewerRequired.Should().BeFalse();
    }

    [Fact]
    public async Task MFA_Not_Required_For_Viewer_Role()
    {
        var (sut, db, tenantId) = CreateSut();
        var tenant = Tenant.Create("Viewer Tenant", "viewer-tenant", TenantType.Institution);
        tenant.Activate();
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var viewerRequired = await sut.IsMfaRequired(tenantId, "Viewer");
        viewerRequired.Should().BeFalse();
    }
}
