namespace FC.Engine.Domain.Entities;

public class ModelInventoryDefinitionRecord
{
    public int Id { get; set; }
    public string ModelCode { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string ReturnHint { get; set; } = string.Empty;
    public string MatchTermsJson { get; set; } = "[]";
    public DateTime MaterializedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
