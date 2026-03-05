using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Abstractions;

public interface IExportEngine
{
    Task<int> QueueExport(Guid tenantId, int submissionId, ExportFormat format, int requestedByUserId, CancellationToken ct = default);
    Task<ExportResult> GenerateExport(int exportRequestId, CancellationToken ct = default);
    Task<Stream> DownloadExport(int exportRequestId, Guid tenantId, CancellationToken ct = default);
    Task<List<ExportRequest>> GetExportHistory(Guid tenantId, int submissionId, CancellationToken ct = default);
}

public class ExportResult
{
    public bool Success { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}
