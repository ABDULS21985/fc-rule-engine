using FC.Engine.Domain.DataRecord;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Infrastructure.Persistence;
using FluentAssertions;

namespace FC.Engine.Infrastructure.Tests.Persistence;

public class DynamicSqlBuilderTests
{
    private readonly DynamicSqlBuilder _builder = new();

    [Fact]
    public void BuildInsert_ShouldGenerateParameterizedInsert()
    {
        var fields = CreateFields("cash_notes", "cash_coins");
        var row = new ReturnDataRow();
        row.SetValue("cash_notes", 100m);
        row.SetValue("cash_coins", 50m);

        var (sql, parameters) = _builder.BuildInsert("mfcr_300", fields, row, 42);

        sql.Should().Contain("INSERT INTO dbo.[mfcr_300]");
        sql.Should().Contain("[cash_notes]");
        sql.Should().Contain("[cash_coins]");
        sql.Should().Contain("@submission_id");
        sql.Should().Contain("@cash_notes");
        sql.Should().Contain("@cash_coins");
    }

    [Fact]
    public void BuildInsert_NullValues_ShouldSkipNullFields()
    {
        var fields = CreateFields("cash_notes", "cash_coins");
        var row = new ReturnDataRow();
        row.SetValue("cash_notes", 100m);
        // cash_coins not set (null)

        var (sql, _) = _builder.BuildInsert("mfcr_300", fields, row, 1);

        sql.Should().Contain("[cash_notes]");
        sql.Should().NotContain("[cash_coins]");
    }

    [Fact]
    public void BuildSelect_ShouldGenerateSelectWithWhereClause()
    {
        var fields = CreateFields("cash_notes", "cash_coins");

        var sql = _builder.BuildSelect("mfcr_300", fields);

        sql.Should().Contain("SELECT id, submission_id, [cash_notes], [cash_coins]");
        sql.Should().Contain("FROM dbo.[mfcr_300]");
        sql.Should().Contain("WHERE submission_id = @submissionId");
        sql.Should().Contain("ORDER BY id");
    }

    [Fact]
    public void BuildSelectByInstitutionAndPeriod_ShouldJoinSubmissions()
    {
        var fields = CreateFields("cash_notes");

        var sql = _builder.BuildSelectByInstitutionAndPeriod("mfcr_300", "MFCR_300", fields);

        sql.Should().Contain("SELECT TOP 1 s.Id FROM dbo.return_submissions s");
        sql.Should().Contain("s.InstitutionId = @institutionId");
        sql.Should().Contain("s.ReturnPeriodId = @returnPeriodId");
        sql.Should().Contain("s.ReturnCode = @returnCode");
        sql.Should().Contain("s.Status NOT IN ('Historical', 'Rejected')");
        sql.Should().Contain("ORDER BY s.SubmittedAt DESC, s.Id DESC");
    }

    [Fact]
    public void BuildInsert_InvalidTableName_ShouldThrow()
    {
        var fields = CreateFields("field");
        var row = new ReturnDataRow();
        row.SetValue("field", 1);

        var act = () => _builder.BuildInsert("DROP TABLE;--", fields, row, 1);
        act.Should().Throw<ArgumentException>().WithMessage("*Invalid SQL identifier*");
    }

    private static List<TemplateField> CreateFields(params string[] names)
    {
        return names.Select((n, i) => new TemplateField
        {
            FieldName = n,
            DataType = FieldDataType.Money,
            FieldOrder = i + 1
        }).ToList();
    }
}
