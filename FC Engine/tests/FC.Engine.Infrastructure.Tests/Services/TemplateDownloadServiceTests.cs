using ClosedXML.Excel;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Moq;

namespace FC.Engine.Infrastructure.Tests.Services;

public class TemplateDownloadServiceTests
{
    [Fact]
    public async Task GenerateTemplateExcel_Includes_Headers_Formatting_And_Instructions()
    {
        var tenantId = Guid.NewGuid();
        var template = BuildTemplate();

        var cache = new Mock<ITemplateMetadataCache>();
        cache.Setup(x => x.GetPublishedTemplate(tenantId, "MFB_PAR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var sut = new TemplateDownloadService(cache.Object);

        var bytes = await sut.GenerateTemplateExcel(tenantId, "MFB_PAR");

        bytes.Should().NotBeNullOrEmpty();

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        workbook.Worksheets.Should().Contain(x => x.Name == "MFB_PAR");
        workbook.Worksheets.Should().Contain(x => x.Name == "Instructions");

        var dataSheet = workbook.Worksheet("MFB_PAR");
        dataSheet.Cell(1, 1).GetString().Should().Be("Institution Code*");
        dataSheet.Cell(1, 2).GetString().Should().Be("Licence Category");

        var instructions = workbook.Worksheet("Instructions");
        instructions.Cell("A1").GetString().Should().Be("Data Entry Instructions");
        instructions.Cell("A5").GetString().Should().Contain("Required fields");

        dataSheet.Column(1).Style.NumberFormat.Format.Should().Be("@");
        dataSheet.Column(3).Style.NumberFormat.Format.Should().Be("#,##0.00");
    }

    [Fact]
    public async Task GenerateTemplateCsv_Includes_Required_Marker()
    {
        var tenantId = Guid.NewGuid();
        var template = BuildTemplate();

        var cache = new Mock<ITemplateMetadataCache>();
        cache.Setup(x => x.GetPublishedTemplate(tenantId, "MFB_PAR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var sut = new TemplateDownloadService(cache.Object);

        var csv = await sut.GenerateTemplateCsv(tenantId, "MFB_PAR");

        csv.Should().Contain("\"Institution Code*\"");
        csv.Should().Contain("\"Licence Category\"");
        csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(1);
    }

    private static CachedTemplate BuildTemplate()
    {
        return new CachedTemplate
        {
            ReturnCode = "MFB_PAR",
            Name = "MFB Portfolio at Risk",
            StructuralCategory = "FixedRow",
            CurrentVersion = new CachedTemplateVersion
            {
                Id = 99,
                VersionNumber = 1,
                Fields = new List<TemplateField>
                {
                    new()
                    {
                        FieldName = "institution_code",
                        DisplayName = "Institution Code",
                        DataType = FieldDataType.Text,
                        IsRequired = true,
                        FieldOrder = 1,
                        HelpText = "Use your regulator-issued institution code."
                    },
                    new()
                    {
                        FieldName = "licence_category",
                        DisplayName = "Licence Category",
                        DataType = FieldDataType.Text,
                        AllowedValues = "[\"Unit\",\"State\",\"National\"]",
                        FieldOrder = 2
                    },
                    new()
                    {
                        FieldName = "total_portfolio",
                        DisplayName = "Total Portfolio",
                        DataType = FieldDataType.Money,
                        FieldOrder = 3
                    }
                }
            }
        };
    }
}
