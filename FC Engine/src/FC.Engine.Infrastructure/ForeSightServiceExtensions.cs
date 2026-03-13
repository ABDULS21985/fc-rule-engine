using FC.Engine.Domain.Abstractions;
using FC.Engine.Infrastructure.BackgroundJobs;
using FC.Engine.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FC.Engine.Infrastructure;

public static class ForeSightServiceExtensions
{
    public static IServiceCollection AddForeSightEngine(this IServiceCollection services)
    {
        services.AddScoped<IForeSightService, ForeSightService>();
        services.AddHostedService<ForeSightDailyComputationJob>();
        return services;
    }
}
