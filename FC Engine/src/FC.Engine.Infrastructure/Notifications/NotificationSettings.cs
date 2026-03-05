namespace FC.Engine.Infrastructure.Notifications;

public class NotificationSettings
{
    public const string SectionName = "Notifications";

    public EmailSettings Email { get; set; } = new();
    public SmsSettings Sms { get; set; } = new();
    public SignalRSettings SignalR { get; set; } = new();
}

public class EmailSettings
{
    public string Provider { get; set; } = "SendGrid";
    public SendGridSettings SendGrid { get; set; } = new();
    public AwsSesSettings AwsSes { get; set; } = new();
}

public class SmsSettings
{
    public string Provider { get; set; } = "AfricasTalking";
    public AfricasTalkingSettings AfricasTalking { get; set; } = new();
}

public class SignalRSettings
{
    public bool RedisBackplane { get; set; }
}

public class SendGridSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string DefaultFromEmail { get; set; } = "noreply@regos.app";
    public string DefaultFromName { get; set; } = "RegOS";
}

public class AwsSesSettings
{
    public string Region { get; set; } = "eu-west-1";
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
    public string DefaultFromEmail { get; set; } = "noreply@regos.app";
    public string DefaultFromName { get; set; } = "RegOS";
    public string? ConfigurationSetName { get; set; }
}

public class AfricasTalkingSettings
{
    public string Username { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string? SenderId { get; set; }
    public string BaseUrl { get; set; } = "https://api.africastalking.com";
}
