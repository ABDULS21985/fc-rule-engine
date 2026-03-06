using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IRegulatorInboxService
{
    Task<IReadOnlyList<RegulatorSubmissionInboxItem>> GetInbox(
        Guid regulatorTenantId,
        string regulatorCode,
        RegulatorInboxFilter? filter = null,
        CancellationToken ct = default);

    Task<RegulatorSubmissionDetail?> GetSubmissionDetail(
        Guid regulatorTenantId,
        string regulatorCode,
        int submissionId,
        CancellationToken ct = default);

    Task<RegulatorReceipt> UpdateReceiptStatus(
        Guid regulatorTenantId,
        int submissionId,
        RegulatorReceiptStatus status,
        int reviewedBy,
        string? notes,
        CancellationToken ct = default);

    Task<IReadOnlyList<ExaminerQuery>> GetQueries(Guid regulatorTenantId, int submissionId, CancellationToken ct = default);
    Task<IReadOnlyList<ExaminerQuery>> GetSubmissionQueries(int submissionId, CancellationToken ct = default);

    Task<ExaminerQuery> RaiseQuery(
        Guid regulatorTenantId,
        int submissionId,
        string? fieldCode,
        string queryText,
        int raisedBy,
        ExaminerQueryPriority priority = ExaminerQueryPriority.Normal,
        CancellationToken ct = default);

    Task<ExaminerQuery?> RespondToQuery(
        Guid regulatorTenantId,
        int queryId,
        int respondedBy,
        string responseText,
        CancellationToken ct = default);

    Task<ExaminerQuery?> RespondToQueryAsInstitution(
        int queryId,
        int respondedBy,
        string responseText,
        CancellationToken ct = default);
}
