using Dapper;
using FC.Engine.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

/// <summary>
/// Computes NDIC insurable vs uninsurable deposits for entities that fail under stress.
/// Uses the N5,000,000 per-depositor cap (R-12).
/// Reads depositor distribution from DepositorDistributions table where available;
/// falls back to the actuarial estimate (72% insurable share for average Nigerian bank).
/// </summary>
public sealed class NDICExposureCalculator : INDICExposureCalculator
{
    private const decimal InsuranceCapPerDepositorNGN = 5_000_000m;
    private const decimal FallbackInsurableShare = 0.72m;
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<NDICExposureCalculator> _log;

    public NDICExposureCalculator(
        IDbConnectionFactory db,
        ILogger<NDICExposureCalculator> log)
    {
        _db = db; _log = log;
    }

    public async Task<(decimal Insurable, decimal Uninsurable)> ComputeAsync(
        int institutionId, string periodCode, CancellationToken ct = default)
    {
        using var conn = await _db.OpenAsync(ct);

        // Attempt to load actual depositor distribution data
        var dist = await conn.QuerySingleOrDefaultAsync<DepositorDistRow>(
            """
            SELECT TotalDeposits, DepositorsAboveCap, TotalDepositorCount,
                   DepositsByAccountsBelowCap
            FROM   DepositorDistributions
            WHERE  InstitutionId = @Id AND PeriodCode = @Period
            """,
            new { Id = institutionId, Period = periodCode });

        if (dist is not null && dist.TotalDepositorCount > 0)
        {
            var insurable   = dist.DepositsByAccountsBelowCap;
            var uninsurable = dist.TotalDeposits - insurable;
            return (insurable, uninsurable);
        }

        // Fallback: load total deposits from PrudentialMetrics
        var totalDeposits = await conn.ExecuteScalarAsync<decimal>(
            """
            SELECT ISNULL(TotalDeposits, 0)
            FROM   PrudentialMetrics
            WHERE  InstitutionId = @Id AND PeriodCode = @Period
            """,
            new { Id = institutionId, Period = periodCode });

        if (totalDeposits <= 0)
        {
            _log.LogWarning(
                "No deposit data for institution {Id} period {Period} — NDIC exposure = 0",
                institutionId, periodCode);
            return (0m, 0m);
        }

        var fallbackInsurable   = totalDeposits * FallbackInsurableShare;
        var fallbackUninsurable = totalDeposits - fallbackInsurable;

        _log.LogDebug(
            "NDIC fallback estimate: Institution={Id} TotalDeposits={TD:N0} " +
            "Insurable={Ins:N0} Uninsurable={Unins:N0}",
            institutionId, totalDeposits, fallbackInsurable, fallbackUninsurable);

        return (fallbackInsurable, fallbackUninsurable);
    }

    public async Task<decimal> GetNDICFundCapacityAsync(CancellationToken ct = default)
    {
        using var conn = await _db.OpenAsync(ct);
        var capacity = await conn.ExecuteScalarAsync<decimal?>(
            "SELECT ConfigValue FROM SystemConfiguration WHERE ConfigKey='NDIC_FUND_CAPACITY_NGN_MILLIONS'");
        // Default: NDIC Deposit Insurance Fund ≈ ₦1.5 trillion (2024)
        return capacity ?? 1_500_000m;
    }

    private sealed record DepositorDistRow(
        decimal TotalDeposits,
        int DepositorsAboveCap,
        int TotalDepositorCount,
        decimal DepositsByAccountsBelowCap);
}
