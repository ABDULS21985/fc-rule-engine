namespace FC.Engine.Admin.Services.Scenarios;

public interface IScenarioTemplateService
{
    List<ScenarioTemplate> GetAllTemplates();
    ScenarioTemplate? GetTemplate(string id);
    ScenarioDefinition CreateFromTemplate(string templateId);
}
