using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Metadata;

public class TemplateField
{
    public int Id { get; set; }
    public int TemplateVersionId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string XmlElementName { get; set; } = string.Empty;
    public string? LineCode { get; set; }
    public string? SectionName { get; set; }
    public int SectionOrder { get; set; }
    public int FieldOrder { get; set; }
    public FieldDataType DataType { get; set; }
    public string SqlType { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public bool IsComputed { get; set; }
    public bool IsKeyField { get; set; }
    public string? DefaultValue { get; set; }
    public string? MinValue { get; set; }
    public string? MaxValue { get; set; }
    public int? MaxLength { get; set; }
    public string? AllowedValues { get; set; }
    public string? ReferenceTable { get; set; }
    public string? ReferenceColumn { get; set; }
    public string? HelpText { get; set; }
    /// <summary>Operator-facing validation rule description from the module definition (e.g. "Must be ≥ 0"). Stored separately from HelpText.</summary>
    public string? ValidationNote { get; set; }
    public string? RegulatoryReference { get; set; }
    public bool IsYtdField { get; set; }
    public int? YtdSourceFieldId { get; set; }
    public DataClassification DataClassification { get; set; } = DataClassification.Internal;
    public DateTime CreatedAt { get; set; }

    public TemplateField Clone()
    {
        return new TemplateField
        {
            FieldName = FieldName,
            DisplayName = DisplayName,
            XmlElementName = XmlElementName,
            LineCode = LineCode,
            SectionName = SectionName,
            SectionOrder = SectionOrder,
            FieldOrder = FieldOrder,
            DataType = DataType,
            SqlType = SqlType,
            IsRequired = IsRequired,
            IsComputed = IsComputed,
            IsKeyField = IsKeyField,
            DefaultValue = DefaultValue,
            MinValue = MinValue,
            MaxValue = MaxValue,
            MaxLength = MaxLength,
            AllowedValues = AllowedValues,
            ReferenceTable = ReferenceTable,
            ReferenceColumn = ReferenceColumn,
            HelpText = HelpText,
            ValidationNote = ValidationNote,
            RegulatoryReference = RegulatoryReference,
            IsYtdField = IsYtdField,
            DataClassification = DataClassification,
            CreatedAt = DateTime.UtcNow
        };
    }
}
