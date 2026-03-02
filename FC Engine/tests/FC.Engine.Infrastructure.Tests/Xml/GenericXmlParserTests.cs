using System.Text;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Infrastructure.Xml;
using FluentAssertions;
using Moq;

namespace FC.Engine.Infrastructure.Tests.Xml;

public class GenericXmlParserTests
{
    private readonly Mock<ITemplateMetadataCache> _cacheMock = new();
    private readonly GenericXmlParser _parser;

    public GenericXmlParserTests()
    {
        _parser = new GenericXmlParser(_cacheMock.Object);
    }

    [Fact]
    public async Task Parse_FixedRow_ShouldExtractAllFields()
    {
        var ns = "urn:cbn:dfis:fc:mfcr300";
        SetupCache("MFCR 300", StructuralCategory.FixedRow, ns, new[]
        {
            CreateField("cash_notes", "CashNotes", FieldDataType.Money, 1),
            CreateField("cash_coins", "CashCoins", FieldDataType.Money, 2),
            CreateField("total_cash", "TotalCash", FieldDataType.Money, 3)
        });

        var xml = $@"<?xml version=""1.0""?>
<MFCR300 xmlns=""{ns}"">
  <Header>
    <InstitutionCode>FC001</InstitutionCode>
    <ReportingDate>2024-01-31</ReportingDate>
    <ReturnCode>MFCR 300</ReturnCode>
  </Header>
  <Data>
    <CashNotes>1000.50</CashNotes>
    <CashCoins>200.25</CashCoins>
    <TotalCash>1200.75</TotalCash>
  </Data>
</MFCR300>";

        var record = await _parser.Parse(ToStream(xml), "MFCR 300");

        record.ReturnCode.Should().Be("MFCR 300");
        record.Category.Should().Be(StructuralCategory.FixedRow);
        record.Rows.Should().HaveCount(1);

        var row = record.SingleRow;
        row.GetDecimal("cash_notes").Should().Be(1000.50m);
        row.GetDecimal("cash_coins").Should().Be(200.25m);
        row.GetDecimal("total_cash").Should().Be(1200.75m);
    }

    [Fact]
    public async Task Parse_MultiRow_ShouldExtractAllRows()
    {
        var ns = "urn:cbn:dfis:fc:mfcr360";
        SetupCache("MFCR 360", StructuralCategory.MultiRow, ns, new[]
        {
            CreateField("serial_no", "SerialNo", FieldDataType.Integer, 1),
            CreateField("bank_name", "BankName", FieldDataType.Text, 2),
            CreateField("amount", "Amount", FieldDataType.Money, 3)
        });

        var xml = $@"<?xml version=""1.0""?>
<MFCR360 xmlns=""{ns}"">
  <Header>
    <InstitutionCode>FC001</InstitutionCode>
    <ReportingDate>2024-01-31</ReportingDate>
    <ReturnCode>MFCR 360</ReturnCode>
  </Header>
  <Rows>
    <Row>
      <BankName>First Bank</BankName>
      <Amount>5000.00</Amount>
    </Row>
    <Row>
      <BankName>Zenith Bank</BankName>
      <Amount>3000.00</Amount>
    </Row>
  </Rows>
</MFCR360>";

        var record = await _parser.Parse(ToStream(xml), "MFCR 360");

        record.Category.Should().Be(StructuralCategory.MultiRow);
        record.Rows.Should().HaveCount(2);

        record.Rows[0].RowKey.Should().Be("1");
        record.Rows[0].GetString("bank_name").Should().Be("First Bank");
        record.Rows[0].GetDecimal("amount").Should().Be(5000m);

        record.Rows[1].RowKey.Should().Be("2");
        record.Rows[1].GetString("bank_name").Should().Be("Zenith Bank");
    }

