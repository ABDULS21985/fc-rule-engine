using System.Reflection;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace FC.Engine.Infrastructure.Tests.Services;

public class NotificationRetryJobTests
{
    [Fact]
    public void Exponential_Backoff_5min_30min_2hr()
    {
        var method = typeof(NotificationRetryJob).GetMethod("GetRetryDelay", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var first = (TimeSpan)method!.Invoke(null, new object[] { 1 })!;
        var second = (TimeSpan)method.Invoke(null, new object[] { 2 })!;
        var third = (TimeSpan)method.Invoke(null, new object[] { 3 })!;

        first.Should().Be(TimeSpan.FromMinutes(5));
        second.Should().Be(TimeSpan.FromMinutes(30));
        third.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public async Task Successful_Delivery_Marked_Sent()
    {
        var tenantId = Guid.NewGuid();
        var delivery = new NotificationDelivery
        {
            Id = 1,
            TenantId = tenantId,
            NotificationEventType = "export.ready",
            Channel = NotificationChannel.Email,
            RecipientId = 10,
            RecipientAddress = "ops@tenant.test",
            Status = DeliveryStatus.Failed,
            AttemptCount = 0,
            MaxAttempts = 3,
            Payload = "{\"RecipientName\":\"Ops\",\"Message\":\"Ready\"}"
        };

        var deliveryRepo = new Mock<INotificationDeliveryRepository>();
        var emailSender = new Mock<IEmailSender>();
        var smsSender = new Mock<ISmsSender>();
        var branding = new Mock<ITenantBrandingService>();
        var logger = new Mock<ILogger<NotificationRetryJob>>();

        deliveryRepo.Setup(x => x.GetRetryBatch(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<NotificationDelivery> { delivery });
        deliveryRepo.Setup(x => x.Update(It.IsAny<NotificationDelivery>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        branding.Setup(x => x.GetBrandingConfig(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BrandingConfig.WithDefaults());

        emailSender.Setup(x => x.SendTemplatedAsync(
                delivery.NotificationEventType,
                It.IsAny<Dictionary<string, string>>(),
                delivery.RecipientAddress,
                It.IsAny<string>(),
                It.IsAny<BrandingConfig>(),
                tenantId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailSendResult { Success = true, ProviderMessageId = "msg-1" });

        using var serviceProvider = new ServiceCollection()
            .AddSingleton(deliveryRepo.Object)
            .AddSingleton(emailSender.Object)
            .AddSingleton(smsSender.Object)
            .AddSingleton(branding.Object)
            .BuildServiceProvider();

        var sut = new NotificationRetryJob(serviceProvider, logger.Object);

        var retryMethod = typeof(NotificationRetryJob).GetMethod("RetryFailedDeliveries", BindingFlags.NonPublic | BindingFlags.Instance);
        retryMethod.Should().NotBeNull();

        await (Task)retryMethod!.Invoke(sut, new object[] { CancellationToken.None })!;

        delivery.Status.Should().Be(DeliveryStatus.Sent);
        delivery.AttemptCount.Should().Be(1);
        delivery.SentAt.Should().NotBeNull();
        delivery.ProviderMessageId.Should().Be("msg-1");
        delivery.NextRetryAt.Should().BeNull();

        deliveryRepo.Verify(x => x.Update(delivery, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Failed_Delivery_Retried_3_Times()
    {
        var tenantId = Guid.NewGuid();
        var delivery = new NotificationDelivery
        {
            Id = 12,
            TenantId = tenantId,
            NotificationEventType = "export.ready",
            Channel = NotificationChannel.Email,
            RecipientId = 31,
            RecipientAddress = "ops@tenant.test",
            Status = DeliveryStatus.Failed,
            AttemptCount = 0,
            MaxAttempts = 3,
            NextRetryAt = DateTime.UtcNow.AddMinutes(-1),
            Payload = "{\"RecipientName\":\"Ops\",\"Message\":\"Ready\"}"
        };

        var deliveryRepo = new Mock<INotificationDeliveryRepository>();
        var emailSender = new Mock<IEmailSender>();
        var smsSender = new Mock<ISmsSender>();
        var branding = new Mock<ITenantBrandingService>();
        var logger = new Mock<ILogger<NotificationRetryJob>>();

        deliveryRepo.Setup(x => x.GetRetryBatch(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
                delivery.AttemptCount < delivery.MaxAttempts
                    ? new List<NotificationDelivery> { delivery }
                    : new List<NotificationDelivery>());
        deliveryRepo.Setup(x => x.Update(It.IsAny<NotificationDelivery>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        branding.Setup(x => x.GetBrandingConfig(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BrandingConfig.WithDefaults());

        emailSender.Setup(x => x.SendTemplatedAsync(
                delivery.NotificationEventType,
                It.IsAny<Dictionary<string, string>>(),
                delivery.RecipientAddress,
                It.IsAny<string>(),
                It.IsAny<BrandingConfig>(),
                tenantId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailSendResult
            {
                Success = false,
                ErrorMessage = "smtp timeout"
            });

        using var serviceProvider = new ServiceCollection()
            .AddSingleton(deliveryRepo.Object)
            .AddSingleton(emailSender.Object)
            .AddSingleton(smsSender.Object)
            .AddSingleton(branding.Object)
            .BuildServiceProvider();

        var sut = new NotificationRetryJob(serviceProvider, logger.Object);

        var retryMethod = typeof(NotificationRetryJob).GetMethod("RetryFailedDeliveries", BindingFlags.NonPublic | BindingFlags.Instance);
        retryMethod.Should().NotBeNull();

        await (Task)retryMethod!.Invoke(sut, new object[] { CancellationToken.None })!;
        delivery.NextRetryAt = DateTime.UtcNow.AddMinutes(-1);
        await (Task)retryMethod.Invoke(sut, new object[] { CancellationToken.None })!;
        delivery.NextRetryAt = DateTime.UtcNow.AddMinutes(-1);
        await (Task)retryMethod.Invoke(sut, new object[] { CancellationToken.None })!;
        await (Task)retryMethod.Invoke(sut, new object[] { CancellationToken.None })!;

        delivery.Status.Should().Be(DeliveryStatus.Failed);
        delivery.AttemptCount.Should().Be(3);
        delivery.NextRetryAt.Should().NotBeNull();

        emailSender.Verify(x => x.SendTemplatedAsync(
            delivery.NotificationEventType,
            It.IsAny<Dictionary<string, string>>(),
            delivery.RecipientAddress,
            It.IsAny<string>(),
            It.IsAny<BrandingConfig>(),
            tenantId,
            It.IsAny<CancellationToken>()), Times.Exactly(3));
    }
}
