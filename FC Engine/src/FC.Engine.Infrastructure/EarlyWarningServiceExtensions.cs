using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.BackgroundJobs;
using FC.Engine.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FC.Engine.Infrastructure;

/// <summary>
/// Registers all RG-36 Early Warning &amp; Systemic Risk Engine services.
/// Call from Program.cs or DependencyInjection.cs.
/// </summary>
public static class EarlyWarningServiceExtensions
{
    public static IServiceCollection AddEarlyWarningEngine(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Core engine services ──────────────────────────────────────────
        services.AddScoped<IEWIEngine, EWIEngine>();
        services.AddScoped<ICAMELSScorer, CAMELSScorer>();
        services.AddScoped<ISystemicRiskAggregator, SystemicRiskAggregatorService>();
        services.AddScoped<IContagionAnalyzer, ContagionAnalyzer>();
        services.AddScoped<ISupervisoryActionEngine, SupervisoryActionEngine>();
        services.AddScoped<IHeatmapQueryService, HeatmapQueryService>();

        // ── Background computation (hourly cycle) ─────────────────────────
        services.AddHostedService<EWICycleBackgroundService>();

        // ── Options ───────────────────────────────────────────────────────
        services.Configure<EWIEngineOptions>(
            configuration.GetSection("EWIEngine"));

        return services;
    }
}
