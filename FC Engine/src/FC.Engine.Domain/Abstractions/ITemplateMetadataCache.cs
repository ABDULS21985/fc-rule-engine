using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;

namespace FC.Engine.Domain.Abstractions;

public interface ITemplateMetadataCache
{
    Task<CachedTemplate> GetPublishedTemplate(string returnCode, CancellationToken ct = default);
    Task<CachedTemplate> GetPublishedTemplate(Guid tenantId, string returnCode, CancellationToken ct = default);
    Task<IReadOnlyList<CachedTemplate>> GetAllPublishedTemplates(CancellationToken ct = default);
    Task<IReadOnlyList<CachedTemplate>> GetAllPublishedTemplates(Guid tenantId, CancellationToken ct = default);
    void Invalidate(string returnCode);
    void Invalidate(Guid? tenantId, string returnCode);
    void InvalidateModule(int moduleId);
    void InvalidateModule(string moduleCode);
    void InvalidateAll();
}

/// <summary>
/// Cached view of a published template with eager-loaded fields, formulas, and item codes.
/// </summary>
public class CachedTemplate
{
    public int TemplateId { get; set; }
    public Guid? TenantId { get; set; }
    public string ReturnCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string InstitutionType { get; set; } = string.Empty;
    public ReturnFrequency Frequency { get; set; }
    public string StructuralCategory { get; set; } = string.Empty;
    public string PhysicalTableName { get; set; } = string.Empty;
    public string XmlRootElement { get; set; } = string.Empty;
    public string XmlNamespace { get; set; } = string.Empty;
    public int? ModuleId { get; set; }
    public string? ModuleCode { get; set; }
    public CachedTemplateVersion CurrentVersion { get; set; } = null!;
}

public class CachedTemplateVersion
{
    public int Id { get; set; }
    public int VersionNumber { get; set; }
    public IReadOnlyList<TemplateField> Fields { get; set; } = Array.Empty<TemplateField>();
    public IReadOnlyList<TemplateItemCode> ItemCodes { get; set; } = Array.Empty<TemplateItemCode>();
    public IReadOnlyList<IntraSheetFormula> IntraSheetFormulas { get; set; } = Array.Empty<IntraSheetFormula>();
}
