using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Webhooks;

public class WebhookDeliveryService : IWebhookService
{
    private readonly MetadataDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookDeliveryService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public WebhookDeliveryService(
        MetadataDbContext db,
        IHttpClientFactory httpClientFactory,
        ILogger<WebhookDeliveryService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<WebhookEndpoint> CreateEndpointAsync(
        Guid tenantId, string url, string? description,
        List<string> eventTypes, int createdBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL is required.", nameof(url));

        var endpoint = new WebhookEndpoint
        {
            TenantId = tenantId,
            Url = url.Trim(),
            Description = description?.Trim() ?? string.Empty,
            SecretKey = GenerateSecret(),
            EventTypes = JsonSerializer.Serialize(eventTypes ?? new List<string>(), JsonOptions),
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };

        _db.WebhookEndpoints.Add(endpoint);
        await _db.SaveChangesAsync(ct);
        return endpoint;
    }

    public async Task<WebhookEndpoint?> GetEndpointAsync(int id, CancellationToken ct = default)
    {
        return await _db.WebhookEndpoints.FindAsync(new object[] { id }, ct);
    }

    public async Task<List<WebhookEndpoint>> GetEndpointsAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _db.WebhookEndpoints
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task UpdateEndpointAsync(
        int id, string? url, string? description,
        List<string>? eventTypes, bool? isActive, CancellationToken ct = default)
    {
        var endpoint = await _db.WebhookEndpoints.FindAsync(new object[] { id }, ct)
            ?? throw new InvalidOperationException($"Webhook endpoint {id} not found.");

        if (url is not null) endpoint.Url = url.Trim();
        if (description is not null) endpoint.Description = description.Trim();
        if (eventTypes is not null) endpoint.EventTypes = JsonSerializer.Serialize(eventTypes, JsonOptions);
        if (isActive.HasValue)
        {
            endpoint.IsActive = isActive.Value;
            if (isActive.Value)
            {
                endpoint.FailureCount = 0;
                endpoint.DisabledReason = null;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteEndpointAsync(int id, CancellationToken ct = default)
    {
        var endpoint = await _db.WebhookEndpoints.FindAsync(new object[] { id }, ct);
        if (endpoint is not null)
        {
            _db.WebhookEndpoints.Remove(endpoint);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<string> RotateSecretAsync(int id, CancellationToken ct = default)
    {
        var endpoint = await _db.WebhookEndpoints.FindAsync(new object[] { id }, ct)
            ?? throw new InvalidOperationException($"Webhook endpoint {id} not found.");

        endpoint.SecretKey = GenerateSecret();
        await _db.SaveChangesAsync(ct);
        return endpoint.SecretKey;
    }

    public async Task<List<WebhookDelivery>> GetDeliveryLogAsync(
        int endpointId, int take = 50, CancellationToken ct = default)
    {
        return await _db.WebhookDeliveries
            .Where(d => d.EndpointId == endpointId)
            .OrderByDescending(d => d.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task SendTestWebhookAsync(int endpointId, CancellationToken ct = default)
    {
        var endpoint = await _db.WebhookEndpoints.FindAsync(new object[] { endpointId }, ct)
            ?? throw new InvalidOperationException($"Webhook endpoint {endpointId} not found.");

        var testData = new { message = "Test webhook from RegOS", timestamp = DateTime.UtcNow };
        await DeliverAsync(endpoint, "webhook.test", testData, ct);
    }

    public async Task DeliverAsync(
        WebhookEndpoint endpoint, string eventType, object eventData,
        CancellationToken ct = default)
    {
        if (!endpoint.IsActive) return;

        var payload = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid().ToString("N"),
            type = eventType,
            timestamp = DateTime.UtcNow.ToString("O"),
            data = eventData
        }, JsonOptions);

        // Format payload for Slack/Teams if applicable
        var deliveryPayload = payload;
        if (SlackWebhookFormatter.IsSlackUrl(endpoint.Url))
        {
            deliveryPayload = SlackWebhookFormatter.FormatEvent(eventType, payload);
        }
        else if (TeamsWebhookFormatter.IsTeamsUrl(endpoint.Url))
        {
            deliveryPayload = TeamsWebhookFormatter.FormatEvent(eventType, payload);
        }

        var signature = ComputeHmacSha256(payload, endpoint.SecretKey);

        var delivery = new WebhookDelivery
        {
            EndpointId = endpoint.Id,
            EventType = eventType,
            Payload = payload,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };
        _db.WebhookDeliveries.Add(delivery);
        await _db.SaveChangesAsync(ct);

        try
        {
            var sw = Stopwatch.StartNew();
            using var client = _httpClientFactory.CreateClient("WebhookClient");
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
            {
                Content = new StringContent(deliveryPayload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-RegOS-Signature", $"sha256={signature}");
            request.Headers.Add("X-RegOS-Event", eventType);
            request.Headers.Add("X-RegOS-Delivery", delivery.Id.ToString());
            request.Headers.Add("X-RegOS-Timestamp", DateTime.UtcNow.ToString("O"));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var response = await client.SendAsync(request, cts.Token);
            sw.Stop();

            delivery.HttpStatus = (int)response.StatusCode;
            delivery.ResponseBody = await TruncateResponseAsync(response);
            delivery.DurationMs = (int)sw.ElapsedMilliseconds;
            delivery.AttemptCount = 1;

            if (response.IsSuccessStatusCode)
            {
                delivery.Status = "Delivered";
                delivery.DeliveredAt = DateTime.UtcNow;
                endpoint.LastDeliveryAt = DateTime.UtcNow;
                endpoint.FailureCount = 0;
            }
            else
            {
                delivery.Status = "Failed";
                endpoint.FailureCount++;
                ScheduleRetry(delivery);
            }
        }
        catch (Exception ex)
        {
            delivery.Status = "Failed";
            delivery.ResponseBody = ex.Message;
            delivery.AttemptCount = 1;
            endpoint.FailureCount++;
            ScheduleRetry(delivery);

            _logger.LogWarning(ex, "Webhook delivery {DeliveryId} to {Url} failed",
                delivery.Id, endpoint.Url);
        }

        // Auto-disable after 50 consecutive failures
        if (endpoint.FailureCount >= 50)
        {
            endpoint.IsActive = false;
            endpoint.DisabledReason = "Auto-disabled after 50 consecutive delivery failures";
            _logger.LogWarning("Webhook endpoint {EndpointId} auto-disabled after 50 failures", endpoint.Id);
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task RetryDeliveryAsync(WebhookDelivery delivery, CancellationToken ct = default)
    {
        if (delivery.Endpoint is null || !delivery.Endpoint.IsActive)
        {
            delivery.Status = "Exhausted";
            return;
        }

        var endpoint = delivery.Endpoint;
        var signature = ComputeHmacSha256(delivery.Payload, endpoint.SecretKey);

        try
        {
            var sw = Stopwatch.StartNew();
            using var client = _httpClientFactory.CreateClient("WebhookClient");
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
            {
                Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-RegOS-Signature", $"sha256={signature}");
            request.Headers.Add("X-RegOS-Event", delivery.EventType);
            request.Headers.Add("X-RegOS-Delivery", delivery.Id.ToString());
            request.Headers.Add("X-RegOS-Timestamp", DateTime.UtcNow.ToString("O"));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var response = await client.SendAsync(request, cts.Token);
            sw.Stop();

            delivery.HttpStatus = (int)response.StatusCode;
            delivery.ResponseBody = await TruncateResponseAsync(response);
            delivery.DurationMs = (int)sw.ElapsedMilliseconds;
            delivery.AttemptCount++;

            if (response.IsSuccessStatusCode)
            {
                delivery.Status = "Delivered";
                delivery.DeliveredAt = DateTime.UtcNow;
                delivery.NextRetryAt = null;
                endpoint.LastDeliveryAt = DateTime.UtcNow;
                endpoint.FailureCount = 0;
            }
            else
            {
                endpoint.FailureCount++;
                ScheduleRetry(delivery);
            }
        }
        catch (Exception ex)
        {
            delivery.AttemptCount++;
            delivery.ResponseBody = ex.Message;
            endpoint.FailureCount++;
            ScheduleRetry(delivery);

            _logger.LogWarning(ex, "Webhook retry {DeliveryId} attempt {Attempt} failed",
                delivery.Id, delivery.AttemptCount);
        }

        if (endpoint.FailureCount >= 50)
        {
            endpoint.IsActive = false;
            endpoint.DisabledReason = "Auto-disabled after 50 consecutive delivery failures";
        }
    }

    internal static string ComputeHmacSha256(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void ScheduleRetry(WebhookDelivery delivery)
    {
        if (delivery.AttemptCount >= delivery.MaxAttempts)
        {
            delivery.Status = "Exhausted";
            delivery.NextRetryAt = null;
            return;
        }

        delivery.NextRetryAt = DateTime.UtcNow.Add(delivery.AttemptCount switch
        {
            1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(10),
            _ => TimeSpan.FromHours(1)
        });
    }

    private static string GenerateSecret()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static async Task<string?> TruncateResponseAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return body.Length > 4000 ? body[..4000] : body;
    }
}
