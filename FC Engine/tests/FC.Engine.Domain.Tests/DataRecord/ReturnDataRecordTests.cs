using FC.Engine.Domain.DataRecord;
using FC.Engine.Domain.Enums;
using FluentAssertions;

namespace FC.Engine.Domain.Tests.DataRecord;

public class ReturnDataRecordTests
{
    [Fact]
    public void FixedRow_SingleRow_ShouldReturnTheOnlyRow()
    {
        var record = new ReturnDataRecord("MFCR 300", 1, StructuralCategory.FixedRow);
        var row = new ReturnDataRow();
        row.SetValue("cash_notes", 1000m);
        record.AddRow(row);

        record.SingleRow.Should().BeSameAs(row);
        record.Rows.Should().HaveCount(1);
    }

    [Fact]
    public void SingleRow_OnMultiRow_ShouldThrow()
    {
        var record = new ReturnDataRecord("MFCR 360", 1, StructuralCategory.MultiRow);
        var row = new ReturnDataRow { RowKey = "1" };
        record.AddRow(row);

        var act = () => record.SingleRow;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetValue_FixedRow_ShouldReturnFieldValue()
    {
        var record = new ReturnDataRecord("MFCR 300", 1, StructuralCategory.FixedRow);
        var row = new ReturnDataRow();
        row.SetValue("cash_notes", 1500m);
        record.AddRow(row);

        record.GetValue("cash_notes").Should().Be(1500m);
    }

    [Fact]
    public void GetValue_MultiRow_ShouldReturnByRowKey()
    {
        var record = new ReturnDataRecord("MFCR 360", 1, StructuralCategory.MultiRow);

        var row1 = new ReturnDataRow { RowKey = "1" };
        row1.SetValue("amount", 100m);

        var row2 = new ReturnDataRow { RowKey = "2" };
        row2.SetValue("amount", 200m);

        record.AddRow(row1);
        record.AddRow(row2);

        record.GetValue("amount", "1").Should().Be(100m);
        record.GetValue("amount", "2").Should().Be(200m);
    }

    [Fact]
    public void GetDecimal_ShouldConvertStringToDecimal()
    {
        var record = new ReturnDataRecord("MFCR 300", 1, StructuralCategory.FixedRow);
        var row = new ReturnDataRow();
        row.SetValue("cash_notes", "1234.56");
        record.AddRow(row);

        record.GetDecimal("cash_notes").Should().Be(1234.56m);
    }

    [Fact]
    public void GetDecimal_NullValue_ShouldReturnNull()
    {
        var record = new ReturnDataRecord("MFCR 300", 1, StructuralCategory.FixedRow);
        var row = new ReturnDataRow();
        record.AddRow(row);

        record.GetDecimal("missing_field").Should().BeNull();
    }

    [Fact]
    public void Category_ShouldBeSetCorrectly()
    {
        var record = new ReturnDataRecord("MFCR 300", 1, StructuralCategory.ItemCoded);
        record.Category.Should().Be(StructuralCategory.ItemCoded);
        record.ReturnCode.Should().Be("MFCR 300");
        record.TemplateVersionId.Should().Be(1);
    }
}

public class ReturnDataRowTests
{
    [Fact]
    public void SetAndGetValue_ShouldRoundTrip()
    {
        var row = new ReturnDataRow();
        row.SetValue("field1", 42m);
        row.GetValue("field1").Should().Be(42m);
    }

    [Fact]
    public void GetValue_CaseInsensitive()
    {
        var row = new ReturnDataRow();
        row.SetValue("Cash_Notes", 100m);
        row.GetValue("cash_notes").Should().Be(100m);
    }

    [Fact]
    public void HasField_ShouldReturnTrueForExistingField()
    {
        var row = new ReturnDataRow();
        row.SetValue("amount", 50m);

        row.HasField("amount").Should().BeTrue();
        row.HasField("missing").Should().BeFalse();
    }

    [Fact]
    public void GetDecimal_ShouldHandleVariousTypes()
    {
        var row = new ReturnDataRow();
        row.SetValue("decimal_val", 100.5m);
        row.SetValue("int_val", 42);
        row.SetValue("long_val", 999L);
        row.SetValue("string_val", "123.45");
        row.SetValue("null_val", null);

        row.GetDecimal("decimal_val").Should().Be(100.5m);
        row.GetDecimal("int_val").Should().Be(42m);
        row.GetDecimal("long_val").Should().Be(999m);
        row.GetDecimal("string_val").Should().Be(123.45m);
        row.GetDecimal("null_val").Should().BeNull();
    }

    [Fact]
    public void GetString_ShouldConvertToString()
    {
        var row = new ReturnDataRow();
        row.SetValue("amount", 100m);
        row.GetString("amount").Should().Be("100");
    }

    [Fact]
    public void GetDateTime_ShouldParseDateTime()
    {
        var row = new ReturnDataRow();
        var date = new DateTime(2024, 6, 15);
        row.SetValue("report_date", date);

        row.GetDateTime("report_date").Should().Be(date);
    }

    [Fact]
    public void GetDateTime_ShouldParseFromString()
    {
        var row = new ReturnDataRow();
        row.SetValue("report_date", "2024-06-15");

        row.GetDateTime("report_date").Should().Be(new DateTime(2024, 6, 15));
    }

    [Fact]
    public void AllFields_ShouldReturnAllSetFields()
    {
        var row = new ReturnDataRow();
        row.SetValue("a", 1);
        row.SetValue("b", 2);

        row.AllFields.Should().HaveCount(2);
    }
}
