using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Api.Endpoints;

public static class CrossBorderEndpoints
{
    public static void MapCrossBorderEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/cross-border")
            .WithTags("Cross-Border Harmonisation")
            .RequireAuthorization();

        // ── Jurisdictions ────────────────────────────────────────────

        group.MapGet("/jurisdictions", async (
            MetadataDbContext db,
            CancellationToken ct) =>
        {
            var jurisdictions = await db.RegulatoryJurisdictions
                .AsNoTracking()
                .Where(j => j.IsActive)
                .OrderBy(j => j.CountryName)
                .ToListAsync(ct);
            return Results.Ok(jurisdictions);
        })
        .WithName("GetJurisdictions")
        .WithSummary("List all regulatory jurisdictions.");

        // ── Equivalence Mappings ─────────────────────────────────────

        group.MapGet("/mappings", async (
            [FromQuery] string? conceptDomain,
            IEquivalenceMappingService service,
            CancellationToken ct) =>
        {
            var mappings = await service.ListMappingsAsync(conceptDomain, ct);
            return Results.Ok(mappings);
        })
        .WithName("ListEquivalenceMappings")
        .WithSummary("List all equivalence mappings.");

        group.MapGet("/mappings/{id:long}", async (
            long id,
            IEquivalenceMappingService service,
            CancellationToken ct) =>
        {
            var mapping = await service.GetMappingAsync(id, ct);
            return mapping is null ? Results.NotFound() : Results.Ok(mapping);
        })
        .WithName("GetEquivalenceMapping")
        .WithSummary("Get a single equivalence mapping with entries.");

        group.MapPost("/mappings", async (
            [FromBody] CreateMappingRequest request,
            IEquivalenceMappingService service,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var userId = GetUserId(ctx);
            var id = await service.CreateMappingAsync(
                request.MappingCode, request.MappingName, request.ConceptDomain,
                request.Description, request.Entries, userId, ct);
            return Results.Created($"/api/v1/cross-border/mappings/{id}", new { id });
        })
        .WithName("CreateEquivalenceMapping")
        .WithSummary("Create a new equivalence mapping with entries.");

        group.MapGet("/mappings/compare/{mappingCode}", async (
            string mappingCode,
            IEquivalenceMappingService service,
            CancellationToken ct) =>
        {
            var thresholds = await service.GetCrossBorderComparisonAsync(mappingCode, ct);
            return Results.Ok(thresholds);
        })
        .WithName("CompareJurisdictionThresholds")
        .WithSummary("Compare thresholds across jurisdictions for a mapping code.");

        group.MapPut("/mappings/{mappingId:long}/thresholds/{jurisdictionCode}", async (
            long mappingId,
            string jurisdictionCode,
            [FromBody] UpdateThresholdRequest request,
            IEquivalenceMappingService service,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var userId = GetUserId(ctx);
            await service.UpdateThresholdAsync(mappingId, jurisdictionCode, request.NewThreshold, userId, ct);
            return Results.NoContent();
        })
        .WithName("UpdateEquivalenceThreshold")
        .WithSummary("Update a threshold for a specific jurisdiction within a mapping.");

        // ── FX Rates ─────────────────────────────────────────────────

        group.MapGet("/fx-rates", async (
            [FromQuery] string baseCurrency,
            [FromQuery] string quoteCurrency,
            [FromQuery] DateOnly? rateDate,
            ICurrencyConversionEngine service,
            CancellationToken ct) =>
        {
            var date = rateDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var rate = await service.GetRateAsync(baseCurrency, quoteCurrency, date, ct: ct);
            return Results.Ok(new { baseCurrency, quoteCurrency, rateDate = date, rate });
        })
        .WithName("GetFxRate")
        .WithSummary("Get an FX rate for a currency pair on a specific date.");

