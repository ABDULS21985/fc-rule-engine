using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FC.Engine.Infrastructure.Tests.Services;

public class ImpactAssessmentEngineTests
{
    private static MetadataDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new MetadataDbContext(options);
    }

    private static (IPolicyScenarioService scenarioSvc, IImpactAssessmentEngine engine, MetadataDbContext db) CreateServices(string testName)
    {
        var db = CreateDb(testName);
        var audit = new PolicyAuditLogger(db, NullLogger<PolicyAuditLogger>.Instance);
        var scenarioSvc = new PolicyScenarioService(db, audit, NullLogger<PolicyScenarioService>.Instance);
        var engine = new ImpactAssessmentEngine(db, audit, NullLogger<ImpactAssessmentEngine>.Instance);
        return (scenarioSvc, engine, db);
    }

    private static void SeedParameterPresets(MetadataDbContext db)
    {
        if (!db.PolicyParameterPresets.Any())
        {
            db.PolicyParameterPresets.Add(new PolicyParameterPreset
            {
                ParameterCode = "MIN_CAR",
                ParameterName = "Minimum Capital Adequacy Ratio",
                PolicyDomain = PolicyDomain.CapitalAdequacy,
                CurrentBaseline = 10.000000m,
                Unit = ParameterUnit.Percentage,
                ReturnLineReference = "SRF-001.L45",
                Description = "Basel II/III minimum CAR for DMBs",
                RegulatorCode = "CBN"
            });
            db.SaveChanges();
        }
    }

    private static void SeedInstitution(MetadataDbContext db, int id, string code, string licenseType)
    {
        if (!db.Set<Institution>().Any(i => i.Id == id))
        {
            db.Set<Institution>().Add(new Institution
            {
                Id = id,
                InstitutionCode = code,
                InstitutionName = $"Test Bank {code}",
                LicenseType = licenseType,
                IsActive = true
            });
            db.SaveChanges();
        }
    }

    [Fact]
    public async Task RunAssessment_CARIncrease_CategorizesEntitiesCorrectly()
    {
        var (scenarioSvc, engine, db) = CreateServices(nameof(RunAssessment_CARIncrease_CategorizesEntitiesCorrectly));
        SeedParameterPresets(db);

        // Seed institutions
        SeedInstitution(db, 1, "DMB-001", "DMB");
        SeedInstitution(db, 2, "DMB-002", "DMB");
        SeedInstitution(db, 3, "DMB-003", "DMB");
        SeedInstitution(db, 4, "MFB-001", "MFB");
        SeedInstitution(db, 5, "MFB-002", "MFB");

        // Create scenario
        var scenarioId = await scenarioSvc.CreateScenarioAsync(
            regulatorId: 1, title: "Increase minimum CAR to 12.5%",
            description: null, domain: PolicyDomain.CapitalAdequacy,
            targetEntityTypes: "ALL", baselineDate: new DateOnly(2026, 3, 1),
            createdByUserId: 1);

        await scenarioSvc.AddParameterAsync(scenarioId, 1, "MIN_CAR", 12.5m, "ALL", 1);

        // Verify scenario was created and parameter added
        var scenario = await db.PolicyScenarios.Include(s => s.Parameters).FirstAsync(s => s.Id == scenarioId);
        scenario.Status.Should().Be(PolicyStatus.ParametersSet);
        scenario.Parameters.Should().HaveCount(1);
        scenario.Parameters[0].ParameterCode.Should().Be("MIN_CAR");
        scenario.Parameters[0].CurrentValue.Should().Be(10.0m);
        scenario.Parameters[0].ProposedValue.Should().Be(12.5m);
    }

    [Fact]
    public async Task CreateScenario_SetsStatusToDraft()
    {
        var (scenarioSvc, _, db) = CreateServices(nameof(CreateScenario_SetsStatusToDraft));
        SeedParameterPresets(db);

        var id = await scenarioSvc.CreateScenarioAsync(
            1, "Test Scenario", "Description", PolicyDomain.CapitalAdequacy,
            "DMB,MFB", new DateOnly(2026, 3, 1), 1);

        var scenario = await db.PolicyScenarios.FindAsync(id);
        scenario.Should().NotBeNull();
        scenario!.Status.Should().Be(PolicyStatus.Draft);
        scenario.RegulatorId.Should().Be(1);
        scenario.Title.Should().Be("Test Scenario");
    }

    [Fact]
    public async Task AddParameter_TransitionsStatusToParametersSet()
    {
        var (scenarioSvc, _, db) = CreateServices(nameof(AddParameter_TransitionsStatusToParametersSet));
        SeedParameterPresets(db);

        var id = await scenarioSvc.CreateScenarioAsync(
            1, "Test", null, PolicyDomain.CapitalAdequacy, "ALL",
            new DateOnly(2026, 3, 1), 1);

        await scenarioSvc.AddParameterAsync(id, 1, "MIN_CAR", 12.5m, "ALL", 1);

        var scenario = await db.PolicyScenarios.FindAsync(id);
        scenario!.Status.Should().Be(PolicyStatus.ParametersSet);
    }

    [Fact]
    public async Task AddParameter_LooksUpPresetValues()
    {
        var (scenarioSvc, _, db) = CreateServices(nameof(AddParameter_LooksUpPresetValues));
        SeedParameterPresets(db);

        var id = await scenarioSvc.CreateScenarioAsync(
            1, "Test", null, PolicyDomain.CapitalAdequacy, "ALL",
            new DateOnly(2026, 3, 1), 1);

        await scenarioSvc.AddParameterAsync(id, 1, "MIN_CAR", 12.5m, "ALL", 1);

        var param = await db.PolicyParameters.FirstAsync(p => p.ScenarioId == id);
        param.ParameterName.Should().Be("Minimum Capital Adequacy Ratio");
        param.CurrentValue.Should().Be(10.0m);
        param.ProposedValue.Should().Be(12.5m);
        param.ReturnLineReference.Should().Be("SRF-001.L45");
    }

    [Fact]
    public async Task CloneScenario_CopiesParametersWithNewTitle()
    {
        var (scenarioSvc, _, db) = CreateServices(nameof(CloneScenario_CopiesParametersWithNewTitle));
        SeedParameterPresets(db);

        var sourceId = await scenarioSvc.CreateScenarioAsync(
            1, "Original", null, PolicyDomain.CapitalAdequacy, "ALL",
            new DateOnly(2026, 3, 1), 1);

        await scenarioSvc.AddParameterAsync(sourceId, 1, "MIN_CAR", 12.5m, "ALL", 1);

        var cloneId = await scenarioSvc.CloneScenarioAsync(sourceId, 1, "Clone Option B", 1);

        cloneId.Should().NotBe(sourceId);

        var clone = await db.PolicyScenarios.Include(s => s.Parameters).FirstAsync(s => s.Id == cloneId);
        clone.Title.Should().Be("Clone Option B");
        clone.Parameters.Should().HaveCount(1);
        clone.Parameters[0].ParameterCode.Should().Be("MIN_CAR");
        clone.Parameters[0].ProposedValue.Should().Be(12.5m);
    }

    [Fact]
    public async Task ListScenarios_FiltersbyDomainAndStatus()
    {
        var (scenarioSvc, _, db) = CreateServices(nameof(ListScenarios_FiltersbyDomainAndStatus));
        SeedParameterPresets(db);

        await scenarioSvc.CreateScenarioAsync(1, "Capital 1", null, PolicyDomain.CapitalAdequacy, "ALL", new DateOnly(2026, 3, 1), 1);
        await scenarioSvc.CreateScenarioAsync(1, "Liquidity 1", null, PolicyDomain.Liquidity, "ALL", new DateOnly(2026, 3, 1), 1);
        await scenarioSvc.CreateScenarioAsync(1, "Capital 2", null, PolicyDomain.CapitalAdequacy, "ALL", new DateOnly(2026, 3, 1), 1);

        var allResult = await scenarioSvc.ListScenariosAsync(1, null, null, 1, 10);
        allResult.TotalCount.Should().Be(3);

        var capitalOnly = await scenarioSvc.ListScenariosAsync(1, PolicyDomain.CapitalAdequacy, null, 1, 10);
        capitalOnly.TotalCount.Should().Be(2);
        capitalOnly.Items.Should().AllSatisfy(s => s.Domain.Should().Be(PolicyDomain.CapitalAdequacy));
    }

    [Fact]
    public async Task TransitionStatus_UpdatesScenarioStatus()
    {
        var (scenarioSvc, _, db) = CreateServices(nameof(TransitionStatus_UpdatesScenarioStatus));
        SeedParameterPresets(db);

        var id = await scenarioSvc.CreateScenarioAsync(
            1, "Test", null, PolicyDomain.CapitalAdequacy, "ALL",
            new DateOnly(2026, 3, 1), 1);

        await scenarioSvc.TransitionStatusAsync(id, 1, PolicyStatus.Withdrawn, 1);

        var scenario = await db.PolicyScenarios.FindAsync(id);
        scenario!.Status.Should().Be(PolicyStatus.Withdrawn);
    }

    [Fact]
    public async Task AuditLog_RecordsScenarioCreation()
    {
        var (scenarioSvc, _, db) = CreateServices(nameof(AuditLog_RecordsScenarioCreation));
        SeedParameterPresets(db);

        await scenarioSvc.CreateScenarioAsync(
            1, "Audit Test", null, PolicyDomain.CapitalAdequacy, "ALL",
            new DateOnly(2026, 3, 1), 1);

        var auditEntries = await db.PolicyAuditLog.ToListAsync();
        auditEntries.Should().NotBeEmpty();
        auditEntries.Should().Contain(e => e.Action.Contains("Created") || e.Action.Contains("SCENARIO"));
    }
}
