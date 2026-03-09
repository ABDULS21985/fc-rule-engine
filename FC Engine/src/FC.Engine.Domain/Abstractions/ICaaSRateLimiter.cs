namespace FC.Engine.Domain.Abstractions;

public interface ICaaSRateLimiter
{
    /// <summary>
    /// Checks and increments the sliding-window counter for this partner.
    /// Returns true if request is allowed; false if limit exceeded.
    /// Fails open (allows request) if Redis is unavailable.
    /// </summary>
    Task<RateLimitResult> CheckAndIncrementAsync(
        int partnerId,
        PartnerTier tier,
        CancellationToken ct = default);
}

public sealed record RateLimitResult(
    bool Allowed,
    int Limit,
    int Remaining,
    int RetryAfterSeconds);
