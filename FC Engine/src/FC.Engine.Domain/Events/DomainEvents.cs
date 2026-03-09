namespace FC.Engine.Domain.Events;

/// <summary>
/// Marker interface for all domain events published to the event bus.
/// </summary>
public interface IDomainEvent
{
    Guid TenantId { get; }
    string EventType { get; }
    DateTime OccurredAt { get; }
    Guid CorrelationId { get; }
}

// ── Return lifecycle ──

public record ReturnCreatedEvent(
    Guid TenantId,
    int SubmissionId,
    string ModuleCode,
    string ReturnCode,
    string PeriodLabel,
    DateTime CreatedAt,
    DateTime OccurredAt,
    Guid CorrelationId) : IDomainEvent
{
    public string EventType => "return.created";
}

public record ReturnSubmittedEvent(
    Guid TenantId,
    int SubmissionId,
    string ModuleCode,
    string ReturnCode,
    string PeriodLabel,
    string SubmittedBy,
    DateTime SubmittedAt,
    DateTime OccurredAt,
    Guid CorrelationId) : IDomainEvent
{
    public string EventType => "return.submitted_for_review";
}

public record ReturnApprovedEvent(
    Guid TenantId,
    int SubmissionId,
    string ModuleCode,
    string ReturnCode,
    string PeriodLabel,
    string ApprovedBy,
    DateTime ApprovedAt,
    DateTime OccurredAt,
    Guid CorrelationId) : IDomainEvent
{
    public string EventType => "return.approved";
}

public record ReturnRejectedEvent(
    Guid TenantId,
    int SubmissionId,
    string ModuleCode,
    string ReturnCode,
    string PeriodLabel,
    string RejectedBy,
    string Reason,
    DateTime RejectedAt,
    DateTime OccurredAt,
    Guid CorrelationId) : IDomainEvent
{
    public string EventType => "return.rejected";
}

public record ReturnSubmittedToRegulatorEvent(
    Guid TenantId,
    int SubmissionId,
    string ModuleCode,
    string ReturnCode,
    string PeriodLabel,
    DateTime SubmittedAt,
    DateTime OccurredAt,
    Guid CorrelationId) : IDomainEvent
{
    public string EventType => "return.submitted_to_regulator";
}

// ── Validation ──

public record ValidationCompletedEvent(
    Guid TenantId,
    int SubmissionId,
    string ModuleCode,
    int ErrorCount,
    int WarningCount,
    DateTime CompletedAt,
    DateTime OccurredAt,
    Guid CorrelationId) : IDomainEvent
{
    public string EventType => "validation.completed";
}

// ── Deadlines ──

public record DeadlineApproachingEvent(
    Guid TenantId,
    string ModuleCode,
    string PeriodLabel,
    DateTime Deadline,
    int DaysRemaining,
    DateTime OccurredAt,
    Guid CorrelationId) : IDomainEvent
{
    public string EventType => "deadline.approaching";
}

// ── Subscription ──

public record SubscriptionChangedEvent(
    Guid TenantId,
    string ChangeType,
    string PreviousPlan,
    string NewPlan,
    DateTime ChangedAt,
    DateTime OccurredAt,
    Guid CorrelationId) : IDomainEvent
{
    public string EventType => "subscription.changed";
}

// ── Modules ──

public record ModuleActivatedEvent(
    Guid TenantId,
    string ModuleCode,
    string ModuleName,
    DateTime ActivatedAt,
    DateTime OccurredAt,
    Guid CorrelationId) : IDomainEvent
{
    public string EventType => "module.activated";
}

// ── Users ──

public record UserCreatedEvent(
    Guid TenantId,
    int UserId,
    string Email,
    string Role,
    DateTime CreatedAt,
    DateTime OccurredAt,
    Guid CorrelationId) : IDomainEvent
{
    public string EventType => "user.provisioned";
}

// ── Export ──

public record ExportCompletedEvent(
    Guid TenantId,
    int ExportRequestId,
    string Format,
    string DownloadUrl,
    DateTime CompletedAt,
    DateTime OccurredAt,
    Guid CorrelationId) : IDomainEvent
{
    public string EventType => "export.completed";
}

// ── Direct Regulatory Submission (RG-34) ──

public record ReturnDirectSubmittedEvent(
    Guid TenantId,
    int SubmissionId,
    string ModuleCode,
    string ReturnCode,
    string RegulatorCode,
    string RegulatorReference,
    DateTime SubmittedAt,
    DateTime OccurredAt,
    Guid CorrelationId) : IDomainEvent
{
    public string EventType => "return.direct_submitted";
}

public record ReturnDirectSubmissionFailedEvent(
    Guid TenantId,
    int SubmissionId,
    string RegulatorCode,
    string ErrorMessage,
    int AttemptCount,
    DateTime OccurredAt,
    Guid CorrelationId) : IDomainEvent
{
    public string EventType => "return.direct_submission_failed";
}

public record RegulatorQueryRoutedEvent(
    Guid TenantId,
    int SubmissionId,
    string RegulatorCode,
    string QueryId,
    string QueryText,
    DateTime OccurredAt,
    Guid CorrelationId) : IDomainEvent
{
    public string EventType => "return.regulator_query_routed";
}

// ── Compliance Health Score ──

public record ComplianceScoreChangedEvent(
    Guid TenantId,
    decimal PreviousScore,
    decimal NewScore,
    string Rating,
    string Trend,
    DateTime ComputedAt,
    DateTime OccurredAt,
    Guid CorrelationId) : IDomainEvent
{
    public string EventType => "compliance.score_changed";
}
