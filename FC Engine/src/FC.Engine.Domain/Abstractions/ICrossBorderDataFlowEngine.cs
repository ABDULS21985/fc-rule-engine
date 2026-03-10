using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface ICrossBorderDataFlowEngine
{
    Task<long> DefineFlowAsync(
        int groupId, DataFlowDefinition definition,
        int userId, CancellationToken ct = default);

    Task<IReadOnlyList<DataFlowExecutionResult>> ExecuteFlowsAsync(
        int groupId, string reportingPeriod,
        CancellationToken ct = default);

    Task<DataFlowExecutionResult?> ExecuteSingleFlowAsync(
        long flowId, int groupId, string reportingPeriod,
        CancellationToken ct = default);

    Task<IReadOnlyList<DataFlowSummary>> ListFlowsAsync(
        int groupId, string? sourceJurisdiction, string? targetJurisdiction,
        CancellationToken ct = default);

    Task<IReadOnlyList<DataFlowExecutionResult>> GetExecutionHistoryAsync(
        long flowId, int groupId, int page, int pageSize,
        CancellationToken ct = default);
}
