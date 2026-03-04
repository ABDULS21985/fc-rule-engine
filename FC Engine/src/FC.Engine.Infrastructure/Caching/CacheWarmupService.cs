using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Caching;

public class CacheWarmupService : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    private readonly ITemplateMetadataCache _cache;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CacheWarmupService> _logger;

    public CacheWarmupService(
        ITemplateMetadataCache cache,
        IServiceProvider serviceProvider,
        ILogger<CacheWarmupService> logger)
    {
        _cache = cache;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await WarmCache(stoppingToken);

            try
            {
                await Task.Delay(RefreshInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Host is shutting down.
            }
        }
    }

    private async Task WarmCache(CancellationToken ct)
    {
        _logger.LogInformation("Warming template metadata cache for active tenants...");

        try
        {
            _cache.InvalidateAll();

            // Warm global + platform view.
            var globalTemplates = await _cache.GetAllPublishedTemplates(ct);

            List<Guid> activeTenantIds;
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
                activeTenantIds = await db.Tenants
                    .Where(t => t.Status == TenantStatus.Active)
                    .Select(t => t.TenantId)
                    .ToListAsync(ct);
            }

            foreach (var tenantId in activeTenantIds)
            {
                await _cache.GetAllPublishedTemplates(tenantId, ct);
            }

            _logger.LogInformation(
                "Template metadata cache warmed: {GlobalCount} global/platform templates, {TenantCount} active tenants",
                globalTemplates.Count,
                activeTenantIds.Count);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Template metadata cache warmup failed; cache will continue loading on demand");
        }
    }
}
