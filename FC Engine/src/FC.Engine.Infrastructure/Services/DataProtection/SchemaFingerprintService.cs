using System.Security.Cryptography;
using System.Text;
using FC.Engine.Domain.Models;

namespace FC.Engine.Infrastructure.Services.DataProtection;

public sealed class SchemaFingerprintService
{
    public string ComputeFingerprint(DataTableSchema table)
    {
        var normalized = table.Columns
            .Select(c => $"{c.ColumnName.Trim().ToLowerInvariant()}:{c.DataType.Trim().ToLowerInvariant()}")
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToArray();

        var payload = string.Join("|", normalized);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public decimal CalculateSimilarity(DataTableSchema left, DataTableSchema right)
    {
        var leftSet = left.Columns
            .Select(c => $"{c.ColumnName.Trim().ToLowerInvariant()}:{c.DataType.Trim().ToLowerInvariant()}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rightSet = right.Columns
            .Select(c => $"{c.ColumnName.Trim().ToLowerInvariant()}:{c.DataType.Trim().ToLowerInvariant()}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (leftSet.Count == 0 && rightSet.Count == 0)
        {
            return 100m;
        }

        var intersection = leftSet.Count(rightSet.Contains);
        var union = leftSet.Union(rightSet, StringComparer.OrdinalIgnoreCase).Count();
        return union == 0 ? 0m : decimal.Round((decimal)intersection / union * 100m, 2);
    }

    public int EditDistance(string left, string right)
    {
        left = left.Trim().ToLowerInvariant();
        right = right.Trim().ToLowerInvariant();

        var dp = new int[left.Length + 1, right.Length + 1];
        for (var i = 0; i <= left.Length; i++) dp[i, 0] = i;
        for (var j = 0; j <= right.Length; j++) dp[0, j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[left.Length, right.Length];
    }
}
