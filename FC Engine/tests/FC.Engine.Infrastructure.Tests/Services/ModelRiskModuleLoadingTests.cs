using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.DataRecord;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.DynamicSchema;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FC.Engine.Infrastructure.Validation;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FC.Engine.Infrastructure.Tests.Services;

public class ModelRiskModuleLoadingTests
{
    [Fact]
    public async Task Model_Risk_Module_Imports_And_Publishes_Nine_Return_Sheets()
    {
        await using var db = CreateDbContext(nameof(Model_Risk_Module_Imports_And_Publishes_Nine_Return_Sheets));
        var module = await SeedModules(db, "MODEL_RISK");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out var ddlExecutor);

        var definition = await LoadDefinition("rg50-model-risk-module-definition.json");
        var validation = await sut.ValidateDefinition(definition);
        validation.IsValid.Should().BeTrue(string.Join(" | ", validation.Errors));
        validation.TemplateCount.Should().Be(9);

        var import = await sut.ImportModule(definition, "model-risk-test");
        import.Success.Should().BeTrue(string.Join(" | ", import.Errors));
        import.TemplatesCreated.Should().Be(9);
        import.CrossSheetRulesCreated.Should().Be(6);
        import.FormulasCreated.Should().Be(2);

        (await db.ReturnTemplates.CountAsync(t => t.ModuleId == module.Id)).Should().Be(9);
        (await db.CrossSheetRules.CountAsync()).Should().Be(6);
        (await db.IntraSheetFormulas.CountAsync(f => f.TargetFieldName == "appetite_headroom_percent")).Should().Be(1);
        (await db.IntraSheetFormulas.CountAsync(f => f.TargetFieldName == "variance_value")).Should().Be(1);
        (await db.TemplateFields.CountAsync(f => f.RegulatoryReference != null && f.RegulatoryReference.StartsWith("CBN-MRM-", StringComparison.OrdinalIgnoreCase))).Should().BeGreaterThan(40);

        var publish = await sut.PublishModule("MODEL_RISK", "model-risk-approver");
        publish.Success.Should().BeTrue(string.Join(" | ", publish.Errors));
        publish.TablesCreated.Should().Be(9);

        ddlExecutor.Verify(
            e => e.Execute(
                It.IsAny<int>(),
                It.IsAny<int?>(),
                It.IsAny<int>(),
                It.IsAny<DdlScript>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(9));

        cache.Verify(c => c.InvalidateModule(module.Id), Times.Once);
    }

    [Fact]
    public async Task Model_Risk_Appetite_Headroom_Formula_Fires_When_Stored_Value_Is_Wrong()
    {
        await using var db = CreateDbContext(nameof(Model_Risk_Appetite_Headroom_Formula_Fires_When_Stored_Value_Is_Wrong));
        await SeedModules(db, "MODEL_RISK");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);
        (await sut.ImportModule(await LoadDefinition("rg50-model-risk-module-definition.json"), "model-risk-test")).Success.Should().BeTrue();

        var errors = await EvaluateImportedTemplateFormulas(
            db,
            "MRM_RAP",
            new Dictionary<string, object?>
            {
                ["model_code"] = "PD_CORE",
                ["appetite_limit_percent"] = 8m,
                ["observed_exposure_percent"] = 11m,
                ["appetite_headroom_percent"] = 0m,
                ["appetite_status"] = "Breach",
                ["review_date"] = DateTime.UtcNow.Date
            });

