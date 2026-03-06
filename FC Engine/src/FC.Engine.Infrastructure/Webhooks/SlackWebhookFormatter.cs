using System.Text.Json;

namespace FC.Engine.Infrastructure.Webhooks;

/// <summary>
/// Formats domain events into Slack Block Kit JSON payloads.
/// </summary>
public static class SlackWebhookFormatter
{
    public static bool IsSlackUrl(string url) =>
        url.Contains("hooks.slack.com", StringComparison.OrdinalIgnoreCase);

    public static string FormatEvent(string eventType, string eventPayload)
    {
        var (title, body, color) = ParseEventContent(eventType, eventPayload);

        return JsonSerializer.Serialize(new
        {
            attachments = new[]
            {
                new
                {
                    color,
                    blocks = new object[]
                    {
                        new
                        {
                            type = "header",
                            text = new { type = "plain_text", text = title, emoji = true }
                        },
                        new
                        {
                            type = "section",
                            text = new { type = "mrkdwn", text = body }
                        },
                        new
                        {
                            type = "context",
                            elements = new[]
                            {
                                new { type = "mrkdwn", text = $"*Event:* `{eventType}` | *Source:* RegOS" }
                            }
                        }
                    }
                }
            }
        });
    }

    private static (string title, string body, string color) ParseEventContent(string eventType, string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var data = doc.RootElement.GetProperty("data");

            return eventType switch
            {
                "return.approved" => (
                    "Return Approved",
                    FormatReturnMessage(data, "approved"),
                    "#36a64f"),
                "return.rejected" => (
                    "Return Rejected",
                    FormatReturnMessage(data, "rejected"),
                    "#e01e5a"),
                "return.created" => (
                    "Return Created",
                    FormatReturnMessage(data, "created"),
                    "#2eb886"),
                "return.submitted" => (
                    "Return Submitted",
                    FormatReturnMessage(data, "submitted"),
                    "#4a90d9"),
                "return.submitted_to_regulator" => (
                    "Submitted to Regulator",
                    FormatReturnMessage(data, "submitted to regulator"),
                    "#6f42c1"),
                "validation.completed" => (
                    "Validation Completed",
                    FormatValidationMessage(data),
                    "#daa520"),
                "deadline.approaching" => (
                    "Deadline Approaching",
                    FormatDeadlineMessage(data),
                    "#ff9900"),
                "subscription.changed" => (
                    "Subscription Changed",
                    FormatSubscriptionMessage(data),
                    "#17a2b8"),
                "module.activated" => (
                    "Module Activated",
                    FormatModuleMessage(data),
                    "#28a745"),
                "export.completed" => (
                    "Export Completed",
                    GetString(data, "message", "An export has completed."),
                    "#6c757d"),
                _ => (
                    "RegOS Event",
                    $"Event `{eventType}` received.",
                    "#cccccc")
            };
        }
        catch
        {
            return ("RegOS Event", $"Event `{eventType}` received.", "#cccccc");
        }
    }

    private static string FormatReturnMessage(JsonElement data, string action)
    {
        var returnCode = GetString(data, "returnCode", "N/A");
        var period = GetString(data, "periodLabel", "");
        var periodPart = string.IsNullOrEmpty(period) ? "" : $" for *{period}*";
        return $"Return `{returnCode}`{periodPart} has been {action}.";
    }

    private static string FormatValidationMessage(JsonElement data)
    {
        var errors = GetInt(data, "errorCount");
        var warnings = GetInt(data, "warningCount");
        return $"Validation completed with *{errors}* error(s) and *{warnings}* warning(s).";
    }

    private static string FormatDeadlineMessage(JsonElement data)
    {
        var returnCode = GetString(data, "returnCode", "N/A");
        var deadline = GetString(data, "deadline", "");
        return $"Deadline approaching for `{returnCode}`. Due: *{deadline}*";
    }

    private static string FormatSubscriptionMessage(JsonElement data)
    {
        var changeType = GetString(data, "changeType", "changed");
        var from = GetString(data, "previousPlan", "");
        var to = GetString(data, "newPlan", "");
        return $"Subscription {changeType}: `{from}` → `{to}`";
    }

    private static string FormatModuleMessage(JsonElement data)
    {
        var moduleName = GetString(data, "moduleName", "Unknown");
        var moduleCode = GetString(data, "moduleCode", "");
        return $"Module *{moduleName}* (`{moduleCode}`) has been activated.";
    }

    private static string GetString(JsonElement el, string prop, string fallback)
    {
        if (el.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String)
            return val.GetString() ?? fallback;
        return fallback;
    }

    private static int GetInt(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.Number)
            return val.GetInt32();
        return 0;
    }
}
