namespace FC.Engine.Domain.Enums;

/// <summary>
/// Status of a submission approval in the maker-checker workflow.
/// </summary>
public enum ApprovalStatus
{
    /// <summary>Awaiting checker review.</summary>
    Pending = 0,

    /// <summary>Approved by checker — submission is finalized.</summary>
    Approved = 1,

    /// <summary>Rejected by checker — returned to maker for correction.</summary>
    Rejected = 2
}
