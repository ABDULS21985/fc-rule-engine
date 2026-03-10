using System.Security.Claims;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Auth;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FC.Engine.Infrastructure.Tests.Auth;

public class RegulatorTenantAccessHandlerTests
{
    [Fact]
    public async Task HandleAsync_Succeeds_ForAuthenticatedRegulatorTenant()
    {
        await using var db = CreateDb();
        var tenant = CreateTenant("cbn", TenantType.Regulator);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupGet(x => x.CurrentTenantId).Returns((Guid?)null);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("TenantId", tenant.TenantId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, "42")
        ], "FC.Admin.Auth"));

        var requirement = new RegulatorTenantAccessRequirement();
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        var platformResolver = new Mock<IPlatformRegulatorTenantResolver>();
        var sut = new RegulatorTenantAccessHandler(
            tenantContext.Object,
            new TenantAccessContextResolver(db),
            platformResolver.Object);

        await sut.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_Succeeds_ForPlatformAdminImpersonatingRegulatorTenant()
    {
        await using var db = CreateDb();
        var tenant = CreateTenant("ndic", TenantType.Regulator);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupGet(x => x.IsPlatformAdmin).Returns(true);
        tenantContext.SetupGet(x => x.CurrentTenantId).Returns(tenant.TenantId);
        tenantContext.SetupGet(x => x.ImpersonatingTenantId).Returns(tenant.TenantId);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "7"),
            new Claim(ClaimTypes.Role, "PlatformAdmin"),
            new Claim("IsPlatformAdmin", "true")
        ], "FC.Admin.Auth"));

        var requirement = new RegulatorTenantAccessRequirement();
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        var platformResolver = new Mock<IPlatformRegulatorTenantResolver>();
        var sut = new RegulatorTenantAccessHandler(
            tenantContext.Object,
            new TenantAccessContextResolver(db),
            platformResolver.Object);

        await sut.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_Succeeds_WhenPlatformAdminFallsBackToResolvedRegulatorTenant()
    {
        await using var db = CreateDb();
        var tenant = CreateTenant("cbn", TenantType.Regulator);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupGet(x => x.IsPlatformAdmin).Returns(true);
        tenantContext.SetupGet(x => x.CurrentTenantId).Returns((Guid?)null);
        tenantContext.SetupGet(x => x.ImpersonatingTenantId).Returns((Guid?)null);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "7"),
            new Claim(ClaimTypes.Role, "PlatformAdmin"),
            new Claim("IsPlatformAdmin", "true")
        ], "FC.Admin.Auth"));

        var requirement = new RegulatorTenantAccessRequirement();
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        var platformResolver = new Mock<IPlatformRegulatorTenantResolver>();
        platformResolver
            .Setup(x => x.TryResolveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformRegulatorTenantContext(tenant.TenantId, tenant.TenantName, "CBN"));
        var sut = new RegulatorTenantAccessHandler(
            tenantContext.Object,
            new TenantAccessContextResolver(db),
            platformResolver.Object);

        await sut.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_Fails_WhenPlatformAdminHasNoResolvedRegulatorTenant()
    {
        await using var db = CreateDb();

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupGet(x => x.IsPlatformAdmin).Returns(true);
        tenantContext.SetupGet(x => x.CurrentTenantId).Returns((Guid?)null);
        tenantContext.SetupGet(x => x.ImpersonatingTenantId).Returns((Guid?)null);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "7"),
            new Claim(ClaimTypes.Role, "PlatformAdmin"),
            new Claim("IsPlatformAdmin", "true")
        ], "FC.Admin.Auth"));

        var requirement = new RegulatorTenantAccessRequirement();
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        var platformResolver = new Mock<IPlatformRegulatorTenantResolver>();
        platformResolver
            .Setup(x => x.TryResolveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformRegulatorTenantContext?)null);
        var sut = new RegulatorTenantAccessHandler(
            tenantContext.Object,
            new TenantAccessContextResolver(db),
            platformResolver.Object);

        await sut.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
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
