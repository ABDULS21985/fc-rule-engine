using FC.Engine.Domain.Entities;

namespace FC.Engine.Domain.Abstractions;

/// <summary>
/// Repository for managing financial institution portal users.
/// </summary>
public interface IInstitutionUserRepository
{
    Task<InstitutionUser?> GetById(int id, CancellationToken ct = default);
    Task<InstitutionUser?> GetByUsername(string username, CancellationToken ct = default);
    Task<InstitutionUser?> GetByEmail(string email, CancellationToken ct = default);
    Task<IReadOnlyList<InstitutionUser>> GetByInstitution(int institutionId, CancellationToken ct = default);
    Task<int> GetCountByInstitution(int institutionId, CancellationToken ct = default);
    Task<bool> UsernameExists(string username, CancellationToken ct = default);
    Task<bool> EmailExists(string email, CancellationToken ct = default);
    Task Create(InstitutionUser user, CancellationToken ct = default);
    Task Update(InstitutionUser user, CancellationToken ct = default);
}
