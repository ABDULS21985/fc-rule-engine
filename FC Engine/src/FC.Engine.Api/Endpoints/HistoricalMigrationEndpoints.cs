using System.Security.Claims;
using FC.Engine.Domain.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace FC.Engine.Api.Endpoints;

public static class HistoricalMigrationEndpoints
{
    public static void MapHistoricalMigrationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/migration").WithTags("Historical Migration");

        group.MapGet("/jobs", async (
            [FromQuery] int? institutionId,
            IHistoricalMigrationService migrationService,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            var jobs = await migrationService.GetJobs(tenantContext.CurrentTenantId.Value, institutionId, ct);
            return Results.Ok(jobs);
        })
        .RequireAuthorization("CanCreateSubmission")
        .WithSummary("List historical migration jobs for the current tenant.");

        group.MapGet("/jobs/{importJobId:int}", async (
            int importJobId,
            IHistoricalMigrationService migrationService,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            var job = await migrationService.GetJob(tenantContext.CurrentTenantId.Value, importJobId, ct);
            return job is null ? Results.NotFound() : Results.Ok(job);
        })
        .RequireAuthorization("CanCreateSubmission")
        .WithSummary("Get a historical migration job.");

        group.MapPost("/jobs/upload", async (
            HttpRequest request,
            ClaimsPrincipal user,
            IHistoricalMigrationService migrationService,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Multipart form-data is required." });
            }

            var form = await request.ReadFormAsync(ct);
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "A source file is required." });
            }

            if (!int.TryParse(form["institutionId"], out var institutionId) || institutionId <= 0)
            {
                return Results.BadRequest(new { error = "institutionId is required." });
            }

            if (!int.TryParse(form["returnPeriodId"], out var returnPeriodId) || returnPeriodId <= 0)
            {
                return Results.BadRequest(new { error = "returnPeriodId is required." });
            }

            var returnCode = form["returnCode"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(returnCode))
            {
                return Results.BadRequest(new { error = "returnCode is required." });
            }

            var userId = ParseUserId(user);
            if (userId is null)
            {
                return Results.Forbid();
            }

            if (file.Length > 50 * 1024 * 1024)
            {
                return Results.BadRequest(new { error = "File size exceeds 50 MB limit." });
            }

            await using var fileStream = file.OpenReadStream();
            var job = await migrationService.UploadAndParse(
                tenantContext.CurrentTenantId.Value,
                institutionId,
                returnCode,
                returnPeriodId,
                file.FileName,
                fileStream,
                userId.Value,
                ct);

            return Results.Ok(job);
        })
        .RequireAuthorization("CanCreateSubmission")
        .DisableAntiforgery()
        .WithSummary("Upload and parse a historical return file.");

        group.MapGet("/jobs/{importJobId:int}/mapping", async (
            int importJobId,
            IHistoricalMigrationService migrationService,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            var mapping = await migrationService.GetMappingEditor(tenantContext.CurrentTenantId.Value, importJobId, ct);
            return Results.Ok(mapping);
        })
        .RequireAuthorization("CanCreateSubmission")
        .WithSummary("Get mapping review details for an import job.");

        group.MapPut("/jobs/{importJobId:int}/mapping", async (
            int importJobId,
            [FromBody] ImportMappingUpdateRequest request,
            IHistoricalMigrationService migrationService,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            await migrationService.SaveMapping(
                tenantContext.CurrentTenantId.Value,
                importJobId,
                request.Updates ?? [],
                request.SourceIdentifier,
                ct);

            var mapping = await migrationService.GetMappingEditor(tenantContext.CurrentTenantId.Value, importJobId, ct);
            return Results.Ok(mapping);
        })
        .RequireAuthorization("CanCreateSubmission")
        .WithSummary("Save mapping updates for an import job.");

        group.MapPost("/jobs/{importJobId:int}/validate", async (
            int importJobId,
            IHistoricalMigrationService migrationService,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            var result = await migrationService.ValidateJob(tenantContext.CurrentTenantId.Value, importJobId, ct);
            return Results.Ok(result);
        })
        .RequireAuthorization("CanCreateSubmission")
        .WithSummary("Run relaxed validation for an import job.");

        group.MapPost("/jobs/{importJobId:int}/stage", async (
            int importJobId,
            IHistoricalMigrationService migrationService,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            var result = await migrationService.StageJob(tenantContext.CurrentTenantId.Value, importJobId, ct);
            return Results.Ok(result);
        })
        .RequireAuthorization("CanCreateSubmission")
        .WithSummary("Move an import job to staged status.");

        group.MapGet("/jobs/{importJobId:int}/staged", async (
            int importJobId,
            [FromQuery] int take,
            IHistoricalMigrationService migrationService,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            var staged = await migrationService.GetStagedReview(
                tenantContext.CurrentTenantId.Value,
                importJobId,
                take <= 0 ? 200 : take,
                ct);
            return Results.Ok(staged);
        })
        .RequireAuthorization("CanCreateSubmission")
        .WithSummary("Get staged records for review/edit.");

        group.MapPut("/jobs/{importJobId:int}/staged", async (
            int importJobId,
            [FromBody] ImportStagedReviewSaveRequest request,
            IHistoricalMigrationService migrationService,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            await migrationService.SaveStagedReview(
                tenantContext.CurrentTenantId.Value,
                importJobId,
                request.Records ?? [],
                ct);
            return Results.Ok(new { saved = request.Records?.Count ?? 0 });
        })
        .RequireAuthorization("CanCreateSubmission")
        .WithSummary("Save staged review edits.");

        group.MapPost("/jobs/{importJobId:int}/commit", async (
            int importJobId,
            IHistoricalMigrationService migrationService,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            var result = await migrationService.CommitJob(tenantContext.CurrentTenantId.Value, importJobId, ct);
            return Results.Ok(result);
        })
        .RequireAuthorization("CanCreateSubmission")
        .WithSummary("Commit staged historical data as a read-only historical submission.");

        group.MapGet("/tracker", async (
            IHistoricalMigrationService migrationService,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            var tracker = await migrationService.GetTracker(tenantContext.CurrentTenantId.Value, ct);
            return Results.Ok(tracker);
        })
        .RequireAuthorization("CanCreateSubmission")
        .WithSummary("Get migration tracker progress by module.");

        group.MapPost("/tracker/{moduleId:int}/signoff", async (
            int moduleId,
            [FromBody] MigrationSignOffRequest request,
            ClaimsPrincipal user,
            IHistoricalMigrationService migrationService,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            var userId = ParseUserId(user);
            if (userId is null)
            {
                return Results.Forbid();
            }

            await migrationService.SetModuleSignOff(
                tenantContext.CurrentTenantId.Value,
                moduleId,
                request.SignedOff,
                userId.Value,
                request.Notes,
                ct);

            return Results.Ok(new { moduleId, request.SignedOff });
        })
        .RequireAuthorization("CanManageUsers")
        .WithSummary("Set migration compliance sign-off state for a module.");
    }

    private static int? ParseUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? user.FindFirst("sub")?.Value;
        return int.TryParse(raw, out var userId) ? userId : null;
    }
}

public class ImportMappingUpdateRequest
{
    public string? SourceIdentifier { get; set; }
    public List<ImportMappingUpdate> Updates { get; set; } = [];
}

public class ImportStagedReviewSaveRequest
{
    public List<ImportStagedRecordDto> Records { get; set; } = [];
}

public class MigrationSignOffRequest
{
    public bool SignedOff { get; set; }
    public string? Notes { get; set; }
}
