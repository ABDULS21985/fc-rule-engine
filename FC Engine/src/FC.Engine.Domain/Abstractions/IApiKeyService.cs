namespace FC.Engine.Domain.Abstractions;

public interface IApiKeyService
{
    Task<ApiKeyCreateResult> CreateApiKey(
        Guid tenantId,
        int createdByUserId,
        CreateApiKeyRequest request,
        CancellationToken ct = default);

    Task<ApiKeyValidationResult?> ValidateApiKey(
        string rawKey,
        string? ipAddress,
        CancellationToken ct = default);
}

public class CreateApiKeyRequest
{
    public string Description { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
    public int? RateLimitPerMinute { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class ApiKeyCreateResult
{
    public int ApiKeyId { get; set; }
    public string RawKey { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class ApiKeyValidationResult
{
    public int ApiKeyId { get; set; }
    public Guid TenantId { get; set; }
    public IReadOnlyList<string> Permissions { get; set; } = Array.Empty<string>();
    public int RateLimitPerMinute { get; set; }
}
