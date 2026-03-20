using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Entities;

public class Subscription
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public int PlanId { get; set; }
    public SubscriptionStatus Status { get; private set; } = SubscriptionStatus.Trial;
    public BillingFrequency BillingFrequency { get; set; } = BillingFrequency.Monthly;
    public DateTime CurrentPeriodStart { get; set; }
    public DateTime CurrentPeriodEnd { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public DateTime? GracePeriodEndsAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Tenant? Tenant { get; set; }
    public SubscriptionPlan? Plan { get; set; }
    public ICollection<SubscriptionModule> Modules { get; set; } = new List<SubscriptionModule>();
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    public void Activate()
    {
        if (Status is not (SubscriptionStatus.Trial or SubscriptionStatus.Suspended or SubscriptionStatus.PastDue))
            throw new InvalidOperationException($"Cannot activate from {Status}");

        Status = SubscriptionStatus.Active;
        TrialEndsAt = null;
        GracePeriodEndsAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkPastDue()
    {
        if (Status != SubscriptionStatus.Active)
            throw new InvalidOperationException($"Cannot mark PastDue from {Status}");

        Status = SubscriptionStatus.PastDue;
        GracePeriodEndsAt = DateTime.UtcNow.AddDays(14);
        UpdatedAt = DateTime.UtcNow;
    }

    public void Suspend(string? reason = null)
    {
        if (Status is not (SubscriptionStatus.PastDue or SubscriptionStatus.Active))
            throw new InvalidOperationException($"Cannot suspend from {Status}");

        Status = SubscriptionStatus.Suspended;
        CancellationReason = reason;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Reactivate()
    {
        if (Status is not (SubscriptionStatus.PastDue or SubscriptionStatus.Suspended))
            throw new InvalidOperationException($"Cannot reactivate from {Status}");

        Status = SubscriptionStatus.Active;
        GracePeriodEndsAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Cancel(string reason)
    {
        if (Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Expired)
            throw new InvalidOperationException($"Already terminal: {Status}");

        Status = SubscriptionStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        CancellationReason = reason;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Expire()
    {
        if (Status != SubscriptionStatus.Trial)
            throw new InvalidOperationException("Only Trial subscriptions can expire");

        Status = SubscriptionStatus.Expired;
        UpdatedAt = DateTime.UtcNow;
    }

    public void AdvancePeriod()
    {
        CurrentPeriodStart = CurrentPeriodEnd;
        CurrentPeriodEnd = BillingFrequency == BillingFrequency.Monthly
            ? CurrentPeriodStart.AddMonths(1)
            : CurrentPeriodStart.AddYears(1);
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsActiveForEntitlement()
    {
        return Status.GrantsEntitlement();
    }
}