        group.MapPost("/fx-rates", async (
            [FromBody] UpsertRateRequest request,
            ICurrencyConversionEngine service,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var userId = GetUserId(ctx);
            await service.UpsertRateAsync(
                request.BaseCurrency, request.QuoteCurrency, request.RateDate,
                request.Rate, request.RateSource, request.RateType, userId, ct);
            return Results.Ok(new { success = true });
        })
        .WithName("UpsertFxRate")
        .WithSummary("Insert or update an FX rate.");

        group.MapGet("/fx-rates/convert", async (
            [FromQuery] string from,
            [FromQuery] string to,
            [FromQuery] decimal amount,
            [FromQuery] DateOnly? rateDate,
            ICurrencyConversionEngine service,
            CancellationToken ct) =>
        {
            var date = rateDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var result = await service.ConvertAsync(amount, from, to, date, ct: ct);
            return Results.Ok(result);
        })
        .WithName("ConvertCurrency")
        .WithSummary("Convert an amount between currencies using the latest available rate.");

        group.MapGet("/fx-rates/history", async (
            [FromQuery] string baseCurrency,
            [FromQuery] string quoteCurrency,
            [FromQuery] DateOnly fromDate,
            [FromQuery] DateOnly toDate,
            ICurrencyConversionEngine service,
            CancellationToken ct) =>
        {
            var history = await service.GetRateHistoryAsync(baseCurrency, quoteCurrency, fromDate, toDate, ct);
            return Results.Ok(history);
        })
        .WithName("GetFxRateHistory")
        .WithSummary("Get FX rate history for a currency pair.");

        // ── Consolidation ────────────────────────────────────────────

        group.MapPost("/groups/{groupId:int}/consolidate", async (
            int groupId,
            [FromBody] ConsolidateRequest request,
            IConsolidationEngine engine,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var userId = GetUserId(ctx);
            var result = await engine.RunConsolidationAsync(
                groupId, request.ReportingPeriod, request.SnapshotDate, userId, ct);
            return Results.Created($"/api/v1/cross-border/groups/{groupId}/runs/{result.RunId}", result);
        })
        .WithName("RunConsolidation")
        .WithSummary("Execute a full consolidation run for a financial group.");

        group.MapGet("/groups/{groupId:int}/runs/{runId:long}", async (
            int groupId,
            long runId,
            IConsolidationEngine engine,
            CancellationToken ct) =>
        {
            var run = await engine.GetRunResultAsync(runId, groupId, ct);
            return run is null ? Results.NotFound() : Results.Ok(run);
        })
        .WithName("GetConsolidationRunDetail")
        .WithSummary("Get consolidation run result.");

        group.MapGet("/groups/{groupId:int}/runs/{runId:long}/snapshots", async (
            int groupId,
            long runId,
            IConsolidationEngine engine,
            CancellationToken ct) =>
        {
            var snapshots = await engine.GetSubsidiarySnapshotsAsync(runId, groupId, ct);
            return Results.Ok(snapshots);
        })
        .WithName("GetConsolidationSnapshots")
        .WithSummary("Get subsidiary snapshots for a consolidation run.");

        group.MapGet("/groups/{groupId:int}/runs/{runId:long}/adjustments", async (
            int groupId,
            long runId,
            IConsolidationEngine engine,
            CancellationToken ct) =>
        {
            var adjustments = await engine.GetAdjustmentsAsync(runId, groupId, ct);
            return Results.Ok(adjustments);
        })
        .WithName("GetConsolidationAdjustments")
        .WithSummary("Get consolidation adjustments for a run.");

        group.MapPost("/groups/{groupId:int}/runs/{runId:long}/adjustments", async (
            int groupId,
            long runId,
            [FromBody] ConsolidationAdjustmentInput input,
            IConsolidationEngine engine,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var userId = GetUserId(ctx);
            await engine.AddManualAdjustmentAsync(runId, groupId, input, userId, ct);
            return Results.Created($"/api/v1/cross-border/groups/{groupId}/runs/{runId}/adjustments", new { success = true });
        })
        .WithName("AddConsolidationAdjustment")
        .WithSummary("Add a manual consolidation adjustment to a run.");

        // ── Data Flows ───────────────────────────────────────────────

