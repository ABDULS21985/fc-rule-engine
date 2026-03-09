using Dapper;
using FC.Engine.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

/// <summary>
/// At-least-once webhook dispatcher with HMAC-SHA256 signing.
/// Deliveries are persisted in CaaSWebhookDeliveries before HTTP dispatch.
/// Exponential back-off: 30s → 5m → 30m → 2h → 8h. Dead-letter after 5 attempts.
/// </summary>
public sealed class CaaSWebhookDispatcher : ICaaSWebhookDispatcher
{
    private readonly IDbConnectionFactory _db;
    private readonly HttpClient _httpClient;
    private readonly ILogger<CaaSWebhookDispatcher> _log;

    public CaaSWebhookDispatcher(
        IDbConnectionFactory db,
        IHttpClientFactory httpFactory,
        ILogger<CaaSWebhookDispatcher> log)
    {
        _db         = db;
        _httpClient = httpFactory.CreateClient("CaaSWebhook");
        _log        = log;
    }

    public async Task EnqueueAsync(
        int partnerId, WebhookEventType eventType,
        object payload, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        var partner = await conn.QuerySingleOrDefaultAsync<WebhookConfigRow>(
            """
            SELECT WebhookUrl, WebhookSecret
            FROM   CaaSPartners
            WHERE  Id = @PartnerId AND IsActive = 1 AND WebhookUrl IS NOT NULL
            """,
            new { PartnerId = partnerId });

        if (partner is null) return; // No webhook configured — silently skip

        var payloadJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            event_type  = ToSnakeCase(eventType.ToString()),
            occurred_at = DateTimeOffset.UtcNow.ToString("o"),
            data        = payload
        });

        var hmac = ComputeHmac(payloadJson, partner.WebhookSecret!);

        await conn.ExecuteAsync(
            """
            INSERT INTO CaaSWebhookDeliveries
                (PartnerId, EventType, Payload, HmacSignature, Status, NextRetryAt)
            VALUES (@PartnerId, @EventType, @Payload, @Hmac, 'PENDING', SYSUTCDATETIME())
            """,
            new
            {
                PartnerId = partnerId,
                EventType = ToSnakeCase(eventType.ToString()),
                Payload   = payloadJson,
                Hmac      = hmac
            });
    }

    public async Task ProcessPendingAsync(CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        var deliveries = await conn.QueryAsync<WebhookDeliveryRow>(
            """
            SELECT TOP 50
                   wd.Id, wd.PartnerId, wd.EventType, wd.Payload,
                   wd.HmacSignature, wd.AttemptCount, wd.MaxAttempts,
                   p.WebhookUrl
            FROM   CaaSWebhookDeliveries wd
            JOIN   CaaSPartners p ON p.Id = wd.PartnerId
            WHERE  wd.Status = 'PENDING'
              AND  (wd.NextRetryAt IS NULL OR wd.NextRetryAt <= SYSUTCDATETIME())
            ORDER BY wd.CreatedAt ASC
            """);

        foreach (var delivery in deliveries)
        {
            await DispatchDeliveryAsync(conn, delivery, ct);
        }
    }

    private async Task DispatchDeliveryAsync(
        System.Data.IDbConnection conn,
        WebhookDeliveryRow delivery,
        CancellationToken ct)
    {
        var attempt = delivery.AttemptCount + 1;

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, delivery.WebhookUrl)
            {
                Content = new StringContent(
                    delivery.Payload, System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-RegOS-Event",     delivery.EventType);
            request.Headers.Add("X-RegOS-Signature", $"sha256={delivery.HmacSignature}");
            request.Headers.Add("X-RegOS-Delivery",  delivery.Id.ToString());
            request.Headers.Add("X-RegOS-Attempt",   attempt.ToString());

            var response = await _httpClient.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                await conn.ExecuteAsync(
                    """
                    UPDATE CaaSWebhookDeliveries
                    SET    Status = 'DELIVERED', DeliveredAt = SYSUTCDATETIME(),
                           AttemptCount = @Attempt, LastHttpStatus = @Status
                    WHERE  Id = @Id
                    """,
                    new { Attempt = attempt, Status = (int)response.StatusCode, Id = delivery.Id });

                _log.LogInformation(
                    "Webhook delivered: DeliveryId={Id} Partner={PartnerId} Event={Event}",
                    delivery.Id, delivery.PartnerId, delivery.EventType);
            }
            else
            {
                await HandleFailedAttemptAsync(conn, delivery, attempt,
                    (int)response.StatusCode, $"HTTP {(int)response.StatusCode}", ct);
            }
        }
        catch (Exception ex)
        {
            await HandleFailedAttemptAsync(conn, delivery, attempt, null, ex.Message, ct);
        }
    }

    private static async Task HandleFailedAttemptAsync(
        System.Data.IDbConnection conn,
        WebhookDeliveryRow delivery,
        int attempt,
        int? httpStatus,
        string errorMessage,
        CancellationToken _)
    {
        var isDead = attempt >= delivery.MaxAttempts;

        // Exponential back-off: 30s, 5m, 30m, 2h, 8h
        var delaySeconds = (int)Math.Pow(2, attempt) * 30;
        var nextRetry    = isDead ? (DateTime?)null : DateTime.UtcNow.AddSeconds(delaySeconds);

        await conn.ExecuteAsync(
            """
            UPDATE CaaSWebhookDeliveries
            SET    AttemptCount = @Attempt,
                   LastAttemptAt = SYSUTCDATETIME(),
                   LastHttpStatus = @HttpStatus,
                   LastErrorMessage = @Error,
                   Status = @Status,
                   NextRetryAt = @NextRetry
            WHERE  Id = @Id
            """,
            new
            {
                Attempt    = attempt,
                HttpStatus = httpStatus,
                Error      = errorMessage[..Math.Min(1000, errorMessage.Length)],
                Status     = isDead ? "DEAD_LETTER" : "PENDING",
                NextRetry  = nextRetry,
                Id         = delivery.Id
            });
    }

    private static string ComputeHmac(string payload, string secret)
    {
        var key  = System.Text.Encoding.UTF8.GetBytes(secret);
        var data = System.Text.Encoding.UTF8.GetBytes(payload);
        using var hmac = new System.Security.Cryptography.HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(data)).ToLowerInvariant();
    }

    private static string ToSnakeCase(string input)
    {
        // "FilingCompleted" → "filing.completed"
        return System.Text.RegularExpressions.Regex
            .Replace(input, "([a-z])([A-Z])", "$1.$2")
            .ToLowerInvariant();
    }

    private sealed record WebhookConfigRow(string? WebhookUrl, string? WebhookSecret);

    private sealed record WebhookDeliveryRow(
        long Id, int PartnerId, string EventType, string Payload,
        string HmacSignature, int AttemptCount, int MaxAttempts, string WebhookUrl);
}
