using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IExaminationWorkspaceService
{
    Task<IReadOnlyList<ExaminationProject>> ListProjects(Guid regulatorTenantId, CancellationToken ct = default);

    Task<ExaminationProject> CreateProject(
        Guid regulatorTenantId,
        int createdBy,
        ExaminationProjectCreateRequest request,
        CancellationToken ct = default);

    Task<ExaminationWorkspaceData?> GetWorkspace(
        Guid regulatorTenantId,
        string regulatorCode,
        int projectId,
        CancellationToken ct = default);

    Task<ExaminationIntelligencePack?> GetIntelligencePack(
        Guid regulatorTenantId,
        string regulatorCode,
        int projectId,
        CancellationToken ct = default);

    Task<ExaminationAnnotation> AddAnnotation(
        Guid regulatorTenantId,
        int projectId,
        int submissionId,
        int? institutionId,
        string? fieldCode,
        string note,
        int createdBy,
        CancellationToken ct = default);

    Task<ExaminationFinding> CreateFinding(
        Guid regulatorTenantId,
        string regulatorCode,
        int projectId,
        ExaminationFindingCreateRequest request,
        int createdBy,
        CancellationToken ct = default);

    Task<ExaminationFinding?> UpdateFinding(
        Guid regulatorTenantId,
        int projectId,
        int findingId,
        ExaminationFindingUpdateRequest request,
        int updatedBy,
        CancellationToken ct = default);

    Task<ExaminationEvidenceRequest> CreateEvidenceRequest(
        Guid regulatorTenantId,
        int projectId,
        ExaminationEvidenceRequestCreateRequest request,
        int requestedBy,
        CancellationToken ct = default);

    Task<ExaminationEvidenceFile> UploadEvidence(
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
        CancellationToken ct = default);

    Task<ExaminationEvidenceDownload?> DownloadEvidence(
        Guid regulatorTenantId,
        int projectId,
        int evidenceFileId,
        CancellationToken ct = default);

    Task<byte[]> GenerateIntelligencePackPdf(
        Guid regulatorTenantId,
        string regulatorCode,
        int projectId,
        CancellationToken ct = default);

    Task<byte[]> GenerateReportPdf(
        Guid regulatorTenantId,
        string regulatorCode,
        int projectId,
        CancellationToken ct = default);
}
