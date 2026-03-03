namespace FC.Engine.Domain.Enums;

public enum SubmissionStatus
{
    Draft,
    Parsing,
    Validating,
    Accepted,
    AcceptedWithWarnings,
    Rejected,

    /// <summary>Awaiting checker approval in maker-checker workflow.</summary>
    PendingApproval,

    /// <summary>Rejected by checker — returned to maker for correction.</summary>
    ApprovalRejected
}
