using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

/// <summary>
/// Manages the lifecycle of policy scenarios from draft through enactment.
/// All methods enforce regulator-scoped access.
/// </summary>
public interface IPolicyScenarioService
{
    Task<long> CreateScenarioAsync(
        int regulatorId,
        string title,
        string? description,
        PolicyDomain domain,
        string targetEntityTypes,
        DateOnly baselineDate,
        int createdByUserId,
        CancellationToken ct = default);

    Task AddParameterAsync(
        long scenarioId,
        int regulatorId,
        string parameterCode,
        decimal proposedValue,
        string? applicableEntityTypes,
        int userId,
        CancellationToken ct = default);

    Task UpdateParameterAsync(
        long scenarioId,
        int regulatorId,
        string parameterCode,
        decimal newProposedValue,
        int userId,
        CancellationToken ct = default);

    Task<PolicyScenarioDetail> GetScenarioAsync(
        long scenarioId,
        int regulatorId,
        CancellationToken ct = default);

    Task<PagedResult<PolicyScenarioSummary>> ListScenariosAsync(
        int regulatorId,
        PolicyDomain? domain,
        PolicyStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<long> CloneScenarioAsync(
        long sourceScenarioId,
        int regulatorId,
        string newTitle,
        int userId,
        CancellationToken ct = default);

    Task TransitionStatusAsync(
        long scenarioId,
        int regulatorId,
        PolicyStatus newStatus,
        int userId,
        CancellationToken ct = default);
}
