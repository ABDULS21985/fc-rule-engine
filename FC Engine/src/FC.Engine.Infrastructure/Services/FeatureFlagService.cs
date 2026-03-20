using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FC.Engine.Infrastructure.Services;

public class FeatureFlagService : IFeatureFlagService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    private readonly MetadataDbContext _db;
    private readonly IMemoryCache _cache;

    public FeatureFlagService(MetadataDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<bool> IsEnabled(string flagCode, Guid? tenantId = null)
    {
        if (string.IsNullOrWhiteSpace(flagCode))
        {
            return false;
        }

        var flag = await GetFlag(flagCode);
        if (flag is null)
        {
            return false;
        }

        if (tenantId.HasValue)
        {
            if (ContainsTenant(flag.AllowedTenants, tenantId.Value))
            {
                return true;
            }

            if (await IsAllowedByPlan(flag.AllowedPlans, tenantId.Value))
            {
                return true;
            }

            if (flag.RolloutPercent > 0 && IsInRollout(tenantId.Value, flag.RolloutPercent))
            {
                return true;
            }
        }

        return flag.IsEnabled;
    }

    public async Task<IReadOnlyList<FeatureFlag>> GetAll(CancellationToken ct = default)
    {
        return await _db.FeatureFlags
            .AsNoTracking()
            .OrderBy(f => f.FlagCode)
            .ToListAsync(ct);
    }

    public async Task<FeatureFlag> Upsert(
        string flagCode,
        string description,
        bool isEnabled,
        int rolloutPercent,
        string? allowedTenants,
        string? allowedPlans,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(flagCode))
        {
            throw new ArgumentException("Flag code is required.", nameof(flagCode));
        }

        var normalizedCode = flagCode.Trim().ToLowerInvariant();
        var normalizedRollout = Math.Clamp(rolloutPercent, 0, 100);

        var existing = await _db.FeatureFlags
            .FirstOrDefaultAsync(f => f.FlagCode == normalizedCode, ct);

        if (existing is null)
        {
            existing = new FeatureFlag
            {
                FlagCode = normalizedCode,
                Description = description.Trim(),
                IsEnabled = isEnabled,
                RolloutPercent = normalizedRollout,
                AllowedTenants = NormalizeJsonArray(allowedTenants),
                AllowedPlans = NormalizeJsonArray(allowedPlans),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.FeatureFlags.Add(existing);
        }
        else
        {
            existing.Description = description.Trim();
            existing.IsEnabled = isEnabled;
            existing.RolloutPercent = normalizedRollout;
            existing.AllowedTenants = NormalizeJsonArray(allowedTenants);
            existing.AllowedPlans = NormalizeJsonArray(allowedPlans);
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        InvalidateCache(normalizedCode);
        return existing;
    }

    public void InvalidateCache(string flagCode)
    {
        if (!string.IsNullOrWhiteSpace(flagCode))
        {
            _cache.Remove(GetCacheKey(flagCode.Trim().ToLowerInvariant()));
        }
    }

    private async Task<FeatureFlag?> GetFlag(string flagCode)
    {
        var normalizedCode = flagCode.Trim().ToLowerInvariant();
        var key = GetCacheKey(normalizedCode);
        if (_cache.TryGetValue(key, out FeatureFlag? cached))
        {
            return cached;
        }

        var flag = await _db.FeatureFlags
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.FlagCode == normalizedCode);

        _cache.Set(key, flag, CacheTtl);
        return flag;
    }

    private async Task<bool> IsAllowedByPlan(string? allowedPlansJson, Guid tenantId)
    {
        var allowedPlans = ParseArray(allowedPlansJson);
        if (allowedPlans.Count == 0)
        {
            return false;
        }

        var planCode = await _db.Subscriptions
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Where(s => SubscriptionStatusRules.EntitlementEligibleStatuses.Contains(s.Status))
            .OrderByDescending(s => s.UpdatedAt)
            .Select(s => s.Plan != null ? s.Plan.PlanCode : null)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(planCode))
        {
            return false;
        }

        return allowedPlans.Contains(planCode.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private static bool ContainsTenant(string? allowedTenantsJson, Guid tenantId)
    {
        var allowed = ParseArray(allowedTenantsJson);
        if (allowed.Count == 0)
        {
            return false;
        }

        return allowed.Any(x => Guid.TryParse(x, out var parsed) && parsed == tenantId);
    }

    private static bool IsInRollout(Guid tenantId, int rolloutPercent)
    {
        var percent = Math.Clamp(rolloutPercent, 0, 100);
        if (percent <= 0)
        {
            return false;
        }

        if (percent >= 100)
        {
            return true;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(tenantId.ToString("N")));
        var value = BitConverter.ToUInt32(hash, 0) % 100;
        return value < percent;
    }

    private static string GetCacheKey(string flagCode) => $"feature_flag:{flagCode}";

    private static List<string> ParseArray(string? jsonOrCsv)
    {
        if (string.IsNullOrWhiteSpace(jsonOrCsv))
        {
            return new List<string>();
        }

        var trimmed = jsonOrCsv.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(trimmed);
                return parsed?
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                    ?? new List<string>();
            }
            catch
            {
                // Fall back to CSV parsing.
            }
        }

        return trimmed
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NormalizeJsonArray(string? jsonOrCsv)
    {
        var values = ParseArray(jsonOrCsv);
        if (values.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(values);
    }
}
