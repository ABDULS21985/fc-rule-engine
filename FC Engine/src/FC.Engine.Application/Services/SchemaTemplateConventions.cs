using System.Text.RegularExpressions;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Application.Services;

internal static partial class SchemaTemplateConventions
{
    private static readonly IReadOnlyDictionary<string, string> SpecialReturnCodes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["fc_car_1"] = "FC CAR 1",
            ["fc_car_2"] = "FC CAR 2",
            ["fc_acr"] = "FC ACR",
            ["fc_fhr"] = "FC FHR",
            ["fc_cvr"] = "FC CVR",
            ["fc_rating"] = "FC RATING",
            ["consol"] = "CONSOL",
            ["npl"] = "NPL",
            ["reports_kri"] = "REPORTS",
            ["sheet3_top10_rankings"] = "SHEET3"
        };

    private static readonly HashSet<string> ComputedReturnCodes =
    [
        "CONSOL",
        "REPORTS",
        "SHEET3"
    ];

    private static readonly HashSet<string> MultiRowTables =
    [
        "fc_fhr",
        "fc_cvr",
        "sheet3_top10_rankings"
    ];

    private static readonly IReadOnlyDictionary<string, string> MultiRowKeyColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["fc_fhr"] = "indicator_name",
            ["fc_cvr"] = "contravention_type",
            ["sheet3_top10_rankings"] = "rank_position"
        };

    internal static string DeriveReturnCode(string tableName)
    {
        if (SpecialReturnCodes.TryGetValue(tableName, out var specialReturnCode))
        {
            return specialReturnCode;
        }

        var match = StandardReturnCodeRegex().Match(tableName);
        if (!match.Success)
        {
            return string.Empty;
        }

        var prefix = match.Groups[1].Value.ToUpperInvariant();
        var number = match.Groups[2].Value;
        var suffix = match.Groups[3].Success ? $"-{match.Groups[3].Value}" : "";

        return $"{prefix} {number}{suffix}";
    }

    internal static ReturnFrequency DeriveFrequency(string returnCode)
    {
        if (ComputedReturnCodes.Contains(returnCode))
            return ReturnFrequency.Computed;
        if (returnCode.StartsWith("MFCR", StringComparison.OrdinalIgnoreCase))
            return ReturnFrequency.Monthly;
        if (returnCode.StartsWith("QFCR", StringComparison.OrdinalIgnoreCase))
            return ReturnFrequency.Quarterly;
        if (returnCode.StartsWith("SFCR", StringComparison.OrdinalIgnoreCase))
            return ReturnFrequency.SemiAnnual;
        if (returnCode.StartsWith("FC", StringComparison.OrdinalIgnoreCase))
            return ReturnFrequency.Computed;

        return ReturnFrequency.Monthly;
    }

    internal static StructuralCategory DeriveStructuralCategory(ParsedTable table)
    {
        var colNames = table.Columns.Select(c => c.Name.ToLowerInvariant()).ToHashSet();
        if (colNames.Contains("serial_no"))
            return StructuralCategory.MultiRow;
        if (colNames.Contains("item_code"))
            return StructuralCategory.ItemCoded;
        if (MultiRowTables.Contains(table.TableName))
            return StructuralCategory.MultiRow;

        return StructuralCategory.FixedRow;
    }

    internal static ParsedColumn? GetStructuralKeyColumn(ParsedTable table, StructuralCategory category)
    {
        if (category == StructuralCategory.ItemCoded)
        {
            return table.Columns.FirstOrDefault(c => c.Name.Equals("item_code", StringComparison.OrdinalIgnoreCase));
        }

        if (category != StructuralCategory.MultiRow)
        {
            return null;
        }

        var serialColumn = table.Columns.FirstOrDefault(c => c.Name.Equals("serial_no", StringComparison.OrdinalIgnoreCase));
        if (serialColumn is not null)
        {
            return serialColumn;
        }

        if (MultiRowKeyColumns.TryGetValue(table.TableName, out var keyColumnName))
        {
            return table.Columns.FirstOrDefault(c => c.Name.Equals(keyColumnName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    [GeneratedRegex(@"^(mfcr|qfcr|sfcr|fc)_(\d+)(?:_(\d+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex StandardReturnCodeRegex();
}
