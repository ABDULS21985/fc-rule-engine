using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services.CrossBorder;

public sealed class CurrencyConversionEngine : ICurrencyConversionEngine
{
    private readonly MetadataDbContext _db;
    private readonly IHarmonisationAuditLogger _audit;
    private readonly ILogger<CurrencyConversionEngine> _log;

    public CurrencyConversionEngine(MetadataDbContext db, IHarmonisationAuditLogger audit, ILogger<CurrencyConversionEngine> log)
    {
        _db = db;
        _audit = audit;
        _log = log;
    }

    public async Task<ConvertedAmount> ConvertAsync(
        decimal sourceAmount, string sourceCurrency, string targetCurrency,
        DateOnly rateDate, FxRateType rateType = FxRateType.PeriodEnd,
        CancellationToken ct = default)
    {
        if (sourceCurrency == targetCurrency)
            return new ConvertedAmount
            {
                SourceAmount = sourceAmount, SourceCurrency = sourceCurrency,
                ConvertedValue = sourceAmount, TargetCurrency = targetCurrency,
                FxRate = 1.0m, RateDate = rateDate, RateSource = "SAME_CURRENCY"
            };

        var rate = await GetRateAsync(sourceCurrency, targetCurrency, rateDate, rateType, ct);
        var converted = Math.Round(sourceAmount * rate, 2);

        return new ConvertedAmount
        {
            SourceAmount = sourceAmount, SourceCurrency = sourceCurrency,
            ConvertedValue = converted, TargetCurrency = targetCurrency,
            FxRate = rate, RateDate = rateDate,
            RateSource = $"FX_{rateType.ToString().ToUpperInvariant()}"
        };
    }

    public async Task<decimal> GetRateAsync(
        string baseCurrency, string quoteCurrency, DateOnly rateDate,
        FxRateType rateType = FxRateType.PeriodEnd, CancellationToken ct = default)
    {
        if (baseCurrency == quoteCurrency) return 1.0m;

        // Try direct pair
        var directRate = await _db.CrossBorderFxRates
            .AsNoTracking()
            .Where(r => r.BaseCurrency == baseCurrency && r.QuoteCurrency == quoteCurrency
                && r.RateDate <= rateDate && r.RateType == rateType && r.IsActive)
            .OrderByDescending(r => r.RateDate)
            .Select(r => (decimal?)r.Rate)
            .FirstOrDefaultAsync(ct);

        if (directRate.HasValue) return directRate.Value;

        // Try inverse pair
        var inverseRate = await _db.CrossBorderFxRates
            .AsNoTracking()
            .Where(r => r.BaseCurrency == quoteCurrency && r.QuoteCurrency == baseCurrency
                && r.RateDate <= rateDate && r.RateType == rateType && r.IsActive)
            .OrderByDescending(r => r.RateDate)
            .Select(r => (decimal?)r.InverseRate)
            .FirstOrDefaultAsync(ct);

        if (inverseRate.HasValue) return inverseRate.Value;

        // Triangulate via USD
        _log.LogInformation("Triangulating rate: {Base}/{Quote} via USD on {Date}", baseCurrency, quoteCurrency, rateDate);
        var baseToUsd = await GetRateAsync(baseCurrency, "USD", rateDate, rateType, ct);
        var usdToQuote = await GetRateAsync("USD", quoteCurrency, rateDate, rateType, ct);
        return Math.Round(baseToUsd * usdToQuote, 8);
    }

    public async Task UpsertRateAsync(
        string baseCurrency, string quoteCurrency, DateOnly rateDate,
        decimal rate, string rateSource, FxRateType rateType,
        int userId, CancellationToken ct = default)
    {
        var inverseRate = rate > 0 ? Math.Round(1m / rate, 8) : 0m;

        var existing = await _db.CrossBorderFxRates
            .FirstOrDefaultAsync(r => r.BaseCurrency == baseCurrency && r.QuoteCurrency == quoteCurrency
                && r.RateDate == rateDate && r.RateType == rateType, ct);

        if (existing is not null)
        {
            existing.Rate = rate;
            existing.InverseRate = inverseRate;
            existing.RateSource = rateSource;
        }
        else
        {
            _db.CrossBorderFxRates.Add(new CrossBorderFxRate
            {
                BaseCurrency = baseCurrency, QuoteCurrency = quoteCurrency,
                RateDate = rateDate, Rate = rate, InverseRate = inverseRate,
                RateSource = rateSource, RateType = rateType
            });
        }

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(null, null, Guid.NewGuid(), "FX_RATE_UPSERTED",
            new { baseCurrency, quoteCurrency, rateDate, rate, rateSource }, userId, ct);
    }

    public async Task<IReadOnlyList<FxRateDto>> GetRateHistoryAsync(
        string baseCurrency, string quoteCurrency,
        DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        var rows = await _db.CrossBorderFxRates
            .AsNoTracking()
            .Where(r => r.BaseCurrency == baseCurrency && r.QuoteCurrency == quoteCurrency
                && r.RateDate >= fromDate && r.RateDate <= toDate && r.IsActive)
            .OrderByDescending(r => r.RateDate)
            .ToListAsync(ct);

        return rows.Select(r => new FxRateDto
        {
            BaseCurrency = r.BaseCurrency, QuoteCurrency = r.QuoteCurrency,
            RateDate = r.RateDate, Rate = r.Rate, InverseRate = r.InverseRate,
            RateSource = r.RateSource, RateType = r.RateType.ToString()
        }).ToList();
    }
}
