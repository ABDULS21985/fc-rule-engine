using System.Text.Json;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.DataRecord;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FC.Engine.Infrastructure.Tests.Services;

public class HistoricalMigrationServiceTests
{
    [Fact]
    public async Task SetModuleSignOff_Is_Reflected_In_Tracker()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(nameof(SetModuleSignOff_Is_Reflected_In_Tracker));

        var module = new Module
        {
            ModuleCode = "BDC_CBN",
            ModuleName = "BDC CBN",
            RegulatorCode = "CBN",
            SheetCount = 12,
            DefaultFrequency = "Monthly",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Modules.Add(module);
        await db.SaveChangesAsync();

        var template = new ReturnTemplate
        {
            ModuleId = module.Id,
            ReturnCode = "BDC_CAP",
            Name = "BDC Capital",
            Frequency = ReturnFrequency.Monthly,
            StructuralCategory = StructuralCategory.FixedRow,
            PhysicalTableName = "return_data_bdc_cap",
            XmlRootElement = "return",
            XmlNamespace = "urn:bdc",
            IsSystemTemplate = false,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = "test"
        };
        db.ReturnTemplates.Add(template);
        await db.SaveChangesAsync();

        var period = new ReturnPeriod
        {
            TenantId = tenantId,
            ModuleId = module.Id,
            Year = 2025,
            Month = 12,
            Frequency = "Monthly",
            ReportingDate = new DateTime(2025, 12, 31),
            DeadlineDate = new DateTime(2026, 1, 30),
            IsOpen = false,
            Status = "Closed",
            CreatedAt = DateTime.UtcNow
        };
        db.ReturnPeriods.Add(period);
        await db.SaveChangesAsync();

        db.ImportJobs.Add(new ImportJob
        {
            TenantId = tenantId,
            TemplateId = template.Id,
            InstitutionId = 1,
            ReturnPeriodId = period.Id,
            SourceFileName = "legacy.xlsx",
            SourceFormat = HistoricalSourceFormat.Excel,
            Status = ImportJobStatus.Committed,
            RecordCount = 2,
            ErrorCount = 0,
            WarningCount = 1,
            ImportedBy = 99,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var sut = CreateService(db);

        await sut.SetModuleSignOff(tenantId, module.Id, true, 99, "Compliance reviewed");
        var tracker = await sut.GetTracker(tenantId);
        var moduleProgress = tracker.Modules.Single(x => x.ModuleId == module.Id);

        moduleProgress.AutoSignOffEligible.Should().BeTrue();
        moduleProgress.SignOff.Should().BeTrue();
        moduleProgress.SignedOffBy.Should().Be(99);
        moduleProgress.SignOffNotes.Should().Be("Compliance reviewed");
    }

    [Fact]
    public async Task CommitJob_Uses_Edited_Staged_Review_Values()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(nameof(CommitJob_Uses_Edited_Staged_Review_Values));

        var period = new ReturnPeriod
        {
            TenantId = tenantId,
            Year = 2025,
            Month = 12,
            Frequency = "Monthly",
            ReportingDate = new DateTime(2025, 12, 31),
            DeadlineDate = new DateTime(2026, 1, 30),
            IsOpen = false,
            CreatedAt = DateTime.UtcNow
        };
        db.ReturnPeriods.Add(period);
        await db.SaveChangesAsync();

        var template = new ReturnTemplate
        {
            ReturnCode = "BDC_CAP",
            Name = "BDC Capital",
            Frequency = ReturnFrequency.Monthly,
            StructuralCategory = StructuralCategory.FixedRow,
            PhysicalTableName = "return_data_bdc_cap",
            XmlRootElement = "return",
            XmlNamespace = "urn:bdc",
            IsSystemTemplate = false,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = "test"
        };
        db.ReturnTemplates.Add(template);
        await db.SaveChangesAsync();

        var stagedData = JsonSerializer.Serialize(new
        {
            records = new[]
            {
                new Dictionary<string, string?> { ["Legacy Balance"] = "100.25" }
            },
            mappings = new[]
            {
                new
                {
                    sourceIndex = 1,
                    sourceHeader = "Legacy Balance",
                    targetFieldName = "closing_balance",
                    targetFieldLabel = "Closing Balance",
                    confidence = 1.0,
                    ignored = false,
                    sampleValues = new[] { "100.25" }
                }
            },
            reviewedRecords = Array.Empty<object>(),
            unmappedColumns = Array.Empty<string>(),
            unmappedFields = Array.Empty<string>(),
            parserWarnings = Array.Empty<string>()
        });

        var job = new ImportJob
        {
            TenantId = tenantId,
            TemplateId = template.Id,
            InstitutionId = 11,
            ReturnPeriodId = period.Id,
            SourceFileName = "legacy.xlsx",
            SourceFormat = HistoricalSourceFormat.Excel,
            Status = ImportJobStatus.Staged,
            StagedData = stagedData,
            ImportedBy = 9,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.ImportJobs.Add(job);
        await db.SaveChangesAsync();

        var templateCache = new Mock<ITemplateMetadataCache>();
        templateCache.Setup(x => x.GetPublishedTemplate(tenantId, "BDC_CAP", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedTemplate
            {
                TemplateId = template.Id,
                ReturnCode = "BDC_CAP",
                Name = "BDC Capital",
                StructuralCategory = "FixedRow",
                CurrentVersion = new CachedTemplateVersion
                {
                    Id = 500,
                    Fields = new List<TemplateField>
                    {
                        new()
                        {
                            FieldName = "closing_balance",
                            DisplayName = "Closing Balance",
                            DataType = FieldDataType.Decimal,
                            FieldOrder = 1
                        }
                    }
                }
            });

        var dataRepo = new Mock<IGenericDataRepository>();
        ReturnDataRecord? persistedRecord = null;
        dataRepo.Setup(x => x.DeleteBySubmission("BDC_CAP", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        dataRepo.Setup(x => x.Save(It.IsAny<ReturnDataRecord>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<ReturnDataRecord, int, CancellationToken>((record, _, _) => persistedRecord = record)
            .Returns(Task.CompletedTask);

        var sut = CreateService(db, templateCache: templateCache.Object, dataRepository: dataRepo.Object);

        await sut.SaveStagedReview(
            tenantId,
            job.Id,
            new List<ImportStagedRecordDto>
            {
                new()
                {
                    RowNumber = 1,
                    Values = new Dictionary<string, string?> { ["closing_balance"] = "777.01" }
                }
            });

        var committed = await sut.CommitJob(tenantId, job.Id);

        committed.Status.Should().Be(ImportJobStatus.Committed);
        persistedRecord.Should().NotBeNull();
        persistedRecord!.Rows.Should().HaveCount(1);
        persistedRecord.Rows[0].GetValue("closing_balance").Should().Be(777.01m);

        var submission = await db.Submissions.SingleAsync(x =>
            x.ReturnCode == "BDC_CAP"
            && x.ReturnPeriodId == period.Id
            && x.Status == SubmissionStatus.Historical);
        submission.Status.Should().Be(SubmissionStatus.Historical);
    }

    private static HistoricalMigrationService CreateService(
        MetadataDbContext db,
        ITemplateMetadataCache? templateCache = null,
        IGenericDataRepository? dataRepository = null)
    {
        templateCache ??= new Mock<ITemplateMetadataCache>().Object;
        dataRepository ??= new Mock<IGenericDataRepository>().Object;

        var parsers = new List<IFileParser>();
        var audit = new Mock<IAuditLogger>();
        audit.Setup(x => x.Log(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<object?>(),
                It.IsAny<object?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var validation = new ValidationOrchestrator(
            new Mock<ITemplateMetadataCache>().Object,
            new Mock<IFormulaEvaluator>().Object,
            new Mock<ICrossSheetValidator>().Object,
            new Mock<IBusinessRuleEvaluator>().Object);

        return new HistoricalMigrationService(
            db,
            templateCache,
            parsers,
            dataRepository,
            validation,
            audit.Object,
            NullLogger<HistoricalMigrationService>.Instance);
    }

    private static MetadataDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new MetadataDbContext(options);
    }
}