        errors.Should().Contain(e => e.Field.Contains("appetite_headroom_percent", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<IReadOnlyList<ValidationError>> EvaluateImportedTemplateFormulas(
        MetadataDbContext db,
        string returnCode,
        IDictionary<string, object?> fieldValues)
    {
        var template = await db.ReturnTemplates.SingleAsync(t => t.ReturnCode == returnCode);
        var versionId = await db.TemplateVersions
            .Where(v => v.TemplateId == template.Id)
            .Select(v => v.Id)
            .SingleAsync();

        var formulas = await db.IntraSheetFormulas
            .Where(f => f.TemplateVersionId == versionId && f.IsActive)
            .OrderBy(f => f.SortOrder)
            .ToListAsync();

        var cache = new Mock<ITemplateMetadataCache>();
        cache.Setup(c => c.GetPublishedTemplate(returnCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedTemplate
            {
                TemplateId = template.Id,
                ReturnCode = returnCode,
                StructuralCategory = template.StructuralCategory.ToString(),
                CurrentVersion = new CachedTemplateVersion
                {
                    Id = versionId,
                    VersionNumber = 1,
                    IntraSheetFormulas = formulas
                }
            });

        var evaluator = new FormulaEvaluator(cache.Object);
        var record = new ReturnDataRecord(returnCode, versionId, template.StructuralCategory);
        var row = new ReturnDataRow { RowKey = "ROW-1" };
        foreach (var kvp in fieldValues)
        {
            row.SetValue(kvp.Key, kvp.Value);
        }

        record.AddRow(row);
        return await evaluator.Evaluate(record, CancellationToken.None);
    }

    private static MetadataDbContext CreateDbContext(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new MetadataDbContext(options);
    }

    private static async Task<Module> SeedModules(MetadataDbContext db, params string[] moduleCodes)
    {
        Module? first = null;
        foreach (var code in moduleCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var module = new Module
            {
                ModuleCode = code,
                ModuleName = $"{code} Module",
                RegulatorCode = "CBN",
                DefaultFrequency = "Quarterly",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            db.Modules.Add(module);
            first ??= module;
        }

        await db.SaveChangesAsync();
        return first!;
    }

    private static ModuleImportService CreateSut(
        MetadataDbContext db,
        Mock<ITemplateMetadataCache> cache,
        out Mock<IDdlMigrationExecutor> ddlExecutor)
    {
        var ddlEngine = new Mock<IDdlEngine>();
        ddlEngine.Setup(d => d.GenerateCreateTable(It.IsAny<FC.Engine.Domain.Metadata.ReturnTemplate>(), It.IsAny<FC.Engine.Domain.Metadata.TemplateVersion>()))
            .Returns(new DdlScript("CREATE TABLE dbo.[tmp_model_risk](id INT, TenantId UNIQUEIDENTIFIER NULL);", "DROP TABLE dbo.[tmp_model_risk];"));
        ddlEngine.Setup(d => d.GenerateAlterTable(It.IsAny<FC.Engine.Domain.Metadata.ReturnTemplate>(), It.IsAny<FC.Engine.Domain.Metadata.TemplateVersion>(), It.IsAny<FC.Engine.Domain.Metadata.TemplateVersion>()))
            .Returns(new DdlScript("ALTER TABLE dbo.[tmp_model_risk] ADD test_col INT NULL;", "ALTER TABLE dbo.[tmp_model_risk] DROP COLUMN test_col;"));

        ddlExecutor = new Mock<IDdlMigrationExecutor>();
        ddlExecutor.Setup(e => e.Execute(
                It.IsAny<int>(),
                It.IsAny<int?>(),
                It.IsAny<int>(),
                It.IsAny<DdlScript>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationResult(true, null));
        ddlExecutor.Setup(e => e.Rollback(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationResult(true, null));

        return new ModuleImportService(
            db,
            ddlEngine.Object,
            ddlExecutor.Object,
            cache.Object,
            new SqlTypeMapper(),
            NullLogger<ModuleImportService>.Instance,
            null);
    }

    private static async Task<string> LoadDefinition(string fileName)
    {
        var root = FindSolutionRoot();
        var path = Path.Combine(
            root,
            "src",
            "FC.Engine.Migrator",
            "SeedData",
            "ModuleDefinitions",
            fileName);

        File.Exists(path).Should().BeTrue($"Expected MODEL_RISK definition file at {path}");
        return await File.ReadAllTextAsync(path);
    }

    private static string FindSolutionRoot()
    {
        var current = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(current);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "FCEngine.sln");
            if (File.Exists(candidate))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate FCEngine.sln from test base directory.");
    }
}
