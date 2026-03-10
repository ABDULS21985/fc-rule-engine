using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface ICurrencyConversionEngine
{
    Task<ConvertedAmount> ConvertAsync(
        decimal sourceAmount, string sourceCurrency, string targetCurrency,
        DateOnly rateDate, FxRateType rateType = FxRateType.PeriodEnd,
        CancellationToken ct = default);

    Task<decimal> GetRateAsync(
        string baseCurrency, string quoteCurrency, DateOnly rateDate,
        FxRateType rateType = FxRateType.PeriodEnd,
        CancellationToken ct = default);

    Task UpsertRateAsync(
        string baseCurrency, string quoteCurrency, DateOnly rateDate,
        decimal rate, string rateSource, FxRateType rateType,
        int userId, CancellationToken ct = default);

    Task<IReadOnlyList<FxRateDto>> GetRateHistoryAsync(
        string baseCurrency, string quoteCurrency,
        DateOnly fromDate, DateOnly toDate,
        CancellationToken ct = default);
}
