using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IContagionCascadeEngine
{
    Task<(
        IReadOnlyList<EntityShockResult> AdditionalFailures,
        IReadOnlyList<ContagionEvent>    Events,
        int                              RoundsExecuted
    )> CascadeAsync(
        IReadOnlyList<EntityShockResult> round0Results,
        string regulatorCode,
        string periodCode,
        long   runId,
        CancellationToken ct = default);
}
