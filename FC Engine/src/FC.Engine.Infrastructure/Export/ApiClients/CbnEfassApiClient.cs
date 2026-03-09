using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure.Export.ApiClients;

public class CbnEfassApiClient : IRegulatorApiClient
{
    private readonly HttpClient _httpClient;
    private readonly CbnApiSettings _settings;
    private readonly ILogger<CbnEfassApiClient> _logger;

    public CbnEfassApiClient(
        HttpClient httpClient,
        IOptions<RegulatoryApiSettings> options,
        ILogger<CbnEfassApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value.Cbn;
        _logger = logger;
    }

    public string RegulatorCode => "CBN";

    public async Task<RegulatorApiResponse> SubmitAsync(
        byte[] package, byte[]? signature, RegulatorSubmissionContext context,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Submitting to CBN eFASS for submission {SubmissionId}", context.SubmissionId);

        var token = await AuthenticateAsync(ct);

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(package), "package", $"{context.ReturnCode}_{context.InstitutionCode}.zip");

        if (signature is not null)
        {
            content.Add(new ByteArrayContent(signature), "signature", "signature.bin");
        }

        content.Add(new StringContent(context.InstitutionCode), "institutionCode");
        content.Add(new StringContent(context.ReturnCode), "returnCode");
        content.Add(new StringContent(context.PeriodLabel), "period");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/submissions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = content;

        var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (response.IsSuccessStatusCode)
        {
            var receipt = ParseReceipt(body);
            return new RegulatorApiResponse
            {
                Success = true,
                Reference = receipt.Reference,
                Message = receipt.Message,
                HttpStatusCode = (int)response.StatusCode,
                RawResponseBody = body,
                ReceivedAt = DateTime.UtcNow
            };
        }

        _logger.LogWarning("CBN eFASS submission failed with status {StatusCode}: {Body}",
            (int)response.StatusCode, body);

        return new RegulatorApiResponse
        {
            Success = false,
            Message = $"eFASS returned {response.StatusCode}: {TruncateBody(body)}",
            HttpStatusCode = (int)response.StatusCode,
            RawResponseBody = body
        };
    }

    public async Task<RegulatorStatusResponse> CheckStatusAsync(
        string regulatorReference, CancellationToken ct = default)
    {
        var token = await AuthenticateAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/submissions/{Uri.EscapeDataString(regulatorReference)}/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

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

        return ParseStatusResponse(body);
    }

    public async Task<List<RegulatorQueryInfo>> FetchQueriesAsync(
        string regulatorReference, CancellationToken ct = default)
    {
        var token = await AuthenticateAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/submissions/{Uri.EscapeDataString(regulatorReference)}/queries");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return new List<RegulatorQueryInfo>();
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        return ParseQueries(body);
    }

    private async Task<string> AuthenticateAsync(CancellationToken ct)
    {
        using var authContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _settings.ClientId,
            ["client_secret"] = _settings.ClientSecret
        });

        var authResponse = await _httpClient.PostAsync("/api/v1/auth/token", authContent, ct);
        var authBody = await authResponse.Content.ReadAsStringAsync(ct);

        if (!authResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"CBN eFASS authentication failed: {authBody}");
        }

        using var doc = JsonDocument.Parse(authBody);
        return doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("CBN eFASS auth response missing access_token.");
    }

    private static (string Reference, string Message) ParseReceipt(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var reference = root.TryGetProperty("referenceNumber", out var refEl)
                ? refEl.GetString() ?? string.Empty
                : string.Empty;
            var message = root.TryGetProperty("message", out var msgEl)
                ? msgEl.GetString() ?? "Submission accepted"
                : "Submission accepted";
            return (reference, message);
        }
        catch
        {
            return (string.Empty, "Submission accepted (unable to parse response)");
        }
    }

    private static RegulatorStatusResponse ParseStatusResponse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            return new RegulatorStatusResponse
            {
                Status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "UNKNOWN" : "UNKNOWN",
                Message = root.TryGetProperty("message", out var m) ? m.GetString() : null,
                LastUpdatedAt = root.TryGetProperty("updatedAt", out var u)
                    ? u.TryGetDateTime(out var dt) ? dt : null
                    : null
            };
        }
        catch
        {
            return new RegulatorStatusResponse { Status = "UNKNOWN", Message = "Unable to parse status response" };
        }
    }

    private static List<RegulatorQueryInfo> ParseQueries(string body)
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
                    QueryText = item.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty,
                    Priority = item.TryGetProperty("priority", out var p) ? p.GetString() ?? "Normal" : "Normal",
                    RaisedAt = item.TryGetProperty("raisedAt", out var r) && r.TryGetDateTime(out var dt)
                        ? dt : DateTime.UtcNow
                });
            }
        }
        catch
        {
            // Return empty list on parse failure
        }

        return queries;
    }

    private static string TruncateBody(string body)
    {
        return body.Length > 500 ? body[..500] + "..." : body;
    }
}
