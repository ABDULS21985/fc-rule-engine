using System.Data;
using Dapper;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.DataRecord;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Infrastructure.Persistence;

public class GenericDataRepository : IGenericDataRepository
{
    private readonly IDbConnection _connection;
    private readonly ITemplateMetadataCache _cache;
    private readonly DynamicSqlBuilder _sqlBuilder;

    public GenericDataRepository(
        IDbConnection connection,
        ITemplateMetadataCache cache,
        DynamicSqlBuilder sqlBuilder)
    {
        _connection = connection;
        _cache = cache;
        _sqlBuilder = sqlBuilder;
    }

    public async Task Save(ReturnDataRecord record, int submissionId, CancellationToken ct = default)
    {
        var template = await _cache.GetPublishedTemplate(record.ReturnCode, ct);
        var tableName = template.PhysicalTableName;
        var fields = template.CurrentVersion.Fields;

        foreach (var row in record.Rows)
        {
            var (sql, parameters) = _sqlBuilder.BuildInsert(tableName, fields, row, submissionId);
            await _connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: ct));
        }
    }

    public async Task<ReturnDataRecord?> GetBySubmission(
        string returnCode, int submissionId, CancellationToken ct = default)
    {
        var template = await _cache.GetPublishedTemplate(returnCode, ct);
        var tableName = template.PhysicalTableName;
        var fields = template.CurrentVersion.Fields;
        var category = Enum.Parse<StructuralCategory>(template.StructuralCategory);

        var sql = _sqlBuilder.BuildSelect(tableName, fields);
        var rows = await _connection.QueryAsync(
            new CommandDefinition(sql, new { submissionId }, cancellationToken: ct));

        var rowList = rows.ToList();
        if (!rowList.Any()) return null;

        var record = new ReturnDataRecord(returnCode, template.CurrentVersion.Id, category);

        foreach (IDictionary<string, object> dbRow in rowList)
        {
            var dataRow = new ReturnDataRow();

            if (category == StructuralCategory.MultiRow)
                dataRow.RowKey = (dbRow.TryGetValue("serial_no", out var sn) ? sn?.ToString() : null);
            else if (category == StructuralCategory.ItemCoded)
                dataRow.RowKey = (dbRow.TryGetValue("item_code", out var ic) ? ic?.ToString() : null);

            foreach (var field in fields)
            {
                if (dbRow.TryGetValue(field.FieldName, out var value) && value != null)
                    dataRow.SetValue(field.FieldName, value);
            }

            record.AddRow(dataRow);
        }

        return record;
    }

    public async Task<ReturnDataRecord?> GetByInstitutionAndPeriod(
        string returnCode, int institutionId, int returnPeriodId, CancellationToken ct = default)
    {
        var template = await _cache.GetPublishedTemplate(returnCode, ct);
        var tableName = template.PhysicalTableName;
        var fields = template.CurrentVersion.Fields;
        var category = Enum.Parse<StructuralCategory>(template.StructuralCategory);

        var sql = _sqlBuilder.BuildSelectByInstitutionAndPeriod(tableName, fields);
        var rows = await _connection.QueryAsync(
            new CommandDefinition(sql, new { institutionId, returnPeriodId }, cancellationToken: ct));

        var rowList = rows.ToList();
        if (!rowList.Any()) return null;

        var record = new ReturnDataRecord(returnCode, template.CurrentVersion.Id, category);

        foreach (IDictionary<string, object> dbRow in rowList)
        {
            var dataRow = new ReturnDataRow();

            if (category == StructuralCategory.MultiRow)
                dataRow.RowKey = (dbRow.TryGetValue("serial_no", out var sn) ? sn?.ToString() : null);
            else if (category == StructuralCategory.ItemCoded)
                dataRow.RowKey = (dbRow.TryGetValue("item_code", out var ic) ? ic?.ToString() : null);

            foreach (var field in fields)
            {
                if (dbRow.TryGetValue(field.FieldName, out var value) && value != null)
                    dataRow.SetValue(field.FieldName, value);
            }

            record.AddRow(dataRow);
        }

        return record;
    }

    public async Task DeleteBySubmission(string returnCode, int submissionId, CancellationToken ct = default)
    {
        var template = await _cache.GetPublishedTemplate(returnCode, ct);
        var sql = $"DELETE FROM dbo.[{template.PhysicalTableName}] WHERE submission_id = @submissionId";
        await _connection.ExecuteAsync(
            new CommandDefinition(sql, new { submissionId }, cancellationToken: ct));
    }
}
