using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Infrastructure.DynamicSchema;
using FluentAssertions;

namespace FC.Engine.Infrastructure.Tests.DynamicSchema;

public class DdlEngineTests
{
    private readonly DdlEngine _engine;

    public DdlEngineTests()
    {
        _engine = new DdlEngine(new SqlTypeMapper());
    }

    [Fact]
    public void GenerateCreateTable_FixedRow_ShouldProduceValidDdl()
    {
        var template = CreateTemplate("mfcr_300");
        var version = CreateVersion(
            new TemplateField { FieldName = "cash_notes", DataType = FieldDataType.Money, FieldOrder = 1 },
            new TemplateField { FieldName = "cash_coins", DataType = FieldDataType.Money, FieldOrder = 2 },
            new TemplateField { FieldName = "total_cash", DataType = FieldDataType.Money, FieldOrder = 3, IsRequired = true }
        );

        var ddl = _engine.GenerateCreateTable(template, version);

        ddl.ForwardSql.Should().Contain("CREATE TABLE dbo.[mfcr_300]");
        ddl.ForwardSql.Should().Contain("[cash_notes] DECIMAL(20,2)");
        ddl.ForwardSql.Should().Contain("[cash_coins] DECIMAL(20,2)");
        ddl.ForwardSql.Should().Contain("[total_cash] DECIMAL(20,2) NOT NULL");
        ddl.ForwardSql.Should().Contain("submission_id INT NOT NULL");
        ddl.ForwardSql.Should().Contain("id INT IDENTITY(1,1) PRIMARY KEY");
        ddl.ForwardSql.Should().Contain("CREATE INDEX IX_mfcr_300_submission");

        ddl.RollbackSql.Should().Contain("DROP TABLE IF EXISTS dbo.[mfcr_300]");
    }

    [Fact]
    public void GenerateCreateTable_WithDefaultValue_ShouldIncludeDefault()
    {
        var template = CreateTemplate("mfcr_301");
        var version = CreateVersion(
            new TemplateField
            {
                FieldName = "status",
                DataType = FieldDataType.Text,
                SqlType = "NVARCHAR(50)",
                DefaultValue = "'Active'",
                FieldOrder = 1
            }
        );

        var ddl = _engine.GenerateCreateTable(template, version);

        ddl.ForwardSql.Should().Contain("[status] NVARCHAR(50) DEFAULT 'Active'");
    }

    [Fact]
    public void GenerateCreateTable_InvalidTableName_ShouldThrow()
    {
        var template = CreateTemplate("DROP TABLE;--");

        var act = () => _engine.GenerateCreateTable(template, CreateVersion());
        act.Should().Throw<ArgumentException>().WithMessage("*Invalid table name*");
    }

    [Fact]
    public void GenerateAlterTable_NewField_ShouldAddColumn()
    {
        var template = CreateTemplate("mfcr_300");
        var oldVersion = CreateVersion(
            new TemplateField { FieldName = "cash_notes", DataType = FieldDataType.Money, FieldOrder = 1 }
        );
        var newVersion = CreateVersion(
            new TemplateField { FieldName = "cash_notes", DataType = FieldDataType.Money, FieldOrder = 1 },
            new TemplateField { FieldName = "new_field", DataType = FieldDataType.Integer, FieldOrder = 2 }
        );

        var ddl = _engine.GenerateAlterTable(template, oldVersion, newVersion);

        ddl.ForwardSql.Should().Contain("ALTER TABLE dbo.[mfcr_300] ADD [new_field] INT");
        ddl.RollbackSql.Should().Contain("DROP COLUMN [new_field]");
    }

