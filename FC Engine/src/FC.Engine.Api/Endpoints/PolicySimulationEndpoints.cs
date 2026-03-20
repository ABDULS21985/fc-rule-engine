using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Api.Endpoints;

public static class PolicySimulationEndpoints
{
    public static IEndpointRouteBuilder MapPolicySimulationEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Regulator-facing endpoints ─────────────────────────────────
        var regulator = app.MapGroup("/regulator/policies")
            .RequireAuthorization("RegulatorApi")
            .WithTags("Policy Simulation — Regulator");

        regulator.MapPost("/scenarios", async (
            [FromBody] CreateScenarioRequest req,
            IPolicyScenarioService svc,
            HttpContext ctx, CancellationToken ct) =>
        {
            var regulatorId = GetRegulatorId(ctx);
            if (regulatorId == 0) return Results.Forbid();
            var userId = GetUserId(ctx);
            if (userId == 0) return Results.Forbid();
            var id = await svc.CreateScenarioAsync(
                regulatorId, req.Title, req.Description, req.Domain,
                req.TargetEntityTypes, req.BaselineDate, userId, ct);
            return Results.Created($"/api/v1/regulator/policies/scenarios/{id}", new { id });
        }).WithName("CreatePolicyScenario")
          .WithSummary("Create a new policy scenario for what-if modelling");

        regulator.MapGet("/scenarios", async (
            IPolicyScenarioService svc,
            HttpContext ctx,
            [FromQuery] PolicyDomain? domain,
            [FromQuery] PolicyStatus? status,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken ct = default) =>
        {
            var regulatorId = GetRegulatorId(ctx);
            if (regulatorId == 0) return Results.Forbid();
            var result = await svc.ListScenariosAsync(regulatorId, domain, status, page, pageSize, ct);
            return Results.Ok(result);
        }).WithName("ListPolicyScenarios")
          .WithSummary("List policy scenarios with optional filtering");

        regulator.MapGet("/scenarios/{scenarioId:long}", async (
            long scenarioId,
            IPolicyScenarioService svc,
            HttpContext ctx, CancellationToken ct) =>
        {
            var regulatorId = GetRegulatorId(ctx);
            if (regulatorId == 0) return Results.Forbid();
            var result = await svc.GetScenarioAsync(scenarioId, regulatorId, ct);
            return Results.Ok(result);
        }).WithName("GetPolicyScenario")
          .WithSummary("Get detailed policy scenario with parameters and run history");

        regulator.MapPost("/scenarios/{scenarioId:long}/parameters", async (
            long scenarioId,
            [FromBody] AddParameterRequest req,
            IPolicyScenarioService svc,
            HttpContext ctx, CancellationToken ct) =>
        {
            var regulatorId = GetRegulatorId(ctx);
            if (regulatorId == 0) return Results.Forbid();
            var userId = GetUserId(ctx);
            if (userId == 0) return Results.Forbid();
            await svc.AddParameterAsync(scenarioId, regulatorId, req.ParameterCode,
                req.ProposedValue, req.ApplicableEntityTypes, userId, ct);
            return Results.NoContent();
        }).WithName("AddPolicyParameter")
          .WithSummary("Add a parameter adjustment to a policy scenario");

        regulator.MapPut("/scenarios/{scenarioId:long}/parameters/{parameterCode}", async (
            long scenarioId,
            string parameterCode,
            [FromBody] UpdateParameterRequest req,
            IPolicyScenarioService svc,
            HttpContext ctx, CancellationToken ct) =>
        {
            var regulatorId = GetRegulatorId(ctx);
            var userId = GetUserId(ctx);
            await svc.UpdateParameterAsync(scenarioId, regulatorId, parameterCode,
                req.NewProposedValue, userId, ct);
            return Results.NoContent();
        }).WithName("UpdatePolicyParameter")
          .WithSummary("Update the proposed value for an existing parameter adjustment");

        regulator.MapPost("/scenarios/{scenarioId:long}/clone", async (
            long scenarioId,
            [FromBody] CloneScenarioRequest req,
            IPolicyScenarioService svc,
            HttpContext ctx, CancellationToken ct) =>
        {
            var regulatorId = GetRegulatorId(ctx);
            var userId = GetUserId(ctx);
            var newId = await svc.CloneScenarioAsync(scenarioId, regulatorId, req.NewTitle, userId, ct);
            return Results.Created($"/api/v1/regulator/policies/scenarios/{newId}", new { id = newId });
        }).WithName("ClonePolicyScenario")
          .WithSummary("Clone a scenario for variant modelling");

        regulator.MapPost("/scenarios/{scenarioId:long}/simulate", async (
            long scenarioId,
            IImpactAssessmentEngine engine,
            HttpContext ctx, CancellationToken ct) =>
        {
            var regulatorId = GetRegulatorId(ctx);
            if (regulatorId == 0) return Results.Forbid();
            var userId = GetUserId(ctx);
            if (userId == 0) return Results.Forbid();
            var result = await engine.RunAssessmentAsync(scenarioId, regulatorId, userId, ct);
            return Results.Ok(result);
        }).WithName("RunImpactAssessment")
          .WithSummary("Run a full sector impact assessment for the scenario");

