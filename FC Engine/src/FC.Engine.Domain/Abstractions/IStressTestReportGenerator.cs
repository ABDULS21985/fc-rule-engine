namespace FC.Engine.Domain.Abstractions;

public interface IStressTestReportGenerator
{
    Task<byte[]> GenerateAsync(
        long runId,
        bool anonymiseEntities,
        CancellationToken ct = default);
}
