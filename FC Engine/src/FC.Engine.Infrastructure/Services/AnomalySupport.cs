using System.Globalization;
using System.Text;
using System.Text.Json;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Infrastructure.Services;

internal static class AnomalySupport
{
    internal static readonly SubmissionStatus[] AcceptedStatuses =
    {
        SubmissionStatus.Accepted,
        SubmissionStatus.AcceptedWithWarnings,
        SubmissionStatus.RegulatorAcknowledged,
        SubmissionStatus.RegulatorAccepted,
        SubmissionStatus.RegulatorQueriesRaised,
        SubmissionStatus.Historical
    };

    public static Dictionary<string, decimal> BuildConfigMap(IEnumerable<AnomalyThresholdConfig> configs)
    {
        return configs
            .Where(x => x.EffectiveTo == null)
            .GroupBy(x => x.ConfigKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(y => y.EffectiveFrom).First().ConfigValue,
                StringComparer.OrdinalIgnoreCase);
    }

    public static Dictionary<string, MetricPoint> ExtractSubmissionMetrics(string? json)
    {
        var metrics = new Dictionary<string, MetricPoint>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return metrics;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("Rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
            {
                return metrics;
            }

            foreach (var row in rows.EnumerateArray())
            {
                if (!row.TryGetProperty("Fields", out var fields) || fields.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var property in fields.EnumerateObject())
                {
                    if (!TryReadDecimal(property.Value, out var value))
                    {
                        continue;
                    }

                    var fieldCode = NormalizeFieldCode(property.Name);
                    if (string.IsNullOrWhiteSpace(fieldCode))
                    {
                        continue;
                    }

                    if (metrics.TryGetValue(fieldCode, out var current))
                    {
                        current.Value += value;
                    }
                    else
                    {
                        metrics[fieldCode] = new MetricPoint
                        {
                            FieldCode = fieldCode,
                            FieldLabel = HumanizeFieldLabel(property.Name),
                            Value = value
                        };
                    }
                }
            }
        }
        catch (JsonException)
        {
            return new Dictionary<string, MetricPoint>(StringComparer.OrdinalIgnoreCase);
        }

        return metrics;
    }

    public static bool TryReadDecimal(JsonElement value, out decimal number)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                return value.TryGetDecimal(out number);
            case JsonValueKind.String:
                return decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number);
            default:
                number = 0;
                return false;
        }
    }

    public static string NormalizeFieldCode(string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(fieldName.Length);
        foreach (var ch in fieldName)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    public static string HumanizeFieldLabel(string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return "Unknown field";
        }

        var normalized = fieldName.Replace('_', ' ').Replace('-', ' ').Trim();
        if (normalized.Length == 0)
        {
            return "Unknown field";
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    public static decimal Median(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0)
        {
            return 0m;
        }

        var ordered = values.OrderBy(x => x).ToArray();
        var mid = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[mid - 1] + ordered[mid]) / 2m
            : ordered[mid];
    }

    public static decimal Percentile(IReadOnlyList<decimal> values, decimal percentile)
    {
        if (values.Count == 0)
        {
            return 0m;
        }

        var ordered = values.OrderBy(x => x).ToArray();
        if (ordered.Length == 1)
        {
            return ordered[0];
        }

        var rank = (percentile / 100m) * (ordered.Length - 1);
        var lowerIndex = (int)Math.Floor(rank);
        var upperIndex = (int)Math.Ceiling(rank);
        if (lowerIndex == upperIndex)
        {
            return ordered[lowerIndex];
        }

        var weight = rank - lowerIndex;
        return ordered[lowerIndex] + ((ordered[upperIndex] - ordered[lowerIndex]) * weight);
    }

    public static decimal StandardDeviation(IReadOnlyList<decimal> values)
    {
        if (values.Count <= 1)
        {
            return 0m;
        }

        var mean = values.Average();
        decimal variance = 0m;
        foreach (var value in values)
        {
            var diff = value - mean;
            variance += diff * diff;
        }

        variance /= values.Count - 1;
        return (decimal)Math.Sqrt((double)variance);
    }

    public static decimal Clamp(decimal value, decimal min = 0m, decimal max = 100m)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    public static string DetermineTrafficLight(decimal qualityScore)
    {
        return qualityScore >= 80m
            ? "GREEN"
            : qualityScore >= 50m
                ? "AMBER"
                : "RED";
    }

    public static int SeverityRank(string severity)
    {
        return severity.ToUpperInvariant() switch
        {
            "ALERT" => 3,
            "WARNING" => 2,
            "INFO" => 1,
            _ => 0
        };
    }

    public static string BuildPeriodCode(ReturnPeriod period)
    {
        return RegulatorAnalyticsSupport.FormatPeriodCode(period);
    }

    public sealed class MetricPoint
    {
        public string FieldCode { get; set; } = string.Empty;
        public string FieldLabel { get; set; } = string.Empty;
        public decimal Value { get; set; }
    }
}
