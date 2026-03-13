using System.Text.Json;
using System.Text.Json.Serialization;

namespace FC.Engine.Portal.Services;

internal sealed class InstitutionSettingsEnvelope
{
    public InstitutionPortalSettings PortalSettings { get; set; } = new();
    public InstitutionProfileState Profile { get; set; } = new();
    public List<PendingInviteModel> PendingInvitations { get; set; } = [];
    public List<DeadlineExtensionRequestRecord> ExtensionRequests { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? LegacyRootValues { get; set; }
}

internal sealed class InstitutionProfileState
{
    public string? LogoUrl { get; set; }
    public List<ContactPersonModel> ContactPersons { get; set; } = [];
    public List<RegulatoryIdentifierModel> RegulatoryIdentifiers { get; set; } = [];
    public List<ProfileAuditEntry> RecentChanges { get; set; } = [];
}

internal sealed class DeadlineExtensionRequestRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int InstitutionId { get; set; }
    public int? RequestedByUserId { get; set; }
    public string RequestedByDisplayName { get; set; } = string.Empty;
    public string ReturnCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string? ModuleCode { get; set; }
    public string? PeriodLabel { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? RequestedUntil { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<string> AttachmentNames { get; set; } = [];
    public List<string> AttachmentUrls { get; set; } = [];
    public string Status { get; set; } = "Pending";
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
}

internal static class InstitutionSettingsState
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private static readonly string[] PortalSettingKeys =
    [
        nameof(InstitutionPortalSettings.EmailOnSubmissionResult),
        nameof(InstitutionPortalSettings.EmailOnDeadlineApproaching),
        nameof(InstitutionPortalSettings.DeadlineReminderDays),
        nameof(InstitutionPortalSettings.DefaultSubmissionFormat),
        nameof(InstitutionPortalSettings.SessionTimeoutHours),
        nameof(InstitutionPortalSettings.TimezoneId)
    ];

    public static InstitutionSettingsEnvelope Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new InstitutionSettingsEnvelope();
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<InstitutionSettingsEnvelope>(json, JsonOptions)
                           ?? new InstitutionSettingsEnvelope();
            Normalize(envelope);
            return envelope;
        }
        catch
        {
            return new InstitutionSettingsEnvelope
            {
                PortalSettings = DeserializeLegacyPortalSettings(json)
            };
        }
    }

    public static string Serialize(InstitutionSettingsEnvelope envelope)
    {
        Normalize(envelope);
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    private static void Normalize(InstitutionSettingsEnvelope envelope)
    {
        envelope.PortalSettings ??= new InstitutionPortalSettings();
        envelope.Profile ??= new InstitutionProfileState();
        envelope.PendingInvitations ??= [];
        envelope.ExtensionRequests ??= [];
        envelope.Profile.ContactPersons ??= [];
        envelope.Profile.RegulatoryIdentifiers ??= [];
        envelope.Profile.RecentChanges ??= [];

        if (envelope.LegacyRootValues is null || envelope.LegacyRootValues.Count == 0)
        {
            return;
        }

        var legacySettings = DeserializeLegacyPortalSettings(envelope.LegacyRootValues);
        envelope.PortalSettings = MergePortalSettings(envelope.PortalSettings, legacySettings);

        foreach (var key in PortalSettingKeys)
        {
            envelope.LegacyRootValues.Remove(key);
        }

        if (envelope.LegacyRootValues.Count == 0)
        {
            envelope.LegacyRootValues = null;
        }
    }

    private static InstitutionPortalSettings DeserializeLegacyPortalSettings(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<InstitutionPortalSettings>(json, JsonOptions)
                   ?? new InstitutionPortalSettings();
        }
        catch
        {
            return new InstitutionPortalSettings();
        }
    }

    private static InstitutionPortalSettings DeserializeLegacyPortalSettings(Dictionary<string, JsonElement> legacyValues)
    {
        var knownValues = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in PortalSettingKeys)
        {
            if (legacyValues.TryGetValue(key, out var value))
            {
                knownValues[key] = value;
            }
        }

        if (knownValues.Count == 0)
        {
            return new InstitutionPortalSettings();
        }

        try
        {
            var json = JsonSerializer.Serialize(knownValues, JsonOptions);
            return JsonSerializer.Deserialize<InstitutionPortalSettings>(json, JsonOptions)
                   ?? new InstitutionPortalSettings();
        }
        catch
        {
            return new InstitutionPortalSettings();
        }
    }

    private static InstitutionPortalSettings MergePortalSettings(
        InstitutionPortalSettings current,
        InstitutionPortalSettings legacy)
    {
        return new InstitutionPortalSettings
        {
            EmailOnSubmissionResult = current.EmailOnSubmissionResult,
            EmailOnDeadlineApproaching = current.EmailOnDeadlineApproaching,
            DeadlineReminderDays = current.DeadlineReminderDays,
            DefaultSubmissionFormat = string.IsNullOrWhiteSpace(current.DefaultSubmissionFormat)
                ? legacy.DefaultSubmissionFormat
                : current.DefaultSubmissionFormat,
            SessionTimeoutHours = current.SessionTimeoutHours <= 0
                ? legacy.SessionTimeoutHours
                : current.SessionTimeoutHours,
            TimezoneId = string.IsNullOrWhiteSpace(current.TimezoneId)
                ? legacy.TimezoneId
                : current.TimezoneId
        };
    }
}
