using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IRegulatorIntelligenceService
{
    Task<EntityIntelligenceProfile> GetEntityProfileAsync(
        Guid targetTenantId,
        string regulatorCode,
        CancellationToken ct = default);

    Task<SectorIntelligenceSummary> GetSectorSummaryAsync(
        string regulatorCode,
        string? licenceCategory = null,
        string? periodCode = null,
        CancellationToken ct = default);

    Task<List<MetricRankingEntry>> RankEntitiesByMetricAsync(
        string fieldCode,
        string regulatorCode,
        string? licenceCategory = null,
        string? periodCode = null,
        int topN = 20,
        bool ascending = false,
        CancellationToken ct = default);

    Task<ExaminationBriefing> GenerateExaminationBriefingAsync(
        Guid targetTenantId,
        string regulatorCode,
        CancellationToken ct = default);
}
