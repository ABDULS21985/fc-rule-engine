using System.Text.RegularExpressions;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;

namespace FC.Engine.Application.Services;

/// <summary>
/// Parses schema.sql to extract 103 template definitions and seeds them into metadata tables as Published.
/// Detects structural category: serial_no → MultiRow, item_code → ItemCoded, else → FixedRow.
/// </summary>
public class SeedService
{
    private readonly ITemplateRepository _templateRepo;

    // Tables to skip (not return templates)
    private static readonly HashSet<string> SkipTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "sheet2_return_codes", "cleaned_summary", "summary",
        "institutions", "return_periods", "return_submissions",
        "bank_codes", "sectors", "sub_sectors", "states", "local_governments"
    };

    // System columns to skip when extracting fields
    private static readonly HashSet<string> SystemColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "submission_id", "created_at", "updated_at", "serial_no", "item_code"
    };

    public SeedService(ITemplateRepository templateRepo)
    {
        _templateRepo = templateRepo;
    }

    public async Task<SeedResult> SeedFromSchema(string schemaFilePath, string performedBy, CancellationToken ct = default)
    {
        var sql = await File.ReadAllTextAsync(schemaFilePath, ct);
        var tables = ParseCreateTables(sql);
        var comments = ParseTableComments(sql);

        var result = new SeedResult();

        foreach (var table in tables)
        {
            if (SkipTables.Contains(table.TableName))
                continue;

            try
            {
                var returnCode = DeriveReturnCode(table.TableName);
                if (string.IsNullOrEmpty(returnCode)) continue;

                if (await _templateRepo.ExistsByReturnCode(returnCode, ct))
                {
                    result.Skipped.Add(returnCode);
                    continue;
                }

                var description = comments.GetValueOrDefault(table.TableName, returnCode);
                var frequency = DeriveFrequency(returnCode);
                var category = DeriveStructuralCategory(table);

                var template = new ReturnTemplate
                {
                    ReturnCode = returnCode,
                    Name = description,
                    Description = description,
                    Frequency = frequency,
                    StructuralCategory = category,
                    PhysicalTableName = table.TableName,
                    XmlRootElement = returnCode.Replace(" ", ""),
                    XmlNamespace = $"urn:cbn:dfis:fc:{returnCode.Replace(" ", "").ToLowerInvariant()}",
                    IsSystemTemplate = true,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = performedBy,
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedBy = performedBy
                };

                // Create version 1 as Published
                var version = template.CreateDraftVersion(performedBy);

                // Add serial_no / item_code as key fields if applicable
                int fieldOrder = 0;
                if (category == StructuralCategory.MultiRow)
                {
                    version.AddField(CreateKeyField("serial_no", "Serial Number", FieldDataType.Integer, "INT", ++fieldOrder));
                }
                else if (category == StructuralCategory.ItemCoded)
                {
                    var itemCodeCol = table.Columns.FirstOrDefault(c => c.Name.Equals("item_code", StringComparison.OrdinalIgnoreCase));
                    var sqlType = itemCodeCol?.SqlType ?? "VARCHAR(20)";
                    var dataType = sqlType.Contains("INT", StringComparison.OrdinalIgnoreCase)
                        ? FieldDataType.Integer : FieldDataType.Text;
                    version.AddField(CreateKeyField("item_code", "Item Code", dataType, sqlType, ++fieldOrder));
                }

                // Add data columns as fields
                foreach (var col in table.Columns)
                {
                    if (SystemColumns.Contains(col.Name)) continue;

                    var dataType = MapSqlTypeToDataType(col.SqlType);
                    var isYtd = col.Name.EndsWith("_ytd", StringComparison.OrdinalIgnoreCase);

                    version.AddField(new TemplateField
                    {
                        FieldName = col.Name,
                        DisplayName = DeriveDisplayName(col.Name),
                        XmlElementName = ToPascalCase(col.Name),
                        LineCode = col.LineCode,
                        FieldOrder = ++fieldOrder,
                        DataType = dataType,
                        SqlType = col.SqlType,
                        IsRequired = false,
                        IsComputed = col.Name.StartsWith("total_", StringComparison.OrdinalIgnoreCase)
                            || col.Name.StartsWith("net_", StringComparison.OrdinalIgnoreCase),
                        IsYtdField = isYtd,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                // Publish immediately (these are existing tables)
                version.SubmitForReview();
                version.Publish(DateTime.UtcNow, performedBy);

                await _templateRepo.Add(template, ct);
                result.Created.Add(returnCode);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{table.TableName}: {ex.Message}");
            }
        }

        return result;
    }

    private static TemplateField CreateKeyField(string name, string display, FieldDataType dataType, string sqlType, int order)
    {
        return new TemplateField
        {
            FieldName = name,
            DisplayName = display,
            XmlElementName = ToPascalCase(name),
            FieldOrder = order,
            DataType = dataType,
            SqlType = sqlType,
            IsRequired = true,
            IsKeyField = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static List<ParsedTable> ParseCreateTables(string sql)
    {
        var tables = new List<ParsedTable>();
        var tableRegex = new Regex(
            @"CREATE\s+TABLE\s+(\w+)\s*\((.*?)\);",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match match in tableRegex.Matches(sql))
        {
            var tableName = match.Groups[1].Value;
            var body = match.Groups[2].Value;
            var columns = ParseColumns(body);
            tables.Add(new ParsedTable(tableName, columns));
        }

        return tables;
    }

    private static List<ParsedColumn> ParseColumns(string body)
    {
        var columns = new List<ParsedColumn>();
        var lines = body.Split('\n')
            .Select(l => l.Trim().TrimEnd(','))
            .Where(l => !string.IsNullOrWhiteSpace(l));

        foreach (var line in lines)
        {
            // Skip constraints and keys
            if (line.StartsWith("PRIMARY", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("FOREIGN", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("CHECK", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("--", StringComparison.Ordinal) ||
                line.StartsWith(")", StringComparison.Ordinal))
                continue;

            var colMatch = Regex.Match(line,
                @"^(\w+)\s+(SERIAL|INT|INTEGER|BIGINT|NUMERIC\(\d+,\d+\)|VARCHAR\(\d+\)|TEXT|DATE|TIMESTAMP|BOOLEAN)",
                RegexOptions.IgnoreCase);

            if (!colMatch.Success) continue;

            var colName = colMatch.Groups[1].Value;
            var sqlType = colMatch.Groups[2].Value.ToUpperInvariant();

            // Normalize SERIAL to INT
            if (sqlType == "SERIAL") sqlType = "INT";

            // Extract line code from comment
            string? lineCode = null;
            var commentMatch = Regex.Match(line, @"--\s*(\d{4,5})");
            if (commentMatch.Success)
                lineCode = commentMatch.Groups[1].Value;

            columns.Add(new ParsedColumn(colName, sqlType, lineCode));
        }

        return columns;
    }

    private static Dictionary<string, string> ParseTableComments(string sql)
    {
        var comments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var regex = new Regex(
            @"--\s*TABLE\s+\d+:\s*(.+?)\s*-\s*(.+)\s*\n.*?CREATE\s+TABLE\s+(\w+)",
            RegexOptions.IgnoreCase);

        foreach (Match match in regex.Matches(sql))
        {
            var returnCode = match.Groups[1].Value.Trim();
            var description = match.Groups[2].Value.Trim();
            var tableName = match.Groups[3].Value.Trim();

            comments[tableName] = $"{returnCode} - {description}";
        }

        return comments;
    }

    private static string DeriveReturnCode(string tableName)
    {
        // mfcr_300 → MFCR 300, qfcr_364 → QFCR 364, sfcr_400 → SFCR 400
        // mfcr_306_1 → MFCR 306-1, fc_100 → FC 100
        var match = Regex.Match(tableName, @"^(mfcr|qfcr|sfcr|fc)_(\d+)(?:_(\d+))?$", RegexOptions.IgnoreCase);
        if (!match.Success) return string.Empty;

        var prefix = match.Groups[1].Value.ToUpperInvariant();
        var number = match.Groups[2].Value;
        var suffix = match.Groups[3].Success ? $"-{match.Groups[3].Value}" : "";

        return $"{prefix} {number}{suffix}";
    }

    private static ReturnFrequency DeriveFrequency(string returnCode)
    {
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

    private static StructuralCategory DeriveStructuralCategory(ParsedTable table)
    {
        var colNames = table.Columns.Select(c => c.Name.ToLowerInvariant()).ToHashSet();
        if (colNames.Contains("serial_no")) return StructuralCategory.MultiRow;
        if (colNames.Contains("item_code")) return StructuralCategory.ItemCoded;
        return StructuralCategory.FixedRow;
    }

    private static FieldDataType MapSqlTypeToDataType(string sqlType)
    {
        return sqlType.ToUpperInvariant() switch
        {
            var s when s.StartsWith("NUMERIC") => FieldDataType.Money,
            "INT" or "INTEGER" or "BIGINT" => FieldDataType.Integer,
            var s when s.StartsWith("VARCHAR") => FieldDataType.Text,
            "TEXT" => FieldDataType.Text,
            "DATE" => FieldDataType.Date,
            "TIMESTAMP" => FieldDataType.Date,
            "BOOLEAN" => FieldDataType.Boolean,
            _ => FieldDataType.Text
        };
    }

    private static string DeriveDisplayName(string fieldName)
    {
        // cash_notes → Cash Notes, total_due_from_banks → Total Due From Banks
        return string.Join(" ",
            fieldName.Split('_')
                .Select(w => w.Length > 0
                    ? char.ToUpperInvariant(w[0]) + w[1..]
                    : w));
    }

    private static string ToPascalCase(string fieldName)
    {
        return string.Concat(
            fieldName.Split('_')
                .Select(w => w.Length > 0
                    ? char.ToUpperInvariant(w[0]) + w[1..]
                    : w));
    }
}

public record ParsedTable(string TableName, List<ParsedColumn> Columns);
public record ParsedColumn(string Name, string SqlType, string? LineCode);

public class SeedResult
{
    public List<string> Created { get; set; } = new();
    public List<string> Skipped { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
