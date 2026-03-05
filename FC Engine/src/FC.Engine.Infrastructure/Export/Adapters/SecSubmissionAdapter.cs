using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Infrastructure.Export.Adapters;

public class SecSubmissionAdapter : RegulatorSubmissionAdapterBase
{
    private static readonly ExportFormat[] Formats = [ExportFormat.Excel];

    public SecSubmissionAdapter(
        IEnumerable<IExportGenerator> generators,
        ITenantBrandingService brandingService)
        : base(generators, brandingService)
    {
    }

    public override string RegulatorCode => "SEC";
    protected override IReadOnlyList<ExportFormat> SupportedFormats => Formats;
}
