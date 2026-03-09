using Dapper;
using Microsoft.Data.SqlClient;

namespace FC.Engine.Integration.Tests;

internal static class TestSqlConnectionResolver
{
    public static async Task<string> ResolveAsync(CancellationToken ct = default)
    {
        var env = Environment.GetEnvironmentVariable("FCENGINE_TEST_CONNSTRING");
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(env))
        {
            candidates.Add(env);
        }

        candidates.Add("Server=localhost,1433;Database=FcEngine_Test;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;Connection Timeout=3");
        candidates.Add("Server=localhost,1433;Database=FcEngine_Test;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;Connection Timeout=3");
        candidates.Add("Server=localhost,1433;Database=FcEngine;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;Connection Timeout=3");
        candidates.Add("Server=localhost,1433;Database=FcEngine;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;Connection Timeout=3");

        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            try
            {
                await using var conn = new SqlConnection(candidate);
                await conn.OpenAsync(ct);
                await conn.ExecuteScalarAsync<int>("SELECT 1");
                return candidate;
            }
            catch
            {
                // Try next candidate.
            }
        }

        throw new InvalidOperationException(
            "No usable SQL test connection string found. Set FCENGINE_TEST_CONNSTRING to a reachable SQL Server with RegOS™ schema.");
    }
}
