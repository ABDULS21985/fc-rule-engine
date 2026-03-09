using System.Security.Claims;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;

namespace FC.Engine.Api.Endpoints;

public static class StressTestEndpoints
{
    public static void MapStressTestEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/stress-test")
            .WithTags("Stress Testing")
            .RequireAuthorization();

        // GET /stress-test/scenarios — List available stress test scenarios
        group.MapGet("/scenarios", (IStressTestService stressTestService) =>
        {
            var scenarios = stressTestService.GetAvailableScenarios();
            return Results.Ok(scenarios);
        })
        .Produces<List<StressScenarioInfo>>()
        .WithName("ListStressScenarios")
        .WithSummary("List available stress test scenarios with default parameters");

        // POST /stress-test/run — Run a sector-wide stress test
        group.MapPost("/run", async (
            StressTestRequest request,
            ClaimsPrincipal principal,
            IStressTestService stressTestService,
            CancellationToken ct) =>
        {
            var regulatorCode = principal.FindFirst("RegulatorCode")?.Value;
            if (string.IsNullOrWhiteSpace(regulatorCode))
            {
                return Results.BadRequest(new { error = "RegulatorCode claim is required. This endpoint is for regulator use only." });
            }

            if (request.ScenarioType == StressScenarioType.Custom && request.CustomParameters is null)
            {
                return Results.BadRequest(new { error = "CustomParameters are required when ScenarioType is Custom." });
            }

            var report = await stressTestService.RunStressTestAsync(regulatorCode, request, ct);
            return Results.Ok(report);
        })
        .Produces<StressTestReport>()
        .WithName("RunStressTest")
        .WithSummary("Run a sector-wide stress test with specified scenario");

        // POST /stress-test/report/pdf — Generate stress test report as PDF
        group.MapPost("/report/pdf", async (
            StressTestRequest request,
            ClaimsPrincipal principal,
            IStressTestService stressTestService,
            CancellationToken ct) =>
        {
            var regulatorCode = principal.FindFirst("RegulatorCode")?.Value;
            if (string.IsNullOrWhiteSpace(regulatorCode))
            {
                return Results.BadRequest(new { error = "RegulatorCode claim is required." });
            }

            var report = await stressTestService.RunStressTestAsync(regulatorCode, request, ct);
            var pdf = await stressTestService.GenerateReportPdfAsync(regulatorCode, report, ct);

            var fileName = $"stress-test-{request.ScenarioType}-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
            return Results.File(pdf, "application/pdf", fileName);
        })
        .Produces(200, contentType: "application/pdf")
        .WithName("GenerateStressTestPdf")
        .WithSummary("Run stress test and generate branded PDF report");
    }
}
