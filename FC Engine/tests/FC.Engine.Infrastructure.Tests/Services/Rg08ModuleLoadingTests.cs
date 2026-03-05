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

public class Rg08ModuleLoadingTests
{
    [Fact]
    public async Task BDC_Module_Imports_12_Templates_Successfully()
    {
        await using var db = CreateDbContext(nameof(BDC_Module_Imports_12_Templates_Successfully));
        var bdcModule = await SeedModules(db, "BDC_CBN", "NFIU_AML");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);

        var definition = await LoadDefinition("rg08-bdc-cbn-module-definition.json");
        var validation = await sut.ValidateDefinition(definition);
        validation.IsValid.Should().BeTrue(string.Join(" | ", validation.Errors));
        validation.TemplateCount.Should().Be(12);

        var import = await sut.ImportModule(definition, "rg08-test");
        import.Success.Should().BeTrue(string.Join(" | ", import.Errors));
        import.TemplatesCreated.Should().Be(12);

        (await db.ReturnTemplates.CountAsync(t => t.ModuleId == bdcModule.Id)).Should().Be(12);
        (await db.InterModuleDataFlows.CountAsync(f => f.SourceModuleId == bdcModule.Id)).Should().Be(4);
    }

    [Fact]
    public async Task BDC_Publish_Creates_12_Tables_With_RLS()
    {
        await using var db = CreateDbContext(nameof(BDC_Publish_Creates_12_Tables_With_RLS));
        var module = await SeedModules(db, "BDC_CBN", "NFIU_AML");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out var ddlExecutor);

        var definition = await LoadDefinition("rg08-bdc-cbn-module-definition.json");
        (await sut.ImportModule(definition, "rg08-test")).Success.Should().BeTrue();

        var publish = await sut.PublishModule("BDC_CBN", "rg08-approver");
        publish.Success.Should().BeTrue(string.Join(" | ", publish.Errors));
        publish.TablesCreated.Should().Be(12);

        ddlExecutor.Verify(
            e => e.Execute(
                It.IsAny<int>(),
                It.IsAny<int?>(),
                It.IsAny<int>(),
                It.IsAny<DdlScript>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(12));

        cache.Verify(c => c.InvalidateModule(module.Id), Times.Once);
    }

    [Fact]
    public async Task BDC_FX_Rate_Band_Validation_Fires()
    {
        await using var db = CreateDbContext(nameof(BDC_FX_Rate_Band_Validation_Fires));
        await SeedModules(db, "BDC_CBN", "NFIU_AML");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);
        (await sut.ImportModule(await LoadDefinition("rg08-bdc-cbn-module-definition.json"), "rg08-test")).Success.Should().BeTrue();

        var errors = await EvaluateImportedTemplateFormulas(
            db,
            "BDC_FXV",
            new Dictionary<string, object?>
            {
                ["usd_buying_volume"] = 100m,
                ["gbp_buying_volume"] = 0m,
                ["eur_buying_volume"] = 0m,
                ["cny_buying_volume"] = 0m,
                ["other_buying_volume"] = 0m,
                ["usd_selling_volume"] = 50m,
                ["gbp_selling_volume"] = 0m,
                ["eur_selling_volume"] = 0m,
                ["cny_selling_volume"] = 0m,
                ["other_selling_volume"] = 0m,
                ["total_buying_volume"] = 100m,
                ["total_selling_volume"] = 50m,
                ["net_position"] = 50m,

                ["usd_buying_rate_avg"] = 2000m,
                ["gbp_buying_rate_avg"] = 1900m,
                ["eur_buying_rate_avg"] = 1650m,
                ["cny_buying_rate_avg"] = 210m,
                ["other_buying_rate_avg"] = 1000m,
                ["total_buying_rate_avg"] = 6760m,

                ["usd_selling_rate_avg"] = 2010m,
                ["gbp_selling_rate_avg"] = 1910m,
                ["eur_selling_rate_avg"] = 1660m,
                ["cny_selling_rate_avg"] = 220m,
                ["other_selling_rate_avg"] = 1010m,
                ["total_selling_rate_avg"] = 6810m,
                ["spread_avg"] = 50m,

                ["usd_rate_band_ok"] = 1m,
                ["gbp_rate_band_ok"] = 1m,
                ["eur_rate_band_ok"] = 1m,
                ["cny_rate_band_ok"] = 1m,
                ["other_rate_band_ok"] = 1m
            });

        errors.Should().Contain(e => e.Field == "usd_rate_band_ok" && e.Category == ValidationCategory.IntraSheet);
    }

    [Fact]
    public async Task BDC_Capital_Check_Category_A_35M()
    {
        await using var db = CreateDbContext(nameof(BDC_Capital_Check_Category_A_35M));
        await SeedModules(db, "BDC_CBN", "NFIU_AML");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);
        (await sut.ImportModule(await LoadDefinition("rg08-bdc-cbn-module-definition.json"), "rg08-test")).Success.Should().BeTrue();

        var errors = await EvaluateImportedTemplateFormulas(
            db,
            "BDC_CAP",
            new Dictionary<string, object?>
            {
                ["paid_up_capital"] = 20000000m,
                ["retained_earnings"] = 7000000m,
                ["statutory_reserves"] = 4000000m,
                ["other_reserves"] = 3000000m,
                ["total_shareholders_funds"] = 34000000m,
                ["total_assets"] = 100000000m,
                ["minimum_capital_requirement"] = 35000000m,
                ["capital_buffer"] = -1000000m,
                ["capital_adequacy_ratio"] = 0.34m
            });

        errors.Should().Contain(e => e.Field == "total_shareholders_funds");
    }

    [Fact]
    public async Task BDC_Total_Assets_CrossSheet_CAP_Equals_FIN()
    {
        await using var db = CreateDbContext(nameof(BDC_Total_Assets_CrossSheet_CAP_Equals_FIN));
        await SeedModules(db, "BDC_CBN", "NFIU_AML");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);
        (await sut.ImportModule(await LoadDefinition("rg08-bdc-cbn-module-definition.json"), "rg08-test")).Success.Should().BeTrue();

        var rules = await db.CrossSheetRules
            .Include(r => r.Operands)
            .Include(r => r.Expression)
            .Where(r => r.IsActive && r.Operands.Any(o => o.TemplateReturnCode == "BDC_CAP"))
            .ToListAsync();

        var formulaRepo = new Mock<IFormulaRepository>();
        formulaRepo.Setup(r => r.GetCrossSheetRulesForTemplate("BDC_CAP", It.IsAny<CancellationToken>()))
            .ReturnsAsync(rules);

        var dataRepo = new Mock<IGenericDataRepository>();
        dataRepo.Setup(d => d.GetByInstitutionAndPeriod("BDC_FIN", 77, 202601, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRecord("BDC_FIN", ("total_assets", 95000000m)));

        var validator = new CrossSheetValidator(
            formulaRepo.Object,
            dataRepo.Object,
            cache.Object);

        var capRecord = CreateRecord("BDC_CAP", ("total_assets", 100000000m));
        var errors = await validator.Validate(capRecord, 77, 202601, CancellationToken.None);

        errors.Should().Contain(e => e.RuleId.Contains("BDC_CBN_BDC_CAP_CSR_1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BDC_AML_Data_Flows_To_NFIU()
    {
        await using var db = CreateDbContext(nameof(BDC_AML_Data_Flows_To_NFIU));
        await SeedModules(db, "BDC_CBN", "NFIU_AML");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);
        (await sut.ImportModule(await LoadDefinition("rg08-bdc-cbn-module-definition.json"), "rg08-test")).Success.Should().BeTrue();

        var tenantId = Guid.NewGuid();
        var submission = Submission.Create(77, 202601, "BDC_AML", tenantId);
        db.Submissions.Add(submission);
        await db.SaveChangesAsync();

        var entitlement = new Mock<IEntitlementService>();
        entitlement.Setup(e => e.HasModuleAccess(tenantId, "NFIU_AML", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var genericRepo = new Mock<IGenericDataRepository>();
        genericRepo.Setup(r => r.ReadFieldValue("BDC_AML", submission.Id, "str_filed_count", It.IsAny<CancellationToken>()))
            .ReturnsAsync(9m);
        genericRepo.Setup(r => r.ReadFieldValue("BDC_AML", submission.Id, "ctr_filed_count", It.IsAny<CancellationToken>()))
            .ReturnsAsync(13m);
        genericRepo.Setup(r => r.ReadFieldValue("BDC_AML", submission.Id, "pep_customers_count", It.IsAny<CancellationToken>()))
            .ReturnsAsync(4m);
        genericRepo.Setup(r => r.ReadFieldValue("BDC_AML", submission.Id, "tfs_screenings_count", It.IsAny<CancellationToken>()))
            .ReturnsAsync(17m);

        var engine = new InterModuleDataFlowEngine(
            db,
            entitlement.Object,
            genericRepo.Object,
            NullLogger<InterModuleDataFlowEngine>.Instance);

        await engine.ProcessDataFlows(
            tenantId,
            submission.Id,
            "BDC_CBN",
            "BDC_AML",
            submission.InstitutionId,
            submission.ReturnPeriodId,
            CancellationToken.None);

        genericRepo.Verify(r => r.WriteFieldValue(
                "NFIU_STR",
                It.IsAny<int>(),
                "str_filed_count",
                9m,
                "InterModule",
                "BDC_CBN/BDC_AML/str_filed_count",
                It.IsAny<CancellationToken>()),
            Times.Once);

        genericRepo.Verify(r => r.WriteFieldValue(
                "NFIU_CTR",
                It.IsAny<int>(),
                "ctr_filed_count",
                13m,
                "InterModule",
                "BDC_CBN/BDC_AML/ctr_filed_count",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MFB_Module_Imports_12_Templates()
    {
        await using var db = CreateDbContext(nameof(MFB_Module_Imports_12_Templates));
        var module = await SeedModules(db, "MFB_PAR", "NDIC_RETURNS", "NFIU_AML");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);

        var definition = await LoadDefinition("rg08-mfb-par-module-definition.json");
        var validation = await sut.ValidateDefinition(definition);
        validation.IsValid.Should().BeTrue(string.Join(" | ", validation.Errors));
        validation.TemplateCount.Should().Be(12);

        var import = await sut.ImportModule(definition, "rg08-test");
        import.Success.Should().BeTrue(string.Join(" | ", import.Errors));
        import.TemplatesCreated.Should().Be(12);

        (await db.ReturnTemplates.CountAsync(t => t.ModuleId == module.Id)).Should().Be(12);
        (await db.InterModuleDataFlows.CountAsync(f => f.SourceModuleId == module.Id)).Should().Be(7);
    }

    [Fact]
    public async Task MFB_PAR_Ratio_Calculates_Correctly()
    {
        await using var db = CreateDbContext(nameof(MFB_PAR_Ratio_Calculates_Correctly));
        await SeedModules(db, "MFB_PAR", "NDIC_RETURNS", "NFIU_AML");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);
        (await sut.ImportModule(await LoadDefinition("rg08-mfb-par-module-definition.json"), "rg08-test")).Success.Should().BeTrue();

        var errors = await EvaluateImportedTemplateFormulas(
            db,
            "MFB_PAR",
            new Dictionary<string, object?>
            {
                ["current_outstanding_principal"] = 80m,
                ["current_interest_arrears"] = 20m,
                ["current_total_exposure"] = 100m,

                ["par_1_30_outstanding_principal"] = 30m,
                ["par_1_30_interest_arrears"] = 0m,
                ["par_1_30_total_exposure"] = 30m,

                ["par_31_60_outstanding_principal"] = 20m,
                ["par_31_60_interest_arrears"] = 0m,
                ["par_31_60_total_exposure"] = 20m,

                ["par_61_90_outstanding_principal"] = 10m,
                ["par_61_90_interest_arrears"] = 0m,
                ["par_61_90_total_exposure"] = 10m,

                ["par_91_180_outstanding_principal"] = 5m,
                ["par_91_180_interest_arrears"] = 0m,
                ["par_91_180_total_exposure"] = 5m,

                ["par_181_365_outstanding_principal"] = 2m,
                ["par_181_365_interest_arrears"] = 0m,
                ["par_181_365_total_exposure"] = 2m,

                ["par_over_365_outstanding_principal"] = 3m,
                ["par_over_365_interest_arrears"] = 0m,
                ["par_over_365_total_exposure"] = 3m,

                ["total_portfolio"] = 170m,
                ["total_par"] = 70m,
                ["par_ratio"] = 41.18m
            });

        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task MFB_Unit_Capital_50M_Minimum_Enforced()
    {
        var errors = await EvaluateMfbCapitalThreshold("MFB_Unit_Capital_50M_Minimum_Enforced", 50000000m, 49000000m);
        errors.Should().Contain(e => e.Field == "paid_up_capital");
    }

    [Fact]
    public async Task MFB_State_Capital_200M_Minimum_Enforced()
    {
        var errors = await EvaluateMfbCapitalThreshold("MFB_State_Capital_200M_Minimum_Enforced", 200000000m, 199000000m);
        errors.Should().Contain(e => e.Field == "paid_up_capital");
    }

    [Fact]
    public async Task MFB_National_Capital_5B_Minimum_Enforced()
    {
        var errors = await EvaluateMfbCapitalThreshold("MFB_National_Capital_5B_Minimum_Enforced", 5000000000m, 4990000000m);
        errors.Should().Contain(e => e.Field == "paid_up_capital");
    }

    [Fact]
    public async Task MFB_CAR_Formula_Calculates_Correctly()
    {
        await using var db = CreateDbContext(nameof(MFB_CAR_Formula_Calculates_Correctly));
        await SeedModules(db, "MFB_PAR", "NDIC_RETURNS", "NFIU_AML");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);
        (await sut.ImportModule(await LoadDefinition("rg08-mfb-par-module-definition.json"), "rg08-test")).Success.Should().BeTrue();

        var errors = await EvaluateImportedTemplateFormulas(
            db,
            "MFB_CAP",
            new Dictionary<string, object?>
            {
                ["paid_up_capital"] = 60000000m,
                ["share_premium"] = 10000000m,
                ["retained_earnings"] = 5000000m,
                ["statutory_reserves"] = 5000000m,
                ["total_qualifying_capital"] = 80000000m,
                ["tier1_capital"] = 70000000m,
                ["tier2_capital"] = 10000000m,
                ["total_risk_weighted_assets"] = 200000000m,
                ["car_ratio"] = 40m,
                ["minimum_capital_requirement"] = 50000000m
            });

        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task MFB_Capital_Flows_To_NDIC()
    {
        await using var db = CreateDbContext(nameof(MFB_Capital_Flows_To_NDIC));
        var module = await SeedModules(db, "MFB_PAR", "NDIC_RETURNS", "NFIU_AML");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);
        (await sut.ImportModule(await LoadDefinition("rg08-mfb-par-module-definition.json"), "rg08-test")).Success.Should().BeTrue();

        var flow = await db.InterModuleDataFlows
            .SingleOrDefaultAsync(f => f.SourceModuleId == module.Id
                                       && f.SourceTemplateCode == "MFB_CAP"
                                       && f.SourceFieldCode == "total_qualifying_capital"
                                       && f.TargetModuleCode == "NDIC_RETURNS"
                                       && f.TargetTemplateCode == "NDIC_FIN"
                                       && f.TargetFieldCode == "capital");

        flow.Should().NotBeNull();
        flow!.TransformationType.Should().Be("DirectCopy");
    }

    [Fact]
    public async Task MFB_Deposits_Flow_To_NDIC()
    {
        await using var db = CreateDbContext(nameof(MFB_Deposits_Flow_To_NDIC));
        var module = await SeedModules(db, "MFB_PAR", "NDIC_RETURNS", "NFIU_AML");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);
        (await sut.ImportModule(await LoadDefinition("rg08-mfb-par-module-definition.json"), "rg08-test")).Success.Should().BeTrue();

        var flow = await db.InterModuleDataFlows
            .SingleOrDefaultAsync(f => f.SourceModuleId == module.Id
                                       && f.SourceTemplateCode == "MFB_DEP"
                                       && f.SourceFieldCode == "total_deposits"
                                       && f.TargetModuleCode == "NDIC_RETURNS"
                                       && f.TargetTemplateCode == "NDIC_DEP"
                                       && f.TargetFieldCode == "total_deposits");

        flow.Should().NotBeNull();
        flow!.TransformationType.Should().Be("DirectCopy");
    }

    [Fact]
    public async Task NFIU_Module_Imports_12_Templates()
    {
        await using var db = CreateDbContext(nameof(NFIU_Module_Imports_12_Templates));
        var module = await SeedModules(db, "NFIU_AML");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);

        var definition = await LoadDefinition("rg08-nfiu-aml-module-definition.json");
        var validation = await sut.ValidateDefinition(definition);
        validation.IsValid.Should().BeTrue(string.Join(" | ", validation.Errors));
        validation.TemplateCount.Should().Be(12);

        var import = await sut.ImportModule(definition, "rg08-test");
        import.Success.Should().BeTrue(string.Join(" | ", import.Errors));
        import.TemplatesCreated.Should().Be(12);

        (await db.ReturnTemplates.CountAsync(t => t.ModuleId == module.Id)).Should().Be(12);
    }

    [Fact]
    public async Task NFIU_Receives_STR_Data_From_BDC()
    {
        await using var db = CreateDbContext(nameof(NFIU_Receives_STR_Data_From_BDC));
        await SeedModules(db, "BDC_CBN", "NFIU_AML");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);
        (await sut.ImportModule(await LoadDefinition("rg08-bdc-cbn-module-definition.json"), "rg08-test")).Success.Should().BeTrue();

        var tenantId = Guid.NewGuid();
        var submission = Submission.Create(77, 202601, "BDC_AML", tenantId);
        db.Submissions.Add(submission);
        await db.SaveChangesAsync();

        var entitlement = new Mock<IEntitlementService>();
        entitlement.Setup(e => e.HasModuleAccess(tenantId, "NFIU_AML", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var genericRepo = new Mock<IGenericDataRepository>();
        genericRepo.Setup(r => r.ReadFieldValue("BDC_AML", submission.Id, "str_filed_count", It.IsAny<CancellationToken>()))
            .ReturnsAsync(11m);
        genericRepo.Setup(r => r.ReadFieldValue("BDC_AML", submission.Id, It.Is<string>(f => f != "str_filed_count"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        var engine = new InterModuleDataFlowEngine(
            db,
            entitlement.Object,
            genericRepo.Object,
            NullLogger<InterModuleDataFlowEngine>.Instance);

        await engine.ProcessDataFlows(tenantId, submission.Id, "BDC_CBN", "BDC_AML", 77, 202601, CancellationToken.None);

        genericRepo.Verify(r => r.WriteFieldValue(
                "NFIU_STR",
                It.IsAny<int>(),
                "str_filed_count",
                11m,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task NFIU_Receives_CTR_Data_From_MFB()
    {
        await using var db = CreateDbContext(nameof(NFIU_Receives_CTR_Data_From_MFB));
        await SeedModules(db, "MFB_PAR", "NFIU_AML", "NDIC_RETURNS");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);
        (await sut.ImportModule(await LoadDefinition("rg08-mfb-par-module-definition.json"), "rg08-test")).Success.Should().BeTrue();

        var tenantId = Guid.NewGuid();
        var submission = Submission.Create(77, 202601, "MFB_AML", tenantId);
        db.Submissions.Add(submission);
        await db.SaveChangesAsync();

        var entitlement = new Mock<IEntitlementService>();
        entitlement.Setup(e => e.HasModuleAccess(tenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var genericRepo = new Mock<IGenericDataRepository>();
        genericRepo.Setup(r => r.ReadFieldValue("MFB_AML", submission.Id, "ctr_filed_count", It.IsAny<CancellationToken>()))
            .ReturnsAsync(15m);
        genericRepo.Setup(r => r.ReadFieldValue("MFB_AML", submission.Id, It.Is<string>(f => f != "ctr_filed_count"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        var engine = new InterModuleDataFlowEngine(
            db,
            entitlement.Object,
            genericRepo.Object,
            NullLogger<InterModuleDataFlowEngine>.Instance);

        await engine.ProcessDataFlows(tenantId, submission.Id, "MFB_PAR", "MFB_AML", 77, 202601, CancellationToken.None);

        genericRepo.Verify(r => r.WriteFieldValue(
                "NFIU_CTR",
                It.IsAny<int>(),
                "ctr_filed_count",
                15m,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task NFIU_Aggregates_STR_From_Multiple_Sources()
    {
        await using var db = CreateDbContext(nameof(NFIU_Aggregates_STR_From_Multiple_Sources));
        await SeedModules(db, "BDC_CBN", "MFB_PAR", "NFIU_AML", "NDIC_RETURNS");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);
        (await sut.ImportModule(await LoadDefinition("rg08-bdc-cbn-module-definition.json"), "rg08-test")).Success.Should().BeTrue();
        (await sut.ImportModule(await LoadDefinition("rg08-mfb-par-module-definition.json"), "rg08-test")).Success.Should().BeTrue();

        var tenantId = Guid.NewGuid();
        var bdcSubmission = Submission.Create(77, 202601, "BDC_AML", tenantId);
        var mfbSubmission = Submission.Create(77, 202601, "MFB_AML", tenantId);
        db.Submissions.AddRange(bdcSubmission, mfbSubmission);
        await db.SaveChangesAsync();

        var entitlement = new Mock<IEntitlementService>();
        entitlement.Setup(e => e.HasModuleAccess(tenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var genericRepo = new Mock<IGenericDataRepository>();
        genericRepo.Setup(r => r.ReadFieldValue("BDC_AML", bdcSubmission.Id, "str_filed_count", It.IsAny<CancellationToken>()))
            .ReturnsAsync(8m);
        genericRepo.Setup(r => r.ReadFieldValue("MFB_AML", mfbSubmission.Id, "str_filed_count", It.IsAny<CancellationToken>()))
            .ReturnsAsync(4m);

        genericRepo.Setup(r => r.ReadFieldValue("BDC_AML", bdcSubmission.Id, It.Is<string>(f => f != "str_filed_count"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        genericRepo.Setup(r => r.ReadFieldValue("MFB_AML", mfbSubmission.Id, It.Is<string>(f => f != "str_filed_count"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        var engine = new InterModuleDataFlowEngine(
            db,
            entitlement.Object,
            genericRepo.Object,
            NullLogger<InterModuleDataFlowEngine>.Instance);

        await engine.ProcessDataFlows(tenantId, bdcSubmission.Id, "BDC_CBN", "BDC_AML", 77, 202601, CancellationToken.None);

        genericRepo.Verify(r => r.WriteFieldValue(
                "NFIU_STR",
                It.IsAny<int>(),
                "str_filed_count",
                12m,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task NFIU_CTR_Threshold_5M_NGN_Validates()
    {
        await using var db = CreateDbContext(nameof(NFIU_CTR_Threshold_5M_NGN_Validates));
        await SeedModules(db, "NFIU_AML");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);
        (await sut.ImportModule(await LoadDefinition("rg08-nfiu-aml-module-definition.json"), "rg08-test")).Success.Should().BeTrue();

        var ctrField = await db.TemplateFields
            .Join(db.TemplateVersions,
                f => f.TemplateVersionId,
                v => v.Id,
                (f, v) => new { Field = f, Version = v })
            .Join(db.ReturnTemplates,
                fv => fv.Version.TemplateId,
                t => t.Id,
                (fv, t) => new { fv.Field, t.ReturnCode })
            .Where(x => x.ReturnCode == "NFIU_CTR" && x.Field.FieldName == "ctr_threshold_ngn")
            .Select(x => x.Field)
            .SingleAsync();

        ctrField.MinValue.Should().Be("5000000");
    }

    [Fact]
    public async Task BDC_Full_Return_Lifecycle()
    {
        await AssertModuleLifecycle("BDC_Full_Return_Lifecycle", "BDC_CBN", "BDC_COV", "rg08-bdc-cbn-module-definition.json");
    }

    [Fact]
    public async Task MFB_Full_Return_Lifecycle()
    {
        await AssertModuleLifecycle("MFB_Full_Return_Lifecycle", "MFB_PAR", "MFB_COV", "rg08-mfb-par-module-definition.json");
    }

    [Fact]
    public async Task NFIU_Full_Return_Lifecycle()
    {
        await AssertModuleLifecycle("NFIU_Full_Return_Lifecycle", "NFIU_AML", "NFIU_COV", "rg08-nfiu-aml-module-definition.json");
    }

    private static async Task<IReadOnlyList<ValidationError>> EvaluateMfbCapitalThreshold(
        string dbName,
        decimal minimumCapital,
        decimal paidUpCapital)
    {
        await using var db = CreateDbContext(dbName);
        await SeedModules(db, "MFB_PAR", "NDIC_RETURNS", "NFIU_AML");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);
        (await sut.ImportModule(await LoadDefinition("rg08-mfb-par-module-definition.json"), "rg08-test")).Success.Should().BeTrue();

        return await EvaluateImportedTemplateFormulas(
            db,
            "MFB_CAP",
            new Dictionary<string, object?>
            {
                ["paid_up_capital"] = paidUpCapital,
                ["share_premium"] = 0m,
                ["retained_earnings"] = 0m,
                ["statutory_reserves"] = 0m,
                ["total_qualifying_capital"] = paidUpCapital,
                ["tier1_capital"] = paidUpCapital,
                ["tier2_capital"] = 0m,
                ["total_risk_weighted_assets"] = 100000000m,
                ["car_ratio"] = Math.Round((paidUpCapital / 100000000m) * 100m, 2),
                ["minimum_capital_requirement"] = minimumCapital
            });
    }

    private static async Task AssertModuleLifecycle(
        string dbName,
        string moduleCode,
        string returnCode,
        string definitionFile)
    {
        await using var db = CreateDbContext(dbName);
        await SeedModules(db, "BDC_CBN", "MFB_PAR", "NFIU_AML", "NDIC_RETURNS");

        var cache = new Mock<ITemplateMetadataCache>();
        var sut = CreateSut(db, cache, out _);

        (await sut.ImportModule(await LoadDefinition(definitionFile), "rg08-test")).Success.Should().BeTrue();
        var publish = await sut.PublishModule(moduleCode, "rg08-approver");
        publish.Success.Should().BeTrue(string.Join(" | ", publish.Errors));

        var template = await db.ReturnTemplates.SingleAsync(t => t.ReturnCode == returnCode);
        var version = await db.TemplateVersions
            .OrderByDescending(v => v.VersionNumber)
            .FirstAsync(v => v.TemplateId == template.Id);

        version.Status.Should().Be(TemplateStatus.Published);

        var submission = Submission.Create(77, 202601, returnCode, Guid.NewGuid());
        submission.SetTemplateVersion(version.Id);
        submission.MarkParsing();
        submission.MarkValidating();
        submission.MarkPendingApproval();
        submission.MarkAccepted();

        submission.Status.Should().Be(SubmissionStatus.Accepted);
        submission.TemplateVersionId.Should().Be(version.Id);
    }

    private static async Task<IReadOnlyList<ValidationError>> EvaluateImportedTemplateFormulas(
        MetadataDbContext db,
        string returnCode,
        IDictionary<string, object?> fieldValues)
    {
        var templateId = await db.ReturnTemplates
            .Where(t => t.ReturnCode == returnCode)
            .Select(t => t.Id)
            .SingleAsync();

        var versionId = await db.TemplateVersions
            .Where(v => v.TemplateId == templateId)
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
                TemplateId = templateId,
                ReturnCode = returnCode,
                StructuralCategory = StructuralCategory.FixedRow.ToString(),
                CurrentVersion = new CachedTemplateVersion
                {
                    Id = versionId,
                    VersionNumber = 1,
                    IntraSheetFormulas = formulas
                }
            });

        var evaluator = new FormulaEvaluator(cache.Object);
        var record = new ReturnDataRecord(returnCode, 1, StructuralCategory.FixedRow);
        var row = new ReturnDataRow();
        foreach (var kvp in fieldValues)
        {
            row.SetValue(kvp.Key, kvp.Value);
        }

        record.AddRow(row);
        return await evaluator.Evaluate(record, CancellationToken.None);
    }

    private static ReturnDataRecord CreateRecord(string returnCode, params (string field, decimal value)[] values)
    {
        var record = new ReturnDataRecord(returnCode, 1, StructuralCategory.FixedRow);
        var row = new ReturnDataRow();
        foreach (var (field, value) in values)
        {
            row.SetValue(field, value);
        }

        record.AddRow(row);
        return record;
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
                RegulatorCode = code.StartsWith("NFIU", StringComparison.OrdinalIgnoreCase) ? "NFIU" : "CBN",
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
            .Returns(new DdlScript("CREATE TABLE dbo.[tmp_rg08](id INT, TenantId UNIQUEIDENTIFIER NULL);", "DROP TABLE dbo.[tmp_rg08];"));
        ddlEngine.Setup(d => d.GenerateAlterTable(It.IsAny<FC.Engine.Domain.Metadata.ReturnTemplate>(), It.IsAny<FC.Engine.Domain.Metadata.TemplateVersion>(), It.IsAny<FC.Engine.Domain.Metadata.TemplateVersion>()))
            .Returns(new DdlScript("ALTER TABLE dbo.[tmp_rg08] ADD test_col INT NULL;", "ALTER TABLE dbo.[tmp_rg08] DROP COLUMN test_col;"));

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

        File.Exists(path).Should().BeTrue($"Expected RG-08 definition file at {path}");
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
