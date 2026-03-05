using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.DynamicSchema;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FC.Engine.Infrastructure.Tests.Services;

public class ModuleImportServiceTests
{
    [Fact]
    public async Task Valid_JSON_Imports_All_Records()
    {
        await using var db = CreateDbContext(nameof(Valid_JSON_Imports_All_Records));
        SeedModule(db, "BDC_CBN");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache);

        var result = await sut.ImportModule(ValidDefinitionJson(), "tester");

        result.Success.Should().BeTrue(string.Join(" | ", result.Errors));
        result.TemplatesCreated.Should().Be(1);
        result.FieldsCreated.Should().Be(4);
        result.FormulasCreated.Should().Be(2);
        result.CrossSheetRulesCreated.Should().Be(0);

        (await db.ReturnTemplates.CountAsync()).Should().Be(1);
        (await db.TemplateVersions.CountAsync()).Should().Be(1);
        (await db.TemplateFields.CountAsync()).Should().Be(4);
        (await db.TemplateItemCodes.CountAsync()).Should().Be(2);
        (await db.IntraSheetFormulas.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Duplicate_ReturnCode_Rejected()
    {
        await using var db = CreateDbContext(nameof(Duplicate_ReturnCode_Rejected));
        SeedModule(db, "BDC_CBN");
        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache);

        var result = await sut.ValidateDefinition(DuplicateReturnCodeJson());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Duplicate ReturnCode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Circular_Dependency_Detected_And_Rejected()
    {
        await using var db = CreateDbContext(nameof(Circular_Dependency_Detected_And_Rejected));
        SeedModule(db, "BDC_CBN");
        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache);

        var result = await sut.ValidateDefinition(CircularDependencyJson());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("circular", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Publish_Updates_Template_Status_To_Published()
    {
        await using var db = CreateDbContext(nameof(Publish_Updates_Template_Status_To_Published));
        var module = SeedModule(db, "BDC_CBN");
        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache);
        var import = await sut.ImportModule(ValidDefinitionJson(), "tester");
        import.Success.Should().BeTrue();

        var publish = await sut.PublishModule(module.ModuleCode, "approver");

        publish.Success.Should().BeTrue(string.Join(" | ", publish.Errors));
        publish.TablesCreated.Should().Be(1);
        publish.VersionsPublished.Should().Be(1);

        var version = await db.TemplateVersions.SingleAsync();
        version.Status.Should().Be(TemplateStatus.Published);

        var moduleVersion = await db.ModuleVersions
            .OrderByDescending(v => v.Id)
            .FirstAsync(v => v.ModuleId == module.Id);
        moduleVersion.Status.Should().Be("Published");

        cache.Verify(c => c.InvalidateModule(module.Id), Times.Once);
    }

    private static MetadataDbContext CreateDbContext(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new MetadataDbContext(options);
    }

    private static Module SeedModule(MetadataDbContext db, string code)
    {
        var module = new Module
        {
            ModuleCode = code,
            ModuleName = "BDC Module",
            RegulatorCode = "CBN",
            IsActive = true,
            DefaultFrequency = "Monthly",
            CreatedAt = DateTime.UtcNow
        };

        db.Modules.Add(module);
        db.SaveChanges();
        return module;
    }

    private static ModuleImportService CreateSut(MetadataDbContext db, Mock<ITemplateMetadataCache> cache)
    {
        var ddl = new Mock<IDdlEngine>();
        ddl.Setup(d => d.GenerateCreateTable(It.IsAny<FC.Engine.Domain.Metadata.ReturnTemplate>(), It.IsAny<FC.Engine.Domain.Metadata.TemplateVersion>()))
            .Returns(new DdlScript("CREATE TABLE dbo.[tmp_rg07](id INT);", "DROP TABLE dbo.[tmp_rg07];"));
        ddl.Setup(d => d.GenerateAlterTable(It.IsAny<FC.Engine.Domain.Metadata.ReturnTemplate>(), It.IsAny<FC.Engine.Domain.Metadata.TemplateVersion>(), It.IsAny<FC.Engine.Domain.Metadata.TemplateVersion>()))
            .Returns(new DdlScript("ALTER TABLE dbo.[tmp_rg07] ADD test_col INT NULL;", "ALTER TABLE dbo.[tmp_rg07] DROP COLUMN test_col;"));

        var ddlExec = new Mock<IDdlMigrationExecutor>();
        ddlExec.Setup(e => e.Execute(
                It.IsAny<int>(),
                It.IsAny<int?>(),
                It.IsAny<int>(),
                It.IsAny<DdlScript>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationResult(true, null));
        ddlExec.Setup(e => e.Rollback(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationResult(true, null));

        return new ModuleImportService(
            db,
            ddl.Object,
            ddlExec.Object,
            cache.Object,
            new SqlTypeMapper(),
            NullLogger<ModuleImportService>.Instance);
    }

    private static string ValidDefinitionJson()
    {
        return """
               {
                 "moduleCode": "BDC_CBN",
                 "moduleVersion": "1.0.0",
                 "description": "BDC module",
                 "templates": [
                   {
                     "returnCode": "BDC_FXV",
                     "name": "FX Volumes",
                     "frequency": "Monthly",
                     "structuralCategory": "FixedRow",
                     "tablePrefix": "bdc",
                     "sections": [
                       { "code": "FXV", "name": "FX Section", "displayOrder": 1 }
                     ],
                     "fields": [
                       { "fieldCode": "usd_volume", "label": "USD Volume", "dataType": "Money", "section": "FXV", "required": true, "displayOrder": 1 },
                       { "fieldCode": "gbp_volume", "label": "GBP Volume", "dataType": "Money", "section": "FXV", "required": true, "displayOrder": 2 },
                       { "fieldCode": "total_fx_volume", "label": "Total FX", "dataType": "Money", "section": "FXV", "required": true, "displayOrder": 3 },
                       { "fieldCode": "usd_rate_avg", "label": "USD Rate", "dataType": "Rate", "section": "FXV", "required": true, "displayOrder": 4 }
                     ],
                     "itemCodes": [
                       { "code": "USD", "label": "USD", "displayOrder": 1 },
                       { "code": "GBP", "label": "GBP", "displayOrder": 2 }
                     ],
                     "formulas": [
                       {
                         "formulaType": "Sum",
                         "targetField": "total_fx_volume",
                         "sourceFields": [ "usd_volume", "gbp_volume" ],
                         "severity": "Error",
                         "description": "Total must be sum"
                       },
                       {
                         "formulaType": "Custom",
                         "customFunction": "RATE_BAND_CHECK",
                         "targetField": "usd_rate_avg",
                         "sourceFields": [],
                         "parameters": { "reference_rate": 1500, "band_percent": 10 },
                         "severity": "Warning",
                         "description": "Rate in band"
                       }
                     ],
                     "crossSheetRules": []
                   }
                 ],
                 "interModuleDataFlows": []
               }
               """;
    }

    private static string DuplicateReturnCodeJson()
    {
        return """
               {
                 "moduleCode": "BDC_CBN",
                 "moduleVersion": "1.0.0",
                 "templates": [
                   { "returnCode": "BDC_DUP", "name": "One", "frequency": "Monthly", "structuralCategory": "FixedRow", "fields": [ { "fieldCode": "a", "label": "A", "dataType": "Money", "required": true, "displayOrder": 1 } ], "formulas": [] },
                   { "returnCode": "BDC_DUP", "name": "Two", "frequency": "Monthly", "structuralCategory": "FixedRow", "fields": [ { "fieldCode": "b", "label": "B", "dataType": "Money", "required": true, "displayOrder": 1 } ], "formulas": [] }
                 ]
               }
               """;
    }

    private static string CircularDependencyJson()
    {
        return """
               {
                 "moduleCode": "BDC_CBN",
                 "moduleVersion": "1.0.0",
                 "templates": [
                   {
                     "returnCode": "BDC_CYCLE",
                     "name": "Cycle",
                     "frequency": "Monthly",
                     "structuralCategory": "FixedRow",
                     "fields": [
                       { "fieldCode": "a", "label": "A", "dataType": "Money", "required": true, "displayOrder": 1 },
                       { "fieldCode": "b", "label": "B", "dataType": "Money", "required": true, "displayOrder": 2 }
                     ],
                     "formulas": [
                       { "formulaType": "Sum", "targetField": "a", "sourceFields": [ "b" ], "severity": "Error" },
                       { "formulaType": "Sum", "targetField": "b", "sourceFields": [ "a" ], "severity": "Error" }
                     ]
                   }
                 ]
               }
               """;
    }
}
