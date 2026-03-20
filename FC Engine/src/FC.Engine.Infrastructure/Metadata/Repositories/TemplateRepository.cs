using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Metadata.Repositories;

public class TemplateRepository : ITemplateRepository
{
    private readonly MetadataDbContext _db;

    public TemplateRepository(MetadataDbContext db) => _db = db;

    public async Task<ReturnTemplate?> GetById(int id, CancellationToken ct = default)
    {
        return await _db.ReturnTemplates
            .Include(t => t.Versions)
                .ThenInclude(v => v.Fields)
            .Include(t => t.Versions)
                .ThenInclude(v => v.ItemCodes)
            .Include(t => t.Versions)
                .ThenInclude(v => v.IntraSheetFormulas)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<ReturnTemplate?> GetByReturnCode(string returnCode, CancellationToken ct = default)
    {
        return await _db.ReturnTemplates
            .Include(t => t.Versions)
                .ThenInclude(v => v.Fields)
            .Include(t => t.Versions)
                .ThenInclude(v => v.ItemCodes)
            .Include(t => t.Versions)
                .ThenInclude(v => v.IntraSheetFormulas)
            .FirstOrDefaultAsync(t => t.ReturnCode == returnCode, ct);
    }

    public async Task<ReturnTemplate?> GetPublishedByReturnCode(string returnCode, CancellationToken ct = default)
    {
        return await _db.ReturnTemplates
            .Include(t => t.Versions.Where(v => v.Status == TemplateStatus.Published))
                .ThenInclude(v => v.Fields)
            .Include(t => t.Versions.Where(v => v.Status == TemplateStatus.Published))
                .ThenInclude(v => v.ItemCodes)
            .Include(t => t.Versions.Where(v => v.Status == TemplateStatus.Published))
                .ThenInclude(v => v.IntraSheetFormulas)
            .FirstOrDefaultAsync(t => t.ReturnCode == returnCode, ct);
    }

    public async Task<IReadOnlyList<ReturnTemplate>> GetAll(CancellationToken ct = default)
    {
        return await _db.ReturnTemplates
            .Include(t => t.Versions)
                .ThenInclude(v => v.Fields)
            .Include(t => t.Versions)
                .ThenInclude(v => v.ItemCodes)
            .OrderBy(t => t.ReturnCode)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ReturnTemplate>> GetByFrequency(string frequency, CancellationToken ct = default)
    {
        return await _db.ReturnTemplates
            .Where(t => t.Frequency.ToString() == frequency)
            .OrderBy(t => t.ReturnCode)
            .ToListAsync(ct);
    }

    public async Task Add(ReturnTemplate template, CancellationToken ct = default)
    {
        _db.ReturnTemplates.Add(template);
        await _db.SaveChangesAsync(ct);
    }

    public async Task Update(ReturnTemplate template, CancellationToken ct = default)
    {
        _db.ReturnTemplates.Update(template);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> ExistsByReturnCode(string returnCode, CancellationToken ct = default)
    {
        return await _db.ReturnTemplates.AnyAsync(t => t.ReturnCode == returnCode, ct);
    }

    public async Task<IReadOnlyList<ReturnTemplate>> GetAllForTenant(Guid tenantId, CancellationToken ct = default)
    {
        return await _db.ReturnTemplates
            .Include(t => t.Versions)
            .Where(t => t.TenantId == tenantId || t.TenantId == null)
            .OrderBy(t => t.ReturnCode)
            .ToListAsync(ct);
    }

    public async Task<ReturnTemplate?> GetByReturnCodeForTenant(string returnCode, Guid tenantId, CancellationToken ct = default)
    {
        return await _db.ReturnTemplates
            .Include(t => t.Versions)
                .ThenInclude(v => v.Fields)
            .Include(t => t.Versions)
                .ThenInclude(v => v.ItemCodes)
            .Include(t => t.Versions)
                .ThenInclude(v => v.IntraSheetFormulas)
            .Where(t => t.TenantId == tenantId || t.TenantId == null)
            .FirstOrDefaultAsync(t => t.ReturnCode == returnCode, ct);
    }

    public async Task<IReadOnlyList<ReturnTemplate>> GetByModuleIds(IEnumerable<int> moduleIds, CancellationToken ct = default)
    {
        var ids = moduleIds.ToList();
        return await _db.ReturnTemplates
            .Include(t => t.Versions)
            .Where(t => t.ModuleId.HasValue && ids.Contains(t.ModuleId.Value))
            .OrderBy(t => t.ReturnCode)
            .ToListAsync(ct);
    }

    public async Task<TemplateVersion?> GetLatestDraftVersion(string returnCode, CancellationToken ct = default)
    {
        // Load the template with its Draft/Review versions and their fields.
        var template = await _db.ReturnTemplates
            .Include(t => t.Versions.Where(v =>
                v.Status == TemplateStatus.Draft || v.Status == TemplateStatus.Review))
                .ThenInclude(v => v.Fields)
            .FirstOrDefaultAsync(t => t.ReturnCode == returnCode, ct);

        if (template is null) return null;

        var draftVersion = template.Versions
            .Where(v => v.Status is TemplateStatus.Draft or TemplateStatus.Review)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault();

        if (draftVersion is null) return null;

        // Load formulas separately to keep the filtered-include query simple.
        var formulas = await _db.IntraSheetFormulas
            .Where(f => f.TemplateVersionId == draftVersion.Id)
            .OrderBy(f => f.SortOrder)
            .ToListAsync(ct);

        draftVersion.SetFormulas(formulas);
        return draftVersion;
    }

    public Task<bool> HasExistingDraft(int templateId, CancellationToken ct = default)
    {
        return _db.TemplateVersions.AnyAsync(
            v => v.TemplateId == templateId && (v.Status == TemplateStatus.Draft || v.Status == TemplateStatus.Review),
            ct);
    }
}
