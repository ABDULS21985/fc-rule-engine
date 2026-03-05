using System.Xml.Linq;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Infrastructure.Export;

public class XmlExportCoverageValidator
{
    private readonly ITemplateMetadataCache _templateCache;
    private readonly IXsdGenerator _xsdGenerator;

    public XmlExportCoverageValidator(
        ITemplateMetadataCache templateCache,
        IXsdGenerator xsdGenerator)
    {
        _templateCache = templateCache;
        _xsdGenerator = xsdGenerator;
    }

    public async Task<XmlExportCoverageReport> Validate(CancellationToken ct = default)
    {
        var templates = await _templateCache.GetAllPublishedTemplates(ct);
        var moduleGroups = templates
            .GroupBy(t => string.IsNullOrWhiteSpace(t.ModuleCode) ? "UNMAPPED" : t.ModuleCode!)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var moduleReports = new List<XmlModuleCoverageResult>(moduleGroups.Count);
        foreach (var moduleGroup in moduleGroups)
        {
            var templateResults = new List<XmlTemplateCoverageResult>(moduleGroup.Count());
            foreach (var template in moduleGroup.OrderBy(t => t.ReturnCode, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var schemaSet = await _xsdGenerator.GenerateSchema(template.ReturnCode, ct);
                    var sampleXml = BuildSampleXml(template);
                    sampleXml.Validate(schemaSet, (_, _) => { }, true);

                    templateResults.Add(new XmlTemplateCoverageResult
                    {
                        ReturnCode = template.ReturnCode,
                        Success = true
                    });
                }
                catch (Exception ex)
                {
                    templateResults.Add(new XmlTemplateCoverageResult
                    {
                        ReturnCode = template.ReturnCode,
                        Success = false,
                        Error = ex.Message
                    });
                }
            }

            moduleReports.Add(new XmlModuleCoverageResult
            {
                ModuleCode = moduleGroup.Key,
                Templates = templateResults
            });
        }

        return new XmlExportCoverageReport
        {
            Modules = moduleReports
        };
    }

    private static XDocument BuildSampleXml(CachedTemplate template)
    {
        var fields = template.CurrentVersion.Fields.OrderBy(x => x.FieldOrder).ToList();
        var category = Enum.Parse<StructuralCategory>(template.StructuralCategory);
        XNamespace ns = template.XmlNamespace;

        var root = new XElement(ns + template.XmlRootElement,
            new XElement(ns + "Header",
                new XElement(ns + "InstitutionCode", "SAMPLE01"),
                new XElement(ns + "ReportingDate", "2026-01-31"),
                new XElement(ns + "ReturnCode", template.ReturnCode)));

        if (category == StructuralCategory.FixedRow)
        {
            var dataElement = new XElement(ns + "Data");
            AppendSampleFields(dataElement, fields, ns);
            root.Add(dataElement);
        }
        else
        {
            var rowsElement = new XElement(ns + "Rows");
            var rowElement = new XElement(ns + "Row");
            AppendSampleFields(rowElement, fields, ns);
            rowsElement.Add(rowElement);
            root.Add(rowsElement);
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
    }

    private static void AppendSampleFields(
        XElement parent,
        IReadOnlyList<Domain.Metadata.TemplateField> fields,
        XNamespace ns)
    {
        foreach (var field in fields)
        {
            if (!field.IsRequired && !field.IsKeyField)
            {
                continue;
            }

            parent.Add(new XElement(ns + field.XmlElementName, BuildSampleValue(field.DataType, field)));
        }
    }

    private static string BuildSampleValue(FieldDataType dataType, Domain.Metadata.TemplateField field)
    {
        if (field.IsKeyField && field.FieldName.Contains("item", StringComparison.OrdinalIgnoreCase))
        {
            return "ITEM_001";
        }

        return dataType switch
        {
            FieldDataType.Integer => "1",
            FieldDataType.Decimal => "1.2345",
            FieldDataType.Money => "1000.00",
            FieldDataType.Percentage => "0.0500",
            FieldDataType.Date => "2026-01-31",
            FieldDataType.Boolean => "true",
            _ => "sample"
        };
    }
}

public class XmlExportCoverageReport
{
    public List<XmlModuleCoverageResult> Modules { get; set; } = [];
    public int ModuleCount => Modules.Count;
    public int TemplateCount => Modules.Sum(x => x.Templates.Count);
    public int FailedTemplateCount => Modules.Sum(x => x.Templates.Count(t => !t.Success));
    public bool Success => FailedTemplateCount == 0;
}

public class XmlModuleCoverageResult
{
    public string ModuleCode { get; set; } = string.Empty;
    public List<XmlTemplateCoverageResult> Templates { get; set; } = [];
}

public class XmlTemplateCoverageResult
{
    public string ReturnCode { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
}
