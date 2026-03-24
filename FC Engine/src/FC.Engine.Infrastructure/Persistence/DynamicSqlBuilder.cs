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

    public string BuildSelect(string tableName, IReadOnlyList<TemplateField> fields, Guid? tenantId = null)
    {
        ValidateName(tableName);

        var columns = new List<string> { "id", "submission_id" };
        foreach (var f in fields)
        {
            ValidateName(f.FieldName);
            columns.Add($"[{f.FieldName}]");
        }

        // RLS handles tenant filtering; explicit TenantId filter helps index usage.
        var tenantFilter = tenantId.HasValue ? " AND TenantId = @TenantId" : "";
        return $"SELECT {string.Join(", ", columns)} FROM dbo.[{tableName}] " +
               $"WHERE submission_id = @submissionId{tenantFilter} ORDER BY id";
    }

    public string BuildSelectByInstitutionAndPeriod(
        string tableName,
        string returnCode,
        IReadOnlyList<TemplateField> fields,
        Guid? tenantId = null)
    {
        ValidateName(tableName);

        var columns = new List<string> { $"d.id", "d.submission_id" };
        foreach (var f in fields)
        {
            ValidateName(f.FieldName);
            columns.Add($"d.[{f.FieldName}]");
        }

        var rowTenantFilter = tenantId.HasValue ? " AND d.TenantId = @TenantId" : "";
        var submissionTenantFilter = tenantId.HasValue ? " AND s.TenantId = @TenantId" : "";

        return $"SELECT {string.Join(", ", columns)} FROM dbo.[{tableName}] d " +
               "WHERE d.submission_id = (" +
               "SELECT TOP 1 s.Id FROM dbo.return_submissions s " +
               "WHERE s.InstitutionId = @institutionId " +
               "AND s.ReturnPeriodId = @returnPeriodId " +
               "AND s.ReturnCode = @returnCode " +
               "AND s.Status NOT IN ('Historical', 'Rejected')" +
               $"{submissionTenantFilter} " +
               "ORDER BY s.SubmittedAt DESC, s.Id DESC)" +
               rowTenantFilter +
               " ORDER BY d.id";
    }

    public string BuildDeleteBySubmission(string tableName, Guid? tenantId = null)
    {
        ValidateName(tableName);
        var tenantFilter = tenantId.HasValue ? " AND TenantId = @TenantId" : "";
        return $"DELETE FROM dbo.[{tableName}] WHERE submission_id = @submissionId{tenantFilter}";
    }

    public string BuildSelectFieldBySubmission(string tableName, string fieldName, Guid? tenantId = null)
    {
        ValidateName(tableName);
        ValidateName(fieldName);
        var tenantFilter = tenantId.HasValue ? " AND TenantId = @TenantId" : "";
        return $"SELECT TOP 1 [{fieldName}] FROM dbo.[{tableName}] " +
               $"WHERE submission_id = @submissionId{tenantFilter} ORDER BY id";
    }

    public string BuildUpdateFieldBySubmission(string tableName, string fieldName, Guid? tenantId = null)
    {
        ValidateName(tableName);
        ValidateName(fieldName);
        var tenantFilter = tenantId.HasValue ? " AND TenantId = @TenantId" : "";
        return $"UPDATE dbo.[{tableName}] SET [{fieldName}] = @value " +
               $"WHERE submission_id = @submissionId{tenantFilter}";
    }

    public string BuildInsertSingleField(string tableName, string fieldName, Guid? tenantId = null)
    {
        ValidateName(tableName);
        ValidateName(fieldName);
        if (tenantId.HasValue)
        {
            return $"INSERT INTO dbo.[{tableName}] (submission_id, TenantId, [{fieldName}]) " +
                   "VALUES (@submissionId, @TenantId, @value)";
        }

        return $"INSERT INTO dbo.[{tableName}] (submission_id, [{fieldName}]) VALUES (@submissionId, @value)";
    }

    private static void ValidateName(string name)
    {
        if (!SafeNameRegex().IsMatch(name))
            throw new ArgumentException($"Invalid SQL identifier: {name}");
    }

    [GeneratedRegex(@"^[a-z_][a-z0-9_]*$")]
    private static partial Regex SafeNameRegex();
}
