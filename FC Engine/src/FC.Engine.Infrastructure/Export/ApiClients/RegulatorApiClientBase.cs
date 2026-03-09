using System.Net.Http.Headers;
using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Export.ApiClients;

/// <summary>
/// Base class for regulators (NDIC, SEC, NAICOM, PenCom) that share a common REST file-upload pattern.
/// </summary>
public abstract class RegulatorApiClientBase : IRegulatorApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger _logger;

    protected RegulatorApiClientBase(HttpClient httpClient, string apiKey, ILogger logger)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _logger = logger;
    }

    public abstract string RegulatorCode { get; }

    protected virtual string SubmitEndpoint => "/api/v1/submissions";
    protected virtual string StatusEndpoint => "/api/v1/submissions/{0}/status";
    protected virtual string QueriesEndpoint => "/api/v1/submissions/{0}/queries";
    protected virtual string ContentType => "application/octet-stream";
    protected virtual string FileFieldName => "file";
    protected virtual string FileExtension => ".xlsx";

    public async Task<RegulatorApiResponse> SubmitAsync(
        byte[] package, byte[]? signature, RegulatorSubmissionContext context,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Submitting to {Regulator} for submission {SubmissionId}",
            RegulatorCode, context.SubmissionId);

        using var content = new MultipartFormDataContent();
        var fileName = $"{context.ReturnCode}_{context.InstitutionCode}{FileExtension}";
        content.Add(new ByteArrayContent(package), FileFieldName, fileName);

        if (signature is not null)
        {
            content.Add(new ByteArrayContent(signature), "signature", "signature.bin");
        }

        content.Add(new StringContent(context.InstitutionCode), "institutionCode");
        content.Add(new StringContent(context.ReturnCode), "returnCode");
        content.Add(new StringContent(context.PeriodLabel), "period");

        using var request = new HttpRequestMessage(HttpMethod.Post, SubmitEndpoint);
        request.Headers.TryAddWithoutValidation("X-Api-Key", _apiKey);
        request.Content = content;

        var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (response.IsSuccessStatusCode)
        {
            var (reference, message) = ParseReceiptJson(body);
            return new RegulatorApiResponse
            {
                Success = true,
                Reference = reference,
                Message = message,
                HttpStatusCode = (int)response.StatusCode,
                RawResponseBody = body,
                ReceivedAt = DateTime.UtcNow
            };
        }

        _logger.LogWarning("{Regulator} submission failed with status {StatusCode}: {Body}",
            RegulatorCode, (int)response.StatusCode, body.Length > 500 ? body[..500] : body);

        return new RegulatorApiResponse
        {
            Success = false,
            Message = $"{RegulatorCode} returned {response.StatusCode}",
            HttpStatusCode = (int)response.StatusCode,
            RawResponseBody = body
        };
    }

    public async Task<RegulatorStatusResponse> CheckStatusAsync(
        string regulatorReference, CancellationToken ct = default)
    {
        var endpoint = string.Format(StatusEndpoint, Uri.EscapeDataString(regulatorReference));

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.TryAddWithoutValidation("X-Api-Key", _apiKey);

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

        return ParseStatusJson(body);
    }

    public async Task<List<RegulatorQueryInfo>> FetchQueriesAsync(
        string regulatorReference, CancellationToken ct = default)
    {
        var endpoint = string.Format(QueriesEndpoint, Uri.EscapeDataString(regulatorReference));

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.TryAddWithoutValidation("X-Api-Key", _apiKey);

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return new List<RegulatorQueryInfo>();
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        return ParseQueriesJson(body);
    }

    private static (string Reference, string Message) ParseReceiptJson(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var reference = root.TryGetProperty("referenceNumber", out var r) ? r.GetString() ?? "" : "";
            var message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "Accepted" : "Accepted";
            return (reference, message);
        }
        catch
        {
            return (string.Empty, "Accepted (unable to parse response)");
        }
    }

    private static RegulatorStatusResponse ParseStatusJson(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            return new RegulatorStatusResponse
            {
                Status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "UNKNOWN" : "UNKNOWN",
                Message = root.TryGetProperty("message", out var m) ? m.GetString() : null,
                LastUpdatedAt = root.TryGetProperty("updatedAt", out var u) && u.TryGetDateTime(out var dt)
                    ? dt : null
            };
        }
        catch
        {
            return new RegulatorStatusResponse { Status = "UNKNOWN" };
        }
    }

    private static List<RegulatorQueryInfo> ParseQueriesJson(string body)
    {
        var queries = new List<RegulatorQueryInfo>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return queries;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                queries.Add(new RegulatorQueryInfo
                {
                    QueryId = item.TryGetProperty("id", out var id) ? id.ToString() : Guid.NewGuid().ToString(),
                    QueryText = item.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "",
                    Priority = item.TryGetProperty("priority", out var p) ? p.GetString() ?? "Normal" : "Normal",
                    RaisedAt = item.TryGetProperty("raisedAt", out var r) && r.TryGetDateTime(out var dt)
                        ? dt : DateTime.UtcNow
                });
            }
        }
        catch
        {
            // Return empty on parse failure
        }

        return queries;
    }
}
