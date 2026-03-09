using FC.Engine.Domain.Abstractions;

namespace FC.Engine.Infrastructure.Services.DataProtection;

public interface IContinuousDspmWatcher
{
    string Name { get; }
    Task ExecuteAsync(CancellationToken ct = default);
}

public sealed class AtRestDspmWatcher : IContinuousDspmWatcher
{
    private readonly IDataProtectionService _dataProtectionService;

    public AtRestDspmWatcher(IDataProtectionService dataProtectionService)
        => _dataProtectionService = dataProtectionService;

    public string Name => "at_rest";

    public Task ExecuteAsync(CancellationToken ct = default)
        => _dataProtectionService.RunAtRestScanAsync(null, ct);
}

public sealed class ShadowDspmWatcher : IContinuousDspmWatcher
{
    private readonly IDataProtectionService _dataProtectionService;

    public ShadowDspmWatcher(IDataProtectionService dataProtectionService)
        => _dataProtectionService = dataProtectionService;

    public string Name => "shadow_copy";

    public Task ExecuteAsync(CancellationToken ct = default)
        => _dataProtectionService.RunShadowCopyDetectionAsync(null, ct);
}
