using System.Text.Json;
using FC.Engine.Infrastructure.Webhooks;
using FluentAssertions;

namespace FC.Engine.Infrastructure.Tests.Webhooks;

public class SlackTeamsFormatterTests
{
    private const string SamplePayload = """
    {
        "id": "abc123",
        "type": "return.approved",
        "timestamp": "2026-01-15T10:00:00Z",
        "data": {
            "returnCode": "CBN001",
            "periodLabel": "Jan 2026",
            "submissionId": 42
        }
    }
    """;

    private const string ValidationPayload = """
    {
        "id": "abc123",
        "type": "validation.completed",
        "timestamp": "2026-01-15T10:00:00Z",
        "data": {
            "errorCount": 3,
            "warningCount": 1
        }
    }
    """;

    // ── Slack ──

    [Fact]
    public void Slack_IsSlackUrl_Detects_Slack_Hooks()
    {
        SlackWebhookFormatter.IsSlackUrl("https://hooks.slack.com/services/T00/B00/xxx")
            .Should().BeTrue();
    }

    [Fact]
    public void Slack_IsSlackUrl_Returns_False_For_Generic_Url()
    {
        SlackWebhookFormatter.IsSlackUrl("https://example.com/webhook")
            .Should().BeFalse();
    }

    [Fact]
    public void Slack_FormatEvent_Produces_Valid_Json()
    {
        var result = SlackWebhookFormatter.FormatEvent("return.approved", SamplePayload);

        result.Should().NotBeNullOrWhiteSpace();
        var action = () => JsonDocument.Parse(result);
        action.Should().NotThrow();
    }

    [Fact]
    public void Slack_FormatEvent_Contains_Attachments()
    {
        var result = SlackWebhookFormatter.FormatEvent("return.approved", SamplePayload);
        using var doc = JsonDocument.Parse(result);

        doc.RootElement.TryGetProperty("attachments", out var attachments).Should().BeTrue();
        attachments.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Slack_FormatEvent_ReturnApproved_Has_Green_Color()
    {
        var result = SlackWebhookFormatter.FormatEvent("return.approved", SamplePayload);
        using var doc = JsonDocument.Parse(result);

        var color = doc.RootElement
            .GetProperty("attachments")[0]
            .GetProperty("color")
            .GetString();

        color.Should().Be("#36a64f");
    }

    [Fact]
    public void Slack_FormatEvent_Validation_Includes_Error_Count()
    {
        var result = SlackWebhookFormatter.FormatEvent("validation.completed", ValidationPayload);

        result.Should().Contain("3");
        result.Should().Contain("1");
    }

    [Fact]
    public void Slack_FormatEvent_Unknown_EventType_Falls_Back()
    {
        var result = SlackWebhookFormatter.FormatEvent("unknown.event", SamplePayload);

        result.Should().NotBeNullOrWhiteSpace();
        result.Should().Contain("unknown.event");
    }

    // ── Teams ──

    [Fact]
    public void Teams_IsTeamsUrl_Detects_Office_Webhooks()
    {
        TeamsWebhookFormatter.IsTeamsUrl("https://company.webhook.office.com/webhookb2/xxx")
            .Should().BeTrue();
    }

    [Fact]
    public void Teams_IsTeamsUrl_Returns_False_For_Generic_Url()
    {
        TeamsWebhookFormatter.IsTeamsUrl("https://example.com/webhook")
            .Should().BeFalse();
    }

    [Fact]
    public void Teams_FormatEvent_Produces_Valid_Json()
    {
        var result = TeamsWebhookFormatter.FormatEvent("return.approved", SamplePayload);

        result.Should().NotBeNullOrWhiteSpace();
        var action = () => JsonDocument.Parse(result);
        action.Should().NotThrow();
    }

    [Fact]
    public void Teams_FormatEvent_Contains_AdaptiveCard()
    {
        var result = TeamsWebhookFormatter.FormatEvent("return.approved", SamplePayload);
        using var doc = JsonDocument.Parse(result);

        var attachments = doc.RootElement.GetProperty("attachments");
        attachments.GetArrayLength().Should().BeGreaterThan(0);

        var contentType = attachments[0].GetProperty("contentType").GetString();
        contentType.Should().Be("application/vnd.microsoft.card.adaptive");
    }

    [Fact]
    public void Teams_FormatEvent_Unknown_EventType_Falls_Back()
    {
        var result = TeamsWebhookFormatter.FormatEvent("unknown.event", SamplePayload);

        result.Should().NotBeNullOrWhiteSpace();
        result.Should().Contain("unknown.event");
    }

    [Fact]
    public void Teams_FormatEvent_ReturnRejected_Uses_Attention_Color()
    {
        var rejectedPayload = """
        {
            "id": "def456",
            "type": "return.rejected",
            "timestamp": "2026-01-15T10:00:00Z",
            "data": {
                "returnCode": "CBN002",
                "periodLabel": "Feb 2026"
            }
        }
        """;

        var result = TeamsWebhookFormatter.FormatEvent("return.rejected", rejectedPayload);
        result.Should().Contain("Attention");
    }

    // ── Edge cases ──

    [Fact]
    public void Slack_FormatEvent_Handles_Invalid_Payload_Gracefully()
    {
        var result = SlackWebhookFormatter.FormatEvent("return.approved", "not-valid-json");

        result.Should().NotBeNullOrWhiteSpace();
        var action = () => JsonDocument.Parse(result);
        action.Should().NotThrow();
    }

    [Fact]
    public void Teams_FormatEvent_Handles_Invalid_Payload_Gracefully()
    {
        var result = TeamsWebhookFormatter.FormatEvent("return.approved", "not-valid-json");

        result.Should().NotBeNullOrWhiteSpace();
        var action = () => JsonDocument.Parse(result);
        action.Should().NotThrow();
    }
}
