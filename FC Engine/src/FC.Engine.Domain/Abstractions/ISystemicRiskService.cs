using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface ISystemicRiskService
{
    Task<SystemicRiskDashboard> GetDashboard(string regulatorCode, CancellationToken ct = default);
    Task<List<CamelsScore>> ComputeCamelsScores(string regulatorCode, CancellationToken ct = default);
    Task<List<SystemicEwi>> ComputeSystemicIndicators(string regulatorCode, CancellationToken ct = default);
    Task<ContagionAnalysis> AnalyzeContagion(string regulatorCode, CancellationToken ct = default);
    Task<SupervisoryAction> GenerateSupervisoryAction(string regulatorCode, int institutionId, string flagCode, CancellationToken ct = default);
}
