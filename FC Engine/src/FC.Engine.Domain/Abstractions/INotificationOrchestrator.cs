using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Abstractions;

public interface INotificationOrchestrator
{
    Task Notify(NotificationRequest request, CancellationToken ct = default);
}

public class NotificationRequest
{
    public Guid TenantId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;
    public bool IsMandatory { get; set; }
    public string? ActionUrl { get; set; }
    public Dictionary<string, string> Data { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Targeting
    public List<int> RecipientUserIds { get; set; } = new();
    public List<int> RecipientPortalUserIds { get; set; } = new();
    public List<string> RecipientRoles { get; set; } = new();
    public int? RecipientInstitutionId { get; set; }

    public NotificationPayload ToPayload() => new()
    {
        Title = Title,
        Message = Message,
        EventType = EventType,
        Priority = Priority,
        ActionUrl = ActionUrl,
        Timestamp = DateTime.UtcNow
    };
}

public class NotificationRecipient
{
    public int UserId { get; set; }
    public Guid TenantId { get; set; }
    public int InstitutionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
}
