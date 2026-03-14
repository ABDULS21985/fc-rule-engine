using System.Net;
using System.Text;
using FC.Engine.Domain.Models;
using FluentAssertions;
using Xunit;

namespace FC.Engine.Integration.Tests.RegulatorIQ;

[Collection("RegulatorIqIntegration")]
public sealed class RegulatorIqAdminEndpointsIntegrationTests : IClassFixture<RegulatorIqFixture>
{
    private readonly RegulatorIqFixture _fixture;

    public RegulatorIqAdminEndpointsIntegrationTests(RegulatorIqFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ConversationExportEndpoint_RegulatorSession_ReturnsPdfOverHttp()
    {
        var orchestrator = _fixture.CreateOrchestrator();
        var result = await orchestrator.QueryAsync(new RegulatorIqQueryRequest
        {
            Query = "Compare Access Bank vs Zenith on CAR and NPL",
            RegulatorCode = "CBN",
            RegulatorId = "examiner-001",
            UserRole = "Examiner"
        });

        using var factory = new AdminRegulatorIqWebApplicationFactory(_fixture);
        using var client = factory.CreateAuthenticatedClient("examiner-001", _fixture.CbnRegulatorTenantId, "CBN", "Examiner");

        var response = await client.GetAsync($"/regulator/regulatoriq/conversations/{result.ConversationId:D}/export");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        bytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(bytes.Take(4).ToArray()).Should().Be("%PDF");
    }

    [Fact]
    public async Task ExaminationBriefingExportEndpoint_RegulatorSession_ReturnsPdfOverHttp()
    {
        using var factory = new AdminRegulatorIqWebApplicationFactory(_fixture);
        using var client = factory.CreateAuthenticatedClient("examiner-001", _fixture.CbnRegulatorTenantId, "CBN", "Examiner");

        var response = await client.GetAsync($"/regulator/regulatoriq/entities/{_fixture.AccessBankTenantId:D}/briefing/export");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        bytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(bytes.Take(4).ToArray()).Should().Be("%PDF");
    }

    [Fact]
    public async Task ConversationExportEndpoint_InstitutionTenant_IsForbidden()
    {
        var orchestrator = _fixture.CreateOrchestrator();
        var result = await orchestrator.QueryAsync(new RegulatorIqQueryRequest
        {
            Query = "Show filing status for Access Bank",
            RegulatorCode = "CBN",
            RegulatorId = "examiner-001",
            UserRole = "Examiner"
        });

        using var factory = new AdminRegulatorIqWebApplicationFactory(_fixture);
        using var client = factory.CreateAuthenticatedClient("examiner-001", _fixture.AccessBankTenantId, "CBN", "ComplianceOfficer");

        var response = await client.GetAsync($"/regulator/regulatoriq/conversations/{result.ConversationId:D}/export");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
