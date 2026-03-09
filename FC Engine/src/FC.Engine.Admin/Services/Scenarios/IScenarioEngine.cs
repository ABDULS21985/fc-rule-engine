namespace FC.Engine.Admin.Services.Scenarios;

public interface IScenarioEngine
{
    Task<List<ScenarioDefinition>> GetAllScenarios();
    Task<ScenarioDefinition?> GetScenario(int id);
    Task<ScenarioDefinition> SaveScenario(ScenarioDefinition scenario);
    Task DeleteScenario(int id);
    Task<ScenarioResult> RunScenario(Guid tenantId, int scenarioId);
    Task<ComparisonReport> CompareScenarios(Guid tenantId, List<int> scenarioIds);
    Task<MacroPrudentialResult> RunMacroPrudential(Guid tenantId, ScenarioDefinition scenario);
}
