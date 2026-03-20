using FC.Engine.Application.DTOs;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;

namespace FC.Engine.Application.Services;

public class TemplateService
{
    private readonly ITemplateRepository _templateRepo;
    private readonly IAuditLogger _audit;
    private readonly ITemplateMetadataCache _cache;
    private readonly ISqlTypeMapper _sqlTypeMapper;
    private readonly IEntitlementService _entitlementService;
    private readonly ITenantContext _tenantContext;

    public TemplateService(
        ITemplateRepository templateRepo,
        IAuditLogger audit,
        ITemplateMetadataCache cache,
        ISqlTypeMapper sqlTypeMapper,
        IEntitlementService entitlementService,
        ITenantContext tenantContext)
    {
        _templateRepo = templateRepo;
        _audit = audit;
        _cache = cache;
        _sqlTypeMapper = sqlTypeMapper;
        _entitlementService = entitlementService;
        _tenantContext = tenantContext;
    }

    public async Task<TemplateDto> CreateTemplate(CreateTemplateRequest request, CancellationToken ct = default)
    {
        if (await _templateRepo.ExistsByReturnCode(request.ReturnCode, ct))
            throw new InvalidOperationException($"Template '{request.ReturnCode}' already exists");

        var returnCode = Domain.ValueObjects.ReturnCode.Parse(request.ReturnCode);
        var tenantId = request.TenantId ?? _tenantContext.CurrentTenantId;

        var template = new ReturnTemplate
        {
            TenantId = tenantId,
            ModuleId = request.ModuleId,
            ReturnCode = request.ReturnCode,
            Name = request.Name,
            Description = request.Description,
            Frequency = request.Frequency,
            StructuralCategory = request.StructuralCategory,
            PhysicalTableName = returnCode.ToTableName(),
            XmlRootElement = returnCode.ToXmlRootElement(),
            XmlNamespace = returnCode.ToXmlNamespace(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = request.CreatedBy,
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = request.CreatedBy
        };

        // Auto-create first draft version
        template.CreateDraftVersion(request.CreatedBy);

        await _templateRepo.Add(template, ct);
        await _audit.Log("ReturnTemplate", template.Id, "Created", null, template, request.CreatedBy, ct);

        return MapToDto(template);
    }

    public async Task<TemplateDetailDto?> GetTemplateDetail(string returnCode, CancellationToken ct = default)
    {
        var template = await _templateRepo.GetByReturnCode(returnCode, ct);
        if (template == null) return null;
        var tenantId = _tenantContext.CurrentTenantId;
        if (template.TenantId.HasValue && template.TenantId != tenantId) return null;
        return MapToDetailDto(template);
    }

    public async Task<IReadOnlyList<TemplateDto>> GetAllTemplates(CancellationToken ct = default)
    {
        var templates = await _templateRepo.GetAll(ct);
        var tenantId = _tenantContext.CurrentTenantId;
        var filtered = templates.Where(t => t.TenantId == null || t.TenantId == tenantId);
        return filtered.Select(MapToDto).ToList();
    }

    public async Task<List<ReturnTemplate>> GetEntitledTemplates(Guid tenantId, CancellationToken ct = default)
    {
        var entitlement = await _entitlementService.ResolveEntitlements(tenantId, ct);
        var activeModuleIds = entitlement.ActiveModules
            .Select(m => m.ModuleId)
            .Distinct()
            .ToList();

        if (activeModuleIds.Count == 0)
        {
            return new List<ReturnTemplate>();
        }

        var templates = await _templateRepo.GetByModuleIds(activeModuleIds, ct);
        return templates
            .Where(t => t.TenantId == tenantId || t.TenantId == null)
            .ToList();
    }

    public async Task AddFieldToVersion(int templateId, int versionId, AddFieldRequest request, string performedBy, CancellationToken ct = default)
    {
        var template = await _templateRepo.GetById(templateId, ct)
            ?? throw new InvalidOperationException($"Template {templateId} not found");

        var version = template.GetVersion(versionId);
        if (version.Status != TemplateStatus.Draft)
            throw new InvalidOperationException("Fields can only be added to Draft versions");

        var field = new TemplateField
        {
            CreatedAt = DateTime.UtcNow
        };
        ApplyFieldRequest(field, request);

        version.AddField(field);
        await _templateRepo.Update(template, ct);
        await _audit.Log("TemplateField", field.Id, "Added", null, field, performedBy, ct);
    }

    public async Task UpdateFieldInVersion(
        int templateId,
        int versionId,
        int fieldId,
        AddFieldRequest request,
        string performedBy,
        CancellationToken ct = default)
    {
        var (template, version) = await LoadDraftVersion(templateId, versionId, ct);

        var field = version.Fields.FirstOrDefault(f => f.Id == fieldId)
            ?? throw new InvalidOperationException($"Field {fieldId} not found");

        var before = SnapshotField(field);
        ApplyFieldRequest(field, request);

        await _templateRepo.Update(template, ct);
        await _audit.Log("TemplateField", field.Id, "Updated", before, SnapshotField(field), performedBy, ct);
    }

    public async Task RemoveFieldFromVersion(
        int templateId,
        int versionId,
        int fieldId,
        string performedBy,
        CancellationToken ct = default)
    {
        var (template, version) = await LoadDraftVersion(templateId, versionId, ct);

        var field = version.Fields.FirstOrDefault(f => f.Id == fieldId)
            ?? throw new InvalidOperationException($"Field {fieldId} not found");

        var before = SnapshotField(field);
        version.RemoveField(fieldId);

        await _templateRepo.Update(template, ct);
        await _audit.Log("TemplateField", fieldId, "Removed", before, null, performedBy, ct);
    }

    private async Task<(ReturnTemplate Template, TemplateVersion Version)> LoadDraftVersion(
        int templateId,
        int versionId,
        CancellationToken ct)
    {
        var template = await _templateRepo.GetById(templateId, ct)
            ?? throw new InvalidOperationException($"Template {templateId} not found");

        var version = template.GetVersion(versionId);
        if (version.Status != TemplateStatus.Draft)
            throw new InvalidOperationException("Fields can only be modified on Draft versions");

        return (template, version);
    }

    private void ApplyFieldRequest(TemplateField field, AddFieldRequest request)
    {
        field.FieldName = request.FieldName;
        field.DisplayName = request.DisplayName;
        field.XmlElementName = string.IsNullOrWhiteSpace(request.XmlElementName)
            ? request.FieldName
            : request.XmlElementName;
        field.LineCode = request.LineCode;
        field.SectionName = request.SectionName;
        field.SectionOrder = request.SectionOrder;
        field.FieldOrder = request.FieldOrder;
        field.DataType = request.DataType;
        field.SqlType = _sqlTypeMapper.MapToSqlType(request.DataType);
        field.IsRequired = request.IsRequired;
        field.IsComputed = request.IsComputed;
        field.IsKeyField = request.IsKeyField;
        field.DefaultValue = request.DefaultValue;
        field.MinValue = request.MinValue;
        field.MaxValue = request.MaxValue;
        field.MaxLength = request.MaxLength;
        field.AllowedValues = request.AllowedValues;
        field.HelpText = request.HelpText;
        field.ValidationNote = request.ValidationNote;
        field.RegulatoryReference = request.RegulatoryReference;
        field.DataClassification = request.DataClassification;
    }

    private static object SnapshotField(TemplateField field) => new
    {
        field.Id,
        field.FieldName,
        field.DisplayName,
        field.XmlElementName,
        field.LineCode,
        field.SectionName,
        field.SectionOrder,
        field.FieldOrder,
        DataType = field.DataType.ToString(),
        field.SqlType,
        field.IsRequired,
        field.IsComputed,
        field.IsKeyField,
        field.DefaultValue,
        field.MinValue,
        field.MaxValue,
        field.MaxLength,
        field.AllowedValues,
        field.HelpText,
        field.ValidationNote,
        field.RegulatoryReference,
        DataClassification = field.DataClassification.ToString()
    };

    private static TemplateDto MapToDto(ReturnTemplate t)
    {
        var published = t.CurrentPublishedVersion;
        return new TemplateDto
        {
            Id = t.Id,
            ReturnCode = t.ReturnCode,
            Name = t.Name,
            Description = t.Description,
            Frequency = t.Frequency.ToString(),
            StructuralCategory = t.StructuralCategory.ToString(),
            PhysicalTableName = t.PhysicalTableName,
            PublishedVersionId = published?.Id,
            PublishedVersionNumber = published?.VersionNumber,
            FieldCount = published?.Fields.Count ?? 0,
            CreatedAt = t.CreatedAt
        };
    }

    private static TemplateDetailDto MapToDetailDto(ReturnTemplate t)
    {
        var dto = new TemplateDetailDto
        {
            Id = t.Id,
            ReturnCode = t.ReturnCode,
            Name = t.Name,
            Description = t.Description,
            Frequency = t.Frequency.ToString(),
            StructuralCategory = t.StructuralCategory.ToString(),
            PhysicalTableName = t.PhysicalTableName,
            XmlRootElement = t.XmlRootElement,
            XmlNamespace = t.XmlNamespace,
            CreatedAt = t.CreatedAt
        };

        var published = t.CurrentPublishedVersion;
        dto.PublishedVersionId = published?.Id;
        dto.PublishedVersionNumber = published?.VersionNumber;
        dto.FieldCount = published?.Fields.Count ?? 0;

        dto.Versions = t.Versions.OrderByDescending(v => v.VersionNumber).Select(v => new TemplateVersionDto
        {
            Id = v.Id,
            VersionNumber = v.VersionNumber,
            Status = v.Status.ToString(),
            EffectiveFrom = v.EffectiveFrom,
            EffectiveTo = v.EffectiveTo,
            PublishedAt = v.PublishedAt,
            ApprovedAt = v.ApprovedAt,
            ApprovedBy = v.ApprovedBy,
            ChangeSummary = v.ChangeSummary,
            CreatedAt = v.CreatedAt,
            CreatedBy = v.CreatedBy,
            FieldCount = v.Fields.Count,
            FormulaCount = v.IntraSheetFormulas.Count,
            Fields = v.Fields.OrderBy(f => f.SectionOrder).ThenBy(f => f.FieldOrder).Select(f => new TemplateFieldDto
            {
                Id = f.Id,
                FieldName = f.FieldName,
                DisplayName = f.DisplayName,
                XmlElementName = f.XmlElementName,
                LineCode = f.LineCode,
                SectionName = f.SectionName,
                SectionOrder = f.SectionOrder,
                FieldOrder = f.FieldOrder,
                DataType = f.DataType.ToString(),
                SqlType = f.SqlType,
                IsRequired = f.IsRequired,
                IsComputed = f.IsComputed,
                IsKeyField = f.IsKeyField,
                DefaultValue = f.DefaultValue,
                MinValue = f.MinValue,
                MaxValue = f.MaxValue,
                MaxLength = f.MaxLength,
                AllowedValues = f.AllowedValues,
                HelpText = f.HelpText,
                ValidationNote = f.ValidationNote,
                RegulatoryReference = f.RegulatoryReference,
                DataClassification = f.DataClassification.ToString()
            }).ToList(),
            ItemCodes = v.ItemCodes.Select(ic => new TemplateItemCodeDto
            {
                Id = ic.Id,
                ItemCode = ic.ItemCode,
                ItemName = ic.ItemDescription,
                SortOrder = ic.SortOrder,
                IsTotalRow = ic.IsTotalRow
            }).ToList()
        }).ToList();

        return dto;
    }
}
