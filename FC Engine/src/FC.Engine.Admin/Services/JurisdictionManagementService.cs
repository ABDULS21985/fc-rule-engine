using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Admin.Services;

public class JurisdictionManagementService
{
    private readonly IDbContextFactory<MetadataDbContext> _dbFactory;

    public JurisdictionManagementService(IDbContextFactory<MetadataDbContext> dbFactory) => _dbFactory = dbFactory;

    public async Task<List<JurisdictionListItem>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var jurisdictions = await db.Jurisdictions
            .OrderBy(j => j.Id)
            .ToListAsync(ct);

        var moduleCounts = await db.Modules
            .Where(m => m.JurisdictionId != null)
            .GroupBy(m => m.JurisdictionId!.Value)
            .Select(g => new { JurisdictionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.JurisdictionId, x => x.Count, ct);

        var institutionCounts = await db.Institutions
            .GroupBy(i => i.JurisdictionId)
            .Select(g => new { JurisdictionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.JurisdictionId, x => x.Count, ct);

        return jurisdictions.Select(j => new JurisdictionListItem
        {
            Id = j.Id,
            CountryCode = j.CountryCode,
            CountryName = j.CountryName,
            Currency = j.Currency,
            Timezone = j.Timezone,
            RegulatoryBodies = j.RegulatoryBodies,
            DateFormat = j.DateFormat,
            DataProtectionLaw = j.DataProtectionLaw,
            DataResidencyRegion = j.DataResidencyRegion,
            IsActive = j.IsActive,
            ModuleCount = moduleCounts.GetValueOrDefault(j.Id),
            InstitutionCount = institutionCounts.GetValueOrDefault(j.Id)
        }).ToList();
    }

    public async Task<Jurisdiction?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Jurisdictions.FindAsync(new object[] { id }, ct);
    }

    public async Task ToggleActiveAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var jurisdiction = await db.Jurisdictions.FindAsync(new object[] { id }, ct)
            ?? throw new InvalidOperationException("Jurisdiction not found.");
        jurisdiction.IsActive = !jurisdiction.IsActive;
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(int id, JurisdictionUpdateRequest request, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var jurisdiction = await db.Jurisdictions.FindAsync(new object[] { id }, ct)
            ?? throw new InvalidOperationException("Jurisdiction not found.");

        jurisdiction.CountryName = request.CountryName;
        jurisdiction.Currency = request.Currency;
        jurisdiction.Timezone = request.Timezone;
        jurisdiction.RegulatoryBodies = request.RegulatoryBodies;
        jurisdiction.DateFormat = request.DateFormat;
        jurisdiction.DataProtectionLaw = request.DataProtectionLaw;
        jurisdiction.DataResidencyRegion = request.DataResidencyRegion;

        await db.SaveChangesAsync(ct);
    }

    public async Task<JurisdictionDashboardStats> GetStatsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var jurisdictions = await db.Jurisdictions.ToListAsync(ct);
        return new JurisdictionDashboardStats
        {
            Total = jurisdictions.Count,
            Active = jurisdictions.Count(j => j.IsActive),
            Planned = jurisdictions.Count(j => !j.IsActive)
        };
    }

    // ── FX Rates ──────────────────────────────────────────────────────

    public async Task<List<JurisdictionFxRate>> GetFxRatesAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.JurisdictionFxRates
            .OrderByDescending(r => r.RateDate)
            .ThenBy(r => r.BaseCurrency)
            .ThenBy(r => r.QuoteCurrency)
            .ToListAsync(ct);
    }

    public async Task UpsertFxRateAsync(JurisdictionFxRate rate, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.JurisdictionFxRates
            .FirstOrDefaultAsync(r =>
                r.BaseCurrency == rate.BaseCurrency &&
                r.QuoteCurrency == rate.QuoteCurrency &&
                r.RateDate == rate.RateDate, ct);

        if (existing is not null)
        {
            existing.Rate = rate.Rate;
            existing.Source = rate.Source;
        }
        else
        {
            db.JurisdictionFxRates.Add(rate);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteFxRateAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rate = await db.JurisdictionFxRates.FindAsync(new object[] { id }, ct);
        if (rate is not null)
        {
            db.JurisdictionFxRates.Remove(rate);
            await db.SaveChangesAsync(ct);
        }
    }
}

public record JurisdictionListItem
{
    public int Id { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;
    public string RegulatoryBodies { get; set; } = "[]";
    public string DateFormat { get; set; } = "dd/MM/yyyy";
    public string? DataProtectionLaw { get; set; }
    public string DataResidencyRegion { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int ModuleCount { get; set; }
    public int InstitutionCount { get; set; }
}

public class JurisdictionUpdateRequest
{
    public string CountryName { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;
    public string RegulatoryBodies { get; set; } = "[]";
    public string DateFormat { get; set; } = "dd/MM/yyyy";
    public string? DataProtectionLaw { get; set; }
    public string DataResidencyRegion { get; set; } = string.Empty;
}

public class JurisdictionDashboardStats
{
    public int Total { get; set; }
    public int Active { get; set; }
    public int Planned { get; set; }
}
