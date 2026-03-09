namespace FC.Engine.Infrastructure.Export;

public class RegulatoryApiSettings
{
    public const string SectionName = "RegulatoryApi";

    public bool Enabled { get; set; }
    public DigitalSignatureSettings DigitalSignature { get; set; } = new();
    public CbnApiSettings Cbn { get; set; } = new();
    public NfiuApiSettings Nfiu { get; set; } = new();
    public RegulatorApiEndpoint Ndic { get; set; } = new();
    public RegulatorApiEndpoint Sec { get; set; } = new();
    public RegulatorApiEndpoint Naicom { get; set; } = new();
    public RegulatorApiEndpoint Pencom { get; set; } = new();
    public StatusPollingSettings StatusPolling { get; set; } = new();
}

public class DigitalSignatureSettings
{
    public string CertificatePath { get; set; } = string.Empty;
    public string CertificatePassword { get; set; } = string.Empty;
    public string Algorithm { get; set; } = "SHA256withRSA";
}

public class CbnApiSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60;
}

public class NfiuApiSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 120;
    public bool BatchMode { get; set; } = true;
}

public class RegulatorApiEndpoint
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60;
}

public class StatusPollingSettings
{
    public int IntervalSeconds { get; set; } = 300;
    public int BatchSize { get; set; } = 20;
}
