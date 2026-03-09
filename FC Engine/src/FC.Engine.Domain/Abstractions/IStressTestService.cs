using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IStressTestService
{
    Task<StressTestReport> RunStressTestAsync(
        string regulatorCode, StressTestRequest request, CancellationToken ct = default);

    List<StressScenarioInfo> GetAvailableScenarios();

    Task<byte[]> GenerateReportPdfAsync(
        string regulatorCode, StressTestReport report, CancellationToken ct = default);
}
