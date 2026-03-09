namespace FC.Engine.Domain.Abstractions;

public enum CoreBankingSystem { Finacle, T24, BankOne, Flexcube }

public interface ICoreBankingAdapterFactory
{
    ICoreBankingAdapter GetAdapter(CoreBankingSystem system);
}

public interface ICoreBankingAdapter
{
    CoreBankingSystem SystemType { get; }

    /// <summary>
    /// Extracts data from the core banking system for a given module and period,
    /// returning a field map keyed by RegOS module field codes.
    /// </summary>
    Task<CoreBankingExtractionResult> ExtractReturnDataAsync(
        string moduleCode,
        string periodCode,
        CoreBankingConnectionConfig config,
        CancellationToken ct = default);

    /// <summary>Tests connectivity to the core banking system.</summary>
    Task<ConnectionTestResult> TestConnectionAsync(
        CoreBankingConnectionConfig config,
        CancellationToken ct = default);
}

public sealed record CoreBankingConnectionConfig(
    string SystemType,
    string? BaseUrl,
    string? DatabaseServer,
    string Credential,           // decrypted from Key Vault at runtime
    string FieldMappingJson      // JSON: RegOS field code → CB query/field
);

public sealed record CoreBankingExtractionResult(
    bool Success,
    string ModuleCode,
    string PeriodCode,
    Dictionary<string, object?> ExtractedFields,
    IReadOnlyList<string> UnmappedFields,
    string? ErrorMessage,
    DateTimeOffset ExtractedAt);

public sealed record ConnectionTestResult(
    bool Success,
    string Message,
    long LatencyMs);
