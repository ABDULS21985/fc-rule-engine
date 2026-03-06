using System.Text;
using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Application.Services;

public class NotificationOrchestrator : INotificationOrchestrator
{
    private static readonly TimeZoneInfo WafTimeZone = ResolveWafTimeZone();
    private static readonly NotificationChannelPolicy FallbackPolicy = new(
        InAppEnabled: true,
        EmailEnabled: true,
        SmsEnabled: false,
        IgnorePreferences: false,
        IgnoreQuietHours: false);

    private static readonly IReadOnlyDictionary<string, NotificationChannelPolicy> EventChannelMatrix =
        new Dictionary<string, NotificationChannelPolicy>(StringComparer.OrdinalIgnoreCase)
        {
            [NotificationEvents.ReturnCreated] = new(true, true, false, false, false),
            [NotificationEvents.ReturnSubmittedForReview] = new(true, true, false, false, false),
            [NotificationEvents.ReturnApproved] = new(true, true, false, false, false),
            [NotificationEvents.ReturnRejected] = new(true, true, false, false, false),
            [NotificationEvents.ReturnSubmittedToRegulator] = new(true, true, false, false, false),
            [NotificationEvents.ReturnQueryRaised] = new(true, true, false, false, false),

            [NotificationEvents.DeadlineT30] = new(false, true, false, false, false),
            [NotificationEvents.DeadlineT14] = new(true, true, false, false, false),
            [NotificationEvents.DeadlineT7] = new(true, true, true, false, false),
            [NotificationEvents.DeadlineT3] = new(true, true, true, false, false),
            [NotificationEvents.DeadlineT1] = new(false, true, true, false, true),
            [NotificationEvents.DeadlineOverdue] = new(false, true, true, true, true),

            [NotificationEvents.TrialExpiring] = new(false, true, false, false, false),
            [NotificationEvents.PaymentOverdue] = new(false, true, true, true, true),
            [NotificationEvents.SubscriptionSuspended] = new(false, true, true, true, true),
            [NotificationEvents.ModuleActivated] = new(true, true, false, false, false),

            [NotificationEvents.ExportReady] = new(true, true, false, false, false),
            [NotificationEvents.UserInvited] = new(false, true, false, false, false),
            [NotificationEvents.PasswordReset] = new(false, true, false, false, false),
            [NotificationEvents.SystemAnnouncement] = new(true, true, false, false, false),
            [NotificationEvents.DataFlowCompleted] = new(true, true, false, false, false),

            [NotificationEvents.MfaCodeSms] = new(false, false, true, true, true)
        };

    private readonly IPortalNotificationRepository _portalNotificationRepository;
    private readonly IInstitutionUserRepository _institutionUserRepository;
    private readonly IPortalUserRepository _portalUserRepository;
    private readonly IInstitutionRepository _institutionRepository;
    private readonly INotificationPreferenceRepository _notificationPreferenceRepository;
    private readonly INotificationDeliveryRepository _notificationDeliveryRepository;
    private readonly IEmailSender _emailSender;
    private readonly ISmsSender _smsSender;
    private readonly INotificationPusher _pusher;
    private readonly ITenantBrandingService _brandingService;
    private readonly ILogger<NotificationOrchestrator> _logger;

    public NotificationOrchestrator(
        IPortalNotificationRepository portalNotificationRepository,
        IInstitutionUserRepository institutionUserRepository,
        IPortalUserRepository portalUserRepository,
        IInstitutionRepository institutionRepository,
        INotificationPreferenceRepository notificationPreferenceRepository,
        INotificationDeliveryRepository notificationDeliveryRepository,
        IEmailSender emailSender,
        ISmsSender smsSender,
        INotificationPusher pusher,
        ITenantBrandingService brandingService,
        ILogger<NotificationOrchestrator> logger)
    {
        _portalNotificationRepository = portalNotificationRepository;
        _institutionUserRepository = institutionUserRepository;
        _portalUserRepository = portalUserRepository;
        _institutionRepository = institutionRepository;
        _notificationPreferenceRepository = notificationPreferenceRepository;
        _notificationDeliveryRepository = notificationDeliveryRepository;
        _emailSender = emailSender;
        _smsSender = smsSender;
        _pusher = pusher;
        _brandingService = brandingService;
        _logger = logger;
    }

