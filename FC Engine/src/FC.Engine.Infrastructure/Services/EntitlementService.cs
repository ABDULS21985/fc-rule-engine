using System.Data;
using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public class EntitlementService : IEntitlementService
{
    private const string SubscriptionTablesCacheKey = "entitlement:subscription-tables-available";
    private static readonly TimeSpan SubscriptionMetadataTtl = TimeSpan.FromMinutes(10);
    private static readonly IReadOnlyList<string> DefaultFeatures = new[] { "xml_submission", "validation", "reporting" };

    private readonly MetadataDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<EntitlementService> _logger;
    private static readonly TimeSpan CacheTTL = TimeSpan.FromMinutes(5);

    public EntitlementService(
        MetadataDbContext db,
        IMemoryCache cache,
        ILogger<EntitlementService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<TenantEntitlement> ResolveEntitlements(Guid tenantId, CancellationToken ct = default)
    {
        var cacheKey = $"entitlement:{tenantId}";

        if (_cache.TryGetValue(cacheKey, out TenantEntitlement? cached) && cached is not null)
            return cached;

        // 1. Load tenant
        var tenant = await _db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found");

        // 2. Load active licence types for this tenant
        var tenantLicences = await _db.TenantLicenceTypes
            .Where(tlt => tlt.TenantId == tenantId && tlt.IsActive)
            .Include(tlt => tlt.LicenceType)
            .ToListAsync(ct);

        var licenceTypeIds = tenantLicences.Select(tlt => tlt.LicenceTypeId).ToList();
        var licenceCodes = tenantLicences
            .Where(tlt => tlt.LicenceType != null)
            .Select(tlt => tlt.LicenceType!.Code)
            .ToList();

        // 3. Load eligible modules via licence-module matrix
        var matrixEntries = await _db.LicenceModuleMatrix
            .Where(lmm => licenceTypeIds.Contains(lmm.LicenceTypeId))
            .Include(lmm => lmm.Module)
            .Where(lmm => lmm.Module!.IsActive)
            .ToListAsync(ct);

        // Deduplicate by ModuleId (a module may appear in multiple licence types)
        var eligibleModules = matrixEntries
            .GroupBy(lmm => lmm.ModuleId)
            .Select(g =>
            {
                var first = g.First();
                return new EntitledModule
                {
                    ModuleId = first.ModuleId,
                    ModuleCode = first.Module!.ModuleCode,
                    ModuleName = first.Module.ModuleName,
                    RegulatorCode = first.Module.RegulatorCode,
                    IsRequired = g.Any(lmm => lmm.IsRequired),
                    IsActive = false,
                    SheetCount = first.Module.SheetCount,
                    DefaultFrequency = first.Module.DefaultFrequency
                };
            })
            .OrderBy(m => m.ModuleCode)
            .ToList();

        // 4. Resolve active modules based on subscription tables (if available).
        var subscription = await ResolveSubscriptionState(tenantId, eligibleModules, ct);

        // 5. Build entitlement result
        var entitlement = new TenantEntitlement
        {
            TenantId = tenantId,
            TenantStatus = tenant.Status,
            LicenceTypeCodes = licenceCodes.AsReadOnly(),
            EligibleModules = eligibleModules.AsReadOnly(),
            ActiveModules = subscription.ActiveModules.AsReadOnly(),
            Features = subscription.Features,
            PlanCode = subscription.PlanCode,
            ResolvedAt = DateTime.UtcNow
        };

        _cache.Set(cacheKey, entitlement, new MemoryCacheEntryOptions
        {
            SlidingExpiration = CacheTTL
        });

        _logger.LogDebug("Resolved entitlements for tenant {TenantId}: {ModuleCount} active modules",
            tenantId, subscription.ActiveModules.Count);

        return entitlement;
    }

    public async Task<bool> HasModuleAccess(Guid tenantId, string moduleCode, CancellationToken ct = default)
    {
        var entitlement = await ResolveEntitlements(tenantId, ct);
        return entitlement.ActiveModules.Any(m =>
            string.Equals(m.ModuleCode, moduleCode, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> HasFeatureAccess(Guid tenantId, string featureCode, CancellationToken ct = default)
    {
        var entitlement = await ResolveEntitlements(tenantId, ct);
        return entitlement.Features.Contains(featureCode, StringComparer.OrdinalIgnoreCase);
    }

    public Task InvalidateCache(Guid tenantId)
    {
        _cache.Remove($"entitlement:{tenantId}");
        _logger.LogInformation("Invalidated entitlement cache for tenant {TenantId}", tenantId);
        return Task.CompletedTask;
    }

    private async Task<SubscriptionResolution> ResolveSubscriptionState(
        Guid tenantId,
        IReadOnlyList<EntitledModule> eligibleModules,
        CancellationToken ct)
    {
        var fallback = SubscriptionResolution.Fallback(eligibleModules, DefaultFeatures);

        // In-memory provider has no SQL surface for subscription-table discovery.
        if (!_db.Database.IsRelational())
        {
            return fallback;
        }

        if (!await HasSubscriptionTablesAsync(ct))
        {
            return fallback;
        }

        var dbConnection = _db.Database.GetDbConnection();
        var shouldClose = dbConnection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await dbConnection.OpenAsync(ct);
        }

        try
        {
            var activeSubscription = await GetActiveSubscriptionAsync(dbConnection, tenantId, ct);
            if (activeSubscription is null)
            {
                return fallback;
            }

            var planCode = string.IsNullOrWhiteSpace(activeSubscription.PlanCode)
                ? "DEFAULT"
                : activeSubscription.PlanCode!;

            var features = ParseFeatures(activeSubscription.Features);
            var activeModules = activeSubscription.AllModulesIncluded
                ? eligibleModules.Select(m => ToActive(m)).ToList()
                : await ResolveSubscribedModulesAsync(dbConnection, activeSubscription.SubscriptionId, eligibleModules, ct);

            return new SubscriptionResolution(planCode, features, activeModules);
        }
        catch (Exception ex) when (IsSubscriptionSchemaException(ex))
        {
            // RG-03 tables may be absent or partially deployed. Fall back to eligible=active.
            _cache.Set(SubscriptionTablesCacheKey, false, SubscriptionMetadataTtl);
            _logger.LogWarning(ex,
                "Subscription schema not fully available; using eligible modules as active for tenant {TenantId}",
                tenantId);
            return fallback;
        }
        finally
        {
            if (shouldClose)
            {
                await dbConnection.CloseAsync();
            }
        }
    }

    private async Task<List<EntitledModule>> ResolveSubscribedModulesAsync(
        System.Data.Common.DbConnection connection,
        int subscriptionId,
        IReadOnlyList<EntitledModule> eligibleModules,
        CancellationToken ct)
    {
        var eligibleById = eligibleModules.ToDictionary(m => m.ModuleId);
        var subscribedModuleIds = new HashSet<int>();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT sm.ModuleId
            FROM dbo.subscription_modules sm
            WHERE sm.SubscriptionId = @subscriptionId;";
        var subscriptionIdParam = cmd.CreateParameter();
        subscriptionIdParam.ParameterName = "@subscriptionId";
        subscriptionIdParam.Value = subscriptionId;
        cmd.Parameters.Add(subscriptionIdParam);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            subscribedModuleIds.Add(reader.GetInt32(0));
        }

        if (subscribedModuleIds.Count == 0)
        {
            return eligibleModules.Select(ToActive).ToList();
        }

        return subscribedModuleIds
            .Where(eligibleById.ContainsKey)
            .Select(id => ToActive(eligibleById[id]))
            .ToList();
    }

    private async Task<ActiveSubscription?> GetActiveSubscriptionAsync(
        System.Data.Common.DbConnection connection,
        Guid tenantId,
        CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT TOP (1)
                s.Id,
                sp.PlanCode,
                sp.Features,
                CAST(sp.AllModulesIncluded AS bit) AS AllModulesIncluded
            FROM dbo.subscriptions s
            LEFT JOIN dbo.subscription_plans sp ON sp.Id = s.SubscriptionPlanId
            WHERE s.TenantId = @tenantId
              AND s.Status = 'Active'
            ORDER BY s.Id DESC;";

        var tenantIdParam = cmd.CreateParameter();
        tenantIdParam.ParameterName = "@tenantId";
        tenantIdParam.Value = tenantId;
        cmd.Parameters.Add(tenantIdParam);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new ActiveSubscription(
            reader.GetInt32(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            !reader.IsDBNull(3) && reader.GetBoolean(3));
    }

    private async Task<bool> HasSubscriptionTablesAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(SubscriptionTablesCacheKey, out bool available))
        {
            return available;
        }

        var exists = false;
        try
        {
            var dbConnection = _db.Database.GetDbConnection();
            var shouldClose = dbConnection.State != ConnectionState.Open;
            if (shouldClose)
            {
                await dbConnection.OpenAsync(ct);
            }

            try
            {
                await using var cmd = dbConnection.CreateCommand();
                cmd.CommandText = @"
                    SELECT CASE
                        WHEN OBJECT_ID(N'dbo.subscriptions', N'U') IS NOT NULL
                         AND OBJECT_ID(N'dbo.subscription_modules', N'U') IS NOT NULL
                        THEN 1 ELSE 0 END;";
                var scalar = await cmd.ExecuteScalarAsync(ct);
                exists = scalar is not null && Convert.ToInt32(scalar) == 1;
            }
            finally
            {
                if (shouldClose)
                {
                    await dbConnection.CloseAsync();
                }
            }
        }
        catch
        {
            exists = false;
        }

        _cache.Set(SubscriptionTablesCacheKey, exists, SubscriptionMetadataTtl);
        return exists;
    }

    private static IReadOnlyList<string> ParseFeatures(string? rawFeatures)
    {
        if (string.IsNullOrWhiteSpace(rawFeatures))
        {
            return DefaultFeatures;
        }

        var trimmed = rawFeatures.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                var json = JsonSerializer.Deserialize<List<string>>(trimmed);
                if (json is { Count: > 0 })
                {
                    return json
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Select(v => v.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }
            catch
            {
                // Fall through to CSV parsing
            }
        }

        return trimmed
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static EntitledModule ToActive(EntitledModule module)
    {
        return new EntitledModule
        {
            ModuleId = module.ModuleId,
            ModuleCode = module.ModuleCode,
            ModuleName = module.ModuleName,
            RegulatorCode = module.RegulatorCode,
            IsRequired = module.IsRequired,
            IsActive = true,
            SheetCount = module.SheetCount,
            DefaultFrequency = module.DefaultFrequency
        };
    }

    private static bool IsSubscriptionSchemaException(Exception ex)
    {
        return ex.Message.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("Invalid column name", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("Could not find stored procedure", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ActiveSubscription(
        int SubscriptionId,
        string? PlanCode,
        string? Features,
        bool AllModulesIncluded);

    private sealed record SubscriptionResolution(
        string PlanCode,
        IReadOnlyList<string> Features,
        List<EntitledModule> ActiveModules)
    {
        public static SubscriptionResolution Fallback(
            IReadOnlyList<EntitledModule> eligibleModules,
            IReadOnlyList<string> features)
        {
            return new SubscriptionResolution(
                "DEFAULT",
                features,
                eligibleModules.Select(ToActive).ToList());
        }
    }
}
