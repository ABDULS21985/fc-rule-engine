using System.Security.Cryptography;
using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Services;

public class ApiKeyService : IApiKeyService
{
    private const string ApiKeyPrefix = "regos_live_";
    private readonly MetadataDbContext _db;

    public ApiKeyService(MetadataDbContext db)
    {
        _db = db;
    }

    public async Task<ApiKeyCreateResult> CreateApiKey(
        Guid tenantId,
        int createdByUserId,
        CreateApiKeyRequest request,
        CancellationToken ct = default)
    {
        var rawKey = $"{ApiKeyPrefix}{GenerateSecureRandomString(32)}";
        var keyHash = BCrypt.Net.BCrypt.HashPassword(rawKey);
        var prefix = rawKey[..Math.Min(20, rawKey.Length)];

        var apiKey = new ApiKey
        {
            TenantId = tenantId,
            KeyHash = keyHash,
            KeyPrefix = prefix,
            Description = request.Description,
            Permissions = JsonSerializer.Serialize(request.Permissions ?? new List<string>()),
            RateLimitPerMinute = request.RateLimitPerMinute ?? 100,
            ExpiresAt = request.ExpiresAt,
            CreatedBy = createdByUserId,
            IsActive = true
        };

        _db.ApiKeys.Add(apiKey);
        await _db.SaveChangesAsync(ct);

        return new ApiKeyCreateResult
        {
            ApiKeyId = apiKey.Id,
            RawKey = rawKey,
            Prefix = prefix,
            Message = "Save this key securely. You will not be able to see it again."
        };
    }

    public async Task<ApiKeyValidationResult?> ValidateApiKey(string rawKey, string? ipAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawKey) || !rawKey.StartsWith(ApiKeyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var prefix = rawKey[..Math.Min(20, rawKey.Length)];
        var candidates = await _db.ApiKeys
            .Where(k => k.IsActive && k.KeyPrefix == prefix)
            .Where(k => !k.ExpiresAt.HasValue || k.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(ct);

        foreach (var key in candidates)
        {
            if (!BCrypt.Net.BCrypt.Verify(rawKey, key.KeyHash))
            {
                continue;
            }

            key.LastUsedAt = DateTime.UtcNow;
            key.LastUsedIp = ipAddress;
            await _db.SaveChangesAsync(ct);

            var permissions = ParsePermissions(key.Permissions);
            return new ApiKeyValidationResult
            {
                ApiKeyId = key.Id,
                TenantId = key.TenantId,
                Permissions = permissions,
                RateLimitPerMinute = key.RateLimitPerMinute
            };
        }

        return null;
    }

    private static IReadOnlyList<string> ParsePermissions(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(raw);
            var values = parsed?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return values is { Count: > 0 } ? values : Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string GenerateSecureRandomString(int byteCount)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteCount);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }
}
