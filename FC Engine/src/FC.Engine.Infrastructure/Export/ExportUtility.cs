using System.Globalization;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;

namespace FC.Engine.Infrastructure.Export;

internal static class ExportUtility
{
    public static string FormatPeriod(ReturnPeriod? period)
    {
        if (period is null)
        {
            return "N/A";
        }

        if (period.Month is >= 1 and <= 12)
        {
            var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(period.Month);
            return $"{monthName} {period.Year}";
        }

        if (period.Quarter is >= 1 and <= 4)
        {
            return $"Q{period.Quarter} {period.Year}";
        }

        return period.Year.ToString(CultureInfo.InvariantCulture);
    }

    public static DateTime ResolveReportingDate(ReturnPeriod? period, Submission submission)
    {
        if (period is not null && period.ReportingDate != default)
        {
            return period.ReportingDate.Date;
        }

        if (period is not null && period.Month is >= 1 and <= 12)
        {
            return new DateTime(period.Year, period.Month, DateTime.DaysInMonth(period.Year, period.Month), 0, 0, 0, DateTimeKind.Utc);
        }

        return (submission.SubmittedAt ?? DateTime.MinValue).Date;
    }

    public static string? ResolveStoragePath(string? pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
        {
            return null;
        }

        var value = pathOrUrl.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
        {
            value = absolute.AbsolutePath;
        }

        value = value.Trim('/');

        if (value.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
        {
            value = value["uploads/".Length..];
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static bool IsNumeric(FieldDataType dataType)
    {
        return dataType is FieldDataType.Money
            or FieldDataType.Decimal
            or FieldDataType.Percentage
            or FieldDataType.Integer;
    }

    public static object? FormatExcelValue(object? value, FieldDataType dataType)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string str && string.IsNullOrWhiteSpace(str))
        {
            return null;
        }

        return dataType switch
        {
            FieldDataType.Integer => TryConvertInt(value),
            FieldDataType.Money or FieldDataType.Decimal or FieldDataType.Percentage => TryConvertDecimal(value),
            FieldDataType.Date => TryConvertDate(value),
            FieldDataType.Boolean => TryConvertBool(value),
            _ => value.ToString()
        };
    }

    public static string FormatPlainValue(object? value, FieldDataType dataType)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var normalized = FormatExcelValue(value, dataType);
        if (normalized is null)
        {
            return string.Empty;
        }

        return normalized switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            decimal dec when dataType == FieldDataType.Percentage => dec.ToString("0.00%", CultureInfo.InvariantCulture),
            decimal dec => dec.ToString("0.####", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            _ => normalized.ToString() ?? string.Empty
        };
    }

    public static string SanitizeWorksheetName(string source, ISet<string> existingNames)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            source = "Sheet";
        }

        var invalid = Path.GetInvalidFileNameChars()
            .Concat(new[] { ':', '\\', '/', '?', '*', '[', ']' })
            .Distinct()
            .ToHashSet();

        var sanitized = new string(source
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray())
            .Trim();

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "Sheet";
        }

        if (sanitized.Length > 31)
        {
            sanitized = sanitized[..31];
        }

        var unique = sanitized;
        var index = 1;
        while (existingNames.Contains(unique))
        {
            var suffix = $"_{index++}";
            var maxLength = Math.Max(1, 31 - suffix.Length);
            unique = sanitized[..Math.Min(maxLength, sanitized.Length)] + suffix;
        }

        existingNames.Add(unique);
        return unique;
    }

    private static int? TryConvertInt(object value)
    {
        if (value is int i)
        {
            return i;
        }

        if (value is long l)
        {
            return checked((int)l);
        }

        if (int.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static decimal? TryConvertDecimal(object value)
    {
        if (value is decimal d)
        {
            return d;
        }

        if (value is double db)
        {
            return Convert.ToDecimal(db, CultureInfo.InvariantCulture);
        }

        if (value is float f)
        {
            return Convert.ToDecimal(f, CultureInfo.InvariantCulture);
        }

        if (decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DateTime? TryConvertDate(object value)
    {
        if (value is DateTime dt)
        {
            return dt;
        }

        if (DateTime.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool? TryConvertBool(object value)
    {
        if (value is bool b)
        {
            return b;
        }

        if (bool.TryParse(value.ToString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
