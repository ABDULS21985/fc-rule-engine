using System.Collections.Concurrent;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        return tenantId.HasValue
            ? await GetPublishedTemplate(tenantId.Value, returnCode, ct)
            : await GetPublishedTemplateCore(null, returnCode, ct);
    }

    public async Task<CachedTemplate> GetPublishedTemplate(Guid tenantId, string returnCode, CancellationToken ct = default)
    {
        return await GetPublishedTemplateCore(tenantId, returnCode, ct);
    }

    public async Task<IReadOnlyList<CachedTemplate>> GetAllPublishedTemplates(CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (!tenantId.HasValue)
        {
            if (_cache.IsEmpty)
            {
                await LoadAllFromDatabase(null, ct);
            }

            return _cache.Values.ToList().AsReadOnly();
        }

        return await GetAllPublishedTemplates(tenantId.Value, ct);
    }

    public async Task<IReadOnlyList<CachedTemplate>> GetAllPublishedTemplates(Guid tenantId, CancellationToken ct = default)
    {
        var templates = GetTemplatesForTenant(tenantId);
        if (templates.Count == 0)
        {
            await LoadAllFromDatabase(tenantId, ct);
            templates = GetTemplatesForTenant(tenantId);
        }

        return templates;
    }

    public void Invalidate(string returnCode)
    {
        var upperCode = returnCode.ToUpperInvariant();
        var keysToRemove = _cache.Keys
            .Where(k => k.EndsWith(":" + upperCode, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }
    }

    public void Invalidate(Guid? tenantId, string returnCode)
    {
        var key = BuildCacheKey(tenantId, returnCode);
        _cache.TryRemove(key, out _);
    }

    public void InvalidateModule(int moduleId)
    {
        var keys = _cache
            .Where(kvp => kvp.Value.ModuleId == moduleId)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keys)
        {
            _cache.TryRemove(key, out _);
        }
    }

    public void InvalidateModule(string moduleCode)
    {
        var keys = _cache
            .Where(kvp => string.Equals(kvp.Value.ModuleCode, moduleCode, StringComparison.OrdinalIgnoreCase))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keys)
        {
            _cache.TryRemove(key, out _);
        }
    }

    public void InvalidateAll()
    {
        _cache.Clear();
    }

    private async Task<CachedTemplate> GetPublishedTemplateCore(Guid? tenantId, string returnCode, CancellationToken ct)
    {
        var key = BuildCacheKey(tenantId, returnCode);

        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (tenantId.HasValue)
        {
            var globalKey = BuildCacheKey(null, returnCode);
            if (_cache.TryGetValue(globalKey, out cached))
            {
                return cached;
            }
        }

        var loaded = await LoadFromDatabase(returnCode, tenantId, ct);
        _cache[BuildCacheKey(loaded.TemplateTenantId, returnCode)] = loaded.Template;
        return loaded.Template;
    }

    private IReadOnlyList<CachedTemplate> GetTemplatesForTenant(Guid tenantId)
    {
        var tenantPrefix = tenantId.ToString().ToUpperInvariant() + ":";
        const string globalPrefix = "GLOBAL:";
        return _cache
            .Where(kvp => kvp.Key.StartsWith(tenantPrefix, StringComparison.OrdinalIgnoreCase)
                       || kvp.Key.StartsWith(globalPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(kvp => kvp.Value)
            .ToList()
            .AsReadOnly();
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
            // During startup warmup, no HTTP context is available.
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

    private async Task<(CachedTemplate Template, Guid? TemplateTenantId)> LoadFromDatabase(
        string returnCode,
        Guid? tenantId,
        CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();

        var baseQuery = db.ReturnTemplates
            .AsNoTracking()
            .AsSplitQuery()
            .Include(t => t.Module)
            .Include(t => t.Versions.Where(v => v.Status == TemplateStatus.Published))
                .ThenInclude(v => v.Fields)
            .Include(t => t.Versions.Where(v => v.Status == TemplateStatus.Published))
                .ThenInclude(v => v.ItemCodes)
            .Include(t => t.Versions.Where(v => v.Status == TemplateStatus.Published))
                .ThenInclude(v => v.IntraSheetFormulas);

        ReturnTemplate? template;

        if (tenantId.HasValue)
        {
            template = await baseQuery
                .FirstOrDefaultAsync(t => t.ReturnCode == returnCode && t.TenantId == tenantId, ct)
                ?? await baseQuery
                    .FirstOrDefaultAsync(t => t.ReturnCode == returnCode && t.TenantId == null, ct);
        }
        else
        {
            template = await baseQuery.FirstOrDefaultAsync(t => t.ReturnCode == returnCode, ct);
        }

        if (template is null)
        {
            throw new InvalidOperationException($"No published template found for return code: {returnCode}");
        }

        var publishedVersion = template.Versions
            .FirstOrDefault(v => v.Status == TemplateStatus.Published)
            ?? throw new InvalidOperationException($"Template '{returnCode}' has no published version");

        return (MapToCache(template, publishedVersion), template.TenantId);
    }

    private async Task LoadAllFromDatabase(Guid? tenantId, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();

        var query = db.ReturnTemplates
            .AsNoTracking()
            .AsSplitQuery()
            .Include(t => t.Module)
            .Include(t => t.Versions.Where(v => v.Status == TemplateStatus.Published))
                .ThenInclude(v => v.Fields)
            .Include(t => t.Versions.Where(v => v.Status == TemplateStatus.Published))
                .ThenInclude(v => v.ItemCodes)
            .Include(t => t.Versions.Where(v => v.Status == TemplateStatus.Published))
                .ThenInclude(v => v.IntraSheetFormulas)
            .Where(t => t.Versions.Any(v => v.Status == TemplateStatus.Published));

        if (tenantId.HasValue)
        {
            query = query.Where(t => t.TenantId == tenantId || t.TenantId == null);
        }

        var templates = await query.ToListAsync(ct);

        foreach (var template in templates)
        {
            var publishedVersion = template.Versions.First(v => v.Status == TemplateStatus.Published);
            var cached = MapToCache(template, publishedVersion);
            _cache[BuildCacheKey(template.TenantId, template.ReturnCode)] = cached;
        }
    }

    private static CachedTemplate MapToCache(ReturnTemplate template, TemplateVersion version)
    {
        return new CachedTemplate
        {
            TemplateId = template.Id,
            TenantId = template.TenantId,
            ReturnCode = template.ReturnCode,
            Name = template.Name,
            InstitutionType = template.InstitutionType,
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
