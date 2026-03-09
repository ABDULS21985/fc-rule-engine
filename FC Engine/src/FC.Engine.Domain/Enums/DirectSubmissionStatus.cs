namespace FC.Engine.Domain.Enums;

public enum DirectSubmissionStatus
{
    Pending = 0,
    Packaging = 1,
    Signing = 2,
    Submitting = 3,
    Submitted = 4,
    Acknowledged = 5,
    Accepted = 6,
    Rejected = 7,

    /// <summary>NFIU goAML quality feedback received.</summary>
    QualityFeedback = 8,

    Failed = 9,
    RetryScheduled = 10,

    /// <summary>All retries exhausted without success.</summary>
    Exhausted = 11
}
