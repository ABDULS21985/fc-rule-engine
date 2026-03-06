namespace FC.Engine.Domain.Entities;

public class SavedReport
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public int InstitutionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Definition { get; set; } = "{}";
    public bool IsShared { get; set; }
    public int CreatedByUserId { get; set; }

    // Schedule
    public string? ScheduleCron { get; set; }
    public string? ScheduleFormat { get; set; }
    public string? ScheduleRecipients { get; set; }
    public bool IsScheduleActive { get; set; }
    public DateTime? LastRunAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
