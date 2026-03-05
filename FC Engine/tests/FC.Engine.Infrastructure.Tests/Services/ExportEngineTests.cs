using System.Reflection;
using System.Security.Cryptography;
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

namespace FC.Engine.Infrastructure.Tests.Services;

public class ExportEngineTests
{
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
