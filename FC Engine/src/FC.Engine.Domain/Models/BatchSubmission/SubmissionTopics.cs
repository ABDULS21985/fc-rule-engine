namespace FC.Engine.Domain.Models.BatchSubmission;

/// <summary>
/// Event topic (subject) constants for the submission pipeline event bus.
/// Used by ISubmissionEventPublisher and consumers to ensure consistent routing.
/// </summary>
public static class SubmissionTopics
{
    public const string Initiated      = "submission.initiated";
    public const string Signed         = "submission.signed";
    public const string Dispatched     = "submission.dispatched";
    public const string Acknowledged   = "submission.acknowledged";
    public const string StatusChanged  = "submission.status.changed";
    public const string QueryReceived  = "submission.query.received";
    public const string QueryResponded = "submission.query.responded";
    public const string FinalAccepted  = "submission.final.accepted";
    public const string Rejected       = "submission.rejected";
    public const string RetryScheduled = "submission.retry.scheduled";
}
