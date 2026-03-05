using System.Globalization;
using System.Xml.Linq;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;

namespace FC.Engine.Infrastructure.Export;

public class XbrlExportGenerator : IExportGenerator
{
    private readonly ITemplateMetadataCache _templateCache;
    private readonly IGenericDataRepository _dataRepository;

    public XbrlExportGenerator(
        ITemplateMetadataCache templateCache,
        IGenericDataRepository dataRepository)
    {
        _templateCache = templateCache;
        _dataRepository = dataRepository;
    }

    public ExportFormat Format => ExportFormat.XBRL;
    public string ContentType => "application/xbrl+xml";
    public string FileExtension => "xbrl";

    public async Task<byte[]> Generate(ExportGenerationContext context, CancellationToken ct = default)
    {
        var submission = context.Submission;
        var template = await _templateCache.GetPublishedTemplate(context.TenantId, submission.ReturnCode, ct);
        var record = await _dataRepository.GetBySubmission(template.ReturnCode, submission.Id, ct);
        var fields = template.CurrentVersion.Fields.OrderBy(f => f.FieldOrder).ToList();

        XNamespace xbrli = "http://www.xbrl.org/2003/instance";
        XNamespace iso4217 = "http://www.xbrl.org/2003/iso4217";
        XNamespace reg = template.XmlNamespace;
        XNamespace xlink = "http://www.w3.org/1999/xlink";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

        var root = new XElement(xbrli + "xbrl",
            new XAttribute(XNamespace.Xmlns + "xbrli", xbrli),
            new XAttribute(XNamespace.Xmlns + "iso4217", iso4217),
            new XAttribute(XNamespace.Xmlns + "reg", reg),
            new XAttribute(XNamespace.Xmlns + "xlink", xlink),
            new XAttribute(XNamespace.Xmlns + "xsi", xsi),
            new XElement(xbrli + "unit",
                new XAttribute("id", "U_NGN"),
                new XElement(xbrli + "measure", "iso4217:NGN")),
            new XElement(xbrli + "unit",
                new XAttribute("id", "U_PURE"),
                new XElement(xbrli + "measure", "xbrli:pure")));

        var rows = record?.Rows ?? Array.Empty<Domain.DataRecord.ReturnDataRow>();
        if (rows.Count == 0)
        {
            rows = new[] { new Domain.DataRecord.ReturnDataRow() };
        }

        var reportingDate = ExportUtility.ResolveReportingDate(submission.ReturnPeriod, submission).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var institutionCode = submission.Institution?.InstitutionCode ?? "UNKNOWN";

        var rowNumber = 0;
        foreach (var row in rows)
        {
            rowNumber++;
            var contextId = $"C{rowNumber}";

            var contextElement = new XElement(xbrli + "context",
                new XAttribute("id", contextId),
                new XElement(xbrli + "entity",
                    new XElement(xbrli + "identifier",
                        new XAttribute("scheme", "urn:regos:institution"),
                        institutionCode)),
                new XElement(xbrli + "period",
                    new XElement(xbrli + "instant", reportingDate)));

            if (!string.IsNullOrWhiteSpace(row.RowKey))
            {
                contextElement.Add(new XElement(xbrli + "scenario",
                    new XElement(reg + "RowKey", row.RowKey)));
            }

            root.Add(contextElement);

            foreach (var field in fields)
            {
                var value = row.GetValue(field.FieldName);
                if (value is null)
                {
                    continue;
                }

                var textValue = ExportUtility.FormatPlainValue(value, field.DataType);
                if (string.IsNullOrWhiteSpace(textValue))
                {
                    continue;
                }

                var fact = new XElement(reg + field.XmlElementName,
                    new XAttribute("contextRef", contextId),
                    textValue);

                if (ExportUtility.IsNumeric(field.DataType))
                {
                    fact.Add(new XAttribute("unitRef", field.DataType == FieldDataType.Money ? "U_NGN" : "U_PURE"));
                    fact.Add(new XAttribute("decimals", ResolveDecimals(field.DataType)));
                }

                root.Add(fact);
            }
        }

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    private static string ResolveDecimals(FieldDataType dataType)
    {
        return dataType switch
        {
            FieldDataType.Integer => "0",
            FieldDataType.Money => "2",
            FieldDataType.Decimal => "4",
            FieldDataType.Percentage => "4",
            _ => "INF"
        };
    }
}