    [Fact]
    public void GenerateAlterTable_RemovedField_ShouldPreserveColumn()
    {
        var template = CreateTemplate("mfcr_300");
        var oldVersion = CreateVersion(
            new TemplateField { FieldName = "cash_notes", DataType = FieldDataType.Money, FieldOrder = 1 },
            new TemplateField { FieldName = "old_field", DataType = FieldDataType.Text, FieldOrder = 2 }
        );
        var newVersion = CreateVersion(
            new TemplateField { FieldName = "cash_notes", DataType = FieldDataType.Money, FieldOrder = 1 }
        );

        var ddl = _engine.GenerateAlterTable(template, oldVersion, newVersion);

        // Never drop columns — data preservation
        ddl.ForwardSql.Should().Contain("NOTE: Field [old_field] removed");
        ddl.ForwardSql.Should().NotContain("DROP COLUMN");
    }

    [Fact]
    public void GenerateAlterTable_WidenedType_ShouldAlterColumn()
    {
        var template = CreateTemplate("mfcr_300");
        var oldVersion = CreateVersion(
            new TemplateField { FieldName = "name_field", DataType = FieldDataType.Text, SqlType = "NVARCHAR(50)", FieldOrder = 1 }
        );
        var newVersion = CreateVersion(
            new TemplateField { FieldName = "name_field", DataType = FieldDataType.Text, SqlType = "NVARCHAR(255)", FieldOrder = 1 }
        );

        var ddl = _engine.GenerateAlterTable(template, oldVersion, newVersion);

        ddl.ForwardSql.Should().Contain("ALTER COLUMN [name_field] NVARCHAR(255)");
        ddl.RollbackSql.Should().Contain("ALTER COLUMN [name_field] NVARCHAR(50)");
    }

    [Fact]
    public void GenerateAlterTable_NoChanges_ShouldProduceEmptyDdl()
    {
        var template = CreateTemplate("mfcr_300");
        var version = CreateVersion(
            new TemplateField { FieldName = "cash_notes", DataType = FieldDataType.Money, FieldOrder = 1 }
        );

        var ddl = _engine.GenerateAlterTable(template, version, version);

        ddl.ForwardSql.Trim().Should().BeEmpty();
    }

    private static ReturnTemplate CreateTemplate(string tableName)
    {
        return new ReturnTemplate
        {
            Id = 1,
            ReturnCode = "MFCR 300",
            PhysicalTableName = tableName
        };
    }

    private static TemplateVersion CreateVersion(params TemplateField[] fields)
    {
        var version = new TemplateVersion { Id = 1, VersionNumber = 1, Status = TemplateStatus.Draft };
        foreach (var f in fields) version.AddField(f);
        return version;
    }
}

public class SqlTypeMapperTests
{
    private readonly SqlTypeMapper _mapper = new();

    [Theory]
    [InlineData(FieldDataType.Money, null, "DECIMAL(20,2)")]
    [InlineData(FieldDataType.Decimal, null, "DECIMAL(20,4)")]
    [InlineData(FieldDataType.Percentage, null, "DECIMAL(10,4)")]
    [InlineData(FieldDataType.Integer, null, "INT")]
    [InlineData(FieldDataType.Text, null, "NVARCHAR(255)")]
    [InlineData(FieldDataType.Date, null, "DATE")]
    [InlineData(FieldDataType.Boolean, null, "BIT")]
    public void MapToSqlType_DefaultTypes(FieldDataType dataType, string? sqlOverride, string expected)
    {
        _mapper.MapToSqlType(dataType, sqlOverride).Should().Be(expected);
    }

    [Fact]
    public void MapToSqlType_WithOverride_NormalizesNumericToDecimal()
    {
        // NUMERIC is normalized to DECIMAL for SQL Server compatibility
        _mapper.MapToSqlType(FieldDataType.Money, "NUMERIC(18,4)").Should().Be("DECIMAL(18,4)");
    }

    [Theory]
    [InlineData("BOOLEAN", "BIT")]
    [InlineData("INTEGER", "INT")]
    [InlineData("VARCHAR(100)", "NVARCHAR(100)")]
    [InlineData("TEXT", "NVARCHAR(MAX)")]
    [InlineData("TIMESTAMP", "DATETIME2")]
    public void MapToSqlType_NormalizesNonSqlServerTypes(string input, string expected)
    {
        _mapper.MapToSqlType(FieldDataType.Text, input).Should().Be(expected);
    }
}