    public async Task Notify(NotificationRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.EventType))
        {
            throw new InvalidOperationException("Notification event type is required.");
        }

        var recipients = await ResolveRecipients(request, ct);
        if (recipients.Count == 0)
        {
            _logger.LogDebug(
                "No recipients resolved for notification event {EventType} tenant {TenantId}",
                request.EventType,
                request.TenantId);
            return;
        }

        var policy = ResolvePolicy(request);
        if (string.Equals(request.EventType, NotificationEvents.MfaCodeSms, StringComparison.OrdinalIgnoreCase))
        {
            // Security-sensitive: MFA delivery is always SMS-only.
            policy = EventChannelMatrix[NotificationEvents.MfaCodeSms];
        }

        var isMandatoryEvent = request.IsMandatory || NotificationPolicy.MandatoryEvents.Contains(request.EventType);
        var ignorePreferences = policy.IgnorePreferences || isMandatoryEvent;
        Domain.ValueObjects.BrandingConfig? branding = null;

        foreach (var recipient in recipients)
        {
            var preference = await ResolvePreference(request.TenantId, recipient.UserId, request.EventType, ct);
            var payload = request.ToPayload();

            if (policy.InAppEnabled && (preference.InAppEnabled || ignorePreferences))
            {
                await CreateInAppNotification(request, recipient, ct);
                try
                {
                    await _pusher.PushToUser(recipient.UserId, payload, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SignalR push failed for user {UserId}", recipient.UserId);
                }
            }

            if (policy.EmailEnabled
                && (preference.EmailEnabled || ignorePreferences)
                && !string.IsNullOrWhiteSpace(recipient.Email))
            {
                branding ??= await _brandingService.GetBrandingConfig(request.TenantId, ct);
                await SendEmail(request, recipient, branding, ct);
            }

            if (policy.SmsEnabled
                && (preference.SmsEnabled || ignorePreferences)
                && !string.IsNullOrWhiteSpace(recipient.Phone)
                && (policy.IgnoreQuietHours || ignorePreferences || !IsWithinQuietHours(preference)))
            {
                await SendSms(request, recipient, ct);
            }
        }
    }

    private async Task CreateInAppNotification(
        NotificationRequest request,
        NotificationRecipient recipient,
        CancellationToken ct)
    {
        var notification = new PortalNotification
        {
            TenantId = request.TenantId,
            UserId = recipient.UserId,
            InstitutionId = recipient.InstitutionId,
            EventType = request.EventType,
            Channel = NotificationChannel.InApp,
            Priority = request.Priority,
            RecipientEmail = recipient.Email,
            RecipientPhone = recipient.Phone,
            Type = MapLegacyType(request.EventType),
            Title = request.Title,
            Message = request.Message,
            Link = request.ActionUrl,
            Metadata = request.Data.Count == 0
                ? null
                : JsonSerializer.Serialize(request.Data),
            CreatedAt = DateTime.UtcNow
        };

        await _portalNotificationRepository.Add(notification, ct);
    }

    private async Task SendEmail(
        NotificationRequest request,
        NotificationRecipient recipient,
        Domain.ValueObjects.BrandingConfig branding,
        CancellationToken ct)
    {
        var variables = BuildVariables(request, recipient);

        var delivery = new NotificationDelivery
        {
            TenantId = request.TenantId,
            NotificationEventType = request.EventType,
            Channel = NotificationChannel.Email,
            RecipientId = recipient.UserId,
            RecipientAddress = recipient.Email!,
            Status = DeliveryStatus.Queued,
            Payload = JsonSerializer.Serialize(variables),
            CreatedAt = DateTime.UtcNow
        };

        await _notificationDeliveryRepository.Add(delivery, ct);

        try
        {
            var result = await _emailSender.SendTemplatedAsync(
                request.EventType,
                variables,
                recipient.Email!,
                recipient.Name,
                branding,
                request.TenantId,
                ct);

            delivery.AttemptCount++;
            delivery.SentAt = DateTime.UtcNow;
            delivery.ProviderMessageId = result.ProviderMessageId;
            delivery.ErrorMessage = result.ErrorMessage;
            delivery.Status = result.Success ? DeliveryStatus.Sent : DeliveryStatus.Failed;
            if (!result.Success)
            {
                delivery.NextRetryAt = DateTime.UtcNow.AddMinutes(5);
            }
        }
        catch (Exception ex)
        {
            delivery.AttemptCount++;
            delivery.Status = DeliveryStatus.Failed;
            delivery.ErrorMessage = ex.Message;
            delivery.NextRetryAt = DateTime.UtcNow.AddMinutes(5);
            _logger.LogError(ex, "Email delivery failed for event {EventType} to {Email}", request.EventType, recipient.Email);
        }

        await _notificationDeliveryRepository.Update(delivery, ct);
    }

    private async Task SendSms(NotificationRequest request, NotificationRecipient recipient, CancellationToken ct)
    {
        var variables = BuildVariables(request, recipient);
        var smsText = BuildSmsTemplate(request.EventType, variables);
        if (smsText.Length > 160)
        {
            smsText = smsText[..157] + "...";
        }

        var delivery = new NotificationDelivery
        {
            TenantId = request.TenantId,
            NotificationEventType = request.EventType,
            Channel = NotificationChannel.Sms,
            RecipientId = recipient.UserId,
            RecipientAddress = recipient.Phone!,
            Status = DeliveryStatus.Queued,
            Payload = JsonSerializer.Serialize(variables),
            CreatedAt = DateTime.UtcNow
        };

        await _notificationDeliveryRepository.Add(delivery, ct);

        try
        {
            var result = await _smsSender.SendAsync(recipient.Phone!, smsText, ct);
            delivery.AttemptCount++;
            delivery.SentAt = DateTime.UtcNow;
            delivery.ProviderMessageId = result.ProviderMessageId;
            delivery.ErrorMessage = result.ErrorMessage;
            delivery.Status = result.Success ? DeliveryStatus.Sent : DeliveryStatus.Failed;
            if (!result.Success)
            {
                delivery.NextRetryAt = DateTime.UtcNow.AddMinutes(5);
            }
        }
        catch (Exception ex)
        {
            delivery.AttemptCount++;
            delivery.Status = DeliveryStatus.Failed;
            delivery.ErrorMessage = ex.Message;
            delivery.NextRetryAt = DateTime.UtcNow.AddMinutes(5);
            _logger.LogError(ex, "Sms delivery failed for event {EventType} to {Phone}", request.EventType, recipient.Phone);
        }

        await _notificationDeliveryRepository.Update(delivery, ct);
    }

    private async Task<IReadOnlyList<NotificationRecipient>> ResolveRecipients(NotificationRequest request, CancellationToken ct)
    {
        var recipients = new Dictionary<int, NotificationRecipient>();

        foreach (var userId in request.RecipientUserIds.Distinct())
        {
            var user = await _institutionUserRepository.GetById(userId, ct);
            if (user is null || !user.IsActive || user.TenantId != request.TenantId)
            {
                continue;
            }

            recipients[user.Id] = new NotificationRecipient
            {
                UserId = user.Id,
                TenantId = user.TenantId,
                InstitutionId = user.InstitutionId,
                Name = user.DisplayName,
                Role = user.Role.ToString(),
                Email = user.Email,
                Phone = user.PhoneNumber ?? user.Institution?.ContactPhone
            };
        }

        foreach (var portalUserId in request.RecipientPortalUserIds.Distinct())
        {
            var user = await _portalUserRepository.GetById(portalUserId, ct);
            if (user is null || !user.IsActive || user.TenantId != request.TenantId)
            {
                continue;
            }

            recipients[user.Id] = new NotificationRecipient
            {
                UserId = user.Id,
                TenantId = user.TenantId ?? request.TenantId,
                InstitutionId = 0,
                Name = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
                Role = user.Role.ToString(),
                Email = user.Email,
                Phone = null
            };
        }

        if (request.RecipientInstitutionId.HasValue)
        {
            var users = await _institutionUserRepository.GetByInstitution(request.RecipientInstitutionId.Value, ct);
            foreach (var user in users.Where(u => u.IsActive && u.TenantId == request.TenantId))
            {
                recipients[user.Id] = new NotificationRecipient
                {
                    UserId = user.Id,
                    TenantId = user.TenantId,
                    InstitutionId = user.InstitutionId,
                    Name = user.DisplayName,
                    Role = user.Role.ToString(),
                    Email = user.Email,
                    Phone = user.PhoneNumber
                };
            }
        }

        if (request.RecipientRoles.Count > 0)
        {
            var allowedRoles = request.RecipientRoles
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var institutions = await _institutionRepository.GetByTenant(request.TenantId, ct);
            foreach (var institution in institutions)
            {
                var users = await _institutionUserRepository.GetByInstitution(institution.Id, ct);
                foreach (var user in users)
                {
                    if (!user.IsActive || !allowedRoles.Contains(user.Role.ToString()))
                    {
                        continue;
                    }

                    recipients[user.Id] = new NotificationRecipient
                    {
                        UserId = user.Id,
                        TenantId = user.TenantId,
                        InstitutionId = user.InstitutionId,
                        Name = user.DisplayName,
                        Role = user.Role.ToString(),
                        Email = user.Email,
                        Phone = user.PhoneNumber ?? institution.ContactPhone
                    };
                }
            }
        }

        return recipients.Values.ToList();
    }

    private async Task<NotificationPreference> ResolvePreference(
        Guid tenantId,
        int userId,
        string eventType,
        CancellationToken ct)
    {
        var preference = await _notificationPreferenceRepository.GetPreference(tenantId, userId, eventType, ct);
        if (preference is not null)
        {
            return preference;
        }

        return new NotificationPreference
        {
            TenantId = tenantId,
            UserId = userId,
            EventType = eventType,
            InAppEnabled = true,
            EmailEnabled = true,
            SmsEnabled = false,
            SmsQuietHoursStart = new TimeSpan(22, 0, 0),
            SmsQuietHoursEnd = new TimeSpan(7, 0, 0)
        };
    }

    private static Dictionary<string, string> BuildVariables(NotificationRequest request, NotificationRecipient recipient)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Title"] = request.Title,
            ["Message"] = request.Message,
            ["RecipientName"] = recipient.Name,
            ["ActionUrl"] = request.ActionUrl ?? string.Empty,
            ["EventType"] = request.EventType
        };

        foreach (var (key, value) in request.Data)
        {
            variables[key] = value;
        }

        return variables;
    }

    private static bool IsWithinQuietHours(NotificationPreference preference)
    {
        if (!preference.SmsQuietHoursStart.HasValue || !preference.SmsQuietHoursEnd.HasValue)
        {
            return false;
        }

        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, WafTimeZone).TimeOfDay;
        var start = preference.SmsQuietHoursStart.Value;
        var end = preference.SmsQuietHoursEnd.Value;

        if (start > end)
        {
            return nowLocal >= start || nowLocal < end;
        }

        return nowLocal >= start && nowLocal < end;
    }

    private static string BuildSmsTemplate(string eventType, IReadOnlyDictionary<string, string> vars)
    {
        static string Get(IReadOnlyDictionary<string, string> map, string key, string fallback = "")
            => map.TryGetValue(key, out var value) ? value : fallback;

        return eventType switch
        {
            NotificationEvents.MfaCodeSms => $"Your RegOS verification code is {Get(vars, "Code")}. Valid for 5 minutes. Do not share.",
            NotificationEvents.DeadlineT1 =>
                $"URGENT: {Get(vars, "ModuleName", "Return")} due TOMORROW {Get(vars, "Deadline")}. Login: regos.app",
            NotificationEvents.DeadlineOverdue =>
                $"OVERDUE: {Get(vars, "ModuleName", "Return")} past deadline. Submit immediately. regos.app",
            NotificationEvents.PaymentOverdue =>
                $"RegOS invoice {Get(vars, "InvoiceNumber")} overdue. Pay now to avoid suspension.",
            _ => BuildFallbackSms(vars)
        };
    }

    private static string BuildFallbackSms(IReadOnlyDictionary<string, string> vars)
    {
        var title = vars.TryGetValue("Title", out var t) ? t : "RegOS Notification";
        var message = vars.TryGetValue("Message", out var m) ? m : "You have a new update.";
        var actionUrl = vars.TryGetValue("ActionUrl", out var u) ? u : string.Empty;

        var builder = new StringBuilder();
        builder.Append(title);
        builder.Append(": ");
        builder.Append(message);
        if (!string.IsNullOrWhiteSpace(actionUrl))
        {
            builder.Append(" ");
            builder.Append(actionUrl);
        }

        return builder.ToString();
    }

    private static NotificationChannelPolicy ResolvePolicy(NotificationRequest request)
    {
        if (EventChannelMatrix.TryGetValue(request.EventType, out var policy))
        {
            return policy;
        }

        var smsEnabled = request.Priority >= NotificationPriority.High;
        return FallbackPolicy with { SmsEnabled = smsEnabled };
    }

    private static NotificationType MapLegacyType(string eventType)
    {
        if (eventType.StartsWith("deadline.", StringComparison.OrdinalIgnoreCase))
        {
            return NotificationType.DeadlineApproaching;
        }

        if (string.Equals(eventType, NotificationEvents.ReturnSubmittedForReview, StringComparison.OrdinalIgnoreCase))
        {
            return NotificationType.ApprovalRequest;
        }

        if (string.Equals(eventType, NotificationEvents.ReturnApproved, StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, NotificationEvents.ReturnRejected, StringComparison.OrdinalIgnoreCase))
        {
            return NotificationType.ApprovalResult;
        }

        if (string.Equals(eventType, NotificationEvents.SystemAnnouncement, StringComparison.OrdinalIgnoreCase)
            || eventType.StartsWith("subscription.", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, NotificationEvents.ExportReady, StringComparison.OrdinalIgnoreCase))
        {
            return NotificationType.SystemAnnouncement;
        }

        return NotificationType.SubmissionResult;
    }

    private static TimeZoneInfo ResolveWafTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");
        }
        catch
        {
            return TimeZoneInfo.CreateCustomTimeZone("WAT", TimeSpan.FromHours(1), "WAT", "WAT");
        }
    }

    private sealed record NotificationChannelPolicy(
        bool InAppEnabled,
        bool EmailEnabled,
        bool SmsEnabled,
        bool IgnorePreferences,
        bool IgnoreQuietHours);
}
