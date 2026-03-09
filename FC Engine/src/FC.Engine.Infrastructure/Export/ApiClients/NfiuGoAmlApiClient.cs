using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure.Export.ApiClients;

public class NfiuGoAmlApiClient : IRegulatorApiClient
{
    private readonly HttpClient _httpClient;
    private readonly NfiuApiSettings _settings;
    private readonly ILogger<NfiuGoAmlApiClient> _logger;

    public NfiuGoAmlApiClient(
        HttpClient httpClient,
        IOptions<RegulatoryApiSettings> options,
        ILogger<NfiuGoAmlApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value.Nfiu;
        _logger = logger;
    }

    public string RegulatorCode => "NFIU";

    public async Task<RegulatorApiResponse> SubmitAsync(
        byte[] package, byte[]? signature, RegulatorSubmissionContext context,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Submitting goAML XML to NFIU for submission {SubmissionId}", context.SubmissionId);

        await AuthenticateAsync(ct);

        using var xmlContent = new ByteArrayContent(package);
        xmlContent.Headers.ContentType = new MediaTypeHeaderValue("application/xml");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reports/submit");
        request.Headers.TryAddWithoutValidation("X-Api-Key", _settings.ApiKey);
        request.Headers.TryAddWithoutValidation("X-Institution-Code", context.InstitutionCode);

        if (signature is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Digital-Signature", Convert.ToBase64String(signature));
        }

        request.Content = xmlContent;

        var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (response.IsSuccessStatusCode)
        {
            var ack = ParseGoAmlAcknowledgement(body);
            return new RegulatorApiResponse
            {
                Success = true,
                Reference = ack.Reference,
                Message = ack.Message,
                HttpStatusCode = (int)response.StatusCode,
                RawResponseBody = body,
                ReceivedAt = DateTime.UtcNow
            };
        }

        _logger.LogWarning("NFIU goAML submission failed with status {StatusCode}: {Body}",
            (int)response.StatusCode, body);

        return new RegulatorApiResponse
        {
            Success = false,
            Message = $"goAML returned {response.StatusCode}",
            HttpStatusCode = (int)response.StatusCode,
            RawResponseBody = body
        };
    }

    public async Task<RegulatorStatusResponse> CheckStatusAsync(
        string regulatorReference, CancellationToken ct = default)
    {
        await AuthenticateAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/reports/{Uri.EscapeDataString(regulatorReference)}/status");
        request.Headers.TryAddWithoutValidation("X-Api-Key", _settings.ApiKey);

        var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            return new RegulatorStatusResponse
            {
                Status = "UNKNOWN",
                Message = $"Status check failed: {response.StatusCode}"
            };
        }

        return ParseGoAmlStatusResponse(body);
    }

    public async Task<List<RegulatorQueryInfo>> FetchQueriesAsync(
        string regulatorReference, CancellationToken ct = default)
    {
        await AuthenticateAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/reports/{Uri.EscapeDataString(regulatorReference)}/queries");
        request.Headers.TryAddWithoutValidation("X-Api-Key", _settings.ApiKey);

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return new List<RegulatorQueryInfo>();
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        return ParseGoAmlQueries(body);
    }

    private Task AuthenticateAsync(CancellationToken ct)
    {
        // goAML uses API key + username/password combo validated per-request via headers.
        // No separate auth token flow needed; credentials are sent with each request.
        return Task.CompletedTask;
    }

    private static (string Reference, string Message) ParseGoAmlAcknowledgement(string body)
    {
        try
        {
            // goAML acknowledgement may be XML
            var doc = XDocument.Parse(body);
            XNamespace goaml = "http://www.unodc.org/goaml";

            var ack = doc.Root;
            var reference = ack?.Element(goaml + "referenceNumber")?.Value
                ?? ack?.Element("referenceNumber")?.Value
                ?? string.Empty;
            var message = ack?.Element(goaml + "message")?.Value
                ?? ack?.Element("message")?.Value
                ?? "Report accepted by goAML";

            return (reference, message);
        }
        catch
        {
            // Fallback: try JSON
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(body);
                var root = json.RootElement;
                var reference = root.TryGetProperty("referenceNumber", out var r) ? r.GetString() ?? "" : "";
                var message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "Accepted" : "Accepted";
                return (reference, message);
            }
            catch
            {
                return (string.Empty, "Report submitted (unable to parse acknowledgement)");
            }
        }
    }

    private static RegulatorStatusResponse ParseGoAmlStatusResponse(string body)
    {
        try
        {
            var doc = XDocument.Parse(body);
            XNamespace goaml = "http://www.unodc.org/goaml";

            var status = doc.Root?.Element(goaml + "status")?.Value
                ?? doc.Root?.Element("status")?.Value
                ?? "UNKNOWN";
            var message = doc.Root?.Element(goaml + "message")?.Value
                ?? doc.Root?.Element("message")?.Value;

            var result = new RegulatorStatusResponse
            {
                Status = status,
                Message = message,
                LastUpdatedAt = DateTime.UtcNow
            };

            // Parse quality feedback if present
            var qualityEl = doc.Root?.Element(goaml + "qualityFeedback")
                ?? doc.Root?.Element("qualityFeedback");

            if (qualityEl is not null)
            {
                result.QualityFeedback = new GoAmlQualityFeedback
                {
                    OverallQuality = qualityEl.Element(goaml + "overallQuality")?.Value
                        ?? qualityEl.Element("overallQuality")?.Value ?? string.Empty,
                    Issues = (qualityEl.Elements(goaml + "issue") ?? qualityEl.Elements("issue"))
                        .Select(e => e.Value)
                        .ToList(),
                    ReceivedAt = DateTime.UtcNow
                };
            }

            return result;
        }
        catch
        {
            return new RegulatorStatusResponse { Status = "UNKNOWN", Message = "Unable to parse goAML status" };
        }
    }

    private static List<RegulatorQueryInfo> ParseGoAmlQueries(string body)
    {
        var queries = new List<RegulatorQueryInfo>();
        try
        {
            var doc = XDocument.Parse(body);
            XNamespace goaml = "http://www.unodc.org/goaml";

            var queryElements = doc.Root?.Elements(goaml + "query") ?? doc.Root?.Elements("query");
            if (queryElements is null) return queries;

            foreach (var q in queryElements)
            {
                queries.Add(new RegulatorQueryInfo
                {
                    QueryId = q.Element(goaml + "id")?.Value ?? q.Element("id")?.Value ?? Guid.NewGuid().ToString(),
                    QueryText = q.Element(goaml + "text")?.Value ?? q.Element("text")?.Value ?? string.Empty,
                    Priority = q.Element(goaml + "priority")?.Value ?? q.Element("priority")?.Value ?? "Normal",
                    RaisedAt = DateTime.UtcNow
                });
            }
        }
        catch
        {
            // Return empty list on parse failure
        }

        return queries;
    }
}
