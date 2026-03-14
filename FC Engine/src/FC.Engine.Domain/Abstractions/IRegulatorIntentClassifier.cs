namespace FC.Engine.Domain.Abstractions;

public interface IRegulatorIntentClassifier
{
    Task<RegulatorIntentResult> ClassifyAsync(
        string userQuery,
        RegulatorContext context,
        CancellationToken ct = default);
}

public sealed class RegulatorIntentResult
{
    public string IntentCode { get; set; } = "UNCLEAR";
    public decimal Confidence { get; set; }
    public Dictionary<string, string> ExtractedParameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ResolvedEntityNames { get; set; } = new();
    public List<Guid> ResolvedEntityIds { get; set; } = new();
    public string? PeriodCode { get; set; }
    public string? FieldCode { get; set; }
    public string? LicenceCategory { get; set; }
    public bool NeedsDisambiguation { get; set; }
    public List<string>? DisambiguationOptions { get; set; }
}

public sealed class RegulatorContext
{
    public Guid RegulatorTenantId { get; set; }
    public string RegulatorCode { get; set; } = string.Empty;
    public string RegulatorName { get; set; } = string.Empty;
    public Guid? CurrentExaminationEntityId { get; set; }
    public List<(Guid TenantId, string Name)> RecentEntities { get; set; } = new();
    public string? CurrentScope { get; set; }
    public List<(string Query, string Intent)> RecentTurns { get; set; } = new();
}
