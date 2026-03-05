using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.ValueObjects;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace FC.Engine.Infrastructure.Notifications;

public class SendGridEmailSender : IEmailSender
{
    private readonly ISendGridClient _client;
    private readonly SendGridSettings _settings;
    private readonly IEmailTemplateRepository _templateRepository;

    public SendGridEmailSender(
        IOptions<NotificationSettings> notificationOptions,
        IEmailTemplateRepository templateRepository)
        : this(
            new SendGridClient(notificationOptions.Value.Email.SendGrid.ApiKey),
            notificationOptions,
            templateRepository)
    {
    }

    internal SendGridEmailSender(
        ISendGridClient client,
        IOptions<NotificationSettings> notificationOptions,
        IEmailTemplateRepository templateRepository)
    {
        _settings = notificationOptions.Value.Email.SendGrid;
        _templateRepository = templateRepository;
        _client = client;
    }

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            return new EmailSendResult
            {
                Success = false,
                ErrorMessage = "SendGrid API key not configured."
            };
        }

        var from = new EmailAddress(
            message.FromEmail ?? _settings.DefaultFromEmail,
            message.FromName ?? _settings.DefaultFromName);
        var to = new EmailAddress(message.ToEmail, message.ToName);

        var sgMessage = MailHelper.CreateSingleEmail(
            from,
            to,
            message.Subject,
            message.PlainTextBody,
            message.HtmlBody);

        if (!string.IsNullOrWhiteSpace(message.ReplyTo))
        {
            sgMessage.ReplyTo = new EmailAddress(message.ReplyTo);
        }

        foreach (var header in message.Headers)
        {
            sgMessage.AddHeader(header.Key, header.Value);
        }

        var response = await _client.SendEmailAsync(sgMessage, ct);
        response.Headers.TryGetValues("X-Message-Id", out var messageIds);

        return new EmailSendResult
        {
            Success = response.IsSuccessStatusCode,
            ProviderMessageId = messageIds?.FirstOrDefault(),
            ErrorMessage = response.IsSuccessStatusCode
                ? null
                : await response.Body.ReadAsStringAsync(ct)
        };
    }

    public async Task<EmailSendResult> SendTemplatedAsync(
        string templateId,
        Dictionary<string, string> variables,
        string toEmail,
        string toName,
        BrandingConfig branding,
        Guid? tenantId = null,
        CancellationToken ct = default)
    {
        var template = tenantId.HasValue
            ? await _templateRepository.GetActiveTemplate(templateId, tenantId.Value, ct)
            : null;

        var subject = ReplaceVariables(template?.Subject ?? "{{Title}}", variables);
        var htmlBody = ReplaceVariables(template?.HtmlBody ?? "<p>{{Message}}</p>", variables);
        var plainText = ReplaceVariables(template?.PlainTextBody ?? "{{Message}}", variables);

        htmlBody = WrapWithBranding(htmlBody, branding);

        var (fromEmail, fromName) = ResolveSender(branding, _settings);

        return await SendAsync(new EmailMessage
        {
            FromEmail = fromEmail,
            FromName = fromName,
            ToEmail = toEmail,
            ToName = toName,
            Subject = subject,
            HtmlBody = htmlBody,
            PlainTextBody = plainText,
            ReplyTo = branding.SupportEmail
        }, ct);
    }

    internal static (string FromEmail, string FromName) ResolveSender(BrandingConfig branding, SendGridSettings defaults)
    {
        var fromEmail = !string.IsNullOrWhiteSpace(branding.SupportEmail)
            ? branding.SupportEmail!
            : defaults.DefaultFromEmail;

        var fromName = !string.IsNullOrWhiteSpace(branding.CompanyName)
            ? branding.CompanyName!
            : defaults.DefaultFromName;

        return (fromEmail, fromName);
    }

    internal static string ReplaceVariables(string? template, IReadOnlyDictionary<string, string> vars)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return string.Empty;
        }

        var output = template;
        foreach (var (key, value) in vars)
        {
            output = output.Replace($"{{{{{key}}}}}", value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return output;
    }

    internal static string WrapWithBranding(string bodyHtml, BrandingConfig branding)
    {
        var primary = string.IsNullOrWhiteSpace(branding.PrimaryColor) ? "#0f766e" : branding.PrimaryColor;
        var logo = branding.LogoUrl ?? string.Empty;
        var company = branding.CompanyName ?? "RegOS";
        var supportEmail = branding.SupportEmail;
        var copyright = branding.CopyrightText ?? $"(c) {DateTime.UtcNow.Year} RegOS. All rights reserved.";

        return $@"
<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='margin:0;padding:0;background:#f4f4f4;font-family:Arial,sans-serif;'>
<table width='100%' cellpadding='0' cellspacing='0' style='max-width:600px;margin:0 auto;background:#ffffff;'>
  <tr>
    <td style='padding:20px;text-align:center;background:{primary};'>
      {(string.IsNullOrWhiteSpace(logo) ? $"<strong style='color:#fff'>{company}</strong>" : $"<img src='{logo}' alt='{company}' style='max-height:50px;' />")}
    </td>
  </tr>
  <tr>
    <td style='padding:30px 20px;'>
      {bodyHtml}
    </td>
  </tr>
  <tr>
    <td style='padding:15px 20px;background:#f8f9fa;text-align:center;font-size:12px;color:#666;'>
      {copyright}<br/>
      {(string.IsNullOrWhiteSpace(supportEmail) ? string.Empty : $"Support: {supportEmail}")}
    </td>
  </tr>
</table>
</body>
</html>";
    }
}
