using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.ValueObjects;

namespace FC.Engine.Domain.Abstractions;

public interface IExportGenerator
{
    ExportFormat Format { get; }
    string ContentType { get; }
    string FileExtension { get; }
    Task<byte[]> Generate(ExportGenerationContext context, CancellationToken ct = default);
}

public sealed class ExportGenerationContext
{
    public required Guid TenantId { get; init; }
    public required Submission Submission { get; init; }
    public required BrandingConfig Branding { get; init; }
}
