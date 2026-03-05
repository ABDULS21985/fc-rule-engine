using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.DataRecord;
using FC.Engine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Export;

public class XmlExportGenerator : IExportGenerator
{
    private readonly ITemplateMetadataCache _templateCache;
    private readonly IGenericDataRepository _dataRepository;
    private readonly IXsdGenerator _xsdGenerator;
    private readonly ILogger<XmlExportGenerator> _logger;

    public XmlExportGenerator(
        ITemplateMetadataCache templateCache,
        IGenericDataRepository dataRepository,
        IXsdGenerator xsdGenerator,
        ILogger<XmlExportGenerator> logger)
    {
        _templateCache = templateCache;
        _dataRepository = dataRepository;
        _xsdGenerator = xsdGenerator;
        _logger = logger;
    }

    public ExportFormat Format => ExportFormat.XML;
    public string ContentType => "application/xml";
    public string FileExtension => "xml";

    public async Task<byte[]> Generate(ExportGenerationContext context, CancellationToken ct = default)
    {
        var submission = context.Submission;
        var template = await _templateCache.GetPublishedTemplate(context.TenantId, submission.ReturnCode, ct);
        var version = template.CurrentVersion;
        var category = Enum.Parse<StructuralCategory>(template.StructuralCategory);
        var fields = version.Fields.OrderBy(x => x.FieldOrder).ToList();
        var record = await _dataRepository.GetBySubmission(template.ReturnCode, submission.Id, ct);

        XNamespace ns = template.XmlNamespace;
        var root = new XElement(ns + template.XmlRootElement);
        root.Add(new XElement(ns + "Header",
            new XElement(ns + "InstitutionCode", submission.Institution?.InstitutionCode ?? string.Empty),
            new XElement(ns + "ReportingDate", ExportUtility.ResolveReportingDate(submission.ReturnPeriod, submission).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new XElement(ns + "ReturnCode", template.ReturnCode),
            new XElement(ns + "SchemaVersion", version.VersionNumber)));

        if (category == StructuralCategory.FixedRow)
        {
            var dataElement = new XElement(ns + "Data");
            var row = record?.Rows.FirstOrDefault() ?? new ReturnDataRow();
            AppendFieldElements(dataElement, row, fields, ns);
            root.Add(dataElement);
        }
        else
        {
            var rowsElement = new XElement(ns + "Rows");
            foreach (var row in record?.Rows ?? Array.Empty<ReturnDataRow>())
            {
                var rowElement = new XElement(ns + "Row");
                AppendFieldElements(rowElement, row, fields, ns);
                rowsElement.Add(rowElement);
            }

            root.Add(rowsElement);
        }

        var xml = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
        await ValidateAgainstSchema(template.ReturnCode, xml, ct);

        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, new XmlWriterSettings
        {
            Indent = true,
            Async = false,
            Encoding = new System.Text.UTF8Encoding(false)
        }))
        {
            xml.Save(writer);
            writer.Flush();
        }

        return ms.ToArray();
    }

    private static void AppendFieldElements(
        XElement parent,
        ReturnDataRow row,
        IReadOnlyList<Domain.Metadata.TemplateField> fields,
        XNamespace ns)
    {
        foreach (var field in fields)
        {
            var value = row.GetValue(field.FieldName);
            if (value is null && !field.IsRequired)
            {
                continue;
            }

            parent.Add(new XElement(ns + field.XmlElementName, FormatXmlValue(value, field.DataType)));
        }
    }

    private static string FormatXmlValue(object? value, FieldDataType dataType)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var normalized = ExportUtility.FormatExcelValue(value, dataType);
        if (normalized is null)
        {
            return string.Empty;
        }

        return normalized switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            decimal dec => dec.ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            _ => normalized.ToString() ?? string.Empty
        };
    }

    private async Task ValidateAgainstSchema(string returnCode, XDocument xml, CancellationToken ct)
    {
        var schemaSet = await _xsdGenerator.GenerateSchema(returnCode, ct);
        var validationErrors = new List<string>();
        var warnings = new List<string>();

        xml.Validate(schemaSet, (_, args) =>
        {
            if (args.Severity == XmlSeverityType.Error)
            {
                validationErrors.Add(args.Message);
            }
            else
            {
                warnings.Add(args.Message);
            }
        }, true);

        if (warnings.Count > 0)
        {
            _logger.LogWarning(
                "XML export validation warnings for {ReturnCode}: {Warnings}",
                returnCode,
                string.Join("; ", warnings));
        }

        if (validationErrors.Count > 0)
        {
            throw new XmlSchemaValidationException(
                $"XML export failed schema validation for {returnCode}: {string.Join("; ", validationErrors)}");
        }
    }
}
