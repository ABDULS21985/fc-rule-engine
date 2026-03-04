using System.Collections.Concurrent;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FC.Engine.Infrastructure.Metadata;

namespace FC.Engine.Infrastructure.Caching;

public class TemplateMetadataCache : ITemplateMetadataCache
{
    private readonly ConcurrentDictionary<string, CachedTemplate> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceProvider _serviceProvider;

    public TemplateMetadataCache(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<CachedTemplate> GetPublishedTemplate(string returnCode, CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var key = BuildCacheKey(tenantId, returnCode);

        if (_cache.TryGetValue(key, out var cached))
            return cached;

        // Try global key fallback
        if (tenantId.HasValue)
        {
            var globalKey = BuildCacheKey(null, returnCode);
            if (_cache.TryGetValue(globalKey, out cached))
                return cached;
        }

        var template = await LoadFromDatabase(returnCode, ct);
        _cache[key] = template;
        return template;
    }

    public async Task<IReadOnlyList<CachedTemplate>> GetAllPublishedTemplates(CancellationToken ct = default)
    {
        if (_cache.IsEmpty)
        {
            await LoadAllFromDatabase(ct);
        }

        var tenantId = ResolveTenantId();
        if (!tenantId.HasValue)
        {
            // No tenant context (warmup or PlatformAdmin) — return all
            return _cache.Values.ToList().AsReadOnly();
        }

        // Return only templates for this tenant or global templates
        var tenantPrefix = tenantId.Value.ToString().ToUpperInvariant() + ":";
        const string globalPrefix = "GLOBAL:";
        return _cache
            .Where(kvp => kvp.Key.StartsWith(tenantPrefix) || kvp.Key.StartsWith(globalPrefix))
            .Select(kvp => kvp.Value)
            .ToList()
            .AsReadOnly();
    }

    public void Invalidate(string returnCode)
    {
        // Invalidate all tenant-scoped and global versions of this template
        var upperCode = returnCode.ToUpperInvariant();
        var keysToRemove = _cache.Keys
            .Where(k => k.EndsWith(":" + upperCode))
            .ToList();
        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }
    }

    public void InvalidateAll()
    {
        _cache.Clear();
    }

    private Guid? ResolveTenantId()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var tenantContext = scope.ServiceProvider.GetService<ITenantContext>();
            return tenantContext?.CurrentTenantId;
        }
        catch
        {
            // During startup warmup, no HTTP context is available
            return null;
        }
    }

    private static string BuildCacheKey(Guid? tenantId, string returnCode)
    {
        var prefix = tenantId.HasValue
            ? tenantId.Value.ToString().ToUpperInvariant()
            : "GLOBAL";
        return $"{prefix}:{returnCode.ToUpperInvariant()}";
    }

    private async Task<CachedTemplate> LoadFromDatabase(string returnCode, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();

        var template = await db.ReturnTemplates
            .Include(t => t.Module)
            .Include(t => t.Versions.Where(v => v.Status == TemplateStatus.Published))
                .ThenInclude(v => v.Fields)
            .Include(t => t.Versions.Where(v => v.Status == TemplateStatus.Published))
                .ThenInclude(v => v.ItemCodes)
            .Include(t => t.Versions.Where(v => v.Status == TemplateStatus.Published))
                .ThenInclude(v => v.IntraSheetFormulas)
            .FirstOrDefaultAsync(t => t.ReturnCode == returnCode, ct)
            ?? throw new InvalidOperationException($"No published template found for return code: {returnCode}");

        var publishedVersion = template.Versions
            .FirstOrDefault(v => v.Status == TemplateStatus.Published)
            ?? throw new InvalidOperationException($"Template '{returnCode}' has no published version");

        return MapToCache(template, publishedVersion);
    }

    private async Task LoadAllFromDatabase(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();

        var templates = await db.ReturnTemplates
            .Include(t => t.Module)
            .Include(t => t.Versions.Where(v => v.Status == TemplateStatus.Published))
                .ThenInclude(v => v.Fields)
            .Include(t => t.Versions.Where(v => v.Status == TemplateStatus.Published))
                .ThenInclude(v => v.ItemCodes)
            .Include(t => t.Versions.Where(v => v.Status == TemplateStatus.Published))
                .ThenInclude(v => v.IntraSheetFormulas)
            .Where(t => t.Versions.Any(v => v.Status == TemplateStatus.Published))
            .ToListAsync(ct);

        var tenantId = ResolveTenantId();
        foreach (var template in templates)
        {
            var publishedVersion = template.Versions.First(v => v.Status == TemplateStatus.Published);
            var cached = MapToCache(template, publishedVersion);
            // Use template's TenantId for the cache key; null TenantId = GLOBAL
            var cacheKey = BuildCacheKey(template.TenantId, template.ReturnCode);
            _cache[cacheKey] = cached;
        }
    }

    private static CachedTemplate MapToCache(ReturnTemplate template, TemplateVersion version)
    {
        return new CachedTemplate
        {
            TemplateId = template.Id,
            ReturnCode = template.ReturnCode,
            Name = template.Name,
            Frequency = template.Frequency,
            StructuralCategory = template.StructuralCategory.ToString(),
            PhysicalTableName = template.PhysicalTableName,
            XmlRootElement = template.XmlRootElement,
            XmlNamespace = template.XmlNamespace,
            ModuleId = template.ModuleId,
            ModuleCode = template.Module?.ModuleCode,
            CurrentVersion = new CachedTemplateVersion
            {
                Id = version.Id,
                VersionNumber = version.VersionNumber,
                Fields = version.Fields.OrderBy(f => f.FieldOrder).ToList().AsReadOnly(),
                ItemCodes = version.ItemCodes.ToList().AsReadOnly(),
                IntraSheetFormulas = version.IntraSheetFormulas
                    .Where(f => f.IsActive)
                    .OrderBy(f => f.SortOrder)
                    .ToList()
                    .AsReadOnly()
            }
        };
    }
}
