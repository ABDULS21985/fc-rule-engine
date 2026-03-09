using FC.Engine.Domain.Abstractions;
using FC.Engine.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Infrastructure;

namespace FC.Engine.Infrastructure;

/// <summary>
/// Registers all RG-37 Sector-Wide Stress Testing Framework services.
/// Call from Program.cs or DependencyInjection.cs: services.AddStressTestingFramework(configuration);
/// </summary>
public static class StressTestServiceExtensions
{
    public static IServiceCollection AddStressTestingFramework(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IMacroShockTransmitter, MacroShockTransmitter>();
        services.AddScoped<IContagionCascadeEngine, ContagionCascadeEngine>();
        services.AddScoped<INDICExposureCalculator, NDICExposureCalculator>();
        services.AddScoped<IStressTestOrchestrator, StressTestOrchestrator>();
        services.AddScoped<IStressTestReportGenerator, StressTestReportGenerator>();

        QuestPDF.Settings.License = LicenseType.Community;

        return services;
    }
}
