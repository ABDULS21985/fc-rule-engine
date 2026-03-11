using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed class SanctionsWatchlistRefreshService
{
    private readonly SanctionsWatchlistCatalogService _catalogService;
    private readonly ILogger<SanctionsWatchlistRefreshService> _logger;

    public SanctionsWatchlistRefreshService(
        SanctionsWatchlistCatalogService catalogService,
        ILogger<SanctionsWatchlistRefreshService> logger)
    {
        _catalogService = catalogService;
        _logger = logger;
    }

    public Task<SanctionsCatalogMaterializationResult> RefreshBaselineAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Refreshing sanctions watchlist catalog from the baseline source set.");
        return _catalogService.MaterializeAsync(SanctionsWatchlistBaselineCatalog.CreateRequest(), ct);
    }

    public async Task<bool> RefreshIfStaleAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        var state = await _catalogService.LoadAsync(ct);
        var materializedAt = state.MaterializedAt;
        var isMissing = state.Sources.Count == 0 || state.Entries.Count == 0 || materializedAt is null;
        var isStale = materializedAt is DateTime materializedAtValue
            && DateTime.UtcNow - materializedAtValue >= maxAge;

        if (!isMissing && !isStale)
        {
            _logger.LogDebug(
                "Sanctions watchlist catalog is current. MaterializedAt={MaterializedAt:o} MaxAgeHours={MaxAgeHours}",
                materializedAt,
                maxAge.TotalHours);
            return false;
        }

        var reason = isMissing ? "missing" : "stale";
        _logger.LogInformation(
            "Sanctions watchlist catalog refresh triggered because the catalog is {Reason}. MaterializedAt={MaterializedAt:o}",
            reason,
            materializedAt);

        await RefreshBaselineAsync(ct);
        return true;
    }
}
