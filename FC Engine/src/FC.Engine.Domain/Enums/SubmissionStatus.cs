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
    ApprovalRejected,

    /// <summary>Imported legacy data — read-only, not submittable, excluded from workflow transitions.</summary>
    Historical,

    /// <summary>Submission has been sent to the regulator via direct API (RG-34).</summary>
    SubmittedToRegulator,

    /// <summary>Regulator has acknowledged receipt of the submission.</summary>
    RegulatorAcknowledged,

    /// <summary>Regulator has fully accepted the submission.</summary>
    RegulatorAccepted,

    /// <summary>Regulator has raised queries about the submission.</summary>
    RegulatorQueriesRaised
}
