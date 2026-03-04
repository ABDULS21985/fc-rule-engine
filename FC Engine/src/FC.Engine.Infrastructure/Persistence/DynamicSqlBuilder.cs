using System.Text.RegularExpressions;
using Dapper;
using FC.Engine.Domain.DataRecord;
using FC.Engine.Domain.Metadata;

namespace FC.Engine.Infrastructure.Persistence;

public partial class DynamicSqlBuilder
{
    public (string Sql, DynamicParameters Parameters) BuildInsert(
        string tableName,
        IReadOnlyList<TemplateField> fields,
        ReturnDataRow row,
        int submissionId,
        Guid? tenantId = null)
    {
        ValidateName(tableName);

        var parameters = new DynamicParameters();
        parameters.Add("@submission_id", submissionId);

        var columns = new List<string> { "submission_id" };
        var paramNames = new List<string> { "@submission_id" };

        // Include TenantId in INSERT for belt-and-suspenders with RLS
        if (tenantId.HasValue)
        {
            columns.Add("TenantId");
            paramNames.Add("@TenantId");
            parameters.Add("@TenantId", tenantId.Value);
        }

        foreach (var field in fields)
        {
            var value = row.GetValue(field.FieldName);
            if (value != null)
            {
                ValidateName(field.FieldName);
                var paramName = $"@{field.FieldName}";
                columns.Add($"[{field.FieldName}]");
                paramNames.Add(paramName);
                parameters.Add(paramName, value);
            }
        }

        var sql = $"INSERT INTO dbo.[{tableName}] ({string.Join(", ", columns)}) " +
                  $"VALUES ({string.Join(", ", paramNames)})";

        return (sql, parameters);
    }

    public string BuildSelect(string tableName, IReadOnlyList<TemplateField> fields)
    {
        ValidateName(tableName);

        var columns = new List<string> { "id", "submission_id" };
        foreach (var f in fields)
        {
            ValidateName(f.FieldName);
            columns.Add($"[{f.FieldName}]");
        }

        // RLS handles tenant filtering; submission_id WHERE is for data specificity
        return $"SELECT {string.Join(", ", columns)} FROM dbo.[{tableName}] " +
               "WHERE submission_id = @submissionId ORDER BY id";
    }

    public string BuildSelectByInstitutionAndPeriod(string tableName, IReadOnlyList<TemplateField> fields)
    {
        ValidateName(tableName);

        var columns = new List<string> { $"d.id", "d.submission_id" };
        foreach (var f in fields)
        {
            ValidateName(f.FieldName);
            columns.Add($"d.[{f.FieldName}]");
        }

        return $"SELECT {string.Join(", ", columns)} FROM dbo.[{tableName}] d " +
               "INNER JOIN dbo.return_submissions s ON d.submission_id = s.id " +
               "WHERE s.InstitutionId = @institutionId AND s.ReturnPeriodId = @returnPeriodId " +
               "ORDER BY d.id";
    }

    private static void ValidateName(string name)
    {
        if (!SafeNameRegex().IsMatch(name))
            throw new ArgumentException($"Invalid SQL identifier: {name}");
    }

    [GeneratedRegex(@"^[a-z_][a-z0-9_]*$")]
    private static partial Regex SafeNameRegex();
}