        group.MapGet("/groups/{groupId:int}/data-flows", async (
            int groupId,
            [FromQuery] string? sourceJurisdiction,
            [FromQuery] string? targetJurisdiction,
            ICrossBorderDataFlowEngine engine,
            CancellationToken ct) =>
        {
            var flows = await engine.ListFlowsAsync(groupId, sourceJurisdiction, targetJurisdiction, ct);
            return Results.Ok(flows);
        })
        .WithName("ListDataFlows")
        .WithSummary("List cross-border data flow definitions for a group.");

        group.MapPost("/groups/{groupId:int}/data-flows", async (
            int groupId,
            [FromBody] DataFlowDefinition definition,
            ICrossBorderDataFlowEngine engine,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var userId = GetUserId(ctx);
            var id = await engine.DefineFlowAsync(groupId, definition, userId, ct);
            return Results.Created($"/api/v1/cross-border/groups/{groupId}/data-flows/{id}", new { id });
        })
        .WithName("CreateDataFlow")
        .WithSummary("Create a new cross-border data flow definition.");

        group.MapPost("/groups/{groupId:int}/data-flows/execute", async (
            int groupId,
            [FromBody] ExecuteFlowsRequest request,
            ICrossBorderDataFlowEngine engine,
            CancellationToken ct) =>
        {
            var results = await engine.ExecuteFlowsAsync(groupId, request.ReportingPeriod, ct);
            return Results.Ok(results);
        })
        .WithName("ExecuteDataFlows")
        .WithSummary("Execute all active data flows for a group and reporting period.");

        // ── Divergence Detection ─────────────────────────────────────

        group.MapGet("/divergences", async (
            [FromQuery] string? conceptDomain,
            [FromQuery] string? minSeverity,
            IDivergenceDetectionService service,
            CancellationToken ct) =>
        {
            DivergenceSeverity? sevFilter = null;
            if (!string.IsNullOrWhiteSpace(minSeverity) && Enum.TryParse<DivergenceSeverity>(minSeverity, true, out var parsed))
                sevFilter = parsed;

            var divergences = await service.GetOpenDivergencesAsync(conceptDomain, sevFilter, ct);
            return Results.Ok(divergences);
        })
        .WithName("GetDivergences")
        .WithSummary("List open regulatory divergences.");

        group.MapPost("/divergences/detect", async (
            IDivergenceDetectionService service,
            CancellationToken ct) =>
        {
            var alerts = await service.DetectDivergencesAsync(ct);
            return Results.Ok(alerts);
        })
        .WithName("DetectDivergences")
        .WithSummary("Run divergence detection across all active equivalence mappings.");

        group.MapGet("/groups/{groupId:int}/divergences", async (
            int groupId,
            IDivergenceDetectionService service,
            CancellationToken ct) =>
        {
            var divergences = await service.GetGroupDivergencesAsync(groupId, ct);
            return Results.Ok(divergences);
        })
        .WithName("GetGroupDivergences")
        .WithSummary("Get divergences affecting a specific financial group.");

        group.MapPost("/divergences/{id:long}/acknowledge", async (
            long id,
            IDivergenceDetectionService service,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var userId = GetUserId(ctx);
            await service.AcknowledgeDivergenceAsync(id, userId, ct);
            return Results.NoContent();
        })
        .WithName("AcknowledgeDivergence")
        .WithSummary("Acknowledge a regulatory divergence.");

        group.MapPost("/divergences/{id:long}/resolve", async (
            long id,
            [FromBody] ResolveDivergenceRequest request,
            IDivergenceDetectionService service,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var userId = GetUserId(ctx);
            await service.ResolveDivergenceAsync(id, request.Resolution, userId, ct);
            return Results.NoContent();
        })
        .WithName("ResolveDivergence")
        .WithSummary("Mark a regulatory divergence as resolved.");

        group.MapPost("/divergences/{id:long}/notify", async (
            long id,
            IDivergenceDetectionService service,
            CancellationToken ct) =>
        {
            await service.NotifyGroupsAsync(id, ct);
            return Results.NoContent();
        })
        .WithName("NotifyGroupsOfDivergence")
        .WithSummary("Send divergence notifications to all affected financial groups.");

