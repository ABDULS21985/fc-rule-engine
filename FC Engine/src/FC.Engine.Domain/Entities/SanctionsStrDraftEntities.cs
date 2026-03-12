namespace FC.Engine.Domain.Entities;

public class SanctionsStrDraftRecord
{
    public int Id { get; set; }
    public string DraftId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string MatchedName { get; set; } = string.Empty;
    public string SourceCode { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public decimal ScorePercent { get; set; }
    public bool FreezeRecommended { get; set; }
    public DateTime ScreenedAtUtc { get; set; }
    public DateTime ReviewDueAtUtc { get; set; }
    public string SuspicionBasis { get; set; } = string.Empty;
    public string GoAmlPayloadSummary { get; set; } = string.Empty;
    public string Narrative { get; set; } = string.Empty;
    public string RecommendedActionsJson { get; set; } = "[]";
    public DateTime MaterializedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
