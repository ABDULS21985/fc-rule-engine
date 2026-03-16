using System.Data;
using Dapper;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.DataRecord;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Persistence;

public class GenericDataRepository : IGenericDataRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ITenantContext _tenantContext;
    private readonly ITemplateMetadataCache _cache;
    private readonly DynamicSqlBuilder _sqlBuilder;
    private readonly IDbContextFactory<MetadataDbContext> _metadataDbFactory;

    public GenericDataRepository(
        IDbConnectionFactory connectionFactory,
        ITenantContext tenantContext,
        ITemplateMetadataCache cache,
        DynamicSqlBuilder sqlBuilder,
        IDbContextFactory<MetadataDbContext> metadataDbFactory)
    {
        _connectionFactory = connectionFactory;
        _tenantContext = tenantContext;
        _cache = cache;
        _sqlBuilder = sqlBuilder;
        _metadataDbFactory = metadataDbFactory;
    }

    public async Task Save(ReturnDataRecord record, int submissionId, CancellationToken ct = default)
    {
        var template = await _cache.GetPublishedTemplate(record.ReturnCode, ct);
        var tableName = template.PhysicalTableName;
        var fields = template.CurrentVersion.Fields;
        var tenantId = _tenantContext.CurrentTenantId;
        if (!tenantId.HasValue)
        {
            throw new InvalidOperationException("Tenant context is required for dynamic table inserts.");
        }

        using var connection = await CreateConnectionAsync(ct);
        foreach (var row in record.Rows)
        {
            var (sql, parameters) = _sqlBuilder.BuildInsert(tableName, fields, row, submissionId, tenantId);
            await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: ct));
        }
    }

    public async Task<ReturnDataRecord?> GetBySubmission(
        string returnCode, int submissionId, CancellationToken ct = default)
    {
        var template = await _cache.GetPublishedTemplate(returnCode, ct);
        var tableName = template.PhysicalTableName;
        var fields = template.CurrentVersion.Fields;
        var category = Enum.Parse<StructuralCategory>(template.StructuralCategory);
        var tenantId = _tenantContext.CurrentTenantId;

        var sql = _sqlBuilder.BuildSelect(tableName, fields, tenantId);
        object queryParams = tenantId.HasValue
            ? new { submissionId, TenantId = tenantId.Value }
            : new { submissionId };

        using var connection = await CreateConnectionAsync(ct);
        var rows = await connection.QueryAsync(
            new CommandDefinition(sql, queryParams, cancellationToken: ct));

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
        var tenantId = _tenantContext.CurrentTenantId;

        var sql = _sqlBuilder.BuildSelectByInstitutionAndPeriod(tableName, fields, tenantId);
        object queryParams = tenantId.HasValue
            ? new { institutionId, returnPeriodId, TenantId = tenantId.Value }
            : new { institutionId, returnPeriodId };

        using var connection = await CreateConnectionAsync(ct);
        var rows = await connection.QueryAsync(
            new CommandDefinition(sql, queryParams, cancellationToken: ct));

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
        var tenantId = _tenantContext.CurrentTenantId;
        var sql = _sqlBuilder.BuildDeleteBySubmission(template.PhysicalTableName, tenantId);
        object parameters = tenantId.HasValue
            ? new { submissionId, TenantId = tenantId.Value }
            : new { submissionId };

        using var connection = await CreateConnectionAsync(ct);
        await connection.ExecuteAsync(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
    }

    public async Task<object?> ReadFieldValue(
        string returnCode,
        int submissionId,
        string fieldName,
        CancellationToken ct = default)
    {
        var template = await _cache.GetPublishedTemplate(returnCode, ct);
        var tenantId = _tenantContext.CurrentTenantId;
        var sql = _sqlBuilder.BuildSelectFieldBySubmission(template.PhysicalTableName, fieldName, tenantId);
        object parameters = tenantId.HasValue
            ? new { submissionId, TenantId = tenantId.Value }
            : new { submissionId };

        using var connection = await CreateConnectionAsync(ct);
        return await connection.ExecuteScalarAsync<object?>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
    }

    public async Task WriteFieldValue(
        string returnCode,
        int submissionId,
        string fieldName,
        object? value,
        string? dataSource = null,
        string? sourceDetail = null,
        string? changedBy = null,
        CancellationToken ct = default)
    {
        var template = await _cache.GetPublishedTemplate(returnCode, ct);
        var tenantId = _tenantContext.CurrentTenantId;

        using var connection = await CreateConnectionAsync(ct);
        var existing = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                $"SELECT COUNT(1) FROM dbo.[{template.PhysicalTableName}] WHERE submission_id = @submissionId" +
                (tenantId.HasValue ? " AND TenantId = @TenantId" : string.Empty),
                tenantId.HasValue
                    ? new { submissionId, TenantId = tenantId.Value }
                    : new { submissionId },
                cancellationToken: ct));

        if (existing > 0)
        {
            // Capture current value before update for change history
            var oldValue = await ReadFieldValueInternal(connection, template.PhysicalTableName, submissionId, fieldName, tenantId, ct);

            var updateSql = _sqlBuilder.BuildUpdateFieldBySubmission(template.PhysicalTableName, fieldName, tenantId);
            await connection.ExecuteAsync(new CommandDefinition(
                updateSql,
                tenantId.HasValue
                    ? new { submissionId, TenantId = tenantId.Value, value }
                    : new { submissionId, value },
                cancellationToken: ct));
            await UpsertFieldSourceMetadata(returnCode, submissionId, fieldName, dataSource, sourceDetail, ct);
            await RecordFieldChange(returnCode, submissionId, fieldName, oldValue, value, dataSource, sourceDetail, changedBy, ct);
            return;
        }

        var insertSql = _sqlBuilder.BuildInsertSingleField(template.PhysicalTableName, fieldName, tenantId);
        await connection.ExecuteAsync(new CommandDefinition(
            insertSql,
            tenantId.HasValue
                ? new { submissionId, TenantId = tenantId.Value, value }
                : new { submissionId, value },
            cancellationToken: ct));
        await UpsertFieldSourceMetadata(returnCode, submissionId, fieldName, dataSource, sourceDetail, ct);
        await RecordFieldChange(returnCode, submissionId, fieldName, null, value, dataSource, sourceDetail, changedBy, ct);
    }

    private async Task<object?> ReadFieldValueInternal(
        IDbConnection connection,
        string tableName,
        int submissionId,
        string fieldName,
        Guid? tenantId,
        CancellationToken ct)
    {
        var sql = _sqlBuilder.BuildSelectFieldBySubmission(tableName, fieldName, tenantId);
        return await connection.ExecuteScalarAsync<object?>(
            new CommandDefinition(sql,
                tenantId.HasValue
                    ? new { submissionId, TenantId = tenantId.Value }
                    : new { submissionId },
                cancellationToken: ct));
    }

    private async Task RecordFieldChange(
        string returnCode,
        int submissionId,
        string fieldName,
        object? oldValue,
        object? newValue,
        string? dataSource,
        string? sourceDetail,
        string? changedBy,
        CancellationToken ct)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        if (!tenantId.HasValue) return;

        await using var metadataDb = await _metadataDbFactory.CreateDbContextAsync(ct);
        metadataDb.FieldChangeHistory.Add(new FieldChangeHistory
        {
            TenantId = tenantId.Value,
            SubmissionId = submissionId,
            ReturnCode = returnCode,
            FieldName = fieldName,
            OldValue = oldValue?.ToString(),
            NewValue = newValue?.ToString(),
            ChangeSource = dataSource ?? "Manual",
            SourceDetail = sourceDetail,
            ChangedBy = changedBy ?? "System",
            ChangedAt = DateTime.UtcNow
        });
        await metadataDb.SaveChangesAsync(ct);
    }

    private async Task UpsertFieldSourceMetadata(
        string returnCode,
        int submissionId,
        string fieldName,
        string? dataSource,
        string? sourceDetail,
        CancellationToken ct)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        if (!tenantId.HasValue)
        {
            return;
        }

        var effectiveDataSource = string.IsNullOrWhiteSpace(dataSource) ? "Manual" : dataSource.Trim();

        await using var metadataDb = await _metadataDbFactory.CreateDbContextAsync(ct);
        var entry = await metadataDb.SubmissionFieldSources.FirstOrDefaultAsync(
            x => x.TenantId == tenantId.Value
                 && x.ReturnCode == returnCode
                 && x.SubmissionId == submissionId
                 && x.FieldName == fieldName,
            ct);

        if (entry is null)
        {
            metadataDb.SubmissionFieldSources.Add(new SubmissionFieldSource
            {
                TenantId = tenantId.Value,
                ReturnCode = returnCode,
                SubmissionId = submissionId,
                FieldName = fieldName,
                DataSource = effectiveDataSource,
                SourceDetail = sourceDetail,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            entry.DataSource = effectiveDataSource;
            entry.SourceDetail = sourceDetail;
            entry.UpdatedAt = DateTime.UtcNow;
        }

        await metadataDb.SaveChangesAsync(ct);
    }

    private async Task<IDbConnection> CreateConnectionAsync(CancellationToken ct)
    {
        return await _connectionFactory.CreateConnectionAsync(_tenantContext.CurrentTenantId, ct);
    }
}
