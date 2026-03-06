using FC.Engine.Domain.ValueObjects;

namespace FC.Engine.Domain.Abstractions;

public interface ITenantBrandingService
{
    Task<BrandingConfig> GetBrandingConfig(Guid tenantId, CancellationToken ct = default);
    Task UpdateBrandingConfig(Guid tenantId, BrandingConfig config, CancellationToken ct = default);
    Task<string> UploadLogo(Guid tenantId, Stream fileStream, string fileName, string contentType, CancellationToken ct = default);
    Task<string> UploadCompactLogo(Guid tenantId, Stream fileStream, string fileName, string contentType, CancellationToken ct = default);
    Task<string> UploadFavicon(Guid tenantId, Stream fileStream, string fileName, string contentType, CancellationToken ct = default);
    Task InvalidateCache(Guid tenantId, CancellationToken ct = default);
}
