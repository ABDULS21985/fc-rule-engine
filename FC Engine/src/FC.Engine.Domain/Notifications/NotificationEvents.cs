namespace FC.Engine.Domain.Notifications;

public static class NotificationEvents
{
    // Return lifecycle
    public const string ReturnCreated = "return.created";
    public const string ReturnSubmittedForReview = "return.submitted_for_review";
    public const string ReturnApproved = "return.approved";
    public const string ReturnRejected = "return.rejected";
    public const string ReturnSubmittedToRegulator = "return.submitted_to_regulator";
    public const string ReturnQueryRaised = "return.query_raised";

    // Deadlines
    public const string DeadlineT30 = "deadline.t30";
    public const string DeadlineT14 = "deadline.t14";
    public const string DeadlineT7 = "deadline.t7";
    public const string DeadlineT3 = "deadline.t3";
    public const string DeadlineT1 = "deadline.t1";
    public const string DeadlineOverdue = "deadline.overdue";

    // Subscription
    public const string TrialExpiring = "subscription.trial_expiring";
    public const string PaymentOverdue = "subscription.payment_overdue";
    public const string SubscriptionSuspended = "subscription.suspended";
    public const string ModuleActivated = "subscription.module_activated";

    // Users
    public const string UserInvited = "user.invited";
    public const string PasswordReset = "user.password_reset";
    public const string MfaCodeSms = "user.mfa_code";

    // System
    public const string SystemAnnouncement = "system.announcement";
    public const string ExportReady = "export.ready";
    public const string DataFlowCompleted = "data_flow.completed";
    public const string BreachDetected = "breach.detected";
    public const string BreachEscalation = "breach.escalation";

    // Reports (RG-18)
    public const string ScheduledReportReady = "report.scheduled_ready";

    // Webhooks (RG-30)
    public const string ValidationCompleted = "validation.completed";
    public const string ExportCompleted = "export.completed";
    public const string UserProvisioned = "user.provisioned";
    public const string WebhookTest = "webhook.test";

    public static readonly IReadOnlyList<string> All = new[]
    {
        ReturnCreated,
        ReturnSubmittedForReview,
        ReturnApproved,
        ReturnRejected,
        ReturnSubmittedToRegulator,
        ReturnQueryRaised,
        DeadlineT30,
        DeadlineT14,
        DeadlineT7,
        DeadlineT3,
        DeadlineT1,
        DeadlineOverdue,
        TrialExpiring,
        PaymentOverdue,
        SubscriptionSuspended,
        ModuleActivated,
        UserInvited,
        PasswordReset,
        MfaCodeSms,
        SystemAnnouncement,
        ExportReady,
        DataFlowCompleted,
        BreachDetected,
        BreachEscalation,
        ScheduledReportReady,
        ValidationCompleted,
        ExportCompleted,
        UserProvisioned,
        WebhookTest
    };
}

public static class NotificationPolicy
{
    public static readonly HashSet<string> MandatoryEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        NotificationEvents.DeadlineOverdue,
        NotificationEvents.SubscriptionSuspended,
        NotificationEvents.PaymentOverdue,
        NotificationEvents.MfaCodeSms
    };
}
