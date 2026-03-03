using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Metadata.Repositories;

public class InstitutionUserRepository : IInstitutionUserRepository
{
    private readonly MetadataDbContext _db;

    public InstitutionUserRepository(MetadataDbContext db)
    {
        _db = db;
    }

    public async Task<InstitutionUser?> GetById(int id, CancellationToken ct = default)
    {
        return await _db.InstitutionUsers
            .Include(u => u.Institution)
            .FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<InstitutionUser?> GetByUsername(string username, CancellationToken ct = default)
    {
        return await _db.InstitutionUsers
            .Include(u => u.Institution)
            .FirstOrDefaultAsync(u => u.Username == username, ct);
    }

    public async Task<InstitutionUser?> GetByEmail(string email, CancellationToken ct = default)
    {
        return await _db.InstitutionUsers
            .Include(u => u.Institution)
            .FirstOrDefaultAsync(u => u.Email == email, ct);
    }

    public async Task<IReadOnlyList<InstitutionUser>> GetByInstitution(int institutionId, CancellationToken ct = default)
    {
        return await _db.InstitutionUsers
            .Where(u => u.InstitutionId == institutionId)
            .OrderBy(u => u.DisplayName)
            .ToListAsync(ct);
    }

    public async Task<int> GetCountByInstitution(int institutionId, CancellationToken ct = default)
    {
        return await _db.InstitutionUsers
            .CountAsync(u => u.InstitutionId == institutionId, ct);
    }

    public async Task<bool> UsernameExists(string username, CancellationToken ct = default)
    {
        return await _db.InstitutionUsers
            .AnyAsync(u => u.Username == username, ct);
    }

    public async Task<bool> EmailExists(string email, CancellationToken ct = default)
    {
        return await _db.InstitutionUsers
            .AnyAsync(u => u.Email == email, ct);
    }

    public async Task Create(InstitutionUser user, CancellationToken ct = default)
    {
        _db.InstitutionUsers.Add(user);
        await _db.SaveChangesAsync(ct);
    }

    public async Task Update(InstitutionUser user, CancellationToken ct = default)
    {
        _db.InstitutionUsers.Update(user);
        await _db.SaveChangesAsync(ct);
    }
}
