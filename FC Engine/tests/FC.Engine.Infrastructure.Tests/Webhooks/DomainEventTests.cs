using FC.Engine.Domain.Events;
using FluentAssertions;

namespace FC.Engine.Infrastructure.Tests.Webhooks;

public class DomainEventTests
{
    [Fact]
    public void ReturnCreatedEvent_Has_Correct_EventType()
    {
        var evt = new ReturnCreatedEvent(
            Guid.NewGuid(), 1, "MOD01", "CBN001", "Jan 2026",
            DateTime.UtcNow, DateTime.UtcNow, Guid.NewGuid());

        evt.EventType.Should().Be("return.created");
    }

    [Fact]
    public void ReturnSubmittedEvent_Has_Correct_EventType()
    {
        var evt = new ReturnSubmittedEvent(
            Guid.NewGuid(), 1, "MOD01", "CBN001", "Jan 2026", "maker",
            DateTime.UtcNow, DateTime.UtcNow, Guid.NewGuid());

        evt.EventType.Should().Be("return.submitted_for_review");
    }

    [Fact]
    public void ReturnApprovedEvent_Has_Correct_EventType()
    {
        var evt = new ReturnApprovedEvent(
            Guid.NewGuid(), 1, "MOD01", "CBN001", "Jan 2026", "checker",
            DateTime.UtcNow, DateTime.UtcNow, Guid.NewGuid());

        evt.EventType.Should().Be("return.approved");
    }

    [Fact]
    public void ReturnRejectedEvent_Has_Correct_EventType()
    {
        var evt = new ReturnRejectedEvent(
            Guid.NewGuid(), 1, "MOD01", "CBN001", "Jan 2026", "checker", "Bad data",
            DateTime.UtcNow, DateTime.UtcNow, Guid.NewGuid());

        evt.EventType.Should().Be("return.rejected");
    }

    [Fact]
    public void ValidationCompletedEvent_Has_Correct_EventType()
    {
        var evt = new ValidationCompletedEvent(
            Guid.NewGuid(), 1, "MOD01", 2, 1,
            DateTime.UtcNow, DateTime.UtcNow, Guid.NewGuid());

        evt.EventType.Should().Be("validation.completed");
    }

    [Fact]
    public void SubscriptionChangedEvent_Has_Correct_EventType()
    {
        var evt = new SubscriptionChangedEvent(
            Guid.NewGuid(), "Upgraded", "basic", "professional",
            DateTime.UtcNow, DateTime.UtcNow, Guid.NewGuid());

        evt.EventType.Should().Be("subscription.changed");
    }

    [Fact]
    public void ModuleActivatedEvent_Has_Correct_EventType()
    {
        var evt = new ModuleActivatedEvent(
            Guid.NewGuid(), "CBN", "CBN Returns", DateTime.UtcNow, DateTime.UtcNow, Guid.NewGuid());

        evt.EventType.Should().Be("module.activated");
    }

    [Fact]
    public void DeadlineApproachingEvent_Has_Correct_EventType()
    {
        var evt = new DeadlineApproachingEvent(
            Guid.NewGuid(), "MOD01", "Jan 2026", DateTime.UtcNow, 7,
            DateTime.UtcNow, Guid.NewGuid());

        evt.EventType.Should().Be("deadline.approaching");
    }

    [Fact]
    public void ExportCompletedEvent_Has_Correct_EventType()
    {
        var evt = new ExportCompletedEvent(
            Guid.NewGuid(), 1, "PDF", "/downloads/1", DateTime.UtcNow,
            DateTime.UtcNow, Guid.NewGuid());

        evt.EventType.Should().Be("export.completed");
    }

    [Fact]
    public void UserCreatedEvent_Has_Correct_EventType()
    {
        var evt = new UserCreatedEvent(
            Guid.NewGuid(), 1, "user@test.com", "Maker",
            DateTime.UtcNow, DateTime.UtcNow, Guid.NewGuid());

        evt.EventType.Should().Be("user.provisioned");
    }

    [Fact]
    public void All_Events_Carry_TenantId_And_CorrelationId()
    {
        var tenantId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var evt = new ReturnCreatedEvent(
            tenantId, 1, "MOD01", "CBN001", "Jan 2026",
            DateTime.UtcNow, DateTime.UtcNow, correlationId);

        evt.TenantId.Should().Be(tenantId);
        evt.CorrelationId.Should().Be(correlationId);
    }

    [Fact]
    public void ReturnSubmittedToRegulatorEvent_Has_Correct_EventType()
    {
        var evt = new ReturnSubmittedToRegulatorEvent(
            Guid.NewGuid(), 1, "MOD01", "CBN001", "Jan 2026",
            DateTime.UtcNow, DateTime.UtcNow, Guid.NewGuid());

        evt.EventType.Should().Be("return.submitted_to_regulator");
    }
}
