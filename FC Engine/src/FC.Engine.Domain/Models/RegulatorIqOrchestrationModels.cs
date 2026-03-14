using FC.Engine.Domain.Abstractions;

namespace FC.Engine.Domain.Models;

public sealed class RegulatorIqQueryRequest
{
    public string Query { get; set; } = string.Empty;
    public Guid? RegulatorTenantId { get; set; }
    public string? RegulatorId { get; set; }
    public string? UserRole { get; set; }
    public string? RegulatorCode { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? ExaminationTargetTenantId { get; set; }
    public string? Scope { get; set; }
    public string? IpAddress { get; set; }
    public string? SessionId { get; set; }
}

public sealed class RegulatorIqTurnResult
{
    public Guid ConversationId { get; set; }
    public int? TurnId { get; set; }
    public string IntentCode { get; set; } = "UNCLEAR";
    public bool NeedsDisambiguation { get; set; }
    public List<string> DisambiguationOptions { get; set; } = new();
    public RegulatorIqResponse Response { get; set; } = new();
    public int TotalTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
}
