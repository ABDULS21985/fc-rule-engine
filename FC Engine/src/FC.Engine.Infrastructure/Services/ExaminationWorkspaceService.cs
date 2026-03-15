using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FC.Engine.Infrastructure.Services;

public class ExaminationWorkspaceService : IExaminationWorkspaceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly MetadataDbContext _db;
    private readonly IEntityBenchmarkingService _entityBenchmarking;
    private readonly ITenantBrandingService _brandingService;
    private readonly IEarlyWarningService _earlyWarningService;
    private readonly IFileStorageService _fileStorage;
    private readonly IAuditLogger _auditLogger;

    public ExaminationWorkspaceService(
        MetadataDbContext db,
        IEntityBenchmarkingService entityBenchmarking,
        ITenantBrandingService brandingService,
        IEarlyWarningService earlyWarningService,
        IFileStorageService fileStorage,
        IAuditLogger auditLogger)
    {
        _db = db;
        _entityBenchmarking = entityBenchmarking;
        _brandingService = brandingService;
        _earlyWarningService = earlyWarningService;
        _fileStorage = fileStorage;
        _auditLogger = auditLogger;
    }

    public async Task<IReadOnlyList<ExaminationProject>> ListProjects(Guid regulatorTenantId, CancellationToken ct = default)
    {
        await AutoEscalateOverdueFindings(regulatorTenantId, ct);

        return await _db.ExaminationProjects
            .AsNoTracking()
            .Where(x => x.TenantId == regulatorTenantId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<ExaminationProject> CreateProject(
        Guid regulatorTenantId,
        int createdBy,
        ExaminationProjectCreateRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Project name is required.", nameof(request));
        }

        var entityIds = request.InstitutionIds
            .Where(x => x > 0)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        var moduleCodes = request.ModuleCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        var teamAssignments = request.TeamAssignments
            .Where(x => x.UserId > 0 || !string.IsNullOrWhiteSpace(x.DisplayName))
            .Select(x => new ExaminationTeamAssignment
            {
                UserId = x.UserId,
                DisplayName = (x.DisplayName ?? string.Empty).Trim(),
                Role = string.IsNullOrWhiteSpace(x.Role) ? "Examiner" : x.Role.Trim()
            })
            .ToList();

        var milestones = request.Milestones
            .Where(x => !string.IsNullOrWhiteSpace(x.Title))
            .Select(x => new ExaminationMilestone
            {
                Title = x.Title.Trim(),
                Owner = (x.Owner ?? string.Empty).Trim(),
                DueAt = x.DueAt,
                Completed = x.Completed
            })
            .OrderBy(x => x.DueAt ?? DateTime.MaxValue)
            .ToList();

        var now = DateTime.UtcNow;
        var project = new ExaminationProject
        {
            TenantId = regulatorTenantId,
            Name = request.Name.Trim(),
            Scope = string.IsNullOrWhiteSpace(request.Scope) ? "General examination scope" : request.Scope.Trim(),
            EntityIdsJson = JsonSerializer.Serialize(entityIds, JsonOptions),
            ModuleCodesJson = JsonSerializer.Serialize(moduleCodes, JsonOptions),
            TeamAssignmentsJson = JsonSerializer.Serialize(teamAssignments, JsonOptions),
            TimelineJson = JsonSerializer.Serialize(milestones, JsonOptions),
            PeriodFrom = request.PeriodFrom,
            PeriodTo = request.PeriodTo,
            Status = ExaminationProjectStatus.InProgress,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.ExaminationProjects.Add(project);
        await _db.SaveChangesAsync(ct);

        await CarryForwardOpenFindings(regulatorTenantId, project, entityIds, createdBy, ct);
        await _auditLogger.Log(
            "ExaminationProject",
            project.Id,
            "Create",
            null,
            new
            {
                project.Name,
                project.Scope,
                EntityIds = entityIds,
                ModuleCodes = moduleCodes,
                TeamAssignments = teamAssignments.Count,
                Milestones = milestones.Count
            },
            createdBy.ToString(),
            ct);

        return project;
    }

    public async Task<ExaminationWorkspaceData?> GetWorkspace(
        Guid regulatorTenantId,
        string regulatorCode,
        int projectId,
        CancellationToken ct = default)
    {
        await AutoEscalateOverdueFindings(regulatorTenantId, ct);

        var project = await _db.ExaminationProjects
            .AsNoTracking()
            .Include(x => x.Annotations)
            .Include(x => x.Findings)
            .Include(x => x.EvidenceRequests)
            .Include(x => x.EvidenceFiles)
            .FirstOrDefaultAsync(x => x.TenantId == regulatorTenantId && x.Id == projectId, ct);

        if (project is null)
        {
            return null;
        }

        var institutionIds = ParseIntList(project.EntityIdsJson);
        var moduleCodes = ParseStringList(project.ModuleCodesJson);
        var submissions = await GetScopedSubmissionItems(regulatorCode, institutionIds, moduleCodes, project.PeriodFrom, project.PeriodTo, ct);
        var benchmarkMap = await BuildBenchmarkMap(regulatorCode, submissions.Select(x => x.InstitutionId).Distinct(), ct);
        var intelligencePack = await BuildIntelligencePack(regulatorTenantId, regulatorCode, project, submissions, benchmarkMap, ct);

        return new ExaminationWorkspaceData
        {
            Project = project,
            Submissions = submissions,
            Annotations = project.Annotations.OrderByDescending(x => x.CreatedAt).ToList(),
            BenchmarksByInstitution = benchmarkMap,
            TeamAssignments = ParseJsonList<ExaminationTeamAssignment>(project.TeamAssignmentsJson),
            Milestones = ParseJsonList<ExaminationMilestone>(project.TimelineJson)
                .OrderBy(x => x.DueAt ?? DateTime.MaxValue)
                .ToList(),
            Findings = project.Findings
                .OrderByDescending(x => x.RiskRating)
                .ThenBy(x => x.ManagementResponseDeadline ?? DateTime.MaxValue)
                .ThenByDescending(x => x.UpdatedAt)
                .ToList(),
            EvidenceRequests = project.EvidenceRequests
                .OrderByDescending(x => x.RequestedAt)
                .ToList(),
            EvidenceFiles = project.EvidenceFiles
                .OrderByDescending(x => x.UploadedAt)
                .ToList(),
            IntelligencePack = intelligencePack
        };
    }

    public async Task<ExaminationIntelligencePack?> GetIntelligencePack(
        Guid regulatorTenantId,
        string regulatorCode,
        int projectId,
        CancellationToken ct = default)
    {
        await AutoEscalateOverdueFindings(regulatorTenantId, ct);

        var project = await _db.ExaminationProjects
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == regulatorTenantId && x.Id == projectId, ct);

        if (project is null)
        {
            return null;
        }

        var institutionIds = ParseIntList(project.EntityIdsJson);
        var moduleCodes = ParseStringList(project.ModuleCodesJson);
        var submissions = await GetScopedSubmissionItems(regulatorCode, institutionIds, moduleCodes, project.PeriodFrom, project.PeriodTo, ct);
        var benchmarkMap = await BuildBenchmarkMap(regulatorCode, submissions.Select(x => x.InstitutionId).Distinct(), ct);

        return await BuildIntelligencePack(regulatorTenantId, regulatorCode, project, submissions, benchmarkMap, ct);
    }

    public async Task<ExaminationAnnotation> AddAnnotation(
        Guid regulatorTenantId,
        int projectId,
        int submissionId,
        int? institutionId,
        string? fieldCode,
        string note,
        int createdBy,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            throw new ArgumentException("Annotation note is required.", nameof(note));
        }

        var project = await GetProjectForUpdate(regulatorTenantId, projectId, ct);

        var annotation = new ExaminationAnnotation
        {
            TenantId = regulatorTenantId,
            ProjectId = projectId,
            SubmissionId = submissionId,
            InstitutionId = institutionId,
            FieldCode = string.IsNullOrWhiteSpace(fieldCode) ? null : fieldCode.Trim(),
            Note = note.Trim(),
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };

        _db.ExaminationAnnotations.Add(annotation);
        project.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        await _auditLogger.Log(
            "ExaminationAnnotation",
            annotation.Id,
            "Create",
            null,
            new { annotation.ProjectId, annotation.SubmissionId, annotation.FieldCode, annotation.Note },
            createdBy.ToString(),
            ct);

        return annotation;
    }

    public async Task<ExaminationFinding> CreateFinding(
        Guid regulatorTenantId,
        string regulatorCode,
        int projectId,
        ExaminationFindingCreateRequest request,
        int createdBy,
        CancellationToken ct = default)
    {
        var project = await GetProjectForUpdate(regulatorTenantId, projectId, ct);

        if (string.IsNullOrWhiteSpace(request.Title) && string.IsNullOrWhiteSpace(request.Observation))
        {
            throw new ArgumentException("Finding title or observation is required.", nameof(request));
        }

        Submission? submission = null;
        if (request.SubmissionId.HasValue)
        {
            submission = await _db.Submissions
                .AsNoTracking()
                .Include(x => x.Institution)
                .Include(x => x.ReturnPeriod)
                    .ThenInclude(x => x!.Module)
                .Include(x => x.ValidationReport)
                    .ThenInclude(x => x!.Errors)
                .FirstOrDefaultAsync(
                    x => x.Id == request.SubmissionId.Value
                         && x.ReturnPeriod != null
                         && x.ReturnPeriod.Module != null
                         && x.ReturnPeriod.Module.RegulatorCode == regulatorCode,
                    ct);

            if (submission is null)
            {
                throw new InvalidOperationException($"Submission {request.SubmissionId.Value} is outside the examination scope.");
            }
        }

        var normalizedFieldCode = string.IsNullOrWhiteSpace(request.FieldCode) ? null : request.FieldCode.Trim();
        var validationMatch = submission?.ValidationReport?.Errors
            .FirstOrDefault(x =>
                string.IsNullOrWhiteSpace(normalizedFieldCode)
                || x.Field.Equals(normalizedFieldCode, StringComparison.OrdinalIgnoreCase)
                || x.Field.Contains(normalizedFieldCode, StringComparison.OrdinalIgnoreCase));

        var observation = string.IsNullOrWhiteSpace(request.Observation)
            ? validationMatch?.Message ?? "Observation recorded during examination review."
            : request.Observation.Trim();

        var recommendation = string.IsNullOrWhiteSpace(request.Recommendation)
            ? "Management should address the identified issue and provide evidence of remediation."
            : request.Recommendation.Trim();

        var title = string.IsNullOrWhiteSpace(request.Title)
            ? BuildFindingTitle(validationMatch, request.RiskArea, normalizedFieldCode, submission)
            : request.Title.Trim();

        var finding = new ExaminationFinding
        {
            TenantId = regulatorTenantId,
            ProjectId = projectId,
            SubmissionId = request.SubmissionId ?? submission?.Id,
            InstitutionId = request.InstitutionId ?? submission?.InstitutionId,
            Title = title,
            RiskArea = string.IsNullOrWhiteSpace(request.RiskArea)
                ? DeriveRiskArea(validationMatch, request.Recommendation)
                : request.RiskArea.Trim(),
            Observation = observation,
            RiskRating = request.RiskRating,
            Recommendation = recommendation,
            Status = request.Status,
            RemediationStatus = request.ManagementResponseDeadline.HasValue && request.RemediationStatus == ExaminationRemediationStatus.Open
                ? ExaminationRemediationStatus.AwaitingManagementResponse
                : request.RemediationStatus,
            ModuleCode = string.IsNullOrWhiteSpace(request.ModuleCode)
                ? submission?.ReturnPeriod?.Module?.ModuleCode
                : request.ModuleCode.Trim().ToUpperInvariant(),
            PeriodLabel = string.IsNullOrWhiteSpace(request.PeriodLabel)
                ? submission?.ReturnPeriod is null ? null : RegulatorAnalyticsSupport.FormatPeriodLabel(submission.ReturnPeriod)
                : request.PeriodLabel.Trim(),
            FieldCode = normalizedFieldCode,
            FieldValue = ExtractFieldValue(submission?.ParsedDataJson, normalizedFieldCode),
            ValidationRuleId = validationMatch?.RuleId,
            ValidationMessage = validationMatch?.Message,
            ManagementResponseDeadline = request.ManagementResponseDeadline,
            ManagementResponse = NormalizeOptionalText(request.ManagementResponse),
            ManagementActionPlan = NormalizeOptionalText(request.ManagementActionPlan),
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        if (!string.IsNullOrWhiteSpace(finding.ManagementResponse))
        {
            finding.ManagementResponseSubmittedAt = DateTime.UtcNow;
        }

        _db.ExaminationFindings.Add(finding);
        project.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _auditLogger.Log(
            "ExaminationFinding",
            finding.Id,
            "Create",
            null,
            new
            {
                finding.ProjectId,
                finding.Title,
                finding.RiskArea,
                finding.RiskRating,
                finding.Status,
                finding.RemediationStatus,
                finding.SubmissionId,
                finding.FieldCode
            },
            createdBy.ToString(),
            ct);

        return finding;
    }

    public async Task<ExaminationFinding?> UpdateFinding(
        Guid regulatorTenantId,
        int projectId,
        int findingId,
        ExaminationFindingUpdateRequest request,
        int updatedBy,
        CancellationToken ct = default)
    {
        var project = await GetProjectForUpdate(regulatorTenantId, projectId, ct);
        var finding = await _db.ExaminationFindings
            .FirstOrDefaultAsync(x => x.TenantId == regulatorTenantId && x.ProjectId == projectId && x.Id == findingId, ct);

        if (finding is null)
        {
            return null;
        }

        var oldValues = new
        {
            finding.Status,
            finding.RemediationStatus,
            finding.RiskRating,
            finding.Observation,
            finding.Recommendation,
            finding.ManagementResponseDeadline,
            finding.ManagementResponse,
            finding.ManagementActionPlan,
            finding.EvidenceReference
        };

        finding.Status = request.Status;
        finding.RemediationStatus = request.RemediationStatus;
        finding.RiskRating = request.RiskRating;
        finding.Observation = string.IsNullOrWhiteSpace(request.Observation) ? finding.Observation : request.Observation.Trim();
        finding.Recommendation = string.IsNullOrWhiteSpace(request.Recommendation) ? finding.Recommendation : request.Recommendation.Trim();
        finding.ManagementResponseDeadline = request.ManagementResponseDeadline;
        finding.ManagementResponse = NormalizeOptionalText(request.ManagementResponse);
        finding.ManagementActionPlan = NormalizeOptionalText(request.ManagementActionPlan);
        finding.EvidenceReference = NormalizeOptionalText(request.EvidenceReference);
        finding.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(finding.ManagementResponse))
        {
            finding.ManagementResponseSubmittedAt ??= DateTime.UtcNow;
            if (finding.RemediationStatus == ExaminationRemediationStatus.Open)
            {
                finding.RemediationStatus = ExaminationRemediationStatus.InRemediation;
            }
        }

        if (finding.RemediationStatus == ExaminationRemediationStatus.Closed || finding.Status == ExaminationWorkflowStatus.Closed)
        {
            finding.RemediationStatus = ExaminationRemediationStatus.Closed;
            finding.Status = ExaminationWorkflowStatus.Closed;
            finding.VerifiedAt ??= DateTime.UtcNow;
            finding.VerifiedBy ??= updatedBy;
            finding.ClosedAt ??= DateTime.UtcNow;
        }
        else
        {
            finding.ClosedAt = null;
        }

        if (finding.RemediationStatus == ExaminationRemediationStatus.Escalated)
        {
            finding.EscalatedAt ??= DateTime.UtcNow;
            finding.EscalationReason ??= "Escalated by examiner.";
        }

        project.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _auditLogger.Log(
            "ExaminationFinding",
            finding.Id,
            "Update",
            oldValues,
            new
            {
                finding.Status,
                finding.RemediationStatus,
                finding.RiskRating,
                finding.Observation,
                finding.Recommendation,
                finding.ManagementResponseDeadline,
                finding.ManagementResponse,
                finding.ManagementActionPlan,
                finding.EvidenceReference
            },
            updatedBy.ToString(),
            ct);

        return finding;
    }

    public async Task<ExaminationEvidenceRequest> CreateEvidenceRequest(
        Guid regulatorTenantId,
        int projectId,
        ExaminationEvidenceRequestCreateRequest request,
        int requestedBy,
        CancellationToken ct = default)
    {
        var project = await GetProjectForUpdate(regulatorTenantId, projectId, ct);

        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.RequestText))
        {
            throw new ArgumentException("Evidence request title and detail are required.", nameof(request));
        }

        ExaminationFinding? finding = null;
        if (request.FindingId.HasValue)
        {
            finding = await _db.ExaminationFindings
                .FirstOrDefaultAsync(x => x.TenantId == regulatorTenantId && x.ProjectId == projectId && x.Id == request.FindingId.Value, ct);

            if (finding is null)
            {
                throw new InvalidOperationException($"Finding {request.FindingId.Value} was not found in project {projectId}.");
            }
        }

        var evidenceRequest = new ExaminationEvidenceRequest
        {
            TenantId = regulatorTenantId,
            ProjectId = projectId,
            FindingId = finding?.Id ?? request.FindingId,
            SubmissionId = request.SubmissionId ?? finding?.SubmissionId,
            InstitutionId = request.InstitutionId ?? finding?.InstitutionId,
            Title = request.Title.Trim(),
            RequestText = request.RequestText.Trim(),
            RequestedItemsJson = JsonSerializer.Serialize(
                request.RequestedItems
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .ToList(),
                JsonOptions),
            DueAt = request.DueAt,
            RequestedAt = DateTime.UtcNow,
            RequestedBy = requestedBy,
            Status = ExaminationEvidenceRequestStatus.Open
        };

        _db.ExaminationEvidenceRequests.Add(evidenceRequest);

        if (finding is not null)
        {
            finding.Status = ExaminationWorkflowStatus.ManagementResponseRequired;
            if (finding.RemediationStatus == ExaminationRemediationStatus.Open)
            {
                finding.RemediationStatus = ExaminationRemediationStatus.AwaitingManagementResponse;
            }

            finding.UpdatedAt = DateTime.UtcNow;
        }

        project.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _auditLogger.Log(
            "ExaminationEvidenceRequest",
            evidenceRequest.Id,
            "Create",
            null,
            new
            {
                evidenceRequest.ProjectId,
                evidenceRequest.FindingId,
                evidenceRequest.Title,
                evidenceRequest.DueAt,
                evidenceRequest.Status
            },
            requestedBy.ToString(),
            ct);

        return evidenceRequest;
    }

    public async Task<ExaminationEvidenceFile> UploadEvidence(
        Guid regulatorTenantId,
        int projectId,
        int? findingId,
        int? evidenceRequestId,
        int? submissionId,
        int? institutionId,
        string fileName,
        string contentType,
        long fileSizeBytes,
        Stream content,
        ExaminationEvidenceKind kind,
        ExaminationEvidenceUploaderRole uploadedByRole,
        string? notes,
        int uploadedBy,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Evidence file name is required.", nameof(fileName));
        }

        var project = await GetProjectForUpdate(regulatorTenantId, projectId, ct);

        ExaminationEvidenceRequest? evidenceRequest = null;
        if (evidenceRequestId.HasValue)
        {
            evidenceRequest = await _db.ExaminationEvidenceRequests
                .FirstOrDefaultAsync(x => x.TenantId == regulatorTenantId && x.ProjectId == projectId && x.Id == evidenceRequestId.Value, ct);

            if (evidenceRequest is null)
            {
                throw new InvalidOperationException($"Evidence request {evidenceRequestId.Value} was not found.");
            }
        }

        var resolvedFindingId = findingId ?? evidenceRequest?.FindingId;
        ExaminationFinding? finding = null;
        if (resolvedFindingId.HasValue)
        {
            finding = await _db.ExaminationFindings
                .FirstOrDefaultAsync(x => x.TenantId == regulatorTenantId && x.ProjectId == projectId && x.Id == resolvedFindingId.Value, ct);

            if (finding is null)
            {
                throw new InvalidOperationException($"Finding {resolvedFindingId.Value} was not found.");
            }
        }

        var resolvedSubmissionId = submissionId ?? evidenceRequest?.SubmissionId ?? finding?.SubmissionId;
        var resolvedInstitutionId = institutionId ?? evidenceRequest?.InstitutionId ?? finding?.InstitutionId;

        await using var memory = new MemoryStream();
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        await content.CopyToAsync(memory, ct);
        var bytes = memory.ToArray();
        var actualSize = bytes.LongLength > 0 ? bytes.LongLength : fileSizeBytes;
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var safeFileName = SanitizeFileName(fileName);
        var storagePath = $"examinations/{regulatorTenantId}/projects/{projectId}/evidence/{DateTime.UtcNow:yyyyMMddHHmmssfff}-{hash[..12]}-{safeFileName}";

        memory.Position = 0;
        await _fileStorage.UploadImmutableAsync(storagePath, memory, string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType, ct);

        var evidenceFile = new ExaminationEvidenceFile
        {
            TenantId = regulatorTenantId,
            ProjectId = projectId,
            FindingId = finding?.Id,
            EvidenceRequestId = evidenceRequest?.Id,
            SubmissionId = resolvedSubmissionId,
            InstitutionId = resolvedInstitutionId,
            FileName = safeFileName,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            FileSizeBytes = actualSize,
            StoragePath = storagePath,
            FileHash = hash,
            Kind = kind,
            UploadedByRole = uploadedByRole,
            UploadedBy = uploadedBy,
            UploadedAt = DateTime.UtcNow,
            Notes = NormalizeOptionalText(notes)
        };

        _db.ExaminationEvidenceFiles.Add(evidenceFile);

        if (evidenceRequest is not null)
        {
            evidenceRequest.Status = ExaminationEvidenceRequestStatus.Fulfilled;
            evidenceRequest.FulfilledAt = DateTime.UtcNow;
        }

        if (finding is not null)
        {
            finding.EvidenceReference = $"{evidenceFile.FileName} [{evidenceFile.FileHash[..12]}]";
            finding.UpdatedAt = DateTime.UtcNow;

            if (kind == ExaminationEvidenceKind.RemediationEvidence)
            {
                finding.Status = ExaminationWorkflowStatus.ManagementResponseRequired;
                finding.RemediationStatus = ExaminationRemediationStatus.PendingVerification;
                finding.ManagementResponseSubmittedAt ??= DateTime.UtcNow;
            }
            else if (finding.Status == ExaminationWorkflowStatus.ToReview)
            {
                finding.Status = ExaminationWorkflowStatus.InProgress;
            }
        }

        project.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _auditLogger.Log(
            "ExaminationEvidenceFile",
            evidenceFile.Id,
            "Upload",
            null,
            new
            {
                evidenceFile.ProjectId,
                evidenceFile.FindingId,
                evidenceFile.EvidenceRequestId,
                evidenceFile.FileName,
                evidenceFile.FileHash,
                evidenceFile.Kind,
                evidenceFile.UploadedByRole
            },
            uploadedBy.ToString(),
            ct);

        return evidenceFile;
    }

    public async Task<ExaminationEvidenceDownload?> DownloadEvidence(
        Guid regulatorTenantId,
        int projectId,
        int evidenceFileId,
        CancellationToken ct = default)
    {
        var evidence = await _db.ExaminationEvidenceFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.TenantId == regulatorTenantId && x.ProjectId == projectId && x.Id == evidenceFileId,
                ct);

        if (evidence is null)
        {
            return null;
        }

        await using var stream = await _fileStorage.DownloadAsync(evidence.StoragePath, ct);
        await using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, ct);

        return new ExaminationEvidenceDownload
        {
            FileName = evidence.FileName,
            ContentType = evidence.ContentType,
            Content = memory.ToArray()
        };
    }

    public async Task<byte[]> GenerateIntelligencePackPdf(
        Guid regulatorTenantId,
        string regulatorCode,
        int projectId,
        CancellationToken ct = default)
    {
        var workspace = await GetWorkspace(regulatorTenantId, regulatorCode, projectId, ct)
            ?? throw new InvalidOperationException($"Examination project {projectId} was not found.");

        var intelligencePack = workspace.IntelligencePack
            ?? await BuildIntelligencePack(
                regulatorTenantId,
                regulatorCode,
                workspace.Project,
                workspace.Submissions,
                workspace.BenchmarksByInstitution,
                ct);

        QuestPDF.Settings.License = LicenseType.Community;
        var branding = BrandingConfig.WithDefaults(await _brandingService.GetBrandingConfig(regulatorTenantId, ct));
        var primaryColor = string.IsNullOrWhiteSpace(branding.PrimaryColor) ? "#0F766E" : branding.PrimaryColor!;
        var accentColor = string.IsNullOrWhiteSpace(branding.AccentColor) ? "#1D4ED8" : branding.AccentColor!;

        var pdf = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text("Pre-Examination Intelligence Pack").FontSize(16).Bold().FontColor(primaryColor);
                    col.Item().Text(workspace.Project.Name).FontSize(12).SemiBold();
                    col.Item().Text($"Generated: {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC").FontSize(8).FontColor(Colors.Grey.Medium);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Text("Executive Brief").Bold().FontColor(primaryColor);
                    col.Item().Text(
                        $"Scope covers {intelligencePack.TotalInstitutions} institution(s), {workspace.Submissions.Count} scoped submissions, " +
                        $"{intelligencePack.TotalActiveEwis} active early warning trigger(s), and {intelligencePack.TotalOutstandingRemediationItems} outstanding remediation item(s).");

                    if (intelligencePack.KeyRiskAreas.Count > 0)
                    {
                        col.Item().Text("Key Risk Areas").Bold().FontColor(primaryColor);
                        foreach (var risk in intelligencePack.KeyRiskAreas.Take(8))
                        {
                            col.Item().Text($"- {risk}");
                        }
                    }

                    foreach (var institution in intelligencePack.Institutions)
                    {
                        col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                        col.Item().Text($"{institution.InstitutionName} ({institution.LicenceType})")
                            .Bold()
                            .FontColor(accentColor);

                        if (institution.ChsTrend.Count > 0)
                        {
                            col.Item().Text("4-Quarter CHS Trend").Bold();
                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(1);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Background(primaryColor).Padding(3).Text("Quarter").FontColor(Colors.White).Bold();
                                    header.Cell().Background(primaryColor).Padding(3).Text("Score").FontColor(Colors.White).Bold();
                                });

                                foreach (var point in institution.ChsTrend)
                                {
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(point.QuarterLabel);
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(point.Score.ToString("F1"));
                                }
                            });
                        }

                        if (institution.PeerComparison is not null)
                        {
                            var peer = institution.PeerComparison;
                            col.Item().Text(
                                $"Peer comparison: CAR {peer.CarValue:F2}% vs peer average {peer.CarPeerAverage:F2}% | " +
                                $"NPL {peer.NplValue:F2}% vs peer average {peer.NplPeerAverage:F2}% | " +
                                $"Data quality {peer.DataQualityScore:F1} vs peer average {peer.DataQualityPeerAverage:F1}");
                        }

                        if (institution.ActiveWarnings.Count > 0)
                        {
                            col.Item().Text("Active EWIs").Bold();
                            foreach (var warning in institution.ActiveWarnings.Take(5))
                            {
                                col.Item().Text($"- [{warning.Severity}] {warning.FlagCode}: {warning.Message}");
                            }
                        }

                        if (institution.OutstandingPreviousFindings.Count > 0)
                        {
                            col.Item().Text("Outstanding Previous Findings").Bold();
                            foreach (var finding in institution.OutstandingPreviousFindings.Take(5))
                            {
                                col.Item().Text(
                                    $"- {finding.Title} | {finding.RiskArea} | {finding.RemediationStatus} | " +
                                    $"Due {(finding.ManagementResponseDeadline.HasValue ? finding.ManagementResponseDeadline.Value.ToString("dd MMM yyyy") : "not set")}");
                            }
                        }
                    }
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text(branding.CopyrightText ?? "RegOS").FontSize(7).FontColor(Colors.Grey.Medium);
                    row.ConstantItem(100).AlignRight().Text(txt =>
                    {
                        txt.Span("Page ").FontSize(7);
                        txt.CurrentPageNumber().FontSize(7);
                        txt.Span(" / ").FontSize(7);
                        txt.TotalPages().FontSize(7);
                    });
                });
            });
        }).GeneratePdf();

        var project = await GetProjectForUpdate(regulatorTenantId, projectId, ct);
        project.IntelligencePackGeneratedAt = DateTime.UtcNow;
        project.UpdatedAt = DateTime.UtcNow;
        project.IntelligencePackFilePath = await StoreGeneratedPdf(
            regulatorTenantId,
            projectId,
            "intelligence-pack-latest.pdf",
            pdf,
            ct);
        await _db.SaveChangesAsync(ct);

        return pdf;
    }

    public async Task<byte[]> GenerateReportPdf(
        Guid regulatorTenantId,
        string regulatorCode,
        int projectId,
        CancellationToken ct = default)
    {
        var workspace = await GetWorkspace(regulatorTenantId, regulatorCode, projectId, ct)
            ?? throw new InvalidOperationException($"Examination project {projectId} was not found.");

        QuestPDF.Settings.License = LicenseType.Community;

        var branding = BrandingConfig.WithDefaults(await _brandingService.GetBrandingConfig(regulatorTenantId, ct));
        var primaryColor = string.IsNullOrWhiteSpace(branding.PrimaryColor) ? "#0F766E" : branding.PrimaryColor!;
        var accentColor = string.IsNullOrWhiteSpace(branding.AccentColor) ? "#1D4ED8" : branding.AccentColor!;
        var findings = workspace.Findings.OrderByDescending(x => x.RiskRating).ThenBy(x => x.RiskArea).ToList();
        var findingsByRiskArea = findings
            .GroupBy(x => string.IsNullOrWhiteSpace(x.RiskArea) ? "General" : x.RiskArea)
            .OrderBy(x => x.Key)
            .ToList();
        var executiveSummary = workspace.IntelligencePack?.KeyRiskAreas ?? new List<string>();

        var pdf = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text("Regulator Examination Report").FontSize(16).Bold().FontColor(primaryColor);
                    col.Item().Text(workspace.Project.Name).FontSize(12).SemiBold();
                    col.Item().Text($"Generated: {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC").FontSize(8).FontColor(Colors.Grey.Medium);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Text("Executive Summary").Bold().FontColor(primaryColor);
                    col.Item().Text(
                        $"{findings.Count} finding(s) documented across {findingsByRiskArea.Count} risk area(s). " +
                        $"{findings.Count(x => x.RiskRating == ExaminationRiskRating.High)} high-risk finding(s), " +
                        $"{findings.Count(x => x.RemediationStatus == ExaminationRemediationStatus.Escalated)} escalated remediation item(s), and " +
                        $"{workspace.EvidenceFiles.Count} evidence file(s) are linked to this examination.");

                    foreach (var summary in executiveSummary.Take(6))
                    {
                        col.Item().Text($"- {summary}");
                    }

                    col.Item().Text("Scope").Bold().FontColor(primaryColor);
                    col.Item().Text(workspace.Project.Scope);

                    col.Item().Text("Methodology").Bold().FontColor(primaryColor);
                    col.Item().Text("- Offsite review of scoped return submissions and validation results.");
                    col.Item().Text("- Targeted evidence requests and digital evidence review through the examination workspace.");
                    col.Item().Text("- CHS trend analysis, peer comparison, early warning signals, and carry-forward review of prior findings.");

                    col.Item().Text("Detailed Findings").Bold().FontColor(primaryColor);
                    if (findings.Count == 0)
                    {
                        col.Item().Text("No findings have been documented.");
                    }
                    else
                    {
                        foreach (var riskArea in findingsByRiskArea)
                        {
                            col.Item().PaddingTop(4).Text(riskArea.Key).Bold().FontColor(accentColor);

                            foreach (var finding in riskArea)
                            {
                                col.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Column(section =>
                                {
                                    section.Spacing(3);
                                    section.Item().Text($"{finding.Title} [{finding.RiskRating}]").Bold();
                                    section.Item().Text(finding.Observation);
                                    section.Item().Text($"Recommendation: {finding.Recommendation}");

                                    var linkage = BuildFindingLinkageText(finding);
                                    if (!string.IsNullOrWhiteSpace(linkage))
                                    {
                                        section.Item().Text($"Linked data: {linkage}");
                                    }

                                    if (!string.IsNullOrWhiteSpace(finding.EvidenceReference))
                                    {
                                        section.Item().Text($"Evidence reference: {finding.EvidenceReference}");
                                    }

                                    section.Item().Text(
                                        $"Workflow: {finding.Status} | Remediation: {finding.RemediationStatus} | " +
                                        $"Deadline {(finding.ManagementResponseDeadline.HasValue ? finding.ManagementResponseDeadline.Value.ToString("dd MMM yyyy") : "not set")}");

                                    if (!string.IsNullOrWhiteSpace(finding.ManagementResponse))
                                    {
                                        section.Item().Text($"Management response: {finding.ManagementResponse}");
                                    }

                                    if (!string.IsNullOrWhiteSpace(finding.ManagementActionPlan))
                                    {
                                        section.Item().Text($"Action plan: {finding.ManagementActionPlan}");
                                    }
                                });
                            }
                        }
                    }

                    col.Item().Text("Management Action Plan").Bold().FontColor(primaryColor);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(3);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(primaryColor).Padding(3).Text("Finding").FontColor(Colors.White).Bold();
                            header.Cell().Background(primaryColor).Padding(3).Text("Deadline").FontColor(Colors.White).Bold();
                            header.Cell().Background(primaryColor).Padding(3).Text("Status").FontColor(Colors.White).Bold();
                            header.Cell().Background(primaryColor).Padding(3).Text("Action Plan").FontColor(Colors.White).Bold();
                        });

                        foreach (var finding in findings.Take(200))
                        {
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(finding.Title);
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                .Text(finding.ManagementResponseDeadline?.ToString("dd MMM yyyy") ?? "Not set");
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                .Text($"{finding.Status} / {finding.RemediationStatus}");
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3)
                                .Text(finding.ManagementActionPlan ?? finding.ManagementResponse ?? finding.Recommendation);
                        }
                    });
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text(branding.CopyrightText ?? "RegOS")
                        .FontSize(7)
                        .FontColor(Colors.Grey.Medium);

                    row.ConstantItem(100).AlignRight().Text(txt =>
                    {
                        txt.Span("Page ").FontSize(7);
                        txt.CurrentPageNumber().FontSize(7);
                        txt.Span(" / ").FontSize(7);
                        txt.TotalPages().FontSize(7);
                    });
                });
            });
        }).GeneratePdf();

        var project = await GetProjectForUpdate(regulatorTenantId, projectId, ct);
        project.LastReportGeneratedAt = DateTime.UtcNow;
        project.UpdatedAt = DateTime.UtcNow;
        project.ReportFilePath = await StoreGeneratedPdf(
            regulatorTenantId,
            projectId,
            "examination-report-latest.pdf",
            pdf,
            ct);
        await _db.SaveChangesAsync(ct);

        return pdf;
    }

    private async Task<ExaminationIntelligencePack> BuildIntelligencePack(
        Guid regulatorTenantId,
        string regulatorCode,
        ExaminationProject project,
        IReadOnlyList<RegulatorSubmissionInboxItem> submissions,
        IReadOnlyDictionary<int, EntityBenchmarkResult> benchmarkMap,
        CancellationToken ct)
    {
        var institutionIds = ParseIntList(project.EntityIdsJson);
        if (institutionIds.Count == 0)
        {
            institutionIds = submissions.Select(x => x.InstitutionId).Distinct().OrderBy(x => x).ToList();
        }

        var institutions = await _db.Institutions
            .AsNoTracking()
            .Where(x => institutionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var activeWarnings = await _earlyWarningService.ComputeFlags(regulatorCode, ct);
        var priorOutstandingFindings = await GetOutstandingPreviousFindings(regulatorTenantId, project.Id, institutionIds, ct);

        var institutionSummaries = new List<ExaminationInstitutionIntelligence>();
        foreach (var institutionId in institutionIds)
        {
            institutions.TryGetValue(institutionId, out var institution);
            var trend = institution is null
                ? new List<ExaminationQuarterTrendPoint>()
                : await GetQuarterlyChsTrend(institution.TenantId, ct);

            var warnings = activeWarnings
                .Where(x => x.InstitutionId == institutionId)
                .OrderByDescending(x => x.Severity)
                .ThenBy(x => x.FlagCode)
                .ToList();

            var outstanding = priorOutstandingFindings
                .Where(x => x.InstitutionId == institutionId)
                .OrderByDescending(x => x.RiskRating)
                .ThenBy(x => x.ManagementResponseDeadline ?? DateTime.MaxValue)
                .ToList();

            var keyRiskAreas = warnings
                .Select(x => x.FlagCode.Replace('_', ' '))
                .Concat(outstanding.Select(x => x.RiskArea))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();

            institutionSummaries.Add(new ExaminationInstitutionIntelligence
            {
                InstitutionId = institutionId,
                InstitutionName = institution?.InstitutionName ?? $"Institution #{institutionId}",
                LicenceType = institution?.LicenseType ?? submissions.FirstOrDefault(x => x.InstitutionId == institutionId)?.LicenceType ?? "N/A",
                ChsTrend = trend,
                ChsTrendJson = JsonSerializer.Serialize(trend.Select(x => x.Score), JsonOptions),
                ActiveWarnings = warnings,
                PeerComparison = benchmarkMap.GetValueOrDefault(institutionId),
                OutstandingPreviousFindings = outstanding,
                KeyRiskAreas = keyRiskAreas
            });
        }

        var keyRiskAreasOverall = institutionSummaries
            .SelectMany(x => x.KeyRiskAreas)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key)
            .Select(x => x.Key)
            .Take(10)
            .ToList();

        return new ExaminationIntelligencePack
        {
            ProjectId = project.Id,
            GeneratedAt = DateTime.UtcNow,
            TotalInstitutions = institutionSummaries.Count,
            TotalOutstandingRemediationItems = priorOutstandingFindings.Count,
            TotalActiveEwis = activeWarnings.Count(x => institutionIds.Contains(x.InstitutionId)),
            KeyRiskAreas = keyRiskAreasOverall,
            Institutions = institutionSummaries,
            OutstandingPreviousFindings = priorOutstandingFindings
        };
    }

    private async Task<List<ExaminationFinding>> GetOutstandingPreviousFindings(
        Guid regulatorTenantId,
        int currentProjectId,
        IReadOnlyCollection<int> institutionIds,
        CancellationToken ct)
    {
        if (institutionIds.Count == 0)
        {
            return new List<ExaminationFinding>();
        }

        return await _db.ExaminationFindings
            .AsNoTracking()
            .Where(x => x.TenantId == regulatorTenantId
                        && x.ProjectId != currentProjectId
                        && x.InstitutionId.HasValue
                        && institutionIds.Contains(x.InstitutionId.Value)
                        && x.Status != ExaminationWorkflowStatus.Closed
                        && x.RemediationStatus != ExaminationRemediationStatus.Closed)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(ct);
    }

    private async Task<List<ExaminationQuarterTrendPoint>> GetQuarterlyChsTrend(Guid tenantId, CancellationToken ct)
    {
        var snapshots = await _db.ChsScoreSnapshots
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.ComputedAt)
            .Take(40)
            .ToListAsync(ct);

        return snapshots
            .GroupBy(x => GetQuarterStart(x.ComputedAt))
            .Select(g =>
            {
                var latest = g.OrderByDescending(x => x.ComputedAt).First();
                return new ExaminationQuarterTrendPoint
                {
                    QuarterLabel = $"{latest.ComputedAt.Year}-Q{((latest.ComputedAt.Month - 1) / 3) + 1}",
                    Score = decimal.Round(latest.OverallScore, 1),
                    SnapshotDate = latest.ComputedAt
                };
            })
            .OrderByDescending(x => x.SnapshotDate)
            .Take(4)
            .OrderBy(x => x.SnapshotDate)
            .ToList();
    }

    private static DateTime GetQuarterStart(DateTime value)
    {
        var month = ((value.Month - 1) / 3) * 3 + 1;
        return new DateTime(value.Year, month, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private async Task<Dictionary<int, EntityBenchmarkResult>> BuildBenchmarkMap(
        string regulatorCode,
        IEnumerable<int> institutionIds,
        CancellationToken ct)
    {
        var result = new Dictionary<int, EntityBenchmarkResult>();
        foreach (var institutionId in institutionIds.Where(x => x > 0).Distinct().OrderBy(x => x))
        {
            var benchmark = await _entityBenchmarking.GetEntityBenchmark(regulatorCode, institutionId, ct: ct);
            if (benchmark is not null)
            {
                result[institutionId] = benchmark;
            }
        }

        return result;
    }

    private async Task<List<RegulatorSubmissionInboxItem>> GetScopedSubmissionItems(
        string regulatorCode,
        IReadOnlyCollection<int> institutionIds,
        IReadOnlyCollection<string> moduleCodes,
        DateTime? periodFrom,
        DateTime? periodTo,
        CancellationToken ct)
    {
        var query = BuildScopedSubmissionQuery(regulatorCode);

        if (institutionIds.Count > 0)
        {
            query = query.Where(s => institutionIds.Contains(s.InstitutionId));
        }

        if (moduleCodes.Count > 0)
        {
            query = query.Where(s => s.ReturnPeriod != null
                                     && s.ReturnPeriod.Module != null
                                     && moduleCodes.Contains(s.ReturnPeriod.Module.ModuleCode));
        }

        if (periodFrom.HasValue)
        {
            query = query.Where(s => s.SubmittedAt >= periodFrom.Value);
        }

        if (periodTo.HasValue)
        {
            query = query.Where(s => s.SubmittedAt <= periodTo.Value);
        }

        var rows = await query
            .OrderByDescending(s => s.SubmittedAt)
            .Take(2000)
            .ToListAsync(ct);

        return rows.Select(s => new RegulatorSubmissionInboxItem
        {
            SubmissionId = s.Id,
            TenantId = s.TenantId,
            InstitutionId = s.InstitutionId,
            InstitutionName = s.Institution?.InstitutionName ?? "Unknown",
            LicenceType = s.Institution?.LicenseType ?? "N/A",
            ModuleCode = s.ReturnPeriod?.Module?.ModuleCode ?? "N/A",
            ModuleName = s.ReturnPeriod?.Module?.ModuleName ?? "Unknown",
            PeriodLabel = s.ReturnPeriod is null ? "N/A" : RegulatorAnalyticsSupport.FormatPeriodLabel(s.ReturnPeriod),
            SubmittedAt = s.SubmittedAt ?? default,
            SubmissionStatus = s.Status.ToString(),
            ReceiptStatus = RegulatorReceiptStatus.Received,
            OpenQueryCount = 0
        }).ToList();
    }

    private IQueryable<Submission> BuildScopedSubmissionQuery(string regulatorCode)
    {
        var normalized = regulatorCode.Trim();
        return _db.Submissions
            .AsNoTracking()
            .Include(s => s.Institution)
            .Include(s => s.ReturnPeriod)
                .ThenInclude(rp => rp!.Module)
            .Where(s => s.ReturnPeriod != null
                        && s.ReturnPeriod.Module != null
                        && s.ReturnPeriod.Module.RegulatorCode == normalized);
    }

    private async Task CarryForwardOpenFindings(
        Guid regulatorTenantId,
        ExaminationProject project,
        IReadOnlyCollection<int> institutionIds,
        int createdBy,
        CancellationToken ct)
    {
        if (institutionIds.Count == 0)
        {
            return;
        }

        var previousFindings = await _db.ExaminationFindings
            .AsNoTracking()
            .Where(x => x.TenantId == regulatorTenantId
                        && x.ProjectId != project.Id
                        && x.InstitutionId.HasValue
                        && institutionIds.Contains(x.InstitutionId.Value)
                        && x.Status != ExaminationWorkflowStatus.Closed
                        && x.RemediationStatus != ExaminationRemediationStatus.Closed)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(ct);

        var carryForwardCandidates = previousFindings
            .GroupBy(x => x.CarriedForwardFromFindingId ?? x.Id)
            .Select(x => x.OrderByDescending(y => y.UpdatedAt).First())
            .ToList();

        if (carryForwardCandidates.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var carried = carryForwardCandidates.Select(source => new ExaminationFinding
        {
            TenantId = regulatorTenantId,
            ProjectId = project.Id,
            SubmissionId = source.SubmissionId,
            InstitutionId = source.InstitutionId,
            CarriedForwardFromFindingId = source.Id,
            Title = source.Title,
            RiskArea = source.RiskArea,
            Observation = source.Observation,
            RiskRating = source.RiskRating,
            Recommendation = source.Recommendation,
            Status = ExaminationWorkflowStatus.ToReview,
            RemediationStatus = source.ManagementResponseDeadline.HasValue && source.ManagementResponseDeadline.Value < now
                ? ExaminationRemediationStatus.Escalated
                : source.RemediationStatus,
            ModuleCode = source.ModuleCode,
            PeriodLabel = source.PeriodLabel,
            FieldCode = source.FieldCode,
            FieldValue = source.FieldValue,
            ValidationRuleId = source.ValidationRuleId,
            ValidationMessage = source.ValidationMessage,
            EvidenceReference = source.EvidenceReference,
            ManagementResponseDeadline = source.ManagementResponseDeadline,
            ManagementResponse = source.ManagementResponse,
            ManagementResponseSubmittedAt = source.ManagementResponseSubmittedAt,
            ManagementActionPlan = source.ManagementActionPlan,
            IsCarriedForward = true,
            EscalatedAt = source.ManagementResponseDeadline.HasValue && source.ManagementResponseDeadline.Value < now ? now : source.EscalatedAt,
            EscalationReason = source.ManagementResponseDeadline.HasValue && source.ManagementResponseDeadline.Value < now
                ? "Automatically escalated on carry-forward because the previous remediation deadline has passed."
                : source.EscalationReason,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();

        _db.ExaminationFindings.AddRange(carried);
        project.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
    }

    private async Task AutoEscalateOverdueFindings(Guid regulatorTenantId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var overdue = await _db.ExaminationFindings
            .Where(x => x.TenantId == regulatorTenantId
                        && x.ManagementResponseDeadline.HasValue
                        && x.ManagementResponseDeadline.Value < now
                        && x.Status != ExaminationWorkflowStatus.Closed
                        && x.RemediationStatus != ExaminationRemediationStatus.Closed
                        && x.RemediationStatus != ExaminationRemediationStatus.Escalated)
            .ToListAsync(ct);

        if (overdue.Count == 0)
        {
            return;
        }

        foreach (var finding in overdue)
        {
            finding.Status = ExaminationWorkflowStatus.ManagementResponseRequired;
            finding.RemediationStatus = ExaminationRemediationStatus.Escalated;
            finding.EscalatedAt ??= now;
            finding.EscalationReason = "Automatically escalated because the remediation deadline has passed.";
            finding.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<ExaminationProject> GetProjectForUpdate(Guid regulatorTenantId, int projectId, CancellationToken ct)
    {
        var project = await _db.ExaminationProjects
            .FirstOrDefaultAsync(x => x.TenantId == regulatorTenantId && x.Id == projectId, ct);

        return project ?? throw new InvalidOperationException($"Examination project {projectId} was not found.");
    }

    private async Task<string> StoreGeneratedPdf(
        Guid regulatorTenantId,
        int projectId,
        string fileName,
        byte[] bytes,
        CancellationToken ct)
    {
        var path = $"examinations/{regulatorTenantId}/projects/{projectId}/reports/{fileName}";
        await using var stream = new MemoryStream(bytes);
        await _fileStorage.UploadAsync(path, stream, "application/pdf", ct);
        return path;
    }

    private static string BuildFindingTitle(
        ValidationError? validationMatch,
        string? riskArea,
        string? fieldCode,
        Submission? submission)
    {
        if (!string.IsNullOrWhiteSpace(validationMatch?.Message))
        {
            return validationMatch.Message.Length > 120
                ? validationMatch.Message[..120]
                : validationMatch.Message;
        }

        if (!string.IsNullOrWhiteSpace(fieldCode))
        {
            return $"Exception noted on field {fieldCode}";
        }

        if (!string.IsNullOrWhiteSpace(riskArea))
        {
            return $"{riskArea.Trim()} review finding";
        }

        return submission?.Institution?.InstitutionName is { Length: > 0 } name
            ? $"Review finding for {name}"
            : "Examination finding";
    }

    private static string DeriveRiskArea(ValidationError? validationMatch, string? recommendation)
    {
        if (validationMatch is not null)
        {
            return validationMatch.Category.ToString();
        }

        if (!string.IsNullOrWhiteSpace(recommendation) && recommendation.Contains("capital", StringComparison.OrdinalIgnoreCase))
        {
            return "Capital";
        }

        return "General Risk";
    }

    private static string BuildFindingLinkageText(ExaminationFinding finding)
    {
        var parts = new List<string>();
        if (finding.SubmissionId.HasValue)
        {
            parts.Add($"submission #{finding.SubmissionId.Value}");
        }

        if (!string.IsNullOrWhiteSpace(finding.ModuleCode))
        {
            parts.Add(finding.ModuleCode);
        }

        if (!string.IsNullOrWhiteSpace(finding.PeriodLabel))
        {
            parts.Add(finding.PeriodLabel);
        }

        if (!string.IsNullOrWhiteSpace(finding.FieldCode))
        {
            parts.Add($"field {finding.FieldCode}={(string.IsNullOrWhiteSpace(finding.FieldValue) ? "n/a" : finding.FieldValue)}");
        }

        if (!string.IsNullOrWhiteSpace(finding.ValidationRuleId))
        {
            parts.Add($"validation {finding.ValidationRuleId}");
        }

        if (!string.IsNullOrWhiteSpace(finding.ValidationMessage))
        {
            parts.Add(finding.ValidationMessage);
        }

        return string.Join(" | ", parts);
    }

    private static string? ExtractFieldValue(string? json, string? fieldCode)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(fieldCode))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return TryExtractFieldValue(document.RootElement, NormalizeKey(fieldCode), out var value)
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryExtractFieldValue(JsonElement element, string normalizedFieldCode, out string? value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var normalized = NormalizeKey(property.Name);
                    if ((normalized == normalizedFieldCode
                         || normalized.Contains(normalizedFieldCode, StringComparison.Ordinal)
                         || normalizedFieldCode.Contains(normalized, StringComparison.Ordinal))
                        && TryReadJsonValue(property.Value, out value))
                    {
                        return true;
                    }

                    if (TryExtractFieldValue(property.Value, normalizedFieldCode, out value))
                    {
                        return true;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (TryExtractFieldValue(item, normalizedFieldCode, out value))
                    {
                        return true;
                    }
                }

                break;
        }

        value = null;
        return false;
    }

    private static bool TryReadJsonValue(JsonElement element, out string? value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                value = element.GetString();
                return true;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                value = element.ToString();
                return true;
            default:
                value = null;
                return false;
        }
    }

    private static string NormalizeKey(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(fileName.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "evidence.bin" : cleaned;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static List<int> ParseIntList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<int>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<int>>(json, JsonOptions) ?? new List<int>();
        }
        catch
        {
            return new List<int>();
        }
    }

    private static List<string> ParseStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }

        try
        {
            return (JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static List<T> ParseJsonList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<T>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? new List<T>();
        }
        catch
        {
            return new List<T>();
        }
    }
}
