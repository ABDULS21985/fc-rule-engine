using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Metadata;

public class ReturnTemplate
{
    public int Id { get; set; }

    /// <summary>FK to Tenant for RLS. Null for global/system templates.</summary>
    public Guid? TenantId { get; set; }

    public string ReturnCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ReturnFrequency Frequency { get; set; }
    public StructuralCategory StructuralCategory { get; set; }
    public string PhysicalTableName { get; set; } = string.Empty;
    public string XmlRootElement { get; set; } = string.Empty;
    public string XmlNamespace { get; set; } = string.Empty;
    public bool IsSystemTemplate { get; set; }
    public string OwnerDepartment { get; set; } = "DFIS";
    public string InstitutionType { get; set; } = "FC";
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;

    private readonly List<TemplateVersion> _versions = new();
    public IReadOnlyList<TemplateVersion> Versions => _versions.AsReadOnly();

    public TemplateVersion? CurrentPublishedVersion =>
        _versions.FirstOrDefault(v => v.Status == TemplateStatus.Published);

    public TemplateVersion CreateDraftVersion(string createdBy)
    {
        var nextVersionNumber = _versions.Any() ? _versions.Max(v => v.VersionNumber) + 1 : 1;
        var version = new TemplateVersion
        {
            TemplateId = Id,
            VersionNumber = nextVersionNumber,
            Status = TemplateStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };
        _versions.Add(version);
        return version;
    }

    public TemplateVersion GetVersion(int versionId) =>
        _versions.First(v => v.Id == versionId);

    public TemplateVersion? GetPreviousPublishedVersion(int currentVersionId) =>
        _versions
            .Where(v => v.Id != currentVersionId && v.Status is TemplateStatus.Published or TemplateStatus.Deprecated)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault();

    public void AddVersion(TemplateVersion version) => _versions.Add(version);
}
