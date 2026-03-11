using FC.Engine.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.BackgroundJobs;

public sealed class SeedModuleDefinitionBootstrapJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SeedModuleDefinitionBootstrapJob> _logger;

    public SeedModuleDefinitionBootstrapJob(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<SeedModuleDefinitionBootstrapJob> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("SeedModuleDefinitions:Enabled", true))
        {
            _logger.LogInformation("Seed module definition bootstrap job is disabled by configuration.");
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var bootstrapService = scope.ServiceProvider.GetRequiredService<SeedModuleDefinitionBootstrapService>();
            var result = await bootstrapService.EnsureSeedModulesInstalledAsync(stoppingToken);
            _logger.LogInformation(
                "Seed module definition bootstrap completed. Imported={Imported} Published={Published} Warnings={Warnings} Errors={Errors}",
                result.ModulesImported,
                result.ModulesPublished,
                result.Warnings.Count,
                result.Errors.Count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Seed module definition bootstrap job failed.");
        }
    }
}
