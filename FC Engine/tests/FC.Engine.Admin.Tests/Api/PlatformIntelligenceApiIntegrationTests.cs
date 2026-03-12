using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FC.Engine.Admin.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FC.Engine.Admin.Tests.Api;

public sealed class PlatformIntelligenceApiIntegrationTests : IClassFixture<PlatformIntelligenceApiWebApplicationFactory>
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true
    };

    private readonly PlatformIntelligenceApiWebApplicationFactory _factory;

    public PlatformIntelligenceApiIntegrationTests(PlatformIntelligenceApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Overview_Requires_Authentication()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/api/intelligence/overview");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Overview_Returns_Workspace_Snapshot()
    {
        using var client = _factory.CreateAuthenticatedClient("Admin", "PlatformAdmin");

        var response = await client.GetAsync("/api/intelligence/overview");
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, payload);
        using var json = JsonDocument.Parse(payload, JsonOptions);
        TryGetProperty(json.RootElement, "generatedAt", out var generatedAt).Should().BeTrue();
        generatedAt.ValueKind.Should().Be(JsonValueKind.String);
        TryGetProperty(json.RootElement, "knowledgeGraph", out _).Should().BeTrue();
        TryGetProperty(json.RootElement, "capital", out _).Should().BeTrue();
        TryGetProperty(json.RootElement, "sanctions", out _).Should().BeTrue();
        TryGetProperty(json.RootElement, "resilience", out _).Should().BeTrue();
        TryGetProperty(json.RootElement, "modelRisk", out _).Should().BeTrue();
        TryGetProperty(json.RootElement, "refresh", out _).Should().BeTrue();
    }

    [Fact]
    public async Task KnowledgeCatalog_Returns_Persisted_Graph_State()
    {
        using var client = _factory.CreateAuthenticatedClient("Admin");

        var response = await client.GetAsync("/api/intelligence/knowledge/catalog");
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, payload);
        using var json = JsonDocument.Parse(payload, JsonOptions);
        TryGetProperty(json.RootElement, "nodeCount", out var nodeCount).Should().BeTrue();
        TryGetProperty(json.RootElement, "edgeCount", out var edgeCount).Should().BeTrue();
        TryGetProperty(json.RootElement, "nodeTypes", out var nodeTypes).Should().BeTrue();
        TryGetProperty(json.RootElement, "edgeTypes", out var edgeTypes).Should().BeTrue();
        nodeCount.GetInt32().Should().BeGreaterThanOrEqualTo(0);
        edgeCount.GetInt32().Should().BeGreaterThanOrEqualTo(0);
        nodeTypes.ValueKind.Should().Be(JsonValueKind.Array);
        edgeTypes.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task CapitalActionCatalog_Returns_Seeded_Templates()
    {
        using var client = _factory.CreateAuthenticatedClient("Admin");

        var response = await client.GetAsync("/api/intelligence/capital/action-catalog");
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, payload);
        using var json = JsonDocument.Parse(payload, JsonOptions);
        TryGetProperty(json.RootElement, "rows", out var rows).Should().BeTrue();
        rows.ValueKind.Should().Be(JsonValueKind.Array);
        rows.GetArrayLength().Should().BeGreaterThan(0);
        rows.EnumerateArray()
            .Select(item => item.GetProperty("code").GetString())
            .Should()
            .Contain("COLLATERAL")
            .And.Contain("ISSUANCE");
    }

    [Fact]
    public async Task SanctionsCatalogSources_Returns_Baseline_Sources()
    {
        using var client = _factory.CreateAuthenticatedClient("Admin");

        var response = await client.GetAsync("/api/intelligence/sanctions/catalog/sources");
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, payload);
        using var json = JsonDocument.Parse(payload, JsonOptions);
        TryGetProperty(json.RootElement, "rows", out var rows).Should().BeTrue();
        rows.ValueKind.Should().Be(JsonValueKind.Array);
        rows.GetArrayLength().Should().BeGreaterThan(0);
        var sourceCodes = rows.EnumerateArray()
            .Select(item => item.GetProperty("sourceCode").GetString())
            .ToList();
        sourceCodes.Should().Contain("UN");
        sourceCodes.Should().Contain("OFAC");
        sourceCodes.Should().Contain("NFIU");
    }

    [Fact]
    public async Task ModelRiskCatalog_Returns_Governed_Model_Definitions()
    {
        using var client = _factory.CreateAuthenticatedClient("Admin");

        var response = await client.GetAsync("/api/intelligence/model-risk/catalog");
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, payload);
        using var json = JsonDocument.Parse(payload, JsonOptions);
        TryGetProperty(json.RootElement, "rows", out var rows).Should().BeTrue();
        rows.ValueKind.Should().Be(JsonValueKind.Array);
        rows.GetArrayLength().Should().BeGreaterThan(0);
        var modelCodes = rows.EnumerateArray()
            .Select(item => item.GetProperty("modelCode").GetString())
            .ToList();
        modelCodes.Should().Contain("ECL");
        modelCodes.Should().Contain("CAR");
        modelCodes.Should().Contain("STRESS");
    }

    [Fact]
    public async Task Rollout_Reconcile_Requires_Platform_Admin_Role()
    {
        using var client = _factory.CreateAuthenticatedClient("Admin");

        var response = await client.PostAsJsonAsync(
            "/api/intelligence/rollout/reconcile",
            new { tenantIds = new[] { Guid.NewGuid() } });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        return element.TryGetProperty(name, out value)
               || element.TryGetProperty(char.ToUpperInvariant(name[0]) + name[1..], out value);
    }
}
