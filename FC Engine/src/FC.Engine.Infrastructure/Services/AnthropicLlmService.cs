using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FC.Engine.Domain.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed class AnthropicLlmService : ILlmService
{
    private const string DefaultEndpoint = "https://api.anthropic.com/v1/messages";
    private const string DefaultAnthropicVersion = "2023-06-01";
    private const int MaxRetries = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AnthropicLlmService> _logger;

    public AnthropicLlmService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<AnthropicLlmService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        if (TryGetInt(["RegulatorIQ:Llm:TimeoutSeconds", "Anthropic:TimeoutSeconds"], out var timeoutSeconds) && timeoutSeconds > 0)
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        }
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var model = GetSetting(["RegulatorIQ:Llm:Model", "Anthropic:Model"]);
        var apiKey = GetSetting(["RegulatorIQ:Llm:ApiKey", "Anthropic:ApiKey"]);

        if (string.IsNullOrWhiteSpace(model))
        {
            return Failure("RegulatorIQ LLM model is not configured.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Failure("RegulatorIQ LLM API key is not configured.");
        }

        var payload = BuildPayload(request, model);
        var endpoint = GetSetting(["RegulatorIQ:Llm:Endpoint", "Anthropic:Endpoint"]) ?? DefaultEndpoint;
        var version = GetSetting(["RegulatorIQ:Llm:AnthropicVersion", "Anthropic:Version"]) ?? DefaultAnthropicVersion;

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
            };

            httpRequest.Headers.Add("x-api-key", apiKey);
            httpRequest.Headers.Add("anthropic-version", version);
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            try
            {
                using var response = await _httpClient.SendAsync(httpRequest, ct);
                var responseBody = await response.Content.ReadAsStringAsync(ct);

                if (response.IsSuccessStatusCode)
                {
                    var parsed = JsonSerializer.Deserialize<AnthropicMessageResponse>(responseBody, JsonOptions);
                    var content = string.Concat(parsed?.Content?.Where(x => x.Type == "text").Select(x => x.Text) ?? []);

                    var llmResponse = new LlmResponse
                    {
                        Content = string.Equals(request.ResponseFormat, "json", StringComparison.OrdinalIgnoreCase)
                            ? StripMarkdownFences(content)
                            : content.Trim(),
                        InputTokens = parsed?.Usage?.InputTokens ?? 0,
                        OutputTokens = parsed?.Usage?.OutputTokens ?? 0,
                        Model = parsed?.Model ?? model,
                        Success = true
                    };

                    _logger.LogInformation(
                        "Anthropic completion succeeded for model {Model}. input_tokens={InputTokens}, output_tokens={OutputTokens}",
                        llmResponse.Model,
                        llmResponse.InputTokens,
                        llmResponse.OutputTokens);

                    return llmResponse;
                }

                var errorMessage = TryExtractErrorMessage(responseBody)
                    ?? $"Anthropic returned HTTP {(int)response.StatusCode}.";

                if (ShouldRetry(response.StatusCode) && attempt < MaxRetries)
                {
                    var delay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));
                    _logger.LogWarning(
                        "Anthropic completion retry {Attempt}/{MaxRetries} after HTTP {StatusCode}: {ErrorMessage}",
                        attempt,
                        MaxRetries,
                        (int)response.StatusCode,
                        errorMessage);
                    await Task.Delay(delay, ct);
                    continue;
                }

                _logger.LogWarning(
                    "Anthropic completion failed with HTTP {StatusCode}: {ErrorMessage}",
                    (int)response.StatusCode,
                    errorMessage);

                return Failure(errorMessage, model);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt < MaxRetries)
            {
                var delay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));
                _logger.LogWarning(
                    "Anthropic completion timed out on attempt {Attempt}/{MaxRetries}; retrying after {DelayMs}ms.",
                    attempt,
                    MaxRetries,
                    delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                var delay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));
                _logger.LogWarning(
                    ex,
                    "Anthropic completion failed on attempt {Attempt}/{MaxRetries}; retrying after {DelayMs}ms.",
                    attempt,
                    MaxRetries,
                    delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Anthropic completion failed unexpectedly.");
                return Failure(ex.Message, model);
            }
        }

        return Failure("Anthropic completion failed after the configured retries.", model);
    }

    public async Task<T> CompleteStructuredAsync<T>(LlmRequest request, CancellationToken ct = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(request);

        var effectiveRequest = new LlmRequest
        {
            SystemPrompt = request.SystemPrompt,
            UserMessage = request.UserMessage,
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens,
            ResponseFormat = "json"
        };

        var response = await CompleteAsync(effectiveRequest, ct);
        if (!response.Success)
        {
            throw new InvalidOperationException(response.ErrorMessage ?? "Structured LLM completion failed.");
        }

        var cleaned = StripMarkdownFences(response.Content);
        var parsed = JsonSerializer.Deserialize<T>(cleaned, JsonOptions);
        if (parsed is null)
        {
            throw new JsonException($"Anthropic returned an empty or invalid JSON payload for {typeof(T).Name}.");
        }

        return parsed;
    }

    private AnthropicMessageRequest BuildPayload(LlmRequest request, string model)
    {
        var systemPrompt = request.SystemPrompt?.Trim() ?? string.Empty;
        if (string.Equals(request.ResponseFormat, "json", StringComparison.OrdinalIgnoreCase))
        {
            systemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
                ? "Return valid JSON only. Do not wrap the payload in markdown code fences."
                : $"{systemPrompt}\n\nReturn valid JSON only. Do not wrap the payload in markdown code fences.";
        }

        return new AnthropicMessageRequest
        {
            Model = model,
            MaxTokens = request.MaxTokens,
            Temperature = request.Temperature,
            System = systemPrompt,
            Messages =
            [
                new AnthropicMessage
                {
                    Role = "user",
                    Content = request.UserMessage
                }
            ]
        };
    }

    private string? GetSetting(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            var value = _configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private bool TryGetInt(IEnumerable<string> keys, out int value)
    {
        foreach (var key in keys)
        {
            if (int.TryParse(_configuration[key], out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.TooManyRequests || (int)statusCode == 529;
    }

    private static string StripMarkdownFences(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var lines = trimmed.Split('\n').ToList();
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        lines.RemoveAt(0);
        if (lines.Count > 0 && lines[^1].Trim() == "```")
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join('\n', lines).Trim();
    }

    private static string? TryExtractErrorMessage(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<AnthropicErrorEnvelope>(responseBody, JsonOptions);
            return parsed?.Error?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static LlmResponse Failure(string errorMessage, string model = "")
    {
        return new LlmResponse
        {
            Success = false,
            Model = model,
            ErrorMessage = errorMessage
        };
    }

    private sealed class AnthropicMessageRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("temperature")]
        public decimal Temperature { get; set; }

        [JsonPropertyName("system")]
        public string System { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<AnthropicMessage> Messages { get; set; } = new();
    }

    private sealed class AnthropicMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user";

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private sealed class AnthropicMessageResponse
    {
        [JsonPropertyName("content")]
        public List<AnthropicContentBlock>? Content { get; set; }

        [JsonPropertyName("usage")]
        public AnthropicUsage? Usage { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }
    }

    private sealed class AnthropicContentBlock
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    private sealed class AnthropicUsage
    {
        [JsonPropertyName("input_tokens")]
        public int InputTokens { get; set; }

        [JsonPropertyName("output_tokens")]
        public int OutputTokens { get; set; }
    }

    private sealed class AnthropicErrorEnvelope
    {
        [JsonPropertyName("error")]
        public AnthropicError? Error { get; set; }
    }

    private sealed class AnthropicError
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
