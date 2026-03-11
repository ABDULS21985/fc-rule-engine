using FC.Engine.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.BackgroundJobs;

public sealed class SubscriptionModuleEntitlementBootstrapJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SubscriptionModuleEntitlementBootstrapJob> _logger;

    public SubscriptionModuleEntitlementBootstrapJob(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<SubscriptionModuleEntitlementBootstrapJob> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("SubscriptionModuleEntitlementBootstrap:Enabled", true))
        {
            _logger.LogInformation("Subscription module entitlement bootstrap job is disabled by configuration.");
            return;
        }

        var intervalHours = Math.Max(1, _configuration.GetValue("SubscriptionModuleEntitlementBootstrap:IntervalHours", 12));
        var runOnStartup = _configuration.GetValue("SubscriptionModuleEntitlementBootstrap:RunOnStartup", true);
        var interval = TimeSpan.FromHours(intervalHours);

        if (runOnStartup)
        {
            await RunReconciliationCycleAsync(stoppingToken);
        }

        using var timer = new PeriodicTimer(interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await RunReconciliationCycleAsync(stoppingToken);
        }
    }

    private async Task RunReconciliationCycleAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var registryBootstrap = scope.ServiceProvider.GetRequiredService<ModuleRegistryBootstrapService>();
            var marketplaceBootstrap = scope.ServiceProvider.GetRequiredService<ModuleMarketplaceBootstrapService>();
            var entitlementBootstrap = scope.ServiceProvider.GetRequiredService<SubscriptionModuleEntitlementBootstrapService>();

            await registryBootstrap.EnsureBaselineModulesAsync(ct);
            await marketplaceBootstrap.EnsurePricingAsync(ct);

            var result = await entitlementBootstrap.EnsureIncludedModulesAsync(ct);
            _logger.LogInformation(
                "Subscription module entitlement reconciliation completed. ModulesCreated={Created} ModulesReactivated={Reactivated} ModulesUpdated={Updated} TenantsTouched={TenantsTouched}",
                result.ModulesCreated,
                result.ModulesReactivated,
                result.ModulesUpdated,
                result.TenantsTouched);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subscription module entitlement reconciliation cycle failed.");
        }
    }
}
