using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using FC.Engine.Domain.Models;
using FC.Engine.Domain.Models.BatchSubmission;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure.Export.ChannelAdapters;

/// <summary>
/// NFIU goAML v4.0 XML submission adapter.
/// Handles STR (Suspicious Transaction Reports), CTR (Currency Transaction Reports),
/// and FTR (Foreign Transaction Reports) per UNODC goAML XML schema v4.0.
/// Transport: XML_UPLOAD over HTTPS, API-key authenticated.
/// R-10: All HTTP calls wrapped in Polly pipeline from base class.
/// </summary>
public sealed class NfiuGoAmlChannelAdapter : RegulatoryChannelAdapterBase
{
    private static readonly Lazy<XmlSchemaSet> GoAmlSchema = new(BuildGoAmlSchemaSet);
    private const string GoAmlNs = "http://www.unodc.org/goAML/v4.0";

    private readonly NfiuApiSettings _settings;

    public NfiuGoAmlChannelAdapter(
        HttpClient http,
        IOptions<RegulatoryApiSettings> options,
        ILogger<NfiuGoAmlChannelAdapter> logger)
        : base(http, logger)
    {
        _settings = options.Value.Nfiu;
    }

    public override string RegulatorCode => "NFIU";
    public override string IntegrationMethod => "XML_UPLOAD";

    protected override async Task<BatchRegulatorReceipt> DispatchCoreAsync(
        DispatchPayload payload, CancellationToken ct)
    {
        // Build & validate goAML v4.0 XML envelope
        var goAmlXml = BuildGoAmlEnvelope(payload);
        ValidateGoAmlXml(goAmlXml);

        var xmlBytes = Encoding.UTF8.GetBytes(goAmlXml);
        using var xmlContent = new ByteArrayContent(xmlBytes);
        xmlContent.Headers.ContentType = new MediaTypeHeaderValue("application/xml");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v4/reports/submit");
        request.Headers.TryAddWithoutValidation("X-Api-Key", _settings.ApiKey);
        request.Headers.TryAddWithoutValidation("X-goAML-Entity", payload.InstitutionCode);
        request.Headers.TryAddWithoutValidation("X-goAML-Batch", payload.BatchReference);
        request.Headers.TryAddWithoutValidation("X-Correlation-Id",
            payload.Metadata.GetValueOrDefault("correlation_id", Guid.NewGuid().ToString()));
        request.Content = xmlContent;

        if (payload.Signature.SignatureValue.Length > 0)
        {
            request.Headers.TryAddWithoutValidation("X-Digital-Signature",
                Convert.ToBase64String(payload.Signature.SignatureValue));
            request.Headers.TryAddWithoutValidation("X-Certificate-Thumbprint",
                payload.Signature.CertificateThumbprint);
        }

        var response = await Http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new SubmissionDispatchException(
                $"NFIU goAML rejected submission: HTTP {(int)response.StatusCode} — {TruncateBody(body)}",
                (int)response.StatusCode);
        }

        var ack = ParseGoAmlAcknowledgement(body);
        Logger.LogInformation(
            "NFIU goAML accepted batch {BatchRef}. ReportId: {ReportId}",
            payload.BatchReference, ack.ReportId);

