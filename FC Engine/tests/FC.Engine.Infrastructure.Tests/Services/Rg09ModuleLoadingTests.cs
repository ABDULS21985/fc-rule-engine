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

public class Rg09ModuleLoadingTests
{
    [Fact]
    public async Task Dmb_Basel3_Module_Imports_And_Publishes_EndToEnd()
    {
        await using var db = CreateDbContext(nameof(Dmb_Basel3_Module_Imports_And_Publishes_EndToEnd));
        var module = await SeedModules(db, "DMB_BASEL3", "NDIC_RETURNS", "NFIU_AML", "ESG_CLIMATE");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);

        var definition = await LoadDefinition("dmb_basel3.json");
        var validation = await sut.ValidateDefinition(definition);
        validation.IsValid.Should().BeTrue(string.Join(" | ", validation.Errors));
        validation.TemplateCount.Should().Be(15);

        var import = await sut.ImportModule(definition, "rg09-test");
        import.Success.Should().BeTrue(string.Join(" | ", import.Errors));
        import.TemplatesCreated.Should().Be(15);

        (await db.InterModuleDataFlows.CountAsync(f => f.SourceModuleId == module.Id)).Should().Be(10);
        (await db.IntraSheetFormulas.AnyAsync(f => f.CustomExpression != null && f.CustomExpression.Contains("FUNC:CAR", StringComparison.OrdinalIgnoreCase))).Should().BeTrue();
        (await db.IntraSheetFormulas.AnyAsync(f => f.CustomExpression != null && f.CustomExpression.Contains("FUNC:LCR", StringComparison.OrdinalIgnoreCase))).Should().BeTrue();
        (await db.IntraSheetFormulas.AnyAsync(f => f.CustomExpression != null && f.CustomExpression.Contains("FUNC:NSFR", StringComparison.OrdinalIgnoreCase))).Should().BeTrue();
        (await db.IntraSheetFormulas.AnyAsync(f => f.CustomExpression != null && f.CustomExpression.Contains("FUNC:ECL", StringComparison.OrdinalIgnoreCase))).Should().BeTrue();

        var publish = await sut.PublishModule("DMB_BASEL3", "rg09-approver");
        publish.Success.Should().BeTrue(string.Join(" | ", publish.Errors));
        publish.TablesCreated.Should().Be(15);