        regulator.MapGet("/runs/{runId:long}", async (
            long runId,
            IImpactAssessmentEngine engine,
            HttpContext ctx, CancellationToken ct) =>
        {
            var regulatorId = GetRegulatorId(ctx);
            var result = await engine.GetRunResultAsync(runId, regulatorId, ct);
            return Results.Ok(result);
        }).WithName("GetImpactAssessmentRun")
          .WithSummary("Get results of a specific impact assessment run");

        regulator.MapGet("/runs/{runId:long}/entities", async (
            long runId,
            [AsParameters] EntityResultsQuery query,
            IImpactAssessmentEngine engine,
            HttpContext ctx, CancellationToken ct) =>
        {
            var regulatorId = GetRegulatorId(ctx);
            var results = await engine.GetEntityResultsAsync(
                runId, regulatorId, query.Category, query.EntityType,
                query.Page, query.PageSize, ct);
            return Results.Ok(results);
        }).WithName("GetEntityImpactResults")
          .WithSummary("Get per-entity impact results for a run with filtering and pagination");

        regulator.MapPost("/runs/compare", async (
            [FromBody] CompareRunsRequest req,
            IImpactAssessmentEngine engine,
            HttpContext ctx, CancellationToken ct) =>
        {
            var regulatorId = GetRegulatorId(ctx);
            var result = await engine.CompareRunsAsync(req.RunIds, regulatorId, ct);
            return Results.Ok(result);
        }).WithName("CompareScenarios")
          .WithSummary("Side-by-side comparison of multiple impact assessment runs");

        regulator.MapPost("/runs/{runId:long}/cba", async (
            long runId,
            ICostBenefitAnalyser cba,
            HttpContext ctx, CancellationToken ct) =>
        {
            var regulatorId = GetRegulatorId(ctx);
            var userId = GetUserId(ctx);
            var result = await cba.GenerateAnalysisAsync(runId, regulatorId, userId, ct);
            return Results.Ok(result);
        }).WithName("GenerateCBA")
          .WithSummary("Generate cost-benefit analysis with phase-in scenarios");

        regulator.MapGet("/scenarios/{scenarioId:long}/cba", async (
            long scenarioId,
            ICostBenefitAnalyser cba,
            HttpContext ctx, CancellationToken ct) =>
        {
            var regulatorId = GetRegulatorId(ctx);
            var result = await cba.GetAnalysisAsync(scenarioId, regulatorId, ct);
            return Results.Ok(result);
        }).WithName("GetCBA")
          .WithSummary("Get the latest cost-benefit analysis for a scenario");

        regulator.MapPost("/consultations", async (
            [FromBody] CreateConsultationRequest req,
            IConsultationService svc,
            HttpContext ctx, CancellationToken ct) =>
        {
            var regulatorId = GetRegulatorId(ctx);
            var userId = GetUserId(ctx);
            var id = await svc.CreateConsultationAsync(
                req.ScenarioId, regulatorId, req.Title, req.CoverNote,
                req.Deadline, req.Provisions, userId, ct);
            return Results.Created($"/api/v1/regulator/policies/consultations/{id}", new { id });
        }).WithName("CreateConsultation")
          .WithSummary("Create a draft consultation for industry feedback");

        regulator.MapGet("/consultations/{id:long}", async (
            long id,
            IConsultationService svc,
            HttpContext ctx, CancellationToken ct) =>
        {
            var regulatorId = GetRegulatorId(ctx);
            var result = await svc.GetConsultationAsync(id, regulatorId, ct);
            return Results.Ok(result);
        }).WithName("GetConsultation")
          .WithSummary("Get consultation details with provisions and aggregation data");

        regulator.MapPost("/consultations/{id:long}/publish", async (
            long id, IConsultationService svc,
            HttpContext ctx, CancellationToken ct) =>
        {
            await svc.PublishConsultationAsync(id, GetRegulatorId(ctx), GetUserId(ctx), ct);
            return Results.NoContent();
        }).WithName("PublishConsultation")
          .WithSummary("Publish a draft consultation for industry feedback");

        regulator.MapPost("/consultations/{id:long}/close", async (
            long id, IConsultationService svc,
            HttpContext ctx, CancellationToken ct) =>
        {
            await svc.CloseConsultationAsync(id, GetRegulatorId(ctx), GetUserId(ctx), ct);
            return Results.NoContent();
        }).WithName("CloseConsultation")
          .WithSummary("Close a consultation to stop accepting feedback");

        regulator.MapPost("/consultations/{id:long}/aggregate", async (
            long id, IConsultationService svc,
            HttpContext ctx, CancellationToken ct) =>
        {
            var result = await svc.AggregateFeedbackAsync(
                id, GetRegulatorId(ctx), GetUserId(ctx), ct);
            return Results.Ok(result);
        }).WithName("AggregateFeedback")
          .WithSummary("Aggregate all feedback for a closed consultation");

