using System.Security.Claims;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Auth;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Tests.Auth;

public class TenantClaimsTransformationTests
{
    [Fact]
    public async Task TryResolveAsync_UsesTenantMetadata_ForRegulatorContext()
    {
        await using var db = CreateDb();
        var tenant = CreateTenant("cbn", TenantType.Regulator);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var sut = new TenantAccessContextResolver(db);

        var result = await sut.TryResolveAsync(tenant.TenantId);

        result.Should().NotBeNull();
        result!.TenantType.Should().Be(TenantType.Regulator);
        result.RegulatorCode.Should().Be("CBN");
        result.RegulatorId.Should().BePositive();
    }

    [Fact]
    public async Task TransformAsync_AddsMissingRegulatorClaims_FromTenantMetadata()
    {
        await using var db = CreateDb();
        var tenant = CreateTenant("cbn", TenantType.Regulator);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("TenantId", tenant.TenantId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, "42")
        ], "cookie"));

        var sut = new TenantClaimsTransformation(new TenantAccessContextResolver(db));

        var transformed = await sut.TransformAsync(principal);

        transformed.FindFirst("TenantType")?.Value.Should().Be(TenantType.Regulator.ToString());
        transformed.FindFirst("RegulatorCode")?.Value.Should().Be("CBN");
        transformed.FindFirst("RegulatorId")?.Value.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TransformAsync_RemovesStaleRegulatorClaims_ForNonRegulatorTenant()
    {
        await using var db = CreateDb();
        var tenant = CreateTenant("acme-bank", TenantType.Institution);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("TenantId", tenant.TenantId.ToString()),
            new Claim("RegulatorCode", "CBN"),
            new Claim("RegulatorId", "99")
        ], "cookie"));

        var sut = new TenantClaimsTransformation(new TenantAccessContextResolver(db));

        var transformed = await sut.TransformAsync(principal);

        transformed.FindFirst("TenantType")?.Value.Should().Be(TenantType.Institution.ToString());
        transformed.FindAll("RegulatorCode").Should().BeEmpty();
        transformed.FindAll("RegulatorId").Should().BeEmpty();
    }

    private static MetadataDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MetadataDbContext(options);
    }

    private static Tenant CreateTenant(string slug, TenantType tenantType)
    {
        var tenant = Tenant.Create(slug.ToUpperInvariant(), slug, tenantType, $"{slug}@regos.app");
        tenant.Activate();
        return tenant;
    }
}
