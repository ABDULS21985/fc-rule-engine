using FC.Engine.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.BackgroundJobs;

public sealed class ModuleRegistryBootstrapJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ModuleRegistryBootstrapJob> _logger;

    public ModuleRegistryBootstrapJob(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<ModuleRegistryBootstrapJob> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("ModuleRegistryBootstrap:Enabled", true))
        {
            _logger.LogInformation("Module registry bootstrap job is disabled by configuration.");
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var bootstrapService = scope.ServiceProvider.GetRequiredService<ModuleRegistryBootstrapService>();
            var result = await bootstrapService.EnsureBaselineModulesAsync(stoppingToken);
            _logger.LogInformation(
                "Module registry bootstrap job completed. ModulesCreated={ModulesCreated} ModulesUpdated={ModulesUpdated} MappingsCreated={MappingsCreated} MappingsUpdated={MappingsUpdated}",
                result.ModulesCreated,
                result.ModulesUpdated,
                result.MappingsCreated,
                result.MappingsUpdated);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Module registry bootstrap job failed.");
        }
    }
}
