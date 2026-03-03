namespace FC.Engine.Portal.Services;

using FC.Engine.Application.DTOs;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Portal service that orchestrates the submission wizard workflow:
/// template listing, period queries, and delegating to IngestionOrchestrator.
/// </summary>
public class SubmissionService
{
    private readonly TemplateService _templateService;
    private readonly ISubmissionRepository _submissionRepo;
    private readonly IngestionOrchestrator _orchestrator;
    private readonly MetadataDbContext _db;

    public SubmissionService(
        TemplateService templateService,
        ISubmissionRepository submissionRepo,
        IngestionOrchestrator orchestrator,
        MetadataDbContext db)
    {
        _templateService = templateService;
        _submissionRepo = submissionRepo;
        _orchestrator = orchestrator;
        _db = db;
    }

    /// <summary>
    /// Gets all published templates with "already submitted" status for the current period.
    /// </summary>
    public async Task<List<TemplateSelectItem>> GetTemplatesForInstitution(int institutionId)
    {
        var allTemplates = await _templateService.GetAllTemplates();
        var publishedTemplates = allTemplates.Where(t => t.PublishedVersionId.HasValue).ToList();

        var submissions = await _submissionRepo.GetByInstitution(institutionId);
        var now = DateTime.UtcNow;
        var currentMonth = new DateTime(now.Year, now.Month, 1);
        var currentMonthEnd = currentMonth.AddMonths(1).AddDays(-1);

        var submittedReturnCodes = submissions
            .Where(s => s.SubmittedAt >= currentMonth && s.SubmittedAt <= currentMonthEnd)
            .Where(s => s.Status != SubmissionStatus.Rejected && s.Status != SubmissionStatus.ApprovalRejected)
            .Select(s => s.ReturnCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return publishedTemplates.Select(t => new TemplateSelectItem
        {
            ReturnCode = t.ReturnCode,
            TemplateName = t.Name,
            Frequency = t.Frequency,
            StructuralCategory = t.StructuralCategory,
            AlreadySubmitted = submittedReturnCodes.Contains(t.ReturnCode)
        }).OrderBy(t => t.ReturnCode).ToList();
    }

    /// <summary>
    /// Gets open return periods, optionally checking for existing submissions.
    /// </summary>
    public async Task<List<PeriodSelectItem>> GetOpenPeriods(int institutionId, string returnCode)
    {
        var periods = await _db.ReturnPeriods
            .Where(rp => rp.IsOpen)
            .OrderByDescending(rp => rp.Year)
            .ThenByDescending(rp => rp.Month)
            .ToListAsync();

        var submissions = await _submissionRepo.GetByInstitution(institutionId);
        var existingByPeriod = submissions
            .Where(s => s.ReturnCode.Equals(returnCode, StringComparison.OrdinalIgnoreCase))
            .Where(s => s.Status != SubmissionStatus.Rejected && s.Status != SubmissionStatus.ApprovalRejected)
            .Select(s => s.ReturnPeriodId)
            .ToHashSet();

        return periods.Select(p => new PeriodSelectItem
        {
            ReturnPeriodId = p.Id,
            Value = $"{p.Year}-{p.Month:00}",
            Label = new DateTime(p.Year, p.Month, 1).ToString("MMMM yyyy"),
            ReportingDate = p.ReportingDate,
            Year = p.Year,
            Month = p.Month,
            HasExistingSubmission = existingByPeriod.Contains(p.Id)
        }).ToList();
    }

    /// <summary>
    /// Delegates to IngestionOrchestrator.Process — validates and persists the return.
    /// </summary>
    public async Task<SubmissionResultDto> ProcessSubmission(
        Stream xmlStream, string returnCode, int institutionId, int returnPeriodId)
    {
        return await _orchestrator.Process(xmlStream, returnCode, institutionId, returnPeriodId);
    }
}