        return new BatchRegulatorReceipt(ack.ReportId, ack.Timestamp, (int)response.StatusCode, body);
    }

    protected override async Task<BatchRegulatorStatusResponse> CheckStatusCoreAsync(
        string receiptReference, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v4/reports/{Uri.EscapeDataString(receiptReference)}/status");
        request.Headers.TryAddWithoutValidation("X-Api-Key", _settings.ApiKey);

        var response = await Http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return new BatchRegulatorStatusResponse(receiptReference, "UNKNOWN",
                BatchSubmissionStatusValue.Submitted, null, DateTimeOffset.UtcNow);

        return ParseGoAmlStatus(body, receiptReference);
    }

    protected override async Task<IReadOnlyList<BatchRegulatorQueryDto>> FetchQueriesCoreAsync(
        string receiptReference, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v4/reports/{Uri.EscapeDataString(receiptReference)}/queries");
        request.Headers.TryAddWithoutValidation("X-Api-Key", _settings.ApiKey);

        var response = await Http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return [];
        var body = await response.Content.ReadAsStringAsync(ct);
        return ParseGoAmlQueries(body);
    }

    protected override async Task<BatchRegulatorReceipt> SubmitQueryResponseCoreAsync(
        QueryResponsePayload payload, CancellationToken ct)
    {
        var xmlBody = BuildQueryResponseXml(payload);
        using var content = new StringContent(xmlBody, Encoding.UTF8, "application/xml");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v4/queries/{Uri.EscapeDataString(payload.QueryReference)}/respond");
        request.Headers.TryAddWithoutValidation("X-Api-Key", _settings.ApiKey);
        request.Content = content;

        var response = await Http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new SubmissionDispatchException(
                $"NFIU query response failed: HTTP {(int)response.StatusCode}", (int)response.StatusCode);

        var ack = ParseGoAmlAcknowledgement(body);
        return new BatchRegulatorReceipt(ack.ReportId, ack.Timestamp, (int)response.StatusCode, body);
    }

    // ── goAML XML builders ─────────────────────────────────────────────

    private static string BuildGoAmlEnvelope(DispatchPayload payload)
    {
        var reportType = InferReportType(payload);
        XNamespace ns = GoAmlNs;

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(ns + "goAMLReport",
                new XAttribute("version", "4.0"),
                new XAttribute("reportType", reportType),
                new XElement(ns + "reportingEntity",
                    new XElement(ns + "entityId", payload.InstitutionCode),
                    new XElement(ns + "entityType", "FI")),
                new XElement(ns + "submissionInfo",
                    new XElement(ns + "batchReference", payload.BatchReference),
                    new XElement(ns + "submissionDate",
                        DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")),
                    new XElement(ns + "reportCount",
                        payload.Metadata.GetValueOrDefault("item_count", "1")),
                    new XElement(ns + "reportingPeriod",
                        payload.Metadata.GetValueOrDefault("reporting_period", string.Empty))),
                new XElement(ns + "digitalSignature",
                    new XElement(ns + "algorithm", payload.Signature.SignatureAlgorithm),
                    new XElement(ns + "certificateThumbprint", payload.Signature.CertificateThumbprint),
                    new XElement(ns + "signatureValue",
                        Convert.ToBase64String(payload.Signature.SignatureValue)),
                    new XElement(ns + "dataHash", payload.Signature.SignedDataHash)),
                new XElement(ns + "reportData",
                    new XElement(ns + "encodedContent",
                        Convert.ToBase64String(payload.ExportedFileContent)))));

        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, new XmlWriterSettings
        {
            Indent = false,
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false
        }))
        {
            doc.Save(writer);
        }
        return sb.ToString();
    }

    private static string BuildQueryResponseXml(QueryResponsePayload payload)
    {
        XNamespace ns = GoAmlNs;
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(ns + "queryResponse",
                new XElement(ns + "queryReference", payload.QueryReference),
                new XElement(ns + "responseText", payload.ResponseText),
                new XElement(ns + "responseDate",
                    DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")),
                new XElement(ns + "attachments",
                    payload.Attachments.Select(a =>
                        new XElement(ns + "attachment",
                            new XElement(ns + "fileName", a.FileName),
                            new XElement(ns + "contentType", a.ContentType),
                            new XElement(ns + "content", Convert.ToBase64String(a.Content)),
                            new XElement(ns + "hash", a.FileHash))))));

        return doc.ToString(SaveOptions.DisableFormatting);
    }

    private static void ValidateGoAmlXml(string xml)
    {
        var doc = XDocument.Parse(xml);
        var errors = new List<string>();
        doc.Validate(GoAmlSchema.Value, (_, e) => errors.Add(e.Message));
        if (errors.Count > 0)
            throw new XmlSchemaValidationException(
                $"goAML XML schema validation failed: {string.Join("; ", errors)}");
    }

    private static (string ReportId, DateTimeOffset Timestamp) ParseGoAmlAcknowledgement(string body)
    {
        // Try XML first (goAML native), then JSON fallback
        try
        {
            var doc = XDocument.Parse(body);
            XNamespace ns = GoAmlNs;
            var reportId = doc.Root?.Element(ns + "reportId")?.Value
                ?? doc.Root?.Element("reportId")?.Value
                ?? string.Empty;
            return (reportId, DateTimeOffset.UtcNow);
        }
        catch
        {
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(body);
                var root = json.RootElement;
                var reference = root.TryGetProperty("reportId", out var r) ? r.GetString() ?? string.Empty
                    : root.TryGetProperty("referenceNumber", out var r2) ? r2.GetString() ?? string.Empty
                    : string.Empty;
                return (reference, DateTimeOffset.UtcNow);
            }
            catch
            {
                return (string.Empty, DateTimeOffset.UtcNow);
            }
        }
    }

    private static BatchRegulatorStatusResponse ParseGoAmlStatus(string body, string receiptReference)
    {
        try
        {
            var doc = XDocument.Parse(body);
            XNamespace ns = GoAmlNs;
            var status = doc.Root?.Element(ns + "status")?.Value
                ?? doc.Root?.Element("status")?.Value
                ?? "UNKNOWN";
            var qualityScore = doc.Root?.Element(ns + "qualityScore")?.Value
                ?? doc.Root?.Element("qualityScore")?.Value;
            var message = qualityScore is not null ? $"Quality score: {qualityScore}" : null;
            return new BatchRegulatorStatusResponse(
                receiptReference, status, MapStatus(status), message, DateTimeOffset.UtcNow);
        }
        catch
        {
            return ParseJsonStatus(body, receiptReference);
        }
    }

    private static IReadOnlyList<BatchRegulatorQueryDto> ParseGoAmlQueries(string body)
    {
        var result = new List<BatchRegulatorQueryDto>();
        try
        {
            var doc = XDocument.Parse(body);
            XNamespace ns = GoAmlNs;
            foreach (var q in doc.Descendants(ns + "query"))
            {
                result.Add(new BatchRegulatorQueryDto(
                    QueryReference: q.Element(ns + "queryId")?.Value ?? Guid.NewGuid().ToString(),
                    Type: q.Element(ns + "type")?.Value ?? "CLARIFICATION",
                    QueryText: q.Element(ns + "description")?.Value ?? string.Empty,
                    DueDate: DateOnly.TryParse(q.Element(ns + "dueDate")?.Value, out var d) ? d : null,
                    Priority: q.Element(ns + "priority")?.Value ?? "NORMAL"));
            }
        }
        catch
        {
            return ParseJsonQueries(body);
        }
        return result;
    }

    private static string InferReportType(DispatchPayload payload)
    {
        if (payload.Metadata.TryGetValue("report_type", out var rt))
            return rt.ToUpperInvariant();

        var name = payload.ExportedFileName.ToUpperInvariant();
        if (name.Contains("CTR")) return "CTR";
        if (name.Contains("FTR")) return "FTR";
        if (name.Contains("SAR")) return "SAR";
        return "STR";
    }

    private static XmlSchemaSet BuildGoAmlSchemaSet()
    {
        const string xsd = """
<?xml version="1.0" encoding="utf-8"?>
<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
           targetNamespace="http://www.unodc.org/goAML/v4.0"
           xmlns:goAML="http://www.unodc.org/goAML/v4.0"
           elementFormDefault="qualified">

  <xs:element name="goAMLReport" type="goAML:ReportType" />

  <xs:complexType name="ReportType">
    <xs:sequence>
      <xs:element name="reportingEntity" type="goAML:ReportingEntityType" />
      <xs:element name="submissionInfo"  type="goAML:SubmissionInfoType" />
      <xs:element name="digitalSignature" type="goAML:DigitalSignatureType" minOccurs="0" />
      <xs:element name="reportData"      type="goAML:ReportDataType" />
    </xs:sequence>
    <xs:attribute name="version"    type="xs:string" use="required" />
    <xs:attribute name="reportType" type="xs:string" use="required" />
  </xs:complexType>

  <xs:complexType name="ReportingEntityType">
    <xs:sequence>
      <xs:element name="entityId"   type="xs:string" />
      <xs:element name="entityType" type="xs:string" />
    </xs:sequence>
  </xs:complexType>

  <xs:complexType name="SubmissionInfoType">
    <xs:sequence>
      <xs:element name="batchReference"  type="xs:string" />
      <xs:element name="submissionDate"  type="xs:string" />
      <xs:element name="reportCount"     type="xs:string" />
      <xs:element name="reportingPeriod" type="xs:string" />
    </xs:sequence>
  </xs:complexType>

  <xs:complexType name="DigitalSignatureType">
    <xs:sequence>
      <xs:element name="algorithm"             type="xs:string" />
      <xs:element name="certificateThumbprint" type="xs:string" />
      <xs:element name="signatureValue"        type="xs:string" />
      <xs:element name="dataHash"              type="xs:string" />
    </xs:sequence>
  </xs:complexType>

  <xs:complexType name="ReportDataType">
    <xs:sequence>
      <xs:element name="encodedContent" type="xs:string" />
    </xs:sequence>
  </xs:complexType>

</xs:schema>
""";
        var set = new XmlSchemaSet();
        using var reader = XmlReader.Create(new StringReader(xsd));
        set.Add(GoAmlNs, reader);
        set.Compile();
        return set;
    }
}
