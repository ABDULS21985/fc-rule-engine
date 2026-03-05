using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Infrastructure.Export.Adapters;

public class NfiuSubmissionAdapter : IRegulatorSubmissionAdapter
{
    private static readonly Lazy<XmlSchemaSet> GoAmlSchemaSet = new(BuildGoAmlSchemaSet);

    private readonly ITemplateMetadataCache _templateCache;
    private readonly IGenericDataRepository _dataRepository;

    public NfiuSubmissionAdapter(
        ITemplateMetadataCache templateCache,
        IGenericDataRepository dataRepository)
    {
        _templateCache = templateCache;
        _dataRepository = dataRepository;
    }

    public string RegulatorCode => "NFIU";

    public async Task<byte[]> Package(Submission submission, ExportFormat preferredFormat, CancellationToken ct = default)
    {
        if (preferredFormat != ExportFormat.XML)
        {
            throw new InvalidOperationException("NFIU goAML packaging only supports XML format.");
        }

        var template = await _templateCache.GetPublishedTemplate(submission.TenantId, submission.ReturnCode, ct);
        var record = await _dataRepository.GetBySubmission(template.ReturnCode, submission.Id, ct);
        var fields = template.CurrentVersion.Fields.OrderBy(x => x.FieldOrder).ToList();

        XNamespace goaml = "http://www.unodc.org/goaml";
        var reportCode = ResolveReportCode(submission.ReturnCode);
        var report = new XElement(goaml + "report",
            new XAttribute("version", "4.0"),
            new XAttribute(XNamespace.Xmlns + "goAML", goaml),
            new XElement(goaml + "reportHeader",
                new XElement(goaml + "reportCode", reportCode),
                new XElement(goaml + "submissionDate", DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                new XElement(goaml + "reportingEntity",
                    new XElement(goaml + "institutionName", submission.Institution?.InstitutionName ?? string.Empty),
                    new XElement(goaml + "institutionCode", submission.Institution?.InstitutionCode ?? string.Empty))),
            new XElement(goaml + "reportBody"));

        var body = report.Element(goaml + "reportBody")!;
        var rows = record?.Rows ?? Array.Empty<Domain.DataRecord.ReturnDataRow>();
        var index = 0;
        foreach (var row in rows)
        {
            index++;
            var transaction = new XElement(goaml + "transaction",
                new XElement(goaml + "transactionReference", row.RowKey ?? $"TRX-{index}"),
                new XElement(goaml + "returnCode", submission.ReturnCode));

            foreach (var field in fields)
            {
                var value = row.GetValue(field.FieldName);
                var formatted = ExportUtility.FormatPlainValue(value, field.DataType);
                if (string.IsNullOrWhiteSpace(formatted))
                {
                    continue;
                }

                transaction.Add(new XElement(goaml + ToGoAmlElementName(field.XmlElementName), formatted));
            }

            body.Add(transaction);
        }

        var xml = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), report);
        ValidateGoAml(xml, reportCode);

        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, new XmlWriterSettings
        {
            Indent = true,
            Async = false,
            Encoding = new UTF8Encoding(false)
        }))
        {
            xml.Save(writer);
            writer.Flush();
        }

        return ms.ToArray();
    }

    public Task<SubmissionReceipt> Submit(byte[] package, Submission submission, CancellationToken ct = default)
    {
        return Task.FromResult(new SubmissionReceipt
        {
            Success = true,
            Reference = $"NFIU-{submission.Id}-{DateTime.UtcNow:yyyyMMddHHmmss}",
            Message = "goAML package generated for portal upload.",
            ReceivedAt = DateTime.UtcNow
        });
    }

    private static string ResolveReportCode(string? returnCode)
    {
        if (string.IsNullOrWhiteSpace(returnCode))
        {
            return "STR";
        }

        if (returnCode.Contains("CTR", StringComparison.OrdinalIgnoreCase))
        {
            return "CTR";
        }

        if (returnCode.Contains("SAR", StringComparison.OrdinalIgnoreCase))
        {
            return "SAR";
        }

        if (returnCode.Contains("PEP", StringComparison.OrdinalIgnoreCase))
        {
            return "PEP";
        }

        return "STR";
    }

    private static string ToGoAmlElementName(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "field";
        }

        var clean = new string(source
            .Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
            .ToArray());

        if (string.IsNullOrWhiteSpace(clean))
        {
            return "field";
        }

        if (!char.IsLetter(clean[0]) && clean[0] != '_')
        {
            clean = "_" + clean;
        }

        return clean;
    }

    private static void ValidateGoAml(XDocument xml, string expectedReportCode)
    {
        XNamespace goaml = "http://www.unodc.org/goaml";

        var root = xml.Root ?? throw new InvalidOperationException("goAML XML has no root element.");
        if (root.Name != goaml + "report")
        {
            throw new InvalidOperationException("goAML XML root element must be goAML:report.");
        }

        var header = root.Element(goaml + "reportHeader")
                     ?? throw new InvalidOperationException("goAML XML is missing reportHeader.");
        var body = root.Element(goaml + "reportBody")
                   ?? throw new InvalidOperationException("goAML XML is missing reportBody.");

        var reportCode = header.Element(goaml + "reportCode")?.Value?.Trim();
        if (!string.Equals(reportCode, expectedReportCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"goAML reportCode mismatch. Expected '{expectedReportCode}', got '{reportCode}'.");
        }

        var institutionName = header
            .Element(goaml + "reportingEntity")
            ?.Element(goaml + "institutionName")
            ?.Value
            ?.Trim();

        if (string.IsNullOrWhiteSpace(institutionName))
        {
            throw new InvalidOperationException("goAML reportingEntity.institutionName is required.");
        }

        var transactions = body.Elements(goaml + "transaction").ToList();
        if (transactions.Count == 0)
        {
            throw new InvalidOperationException("goAML reportBody must contain at least one transaction.");
        }

        var schemaErrors = new List<string>();
        xml.Validate(GoAmlSchemaSet.Value, (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Message))
            {
                schemaErrors.Add(args.Message);
            }
        }, true);

        if (schemaErrors.Count > 0)
        {
            throw new XmlSchemaValidationException("Generated goAML XML failed schema validation: " + string.Join("; ", schemaErrors));
        }
    }

    private static XmlSchemaSet BuildGoAmlSchemaSet()
    {
        const string schema = """
<?xml version="1.0" encoding="utf-8"?>
<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
           targetNamespace="http://www.unodc.org/goaml"
           xmlns:goAML="http://www.unodc.org/goaml"
           elementFormDefault="qualified">
  <xs:element name="report" type="goAML:ReportType" />

  <xs:complexType name="ReportType">
    <xs:sequence>
      <xs:element name="reportHeader" type="goAML:ReportHeaderType" />
      <xs:element name="reportBody" type="goAML:ReportBodyType" />
    </xs:sequence>
    <xs:attribute name="version" type="xs:string" use="required" />
  </xs:complexType>

  <xs:complexType name="ReportHeaderType">
    <xs:sequence>
      <xs:element name="reportCode" type="xs:string" />
      <xs:element name="submissionDate" type="xs:date" />
      <xs:element name="reportingEntity" type="goAML:ReportingEntityType" />
    </xs:sequence>
  </xs:complexType>

  <xs:complexType name="ReportingEntityType">
    <xs:sequence>
      <xs:element name="institutionName" type="xs:string" />
      <xs:element name="institutionCode" type="xs:string" />
    </xs:sequence>
  </xs:complexType>

  <xs:complexType name="ReportBodyType">
    <xs:sequence>
      <xs:element name="transaction" type="goAML:TransactionType" minOccurs="1" maxOccurs="unbounded" />
    </xs:sequence>
  </xs:complexType>

  <xs:complexType name="TransactionType">
    <xs:sequence>
      <xs:element name="transactionReference" type="xs:string" />
      <xs:element name="returnCode" type="xs:string" />
      <xs:any minOccurs="0" maxOccurs="unbounded" processContents="lax" />
    </xs:sequence>
  </xs:complexType>
</xs:schema>
""";

        var set = new XmlSchemaSet();
        using var reader = XmlReader.Create(new StringReader(schema));
        set.Add("http://www.unodc.org/goaml", reader);
        set.Compile();
        return set;
    }
}
