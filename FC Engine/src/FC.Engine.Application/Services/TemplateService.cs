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

    public TemplateService(
        ITemplateRepository templateRepo,
        IAuditLogger audit,
        ITemplateMetadataCache cache,
        ISqlTypeMapper sqlTypeMapper,
        IEntitlementService entitlementService)
    {
        _templateRepo = templateRepo;
        _audit = audit;
        _cache = cache;
        _sqlTypeMapper = sqlTypeMapper;
        _entitlementService = entitlementService;
    }

    public async Task<TemplateDto> CreateTemplate(CreateTemplateRequest request, CancellationToken ct = default)
    {
        if (await _templateRepo.ExistsByReturnCode(request.ReturnCode, ct))
            throw new InvalidOperationException($"Template '{request.ReturnCode}' already exists");

        var returnCode = Domain.ValueObjects.ReturnCode.Parse(request.ReturnCode);

        var template = new ReturnTemplate
        {
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
        return MapToDetailDto(template);
    }

    public async Task<IReadOnlyList<TemplateDto>> GetAllTemplates(CancellationToken ct = default)
    {
        var templates = await _templateRepo.GetAll(ct);
        return templates.Select(MapToDto).ToList();
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
            FieldName = request.FieldName,
            DisplayName = request.DisplayName,
            XmlElementName = request.XmlElementName,
            LineCode = request.LineCode,
            SectionName = request.SectionName,
            FieldOrder = request.FieldOrder,
            DataType = request.DataType,
            SqlType = _sqlTypeMapper.MapToSqlType(request.DataType),
            IsRequired = request.IsRequired,
            IsKeyField = request.IsKeyField,
            MinValue = request.MinValue,
            MaxValue = request.MaxValue,
            MaxLength = request.MaxLength,
            AllowedValues = request.AllowedValues,
            CreatedAt = DateTime.UtcNow
        };

        version.AddField(field);
        await _templateRepo.Update(template, ct);
        await _audit.Log("TemplateField", field.Id, "Added", null, field, performedBy, ct);
    }

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
            PublishedAt = v.PublishedAt,
            ApprovedBy = v.ApprovedBy,
            ChangeSummary = v.ChangeSummary,
            FieldCount = v.Fields.Count,
            FormulaCount = v.IntraSheetFormulas.Count,
            Fields = v.Fields.OrderBy(f => f.FieldOrder).Select(f => new TemplateFieldDto
            {
                Id = f.Id,
                FieldName = f.FieldName,
                DisplayName = f.DisplayName,
                XmlElementName = f.XmlElementName,
                LineCode = f.LineCode,
                SectionName = f.SectionName,
                FieldOrder = f.FieldOrder,
                DataType = f.DataType.ToString(),
                SqlType = f.SqlType,
                IsRequired = f.IsRequired,
                IsComputed = f.IsComputed,
                IsKeyField = f.IsKeyField,
                MinValue = f.MinValue,
                MaxValue = f.MaxValue,
                MaxLength = f.MaxLength,
                AllowedValues = f.AllowedValues
            }).ToList(),
            ItemCodes = v.ItemCodes.Select(ic => new TemplateItemCodeDto
            {
                Id = ic.Id,
                ItemCode = ic.ItemCode,
                ItemName = ic.ItemDescription,
                SortOrder = ic.SortOrder
            }).ToList()
        }).ToList();

        return dto;
    }
}