        regulator.MapPost("/decisions", async (
            [FromBody] RecordDecisionRequest req,
            IPolicyDecisionService svc,
            HttpContext ctx, CancellationToken ct) =>
        {
            var regulatorId = GetRegulatorId(ctx);
            var userId = GetUserId(ctx);
            var id = await svc.RecordDecisionAsync(
                req.ScenarioId, regulatorId, req.Decision, req.Summary,
                req.EffectiveDate, req.PhaseInMonths, req.CircularReference, userId, ct);
            return Results.Created($"/api/v1/regulator/policies/decisions/{id}", new { id });
        }).WithName("RecordPolicyDecision")
          .WithSummary("Record the final policy decision (enact, defer, withdraw)");

        regulator.MapGet("/decisions/{id:long}/document", async (
            long id, IPolicyDecisionService svc,
            HttpContext ctx, CancellationToken ct) =>
        {
            var doc = await svc.GeneratePolicyDocumentAsync(id, GetRegulatorId(ctx), ct);
            return Results.File(doc, "text/plain", "policy-document.txt");
        }).WithName("GeneratePolicyDocument")
          .WithSummary("Generate a formatted policy decision document");

        regulator.MapGet("/decisions/{id:long}/tracking", async (
            long id,
            IHistoricalImpactTracker tracker,
            HttpContext ctx, CancellationToken ct) =>
        {
            var history = await tracker.GetTrackingHistoryAsync(id, GetRegulatorId(ctx), ct);
            return Results.Ok(history);
        }).WithName("GetImpactTracking")
          .WithSummary("Get predicted vs actual impact tracking history");

        regulator.MapGet("/decisions/{id:long}/accuracy", async (
            long id,
            IHistoricalImpactTracker tracker,
            HttpContext ctx, CancellationToken ct) =>
        {
            var score = await tracker.GetAccuracyScoreAsync(id, GetRegulatorId(ctx), ct);
            return Results.Ok(new { decisionId = id, accuracyScore = score });
        }).WithName("GetAccuracyScore")
          .WithSummary("Get the latest accuracy score for a policy decision");

        regulator.MapGet("/parameter-presets", async (
            [FromQuery] PolicyDomain? domain,
            MetadataDbContext db,
            CancellationToken ct) =>
        {
            var query = db.Set<PolicyParameterPreset>().AsQueryable();
            if (domain.HasValue)
                query = query.Where(p => p.PolicyDomain == domain.Value);
            var presets = await query
                .OrderBy(p => p.PolicyDomain)
                .ThenBy(p => p.ParameterCode)
                .ToListAsync(ct);
            return Results.Ok(presets);
        }).WithName("GetParameterPresets")
          .WithSummary("List regulatory parameter presets, optionally filtered by domain");

        // ── Institution-facing endpoints ───────────────────────────────
        var institution = app.MapGroup("/institution/consultations")
            .RequireAuthorization("InstitutionApi")
            .WithTags("Policy Consultation — Institution");

        institution.MapGet("/", async (
            IConsultationService svc,
            HttpContext ctx, CancellationToken ct) =>
        {
            var institutionId = GetInstitutionId(ctx);
            if (institutionId == 0) return Results.Forbid();
            var consultations = await svc.GetOpenConsultationsAsync(institutionId, ct);
            return Results.Ok(consultations);
        }).WithName("ListOpenConsultations")
          .WithSummary("List open consultations available for feedback");

        institution.MapPost("/{consultationId:long}/feedback", async (
            long consultationId,
            [FromBody] SubmitFeedbackRequest req,
            IConsultationService svc,
            HttpContext ctx, CancellationToken ct) =>
        {
            var institutionId = GetInstitutionId(ctx);
            if (institutionId == 0) return Results.Forbid();
            var userId = GetUserId(ctx);
            if (userId == 0) return Results.Forbid();
            var feedbackId = await svc.SubmitFeedbackAsync(
                consultationId, institutionId, req.OverallPosition,
                req.GeneralComments, req.ProvisionFeedback, userId, ct);
            return Results.Created(
                $"/api/v1/institution/consultations/{consultationId}/feedback/{feedbackId}",
                new { feedbackId });
        }).WithName("SubmitConsultationFeedback")
          .WithSummary("Submit institutional feedback on a consultation");

        return app;
    }

    // ── Claim helpers ──────────────────────────────────────────────────

    /// <summary>Returns 0 when claim is missing — callers must check before using.</summary>
    private static int GetRegulatorId(HttpContext ctx) =>
        ApiClaimResolvers.GetRegulatorId(ctx.User);

    /// <summary>Returns 0 when claim is missing — callers must check before using.</summary>
    private static int GetUserId(HttpContext ctx) =>
        ApiClaimResolvers.GetUserId(ctx.User);

    /// <summary>Returns 0 when claim is missing — callers must check before using.</summary>
    private static int GetInstitutionId(HttpContext ctx) =>
        ApiClaimResolvers.GetInstitutionId(ctx.User);
}

public sealed record CloneScenarioRequest(string NewTitle);
public sealed record UpdateParameterRequest(decimal NewProposedValue);
