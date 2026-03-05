using System.Security.Cryptography;
using FC.Engine.Domain.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace FC.Engine.Infrastructure.Auth;

public class MfaChallengeStore : IMfaChallengeStore
{
    private static readonly TimeSpan ChallengeTtl = TimeSpan.FromMinutes(5);
    private readonly IMemoryCache _cache;

    public MfaChallengeStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    public Task<string> CreateChallenge(MfaLoginChallenge challenge, CancellationToken ct = default)
    {
        var challengeId = GenerateId();
        _cache.Set(CacheKey(challengeId), challenge, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ChallengeTtl
        });

        return Task.FromResult(challengeId);
    }

    public Task<MfaLoginChallenge?> GetChallenge(string challengeId, CancellationToken ct = default)
    {
        _cache.TryGetValue(CacheKey(challengeId), out MfaLoginChallenge? challenge);
        return Task.FromResult(challenge);
    }

    public Task RemoveChallenge(string challengeId, CancellationToken ct = default)
    {
        _cache.Remove(CacheKey(challengeId));
        return Task.CompletedTask;
    }

    private static string CacheKey(string challengeId) => $"mfa:challenge:{challengeId}";

    private static string GenerateId()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }
}
