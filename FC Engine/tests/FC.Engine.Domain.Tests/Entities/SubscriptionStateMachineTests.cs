using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FC.Engine.Domain.Tests.Entities;

public class SubscriptionStateMachineTests
{
    private static Subscription CreateSubscription(SubscriptionStatus status = SubscriptionStatus.Trial)
    {
        var subscription = new Subscription
        {
            TenantId = Guid.NewGuid(),
            PlanId = 1,
            BillingFrequency = BillingFrequency.Monthly,
            CurrentPeriodStart = DateTime.UtcNow,
            CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1)
        };

        if (status == SubscriptionStatus.Active)
        {
            subscription.Activate();
        }
        else if (status == SubscriptionStatus.PastDue)
        {
            subscription.Activate();
            subscription.MarkPastDue();
        }
        else if (status == SubscriptionStatus.Suspended)
        {
            subscription.Activate();
            subscription.MarkPastDue();
            subscription.Suspend();
        }

        return subscription;
    }

    [Fact]
    public void Create_Subscription_Starts_As_Trial()
    {
        var subscription = CreateSubscription();

        subscription.Status.Should().Be(SubscriptionStatus.Trial);
    }

    [Fact]
    public void Activate_After_First_Payment()
    {
        var subscription = CreateSubscription();

        subscription.Activate();

        subscription.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public void Cannot_Suspend_Trial()
    {
        var subscription = CreateSubscription();

        var act = () => subscription.Suspend();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PastDue_After_Invoice_Overdue()
    {
        var subscription = CreateSubscription(SubscriptionStatus.Active);

        subscription.MarkPastDue();

        subscription.Status.Should().Be(SubscriptionStatus.PastDue);
        subscription.GracePeriodEndsAt.Should().NotBeNull();
    }

    [Fact]
    public void Payment_Reactivates_PastDue()
    {
        var subscription = CreateSubscription(SubscriptionStatus.PastDue);

        subscription.Reactivate();

        subscription.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public void Trial_Expires_Without_Payment()
    {
        var subscription = CreateSubscription();

        subscription.Expire();

        subscription.Status.Should().Be(SubscriptionStatus.Expired);
    }
}
