using System.Globalization;
using System.Text.Json;
using FC.Engine.Domain.Models;

namespace FC.Engine.Infrastructure.Services;

internal static class ForeSightSupport
{
    public static decimal Clamp(decimal value, decimal min = 0m, decimal max = 1m)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    public static decimal Logistic(decimal weightedScore)
    {
        var centered = (double)(weightedScore * 6m - 3m);
        return (decimal)(1d / (1d + Math.Exp(-centered)));
    }

    public static decimal ComputeConfidence(
        int observations,
        int targetObservations,
        decimal dataCoverage,
        decimal volatilityPenalty = 0m)
    {
        var observationScore = targetObservations <= 0
            ? 0.25m
            : Clamp((decimal)observations / targetObservations) * 0.35m;

        var coverageScore = Clamp(dataCoverage) * 0.35m;
        var baseline = 0.30m;
        var penalty = Clamp(volatilityPenalty, 0m, 0.25m);
        return Clamp(baseline + observationScore + coverageScore - penalty, 0.05m, 0.98m);
    }

    public static decimal CalculateSlope(IReadOnlyList<decimal> values)
    {
        if (values.Count < 2)
        {
            return 0m;
        }

        var count = values.Count;
        var xMean = (count - 1) / 2m;
        var yMean = values.Average();
        decimal ssxy = 0m;
        decimal ssxx = 0m;

        for (var i = 0; i < count; i++)
        {
            var dx = i - xMean;
            ssxy += dx * (values[i] - yMean);
            ssxx += dx * dx;
        }

        return ssxx == 0m ? 0m : ssxy / ssxx;
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
            var delta = value - mean;
            variance += delta * delta;
        }

        variance /= values.Count - 1;
        return (decimal)Math.Sqrt((double)variance);
    }

    public static decimal[] ForecastHoltLinear(IReadOnlyList<decimal> values, int periods, double alpha = 0.35, double beta = 0.15)
    {
        if (periods <= 0)
        {
            return Array.Empty<decimal>();
        }

        if (values.Count == 0)
        {
            return Enumerable.Repeat(0m, periods).ToArray();
        }

        var series = values.Select(static x => (double)x).ToArray();
        var level = series[0];
        var trend = series.Length > 1 ? series[1] - series[0] : 0d;

        for (var i = 1; i < series.Length; i++)
        {
            var previousLevel = level;
            level = alpha * series[i] + (1d - alpha) * (level + trend);
            trend = beta * (level - previousLevel) + (1d - beta) * trend;
        }

        var results = new decimal[periods];
        for (var horizon = 1; horizon <= periods; horizon++)
        {
            results[horizon - 1] = decimal.Round((decimal)(level + (horizon * trend)), 4);
        }

        return results;
    }

    public static int CountNonEmptyJsonValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return CountRecursive(document.RootElement);
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    public static string SerializeFeatures(IEnumerable<ForeSightPredictionFeature> features)
        => JsonSerializer.Serialize(features);

    public static List<ForeSightPredictionFeature> DeserializeFeatures(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<ForeSightPredictionFeature>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<ForeSightPredictionFeature>>(json) ?? new List<ForeSightPredictionFeature>();
        }
        catch (JsonException)
        {
            return new List<ForeSightPredictionFeature>();
        }
    }

    public static string FormatPeriodCode(int year, int month, int? quarter)
    {
        if (quarter is >= 1 and <= 4)
        {
            return $"{year}-Q{quarter}";
        }

        return $"{year}-{month:00}";
    }

    public static string RatingLabel(decimal score) => score switch
    {
        >= 90m => "A+",
        >= 80m => "A",
        >= 70m => "B",
        >= 60m => "C",
        >= 50m => "D",
        _ => "F"
    };

    public static string HumanizeFactorList(IReadOnlyList<ForeSightPredictionFeature> factors)
    {
        var labels = factors
            .Select(x => x.FeatureLabel)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(3)
            .ToList();

        return labels.Count switch
        {
            0 => "insufficient signal detail",
            1 => labels[0],
            2 => $"{labels[0]} and {labels[1]}",
            _ => $"{labels[0]}, {labels[1]}, and {labels[2]}"
        };
    }

    public static string NormalizeAction(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "UPDATED";
        }

        return value.Length <= 64 ? value : value[..64];
    }

    public static bool TryParseBoolean(string? value, out bool result)
    {
        if (bool.TryParse(value, out result))
        {
            return true;
        }

        if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }

    public static decimal ParseDecimal(string? value, decimal fallback)
        => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    public static int ParseInt(string? value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static int CountRecursive(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().Sum(static property => CountRecursive(property.Value)),
            JsonValueKind.Array => element.EnumerateArray().Sum(CountRecursive),
            JsonValueKind.String => string.IsNullOrWhiteSpace(element.GetString()) ? 0 : 1,
            JsonValueKind.Number => 1,
            JsonValueKind.True => 1,
            _ => 0
        };
    }
}