    [Fact]
    public async Task Parse_ItemCoded_ShouldExtractByItemCode()
    {
        var ns = "urn:cbn:dfis:fc:mfcr354";
        var itemCodes = new List<TemplateItemCode>
        {
            new() { ItemCode = "IC001", ItemDescription = "Category A", SortOrder = 1 },
            new() { ItemCode = "IC002", ItemDescription = "Category B", SortOrder = 2 }
        };

        SetupCache("MFCR 354", StructuralCategory.ItemCoded, ns, new[]
        {
            CreateField("item_code", "ItemCode", FieldDataType.Text, 1, isKey: true),
            CreateField("description", "Description", FieldDataType.Text, 2),
            CreateField("amount", "Amount", FieldDataType.Money, 3)
        }, itemCodes);

        var xml = $@"<?xml version=""1.0""?>
<MFCR354 xmlns=""{ns}"">
  <Header>
    <InstitutionCode>FC001</InstitutionCode>
    <ReportingDate>2024-01-31</ReportingDate>
    <ReturnCode>MFCR 354</ReturnCode>
  </Header>
  <Rows>
    <Row>
      <ItemCode>IC001</ItemCode>
      <Description>Category A</Description>
      <Amount>1000.00</Amount>
    </Row>
    <Row>
      <ItemCode>IC002</ItemCode>
      <Description>Category B</Description>
      <Amount>2000.00</Amount>
    </Row>
  </Rows>
</MFCR354>";

        var record = await _parser.Parse(ToStream(xml), "MFCR 354");

        record.Category.Should().Be(StructuralCategory.ItemCoded);
        record.Rows.Should().HaveCount(2);

        record.Rows[0].RowKey.Should().Be("IC001");
        record.Rows[0].GetDecimal("amount").Should().Be(1000m);

        record.Rows[1].RowKey.Should().Be("IC002");
        record.Rows[1].GetDecimal("amount").Should().Be(2000m);
    }

    [Fact]
    public async Task Parse_EmptyOptionalFields_ShouldNotIncludeThem()
    {
        var ns = "urn:cbn:dfis:fc:mfcr300";
        SetupCache("MFCR 300", StructuralCategory.FixedRow, ns, new[]
        {
            CreateField("cash_notes", "CashNotes", FieldDataType.Money, 1),
            CreateField("optional_field", "OptionalField", FieldDataType.Money, 2)
        });

        var xml = $@"<?xml version=""1.0""?>
<MFCR300 xmlns=""{ns}"">
  <Data>
    <CashNotes>100</CashNotes>
  </Data>
</MFCR300>";

        var record = await _parser.Parse(ToStream(xml), "MFCR 300");

        record.SingleRow.HasField("cash_notes").Should().BeTrue();
        record.SingleRow.HasField("optional_field").Should().BeFalse();
    }

    [Fact]
    public async Task Parse_IntegerField_ShouldConvertToInt()
    {
        var ns = "urn:cbn:dfis:fc:mfcr300";
        SetupCache("MFCR 300", StructuralCategory.FixedRow, ns, new[]
        {
            CreateField("count_field", "CountField", FieldDataType.Integer, 1)
        });

        var xml = $@"<?xml version=""1.0""?>
<MFCR300 xmlns=""{ns}"">
  <Data>
    <CountField>42</CountField>
  </Data>
</MFCR300>";

        var record = await _parser.Parse(ToStream(xml), "MFCR 300");

        record.SingleRow.GetValue("count_field").Should().Be(42);
    }

    private static TemplateField CreateField(string name, string xmlElement, FieldDataType dataType,
        int order, bool isKey = false)
    {
        return new TemplateField
        {
            FieldName = name,
            XmlElementName = xmlElement,
            DataType = dataType,
            FieldOrder = order,
            IsKeyField = isKey
        };
    }

    private void SetupCache(string returnCode, StructuralCategory category, string xmlNs,
        TemplateField[] fields, List<TemplateItemCode>? itemCodes = null)
    {
        var cached = new CachedTemplate
        {
            TemplateId = 1,
            ReturnCode = returnCode,
            Name = returnCode,
            StructuralCategory = category.ToString(),
            PhysicalTableName = returnCode.ToLowerInvariant().Replace(" ", "_"),
            XmlRootElement = returnCode.Replace(" ", ""),
            XmlNamespace = xmlNs,
            CurrentVersion = new CachedTemplateVersion
            {
                Id = 1,
                VersionNumber = 1,
                Fields = fields.ToList().AsReadOnly(),
                ItemCodes = (itemCodes ?? new List<TemplateItemCode>()).AsReadOnly(),
                IntraSheetFormulas = new List<IntraSheetFormula>().AsReadOnly()
            }
        };

        _cacheMock.Setup(c => c.GetPublishedTemplate(returnCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);
    }

    private static Stream ToStream(string xml) => new MemoryStream(Encoding.UTF8.GetBytes(xml));
}
