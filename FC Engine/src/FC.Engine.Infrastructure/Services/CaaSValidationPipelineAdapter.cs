using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Infrastructure.Services;

/// <summary>
/// Bridges ValidationOrchestrator (RG-11 pipeline) to the IValidationPipeline interface
/// used by CaaS. Uses the relaxed validation path that downgrades structural errors
/// to warnings, making it appropriate for externally-supplied partner data.
/// </summary>
public sealed class CaaSValidationPipelineAdapter : IValidationPipeline
{
    private readonly ValidationOrchestrator _orchestrator;
    private readonly ITemplateMetadataCache _cache;

    public CaaSValidationPipelineAdapter(
        ValidationOrchestrator orchestrator,
        ITemplateMetadataCache cache)
    {
        _orchestrator = orchestrator;
        _cache        = cache;
    }

    public async Task<CaaSValidationReport> ValidateAsync(
        int institutionId,
        string moduleCode,
        string periodCode,
        Dictionary<string, object?> fields,
        CancellationToken ct = default)
    {
        var template = await _cache.GetPublishedTemplate(moduleCode, ct);

        // ValidateRelaxed: uses the RG-11 four-phase pipeline but downgrades
        // cross-sheet/structural errors to warnings for external partner use
        var report = await _orchestrator.ValidateRelaxed(
            new List<Dictionary<string, object?>> { fields },
            template,
            Guid.Empty,  // CaaS partner context — no RegOS tenant RLS
            ct);

        // Build a field label lookup from the template for richer violation output
        var labelMap = template.CurrentVersion?.Fields
            .ToDictionary(f => f.FieldName, f => f.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var violations = report.Errors.Select(e => new CaaSViolation
        {
            FieldCode  = e.Field,
            FieldLabel = labelMap.TryGetValue(e.Field, out var label) ? label : e.Field,
            ErrorCode  = e.RuleId,
            Message    = e.Message,
            Severity   = e.Severity == ValidationSeverity.Error ? "ERROR" : "WARNING"
        }).ToList();

        return new CaaSValidationReport
        {
            Violations     = violations,
            TotalFields    = template.CurrentVersion?.Fields.Count ?? 0,
            ComputedValues = new Dictionary<string, object?>()
        };
    }
}
