using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Hubs;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FC.Engine.Infrastructure.Tests.Services;

public class SignalRNotificationPusherTests
{
    [Fact]
    public async Task SignalR_PushToUser_Delivers_To_Connected_Client()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new MetadataDbContext(options);

        var provider = new Mock<IServiceProvider>();
        var hubContext = new Mock<IHubContext<NotificationHub>>();
        var clients = new Mock<IHubClients>();
        var proxy = new Mock<IClientProxy>();

        clients.Setup(x => x.Group("user:42")).Returns(proxy.Object);
        hubContext.SetupGet(x => x.Clients).Returns(clients.Object);
        provider.Setup(x => x.GetService(typeof(IHubContext<NotificationHub>))).Returns(hubContext.Object);

        var sut = new SignalRNotificationPusher(provider.Object, db);

        await sut.PushToUser(42, new NotificationPayload
        {
            Title = "Test",
            Message = "Hello",
            EventType = "system.announcement"
        });

        proxy.Verify(x => x.SendCoreAsync(
                "ReceiveNotification",
                It.Is<object[]>(o => o.Length == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SignalR_PushToTenant_Delivers_To_All_Tenant_Users()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new MetadataDbContext(options);

        var tenantId = Guid.NewGuid();
        var provider = new Mock<IServiceProvider>();
        var hubContext = new Mock<IHubContext<NotificationHub>>();
        var clients = new Mock<IHubClients>();
        var proxy = new Mock<IClientProxy>();

        clients.Setup(x => x.Group($"tenant:{tenantId}")).Returns(proxy.Object);
        hubContext.SetupGet(x => x.Clients).Returns(clients.Object);
        provider.Setup(x => x.GetService(typeof(IHubContext<NotificationHub>))).Returns(hubContext.Object);

        var sut = new SignalRNotificationPusher(provider.Object, db);

        await sut.PushToTenant(tenantId, new NotificationPayload
        {
            Title = "Tenant",
            Message = "Broadcast",
            EventType = "system.announcement"
        });

        proxy.Verify(x => x.SendCoreAsync(
                "ReceiveNotification",
                It.Is<object[]>(o => o.Length == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
