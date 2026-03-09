namespace FC.Engine.Domain.Abstractions;

/// <summary>
/// CaaS template engine — retrieves module template metadata for external partners.
/// Implementations adapt the existing ITemplateMetadataCache.
/// </summary>
public interface ITemplateEngine
{
    Task<CaaSModuleTemplate> GetTemplateAsync(
        int institutionId,
        string moduleCode,
        CancellationToken ct = default);
}

public sealed class CaaSModuleTemplate
{
    public string ModuleName { get; init; } = string.Empty;
    public string RegulatorCode { get; init; } = string.Empty;
    public string PeriodType { get; init; } = string.Empty;
    public IReadOnlyList<CaaSTemplateFieldInfo> Fields { get; init; } = Array.Empty<CaaSTemplateFieldInfo>();
    public IReadOnlyList<CaaSFormulaInfo> Formulas { get; init; } = Array.Empty<CaaSFormulaInfo>();
}

public sealed class CaaSTemplateFieldInfo
{
    public string Code { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public string? ValidationRule { get; init; }
    public decimal? MinValue { get; init; }
    public decimal? MaxValue { get; init; }
    public string? Description { get; init; }
}

public sealed class CaaSFormulaInfo
{
    public string Code { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Expression { get; init; } = string.Empty;
}
