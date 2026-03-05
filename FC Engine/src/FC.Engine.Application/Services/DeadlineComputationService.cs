using FC.Engine.Domain.Entities;

namespace FC.Engine.Application.Services;

/// <summary>
/// Computes deadline dates and generates period schedules for modules.
/// </summary>
public class DeadlineComputationService
{
    /// <summary>
    /// Compute the deadline date for a given module and period.
    /// Deadline = period end date + offset days.
    /// </summary>
    public DateTime ComputeDeadline(Module module, ReturnPeriod period)
    {
        var periodEnd = GetPeriodEndDate(module.DefaultFrequency, period.Year, period.Month, period.Quarter);
        var offsetDays = module.DeadlineOffsetDays ?? GetDefaultOffsetDays(module.DefaultFrequency);
        return periodEnd.AddDays(offsetDays);
    }

    /// <summary>
    /// Get the end date of a reporting period based on frequency.
    /// </summary>
    public static DateTime GetPeriodEndDate(string frequency, int year, int? month, int? quarter)
    {
        return frequency switch
        {
            "Monthly" => new DateTime(year, month!.Value, DateTime.DaysInMonth(year, month.Value)),
            "Quarterly" => quarter switch
            {
                1 => new DateTime(year, 3, 31),
                2 => new DateTime(year, 6, 30),
                3 => new DateTime(year, 9, 30),
                4 => new DateTime(year, 12, 31),
                _ => throw new ArgumentException($"Invalid quarter: {quarter}")
            },
            "SemiAnnual" => month!.Value <= 6
                ? new DateTime(year, 6, 30)
                : new DateTime(year, 12, 31),
            "Annual" => new DateTime(year, 12, 31),
            _ => month.HasValue
                ? new DateTime(year, month.Value, DateTime.DaysInMonth(year, month.Value))
                : new DateTime(year, 12, 31)
        };
    }

    /// <summary>
    /// Get the default deadline offset days per frequency type.
    /// </summary>
    public static int GetDefaultOffsetDays(string frequency) => frequency switch
    {
        "Monthly" => 30,
        "Quarterly" => 45,
        "SemiAnnual" => 60,
        "Annual" => 90,
        _ => 30
    };

    /// <summary>
    /// Generate all return periods for the next N months for a module.
    /// </summary>
    public List<ReturnPeriod> GeneratePeriodsForNext12Months(Module module, int monthsAhead = 12)
    {
        var periods = new List<ReturnPeriod>();
        var today = DateTime.UtcNow.Date;
        var startDate = new DateTime(today.Year, today.Month, 1);

        switch (module.DefaultFrequency)
        {
            case "Monthly":
                for (var i = 0; i < monthsAhead; i++)
                {
                    var date = startDate.AddMonths(i);
                    periods.Add(new ReturnPeriod
                    {
                        Year = date.Year,
                        Month = date.Month,
                        Frequency = "Monthly",
                        ReportingDate = new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)),
                        IsOpen = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                break;

            case "Quarterly":
                var currentQuarterStart = ((today.Month - 1) / 3) * 3 + 1;
                var quarterDate = new DateTime(today.Year, currentQuarterStart, 1);
                for (var i = 0; i < 4; i++)
                {
                    var qDate = quarterDate.AddMonths(i * 3);
                    var quarter = ((qDate.Month - 1) / 3) + 1;
                    var endMonth = quarter * 3;
                    periods.Add(new ReturnPeriod
                    {
                        Year = qDate.Year,
                        Month = endMonth,
                        Quarter = quarter,
                        Frequency = "Quarterly",
                        ReportingDate = new DateTime(qDate.Year, endMonth, DateTime.DaysInMonth(qDate.Year, endMonth)),
                        IsOpen = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                break;

            case "SemiAnnual":
                var currentHalf = today.Month <= 6 ? 1 : 2;
                for (var i = 0; i < 2; i++)
                {
                    var half = ((currentHalf - 1 + i) % 2) + 1;
                    var year = today.Year + ((currentHalf - 1 + i) / 2);
                    var endMonth = half == 1 ? 6 : 12;
                    periods.Add(new ReturnPeriod
                    {
                        Year = year,
                        Month = endMonth,
                        Frequency = "SemiAnnual",
                        ReportingDate = new DateTime(year, endMonth, DateTime.DaysInMonth(year, endMonth)),
                        IsOpen = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                break;

            case "Annual":
                var horizonEnd = startDate.AddMonths(monthsAhead).AddDays(-1);
                for (var year = startDate.Year; ; year++)
                {
                    var annualEnd = new DateTime(year, 12, 31);
                    if (annualEnd < startDate)
                    {
                        continue;
                    }

                    if (annualEnd > horizonEnd)
                    {
                        break;
                    }

                    periods.Add(new ReturnPeriod
                    {
                        Year = year,
                        Month = 12,
                        Frequency = "Annual",
                        ReportingDate = annualEnd,
                        IsOpen = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                break;

            default:
                // Treat unknown as monthly
                for (var i = 0; i < monthsAhead; i++)
                {
                    var date = startDate.AddMonths(i);
                    periods.Add(new ReturnPeriod
                    {
                        Year = date.Year,
                        Month = date.Month,
                        Frequency = module.DefaultFrequency,
                        ReportingDate = new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)),
                        IsOpen = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                break;
        }

        return periods;
    }
}
