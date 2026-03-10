using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services.CrossBorder;

public sealed class EquivalenceMappingService : IEquivalenceMappingService
{
    private readonly MetadataDbContext _db;
    private readonly IHarmonisationAuditLogger _audit;
    private readonly ILogger<EquivalenceMappingService> _log;

    public EquivalenceMappingService(MetadataDbContext db, IHarmonisationAuditLogger audit, ILogger<EquivalenceMappingService> log)
    {
        _db = db;
        _audit = audit;
        _log = log;
    }

    public async Task<long> CreateMappingAsync(
        string mappingCode, string mappingName, string conceptDomain,
        string? description, IReadOnlyList<EquivalenceEntryInput> entries,
        int userId, CancellationToken ct = default)
    {
        var correlationId = Guid.NewGuid();

        var mapping = new RegulatoryEquivalenceMapping
        {
            MappingCode = mappingCode,
            MappingName = mappingName,
            ConceptDomain = conceptDomain,
            Description = description,
            CreatedByUserId = userId
        };

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            mapping.Entries.Add(new EquivalenceMappingEntry
            {
                JurisdictionCode = entry.JurisdictionCode,
                RegulatorCode = entry.RegulatorCode,
                LocalParameterCode = entry.LocalParameterCode,
                LocalParameterName = entry.LocalParameterName,
                LocalThreshold = entry.LocalThreshold,
                ThresholdUnit = entry.ThresholdUnit,
                CalculationBasis = entry.CalculationBasis,
                ReturnFormCode = entry.ReturnFormCode,
                ReturnLineReference = entry.ReturnLineReference,
                RegulatoryFramework = entry.RegulatoryFramework,
                Notes = entry.Notes,
                DisplayOrder = i
            });
        }

