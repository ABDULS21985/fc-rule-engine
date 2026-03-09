using FC.Engine.Domain.Events;
using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IDataProtectionService
{
    Task<DataSourceSummary> UpsertDataSourceAsync(Guid tenantId, DataSourceRegistrationRequest request, CancellationToken ct = default);
    Task<DataPipelineSummary> UpsertPipelineAsync(Guid tenantId, DataPipelineDefinitionRequest request, CancellationToken ct = default);
    Task<CyberAssetSummary> UpsertAssetAsync(Guid tenantId, CyberAssetRegistrationRequest request, CancellationToken ct = default);
    Task AddAssetDependencyAsync(Guid tenantId, Guid assetId, Guid dependsOnAssetId, CancellationToken ct = default);
    Task<DspmAlertSummary> ReportSecurityAlertAsync(Guid tenantId, SecurityAlertReport report, CancellationToken ct = default);
    Task RecordSecurityEventAsync(Guid tenantId, SecurityEventReport report, CancellationToken ct = default);
    Task<DataPipelineExecutionSummary> RecordPipelineEventAsync(Guid tenantId, PipelineEventReport report, CancellationToken ct = default);
    Task HandlePipelineLifecycleEventAsync(DataPipelineLifecycleEvent pipelineEvent, CancellationToken ct = default);
    Task<IReadOnlyList<DspmScanSummary>> GetScanHistoryAsync(Guid tenantId, Guid? sourceId = null, CancellationToken ct = default);
    Task<IReadOnlyList<ShadowCopyMatch>> GetShadowCopiesAsync(Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<DspmAlertSummary>> GetSecurityAlertsAsync(Guid tenantId, string? alertType = null, CancellationToken ct = default);
    Task RunAtRestScanAsync(Guid? tenantId = null, CancellationToken ct = default);
    Task RunShadowCopyDetectionAsync(Guid? tenantId = null, CancellationToken ct = default);
}
