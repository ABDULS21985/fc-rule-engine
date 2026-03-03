using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Entities;

/// <summary>
/// Tracks the maker-checker approval workflow for a submission.
/// </summary>
public class SubmissionApproval
{
    public int Id { get; set; }

    /// <summary>FK to Submission.</summary>
    public int SubmissionId { get; set; }

    /// <summary>The Maker who requested approval.</summary>
    public int RequestedByUserId { get; set; }

    /// <summary>The Checker who reviewed (null if still pending).</summary>
    public int? ReviewedByUserId { get; set; }

    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    /// <summary>Checker's comments (required when rejecting).</summary>
    public string? ReviewerComments { get; set; }

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ReviewedAt { get; set; }

    // Navigation
    public Submission? Submission { get; set; }
    public InstitutionUser? RequestedBy { get; set; }
    public InstitutionUser? ReviewedBy { get; set; }
}
