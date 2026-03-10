using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace FC.Engine.Infrastructure.Tests.Services;

public class PlatformRegulatorTenantResolverTests
{
    [Fact]
    public async Task TryResolveAsync_Bootstraps_Default_Cbn_Tenant_When_None_Exist()
    {
        await using var db = CreateDb();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var sut = new PlatformRegulatorTenantResolver(db, configuration, NullLogger<PlatformRegulatorTenantResolver>.Instance);

        var result = await sut.TryResolveAsync();

        result.Should().NotBeNull();
        result!.RegulatorCode.Should().Be("CBN");

        var tenant = await db.Tenants.SingleAsync();
        tenant.TenantType.Should().Be(TenantType.Regulator);
        tenant.Status.Should().Be(TenantStatus.Active);
        tenant.TenantSlug.Should().Be("cbn");
    }

    [Fact]
    public async Task TryResolveAsync_Prefers_Configured_Regulator_When_Multiple_Exist()
    {
        await using var db = CreateDb();
        db.Tenants.Add(CreateTenant("cbn", "Central Bank of Nigeria"));
        db.Tenants.Add(CreateTenant("ndic", "Nigeria Deposit Insurance Corporation"));
        await db.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RegulatorPortal:DefaultRegulatorCode"] = "NDIC"
            })
            .Build();
        var sut = new PlatformRegulatorTenantResolver(db, configuration, NullLogger<PlatformRegulatorTenantResolver>.Instance);

        var result = await sut.TryResolveAsync();

        result.Should().NotBeNull();
        result!.RegulatorCode.Should().Be("NDIC");
        result.TenantName.Should().Contain("Insurance");
    }

    private static MetadataDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MetadataDbContext(options);
    }

    private static Tenant CreateTenant(string slug, string name)
    {
        var tenant = Tenant.Create(name, slug, TenantType.Regulator, $"{slug}@regos.app");
        tenant.Activate();
        return tenant;
    }
}
