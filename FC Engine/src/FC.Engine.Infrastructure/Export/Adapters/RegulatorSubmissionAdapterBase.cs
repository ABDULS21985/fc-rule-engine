using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Infrastructure.Export.Adapters;

public abstract class RegulatorSubmissionAdapterBase : IRegulatorSubmissionAdapter
{
    private readonly IReadOnlyDictionary<ExportFormat, IExportGenerator> _generatorMap;
    private readonly ITenantBrandingService _brandingService;

    protected RegulatorSubmissionAdapterBase(
        IEnumerable<IExportGenerator> generators,
        ITenantBrandingService brandingService)
    {
        _generatorMap = generators.ToDictionary(x => x.Format);
        _brandingService = brandingService;
    }

    public abstract string RegulatorCode { get; }
    protected abstract IReadOnlyList<ExportFormat> SupportedFormats { get; }

    public virtual async Task<byte[]> Package(Submission submission, ExportFormat preferredFormat, CancellationToken ct = default)
    {
        if (submission is null)
        {
            throw new ArgumentNullException(nameof(submission));
        }

        var effectiveFormat = SupportedFormats.Contains(preferredFormat)
            ? preferredFormat
            : SupportedFormats.First();

        if (!_generatorMap.TryGetValue(effectiveFormat, out var generator))
        {
            throw new InvalidOperationException($"No export generator registered for {effectiveFormat}.");
        }

        var branding = await _brandingService.GetBrandingConfig(submission.TenantId, ct);
        return await generator.Generate(new ExportGenerationContext
        {
            TenantId = submission.TenantId,
            Submission = submission,
            Branding = branding
        }, ct);
    }

    public virtual Task<SubmissionReceipt> Submit(byte[] package, Submission submission, CancellationToken ct = default)
    {
        var receipt = new SubmissionReceipt
        {
            Success = true,
            Reference = $"{RegulatorCode}-{submission.Id}-{DateTime.UtcNow:yyyyMMddHHmmss}",
            Message = "Package generated for manual regulator portal submission.",
            ReceivedAt = DateTime.UtcNow
        };

        return Task.FromResult(receipt);
    }
}
