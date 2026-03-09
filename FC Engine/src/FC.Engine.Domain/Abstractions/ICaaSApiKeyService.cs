namespace FC.Engine.Domain.Abstractions;

public interface ICaaSApiKeyService
{
    /// <summary>
    /// Creates a new API key. Returns the raw key ONCE — never stored.
    /// </summary>
    Task<(string RawKey, CaaSApiKeyInfo Info)> CreateKeyAsync(
        int partnerId,
        string displayName,
        CaaSEnvironment environment,
        DateTimeOffset? expiresAt,
        int createdByUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Validates an incoming API key. Returns the resolved partner or null
    /// if the key is invalid, revoked, or expired. Updates LastUsedAt on success.
    /// </summary>
    Task<ResolvedPartner?> ValidateKeyAsync(
        string rawKey,
        CancellationToken ct = default);

    Task RevokeKeyAsync(
        int partnerId,
        long keyId,
        int revokedByUserId,
        CancellationToken ct = default);

    Task<IReadOnlyList<CaaSApiKeyInfo>> ListKeysAsync(
        int partnerId,
        CancellationToken ct = default);
}

public sealed record CaaSApiKeyInfo(
    long KeyId,
    string KeyPrefix,
    string DisplayName,
    CaaSEnvironment Environment,
    bool IsActive,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset CreatedAt);
