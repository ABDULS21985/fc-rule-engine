using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Tests.Services;

public class ReturnLockServiceTests
{
    [Fact]
    public async Task Second_User_Is_Blocked_When_Lock_Is_Active()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(nameof(Second_User_Is_Blocked_When_Lock_Is_Active));
        var sut = new ReturnLockService(db);

        var first = await sut.AcquireLock(tenantId, submissionId: 101, userId: 7, userName: "Alice");
        var second = await sut.AcquireLock(tenantId, submissionId: 101, userId: 8, userName: "Bob");

        first.Acquired.Should().BeTrue();
        second.Acquired.Should().BeFalse();
        second.UserId.Should().Be(7);
        second.UserName.Should().Be("Alice");
        second.Message.Should().Contain("Being edited by Alice");
    }

    [Fact]
    public async Task Lock_Expires_After_Inactivity_And_New_User_Can_Acquire()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(nameof(Lock_Expires_After_Inactivity_And_New_User_Can_Acquire));
        var sut = new ReturnLockService(db);

        var first = await sut.AcquireLock(tenantId, submissionId: 202, userId: 10, userName: "Maker One");
        first.Acquired.Should().BeTrue();

        var stored = await db.ReturnLocks.SingleAsync(x => x.SubmissionId == 202);
        stored.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        stored.HeartbeatAt = DateTime.UtcNow.AddMinutes(-31);
        await db.SaveChangesAsync();

        var second = await sut.AcquireLock(tenantId, submissionId: 202, userId: 11, userName: "Maker Two");

        second.Acquired.Should().BeTrue();
        second.UserId.Should().Be(11);
        second.UserName.Should().Be("Maker Two");

        var remainingLocks = await db.ReturnLocks
            .Where(x => x.SubmissionId == 202)
            .ToListAsync();
        remainingLocks.Should().ContainSingle();
        remainingLocks[0].UserId.Should().Be(11);
    }

    private static MetadataDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new MetadataDbContext(options);
    }
}
