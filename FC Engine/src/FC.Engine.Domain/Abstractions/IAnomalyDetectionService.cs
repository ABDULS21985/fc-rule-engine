using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IAnomalyDetectionService
{
    Task<AnomalyReport> AnalyzeSubmissionAsync(
        int submissionId,
        Guid tenantId,
        string performedBy,
        CancellationToken ct = default);

    Task<AnomalyReport?> GetLatestReportForSubmissionAsync(
        int submissionId,
        Guid tenantId,
        CancellationToken ct = default);

    Task<AnomalyReport?> GetReportByIdAsync(
        int reportId,
        Guid tenantId,
        CancellationToken ct = default);

    Task<List<AnomalyReport>> GetReportsForTenantAsync(
        Guid tenantId,
        string? moduleCode = null,
        string? periodCode = null,
        CancellationToken ct = default);

    Task<List<AnomalySectorSummary>> GetSectorSummaryAsync(
        string regulatorCode,
        string? moduleCode = null,
        string? periodCode = null,
        CancellationToken ct = default);

    Task AcknowledgeFindingAsync(
        AnomalyAcknowledgementRequest request,
        CancellationToken ct = default);

    Task RevokeAcknowledgementAsync(
        int findingId,
        Guid tenantId,
        string revokedBy,
        CancellationToken ct = default);

    Task<byte[]> ExportReportPdfAsync(
        int reportId,
        Guid tenantId,
        CancellationToken ct = default);
}
