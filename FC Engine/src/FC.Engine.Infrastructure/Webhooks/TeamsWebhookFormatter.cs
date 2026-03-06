using System.Text.Json;

namespace FC.Engine.Infrastructure.Webhooks;

/// <summary>
/// Formats domain events into Microsoft Teams Adaptive Card JSON payloads.
/// </summary>
public static class TeamsWebhookFormatter
{
    public static bool IsTeamsUrl(string url) =>
        url.Contains(".webhook.office.com", StringComparison.OrdinalIgnoreCase)
        || url.Contains("microsoft.webhook.office.com", StringComparison.OrdinalIgnoreCase);

    public static string FormatEvent(string eventType, string eventPayload)
    {
        var (title, body, color) = ParseEventContent(eventType, eventPayload);

        return JsonSerializer.Serialize(new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = new
                    {
                        type = "AdaptiveCard",
                        version = "1.4",
                        body = new object[]
                        {
                            new
                            {
                                type = "TextBlock",
                                size = "Medium",
                                weight = "Bolder",
                                text = title,
                                color
                            },
                            new
                            {
                                type = "TextBlock",
                                text = body,
                                wrap = true
                            },
                            new
                            {
                                type = "TextBlock",
                                text = $"Event: {eventType} | Source: RegOS",
                                isSubtle = true,
                                size = "Small"
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
                    "Good"),
                "return.rejected" => (
                    "Return Rejected",
                    FormatReturnMessage(data, "rejected"),
                    "Attention"),
                "return.created" => (
                    "Return Created",
                    FormatReturnMessage(data, "created"),
                    "Good"),
                "return.submitted" => (
                    "Return Submitted",
                    FormatReturnMessage(data, "submitted"),
                    "Accent"),
                "return.submitted_to_regulator" => (
                    "Submitted to Regulator",
                    FormatReturnMessage(data, "submitted to regulator"),
                    "Accent"),
                "validation.completed" => (
                    "Validation Completed",
                    FormatValidationMessage(data),
                    "Warning"),
                "deadline.approaching" => (
                    "Deadline Approaching",
                    FormatDeadlineMessage(data),
                    "Attention"),
                "subscription.changed" => (
                    "Subscription Changed",
                    FormatSubscriptionMessage(data),
                    "Accent"),
                "module.activated" => (
                    "Module Activated",
                    FormatModuleMessage(data),
                    "Good"),
                "export.completed" => (
                    "Export Completed",
                    GetString(data, "message", "An export has completed."),
                    "Default"),
                _ => (
                    "RegOS Event",
                    $"Event '{eventType}' received.",
                    "Default")
            };
        }
        catch
        {
            return ("RegOS Event", $"Event '{eventType}' received.", "Default");
        }
    }

    private static string FormatReturnMessage(JsonElement data, string action)
    {
        var returnCode = GetString(data, "returnCode", "N/A");
        var period = GetString(data, "periodLabel", "");
        var periodPart = string.IsNullOrEmpty(period) ? "" : $" for {period}";
        return $"Return {returnCode}{periodPart} has been {action}.";
    }

    private static string FormatValidationMessage(JsonElement data)
    {
        var errors = GetInt(data, "errorCount");
        var warnings = GetInt(data, "warningCount");
        return $"Validation completed with {errors} error(s) and {warnings} warning(s).";
    }

    private static string FormatDeadlineMessage(JsonElement data)
    {
        var returnCode = GetString(data, "returnCode", "N/A");
        var deadline = GetString(data, "deadline", "");
        return $"Deadline approaching for {returnCode}. Due: {deadline}";
    }

    private static string FormatSubscriptionMessage(JsonElement data)
    {
        var changeType = GetString(data, "changeType", "changed");
        var from = GetString(data, "previousPlan", "");
        var to = GetString(data, "newPlan", "");
        return $"Subscription {changeType}: {from} to {to}";
    }

    private static string FormatModuleMessage(JsonElement data)
    {
        var moduleName = GetString(data, "moduleName", "Unknown");
        var moduleCode = GetString(data, "moduleCode", "");
        return $"Module {moduleName} ({moduleCode}) has been activated.";
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
