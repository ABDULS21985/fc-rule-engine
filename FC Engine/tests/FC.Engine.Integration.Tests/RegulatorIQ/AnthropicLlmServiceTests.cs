using System.Net;
using System.Text;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FC.Engine.Integration.Tests.RegulatorIQ;

public sealed class AnthropicLlmServiceTests
{
    [Fact]
    public async Task CompleteStructuredAsync_StripsMarkdownFences_AndDeserializesJson()
    {
        var handler = new QueueHttpMessageHandler(
        [
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"model":"claude-3-5-sonnet","content":[{"type":"text","text":"```json\n{\"intent\":\"ENTITY_PROFILE\",\"confidence\":0.91}\n```"}],"usage":{"input_tokens":55,"output_tokens":17}}""",
                    Encoding.UTF8,
                    "application/json")
            }
        ]);

        using var httpClient = new HttpClient(handler);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RegulatorIQ:Llm:ApiKey"] = "test-key",
                ["RegulatorIQ:Llm:Model"] = "claude-3-5-sonnet",
                ["RegulatorIQ:Llm:TimeoutSeconds"] = "30"
            })
            .Build();

        var service = new AnthropicLlmService(httpClient, configuration, NullLogger<AnthropicLlmService>.Instance);

        var parsed = await service.CompleteStructuredAsync<TestIntentEnvelope>(
            new LlmRequest
            {
                SystemPrompt = "Classify the regulator query.",
                UserMessage = "Give me a full profile of Access Bank"
            });

        parsed.Intent.Should().Be("ENTITY_PROFILE");
        parsed.Confidence.Should().Be(0.91m);
    }

    [Fact]
    public async Task CompleteAsync_RetriesOnRateLimit_ThenSucceeds()
    {
        var handler = new QueueHttpMessageHandler(
        [
            new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent("""{"error":{"message":"rate limited"}}""", Encoding.UTF8, "application/json")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"model":"claude-3-5-sonnet","content":[{"type":"text","text":"Sector average CAR is 17.75%."}],"usage":{"input_tokens":120,"output_tokens":28}}""",
                    Encoding.UTF8,
                    "application/json")
            }
        ]);

        using var httpClient = new HttpClient(handler);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RegulatorIQ:Llm:ApiKey"] = "test-key",
                ["RegulatorIQ:Llm:Model"] = "claude-3-5-sonnet",
                ["RegulatorIQ:Llm:TimeoutSeconds"] = "30"
            })
            .Build();

        var service = new AnthropicLlmService(httpClient, configuration, NullLogger<AnthropicLlmService>.Instance);

        var response = await service.CompleteAsync(
            new LlmRequest
            {
                SystemPrompt = "Answer as a regulator analyst.",
                UserMessage = "What is the latest sector average CAR?"
            });

        response.Success.Should().BeTrue();
        response.Content.Should().Be("Sector average CAR is 17.75%.");
        response.InputTokens.Should().Be(120);
        response.OutputTokens.Should().Be(28);
        handler.RequestCount.Should().Be(2);
    }

    private sealed class TestIntentEnvelope
    {
        public string Intent { get; set; } = string.Empty;
        public decimal Confidence { get; set; }
    }

    private sealed class QueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public QueueHttpMessageHandler(IEnumerable<HttpResponseMessage> responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No queued HTTP response remained for the test.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
