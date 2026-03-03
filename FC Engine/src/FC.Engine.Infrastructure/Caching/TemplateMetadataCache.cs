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
        var key = returnCode.ToUpperInvariant();
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var template = await LoadFromDatabase(returnCode, ct);
        _cache[key] = template;
        return template;
    }

    public async Task<IReadOnlyList<CachedTemplate>> GetAllPublishedTemplates(CancellationToken ct = default)
    {
        // If cache is empty, load all published templates
        if (_cache.IsEmpty)
        {
            await LoadAllFromDatabase(ct);
        }

        return _cache.Values.ToList().AsReadOnly();
    }

    public void Invalidate(string returnCode)
    {
        _cache.TryRemove(returnCode.ToUpperInvariant(), out _);
    }

    public void InvalidateAll()
    {
        _cache.Clear();
    }

    private async Task<CachedTemplate> LoadFromDatabase(string returnCode, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();

        var template = await db.ReturnTemplates
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
            .Include(t => t.Versions.Where(v => v.Status == TemplateStatus.Published))
                .ThenInclude(v => v.Fields)
            .Include(t => t.Versions.Where(v => v.Status == TemplateStatus.Published))
                .ThenInclude(v => v.ItemCodes)
            .Include(t => t.Versions.Where(v => v.Status == TemplateStatus.Published))
                .ThenInclude(v => v.IntraSheetFormulas)
            .Where(t => t.Versions.Any(v => v.Status == TemplateStatus.Published))
            .ToListAsync(ct);

        foreach (var template in templates)
        {
            var publishedVersion = template.Versions.First(v => v.Status == TemplateStatus.Published);
            var cached = MapToCache(template, publishedVersion);
            _cache[template.ReturnCode.ToUpperInvariant()] = cached;
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
