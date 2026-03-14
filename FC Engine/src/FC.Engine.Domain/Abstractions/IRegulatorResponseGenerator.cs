using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IRegulatorResponseGenerator
{
    Task<RegulatorIqResponse> GenerateAsync(
        string originalQuery,
        RegulatorIntentResult classifiedIntent,
        RegulatorContext context,
        CancellationToken ct = default);
}

public sealed class RegulatorIqResponse
{
    public string AnswerText { get; set; } = string.Empty;
    public string AnswerFormat { get; set; } = "text";
    public object? StructuredData { get; set; }
    public List<DataCitation> Citations { get; set; } = new();
    public List<string> FollowUpSuggestions { get; set; } = new();
    public List<IntelligenceFlag> Flags { get; set; } = new();
    public List<string> DataSourcesUsed { get; set; } = new();
    public List<Guid> EntitiesAccessed { get; set; } = new();
    public string ClassificationLevel { get; set; } = "RESTRICTED";
    public string ConfidenceLevel { get; set; } = "HIGH";
}

public sealed class DataCitation
{
    public string SourceType { get; set; } = string.Empty;
    public string SourceModule { get; set; } = string.Empty;
    public string? SourceField { get; set; }
    public string? SourcePeriod { get; set; }
    public string? InstitutionName { get; set; }
    public string? Summary { get; set; }
}

public sealed class IntelligenceFlag
{
    public string FlagType { get; set; } = "CONCERN";
    public string Message { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}

public sealed class RegulatorTableData
{
    public List<string> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
}

public sealed class RegulatorRankingData
{
    public string MetricCode { get; set; } = string.Empty;
    public string MetricLabel { get; set; } = string.Empty;
    public List<RegulatorRankingItem> Items { get; set; } = new();
}

public sealed class RegulatorRankingItem
{
    public int Rank { get; set; }
    public Guid? TenantId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string LicenceCategory { get; set; } = string.Empty;
    public decimal? Value { get; set; }
    public string RiskBand { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}

public sealed class RegulatorComparisonData
{
    public string? PeriodCode { get; set; }
    public List<string> EntityNames { get; set; } = new();
    public List<RegulatorComparisonRow> Rows { get; set; } = new();
}

public sealed class RegulatorComparisonRow
{
    public string MetricCode { get; set; } = string.Empty;
    public string MetricLabel { get; set; } = string.Empty;
    public Dictionary<string, decimal?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> SourceModules { get; set; } = new();
}

public sealed class RegulatorChartData
{
    public string ChartType { get; set; } = "line";
    public List<string> Labels { get; set; } = new();
    public List<RegulatorChartSeries> Series { get; set; } = new();
}

public sealed class RegulatorChartSeries
{
    public string Name { get; set; } = string.Empty;
    public List<decimal?> Values { get; set; } = new();
}

public sealed class RegulatorProfileData
{
    public EntityIntelligenceProfile Profile { get; set; } = new();
}
