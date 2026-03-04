using System.Text;
using System.Text.RegularExpressions;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Metadata;

namespace FC.Engine.Infrastructure.DynamicSchema;

public partial class DdlEngine : IDdlEngine
{
    private readonly ISqlTypeMapper _typeMapper;

    public DdlEngine(ISqlTypeMapper typeMapper)
    {
        _typeMapper = typeMapper;
    }

    public DdlScript GenerateCreateTable(ReturnTemplate template, TemplateVersion version)
    {
        var tableName = ValidateTableName(template.PhysicalTableName);
        var fields = version.Fields;
        var sb = new StringBuilder();

        sb.AppendLine($"CREATE TABLE dbo.[{tableName}] (");
        sb.AppendLine("    id INT IDENTITY(1,1) PRIMARY KEY,");
        sb.AppendLine("    submission_id INT NOT NULL REFERENCES dbo.return_submissions(id),");
        sb.AppendLine("    TenantId UNIQUEIDENTIFIER NOT NULL,");

        foreach (var field in fields.OrderBy(f => f.FieldOrder))
        {
            var sqlType = _typeMapper.MapToSqlType(field.DataType, field.SqlType);
            var nullable = field.IsRequired ? " NOT NULL" : "";
            var defaultVal = !string.IsNullOrEmpty(field.DefaultValue)
                ? $" DEFAULT {field.DefaultValue}" : "";
            sb.AppendLine($"    [{ValidateColumnName(field.FieldName)}] {sqlType}{nullable}{defaultVal},");
        }

        sb.AppendLine("    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()");
        sb.AppendLine(");");
        sb.AppendLine();
        sb.AppendLine($"CREATE INDEX IX_{tableName}_submission ON dbo.[{tableName}](submission_id);");
        sb.AppendLine($"CREATE INDEX IX_{tableName}_tenant ON dbo.[{tableName}](TenantId);");

        var rollback = $"DROP TABLE IF EXISTS dbo.[{tableName}];";

        return new DdlScript(sb.ToString(), rollback);
    }

    public DdlScript GenerateAlterTable(
        ReturnTemplate template,
        TemplateVersion oldVersion,
        TemplateVersion newVersion)
    {
        var tableName = ValidateTableName(template.PhysicalTableName);
        var oldFields = oldVersion.Fields.ToDictionary(f => f.FieldName, StringComparer.OrdinalIgnoreCase);
        var newFields = newVersion.Fields.ToDictionary(f => f.FieldName, StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        var rollback = new StringBuilder();

        // Fields added in new version
        foreach (var (name, field) in newFields)
        {
            if (!oldFields.ContainsKey(name))
            {
                var sqlType = _typeMapper.MapToSqlType(field.DataType, field.SqlType);
                var colName = ValidateColumnName(name);
                sb.AppendLine($"ALTER TABLE dbo.[{tableName}] ADD [{colName}] {sqlType};");
                rollback.AppendLine($"ALTER TABLE dbo.[{tableName}] DROP COLUMN [{colName}];");
            }
        }

        // Fields with changed data types (widen only)
        foreach (var (name, newField) in newFields)
        {
            if (oldFields.TryGetValue(name, out var oldField))
            {
                if (oldField.SqlType != newField.SqlType)
                {
                    var newSqlType = _typeMapper.MapToSqlType(newField.DataType, newField.SqlType);
                    var oldSqlType = _typeMapper.MapToSqlType(oldField.DataType, oldField.SqlType);
                    var colName = ValidateColumnName(name);

                    if (IsWideningConversion(oldSqlType, newSqlType))
                    {
                        sb.AppendLine($"ALTER TABLE dbo.[{tableName}] ALTER COLUMN [{colName}] {newSqlType};");
                        rollback.AppendLine($"ALTER TABLE dbo.[{tableName}] ALTER COLUMN [{colName}] {oldSqlType};");
                    }
                }
            }
        }

        // Fields removed: preserve column but note in DDL
        foreach (var (name, _) in oldFields)
        {
            if (!newFields.ContainsKey(name))
            {
                sb.AppendLine($"-- NOTE: Field [{name}] removed from template but column preserved in table");
            }
        }

        return new DdlScript(sb.ToString(), rollback.ToString());
    }

    private static string ValidateTableName(string name)
    {
        if (!SafeNameRegex().IsMatch(name))
            throw new ArgumentException($"Invalid table name: {name}");
        return name;
    }

    private static string ValidateColumnName(string name)
    {
        if (!SafeNameRegex().IsMatch(name))
            throw new ArgumentException($"Invalid column name: {name}");
        return name;
    }

    private static bool IsWideningConversion(string oldType, string newType)
    {
        // Simple widening rules
        var old = oldType.ToUpperInvariant();
        var @new = newType.ToUpperInvariant();

        if (old == @new) return false;
        if (old.StartsWith("INT") && @new.StartsWith("BIGINT")) return true;
        if (old.StartsWith("NVARCHAR") && @new.StartsWith("NVARCHAR"))
        {
            var oldLen = ExtractLength(old);
            var newLen = ExtractLength(@new);
            return newLen > oldLen || newLen == -1; // -1 = MAX
        }
        if (old.StartsWith("DECIMAL") && @new.StartsWith("DECIMAL"))
        {
            var (oldP, oldS) = ExtractPrecision(old);
            var (newP, newS) = ExtractPrecision(@new);
            return newP >= oldP && newS >= oldS;
        }

        return false;
    }

    private static int ExtractLength(string type)
    {
        if (type.Contains("MAX")) return -1;
        var match = Regex.Match(type, @"\((\d+)\)");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    private static (int Precision, int Scale) ExtractPrecision(string type)
    {
        var match = Regex.Match(type, @"\((\d+),(\d+)\)");
        return match.Success
            ? (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value))
            : (0, 0);
    }

    [GeneratedRegex(@"^[a-z_][a-z0-9_]*$")]
    private static partial Regex SafeNameRegex();
}
