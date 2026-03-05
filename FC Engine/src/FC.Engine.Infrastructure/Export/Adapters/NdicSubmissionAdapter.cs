using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Infrastructure.Export.Adapters;

public class NdicSubmissionAdapter : RegulatorSubmissionAdapterBase
{
    private static readonly ExportFormat[] Formats = [ExportFormat.Excel];

    public NdicSubmissionAdapter(
        IEnumerable<IExportGenerator> generators,
        ITenantBrandingService brandingService)
        : base(generators, brandingService)
    {
    }

    public override string RegulatorCode => "NDIC";
    protected override IReadOnlyList<ExportFormat> SupportedFormats => Formats;
}
