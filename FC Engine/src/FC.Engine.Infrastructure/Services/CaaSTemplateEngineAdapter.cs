using FC.Engine.Domain.Abstractions;

namespace FC.Engine.Infrastructure.Services;

/// <summary>
/// Bridges ITemplateMetadataCache (RegOS template store) to the ITemplateEngine
/// interface used by CaaS. Maps CachedTemplate → CaaSModuleTemplate, translating
/// internal field/formula metadata into the public CaaS API shape.
/// </summary>
public sealed class CaaSTemplateEngineAdapter : ITemplateEngine
{
    private readonly ITemplateMetadataCache _cache;

    public CaaSTemplateEngineAdapter(ITemplateMetadataCache cache)
        => _cache = cache;

    public async Task<CaaSModuleTemplate> GetTemplateAsync(
        int institutionId, string moduleCode, CancellationToken ct = default)
    {
        var cached = await _cache.GetPublishedTemplate(moduleCode, ct);

        var version = cached.CurrentVersion
            ?? throw new KeyNotFoundException(
                $"Module '{moduleCode}' has no published template version.");

        var fields = version.Fields.Select(f => new CaaSTemplateFieldInfo
        {
            Code           = f.FieldName,
            Label          = f.DisplayName,
            DataType       = f.DataType.ToString().ToUpperInvariant(),
            IsRequired     = f.IsRequired,
            ValidationRule = f.AllowedValues is not null
                ? $"ALLOWED_VALUES:{f.AllowedValues}"
                : null,
            MinValue    = f.MinValue is not null
                ? decimal.TryParse(f.MinValue, out var mn) ? (decimal?)mn : null : null,
            MaxValue    = f.MaxValue is not null
                ? decimal.TryParse(f.MaxValue, out var mx) ? (decimal?)mx : null : null,
            Description = f.HelpText ?? f.RegulatoryReference
        }).ToArray();

        var formulas = version.IntraSheetFormulas
            .Where(f => f.IsActive)
            .Select(f => new CaaSFormulaInfo
            {
                Code        = f.RuleCode,
                Description = f.RuleName,
                Expression  = f.CustomExpression ?? f.OperandFields
            }).ToArray();

        return new CaaSModuleTemplate
        {
            ModuleName    = cached.Name,
            RegulatorCode = cached.ModuleCode ?? cached.ReturnCode,
            PeriodType    = cached.Frequency.ToString().ToUpperInvariant(),
            Fields        = fields,
            Formulas      = formulas
        };
    }
}
