using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IRootCauseAnalysisService
{
    Task<RootCauseAnalysis> AnalyzeAsync(Guid tenantId, RcaIncidentType type, Guid incidentId, bool forceRefresh = false, CancellationToken ct = default);
    Task<RootCauseAnalysis?> GetCachedAsync(Guid tenantId, RcaIncidentType type, Guid incidentId, CancellationToken ct = default);
    Task<IReadOnlyList<RcaTimelineEntry>> GetTimelineAsync(Guid tenantId, RcaIncidentType type, Guid incidentId, CancellationToken ct = default);
}
