using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Infrastructure.Export.Adapters;

public class InternalSubmissionAdapter : RegulatorSubmissionAdapterBase
{
    private static readonly ExportFormat[] Formats = [ExportFormat.PDF];

    public InternalSubmissionAdapter(
        IEnumerable<IExportGenerator> generators,
        ITenantBrandingService brandingService)
        : base(generators, brandingService)
    {
    }

    public override string RegulatorCode => "INTERNAL";
    protected override IReadOnlyList<ExportFormat> SupportedFormats => Formats;
}
