using FC.Engine.Domain.Entities;

namespace FC.Engine.Domain.Abstractions;

public interface IEvidencePackageService
{
    Task<EvidencePackage> GenerateAsync(int submissionId, string generatedBy, CancellationToken ct = default);
}