        _db.RegulatoryEquivalenceMappings.Add(mapping);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Created equivalence mapping {Code} with {Count} entries. CorrelationId={Corr}",
            mappingCode, entries.Count, correlationId);

        await _audit.LogAsync(null, null, correlationId, "EQUIVALENCE_MAPPING_CREATED",
            new { mappingId = mapping.Id, mappingCode, conceptDomain, jurisdictionCount = entries.Count },
            userId, ct);

        return mapping.Id;
    }

    public async Task AddEntryAsync(
        long mappingId, EquivalenceEntryInput entry,
        int userId, CancellationToken ct = default)
    {
        var mapping = await _db.RegulatoryEquivalenceMappings
            .Include(m => m.Entries)
            .FirstOrDefaultAsync(m => m.Id == mappingId, ct)
            ?? throw new InvalidOperationException($"Mapping {mappingId} not found.");

        mapping.Entries.Add(new EquivalenceMappingEntry
        {
            JurisdictionCode = entry.JurisdictionCode,
            RegulatorCode = entry.RegulatorCode,
            LocalParameterCode = entry.LocalParameterCode,
            LocalParameterName = entry.LocalParameterName,
            LocalThreshold = entry.LocalThreshold,
            ThresholdUnit = entry.ThresholdUnit,
            CalculationBasis = entry.CalculationBasis,
            ReturnFormCode = entry.ReturnFormCode,
            ReturnLineReference = entry.ReturnLineReference,
            RegulatoryFramework = entry.RegulatoryFramework,
            Notes = entry.Notes,
            DisplayOrder = mapping.Entries.Count
        });

        mapping.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateThresholdAsync(
        long mappingId, string jurisdictionCode, decimal newThreshold,
        int userId, CancellationToken ct = default)
    {
        var entry = await _db.EquivalenceMappingEntries
            .FirstOrDefaultAsync(e => e.MappingId == mappingId && e.JurisdictionCode == jurisdictionCode, ct)
            ?? throw new InvalidOperationException($"Entry not found for mapping {mappingId}, jurisdiction {jurisdictionCode}.");

        var oldThreshold = entry.LocalThreshold;
        entry.LocalThreshold = newThreshold;

        var mapping = await _db.RegulatoryEquivalenceMappings.FindAsync([mappingId], ct);
        if (mapping is not null) mapping.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(null, jurisdictionCode, Guid.NewGuid(), "EQUIVALENCE_THRESHOLD_CHANGED",
            new { mappingId, jurisdictionCode, oldThreshold, newThreshold }, userId, ct);
    }

    public async Task<EquivalenceMappingDetail?> GetMappingAsync(
        long mappingId, CancellationToken ct = default)
    {
        var mapping = await _db.RegulatoryEquivalenceMappings
            .AsNoTracking()
            .Include(m => m.Entries)
            .FirstOrDefaultAsync(m => m.Id == mappingId, ct);

        if (mapping is null) return null;

        var jurisdictions = await _db.RegulatoryJurisdictions.AsNoTracking().ToListAsync(ct);
        var jDict = jurisdictions.ToDictionary(j => j.JurisdictionCode, j => j.CountryName);

        return new EquivalenceMappingDetail
        {
            Id = mapping.Id, MappingCode = mapping.MappingCode,
            MappingName = mapping.MappingName, ConceptDomain = mapping.ConceptDomain,
            Description = mapping.Description, Version = mapping.Version, IsActive = mapping.IsActive,
            Entries = mapping.Entries.OrderBy(e => e.DisplayOrder).Select(e => new EquivalenceEntryDetail
            {
                Id = e.Id, JurisdictionCode = e.JurisdictionCode,
                CountryName = jDict.GetValueOrDefault(e.JurisdictionCode, e.JurisdictionCode),
                RegulatorCode = e.RegulatorCode, LocalParameterCode = e.LocalParameterCode,
                LocalParameterName = e.LocalParameterName, LocalThreshold = e.LocalThreshold,
                ThresholdUnit = e.ThresholdUnit, CalculationBasis = e.CalculationBasis,
                ReturnFormCode = e.ReturnFormCode, ReturnLineReference = e.ReturnLineReference,
                RegulatoryFramework = e.RegulatoryFramework, Notes = e.Notes
            }).ToList()
        };
    }

    public async Task<IReadOnlyList<EquivalenceMappingSummary>> ListMappingsAsync(
        string? conceptDomain, CancellationToken ct = default)
    {
        var query = _db.RegulatoryEquivalenceMappings
            .AsNoTracking()
            .Include(m => m.Entries)
            .Where(m => m.IsActive);

        if (!string.IsNullOrEmpty(conceptDomain))
            query = query.Where(m => m.ConceptDomain == conceptDomain);

        var mappings = await query.OrderBy(m => m.MappingCode).ToListAsync(ct);

        return mappings.Select(m => new EquivalenceMappingSummary
        {
            Id = m.Id, MappingCode = m.MappingCode, MappingName = m.MappingName,
            ConceptDomain = m.ConceptDomain, JurisdictionCount = m.Entries.Count, Version = m.Version
        }).ToList();
    }

    public async Task<IReadOnlyList<JurisdictionThreshold>> GetCrossBorderComparisonAsync(
        string mappingCode, CancellationToken ct = default)
    {
        var mapping = await _db.RegulatoryEquivalenceMappings
            .AsNoTracking()
            .Include(m => m.Entries)
            .FirstOrDefaultAsync(m => m.MappingCode == mappingCode && m.IsActive, ct)
            ?? throw new InvalidOperationException($"Mapping {mappingCode} not found.");

        return mapping.Entries
            .OrderBy(e => e.JurisdictionCode)
            .Select(e => new JurisdictionThreshold
            {
                JurisdictionCode = e.JurisdictionCode,
                RegulatorCode = e.RegulatorCode,
                ParameterCode = e.LocalParameterCode,
                Threshold = e.LocalThreshold,
                Unit = e.ThresholdUnit,
                CalculationBasis = e.CalculationBasis,
                RegulatoryFramework = e.RegulatoryFramework
            }).ToList();
    }
}
