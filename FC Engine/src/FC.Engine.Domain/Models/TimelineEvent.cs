namespace FC.Engine.Domain.Models;

public class TimelineEvent
{
    public DateTime Timestamp { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PerformedBy { get; set; } = string.Empty;
    public Dictionary<string, object?>? Diff { get; set; }
}
