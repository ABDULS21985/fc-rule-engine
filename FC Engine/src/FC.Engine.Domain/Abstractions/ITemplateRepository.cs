using FC.Engine.Domain.Metadata;

namespace FC.Engine.Domain.Abstractions;

public interface ITemplateRepository
{
    Task<ReturnTemplate?> GetById(int id, CancellationToken ct = default);
    Task<ReturnTemplate?> GetByReturnCode(string returnCode, CancellationToken ct = default);
    Task<ReturnTemplate?> GetPublishedByReturnCode(string returnCode, CancellationToken ct = default);
    Task<IReadOnlyList<ReturnTemplate>> GetAll(CancellationToken ct = default);
    Task<IReadOnlyList<ReturnTemplate>> GetByFrequency(string frequency, CancellationToken ct = default);
    Task Add(ReturnTemplate template, CancellationToken ct = default);
    Task Update(ReturnTemplate template, CancellationToken ct = default);
    Task<bool> ExistsByReturnCode(string returnCode, CancellationToken ct = default);

    /// <summary>Get templates scoped to a tenant (tenant-owned + global templates).</summary>
    Task<IReadOnlyList<ReturnTemplate>> GetAllForTenant(Guid tenantId, CancellationToken ct = default);

    /// <summary>Get a template by return code, scoped to tenant (tenant-owned or global).</summary>
    Task<ReturnTemplate?> GetByReturnCodeForTenant(string returnCode, Guid tenantId, CancellationToken ct = default);

    /// <summary>Get templates filtered by module IDs (for entitlement-scoped queries).</summary>
    Task<IReadOnlyList<ReturnTemplate>> GetByModuleIds(IEnumerable<int> moduleIds, CancellationToken ct = default);

    /// <summary>
    /// Get the most recent Draft or Review version of a template (for impact analysis of pending changes).
    /// Returns <c>null</c> when no in-flight version exists.
    /// </summary>
    Task<TemplateVersion?> GetLatestDraftVersion(string returnCode, CancellationToken ct = default);
}
