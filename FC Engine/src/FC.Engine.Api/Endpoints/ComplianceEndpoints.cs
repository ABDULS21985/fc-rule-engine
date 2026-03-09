using FC.Engine.Domain.Abstractions;
using FC.Engine.Infrastructure.Services;

namespace FC.Engine.Api.Endpoints;

public static class ComplianceEndpoints
{
    public static void MapComplianceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/compliance")
            .WithTags("Compliance")
            .RequireAuthorization();

        // GET /api/v1/compliance/score/{tenantId}
        group.MapGet("/score/{tenantId:guid}", async (
            Guid tenantId,
            IComplianceHealthService chsService,
            CancellationToken ct) =>
        {
            var score = await chsService.GetCurrentScore(tenantId, ct);
            return Results.Ok(new
            {
                score.TenantId,
                score.TenantName,
                score.LicenceType,
                score.OverallScore,
                Rating = ComplianceHealthService.RatingLabel(score.Rating),
                RatingDescription = ComplianceHealthService.RatingDescription(score.Rating),
                Trend = score.Trend.ToString(),
                Pillars = new
                {
                    FilingTimeliness = new { Score = score.FilingTimeliness, Weight = "25%" },
                    DataQuality = new { Score = score.DataQuality, Weight = "25%" },
                    RegulatoryCapital = new { Score = score.RegulatoryCapital, Weight = "20%" },
                    AuditGovernance = new { Score = score.AuditGovernance, Weight = "15%" },
                    Engagement = new { Score = score.Engagement, Weight = "15%" }
                },
                score.ComputedAt,
                score.PeriodLabel
            });
        })
        .WithName("GetComplianceScore")
        .WithSummary("Get current Compliance Health Score for a tenant")
        .Produces(200);

        // GET /api/v1/compliance/trend/{tenantId}?periods=12
        group.MapGet("/trend/{tenantId:guid}", async (
            Guid tenantId,
            int? periods,
            IComplianceHealthService chsService,
            CancellationToken ct) =>
        {
            var trend = await chsService.GetTrend(tenantId, periods ?? 12, ct);
            return Results.Ok(new
            {
                trend.TenantId,
                OverallTrend = trend.OverallTrend.ToString(),
                trend.ConsecutiveDeclines,
                Snapshots = trend.Snapshots.Select(s => new
                {
                    s.PeriodLabel,
                    s.Date,
                    s.OverallScore,
                    Rating = ComplianceHealthService.RatingLabel(s.Rating),
                    s.FilingTimeliness,
                    s.DataQuality,
                    s.RegulatoryCapital,
                    s.AuditGovernance,
                    s.Engagement
                })
            });
        })
        .WithName("GetComplianceTrend")
        .WithSummary("Get CHS trend over time for a tenant")
        .Produces(200);
    }
}
