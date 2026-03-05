using Amazon;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure.Notifications;

public class AwsSesEmailSender : IEmailSender
{
    private readonly IAmazonSimpleEmailServiceV2 _client;
    private readonly AwsSesSettings _settings;
    private readonly IEmailTemplateRepository _templateRepository;

    public AwsSesEmailSender(
        IOptions<NotificationSettings> notificationOptions,
        IEmailTemplateRepository templateRepository)
        : this(BuildClient(notificationOptions.Value.Email.AwsSes), notificationOptions, templateRepository)
    {
    }

    internal AwsSesEmailSender(
        IAmazonSimpleEmailServiceV2 client,
        IOptions<NotificationSettings> notificationOptions,
        IEmailTemplateRepository templateRepository)
    {
        _client = client;
        _settings = notificationOptions.Value.Email.AwsSes;
        _templateRepository = templateRepository;
    }

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var fromEmail = message.FromEmail ?? _settings.DefaultFromEmail;
        if (string.IsNullOrWhiteSpace(fromEmail))
        {
            return new EmailSendResult
            {
                Success = false,
                ErrorMessage = "AWS SES from email is not configured."
            };
        }

        var fromName = message.FromName ?? _settings.DefaultFromName;
        var request = new SendEmailRequest
        {
            FromEmailAddress = string.IsNullOrWhiteSpace(fromName)
                ? fromEmail
                : $"{fromName} <{fromEmail}>",
            Destination = new Destination
            {
                ToAddresses = new List<string> { message.ToEmail }
            },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = message.Subject, Charset = "UTF-8" },
                    Body = new Body
                    {
                        Html = new Content { Data = message.HtmlBody, Charset = "UTF-8" },
                        Text = string.IsNullOrWhiteSpace(message.PlainTextBody)
                            ? null
                            : new Content { Data = message.PlainTextBody, Charset = "UTF-8" }
                    }
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(message.ReplyTo))
        {
            request.ReplyToAddresses = new List<string> { message.ReplyTo };
        }

        if (!string.IsNullOrWhiteSpace(_settings.ConfigurationSetName))
        {
            request.ConfigurationSetName = _settings.ConfigurationSetName;
        }

        if (message.Headers.Count > 0)
        {
            request.EmailTags = message.Headers
                .Where(h => !string.IsNullOrWhiteSpace(h.Key))
                .Select(h => new MessageTag { Name = h.Key, Value = h.Value ?? string.Empty })
                .ToList();
        }

        try
        {
            var response = await _client.SendEmailAsync(request, ct);
            return new EmailSendResult
            {
                Success = true,
                ProviderMessageId = response.MessageId
            };
        }
        catch (Exception ex)
        {
            return new EmailSendResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
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

        var subject = SendGridEmailSender.ReplaceVariables(template?.Subject ?? "{{Title}}", variables);
        var htmlBody = SendGridEmailSender.ReplaceVariables(template?.HtmlBody ?? "<p>{{Message}}</p>", variables);
        var plainText = SendGridEmailSender.ReplaceVariables(template?.PlainTextBody ?? "{{Message}}", variables);

        htmlBody = SendGridEmailSender.WrapWithBranding(htmlBody, branding);

        var fromEmail = !string.IsNullOrWhiteSpace(branding.SupportEmail)
            ? branding.SupportEmail
            : _settings.DefaultFromEmail;

        var fromName = !string.IsNullOrWhiteSpace(branding.CompanyName)
            ? branding.CompanyName
            : _settings.DefaultFromName;

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

    private static IAmazonSimpleEmailServiceV2 BuildClient(AwsSesSettings settings)
    {
        var region = RegionEndpoint.GetBySystemName(string.IsNullOrWhiteSpace(settings.Region) ? "eu-west-1" : settings.Region);

        if (!string.IsNullOrWhiteSpace(settings.AccessKeyId) && !string.IsNullOrWhiteSpace(settings.SecretAccessKey))
        {
            var credentials = new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey);
            return new AmazonSimpleEmailServiceV2Client(credentials, region);
        }

        return new AmazonSimpleEmailServiceV2Client(region);
    }
}