        // ── Pan-African Dashboard ────────────────────────────────────

        group.MapGet("/groups/{groupId:int}/overview", async (
            int groupId,
            IPanAfricanDashboardService service,
            CancellationToken ct) =>
        {
            var overview = await service.GetGroupOverviewAsync(groupId, ct);
            return overview is null ? Results.NotFound() : Results.Ok(overview);
        })
        .WithName("GetGroupComplianceOverview")
        .WithSummary("Get consolidated compliance overview for a financial group.");

        group.MapGet("/groups/{groupId:int}/compliance-snapshots", async (
            int groupId,
            [FromQuery] string? reportingPeriod,
            IPanAfricanDashboardService service,
            CancellationToken ct) =>
        {
            var snapshots = await service.GetSubsidiarySnapshotsAsync(groupId, reportingPeriod, ct);
            return Results.Ok(snapshots);
        })
        .WithName("GetSubsidiaryComplianceSnapshots")
        .WithSummary("Get subsidiary compliance snapshots for a group.");

        group.MapGet("/groups/{groupId:int}/deadlines", async (
            int groupId,
            [FromQuery] DateOnly fromDate,
            [FromQuery] DateOnly toDate,
            IPanAfricanDashboardService service,
            CancellationToken ct) =>
        {
            var deadlines = await service.GetDeadlineCalendarAsync(groupId, fromDate, toDate, ct);
            return Results.Ok(deadlines);
        })
        .WithName("GetDeadlineCalendar")
        .WithSummary("Get regulatory deadline calendar for a group's jurisdictions.");

        group.MapGet("/groups/{groupId:int}/risk-metrics", async (
            int groupId,
            [FromQuery] string reportingPeriod,
            IPanAfricanDashboardService service,
            CancellationToken ct) =>
        {
            var metrics = await service.GetConsolidatedRiskMetricsAsync(groupId, reportingPeriod, ct);
            return metrics is null ? Results.NotFound() : Results.Ok(metrics);
        })
        .WithName("GetConsolidatedRiskMetrics")
        .WithSummary("Get consolidated risk metrics for a group and reporting period.");

        // ── AfCFTA Protocol Tracking ─────────────────────────────────

        group.MapGet("/afcfta/protocols", async (
            IAfcftaTrackingService service,
            CancellationToken ct) =>
        {
            var protocols = await service.ListProtocolsAsync(ct);
            return Results.Ok(protocols);
        })
        .WithName("ListAfcftaProtocols")
        .WithSummary("List all AfCFTA financial services protocol tracking records.");

        group.MapGet("/afcfta/protocols/{protocolCode}", async (
            string protocolCode,
            IAfcftaTrackingService service,
            CancellationToken ct) =>
        {
            var protocol = await service.GetProtocolAsync(protocolCode, ct);
            return protocol is null ? Results.NotFound() : Results.Ok(protocol);
        })
        .WithName("GetAfcftaProtocol")
        .WithSummary("Get a specific AfCFTA protocol by code.");

        group.MapPut("/afcfta/protocols/{protocolCode}/status", async (
            string protocolCode,
            [FromBody] UpdateProtocolStatusRequest request,
            IAfcftaTrackingService service,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var userId = GetUserId(ctx);
            await service.UpdateProtocolStatusAsync(protocolCode, request.Status, userId, ct);
            return Results.NoContent();
        })
        .WithName("UpdateAfcftaProtocolStatus")
        .WithSummary("Update the status of an AfCFTA protocol.");
    }

    private static int GetUserId(HttpContext ctx) =>
        int.TryParse(ctx.User.FindFirst("sub")?.Value
            ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 1;
}

public sealed record UpdateProtocolStatusRequest(AfcftaProtocolStatus Status);
public sealed record UpdateThresholdRequest(decimal NewThreshold);
public sealed record ResolveDivergenceRequest(string Resolution);
