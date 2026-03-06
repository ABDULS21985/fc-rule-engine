using FC.Engine.Application.DTOs;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;

namespace FC.Engine.Api.Endpoints;

public static class SubmissionEndpoints
{
    public static void MapSubmissionEndpoints(this IEndpointRouteBuilder routes, string versionSuffix = "")
    {
        var suffix = string.IsNullOrEmpty(versionSuffix) ? "" : $"_{versionSuffix}";
        var group = routes.MapGroup("/submissions").WithTags("Submissions");

        group.MapPost("/{returnCode}", async (
            string returnCode,
            HttpRequest request,
            int institutionId,
            int returnPeriodId,
            IngestionOrchestrator orchestrator,
            IFilingCalendarService filingCalendarService,
            CancellationToken ct) =>
        {
            if (request.ContentType == null || !request.ContentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest("Content-Type must be application/xml or text/xml");

            var result = await orchestrator.Process(
                request.Body, returnCode, institutionId, returnPeriodId, ct);

            if (result.Status is "Accepted" or "AcceptedWithWarnings")
            {
                try
                {
                    await filingCalendarService.RecordSla(returnPeriodId, result.SubmissionId, ct);
                }
                catch
                {
                    // SLA tracking should not block API submissions.
                }
            }

            return result.Status switch
            {
                "Accepted" or "AcceptedWithWarnings" => Results.Ok(result),
                "Rejected" => Results.UnprocessableEntity(result),
                _ => Results.Ok(result)
            };
        })
        .Accepts<IFormFile>("application/xml")
        .Produces<SubmissionResultDto>()
        .Produces<SubmissionResultDto>(422)
        .RequireAuthorization("CanCreateSubmission")
        .WithName($"SubmitReturn{suffix}")
        .WithSummary("Submit an XML return for processing and validation");

        group.MapGet("/{id:int}", async (
            int id,
            ISubmissionRepository repo,
            CancellationToken ct) =>
        {
            var submission = await repo.GetByIdWithReport(id, ct);
            if (submission == null) return Results.NotFound();

            return Results.Ok(new SubmissionResultDto
            {
                SubmissionId = submission.Id,
                ReturnCode = submission.ReturnCode,
                Status = submission.Status.ToString(),
                ProcessingDurationMs = submission.ProcessingDurationMs,
                ValidationReport = submission.ValidationReport == null ? null : new ValidationReportDto
                {
                    IsValid = submission.ValidationReport.IsValid,
                    ErrorCount = submission.ValidationReport.ErrorCount,
                    WarningCount = submission.ValidationReport.WarningCount,
                    Errors = submission.ValidationReport.Errors.Select(e => new ValidationErrorDto
                    {
                        RuleId = e.RuleId,
                        Field = e.Field,
                        Message = e.Message,
                        Severity = e.Severity.ToString(),
                        Category = e.Category.ToString(),
                        ExpectedValue = e.ExpectedValue,
                        ActualValue = e.ActualValue,
                        ReferencedReturnCode = e.ReferencedReturnCode
                    }).ToList()
                }
            });
        })
        .Produces<SubmissionResultDto>()
        .WithName($"GetSubmission{suffix}")
        .WithSummary("Get submission details with validation report");

        group.MapGet("/institution/{institutionId:int}", async (
            int institutionId,
            ISubmissionRepository repo,
            CancellationToken ct) =>
        {
            var submissions = await repo.GetByInstitution(institutionId, ct);
            return Results.Ok(submissions.Select(s => new SubmissionResultDto
            {
                SubmissionId = s.Id,
                ReturnCode = s.ReturnCode,
                Status = s.Status.ToString(),
                ProcessingDurationMs = s.ProcessingDurationMs
            }));
        })
        .WithName($"GetInstitutionSubmissions{suffix}")
        .WithSummary("Get all submissions for an institution");
    }
}
