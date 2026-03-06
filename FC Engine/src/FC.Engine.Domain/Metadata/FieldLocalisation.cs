namespace FC.Engine.Domain.Metadata;

public class FieldLocalisation
{
    public int Id { get; set; }
    public int FieldId { get; set; }
    public string LanguageCode { get; set; } = "en";
    public string LocalisedLabel { get; set; } = string.Empty;
    public string? LocalisedHelpText { get; set; }

    public TemplateField? Field { get; set; }
}
