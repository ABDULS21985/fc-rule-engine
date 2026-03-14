namespace FC.Engine.Domain.Abstractions;

public interface ILlmService
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);
    Task<T> CompleteStructuredAsync<T>(LlmRequest request, CancellationToken ct = default) where T : class;
}

public sealed class LlmRequest
{
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserMessage { get; set; } = string.Empty;
    public decimal Temperature { get; set; } = 0.0m;
    public int MaxTokens { get; set; } = 2000;
    public string? ResponseFormat { get; set; }
}

public sealed class LlmResponse
{
    public string Content { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public string Model { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
