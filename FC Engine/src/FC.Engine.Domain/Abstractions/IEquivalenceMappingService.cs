using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IEquivalenceMappingService
{
    Task<long> CreateMappingAsync(
        string mappingCode, string mappingName, string conceptDomain,
        string? description, IReadOnlyList<EquivalenceEntryInput> entries,
        int userId, CancellationToken ct = default);

    Task AddEntryAsync(
        long mappingId, EquivalenceEntryInput entry,
        int userId, CancellationToken ct = default);

    Task UpdateThresholdAsync(
        long mappingId, string jurisdictionCode, decimal newThreshold,
        int userId, CancellationToken ct = default);

    Task<EquivalenceMappingDetail?> GetMappingAsync(
        long mappingId, CancellationToken ct = default);

    Task<IReadOnlyList<EquivalenceMappingSummary>> ListMappingsAsync(
        string? conceptDomain, CancellationToken ct = default);

    Task<IReadOnlyList<JurisdictionThreshold>> GetCrossBorderComparisonAsync(
        string mappingCode, CancellationToken ct = default);
}
