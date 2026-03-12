using System.Net;
using System.Net.Http.Json;
using System.IO.Compression;
using System.Text;
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
        TryGetProperty(json.RootElement, "hero", out _).Should().BeTrue();
        TryGetProperty(json.RootElement, "refresh", out _).Should().BeTrue();
        TryGetProperty(json.RootElement, "institutionCount", out var institutionCount).Should().BeTrue();
        TryGetProperty(json.RootElement, "interventionCount", out var interventionCount).Should().BeTrue();
        TryGetProperty(json.RootElement, "timelineCount", out var timelineCount).Should().BeTrue();
        institutionCount.GetInt32().Should().BeGreaterThanOrEqualTo(0);
        interventionCount.GetInt32().Should().BeGreaterThanOrEqualTo(0);
        timelineCount.GetInt32().Should().BeGreaterThanOrEqualTo(0);
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

    [Fact]
    public async Task Overview_Csv_Export_Returns_Downloadable_File()
    {
        using var client = _factory.CreateAuthenticatedClient("Admin", "PlatformAdmin");

        var response = await client.GetAsync("/api/intelligence/overview/export.csv");
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, payload);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");
        GetDownloadFileName(response).Should().Be("platform-intelligence-overview.csv");
        payload.Should().Contain("Section,Metric,Value,Commentary");
        payload.Should().Contain("Workspace");
        payload.Should().Contain("Refresh");
    }

    [Fact]
    public async Task Overview_Pdf_Export_Returns_Downloadable_File()
    {
        using var client = _factory.CreateAuthenticatedClient("Admin", "PlatformAdmin");

        var response = await client.GetAsync("/api/intelligence/overview/export.pdf");
        var payload = await response.Content.ReadAsByteArrayAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        GetDownloadFileName(response).Should().Be("platform-intelligence-board-brief.pdf");
        payload.Should().NotBeEmpty();
        Encoding.ASCII.GetString(payload.Take(5).ToArray()).Should().Be("%PDF-");
    }

    [Fact]
    public async Task Export_Bundle_Zip_Contains_Manifest_And_Core_Files()
    {
        using var client = _factory.CreateAuthenticatedClient("Admin", "PlatformAdmin");

        var response = await client.GetAsync("/api/intelligence/export-bundle.zip?lens=governor");
        var payload = await response.Content.ReadAsByteArrayAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/zip");
        GetDownloadFileName(response).Should().Be("platform-intelligence-bundle-governor.zip");

        using var stream = new MemoryStream(payload);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entryNames = archive.Entries.Select(x => x.FullName).ToList();
        entryNames.Should().Contain("manifest.json");
        entryNames.Should().Contain("platform-intelligence-overview.csv");
        entryNames.Should().Contain("platform-intelligence-board-brief.pdf");
        entryNames.Should().Contain("stakeholder-briefing-pack-governor.csv");
        entryNames.Should().Contain("stakeholder-briefing-pack-governor.pdf");
        entryNames.Should().Contain("knowledge-graph-dossier.csv");
        entryNames.Should().Contain("capital-supervisory-pack.csv");
        entryNames.Should().Contain("sanctions-supervisory-pack.csv");
        entryNames.Should().Contain("ops-resilience-pack.csv");
        entryNames.Should().Contain("model-risk-pack.csv");

        using var manifestStream = archive.GetEntry("manifest.json")!.Open();
        using var manifest = await JsonDocument.ParseAsync(manifestStream, JsonOptions);
        TryGetProperty(manifest.RootElement, "lens", out var lens).Should().BeTrue();
        TryGetProperty(manifest.RootElement, "files", out var files).Should().BeTrue();
        lens.GetString().Should().Be("governor");
        files.ValueKind.Should().Be(JsonValueKind.Array);
        files.GetArrayLength().Should().BeGreaterThanOrEqualTo(9);
    }

    [Fact]
    public async Task Dashboard_Briefing_Pack_Csv_Export_Returns_Downloadable_File()
    {
        using var client = _factory.CreateAuthenticatedClient("Admin", "PlatformAdmin");

        var response = await client.GetAsync("/api/intelligence/dashboards/briefing-pack/export.csv?lens=governor");
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, payload);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");
        GetDownloadFileName(response).Should().Be("stakeholder-briefing-pack-governor.csv");
        payload.Should().Contain("Section Code,Section Name,Coverage,Signal,Commentary,Recommended Action,Materialized At");
    }

    [Fact]
    public async Task Dashboard_Briefing_Pack_Export_Rejects_Executive_Lens_Without_Institution()
    {
        using var client = _factory.CreateAuthenticatedClient("Admin", "PlatformAdmin");

        var response = await client.GetAsync("/api/intelligence/dashboards/briefing-pack/export.csv?lens=executive");
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, payload);
        using var json = JsonDocument.Parse(payload, JsonOptions);
        TryGetProperty(json.RootElement, "error", out var error).Should().BeTrue();
        error.GetString().Should().Be("InstitutionId is required for the executive lens.");
    }

    [Fact]
    public async Task Sanctions_Screening_Run_Returns_Matches_And_Persists_Latest_Session()
    {
        using var client = _factory.CreateAuthenticatedClient("Admin", "PlatformAdmin");

        var response = await client.PostAsJsonAsync(
            "/api/intelligence/sanctions/screen",
            new
            {
                subjects = new[] { "AL-QAIDA" },
                thresholdPercent = 86d
            });
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, payload);
        using var json = JsonDocument.Parse(payload, JsonOptions);
        TryGetProperty(json.RootElement, "totalSubjects", out var totalSubjects).Should().BeTrue();
        TryGetProperty(json.RootElement, "matchCount", out var matchCount).Should().BeTrue();
        TryGetProperty(json.RootElement, "results", out var results).Should().BeTrue();
        totalSubjects.GetInt32().Should().Be(1);
        matchCount.GetInt32().Should().BeGreaterThan(0);
        results.ValueKind.Should().Be(JsonValueKind.Array);
        results.GetArrayLength().Should().BeGreaterThan(0);

        var sessionResponse = await client.GetAsync("/api/intelligence/sanctions/session");
        var sessionPayload = await sessionResponse.Content.ReadAsStringAsync();

        sessionResponse.StatusCode.Should().Be(HttpStatusCode.OK, sessionPayload);
        using var sessionJson = JsonDocument.Parse(sessionPayload, JsonOptions);
        TryGetProperty(sessionJson.RootElement, "latestRun", out var latestRun).Should().BeTrue();
        latestRun.ValueKind.Should().Be(JsonValueKind.Object);
        TryGetProperty(latestRun, "totalSubjects", out var persistedTotalSubjects).Should().BeTrue();
        TryGetProperty(latestRun, "matchCount", out var persistedMatchCount).Should().BeTrue();
        TryGetProperty(latestRun, "results", out var persistedResults).Should().BeTrue();
        persistedTotalSubjects.GetInt32().Should().Be(1);
        persistedMatchCount.GetInt32().Should().Be(matchCount.GetInt32());
        persistedResults.EnumerateArray()
            .Select(x => x.GetProperty("matchedName").GetString())
            .Should()
            .Contain(name => string.Equals(name, "AL-QAIDA", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Resilience_Self_Assessment_Can_Be_Round_Tripped_And_Reset()
    {
        using var client = _factory.CreateAuthenticatedClient("Admin", "PlatformAdmin");
        var originalResponses = await LoadResilienceResponsesAsync(client);
        var questionId = $"integration-{Guid.NewGuid():N}";

        try
        {
            var postResponse = await client.PostAsJsonAsync(
                "/api/intelligence/resilience/self-assessment",
                new
                {
                    questionId,
                    domain = "Testing cadence",
                    prompt = "Integration coverage posture",
                    score = 4
                });
            var postPayload = await postResponse.Content.ReadAsStringAsync();

            postResponse.StatusCode.Should().Be(HttpStatusCode.OK, postPayload);

            var updatedResponses = await LoadResilienceResponsesAsync(client);
            updatedResponses.Should().ContainSingle(x =>
                x.QuestionId == questionId
                && x.Domain == "Testing cadence"
                && x.Prompt == "Integration coverage posture"
                && x.Score == 4);

            var resetResponse = await client.DeleteAsync("/api/intelligence/resilience/self-assessment");
            var resetPayload = await resetResponse.Content.ReadAsStringAsync();

            resetResponse.StatusCode.Should().Be(HttpStatusCode.OK, resetPayload);
            var responsesAfterReset = await LoadResilienceResponsesAsync(client);
            responsesAfterReset.Should().BeEmpty();
        }
        finally
        {
            var cleanupResponse = await client.DeleteAsync("/api/intelligence/resilience/self-assessment");
            cleanupResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            foreach (var response in originalResponses)
            {
                var restoreResponse = await client.PostAsJsonAsync(
                    "/api/intelligence/resilience/self-assessment",
                    new
                    {
                        response.QuestionId,
                        response.Domain,
                        response.Prompt,
                        response.Score
                    });

                restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            }
        }
    }

    [Fact]
    public async Task Model_Risk_Approval_Workflow_Can_Be_Recorded_And_Read_Back()
    {
        using var client = _factory.CreateAuthenticatedClient("Admin", "PlatformAdmin");
        var workflowKey = $"integration-workflow-{Guid.NewGuid():N}";

        var postResponse = await client.PostAsJsonAsync(
            "/api/intelligence/model-risk/approval-workflow",
            new
            {
                workflowKey,
                modelCode = "INTTEST",
                modelName = "Integration Workflow Model",
                artifact = "integration-api-artifact",
                previousStage = "Model Owner",
                stage = "Validation Team"
            });
        var postPayload = await postResponse.Content.ReadAsStringAsync();

        postResponse.StatusCode.Should().Be(HttpStatusCode.OK, postPayload);

        var getResponse = await client.GetAsync("/api/intelligence/model-risk/approval-workflow");
        var getPayload = await getResponse.Content.ReadAsStringAsync();

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK, getPayload);
        using var json = JsonDocument.Parse(getPayload, JsonOptions);
        TryGetProperty(json.RootElement, "stages", out var stages).Should().BeTrue();
        TryGetProperty(json.RootElement, "auditTrail", out var auditTrail).Should().BeTrue();

        stages.EnumerateArray()
            .Should()
            .Contain(item =>
                string.Equals(item.GetProperty("workflowKey").GetString(), workflowKey, StringComparison.Ordinal)
                && string.Equals(item.GetProperty("modelCode").GetString(), "INTTEST", StringComparison.Ordinal)
                && string.Equals(item.GetProperty("stage").GetString(), "Validation Team", StringComparison.Ordinal));

        auditTrail.EnumerateArray()
            .Should()
            .Contain(item =>
                string.Equals(item.GetProperty("workflowKey").GetString(), workflowKey, StringComparison.Ordinal)
                && string.Equals(item.GetProperty("previousStage").GetString(), "Model Owner", StringComparison.Ordinal)
                && string.Equals(item.GetProperty("stage").GetString(), "Validation Team", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Export_Activity_Returns_Recent_Overview_Exports()
    {
        using var client = _factory.CreateAuthenticatedClient("Admin", "PlatformAdmin");

        var exportResponse = await client.GetAsync("/api/intelligence/overview/export.csv");
        exportResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var activityResponse = await client.GetAsync("/api/intelligence/exports/activity?area=Overview&format=csv&take=5");
        var payload = await activityResponse.Content.ReadAsStringAsync();

        activityResponse.StatusCode.Should().Be(HttpStatusCode.OK, payload);
        using var json = JsonDocument.Parse(payload, JsonOptions);
        TryGetProperty(json.RootElement, "total", out var total).Should().BeTrue();
        TryGetProperty(json.RootElement, "rows", out var rows).Should().BeTrue();
        total.GetInt32().Should().BeGreaterThan(0);
        rows.ValueKind.Should().Be(JsonValueKind.Array);
        rows.EnumerateArray()
            .Should()
            .Contain(item =>
                string.Equals(item.GetProperty("area").GetString(), "Overview", StringComparison.Ordinal)
                && string.Equals(item.GetProperty("format").GetString(), "csv", StringComparison.Ordinal));
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        return element.TryGetProperty(name, out value)
               || element.TryGetProperty(char.ToUpperInvariant(name[0]) + name[1..], out value);
    }

    private static string? GetDownloadFileName(HttpResponseMessage response)
    {
        return response.Content.Headers.ContentDisposition?.FileNameStar?.Trim('"')
               ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
    }

    private static async Task<List<ResilienceResponseDto>> LoadResilienceResponsesAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/intelligence/resilience/self-assessment");
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, payload);
        using var json = JsonDocument.Parse(payload, JsonOptions);
        TryGetProperty(json.RootElement, "responses", out var responses).Should().BeTrue();
        responses.ValueKind.Should().Be(JsonValueKind.Array);

        return responses.EnumerateArray()
            .Select(item => new ResilienceResponseDto(
                item.GetProperty("questionId").GetString() ?? string.Empty,
                item.GetProperty("domain").GetString() ?? string.Empty,
                item.GetProperty("prompt").GetString() ?? string.Empty,
                item.GetProperty("score").GetInt32()))
            .ToList();
    }

    private sealed record ResilienceResponseDto(
        string QuestionId,
        string Domain,
        string Prompt,
        int Score);
}
