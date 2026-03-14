using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IRegulatorDataService
{
    Task<Guid?> ResolveEntityByName(string nameOrAlias, CancellationToken ct = default);

    Task<List<(Guid TenantId, string Name, string LicenceCategory)>> SearchEntities(
        string searchTerm,
        string? licenceCategory = null,
        CancellationToken ct = default);

    Task<List<Dictionary<string, object>>> ExecuteRegulatorQuery(
        string templateCode,
        Dictionary<string, object> parameters,
        string regulatorId,
        string regulatorAgency,
        CancellationToken ct = default);

    Task<EntityIntelligenceProfile> GetEntityProfile(Guid tenantId, CancellationToken ct = default);

    Task<SectorIntelligenceSummary> GetSectorSummary(
        string? licenceCategory = null,
        string? periodCode = null,
        CancellationToken ct = default);

    Task<ExaminationBriefing> GenerateExaminationBriefing(Guid tenantId, CancellationToken ct = default);
}
