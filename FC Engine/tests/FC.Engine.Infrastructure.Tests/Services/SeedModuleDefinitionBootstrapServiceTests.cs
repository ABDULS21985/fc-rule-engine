using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FC.Engine.Infrastructure.Tests.Services;

public class SeedModuleDefinitionBootstrapServiceTests
{
    [Fact]
    public async Task EnsureSeedModulesInstalledAsync_Imports_And_Publishes_Missing_Definitions()
    {
        await using var db = CreateDbContext(nameof(EnsureSeedModulesInstalledAsync_Imports_And_Publishes_Missing_Definitions));
        await SeedLicenceTypesAsync(db, "FC", "BDC");

        var registryBootstrap = new ModuleRegistryBootstrapService(db, NullLogger<ModuleRegistryBootstrapService>.Instance);
        var moduleImportService = new Mock<IModuleImportService>();
        moduleImportService
            .Setup(x => x.ImportModule(It.IsAny<string>(), "platform-bootstrap", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string json, string _, CancellationToken _) =>
            {
                var code = json.Contains("\"moduleCode\": \"MODEL_RISK\"", StringComparison.OrdinalIgnoreCase)
                    ? "MODEL_RISK"
                    : "OPS_RESILIENCE";

                return new ModuleImportResult
                {
                    Success = true,
                    ModuleCode = code
                };
            });
        moduleImportService
            .Setup(x => x.PublishModule(It.IsAny<string>(), "platform-bootstrap", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string moduleCode, string _, CancellationToken _) =>
            {
                var moduleId = db.Modules.Single(x => x.ModuleCode == moduleCode).Id;
                var returnCodes = moduleCode == "OPS_RESILIENCE"
                    ? new[] { "OPS_IBS", "OPS_TOL", "OPS_SCN", "OPS_TPR", "OPS_INC", "OPS_BCP", "OPS_CYB", "OPS_CHG", "OPS_RTO", "OPS_BRD" }
                    : new[] { "MRM_INV", "MRM_VAL", "MRM_PRF", "MRM_BKT", "MRM_MON", "MRM_CHG", "MRM_APR", "MRM_RAP", "MRM_RPT" };

                foreach (var returnCode in returnCodes)
                {
                    if (db.ReturnTemplates.Any(x => x.ReturnCode == returnCode))
                    {
                        continue;
                    }

                    var template = new FC.Engine.Domain.Metadata.ReturnTemplate
                    {
                        ModuleId = moduleId,
                        ReturnCode = returnCode,
                        Name = returnCode,
                        Frequency = ReturnFrequency.Quarterly,
                        StructuralCategory = StructuralCategory.FixedRow,
                        PhysicalTableName = returnCode.ToLowerInvariant(),
                        XmlRootElement = returnCode,
                        XmlNamespace = "urn:regos:test",
                        IsSystemTemplate = true,
                        InstitutionType = "ALL",
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = "test",
                        UpdatedAt = DateTime.UtcNow,
                        UpdatedBy = "test"
                    };
                    db.ReturnTemplates.Add(template);
                    db.SaveChanges();

                    db.TemplateVersions.Add(new FC.Engine.Domain.Metadata.TemplateVersion
                    {
                        TemplateId = template.Id,
                        VersionNumber = 1,
                        Status = TemplateStatus.Published,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = "test",
                        PublishedAt = DateTime.UtcNow,
                        ApprovedAt = DateTime.UtcNow,
                        ApprovedBy = "test"
                    });
                    db.SaveChanges();
                }

                return new ModulePublishResult
                {
                    Success = true,
                    ModuleCode = moduleCode,
                    VersionsPublished = 1
                };
            });

        var sut = new SeedModuleDefinitionBootstrapService(
            db,
            registryBootstrap,
            moduleImportService.Object,
            NullLogger<SeedModuleDefinitionBootstrapService>.Instance);

        var result = await sut.EnsureSeedModulesInstalledAsync();

        result.Errors.Should().BeEmpty();
        result.ModulesImported.Should().Be(2);
        result.ModulesPublished.Should().Be(2);

        moduleImportService.Verify(x => x.ImportModule(It.IsAny<string>(), "platform-bootstrap", It.IsAny<CancellationToken>()), Times.Exactly(2));
        moduleImportService.Verify(x => x.PublishModule("OPS_RESILIENCE", "platform-bootstrap", It.IsAny<CancellationToken>()), Times.Once);
        moduleImportService.Verify(x => x.PublishModule("MODEL_RISK", "platform-bootstrap", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureSeedModulesInstalledAsync_Skips_Already_Published_Modules()
    {
        await using var db = CreateDbContext(nameof(EnsureSeedModulesInstalledAsync_Skips_Already_Published_Modules));
        await SeedLicenceTypesAsync(db, "FC");

        var registryBootstrap = new ModuleRegistryBootstrapService(db, NullLogger<ModuleRegistryBootstrapService>.Instance);
        await registryBootstrap.EnsureBaselineModulesAsync();

        await SeedPublishedTemplatesAsync(
            db,
            "OPS_RESILIENCE",
            ["OPS_IBS", "OPS_TOL", "OPS_SCN", "OPS_TPR", "OPS_INC", "OPS_BCP", "OPS_CYB", "OPS_CHG", "OPS_RTO", "OPS_BRD"]);
        await SeedPublishedTemplatesAsync(
            db,
            "MODEL_RISK",
            ["MRM_INV", "MRM_VAL", "MRM_PRF", "MRM_BKT", "MRM_MON", "MRM_CHG", "MRM_APR", "MRM_RAP", "MRM_RPT"]);

        var moduleImportService = new Mock<IModuleImportService>(MockBehavior.Strict);
        var sut = new SeedModuleDefinitionBootstrapService(
            db,
            registryBootstrap,
            moduleImportService.Object,
            NullLogger<SeedModuleDefinitionBootstrapService>.Instance);

        var result = await sut.EnsureSeedModulesInstalledAsync();

        result.Errors.Should().BeEmpty();
        result.ModulesImported.Should().Be(0);
        result.ModulesPublished.Should().Be(0);
    }

    [Fact]
    public async Task LoadDefinitionPayloadAsync_Reads_Embedded_Json()
    {
        var json = await SeedModuleDefinitionBootstrapService.LoadDefinitionPayloadAsync(
            "FC.Engine.Infrastructure.SeedData.ModuleDefinitions.rg50-model-risk-module-definition.json");

        json.Should().Contain("\"moduleCode\": \"MODEL_RISK\"");
        json.Should().Contain("\"returnCode\": \"MRM_INV\"");
    }

    private static async Task SeedLicenceTypesAsync(MetadataDbContext db, params string[] codes)
    {
        var order = 1;
        foreach (var code in codes)
        {
            db.LicenceTypes.Add(new LicenceType
            {
                Code = code,
                Name = $"{code} Licence",
                Regulator = "CBN",
                IsActive = true,
                DisplayOrder = order++,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedPublishedTemplatesAsync(MetadataDbContext db, string moduleCode, IReadOnlyList<string> returnCodes)
    {
        var moduleId = await db.Modules
            .Where(x => x.ModuleCode == moduleCode)
            .Select(x => x.Id)
            .SingleAsync();

        foreach (var returnCode in returnCodes)
        {
            var template = new FC.Engine.Domain.Metadata.ReturnTemplate
            {
                ModuleId = moduleId,
                ReturnCode = returnCode,
                Name = returnCode,
                Frequency = ReturnFrequency.Quarterly,
                StructuralCategory = StructuralCategory.FixedRow,
                PhysicalTableName = returnCode.ToLowerInvariant(),
                XmlRootElement = returnCode,
                XmlNamespace = "urn:regos:test",
                IsSystemTemplate = true,
                InstitutionType = "ALL",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test",
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = "test"
            };
            db.ReturnTemplates.Add(template);
            await db.SaveChangesAsync();

            db.TemplateVersions.Add(new FC.Engine.Domain.Metadata.TemplateVersion
            {
                TemplateId = template.Id,
                VersionNumber = 1,
                Status = TemplateStatus.Published,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test",
                PublishedAt = DateTime.UtcNow,
                ApprovedAt = DateTime.UtcNow,
                ApprovedBy = "test"
            });
            await db.SaveChangesAsync();
        }
    }

    private static MetadataDbContext CreateDbContext(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new MetadataDbContext(options);
    }
}
