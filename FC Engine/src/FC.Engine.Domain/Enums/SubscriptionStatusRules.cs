namespace FC.Engine.Domain.Enums;

public static class SubscriptionStatusRules
{
    public static readonly SubscriptionStatus[] EntitlementEligibleStatuses =
    [
        SubscriptionStatus.Trial,
        SubscriptionStatus.Active,
        SubscriptionStatus.PastDue
    ];

    public static bool GrantsEntitlement(this SubscriptionStatus status) =>
        EntitlementEligibleStatuses.Contains(status);
}
