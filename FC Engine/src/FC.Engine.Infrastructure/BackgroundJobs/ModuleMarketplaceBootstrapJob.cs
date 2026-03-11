using FC.Engine.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.BackgroundJobs;

public sealed class ModuleMarketplaceBootstrapJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ModuleMarketplaceBootstrapJob> _logger;

    public ModuleMarketplaceBootstrapJob(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<ModuleMarketplaceBootstrapJob> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("ModuleMarketplaceBootstrap:Enabled", true))
        {
            _logger.LogInformation("Module marketplace bootstrap job is disabled by configuration.");
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var bootstrapService = scope.ServiceProvider.GetRequiredService<ModuleMarketplaceBootstrapService>();
            var result = await bootstrapService.EnsurePricingAsync(stoppingToken);
            _logger.LogInformation(
                "Module marketplace bootstrap completed. PricingCreated={Created} PricingUpdated={Updated}",
                result.PricingCreated,
                result.PricingUpdated);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Module marketplace bootstrap job failed.");
        }
    }
}
