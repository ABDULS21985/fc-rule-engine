namespace FC.Engine.Domain.Entities;

public class PlatformInterventionRecord
{
    public int Id { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Signal { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string NextAction { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public string OwnerLane { get; set; } = string.Empty;
    public DateTime MaterializedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PlatformActivityTimelineRecord
{
    public int Id { get; set; }
    public Guid? TenantId { get; set; }
    public int? InstitutionId { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public DateTime HappenedAt { get; set; }
    public string Severity { get; set; } = string.Empty;
    public DateTime MaterializedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
