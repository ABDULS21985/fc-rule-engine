using System.Security.Claims;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Security;

namespace FC.Engine.Api.Endpoints;

public static class FilingCalendarEndpoints
{
    public static void MapFilingCalendarEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/filing-calendar").WithTags("Filing Calendar");

        group.MapGet("/rag", async (
            ITenantContext tenantContext,
            IFilingCalendarService filingCalendarService,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Unauthorized();
            }

            var items = await filingCalendarService.GetRagStatus(tenantContext.CurrentTenantId.Value, ct);
            return Results.Ok(items);
        })
        .RequireAuthorization($"perm:{PermissionCatalog.CalendarRead}")
        .WithName("GetFilingCalendarRag")
        .WithSummary("Get filing RAG status for the current tenant");

        group.MapPost("/deadline-override", async (
            DeadlineOverrideRequest request,
            ClaimsPrincipal principal,
            ITenantContext tenantContext,
            IFilingCalendarService filingCalendarService,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Unauthorized();
            }

            if (request.PeriodId <= 0)
            {
                return Results.BadRequest(new { error = "Invalid periodId." });
            }

            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Results.BadRequest(new { error = "Override reason is required." });
            }

            var overrideByUserId = ResolveUserId(principal);
            await filingCalendarService.OverrideDeadline(
                tenantContext.CurrentTenantId.Value,
                request.PeriodId,
                request.NewDeadline.Date,
                request.Reason.Trim(),
                overrideByUserId,
                ct);

            return Results.Ok(new
            {
                overridden = true,
                request.PeriodId,
                deadline = request.NewDeadline.Date
            });
        })
        .RequireAuthorization($"perm:{PermissionCatalog.CalendarManage}")
        .WithName("OverrideFilingDeadline")
        .WithSummary("Override a filing deadline for a tenant period");
    }

    private static int ResolveUserId(ClaimsPrincipal principal)
    {
        var candidate = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? principal.FindFirstValue("sub");
        return int.TryParse(candidate, out var userId) ? userId : 0;
    }
}

public sealed record DeadlineOverrideRequest(int PeriodId, DateTime NewDeadline, string Reason);
