using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IReturnTimelineService
{
    Task<List<TimelineEvent>> GetTimelineAsync(int submissionId, CancellationToken ct = default);
}
