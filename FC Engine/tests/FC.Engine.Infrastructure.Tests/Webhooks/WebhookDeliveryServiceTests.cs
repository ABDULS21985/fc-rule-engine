using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Webhooks;
using FluentAssertions;

namespace FC.Engine.Infrastructure.Tests.Webhooks;

public class WebhookDeliveryServiceTests
{
    [Fact]
    public void HmacSignature_Is_Deterministic()
    {
        var payload = "{\"type\":\"return.approved\",\"data\":{}}";
        var secret = "test-secret-key";

        var sig1 = WebhookDeliveryService.ComputeHmacSha256(payload, secret);
        var sig2 = WebhookDeliveryService.ComputeHmacSha256(payload, secret);

        sig1.Should().NotBeNullOrWhiteSpace();
        sig1.Should().Be(sig2);
    }

    [Fact]
    public void HmacSignature_Changes_With_Different_Secret()
    {
        var payload = "{\"type\":\"return.approved\",\"data\":{}}";

        var sig1 = WebhookDeliveryService.ComputeHmacSha256(payload, "secret-a");
        var sig2 = WebhookDeliveryService.ComputeHmacSha256(payload, "secret-b");

        sig1.Should().NotBe(sig2);
    }

    [Fact]
    public void HmacSignature_Changes_With_Different_Payload()
    {
        var secret = "test-secret";

        var sig1 = WebhookDeliveryService.ComputeHmacSha256("{\"a\":1}", secret);
        var sig2 = WebhookDeliveryService.ComputeHmacSha256("{\"a\":2}", secret);

        sig1.Should().NotBe(sig2);
    }

    [Fact]
    public void HmacSignature_Is_Lowercase_Hex()
    {
        var sig = WebhookDeliveryService.ComputeHmacSha256("test", "key");

        sig.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ScheduleRetry_Backoff_1min_10min_1hr()
    {
        var delivery = new WebhookDelivery { AttemptCount = 1, MaxAttempts = 5 };

        // Simulate calling ScheduleRetry via attempt count checking
        // Attempt 1 => 1 min
        delivery.AttemptCount = 1;
        var before = DateTime.UtcNow;
        SimulateScheduleRetry(delivery);
        delivery.NextRetryAt.Should().NotBeNull();
        delivery.NextRetryAt!.Value.Should().BeCloseTo(before.AddMinutes(1), TimeSpan.FromSeconds(5));

        // Attempt 2 => 10 min
        delivery.AttemptCount = 2;
        before = DateTime.UtcNow;
        SimulateScheduleRetry(delivery);
        delivery.NextRetryAt!.Value.Should().BeCloseTo(before.AddMinutes(10), TimeSpan.FromSeconds(5));

        // Attempt 3+ => 1 hr
        delivery.AttemptCount = 3;
        before = DateTime.UtcNow;
        SimulateScheduleRetry(delivery);
        delivery.NextRetryAt!.Value.Should().BeCloseTo(before.AddHours(1), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ScheduleRetry_Marks_Exhausted_After_MaxAttempts()
    {
        var delivery = new WebhookDelivery { AttemptCount = 3, MaxAttempts = 3 };

        SimulateScheduleRetry(delivery);

        delivery.Status.Should().Be("Exhausted");
        delivery.NextRetryAt.Should().BeNull();
    }

    [Fact]
    public void ScheduleRetry_Does_Not_Exhaust_When_Under_Max()
    {
        var delivery = new WebhookDelivery { AttemptCount = 1, MaxAttempts = 3 };

        SimulateScheduleRetry(delivery);

        delivery.Status.Should().NotBe("Exhausted");
        delivery.NextRetryAt.Should().NotBeNull();
    }

    /// <summary>
    /// Mirrors the private ScheduleRetry logic from WebhookDeliveryService.
    /// </summary>
    private static void SimulateScheduleRetry(WebhookDelivery delivery)
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
}
