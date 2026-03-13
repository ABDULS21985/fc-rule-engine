using FC.Engine.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FC.Engine.Infrastructure.Services;

/// <summary>
/// Sliding-window rate limiter backed by Redis. Uses a Lua script for atomicity.
/// Window = 60 seconds. Counter key: caas:rl:{partnerId}:{window_bucket}
/// Fails open (allows request) if Redis is unavailable.
/// </summary>
public sealed class CaaSRedisRateLimiter : ICaaSRateLimiter
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<CaaSRedisRateLimiter> _log;

    // Lua script: KEYS[1]=counter key, ARGV[1]=limit, ARGV[2]=window_ms, ARGV[3]=now_ms
    // Returns: {current_count, limit, is_allowed (1/0)}
    private const string SlidingWindowScript = """
        local key = KEYS[1]
        local limit = tonumber(ARGV[1])
        local window = tonumber(ARGV[2])
        local now = tonumber(ARGV[3])
        local window_start = now - window

        -- Remove expired entries
        redis.call('ZREMRANGEBYSCORE', key, '-inf', window_start)

        -- Count current entries
        local count = redis.call('ZCARD', key)

        if count < limit then
            -- Add this request with unique member
            redis.call('ZADD', key, now, now .. ':' .. math.random(1000000))
            redis.call('EXPIRE', key, math.ceil(window / 1000) + 1)
            return {count + 1, limit, 1}
        else
            return {count, limit, 0}
        end
        """;

    public CaaSRedisRateLimiter(ILogger<CaaSRedisRateLimiter> log, IConnectionMultiplexer? redis = null)
    {
        _redis = redis;
        _log   = log;
    }

    public async Task<RateLimitResult> CheckAndIncrementAsync(
        int partnerId, PartnerTier tier, CancellationToken ct = default)
    {
        var limit = RateLimitThresholds.GetRequestsPerMinute(tier);
        const int windowSeconds = 60;
        const long windowMs = windowSeconds * 1000L;

        if (_redis is null)
            return new RateLimitResult(true, limit, limit, 0);

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // Bucket key: one per minute window
        var key = $"caas:rl:{partnerId}:{nowMs / windowMs}";

        try
        {
            var db = _redis.GetDatabase();
            var result = (RedisValue[]?)await db.ScriptEvaluateAsync(
                SlidingWindowScript,
                new RedisKey[] { key },
                new RedisValue[] { limit, windowMs, nowMs });

            if (result is null || result.Length < 3)
            {
                _log.LogWarning(
                    "Redis rate limiter script returned an unexpected response for partner {PartnerId}",
                    partnerId);
                return new RateLimitResult(true, limit, limit, 0);
            }

            var current = (int)result[0];
            var allowed = (int)result[2] == 1;

            if (!allowed)
            {
                _log.LogWarning(
                    "Rate limit exceeded: PartnerId={PartnerId} Tier={Tier} " +
                    "Count={Count} Limit={Limit}",
                    partnerId, tier, current, limit);
            }

            return new RateLimitResult(
                Allowed: allowed,
                Limit: limit,
                Remaining: Math.Max(0, limit - current),
                RetryAfterSeconds: allowed ? 0 : windowSeconds);
        }
        catch (Exception ex)
        {
            // Redis unavailable — fail open (allow request, log error)
            _log.LogError(ex,
                "Redis rate limiter unavailable — failing open for partner {PartnerId}", partnerId);
            return new RateLimitResult(true, limit, limit, 0);
        }
    }
}
