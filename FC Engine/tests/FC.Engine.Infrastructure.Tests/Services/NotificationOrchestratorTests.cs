using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Notifications;
using FC.Engine.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace FC.Engine.Infrastructure.Tests.Services;

public class NotificationOrchestratorTests
{
    [Fact]
    public async Task Mandatory_Event_Ignores_Disabled_Preference()
    {
        var tenantId = Guid.NewGuid();
        var user = new InstitutionUser
        {
            Id = 20,
            TenantId = tenantId,
            InstitutionId = 7,
            Username = "checker",
            Email = "checker@tenant.test",
            PhoneNumber = "+2348031234567",
            DisplayName = "Checker",
            PasswordHash = "hash",
            Role = InstitutionRole.Checker,
            IsActive = true
        };

        var notificationRepo = new Mock<IPortalNotificationRepository>();
        var userRepo = new Mock<IInstitutionUserRepository>();
        var institutionRepo = new Mock<IInstitutionRepository>();
        var preferenceRepo = new Mock<INotificationPreferenceRepository>();
        var deliveryRepo = new Mock<INotificationDeliveryRepository>();
        var emailSender = new Mock<IEmailSender>();
        var smsSender = new Mock<ISmsSender>();
        var pusher = new Mock<INotificationPusher>();
        var branding = new Mock<ITenantBrandingService>();
        var logger = new Mock<ILogger<NotificationOrchestrator>>();

        userRepo.Setup(x => x.GetById(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        preferenceRepo
            .Setup(x => x.GetPreference(tenantId, user.Id, NotificationEvents.DeadlineOverdue, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationPreference
            {
                TenantId = tenantId,
                UserId = user.Id,
                EventType = NotificationEvents.DeadlineOverdue,
                InAppEnabled = false,
                EmailEnabled = false,
                SmsEnabled = false,
                SmsQuietHoursStart = new TimeSpan(22, 0, 0),
                SmsQuietHoursEnd = new TimeSpan(7, 0, 0)
            });

        branding.Setup(x => x.GetBrandingConfig(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BrandingConfig.WithDefaults());

        emailSender.Setup(x => x.SendTemplatedAsync(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<BrandingConfig>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailSendResult { Success = true });

        smsSender.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmsSendResult { Success = true });

        deliveryRepo.Setup(x => x.Add(It.IsAny<NotificationDelivery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationDelivery d, CancellationToken _) => d);
        deliveryRepo.Setup(x => x.Update(It.IsAny<NotificationDelivery>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        pusher.Setup(x => x.PushToUser(It.IsAny<int>(), It.IsAny<NotificationPayload>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new NotificationOrchestrator(
            notificationRepo.Object,
            userRepo.Object,
            institutionRepo.Object,
            preferenceRepo.Object,
            deliveryRepo.Object,
            emailSender.Object,
            smsSender.Object,
            pusher.Object,
            branding.Object,
            logger.Object);

        await sut.Notify(new NotificationRequest
        {
            TenantId = tenantId,
            EventType = NotificationEvents.DeadlineOverdue,
            Title = "Overdue",
            Message = "Return is overdue",
            Priority = NotificationPriority.Critical,
            RecipientUserIds = new List<int> { user.Id }
        });

        notificationRepo.Verify(x => x.Add(It.IsAny<PortalNotification>(), It.IsAny<CancellationToken>()), Times.Once);
        emailSender.Verify(x => x.SendTemplatedAsync(
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(),
            user.Email,
            user.DisplayName,
            It.IsAny<BrandingConfig>(),
            tenantId,
            It.IsAny<CancellationToken>()), Times.Once);
        smsSender.Verify(x => x.SendAsync(user.PhoneNumber!, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Disabled_Channel_Not_Sent_For_NonMandatory_Event()
    {
        var tenantId = Guid.NewGuid();
        var user = new InstitutionUser
        {
            Id = 22,
            TenantId = tenantId,
            InstitutionId = 8,
            Username = "maker",
            Email = "maker@tenant.test",
            PhoneNumber = "+2348035556666",
            DisplayName = "Maker",
            PasswordHash = "hash",
            Role = InstitutionRole.Maker,
            IsActive = true
        };

        var notificationRepo = new Mock<IPortalNotificationRepository>();
        var userRepo = new Mock<IInstitutionUserRepository>();
        var institutionRepo = new Mock<IInstitutionRepository>();
        var preferenceRepo = new Mock<INotificationPreferenceRepository>();
        var deliveryRepo = new Mock<INotificationDeliveryRepository>();
        var emailSender = new Mock<IEmailSender>();
        var smsSender = new Mock<ISmsSender>();
        var pusher = new Mock<INotificationPusher>();
        var branding = new Mock<ITenantBrandingService>();
        var logger = new Mock<ILogger<NotificationOrchestrator>>();

        userRepo.Setup(x => x.GetById(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        preferenceRepo
            .Setup(x => x.GetPreference(tenantId, user.Id, NotificationEvents.ReturnApproved, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationPreference
            {
                TenantId = tenantId,
                UserId = user.Id,
                EventType = NotificationEvents.ReturnApproved,
                InAppEnabled = true,
                EmailEnabled = false,
                SmsEnabled = false
            });

        branding.Setup(x => x.GetBrandingConfig(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BrandingConfig.WithDefaults());

        pusher.Setup(x => x.PushToUser(It.IsAny<int>(), It.IsAny<NotificationPayload>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new NotificationOrchestrator(
            notificationRepo.Object,
            userRepo.Object,
            institutionRepo.Object,
            preferenceRepo.Object,
            deliveryRepo.Object,
            emailSender.Object,
            smsSender.Object,
            pusher.Object,
            branding.Object,
            logger.Object);

        await sut.Notify(new NotificationRequest
        {
            TenantId = tenantId,
            EventType = NotificationEvents.ReturnApproved,
            Title = "Approved",
            Message = "Your return was approved",
            Priority = NotificationPriority.Normal,
            RecipientUserIds = new List<int> { user.Id }
        });

        notificationRepo.Verify(x => x.Add(It.IsAny<PortalNotification>(), It.IsAny<CancellationToken>()), Times.Once);
        emailSender.Verify(x => x.SendTemplatedAsync(
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<BrandingConfig>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        smsSender.Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
