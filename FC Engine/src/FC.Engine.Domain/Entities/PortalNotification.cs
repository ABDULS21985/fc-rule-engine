using System.ComponentModel.DataAnnotations.Schema;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Notifications;

namespace FC.Engine.Domain.Entities;

/// <summary>
/// Persistent in-app notification for an FI Portal user.
/// Created by submission events, approval actions, deadline checks, and system announcements.
/// </summary>
public class PortalNotification
{
    public int Id { get; set; }

    /// <summary>FK to Tenant for RLS.</summary>
    public Guid TenantId { get; set; }

    // ── Targeting ──

    /// <summary>The institution user this notification is for. Null = broadcast to all users of the institution.</summary>
    public int? UserId { get; set; }

    /// <summary>The institution this notification belongs to.</summary>
    public int InstitutionId { get; set; }

    // ── Content ──

    /// <summary>Domain event code (e.g., return.submitted_for_review).</summary>
    public string EventType { get; set; } = NotificationEvents.SystemAnnouncement;

    /// <summary>Channel represented by this notification record.</summary>
    public NotificationChannel Channel { get; set; } = NotificationChannel.InApp;

    /// <summary>Priority controls urgency/escalation across channels.</summary>
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;

    public NotificationType Type { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";

    /// <summary>Relative URL for the notification's action (e.g., "/submissions/123"). Null = no link.</summary>
    public string? Link { get; set; }

    /// <summary>Email recipient used for email channel delivery records.</summary>
    public string? RecipientEmail { get; set; }

    /// <summary>SMS recipient used for sms channel delivery records.</summary>
    public string? RecipientPhone { get; set; }

    /// <summary>Optional JSON metadata for additional context.</summary>
    public string? Metadata { get; set; }

    /// <summary>Backward-compatible alias used by legacy portal code.</summary>
    [NotMapped]
    public string? MetadataJson
    {
        get => Metadata;
        set => Metadata = value;
    }

    // ── State ──

    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}

/// <summary>Categories of portal notifications. Used for filtering and icon/color assignment.</summary>
public enum NotificationType
{
    /// <summary>Submission accepted, rejected, or accepted with warnings.</summary>
    SubmissionResult,

    /// <summary>A reporting deadline is approaching.</summary>
    DeadlineApproaching,

    /// <summary>A new submission is pending the user's review (Checker).</summary>
    ApprovalRequest,

    /// <summary>A submission the user created was approved or rejected by a Checker.</summary>
    ApprovalResult,

    /// <summary>System-wide announcement.</summary>
    SystemAnnouncement,

    /// <summary>A team member was added, deactivated, or role-changed (Admin only).</summary>
    TeamUpdate
}
