using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public class BenchmarkingService : IBenchmarkingService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly MetadataDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IEntitlementService _entitlementService;
    private readonly ILogger<BenchmarkingService> _logger;

    public BenchmarkingService(
        MetadataDbContext db,
        IMemoryCache cache,
        IEntitlementService entitlementService,
        ILogger<BenchmarkingService> logger)
    {
        _db = db;
        _cache = cache;
        _entitlementService = entitlementService;
        _logger = logger;
    }

    public async Task<BenchmarkResult?> GetPeerBenchmark(Guid tenantId, string moduleCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(moduleCode))
        {
            throw new ArgumentException("Module code is required.", nameof(moduleCode));
        }

        var normalizedCode = moduleCode.Trim().ToUpperInvariant();
        var cacheKey = $"dashboard:benchmark:{tenantId}:{normalizedCode}";

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;

            try
            {
                var hasFeature = await _entitlementService.HasFeatureAccess(tenantId, "peer_benchmarking", ct);
                if (!hasFeature)
                {
                    return null;
                }

                var module = await _db.Modules
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.ModuleCode == normalizedCode, ct);
                if (module is null)
                {
                    return null;
                }

                var tenantLicenceTypeId = await _db.TenantLicenceTypes
                    .AsNoTracking()
                    .Where(tlt => tlt.TenantId == tenantId && tlt.IsActive)
                    .OrderByDescending(tlt => tlt.EffectiveDate)
                    .Select(tlt => (int?)tlt.LicenceTypeId)
                    .FirstOrDefaultAsync(ct);

                if (!tenantLicenceTypeId.HasValue)
                {
                    return null;
                }

                var peerTenantIds = await _db.TenantLicenceTypes
                    .AsNoTracking()
                    .Where(tlt => tlt.LicenceTypeId == tenantLicenceTypeId.Value && tlt.IsActive)
                    .Select(tlt => tlt.TenantId)
                    .Distinct()
                    .ToListAsync(ct);

                if (peerTenantIds.Count == 0)
                {
                    return null;
                }

                var peerRows = await _db.FilingSlaRecords
                    .AsNoTracking()
                    .Where(r => r.ModuleId == module.Id
                             && r.DaysToDeadline != null
                             && peerTenantIds.Contains(r.TenantId))
                    .Select(r => new
                    {
                        r.TenantId,
                        Days = r.DaysToDeadline!.Value
                    })
                    .ToListAsync(ct);

                if (peerRows.Count == 0)
                {
                    return new BenchmarkResult
                    {
                        TenantAverageDays = 0,
                        PeerMedianDays = 0,
                        PeerP25Days = 0,
                        PeerP75Days = 0,
                        Percentile = 0,
                        PeerCount = 0
                    };
                }

                var peerValues = peerRows.Select(x => x.Days).ToList();
                var peerTenantsWithData = peerRows.Select(x => x.TenantId).Distinct().Count();

                var tenantValues = await _db.FilingSlaRecords
                    .AsNoTracking()
                    .Where(r => r.TenantId == tenantId
                             && r.ModuleId == module.Id
                             && r.DaysToDeadline != null)
                    .Select(r => r.DaysToDeadline!.Value)
                    .ToListAsync(ct);

                var tenantAverage = tenantValues.Count == 0 ? 0 : tenantValues.Average();

                var orderedPeers = peerValues.OrderBy(v => v).ToList();
                var percentile = CalculatePercentileRank(orderedPeers, tenantAverage);

                return new BenchmarkResult
                {
                    TenantAverageDays = decimal.Round((decimal)tenantAverage, 2),
                    PeerMedianDays = Percentile(orderedPeers, 50),
                    PeerP25Days = Percentile(orderedPeers, 25),
                    PeerP75Days = Percentile(orderedPeers, 75),
                    Percentile = percentile,
                    PeerCount = peerTenantsWithData
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Peer benchmark failed for tenant {TenantId}, module {ModuleCode}", tenantId, normalizedCode);
                throw;
            }
        });
    }

    private static decimal Percentile(IReadOnlyList<int> sortedValues, int percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        var rank = (percentile / 100d) * (sortedValues.Count - 1);
        var lowerIndex = (int)Math.Floor(rank);
        var upperIndex = (int)Math.Ceiling(rank);

        if (lowerIndex == upperIndex)
        {
            return sortedValues[lowerIndex];
        }

        var weight = (decimal)(rank - lowerIndex);
        return decimal.Round(sortedValues[lowerIndex] * (1 - weight) + sortedValues[upperIndex] * weight, 2);
    }

    private static int CalculatePercentileRank(IReadOnlyList<int> sortedValues, double tenantAverage)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        // Higher DaysToDeadline means earlier submissions and better percentile.
        var lessOrEqual = sortedValues.Count(v => v <= tenantAverage);
        return (int)Math.Round((double)lessOrEqual * 100d / sortedValues.Count, MidpointRounding.AwayFromZero);
    }
}
