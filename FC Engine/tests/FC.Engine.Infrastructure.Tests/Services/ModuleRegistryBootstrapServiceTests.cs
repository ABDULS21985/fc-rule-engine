using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FC.Engine.Infrastructure.Tests.Services;

public class ModuleRegistryBootstrapServiceTests
{
    [Fact]
    public async Task EnsureBaselineModulesAsync_Creates_Module_Registry_And_Licence_Mappings()
    {
        await using var db = CreateDbContext(nameof(EnsureBaselineModulesAsync_Creates_Module_Registry_And_Licence_Mappings));
        await SeedLicenceTypesAsync(db, "FC", "BDC", "MFB");

        var sut = new ModuleRegistryBootstrapService(db, NullLogger<ModuleRegistryBootstrapService>.Instance);

        var result = await sut.EnsureBaselineModulesAsync();

        result.ModulesCreated.Should().Be(2);
        result.ModulesUpdated.Should().Be(0);
        result.MappingsCreated.Should().Be(6);
        result.MappingsUpdated.Should().Be(0);

        var modules = await db.Modules
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync();

        modules.Should().ContainSingle(x => x.ModuleCode == "OPS_RESILIENCE" && x.SheetCount == 10 && x.DefaultFrequency == "Quarterly");
        modules.Should().ContainSingle(x => x.ModuleCode == "MODEL_RISK" && x.SheetCount == 9 && x.DefaultFrequency == "Quarterly");

        var mappings = await db.LicenceModuleMatrix.ToListAsync();
        mappings.Should().HaveCount(6);
        mappings.Should().OnlyContain(x => x.IsOptional && !x.IsRequired);
    }

    [Fact]
    public async Task EnsureBaselineModulesAsync_Is_Idempotent_And_Repairs_Module_Metadata()
    {
        await using var db = CreateDbContext(nameof(EnsureBaselineModulesAsync_Is_Idempotent_And_Repairs_Module_Metadata));
        await SeedLicenceTypesAsync(db, "FC", "BDC");

        db.Modules.Add(new Module
        {
            ModuleCode = "OPS_RESILIENCE",
            ModuleName = "Old Name",
            RegulatorCode = "OLD",
            Description = "old",
            SheetCount = 1,
            DefaultFrequency = "Monthly",
            DisplayOrder = 1,
            DeadlineOffsetDays = 10,
            IsActive = false,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        });
        await db.SaveChangesAsync();

        var sut = new ModuleRegistryBootstrapService(db, NullLogger<ModuleRegistryBootstrapService>.Instance);

        var first = await sut.EnsureBaselineModulesAsync();
        var second = await sut.EnsureBaselineModulesAsync();

        first.ModulesCreated.Should().Be(1);
        first.ModulesUpdated.Should().Be(1);
        first.MappingsCreated.Should().Be(4);

        second.ModulesCreated.Should().Be(0);
        second.ModulesUpdated.Should().Be(0);
        second.MappingsCreated.Should().Be(0);
        second.MappingsUpdated.Should().Be(0);

        (await db.Modules.CountAsync(x => x.ModuleCode == "OPS_RESILIENCE")).Should().Be(1);
        (await db.Modules.CountAsync(x => x.ModuleCode == "MODEL_RISK")).Should().Be(1);

        var ops = await db.Modules.SingleAsync(x => x.ModuleCode == "OPS_RESILIENCE");
        ops.ModuleName.Should().Be("Operational Resilience & ICT Risk");
        ops.RegulatorCode.Should().Be("CBN");
        ops.IsActive.Should().BeTrue();
        ops.SheetCount.Should().Be(10);

        (await db.LicenceModuleMatrix.CountAsync()).Should().Be(4);
    }

    private static async Task SeedLicenceTypesAsync(MetadataDbContext db, params string[] codes)
    {
        var displayOrder = 1;
        foreach (var code in codes)
        {
            db.LicenceTypes.Add(new LicenceType
            {
                Code = code,
                Name = $"{code} Licence",
                Regulator = "CBN",
                IsActive = true,
                DisplayOrder = displayOrder++,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private static MetadataDbContext CreateDbContext(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new MetadataDbContext(options);
    }
}
