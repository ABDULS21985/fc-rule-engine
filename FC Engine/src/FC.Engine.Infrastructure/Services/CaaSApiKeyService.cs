using Dapper;
using FC.Engine.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

/// <summary>
/// Manages CaaS API keys. Raw keys are shown once and never stored;
/// only the SHA-256 hash is persisted.
/// </summary>
public sealed class CaaSApiKeyService : ICaaSApiKeyService
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<CaaSApiKeyService> _log;

    public CaaSApiKeyService(IDbConnectionFactory db, ILogger<CaaSApiKeyService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<(string RawKey, CaaSApiKeyInfo Info)> CreateKeyAsync(
        int partnerId,
        string displayName,
        CaaSEnvironment environment,
        DateTimeOffset? expiresAt,
        int createdByUserId,
        CancellationToken ct = default)
    {
        // Format: regos_{env}_{32 random hex chars}
        var envPrefix = environment == CaaSEnvironment.Live ? "live" : "test";
        var randomBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var rawKey = $"regos_{envPrefix}_{Convert.ToHexString(randomBytes).ToLowerInvariant()}";

        var keyHash   = ComputeKeyHash(rawKey);
        var keyPrefix = rawKey[..12]; // "regos_live_a" — safe to display

        await using var conn = await _db.OpenAsync(ct);

        var keyId = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO CaaSApiKeys
                (PartnerId, KeyPrefix, KeyHash, DisplayName, Environment,
                 IsActive, ExpiresAt, CreatedByUserId)
            OUTPUT INSERTED.Id
            VALUES (@PartnerId, @Prefix, @Hash, @Name, @Env,
                    1, @ExpiresAt, @CreatedBy)
            """,
            new
            {
                PartnerId = partnerId,
                Prefix = keyPrefix,
                Hash = keyHash,
                Name = displayName,
                Env = environment.ToString().ToUpperInvariant(),
                ExpiresAt = expiresAt,
                CreatedBy = createdByUserId
            });

        _log.LogInformation(
            "API key created: PartnerId={PartnerId} KeyId={KeyId} Env={Env}",
            partnerId, keyId, environment);

        var info = new CaaSApiKeyInfo(
            keyId, keyPrefix, displayName,
            environment, true, expiresAt, null, DateTimeOffset.UtcNow);

        return (rawKey, info);
    }

    public async Task<ResolvedPartner?> ValidateKeyAsync(
        string rawKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawKey) || !rawKey.StartsWith("regos_"))
            return null;

        var keyHash = ComputeKeyHash(rawKey);

        await using var conn = await _db.OpenAsync(ct);

        var row = await conn.QuerySingleOrDefaultAsync<ApiKeyValidationRow>(
            """
            SELECT k.Id          AS KeyId,
                   k.PartnerId,
                   k.Environment,
                   k.IsActive,
                   k.ExpiresAt,
                   k.RevokedAt,
                   p.InstitutionId,
                   p.PartnerCode,
                   p.Tier,
                   p.IsActive    AS PartnerIsActive,
                   p.AllowedModuleCodes
            FROM   CaaSApiKeys k
            JOIN   CaaSPartners p ON p.Id = k.PartnerId
            WHERE  k.KeyHash = @Hash
            """,
            new { Hash = keyHash });

        if (row is null)                                            return null;
        if (!row.IsActive || !row.PartnerIsActive)                  return null;
        if (row.RevokedAt is not null)                              return null;
        if (row.ExpiresAt is not null && row.ExpiresAt < DateTimeOffset.UtcNow) return null;

        // Update last used — fire-and-forget so it never blocks the request path
        _ = conn.ExecuteAsync(
            "UPDATE CaaSApiKeys SET LastUsedAt = SYSUTCDATETIME() WHERE Id = @Id",
            new { Id = row.KeyId });

        var moduleCodes = string.IsNullOrEmpty(row.AllowedModuleCodes)
            ? Array.Empty<string>()
            : System.Text.Json.JsonSerializer.Deserialize<string[]>(row.AllowedModuleCodes)!;

        return new ResolvedPartner(
            PartnerId: row.PartnerId,
            PartnerCode: row.PartnerCode,
            InstitutionId: row.InstitutionId,
            Tier: Enum.Parse<PartnerTier>(row.Tier, ignoreCase: true),
            Environment: row.Environment,
            AllowedModuleCodes: moduleCodes);
    }

    public async Task RevokeKeyAsync(
        int partnerId, long keyId, int revokedByUserId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        var affected = await conn.ExecuteAsync(
            """
            UPDATE CaaSApiKeys
            SET    IsActive = 0, RevokedAt = SYSUTCDATETIME(), RevokedByUserId = @UserId
            WHERE  Id = @KeyId AND PartnerId = @PartnerId
            """,
            new { KeyId = keyId, PartnerId = partnerId, UserId = revokedByUserId });

        if (affected == 0)
            throw new KeyNotFoundException(
                $"API key {keyId} not found for partner {partnerId}.");

        _log.LogWarning(
            "API key revoked: KeyId={KeyId} PartnerId={PartnerId} RevokedBy={UserId}",
            keyId, partnerId, revokedByUserId);
    }

    public async Task<IReadOnlyList<CaaSApiKeyInfo>> ListKeysAsync(
        int partnerId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<CaaSApiKeyRow>(
            """
            SELECT Id, KeyPrefix, DisplayName, Environment,
                   IsActive, ExpiresAt, LastUsedAt, CreatedAt
            FROM   CaaSApiKeys
            WHERE  PartnerId = @PartnerId AND RevokedAt IS NULL
            ORDER BY CreatedAt DESC
            """,
            new { PartnerId = partnerId });

        return rows.Select(r => new CaaSApiKeyInfo(
            r.Id, r.KeyPrefix, r.DisplayName,
            Enum.Parse<CaaSEnvironment>(r.Environment, ignoreCase: true),
            r.IsActive, r.ExpiresAt, r.LastUsedAt, r.CreatedAt)).ToList();
    }

    private static string ComputeKeyHash(string rawKey)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(rawKey);
        var hash  = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ── Private row types ─────────────────────────────────────────────────────
    private sealed record ApiKeyValidationRow(
        long KeyId, int PartnerId, string Environment, bool IsActive,
        DateTimeOffset? ExpiresAt, DateTimeOffset? RevokedAt,
        int InstitutionId, string PartnerCode, string Tier,
        bool PartnerIsActive, string? AllowedModuleCodes);

    private sealed record CaaSApiKeyRow(
        long Id, string KeyPrefix, string DisplayName, string Environment,
        bool IsActive, DateTimeOffset? ExpiresAt, DateTimeOffset? LastUsedAt,
        DateTimeOffset CreatedAt);
}
