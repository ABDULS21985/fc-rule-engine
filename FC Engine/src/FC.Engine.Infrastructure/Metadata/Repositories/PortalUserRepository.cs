using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Metadata.Repositories;

public class PortalUserRepository : IPortalUserRepository
{
    private readonly MetadataDbContext _db;

    public PortalUserRepository(MetadataDbContext db)
    {
        _db = db;
    }

    public async Task<PortalUser?> GetByUsername(string username, CancellationToken ct = default)
    {
        return await _db.PortalUsers
            .FirstOrDefaultAsync(u => u.Username == username, ct);
    }

    public async Task<PortalUser?> GetByEmail(string email, CancellationToken ct = default)
    {
        return await _db.PortalUsers
            .FirstOrDefaultAsync(u => u.Email == email, ct);
    }

    public async Task<PortalUser?> GetById(int id, CancellationToken ct = default)
    {
        return await _db.PortalUsers.FindAsync([id], ct);
    }

    public async Task<IReadOnlyList<PortalUser>> GetAll(CancellationToken ct = default)
    {
        return await _db.PortalUsers
            .Where(u => u.DeletedAt == null)
            .OrderBy(u => u.Username)
            .ToListAsync(ct);
    }

    public async Task<PortalUser> Create(PortalUser user, CancellationToken ct = default)
    {
        _db.PortalUsers.Add(user);
        await _db.SaveChangesAsync(ct);
        return user;
    }

    public async Task Update(PortalUser user, CancellationToken ct = default)
    {
        _db.PortalUsers.Update(user);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> UsernameExists(string username, CancellationToken ct = default)
    {
        return await _db.PortalUsers.AnyAsync(u => u.Username == username && u.DeletedAt == null, ct);
    }

    public async Task<bool> EmailExists(string email, int? excludeUserId = null, CancellationToken ct = default)
    {
        return await _db.PortalUsers
            .Where(u => u.Email == email && u.DeletedAt == null)
            .Where(u => excludeUserId == null || u.Id != excludeUserId)
            .AnyAsync(ct);
    }
}
