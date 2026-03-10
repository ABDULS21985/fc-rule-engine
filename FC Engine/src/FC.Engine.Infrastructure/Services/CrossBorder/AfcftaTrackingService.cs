using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Services.CrossBorder;

public sealed class AfcftaTrackingService : IAfcftaTrackingService
{
    private readonly MetadataDbContext _db;
    private readonly IHarmonisationAuditLogger _audit;

    public AfcftaTrackingService(MetadataDbContext db, IHarmonisationAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<AfcftaProtocolDto>> ListProtocolsAsync(CancellationToken ct = default)
    {
        var protocols = await _db.AfcftaProtocolTracking
            .AsNoTracking()
            .OrderBy(p => p.Category)
            .ThenBy(p => p.ProtocolCode)
            .ToListAsync(ct);

        return protocols.Select(MapToDto).ToList();
    }

    public async Task<AfcftaProtocolDto?> GetProtocolAsync(string protocolCode, CancellationToken ct = default)
    {
        var protocol = await _db.AfcftaProtocolTracking
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProtocolCode == protocolCode, ct);

        return protocol is null ? null : MapToDto(protocol);
    }

    public async Task UpdateProtocolStatusAsync(
        string protocolCode, AfcftaProtocolStatus newStatus,
        int userId, CancellationToken ct = default)
    {
        var protocol = await _db.AfcftaProtocolTracking
            .FirstOrDefaultAsync(p => p.ProtocolCode == protocolCode, ct)
            ?? throw new InvalidOperationException($"Protocol '{protocolCode}' not found.");

        var oldStatus = protocol.Status;
        protocol.Status = newStatus;
        protocol.LastUpdated = DateTime.UtcNow;

        if (newStatus == AfcftaProtocolStatus.Effective && !protocol.ActualEffectiveDate.HasValue)
            protocol.ActualEffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow);

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(null, null, Guid.NewGuid(),
            "AFCFTA_PROTOCOL_STATUS_CHANGED",
            new { protocolCode, OldStatus = oldStatus.ToString(), NewStatus = newStatus.ToString() },
            userId, ct);
    }

    private static AfcftaProtocolDto MapToDto(AfcftaProtocolTracking p) => new()
    {
        Id = p.Id,
        ProtocolCode = p.ProtocolCode,
        ProtocolName = p.ProtocolName,
        Category = p.Category,
        Status = p.Status,
        ParticipatingJurisdictions = string.IsNullOrWhiteSpace(p.ParticipatingJurisdictions)
            ? []
            : p.ParticipatingJurisdictions.Split(',', StringSplitOptions.TrimEntries).ToList(),
        TargetEffectiveDate = p.TargetEffectiveDate,
        ActualEffectiveDate = p.ActualEffectiveDate,
        Description = p.Description,
        ImpactOnRegOS = p.ImpactOnRegOS
    };
}
