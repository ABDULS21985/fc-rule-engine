using FC.Engine.Domain.Enums;

namespace FC.Engine.Application.DTOs;

public class TemplateDto
{
    public int Id { get; set; }
    public string ReturnCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Frequency { get; set; } = string.Empty;
    public string StructuralCategory { get; set; } = string.Empty;
    public string PhysicalTableName { get; set; } = string.Empty;
    public int? PublishedVersionId { get; set; }
    public int? PublishedVersionNumber { get; set; }
    public int? CurrentVersionId { get; set; }
    public int? CurrentVersionNumber { get; set; }
    public string? CurrentVersionStatus { get; set; }
    public int FieldCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TemplateDetailDto : TemplateDto
{
    public string XmlRootElement { get; set; } = string.Empty;
    public string XmlNamespace { get; set; } = string.Empty;
    public List<TemplateVersionDto> Versions { get; set; } = new();
}

public class TemplateVersionDto
{
    public int Id { get; set; }
    public int VersionNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public string? ChangeSummary { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public int FieldCount { get; set; }
    public int FormulaCount { get; set; }
    public List<TemplateFieldDto> Fields { get; set; } = new();
    public List<TemplateItemCodeDto> ItemCodes { get; set; } = new();
}

public class TemplateFieldDto
{
    public int Id { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string XmlElementName { get; set; } = string.Empty;
    public string? LineCode { get; set; }
    public string? SectionName { get; set; }
    public int SectionOrder { get; set; }
    public int FieldOrder { get; set; }
    public string DataType { get; set; } = string.Empty;
    public string SqlType { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public bool IsComputed { get; set; }
    public bool IsKeyField { get; set; }
    public string? DefaultValue { get; set; }
    public string? MinValue { get; set; }
    public string? MaxValue { get; set; }
    public int? MaxLength { get; set; }
    public string? AllowedValues { get; set; }
    public string? HelpText { get; set; }
    public string? ValidationNote { get; set; }
    public string? RegulatoryReference { get; set; }
    public string DataClassification { get; set; } = "Internal";
}

public class TemplateItemCodeDto
{
    public int Id { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsTotalRow { get; set; }
}

public class CreateTemplateRequest
{
    public string ReturnCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ReturnFrequency Frequency { get; set; }
    public StructuralCategory StructuralCategory { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    public int? ModuleId { get; set; }
}

public class AddFieldRequest
{
    public string FieldName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string XmlElementName { get; set; } = string.Empty;
    public string? LineCode { get; set; }
    public string? SectionName { get; set; }
    public int SectionOrder { get; set; }
    public int FieldOrder { get; set; }
    public FieldDataType DataType { get; set; }
    public bool IsRequired { get; set; }
    public bool IsComputed { get; set; }
    public bool IsKeyField { get; set; }
    public string? DefaultValue { get; set; }
    public string? MinValue { get; set; }
    public string? MaxValue { get; set; }
    public int? MaxLength { get; set; }
    public string? AllowedValues { get; set; }
    public string? HelpText { get; set; }
    public string? ValidationNote { get; set; }
    public string? RegulatoryReference { get; set; }
    public DataClassification DataClassification { get; set; } = DataClassification.Internal;
}
