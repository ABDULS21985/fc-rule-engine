namespace FC.Engine.Domain.Metadata;

public class TemplateSection
{
    public int Id { get; set; }
    public int TemplateVersionId { get; set; }
    /// <summary>Machine-readable identifier (e.g. "ASSETS_AND_LIABILITIES"). Referenced by TemplateField.SectionName for grouping.</summary>
    public string SectionCode { get; set; } = string.Empty;
    public string SectionName { get; set; } = string.Empty;
    public int SectionOrder { get; set; }
    public string? Description { get; set; }
    public bool IsRepeating { get; set; }
}
