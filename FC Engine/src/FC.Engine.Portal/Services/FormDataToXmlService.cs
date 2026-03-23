using System.Text;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Metadata;

namespace FC.Engine.Portal.Services;

/// <summary>
/// Converts structured form data (field name → value dictionaries)
/// into valid XML matching the GenericXmlParser's expected format.
/// </summary>
public class FormDataToXmlService
{
    private readonly ITemplateMetadataCache _templateCache;

    public FormDataToXmlService(ITemplateMetadataCache templateCache)
    {
        _templateCache = templateCache;
    }

    /// <summary>
    /// Convert form data to XML string matching the IngestionOrchestrator's expected format.
    /// </summary>
    /// <param name="returnCode">The template return code.</param>
    /// <param name="institutionCode">The institution code for the Header.</param>
    /// <param name="reportingDate">The reporting date for the Header (yyyy-MM-dd).</param>
    /// <param name="rows">List of rows, each being a dictionary of FieldName → user input value.</param>
    /// <param name="rowKeys">For ItemCoded: list of item codes. Null for FixedRow/MultiRow.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Valid XML string matching the GenericXmlParser expected format.</returns>
    public async Task<string> ConvertToXml(
        string returnCode,
        string institutionCode,
        string reportingDate,
        List<Dictionary<string, string>> rows,
        List<string>? rowKeys = null,
        CancellationToken ct = default)
    {
        var template = await _templateCache.GetPublishedTemplate(returnCode, ct);
        var version = template.CurrentVersion;
        var fields = version.Fields;
        var ns = template.XmlNamespace;
        var rootElement = template.XmlRootElement;
        var category = template.StructuralCategory;

        // Build field name → XmlElementName mapping
        var fieldXmlMap = fields.ToDictionary(f => f.FieldName, f => f.XmlElementName, StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<{rootElement} xmlns=\"{Escape(ns)}\">");

        // Header section
        sb.AppendLine("  <Header>");
        sb.AppendLine($"    <InstitutionCode>{Escape(institutionCode)}</InstitutionCode>");
        sb.AppendLine($"    <ReportingDate>{Escape(reportingDate)}</ReportingDate>");
        sb.AppendLine($"    <ReturnCode>{Escape(returnCode)}</ReturnCode>");
        sb.AppendLine("  </Header>");

        if (category.Equals("FixedRow", StringComparison.OrdinalIgnoreCase))
        {
            // FixedRow: single <Data> section with fields directly
            sb.AppendLine("  <Data>");
            if (rows.Count > 0)
            {
                WriteRowFields(sb, rows[0], fields, fieldXmlMap, "    ");
            }
            sb.AppendLine("  </Data>");
        }
        else
        {
            // MultiRow and ItemCoded: <Rows> with <Row> elements
            sb.AppendLine("  <Rows>");
            for (int i = 0; i < rows.Count; i++)
            {
                sb.AppendLine("    <Row>");
                // For ItemCoded templates, include the item code key for each row
                if (rowKeys is not null && i < rowKeys.Count && !string.IsNullOrEmpty(rowKeys[i]))
                {
                    sb.AppendLine($"      <ItemCode>{Escape(rowKeys[i])}</ItemCode>");
                }
                WriteRowFields(sb, rows[i], fields, fieldXmlMap, "      ");
                sb.AppendLine("    </Row>");
            }
            sb.AppendLine("  </Rows>");
        }

        sb.AppendLine($"</{rootElement}>");
        return sb.ToString();
    }

    /// <summary>
    /// Convert form data to a MemoryStream for orchestrator consumption.
    /// </summary>
    public async Task<MemoryStream> ConvertToStream(
        string returnCode,
        string institutionCode,
        string reportingDate,
        List<Dictionary<string, string>> rows,
        List<string>? rowKeys = null,
        CancellationToken ct = default)
    {
        var xml = await ConvertToXml(returnCode, institutionCode, reportingDate, rows, rowKeys, ct);
        return new MemoryStream(Encoding.UTF8.GetBytes(xml));
    }

    private static void WriteRowFields(
        StringBuilder sb,
        Dictionary<string, string> row,
        IReadOnlyList<TemplateField> fields,
        Dictionary<string, string> fieldXmlMap,
        string indent)
    {
        foreach (var field in fields)
        {
            if (!row.TryGetValue(field.FieldName, out var value)) continue;
            if (!fieldXmlMap.TryGetValue(field.FieldName, out var xmlElem)) continue;

            if (!string.IsNullOrEmpty(value))
            {
                sb.AppendLine($"{indent}<{xmlElem}>{Escape(value)}</{xmlElem}>");
            }
        }
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