        cache.Verify(c => c.InvalidateModule(module.Id), Times.Once);
    }

    [Fact]
    public async Task Ndic_Module_Imports_And_Premium_Formula_Is_Present()
    {
        await using var db = CreateDbContext(nameof(Ndic_Module_Imports_And_Premium_Formula_Is_Present));
        var module = await SeedModules(db, "NDIC_RETURNS", "FATF_EVAL");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);

        var definition = await LoadDefinition("ndic_returns.json");
        var validation = await sut.ValidateDefinition(definition);
        validation.IsValid.Should().BeTrue(string.Join(" | ", validation.Errors));
        validation.TemplateCount.Should().Be(11);

        var import = await sut.ImportModule(definition, "rg09-test");
        import.Success.Should().BeTrue(string.Join(" | ", import.Errors));
        import.TemplatesCreated.Should().Be(11);

        (await db.InterModuleDataFlows.CountAsync(f => f.SourceModuleId == module.Id)).Should().Be(2);
        (await db.IntraSheetFormulas.AnyAsync(f => f.TargetFieldName == "premium_assessment" && f.FormulaType == FormulaType.GreaterThanOrEqual)).Should().BeTrue();

        var publish = await sut.PublishModule("NDIC_RETURNS", "rg09-approver");
        publish.Success.Should().BeTrue(string.Join(" | ", publish.Errors));
        publish.TablesCreated.Should().Be(11);

        cache.Verify(c => c.InvalidateModule(module.Id), Times.Once);
    }

    [Fact]
    public async Task Psp_Module_Imports_And_Channel_Validation_Formulas_Are_Present()
    {
        await using var db = CreateDbContext(nameof(Psp_Module_Imports_And_Channel_Validation_Formulas_Are_Present));
        var module = await SeedModules(db, "PSP_FINTECH", "NDIC_RETURNS", "NFIU_AML", "FATF_EVAL");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);

        var definition = await LoadDefinition("psp_fintech.json");
        var validation = await sut.ValidateDefinition(definition);
        validation.IsValid.Should().BeTrue(string.Join(" | ", validation.Errors));
        validation.TemplateCount.Should().Be(14);

        var import = await sut.ImportModule(definition, "rg09-test");
        import.Success.Should().BeTrue(string.Join(" | ", import.Errors));
        import.TemplatesCreated.Should().Be(14);

        (await db.InterModuleDataFlows.CountAsync(f => f.SourceModuleId == module.Id)).Should().Be(7);
        (await db.IntraSheetFormulas.AnyAsync(f => f.TargetFieldName == "total_txn_count" && f.FormulaType == FormulaType.Sum)).Should().BeTrue();
        (await db.IntraSheetFormulas.AnyAsync(f => f.TargetFieldName == "total_txn_value" && f.FormulaType == FormulaType.Sum)).Should().BeTrue();

        var publish = await sut.PublishModule("PSP_FINTECH", "rg09-approver");
        publish.Success.Should().BeTrue(string.Join(" | ", publish.Errors));
        publish.TablesCreated.Should().Be(14);

        cache.Verify(c => c.InvalidateModule(module.Id), Times.Once);
    }

    [Fact]
    public async Task Pmb_Module_Imports_And_Nhf_Rate_Cap_Validation_Is_Present()
    {
        await using var db = CreateDbContext(nameof(Pmb_Module_Imports_And_Nhf_Rate_Cap_Validation_Is_Present));
        var module = await SeedModules(db, "PMB_CBN", "NDIC_RETURNS", "NFIU_AML");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);

        var definition = await LoadDefinition("pmb_cbn.json");
        var validation = await sut.ValidateDefinition(definition);
        validation.IsValid.Should().BeTrue(string.Join(" | ", validation.Errors));
        validation.TemplateCount.Should().Be(12);

        var import = await sut.ImportModule(definition, "rg09-test");
        import.Success.Should().BeTrue(string.Join(" | ", import.Errors));
        import.TemplatesCreated.Should().Be(12);

        (await db.InterModuleDataFlows.CountAsync(f => f.SourceModuleId == module.Id)).Should().Be(6);
        (await db.IntraSheetFormulas.AnyAsync(f => f.TargetFieldName == "nhf_avg_rate" && f.FormulaType == FormulaType.LessThanOrEqual)).Should().BeTrue();

        var publish = await sut.PublishModule("PMB_CBN", "rg09-approver");
        publish.Success.Should().BeTrue(string.Join(" | ", publish.Errors));
        publish.TablesCreated.Should().Be(12);

        cache.Verify(c => c.InvalidateModule(module.Id), Times.Once);
    }

    [Fact]
    public async Task Rg09_All_Modules_Produce_Expected_Total_Scale()
    {
        await using var db = CreateDbContext(nameof(Rg09_All_Modules_Produce_Expected_Total_Scale));
        await SeedModules(
            db,
            "DMB_BASEL3",
            "NDIC_RETURNS",
            "PSP_FINTECH",
            "PMB_CBN",
            "NFIU_AML",
            "ESG_CLIMATE",
            "FATF_EVAL");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out var ddlExecutor);

        foreach (var file in new[] { "dmb_basel3.json", "ndic_returns.json", "psp_fintech.json", "pmb_cbn.json" })
        {
            var definition = await LoadDefinition(file);
            var validation = await sut.ValidateDefinition(definition);
            validation.IsValid.Should().BeTrue(string.Join(" | ", validation.Errors));

            var import = await sut.ImportModule(definition, "rg09-test");
            import.Success.Should().BeTrue(string.Join(" | ", import.Errors));
        }

        (await db.ReturnTemplates.CountAsync()).Should().Be(52);
        (await db.TemplateFields.CountAsync()).Should().BeGreaterOrEqualTo(1100);
        (await db.IntraSheetFormulas.CountAsync()).Should().BeGreaterOrEqualTo(400);
        (await db.CrossSheetRules.CountAsync()).Should().Be(80);
        (await db.InterModuleDataFlows.CountAsync()).Should().Be(25);

        foreach (var code in new[] { "DMB_BASEL3", "NDIC_RETURNS", "PSP_FINTECH", "PMB_CBN" })
        {
            var publish = await sut.PublishModule(code, "rg09-approver");
            publish.Success.Should().BeTrue(string.Join(" | ", publish.Errors));
        }

        ddlExecutor.Verify(
            e => e.Execute(
                It.IsAny<int>(),
                It.IsAny<int?>(),
                It.IsAny<int>(),
                It.IsAny<DdlScript>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(52));
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
                DefaultFrequency = "Monthly",
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
            .Returns(new DdlScript("CREATE TABLE dbo.[tmp_rg09](id INT, TenantId UNIQUEIDENTIFIER NULL);", "DROP TABLE dbo.[tmp_rg09];"));
        ddlEngine.Setup(d => d.GenerateAlterTable(It.IsAny<FC.Engine.Domain.Metadata.ReturnTemplate>(), It.IsAny<FC.Engine.Domain.Metadata.TemplateVersion>(), It.IsAny<FC.Engine.Domain.Metadata.TemplateVersion>()))
            .Returns(new DdlScript("ALTER TABLE dbo.[tmp_rg09] ADD test_col INT NULL;", "ALTER TABLE dbo.[tmp_rg09] DROP COLUMN test_col;"));

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
        var path = Path.Combine(root, "docs", "module-definitions", "rg09", fileName);
        File.Exists(path).Should().BeTrue($"Expected RG-09 definition file at {path}");
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
