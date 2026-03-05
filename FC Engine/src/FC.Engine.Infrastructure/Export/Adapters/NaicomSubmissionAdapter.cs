using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Infrastructure.Export.Adapters;

public class NaicomSubmissionAdapter : RegulatorSubmissionAdapterBase
{
    private static readonly ExportFormat[] Formats = [ExportFormat.Excel];

    public NaicomSubmissionAdapter(
        IEnumerable<IExportGenerator> generators,
        ITenantBrandingService brandingService)
        : base(generators, brandingService)
    {
    }

    public override string RegulatorCode => "NAICOM";
    protected override IReadOnlyList<ExportFormat> SupportedFormats => Formats;
}
