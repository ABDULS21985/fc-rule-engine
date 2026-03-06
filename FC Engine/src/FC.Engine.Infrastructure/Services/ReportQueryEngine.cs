using System.Diagnostics;
using System.Text.RegularExpressions;
using Dapper;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public partial class ReportQueryEngine : IReportQueryEngine
{
    private const int MaxRowLimit = 10_000;
    private const int QueryTimeoutSeconds = 30;

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ITemplateMetadataCache _cache;
    private readonly ILogger<ReportQueryEngine> _logger;

    public ReportQueryEngine(
        IDbConnectionFactory connectionFactory,
        ITemplateMetadataCache cache,
        ILogger<ReportQueryEngine> logger)
    {
        _connectionFactory = connectionFactory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ReportQueryResult> Execute(
        ReportDefinition definition,
        Guid tenantId,
        List<string> entitledModuleCodes,
        CancellationToken ct = default)
    {
        if (definition.Fields.Count == 0)
            throw new ArgumentException("Report must have at least one field.");

        // Validate entitlements: all fields must be from entitled modules
        foreach (var field in definition.Fields)
        {
            if (!entitledModuleCodes.Contains(field.ModuleCode, StringComparer.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException(
                    $"Not entitled to module '{field.ModuleCode}'.");
        }

        // Resolve template metadata for all referenced templates
        var templateCodes = definition.Fields
            .Select(f => f.TemplateCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var templateMeta = new Dictionary<string, TemplateMeta>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in templateCodes)
        {
            var template = await _cache.GetPublishedTemplate(code, ct);
            templateMeta[code] = new TemplateMeta
            {
                ReturnCode = code,
                PhysicalTableName = template.PhysicalTableName,
                Fields = template.CurrentVersion.Fields
                    .ToDictionary(f => f.FieldName, StringComparer.OrdinalIgnoreCase)
            };
        }

        // Validate all referenced fields exist in their templates
        foreach (var field in definition.Fields)
        {
            if (!templateMeta.TryGetValue(field.TemplateCode, out var meta))
                throw new ArgumentException($"Unknown template '{field.TemplateCode}'.");

            if (!meta.Fields.ContainsKey(field.FieldCode))
                throw new ArgumentException(
                    $"Field '{field.FieldCode}' not found in template '{field.TemplateCode}'.");
        }

        var (sql, parameters) = BuildReportQuery(definition, tenantId, templateMeta);

        _logger.LogInformation(
            "Executing report query for tenant {TenantId}: {FieldCount} fields, {FilterCount} filters",
            tenantId, definition.Fields.Count, definition.Filters.Count);

        var sw = Stopwatch.StartNew();

        using var connection = await _connectionFactory.CreateConnectionAsync(tenantId, ct);
        var rows = await connection.QueryAsync(
            new CommandDefinition(
                sql,
                parameters,
                commandTimeout: QueryTimeoutSeconds,
                cancellationToken: ct));

        sw.Stop();

        var resultRows = new List<Dictionary<string, object?>>();
        var columns = new List<string>();
        var first = true;

        foreach (IDictionary<string, object> row in rows)
        {
            if (first)
            {
                columns.AddRange(row.Keys);
                first = false;
            }

            resultRows.Add(row.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value == DBNull.Value ? null : kvp.Value,
                StringComparer.OrdinalIgnoreCase));
        }

        return new ReportQueryResult
        {
            Columns = columns,
            Rows = resultRows,
            TotalRowCount = resultRows.Count,
            QueryDurationMs = (int)sw.ElapsedMilliseconds
        };
    }

    private (string sql, DynamicParameters parameters) BuildReportQuery(
        ReportDefinition definition,
        Guid tenantId,
        Dictionary<string, TemplateMeta> templateMeta)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@TenantId", tenantId);

        var selectClauses = new List<string>();
        var joinClauses = new List<string>();
        var whereClauses = new List<string>();
        var groupByClauses = new List<string>();
        var orderByClauses = new List<string>();

        // Determine the primary template (first field's template)
        var primaryField = definition.Fields[0];
        var primaryMeta = templateMeta[primaryField.TemplateCode];
        var primaryTable = primaryMeta.PhysicalTableName;
        ValidateName(primaryTable);

        // Build SELECT for non-aggregated fields
        foreach (var field in definition.Fields)
        {
            var meta = templateMeta[field.TemplateCode];
            ValidateName(field.FieldCode);
            ValidateName(meta.PhysicalTableName);

            var tableRef = meta.PhysicalTableName == primaryTable
                ? $"[{primaryTable}]"
                : $"[{meta.PhysicalTableName}]";

            var alias = field.Alias ?? field.FieldCode;
            ValidateAlias(alias);
            selectClauses.Add($"{tableRef}.[{field.FieldCode}] AS [{alias}]");
        }

        // Build SELECT for aggregations
        foreach (var agg in definition.Aggregations)
        {
            ValidateName(agg.Field);
            var func = MapAggregateFunction(agg.Function);
            var alias = agg.Alias ?? $"{func}_{agg.Field}";
            ValidateAlias(alias);
            selectClauses.Add($"{func}([{agg.Field}]) AS [{alias}]");
        }

        // Add submission metadata columns (period, institution)
        selectClauses.Add("i.[InstitutionName]");
        selectClauses.Add("rp.[Year] AS [period_year]");
        selectClauses.Add("rp.[Month] AS [period_month]");

        // FROM + mandatory JOINs
        var fromClause = $"dbo.[{primaryTable}]";
        joinClauses.Add(
            $"INNER JOIN dbo.[return_submissions] s ON [{primaryTable}].submission_id = s.Id AND s.TenantId = @TenantId");
        joinClauses.Add(
            "INNER JOIN dbo.[institutions] i ON s.InstitutionId = i.Id AND i.TenantId = @TenantId");
        joinClauses.Add(
            "INNER JOIN dbo.[return_periods] rp ON s.ReturnPeriodId = rp.Id AND rp.TenantId = @TenantId");

        // Additional template JOINs
        foreach (var field in definition.Fields.Skip(1))
        {
            var meta = templateMeta[field.TemplateCode];
            if (meta.PhysicalTableName == primaryTable) continue;

            ValidateName(meta.PhysicalTableName);
            var joinSql = $"LEFT JOIN dbo.[{meta.PhysicalTableName}] " +
                          $"ON [{meta.PhysicalTableName}].submission_id = s.Id " +
                          $"AND [{meta.PhysicalTableName}].TenantId = @TenantId";

            if (!joinClauses.Contains(joinSql))
                joinClauses.Add(joinSql);
        }

        // WHERE: always include tenant filter
        whereClauses.Add($"[{primaryTable}].TenantId = @TenantId");

        // User filters — all values via parameters
        var paramIndex = 0;
        foreach (var filter in definition.Filters)
        {
            ValidateName(filter.Field);
            var op = MapOperator(filter.Operator);
            var paramName = $"@filter_{paramIndex++}";

            if (op == "IN")
            {
                var values = filter.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                parameters.Add(paramName, values);
                whereClauses.Add($"[{filter.Field}] IN {paramName}");
            }
            else if (op == "LIKE")
            {
                parameters.Add(paramName, $"%{filter.Value}%");
                whereClauses.Add($"[{filter.Field}] {op} {paramName}");
            }
            else
            {
                parameters.Add(paramName, filter.Value);
                whereClauses.Add($"[{filter.Field}] {op} {paramName}");
            }
        }

        // GROUP BY
        foreach (var gb in definition.GroupBy)
        {
            ValidateName(gb);
            groupByClauses.Add($"[{gb}]");
        }

        // When aggregations exist, add non-aggregated SELECT fields to GROUP BY
        if (definition.Aggregations.Count > 0 && groupByClauses.Count == 0)
        {
            foreach (var field in definition.Fields)
            {
                var alias = field.Alias ?? field.FieldCode;
                ValidateName(field.FieldCode);
                var meta = templateMeta[field.TemplateCode];
                var tableRef = meta.PhysicalTableName == primaryTable
                    ? $"[{primaryTable}]"
                    : $"[{meta.PhysicalTableName}]";
                groupByClauses.Add($"{tableRef}.[{field.FieldCode}]");
            }

            groupByClauses.Add("i.[InstitutionName]");
            groupByClauses.Add("rp.[Year]");
            groupByClauses.Add("rp.[Month]");
        }

        // ORDER BY
        foreach (var ob in definition.SortBy)
        {
            ValidateName(ob.Field);
            var dir = ob.Direction?.Equals("DESC", StringComparison.OrdinalIgnoreCase) == true
                ? "DESC"
                : "ASC";
            orderByClauses.Add($"[{ob.Field}] {dir}");
        }

        if (orderByClauses.Count == 0)
            orderByClauses.Add("1");

        // LIMIT
        var limit = Math.Min(definition.Limit > 0 ? definition.Limit : 1000, MaxRowLimit);

        var sql = $"""
            SELECT TOP ({limit}) {string.Join(", ", selectClauses)}
            FROM {fromClause}
            {string.Join(Environment.NewLine, joinClauses)}
            WHERE {string.Join(" AND ", whereClauses)}
            {(groupByClauses.Count > 0 ? $"GROUP BY {string.Join(", ", groupByClauses)}" : "")}
            ORDER BY {string.Join(", ", orderByClauses)}
            """;

        return (sql, parameters);
    }

    private static string MapOperator(string op) => op.ToUpperInvariant() switch
    {
        "=" or "==" or "EQUALS" => "=",
        "!=" or "<>" or "NOTEQUALS" => "<>",
        ">" or "GREATERTHAN" => ">",
        ">=" or "GREATERTHANOREQUAL" => ">=",
        "<" or "LESSTHAN" => "<",
        "<=" or "LESSTHANOREQUAL" => "<=",
        "LIKE" or "CONTAINS" => "LIKE",
        "IN" => "IN",
        _ => throw new ArgumentException($"Invalid filter operator: {op}")
    };

    private static string MapAggregateFunction(string func) => func.ToUpperInvariant() switch
    {
        "SUM" => "SUM",
        "AVG" => "AVG",
        "MIN" => "MIN",
        "MAX" => "MAX",
        "COUNT" => "COUNT",
        _ => throw new ArgumentException($"Invalid aggregate function: {func}")
    };

    private static void ValidateName(string name)
    {
        if (!SafeNameRegex().IsMatch(name))
            throw new ArgumentException($"Invalid SQL identifier: {name}");
    }

    private static void ValidateAlias(string alias)
    {
        if (!SafeAliasRegex().IsMatch(alias))
            throw new ArgumentException($"Invalid column alias: {alias}");
    }

    [GeneratedRegex(@"^[a-z_][a-z0-9_]*$", RegexOptions.IgnoreCase)]
    private static partial Regex SafeNameRegex();

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_ %#]*$")]
    private static partial Regex SafeAliasRegex();

    private class TemplateMeta
    {
        public string ReturnCode { get; set; } = string.Empty;
        public string PhysicalTableName { get; set; } = string.Empty;
        public Dictionary<string, Domain.Metadata.TemplateField> Fields { get; set; } = new();
    }
}
