using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IDivergenceDetectionService
{
    Task<IReadOnlyList<DivergenceAlert>> DetectDivergencesAsync(
        CancellationToken ct = default);

    Task AcknowledgeDivergenceAsync(
        long divergenceId, int userId, CancellationToken ct = default);

    Task ResolveDivergenceAsync(
        long divergenceId, string resolution, int userId,
        CancellationToken ct = default);

    Task<IReadOnlyList<DivergenceAlert>> GetOpenDivergencesAsync(
        string? conceptDomain, DivergenceSeverity? minSeverity,
        CancellationToken ct = default);

    Task<IReadOnlyList<DivergenceAlert>> GetGroupDivergencesAsync(
        int groupId, CancellationToken ct = default);

    Task NotifyGroupsAsync(
        long divergenceId, CancellationToken ct = default);
}
