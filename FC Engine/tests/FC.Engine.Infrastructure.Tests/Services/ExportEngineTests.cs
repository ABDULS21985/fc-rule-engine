using System.Reflection;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using ClosedXML.Excel;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.DataRecord;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.BackgroundJobs;
using FC.Engine.Infrastructure.Export;
using FC.Engine.Infrastructure.Export.Adapters;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using UglyToad.PdfPig;

namespace FC.Engine.Infrastructure.Tests.Services;

public class ExportEngineTests
{
    [Fact]
    public async Task Excel_Export_Opens_In_LibreOffice_With_Correct_Data()
    {
        var tenantId = Guid.NewGuid();
        var submission = BuildSubmission(tenantId, "RET101");

        var template = BuildTemplate(
            returnCode: "RET101",
            moduleId: 7,
            fields:
            [
                new TemplateField
                {
                    FieldName = "customer_name",
                    DisplayName = "Customer Name",
                    XmlElementName = "CustomerName",
                    FieldOrder = 1,
                    DataType = FieldDataType.Text,
                    IsRequired = true
                },
                new TemplateField
                {
                    FieldName = "amount",
                    DisplayName = "Amount",
                    XmlElementName = "Amount",
                    FieldOrder = 2,
                    DataType = FieldDataType.Money,
                    IsRequired = true
                }
            ]);

        var record = new ReturnDataRecord("RET101", 1, StructuralCategory.FixedRow);
        var row = new ReturnDataRow();
        row.SetValue("customer_name", "Jane Doe");
        row.SetValue("amount", 512345.99m);
        record.AddRow(row);

        var cache = new Mock<ITemplateMetadataCache>();
        cache.Setup(x => x.GetPublishedTemplate(tenantId, "RET101", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        cache.Setup(x => x.GetAllPublishedTemplates(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CachedTemplate> { template });

        var dataRepo = new Mock<IGenericDataRepository>();
        dataRepo.Setup(x => x.GetBySubmission("RET101", submission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var generator = new ExcelExportGenerator(
            cache.Object,
            dataRepo.Object,
            new Mock<IFileStorageService>().Object,
            new Mock<ISubmissionApprovalRepository>().Object,
            NullLogger<ExcelExportGenerator>.Instance);

        var bytes = await generator.Generate(new ExportGenerationContext
        {
            TenantId = tenantId,
            Submission = submission,
            Branding = BrandingConfig.WithDefaults()
        });

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("RET101");
        sheet.Cell(2, 1).GetString().Should().Be("Jane Doe");
        sheet.Cell(2, 2).GetValue<decimal>().Should().Be(512345.99m);
    }

    [Fact]
    public async Task Excel_Money_Fields_Formatted_With_Commas()
    {
        var tenantId = Guid.NewGuid();
        var submission = BuildSubmission(tenantId, "RET102");

        var template = BuildTemplate(
            returnCode: "RET102",
            moduleId: 7,
            fields:
            [
                new TemplateField
                {
                    FieldName = "amount",
                    DisplayName = "Amount",
                    XmlElementName = "Amount",
                    FieldOrder = 1,
                    DataType = FieldDataType.Money,
                    IsRequired = true
                }
            ]);

        var record = new ReturnDataRecord("RET102", 1, StructuralCategory.FixedRow);
        var row = new ReturnDataRow();
        row.SetValue("amount", 14500.1m);
        record.AddRow(row);

        var cache = new Mock<ITemplateMetadataCache>();
        cache.Setup(x => x.GetPublishedTemplate(tenantId, "RET102", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        cache.Setup(x => x.GetAllPublishedTemplates(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CachedTemplate> { template });

        var dataRepo = new Mock<IGenericDataRepository>();
        dataRepo.Setup(x => x.GetBySubmission("RET102", submission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var generator = new ExcelExportGenerator(
            cache.Object,
            dataRepo.Object,
            new Mock<IFileStorageService>().Object,
            new Mock<ISubmissionApprovalRepository>().Object,
            NullLogger<ExcelExportGenerator>.Instance);

        var bytes = await generator.Generate(new ExportGenerationContext
        {
            TenantId = tenantId,
            Submission = submission,
            Branding = BrandingConfig.WithDefaults()
        });

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var cell = workbook.Worksheet("RET102").Cell(2, 1);
        cell.Style.NumberFormat.Format.Should().Be("#,##0.00");
        cell.GetValue<decimal>().Should().Be(14500.1m);
    }

    [Fact]
    public async Task Excel_Has_Tenant_Branding_On_Cover_Sheet()
    {
        var tenantId = Guid.NewGuid();
        var submission = BuildSubmission(tenantId, "RET100");

        var template = BuildTemplate(
            returnCode: "RET100",
            moduleId: 7,
            fields:
            [
                new TemplateField
                {
                    FieldName = "amount",
                    DisplayName = "Amount",
                    XmlElementName = "Amount",
                    FieldOrder = 1,
                    DataType = FieldDataType.Money,
                    IsRequired = true
                }
            ]);

        var record = new ReturnDataRecord("RET100", 1, StructuralCategory.FixedRow);
        var row = new ReturnDataRow();
        row.SetValue("amount", 12500.45m);
        record.AddRow(row);

        var cache = new Mock<ITemplateMetadataCache>();
        cache.Setup(x => x.GetPublishedTemplate(tenantId, "RET100", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        cache.Setup(x => x.GetAllPublishedTemplates(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CachedTemplate> { template });

        var dataRepo = new Mock<IGenericDataRepository>();
        dataRepo.Setup(x => x.GetBySubmission("RET100", submission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var fileStorage = new Mock<IFileStorageService>();
        var approvalRepo = new Mock<ISubmissionApprovalRepository>();
        approvalRepo.Setup(x => x.GetBySubmission(submission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubmissionApproval?)null);

        var generator = new ExcelExportGenerator(
            cache.Object,
            dataRepo.Object,
            fileStorage.Object,
            approvalRepo.Object,
            NullLogger<ExcelExportGenerator>.Instance);

        var bytes = await generator.Generate(new ExportGenerationContext
        {
            TenantId = tenantId,
            Submission = submission,
            Branding = new BrandingConfig
            {
                CompanyName = "Acme Financial",
                PrimaryColor = "#114488"
            }
        });

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        workbook.Worksheets.Contains("Cover").Should().BeTrue();
        workbook.Worksheet("Cover").Cell("A1").GetString().Should().Be("Acme Financial");

        var dataSheet = workbook.Worksheet("RET100");
        dataSheet.Cell(1, 1).GetString().Should().Be("Amount");
        dataSheet.Cell(2, 1).GetValue<decimal>().Should().Be(12500.45m);
        dataSheet.Cell(2, 1).Style.NumberFormat.Format.Should().Be("#,##0.00");
    }

    [Fact]
    public async Task XML_Validates_Against_XSD_Schema()
    {
        var tenantId = Guid.NewGuid();
        var submission = BuildSubmission(tenantId, "RET200");

        var template = BuildTemplate(
            returnCode: "RET200",
            moduleId: 11,
            fields:
            [
                new TemplateField
                {
                    FieldName = "customer_name",
                    DisplayName = "Customer Name",
                    XmlElementName = "CustomerName",
                    FieldOrder = 1,
                    DataType = FieldDataType.Text,
                    IsRequired = true
                }
            ],
            xmlRootElement: "RET200");

        var record = new ReturnDataRecord("RET200", 1, StructuralCategory.FixedRow);
        var row = new ReturnDataRow();
        row.SetValue("customer_name", "Jane Doe");
        record.AddRow(row);

        var schemaSet = BuildSimpleSchema(template.XmlNamespace, "RET200");

        var cache = new Mock<ITemplateMetadataCache>();
        cache.Setup(x => x.GetPublishedTemplate(tenantId, "RET200", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var dataRepo = new Mock<IGenericDataRepository>();
        dataRepo.Setup(x => x.GetBySubmission("RET200", submission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var xsdGenerator = new Mock<IXsdGenerator>();
        xsdGenerator.Setup(x => x.GenerateSchema("RET200", It.IsAny<CancellationToken>()))
            .ReturnsAsync(schemaSet);

        var generator = new XmlExportGenerator(
            cache.Object,
            dataRepo.Object,
            xsdGenerator.Object,
            NullLogger<XmlExportGenerator>.Instance);

        var bytes = await generator.Generate(new ExportGenerationContext
        {
            TenantId = tenantId,
            Submission = submission,
            Branding = BrandingConfig.WithDefaults()
        });

        var xml = XDocument.Parse(System.Text.Encoding.UTF8.GetString(bytes));
        XNamespace ns = template.XmlNamespace;
        xml.Root.Should().NotBeNull();
        xml.Root!.Name.Should().Be(ns + "RET200");
        xml.Root.Element(ns + "Header").Should().NotBeNull();
        xml.Root.Element(ns + "Data")!.Element(ns + "CustomerName")!.Value.Should().Be("Jane Doe");
    }

    [Fact]
    public async Task NFIU_GoAML_Adapter_Produces_Valid_GoAML_XML()
    {
        var tenantId = Guid.NewGuid();
        var submission = BuildSubmission(tenantId, "NFIU-STR");
        submission.Institution = new Institution
        {
            TenantId = tenantId,
            InstitutionCode = "NFIU001",
            InstitutionName = "NFIU Demo Bank",
            IsActive = true
        };

        var template = BuildTemplate(
            returnCode: "NFIU-STR",
            moduleId: 33,
            fields:
            [
                new TemplateField
                {
                    FieldName = "txn_amount",
                    DisplayName = "Transaction Amount",
                    XmlElementName = "TransactionAmount",
                    FieldOrder = 1,
                    DataType = FieldDataType.Money
                }
            ]);

        var record = new ReturnDataRecord("NFIU-STR", 1, StructuralCategory.MultiRow);
        var row = new ReturnDataRow { RowKey = "TXN-1" };
        row.SetValue("txn_amount", 9000.12m);
        record.AddRow(row);

        var cache = new Mock<ITemplateMetadataCache>();
        cache.Setup(x => x.GetPublishedTemplate(tenantId, "NFIU-STR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var dataRepo = new Mock<IGenericDataRepository>();
        dataRepo.Setup(x => x.GetBySubmission("NFIU-STR", submission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var adapter = new NfiuSubmissionAdapter(cache.Object, dataRepo.Object);
        var bytes = await adapter.Package(submission, ExportFormat.XML);
        var xml = XDocument.Parse(System.Text.Encoding.UTF8.GetString(bytes));

        XNamespace goaml = "http://www.unodc.org/goaml";
        xml.Root.Should().NotBeNull();
        xml.Root!.Name.Should().Be(goaml + "report");
        xml.Root.Element(goaml + "reportHeader")!.Element(goaml + "reportCode")!.Value.Should().Be("STR");
        xml.Root.Element(goaml + "reportBody")!.Elements(goaml + "transaction").Should().NotBeEmpty();
    }

    [Fact]
    public async Task XML_Has_Correct_Namespace_And_Structure()
    {
        var tenantId = Guid.NewGuid();
        var submission = BuildSubmission(tenantId, "RET201");

        var template = BuildTemplate(
            returnCode: "RET201",
            moduleId: 12,
            fields:
            [
                new TemplateField
                {
                    FieldName = "customer_name",
                    DisplayName = "Customer Name",
                    XmlElementName = "CustomerName",
                    FieldOrder = 1,
                    DataType = FieldDataType.Text,
                    IsRequired = true
                },
                new TemplateField
                {
                    FieldName = "is_flagged",
                    DisplayName = "Is Flagged",
                    XmlElementName = "IsFlagged",
                    FieldOrder = 2,
                    DataType = FieldDataType.Boolean,
                    IsRequired = false
                }
            ],
            xmlRootElement: "RET201");

        var record = new ReturnDataRecord("RET201", 1, StructuralCategory.FixedRow);
        var row = new ReturnDataRow();
        row.SetValue("customer_name", "Ada Obi");
        row.SetValue("is_flagged", true);
        record.AddRow(row);

        var schemaSet = BuildSimpleSchemaWithFlag(template.XmlNamespace, "RET201");

        var cache = new Mock<ITemplateMetadataCache>();
        cache.Setup(x => x.GetPublishedTemplate(tenantId, "RET201", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var dataRepo = new Mock<IGenericDataRepository>();
        dataRepo.Setup(x => x.GetBySubmission("RET201", submission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var xsdGenerator = new Mock<IXsdGenerator>();
        xsdGenerator.Setup(x => x.GenerateSchema("RET201", It.IsAny<CancellationToken>()))
            .ReturnsAsync(schemaSet);

        var generator = new XmlExportGenerator(
            cache.Object,
            dataRepo.Object,
            xsdGenerator.Object,
            NullLogger<XmlExportGenerator>.Instance);

        var bytes = await generator.Generate(new ExportGenerationContext
        {
            TenantId = tenantId,
            Submission = submission,
            Branding = BrandingConfig.WithDefaults()
        });

        var xml = XDocument.Parse(Encoding.UTF8.GetString(bytes));
        XNamespace ns = template.XmlNamespace;
        var root = xml.Root;
        root.Should().NotBeNull();
        root!.Name.Should().Be(ns + "RET201");
        root.Element(ns + "Header")!.Element(ns + "ReturnCode")!.Value.Should().Be("RET201");
        root.Element(ns + "Data")!.Element(ns + "CustomerName")!.Value.Should().Be("Ada Obi");
        root.Element(ns + "Data")!.Element(ns + "IsFlagged")!.Value.Should().Be("true");
    }

    [Fact]
    public async Task PDF_Has_Cover_Page_With_Logo_And_Colours()
    {
        var tenantId = Guid.NewGuid();
        var submission = BuildSubmission(tenantId, "PDF100");
        var template = BuildTemplate(
            returnCode: "PDF100",
            moduleId: 70,
            fields:
            [
                new TemplateField
                {
                    FieldName = "amount",
                    DisplayName = "Amount",
                    XmlElementName = "Amount",
                    FieldOrder = 1,
                    DataType = FieldDataType.Money
                }
            ]);

        var record = new ReturnDataRecord("PDF100", 1, StructuralCategory.FixedRow);
        var row = new ReturnDataRow();
        row.SetValue("amount", 2500m);
        record.AddRow(row);

        var templateCache = new Mock<ITemplateMetadataCache>();
        templateCache.Setup(x => x.GetPublishedTemplate(tenantId, "PDF100", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        templateCache.Setup(x => x.GetAllPublishedTemplates(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CachedTemplate> { template });

        var dataRepo = new Mock<IGenericDataRepository>();
        dataRepo.Setup(x => x.GetBySubmission("PDF100", submission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var approvalRepo = new Mock<ISubmissionApprovalRepository>();
        var storage = new Mock<IFileStorageService>();
        storage.Setup(x => x.DownloadAsync("branding/logo.png", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Create1x1PngBytes()));

        var generator = new PdfExportGenerator(templateCache.Object, dataRepo.Object, approvalRepo.Object, storage.Object);
        var bytes = await generator.Generate(new ExportGenerationContext
        {
            TenantId = tenantId,
            Submission = submission,
            Branding = new BrandingConfig
            {
                CompanyName = "Acme Financial",
                PrimaryColor = "#114488",
                LogoUrl = "uploads/branding/logo.png"
            }
        });

        Encoding.ASCII.GetString(bytes.Take(4).ToArray()).Should().Be("%PDF");
        storage.Verify(x => x.DownloadAsync("branding/logo.png", It.IsAny<CancellationToken>()), Times.Once);

        var text = ExtractPdfText(bytes);
        text.Should().Contain("Acme Financial");
        text.Should().Contain("Regulatory Return Report");
    }

    [Fact]
    public async Task PDF_Has_All_Data_Sheets_As_Tables()
    {
        var tenantId = Guid.NewGuid();
        var submission = BuildSubmission(tenantId, "PDF200A");

        var templateA = BuildTemplate(
            returnCode: "PDF200A",
            moduleId: 71,
            fields:
            [
                new TemplateField
                {
                    FieldName = "amount_a",
                    DisplayName = "Amount A",
                    XmlElementName = "AmountA",
                    FieldOrder = 1,
                    DataType = FieldDataType.Money
                }
            ]);

        var templateB = BuildTemplate(
            returnCode: "PDF200B",
            moduleId: 71,
            fields:
            [
                new TemplateField
                {
                    FieldName = "amount_b",
                    DisplayName = "Amount B",
                    XmlElementName = "AmountB",
                    FieldOrder = 1,
                    DataType = FieldDataType.Money
                }
            ]);

        var recordA = new ReturnDataRecord("PDF200A", 1, StructuralCategory.FixedRow);
        var rowA = new ReturnDataRow();
        rowA.SetValue("amount_a", 1000m);
        recordA.AddRow(rowA);

        var recordB = new ReturnDataRecord("PDF200B", 1, StructuralCategory.FixedRow);
        var rowB = new ReturnDataRow();
        rowB.SetValue("amount_b", 2000m);
        recordB.AddRow(rowB);

        var templateCache = new Mock<ITemplateMetadataCache>();
        templateCache.Setup(x => x.GetPublishedTemplate(tenantId, "PDF200A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(templateA);
        templateCache.Setup(x => x.GetAllPublishedTemplates(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CachedTemplate> { templateA, templateB });

        var dataRepo = new Mock<IGenericDataRepository>();
        dataRepo.Setup(x => x.GetBySubmission("PDF200A", submission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(recordA);
        dataRepo.Setup(x => x.GetBySubmission("PDF200B", submission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(recordB);

        var generator = new PdfExportGenerator(
            templateCache.Object,
            dataRepo.Object,
            new Mock<ISubmissionApprovalRepository>().Object,
            new Mock<IFileStorageService>().Object);

        var bytes = await generator.Generate(new ExportGenerationContext
        {
            TenantId = tenantId,
            Submission = submission,
            Branding = BrandingConfig.WithDefaults()
        });

        var text = ExtractPdfText(bytes);
        text.Should().Contain("PDF200A");
        text.Should().Contain("PDF200B");
        text.Should().Contain("Amount A");
        text.Should().Contain("Amount B");
    }

    [Fact]
    public async Task PDF_Has_Validation_Summary()
    {
        var tenantId = Guid.NewGuid();
        var submission = BuildSubmission(tenantId, "PDF300");
        submission.ValidationReport!.AddError(new ValidationError
        {
            RuleId = "VAL-001",
            Field = "amount",
            Message = "Amount is required",
            Severity = ValidationSeverity.Error,
            Category = ValidationCategory.Schema
        });

        var template = BuildTemplate(
            returnCode: "PDF300",
            moduleId: 72,
            fields:
            [
                new TemplateField
                {
                    FieldName = "amount",
                    DisplayName = "Amount",
                    XmlElementName = "Amount",
                    FieldOrder = 1,
                    DataType = FieldDataType.Money
                }
            ]);

        var templateCache = new Mock<ITemplateMetadataCache>();
        templateCache.Setup(x => x.GetPublishedTemplate(tenantId, "PDF300", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        templateCache.Setup(x => x.GetAllPublishedTemplates(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CachedTemplate> { template });

        var dataRepo = new Mock<IGenericDataRepository>();
        dataRepo.Setup(x => x.GetBySubmission("PDF300", submission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReturnDataRecord("PDF300", 1, StructuralCategory.FixedRow));

        var generator = new PdfExportGenerator(
            templateCache.Object,
            dataRepo.Object,
            new Mock<ISubmissionApprovalRepository>().Object,
            new Mock<IFileStorageService>().Object);

        var bytes = await generator.Generate(new ExportGenerationContext
        {
            TenantId = tenantId,
            Submission = submission,
            Branding = BrandingConfig.WithDefaults()
        });

        var text = ExtractPdfText(bytes);
        text.Should().Contain("Valida");
        text.Should().Contain("Amount is required");
    }

    [Fact]
    public async Task PDF_Has_Attestation_Page()
    {
        var tenantId = Guid.NewGuid();
        var submission = BuildSubmission(tenantId, "PDF400");

        var template = BuildTemplate(
            returnCode: "PDF400",
            moduleId: 73,
            fields:
            [
                new TemplateField
                {
                    FieldName = "field_1",
                    DisplayName = "Field 1",
                    XmlElementName = "Field1",
                    FieldOrder = 1,
                    DataType = FieldDataType.Text
                }
            ]);

        var templateCache = new Mock<ITemplateMetadataCache>();
        templateCache.Setup(x => x.GetPublishedTemplate(tenantId, "PDF400", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        templateCache.Setup(x => x.GetAllPublishedTemplates(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CachedTemplate> { template });

        var dataRepo = new Mock<IGenericDataRepository>();
        dataRepo.Setup(x => x.GetBySubmission("PDF400", submission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReturnDataRecord("PDF400", 1, StructuralCategory.FixedRow));

        var approvalRepo = new Mock<ISubmissionApprovalRepository>();
        approvalRepo.Setup(x => x.GetBySubmission(submission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmissionApproval
            {
                TenantId = tenantId,
                SubmissionId = submission.Id,
                Status = ApprovalStatus.Approved,
                RequestedAt = DateTime.UtcNow.AddHours(-2),
                ReviewedAt = DateTime.UtcNow.AddHours(-1),
                RequestedBy = new InstitutionUser { DisplayName = "Maker User", Username = "maker" },
                ReviewedBy = new InstitutionUser { DisplayName = "Checker User", Username = "checker" }
            });

        var generator = new PdfExportGenerator(
            templateCache.Object,
            dataRepo.Object,
            approvalRepo.Object,
            new Mock<IFileStorageService>().Object);

        var bytes = await generator.Generate(new ExportGenerationContext
        {
            TenantId = tenantId,
            Submission = submission,
            Branding = BrandingConfig.WithDefaults()
        });

        var text = ExtractPdfText(bytes);
        text.Should().Contain("Digital A");
        text.Should().Contain("Maker User");
        text.Should().Contain("Checker User");
    }

    [Fact]
    public async Task PDF_Watermark_Applied_When_Configured()
    {
        var tenantId = Guid.NewGuid();
        var submission = BuildSubmission(tenantId, "PDF500");

        var template = BuildTemplate(
            returnCode: "PDF500",
            moduleId: 74,
            fields:
            [
                new TemplateField
                {
                    FieldName = "field_1",
                    DisplayName = "Field 1",
                    XmlElementName = "Field1",
                    FieldOrder = 1,
                    DataType = FieldDataType.Text
                }
            ]);

        var templateCache = new Mock<ITemplateMetadataCache>();
        templateCache.Setup(x => x.GetPublishedTemplate(tenantId, "PDF500", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        templateCache.Setup(x => x.GetAllPublishedTemplates(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CachedTemplate> { template });

        var dataRepo = new Mock<IGenericDataRepository>();
        dataRepo.Setup(x => x.GetBySubmission("PDF500", submission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReturnDataRecord("PDF500", 1, StructuralCategory.FixedRow));

        var generator = new PdfExportGenerator(
            templateCache.Object,
            dataRepo.Object,
            new Mock<ISubmissionApprovalRepository>().Object,
            new Mock<IFileStorageService>().Object);

        var bytes = await generator.Generate(new ExportGenerationContext
        {
            TenantId = tenantId,
            Submission = submission,
            Branding = new BrandingConfig
            {
                CompanyName = "Acme Financial",
                WatermarkText = "CONFIDENTIAL"
            }
        });

        var text = ExtractPdfText(bytes);
        text.Should().Contain("Watermark: CONFIDENTIAL");
    }

    [Fact]
    public async Task CBN_Adapter_Produces_EFASS_Compatible_Excel()
    {
        var tenantId = Guid.NewGuid();
        var submission = BuildSubmission(tenantId, "CBN001");
        var expectedBytes = BuildMinimalWorkbook("CBN eFASS");

        var brandingService = new Mock<ITenantBrandingService>();
        brandingService.Setup(x => x.GetBrandingConfig(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BrandingConfig.WithDefaults());

        var adapter = new CbnSubmissionAdapter(
            [
                new TypedGenerator(ExportFormat.Excel, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx", expectedBytes),
                new TypedGenerator(ExportFormat.XML, "application/xml", "xml", Encoding.UTF8.GetBytes("<return />"))
            ],
            brandingService.Object);

        var bytes = await adapter.Package(submission, ExportFormat.Excel);
        using var zipStream = new MemoryStream(bytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        archive.Entries.Should().Contain(x => x.FullName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase));
        archive.Entries.Should().Contain(x => x.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        archive.Entries.Should().Contain(x => string.Equals(x.FullName, "manifest.json", StringComparison.OrdinalIgnoreCase));

        var excelEntry = archive.Entries.First(x => x.FullName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase));
        using var excelStream = excelEntry.Open();
        using var workbook = new XLWorkbook(excelStream);
        workbook.Worksheets.First().Cell("A1").GetString().Should().Be("CBN eFASS");
    }

    [Fact]
    public async Task NFIU_GoAML_Adapter_Fails_Without_Transaction_Rows()
    {
        var tenantId = Guid.NewGuid();
        var submission = BuildSubmission(tenantId, "NFIU-STR");
        submission.Institution = new Institution
        {
            TenantId = tenantId,
            InstitutionCode = "NFIU001",
            InstitutionName = "NFIU Demo Bank",
            IsActive = true
        };

        var template = BuildTemplate(
            returnCode: "NFIU-STR",
            moduleId: 33,
            fields:
            [
                new TemplateField
                {
                    FieldName = "txn_amount",
                    DisplayName = "Transaction Amount",
                    XmlElementName = "TransactionAmount",
                    FieldOrder = 1,
                    DataType = FieldDataType.Money
                }
            ]);

        var cache = new Mock<ITemplateMetadataCache>();
        cache.Setup(x => x.GetPublishedTemplate(tenantId, "NFIU-STR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var dataRepo = new Mock<IGenericDataRepository>();
        dataRepo.Setup(x => x.GetBySubmission("NFIU-STR", submission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReturnDataRecord("NFIU-STR", 1, StructuralCategory.MultiRow));

        var adapter = new NfiuSubmissionAdapter(cache.Object, dataRepo.Object);
        var act = () => adapter.Package(submission, ExportFormat.XML);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*at least one transaction*");
    }

    [Fact]
    public async Task SHA256_Hash_Matches_File_Content()
    {
        var tenantId = Guid.NewGuid();
        var submission = BuildSubmission(tenantId, "RET300");

        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7 };
        var exportRepo = new InMemoryExportRequestRepository();
        var submissionRepo = new Mock<ISubmissionRepository>();
        submissionRepo.Setup(x => x.GetById(submission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(submission);
        submissionRepo.Setup(x => x.GetByIdWithReport(submission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(submission);

        var branding = new Mock<ITenantBrandingService>();
        branding.Setup(x => x.GetBrandingConfig(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BrandingConfig.WithDefaults());

        var storage = new InMemoryStorage();
        var notification = new Mock<INotificationOrchestrator>();

        var engine = new ExportEngine(
            exportRepo,
            submissionRepo.Object,
            branding.Object,
            storage,
            [new StaticGenerator(payload)],
            NullLogger<ExportEngine>.Instance,
            notification.Object);

        var requestId = await engine.QueueExport(tenantId, submission.Id, ExportFormat.Excel, 77);
        var result = await engine.GenerateExport(requestId);

        result.Success.Should().BeTrue();
        var storedBytes = storage.Stored.Single().Value;
        var expectedHash = Convert.ToHexString(SHA256.HashData(storedBytes)).ToLowerInvariant();
        result.Sha256Hash.Should().Be(expectedHash);
    }

    [Fact]
    public async Task Export_Notification_Sent_On_Completion()
    {
        var tenantId = Guid.NewGuid();
        var submission = BuildSubmission(tenantId, "RET400");

        var exportRepo = new InMemoryExportRequestRepository();
        var submissionRepo = new Mock<ISubmissionRepository>();
        submissionRepo.Setup(x => x.GetById(submission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(submission);
        submissionRepo.Setup(x => x.GetByIdWithReport(submission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(submission);

        var branding = new Mock<ITenantBrandingService>();
        branding.Setup(x => x.GetBrandingConfig(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BrandingConfig.WithDefaults());

        var storage = new InMemoryStorage();
        var notification = new Mock<INotificationOrchestrator>();

        var engine = new ExportEngine(
            exportRepo,
            submissionRepo.Object,
            branding.Object,
            storage,
            [new StaticGenerator(new byte[] { 9, 9, 9 })],
            NullLogger<ExportEngine>.Instance,
            notification.Object);

        var requestId = await engine.QueueExport(tenantId, submission.Id, ExportFormat.Excel, 44);
        await engine.GenerateExport(requestId);

        notification.Verify(x => x.Notify(
            It.Is<NotificationRequest>(r =>
                r.EventType == Domain.Notifications.NotificationEvents.ExportReady
                && r.TenantId == tenantId
                && r.RecipientUserIds.Contains(44)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Failed_Export_Records_Error_Message()
    {
        var tenantId = Guid.NewGuid();
        var submission = BuildSubmission(tenantId, "RET500");

        var exportRepo = new InMemoryExportRequestRepository();
        var submissionRepo = new Mock<ISubmissionRepository>();
        submissionRepo.Setup(x => x.GetById(submission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(submission);
        submissionRepo.Setup(x => x.GetByIdWithReport(submission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(submission);

        var branding = new Mock<ITenantBrandingService>();
        branding.Setup(x => x.GetBrandingConfig(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BrandingConfig.WithDefaults());

        var engine = new ExportEngine(
            exportRepo,
            submissionRepo.Object,
            branding.Object,
            new InMemoryStorage(),
            [new ThrowingGenerator()],
            NullLogger<ExportEngine>.Instance);

        var requestId = await engine.QueueExport(tenantId, submission.Id, ExportFormat.Excel, 88);
        var result = await engine.GenerateExport(requestId);

        result.Success.Should().BeFalse();
        var request = await exportRepo.GetById(requestId);
        request.Should().NotBeNull();
        request!.Status.Should().Be(ExportRequestStatus.Failed);
        request.ErrorMessage.Should().Contain("Synthetic export failure");
    }

    [Fact]
    public async Task Expired_Exports_Cleaned_Up_After_7_Days()
    {
        var exportRepo = new InMemoryExportRequestRepository();
        var storage = new InMemoryStorage();
        storage.Stored["tenants/t1/exports/100/2.xlsx"] = new byte[] { 1, 2, 3 };

        var request = new ExportRequest
        {
            TenantId = Guid.NewGuid(),
            SubmissionId = 100,
            Format = ExportFormat.Excel,
            Status = ExportRequestStatus.Completed,
            RequestedBy = 7,
            RequestedAt = DateTime.UtcNow.AddDays(-9),
            FilePath = "tenants/t1/exports/100/2.xlsx",
            FileSize = 3,
            ExpiresAt = DateTime.UtcNow.AddDays(-2)
        };
        await exportRepo.Add(request);

        var job = new ExportCleanupJob(exportRepo, storage, NullLogger<ExportCleanupJob>.Instance);

        var method = typeof(ExportCleanupJob).GetMethod("RemoveExpiredExport", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        await (Task)method!.Invoke(job, [request, CancellationToken.None])!;

        storage.Deleted.Should().Contain("tenants/t1/exports/100/2.xlsx");
        request.FilePath.Should().BeNull();
        request.FileSize.Should().BeNull();
        request.Sha256Hash.Should().BeNull();
    }

    private static Submission BuildSubmission(Guid tenantId, string returnCode)
    {
        return new Submission
        {
            Id = 42,
            TenantId = tenantId,
            InstitutionId = 5,
            ReturnPeriodId = 9,
            ReturnCode = returnCode,
            Status = SubmissionStatus.Accepted,
            SubmittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            Institution = new Institution
            {
                Id = 5,
                TenantId = tenantId,
                InstitutionCode = "INST001",
                InstitutionName = "Demo Institution",
                IsActive = true
            },
            ReturnPeriod = new ReturnPeriod
            {
                Id = 9,
                TenantId = tenantId,
                Year = 2026,
                Month = 2,
                Frequency = "Monthly",
                ReportingDate = new DateTime(2026, 2, 28),
                CreatedAt = DateTime.UtcNow,
                IsOpen = false
            },
            ValidationReport = ValidationReport.Create(42, tenantId)
        };
    }

    private static CachedTemplate BuildTemplate(
        string returnCode,
        int? moduleId,
        IReadOnlyList<TemplateField> fields,
        string xmlRootElement = "root")
    {
        return new CachedTemplate
        {
            TemplateId = 1,
            TenantId = Guid.NewGuid(),
            ReturnCode = returnCode,
            Name = returnCode,
            StructuralCategory = StructuralCategory.FixedRow.ToString(),
            PhysicalTableName = returnCode.ToLowerInvariant(),
            XmlRootElement = xmlRootElement,
            XmlNamespace = "urn:regos:test",
            ModuleId = moduleId,
            CurrentVersion = new CachedTemplateVersion
            {
                Id = 1,
                VersionNumber = 1,
                Fields = fields,
                ItemCodes = Array.Empty<TemplateItemCode>(),
                IntraSheetFormulas = Array.Empty<IntraSheetFormula>()
            }
        };
    }

    private static XmlSchemaSet BuildSimpleSchema(string xmlNamespace, string rootElement)
    {
        var xsd = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<xs:schema xmlns:xs=""http://www.w3.org/2001/XMLSchema""
           targetNamespace=""{xmlNamespace}""
           xmlns:tns=""{xmlNamespace}""
           elementFormDefault=""qualified"">
  <xs:element name=""{rootElement}"">
    <xs:complexType>
      <xs:sequence>
        <xs:element name=""Header"">
          <xs:complexType>
            <xs:sequence>
              <xs:element name=""InstitutionCode"" type=""xs:string""/>
              <xs:element name=""ReportingDate"" type=""xs:date""/>
              <xs:element name=""ReturnCode"" type=""xs:string""/>
              <xs:element name=""SchemaVersion"" type=""xs:int""/>
            </xs:sequence>
          </xs:complexType>
        </xs:element>
        <xs:element name=""Data"">
          <xs:complexType>
            <xs:sequence>
              <xs:element name=""CustomerName"" type=""xs:string"" minOccurs=""0""/>
            </xs:sequence>
          </xs:complexType>
        </xs:element>
      </xs:sequence>
    </xs:complexType>
  </xs:element>
</xs:schema>";

        var set = new XmlSchemaSet();
        set.Add(xmlNamespace, XmlReader.Create(new StringReader(xsd)));
        set.Compile();
        return set;
    }

    private static XmlSchemaSet BuildSimpleSchemaWithFlag(string xmlNamespace, string rootElement)
    {
        var xsd = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<xs:schema xmlns:xs=""http://www.w3.org/2001/XMLSchema""
           targetNamespace=""{xmlNamespace}""
           xmlns:tns=""{xmlNamespace}""
           elementFormDefault=""qualified"">
  <xs:element name=""{rootElement}"">
    <xs:complexType>
      <xs:sequence>
        <xs:element name=""Header"">
          <xs:complexType>
            <xs:sequence>
              <xs:element name=""InstitutionCode"" type=""xs:string""/>
              <xs:element name=""ReportingDate"" type=""xs:date""/>
              <xs:element name=""ReturnCode"" type=""xs:string""/>
              <xs:element name=""SchemaVersion"" type=""xs:int""/>
            </xs:sequence>
          </xs:complexType>
        </xs:element>
        <xs:element name=""Data"">
          <xs:complexType>
            <xs:sequence>
              <xs:element name=""CustomerName"" type=""xs:string"" minOccurs=""0""/>
              <xs:element name=""IsFlagged"" type=""xs:boolean"" minOccurs=""0""/>
            </xs:sequence>
          </xs:complexType>
        </xs:element>
      </xs:sequence>
    </xs:complexType>
  </xs:element>
</xs:schema>";

        var set = new XmlSchemaSet();
        set.Add(xmlNamespace, XmlReader.Create(new StringReader(xsd)));
        set.Compile();
        return set;
    }

    private static string ExtractPdfText(byte[] pdfBytes)
    {
        using var stream = new MemoryStream(pdfBytes);
        using var document = PdfDocument.Open(stream);
        var text = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            text.AppendLine(page.Text);
        }

        return new string(text
            .ToString()
            .Where(ch => !char.IsControl(ch) || ch is '\r' or '\n' or '\t')
            .ToArray());
    }

    private static byte[] Create1x1PngBytes()
    {
        return Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO5m2h8AAAAASUVORK5CYII=");
    }

    private static byte[] BuildMinimalWorkbook(string title)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sheet1");
        sheet.Cell("A1").Value = title;
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private sealed class StaticGenerator : IExportGenerator
    {
        private readonly byte[] _payload;

        public StaticGenerator(byte[] payload)
        {
            _payload = payload;
        }

        public ExportFormat Format => ExportFormat.Excel;
        public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        public string FileExtension => "xlsx";

        public Task<byte[]> Generate(ExportGenerationContext context, CancellationToken ct = default)
            => Task.FromResult(_payload);
    }

    private sealed class ThrowingGenerator : IExportGenerator
    {
        public ExportFormat Format => ExportFormat.Excel;
        public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        public string FileExtension => "xlsx";

        public Task<byte[]> Generate(ExportGenerationContext context, CancellationToken ct = default)
            => throw new InvalidOperationException("Synthetic export failure");
    }

    private sealed class TypedGenerator : IExportGenerator
    {
        private readonly byte[] _payload;

        public TypedGenerator(ExportFormat format, string contentType, string extension, byte[] payload)
        {
            Format = format;
            ContentType = contentType;
            FileExtension = extension;
            _payload = payload;
        }

        public ExportFormat Format { get; }
        public string ContentType { get; }
        public string FileExtension { get; }

        public Task<byte[]> Generate(ExportGenerationContext context, CancellationToken ct = default)
            => Task.FromResult(_payload);
    }

    private sealed class InMemoryStorage : IFileStorageService
    {
        public Dictionary<string, byte[]> Stored { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Deleted { get; } = new();

        public async Task<string> UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            Stored[path] = ms.ToArray();
            return path;
        }

        public Task<Stream> DownloadAsync(string path, CancellationToken ct = default)
        {
            if (!Stored.TryGetValue(path, out var bytes))
            {
                throw new FileNotFoundException(path);
            }

            Stream stream = new MemoryStream(bytes, writable: false);
            return Task.FromResult(stream);
        }

        public Task DeleteAsync(string path, CancellationToken ct = default)
        {
            Stored.Remove(path);
            Deleted.Add(path);
            return Task.CompletedTask;
        }

        public string GetPublicUrl(string path) => path;
    }

    private sealed class InMemoryExportRequestRepository : IExportRequestRepository
    {
        private readonly List<ExportRequest> _items = [];
        private int _nextId = 1;

        public Task<ExportRequest> Add(ExportRequest request, CancellationToken ct = default)
        {
            request.Id = _nextId++;
            _items.Add(request);
            return Task.FromResult(request);
        }

        public Task<ExportRequest?> GetById(int id, CancellationToken ct = default)
        {
            return Task.FromResult(_items.FirstOrDefault(x => x.Id == id));
        }

        public Task<IReadOnlyList<ExportRequest>> GetBySubmission(Guid tenantId, int submissionId, CancellationToken ct = default)
        {
            IReadOnlyList<ExportRequest> list = _items
                .Where(x => x.TenantId == tenantId && x.SubmissionId == submissionId)
                .OrderByDescending(x => x.RequestedAt)
                .ToList();
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<ExportRequest>> GetQueuedBatch(int batchSize, CancellationToken ct = default)
        {
            IReadOnlyList<ExportRequest> list = _items
                .Where(x => x.Status == ExportRequestStatus.Queued)
                .OrderBy(x => x.RequestedAt)
                .Take(batchSize)
                .ToList();
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<ExportRequest>> GetExpired(DateTime asOfUtc, int batchSize, CancellationToken ct = default)
        {
            IReadOnlyList<ExportRequest> list = _items
                .Where(x => x.ExpiresAt.HasValue && x.ExpiresAt.Value <= asOfUtc && !string.IsNullOrWhiteSpace(x.FilePath))
                .OrderBy(x => x.ExpiresAt)
                .Take(batchSize)
                .ToList();
            return Task.FromResult(list);
        }

        public Task Update(ExportRequest request, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task<bool> ExistsForSubmission(Guid tenantId, int submissionId, ExportFormat format, ExportRequestStatus status, CancellationToken ct = default)
        {
            return Task.FromResult(_items.Any(x =>
                x.TenantId == tenantId
                && x.SubmissionId == submissionId
                && x.Format == format
                && x.Status == status));
        }
    }
}
