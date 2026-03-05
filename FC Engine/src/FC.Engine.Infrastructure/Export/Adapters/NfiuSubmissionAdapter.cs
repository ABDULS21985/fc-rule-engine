using System.Globalization;
using System.Xml.Linq;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Infrastructure.Export.Adapters;

public class NfiuSubmissionAdapter : IRegulatorSubmissionAdapter
{
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
        var template = await _templateCache.GetPublishedTemplate(submission.TenantId, submission.ReturnCode, ct);
        var record = await _dataRepository.GetBySubmission(template.ReturnCode, submission.Id, ct);
        var fields = template.CurrentVersion.Fields.OrderBy(x => x.FieldOrder).ToList();

        XNamespace goaml = "http://www.unodc.org/goaml";
        var report = new XElement(goaml + "report",
            new XAttribute("version", "4.0"),
            new XAttribute(XNamespace.Xmlns + "goAML", goaml),
            new XElement(goaml + "reportHeader",
                new XElement(goaml + "reportCode", "STR"),
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
        using var ms = new MemoryStream();
        xml.Save(ms);
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
}
