using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FC.Engine.Infrastructure.Services;

public static class PolicySimulationServiceExtensions
{
    public static IServiceCollection AddPolicySimulation(
        this IServiceCollection services, IConfiguration config)
    {
        // Core services
        services.AddScoped<IPolicyScenarioService, PolicyScenarioService>();
        services.AddScoped<IImpactAssessmentEngine, ImpactAssessmentEngine>();
        services.AddScoped<ICostBenefitAnalyser, CostBenefitAnalyser>();
        services.AddScoped<IConsultationService, ConsultationService>();
        services.AddScoped<IPolicyDecisionService, PolicyDecisionService>();
        services.AddScoped<IHistoricalImpactTracker, HistoricalImpactTrackerService>();
        services.AddScoped<IPolicyAuditLogger, PolicyAuditLogger>();

        // Background workers
        services.AddHostedService<HistoricalImpactTrackerWorker>();
        services.AddHostedService<ConsultationDeadlineMonitorWorker>();

        // Options
        services.Configure<PolicySimulationOptions>(config.GetSection(PolicySimulationOptions.SectionName));

        return services;
    }
}
