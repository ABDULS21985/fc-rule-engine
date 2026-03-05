using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FC.Engine.Infrastructure.Services;

public class TenantBrandingService : ITenantBrandingService
{
    private static readonly string[] AllowedImageTypes =
    {
        "image/png",
        "image/svg+xml",
        "image/jpeg",
        "image/jpg",
        "image/x-icon",
        "image/vnd.microsoft.icon"
    };

    private const long MaxAssetSizeBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    private readonly MetadataDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IFileStorageService _storage;

    public TenantBrandingService(
        MetadataDbContext db,
        IMemoryCache cache,
        IFileStorageService storage)
    {
        _db = db;
        _cache = cache;
        _storage = storage;
    }

    public async Task<BrandingConfig> GetBrandingConfig(Guid tenantId, CancellationToken ct = default)
    {
        var key = BuildCacheKey(tenantId);
        if (_cache.TryGetValue(key, out BrandingConfig? cached) && cached is not null)
        {
            return cached;
        }

        var tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);

        var config = tenant?.GetBrandingConfig() ?? BrandingConfig.WithDefaults();

        _cache.Set(key, config, new MemoryCacheEntryOptions
        {
            SlidingExpiration = CacheTtl
        });

        return config;
    }

    public async Task UpdateBrandingConfig(Guid tenantId, BrandingConfig config, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found");

        ValidateConfig(config);

        tenant.SetBrandingConfig(config);
        await _db.SaveChangesAsync(ct);

        _cache.Remove(BuildCacheKey(tenantId));
    }

    public async Task<string> UploadLogo(Guid tenantId, Stream fileStream, string fileName, string contentType, CancellationToken ct = default)
    {
        await using var uploadStream = await PrepareAssetStream(fileStream, contentType, ct);

        var extension = ResolveExtension(fileName, contentType, ".png");
        var path = $"tenants/{tenantId}/branding/logo{extension}";
        var url = await _storage.UploadAsync(path, uploadStream, contentType, ct);

        var config = await GetBrandingConfig(tenantId, ct);
        config.LogoUrl = url;

        await UpdateBrandingConfig(tenantId, config, ct);
        return url;
    }

    public async Task<string> UploadFavicon(Guid tenantId, Stream fileStream, string fileName, string contentType, CancellationToken ct = default)
    {
        await using var uploadStream = await PrepareAssetStream(fileStream, contentType, ct);

        var extension = ResolveExtension(fileName, contentType, ".ico");
        var path = $"tenants/{tenantId}/branding/favicon{extension}";
        var url = await _storage.UploadAsync(path, uploadStream, contentType, ct);

        var config = await GetBrandingConfig(tenantId, ct);
        config.FaviconUrl = url;

        await UpdateBrandingConfig(tenantId, config, ct);
        return url;
    }

    public Task InvalidateCache(Guid tenantId, CancellationToken ct = default)
    {
        _cache.Remove(BuildCacheKey(tenantId));
        return Task.CompletedTask;
    }

    private static string BuildCacheKey(Guid tenantId) => $"branding:{tenantId}";

    private static async Task<Stream> PrepareAssetStream(Stream fileStream, string contentType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(contentType) || !AllowedImageTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("File must be PNG, SVG, JPEG, or ICO.");
        }

        if (!fileStream.CanRead)
        {
            throw new InvalidOperationException("File stream cannot be read.");
        }

        if (fileStream.CanSeek)
        {
            if (fileStream.Length > MaxAssetSizeBytes)
            {
                throw new InvalidOperationException("File must be under 2MB.");
            }

            fileStream.Position = 0;
            return fileStream;
        }

        // For non-seekable streams, copy once into memory and upload from that buffer.
        var buffer = new MemoryStream();
        await fileStream.CopyToAsync(buffer, ct);
        if (buffer.Length > MaxAssetSizeBytes)
        {
            await buffer.DisposeAsync();
            throw new InvalidOperationException("File must be under 2MB.");
        }

        buffer.Position = 0;
        return buffer;
    }

    private static string ResolveExtension(string fileName, string contentType, string fallback)
    {
        var extension = Path.GetExtension(fileName);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return extension.ToLowerInvariant();
        }

        return contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/svg+xml" => ".svg",
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
            _ => fallback
        };
    }

    private static void ValidateConfig(BrandingConfig config)
    {
        ValidateHexOrEmpty(config.PrimaryColor, nameof(config.PrimaryColor));
        ValidateHexOrEmpty(config.SecondaryColor, nameof(config.SecondaryColor));
        ValidateHexOrEmpty(config.AccentColor, nameof(config.AccentColor));
        ValidateHexOrEmpty(config.DangerColor, nameof(config.DangerColor));
        ValidateHexOrEmpty(config.SuccessColor, nameof(config.SuccessColor));
        ValidateHexOrEmpty(config.WarningColor, nameof(config.WarningColor));
        ValidateHexOrEmpty(config.BackgroundColor, nameof(config.BackgroundColor));
        ValidateHexOrEmpty(config.SidebarColor, nameof(config.SidebarColor));
    }

    private static void ValidateHexOrEmpty(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var hex = value.Trim();
        if (hex.Length != 7 || hex[0] != '#')
        {
            throw new InvalidOperationException($"{field} must be a hex color like #006B3F.");
        }

        for (var i = 1; i < hex.Length; i++)
        {
            if (!Uri.IsHexDigit(hex[i]))
            {
                throw new InvalidOperationException($"{field} must be a valid hex color.");
            }
        }
    }
}
