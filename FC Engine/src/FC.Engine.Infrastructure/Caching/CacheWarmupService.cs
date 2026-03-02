using FC.Engine.Domain.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Caching;

public class CacheWarmupService : IHostedService
{
    private readonly ITemplateMetadataCache _cache;
    private readonly ILogger<CacheWarmupService> _logger;

    public CacheWarmupService(ITemplateMetadataCache cache, ILogger<CacheWarmupService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Warming template metadata cache...");
        try
        {
            var templates = await _cache.GetAllPublishedTemplates(cancellationToken);
            _logger.LogInformation("Cache warmed: {Count} templates loaded", templates.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache warmup failed — templates will be loaded on demand");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
