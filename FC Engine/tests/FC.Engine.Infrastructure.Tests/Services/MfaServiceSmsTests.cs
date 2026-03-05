using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Notifications;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using OtpNet;

namespace FC.Engine.Infrastructure.Tests.Services;

public class MfaServiceSmsTests
{
    [Fact]
    public async Task Mfa_Code_Delivered_Via_Sms()
    {
        var tenantId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new MetadataDbContext(options);

        var tenant = Tenant.Create("Tenant", "tenant", TenantType.Institution, "admin@tenant.test");
        tenant.Activate();
        db.Tenants.Add(tenant);
        tenantId = tenant.TenantId;

        db.Institutions.Add(new Institution
        {
            Id = 10,
            TenantId = tenantId,
            InstitutionCode = "T001",
            InstitutionName = "Tenant Institution",
            ContactPhone = "+2348030000000",
            IsActive = true,
            EntityType = EntityType.HeadOffice,
            CreatedAt = DateTime.UtcNow
        });

        db.InstitutionUsers.Add(new InstitutionUser
        {
            Id = 200,
            TenantId = tenantId,
            InstitutionId = 10,
            Username = "maker",
            Email = "maker@tenant.test",
            PhoneNumber = "+2348031234567",
            DisplayName = "Maker",
            PasswordHash = "hash",
            Role = InstitutionRole.Maker,
            IsActive = true,
            MustChangePassword = false
        });

        var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
        db.UserMfaConfigs.Add(new UserMfaConfig
        {
            TenantId = tenantId,
            UserId = 200,
            UserType = "InstitutionUser",
            SecretKey = secret,
            BackupCodes = "[]",
            IsEnabled = true,
            EnabledAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupGet(x => x.CurrentTenantId).Returns(tenantId);

        var orchestrator = new Mock<INotificationOrchestrator>();
        NotificationRequest? captured = null;
        orchestrator
            .Setup(x => x.Notify(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationRequest, CancellationToken>((request, _) => captured = request)
            .Returns(Task.CompletedTask);

        var sut = new MfaService(db, tenantContext.Object, orchestrator.Object);

        var sent = await sut.SendMfaCodeSms(200, "InstitutionUser");

        sent.Should().BeTrue();
        orchestrator.Verify(x => x.Notify(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Once);

        captured.Should().NotBeNull();
        captured!.EventType.Should().Be(NotificationEvents.MfaCodeSms);
        captured.RecipientUserIds.Should().ContainSingle().Which.Should().Be(200);
        captured.Data.Should().ContainKey("Code");
        captured.Data["Code"].Should().MatchRegex("^[0-9]{6}$");
    }
}
