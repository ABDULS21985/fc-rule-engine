namespace FC.Engine.Domain.Abstractions;

/// <summary>
/// Adapter for extracting return data from core banking systems.
/// Implementations connect to specific platforms (Finacle, T24, BankOne, Flexcube).
/// </summary>
public interface ICoreBankingAdapter
{
    /// <summary>Unique adapter name, e.g. "Finacle", "T24", "BankOne", "Flexcube".</summary>
    string AdapterName { get; }

    /// <summary>
    /// Extracts return data from the core banking system and maps it to template fields.
    /// </summary>
    Task<CoreBankingExtractResult> ExtractReturnData(
        string moduleCode,
        string periodCode,
        CoreBankingConnectionConfig config,
        CancellationToken ct = default);

    /// <summary>Test connectivity to the core banking system.</summary>
    Task<bool> TestConnectionAsync(CoreBankingConnectionConfig config, CancellationToken ct = default);
}

/// <summary>Connection configuration for a core banking system.</summary>
public class CoreBankingConnectionConfig
{
    public string BaseUrl { get; set; } = string.Empty;
    public string? DatabaseConnectionString { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? ApiKey { get; set; }
    public Dictionary<string, string> AdditionalSettings { get; set; } = new();
}

/// <summary>Result of a core banking data extraction.</summary>
public class CoreBankingExtractResult
{
    public bool Success { get; set; }
    public Dictionary<string, object> FieldValues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public DateTime ExtractedAtUtc { get; set; } = DateTime.UtcNow;
}
