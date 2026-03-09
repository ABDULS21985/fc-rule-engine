namespace FC.Engine.Domain.Abstractions;

public interface INDICExposureCalculator
{
    Task<(decimal Insurable, decimal Uninsurable)> ComputeAsync(
        int    institutionId,
        string periodCode,
        CancellationToken ct = default);

    Task<decimal> GetNDICFundCapacityAsync(CancellationToken ct = default);
}
