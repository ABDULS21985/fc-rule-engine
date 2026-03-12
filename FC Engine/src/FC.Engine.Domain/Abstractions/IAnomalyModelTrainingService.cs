using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IAnomalyModelTrainingService
{
    Task<AnomalyModelVersion> TrainModuleModelAsync(
        string moduleCode,
        string initiatedBy,
        bool promoteImmediately = false,
        CancellationToken ct = default);

    Task PromoteModelAsync(
        int modelVersionId,
        string promotedBy,
        CancellationToken ct = default);

    Task RollbackModelAsync(
        string moduleCode,
        string rolledBackBy,
        CancellationToken ct = default);

    Task<List<AnomalyModelTrainingSummary>> GetModelHistoryAsync(
        string moduleCode,
        CancellationToken ct = default);
}
