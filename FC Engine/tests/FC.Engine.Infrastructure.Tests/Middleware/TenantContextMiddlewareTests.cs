using System.Security.Claims;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.MultiTenancy;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FC.Engine.Infrastructure.Tests.Middleware;

public class TenantContextMiddlewareTests
{
    [Fact]
    public async Task PlatformAdmin_RegulatorRoute_AutoBinds_Default_Regulator_Tenant()
    {
        await using var db = CreateDb();
        var tenant = CreateTenant("cbn");
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var context = new DefaultHttpContext();
        context.Request.Path = "/regulator/inbox";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Role, "PlatformAdmin"),
            new Claim("IsPlatformAdmin", "true")
        ], "FC.Admin.Auth"));

        var platformResolver = new Mock<IPlatformRegulatorTenantResolver>();
        platformResolver
            .Setup(x => x.TryResolveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformRegulatorTenantContext(tenant.TenantId, tenant.TenantName, "CBN"));

        var calledNext = false;
        var sut = new TenantContextMiddleware(_ =>
        {
            calledNext = true;
            return Task.CompletedTask;
        }, NullLogger<TenantContextMiddleware>.Instance);

        await sut.InvokeAsync(context, new TenantAccessContextResolver(db), platformResolver.Object);

        calledNext.Should().BeTrue();
        context.Items["TenantId"].Should().Be(tenant.TenantId);
        context.Items["ImpersonatingTenantId"].Should().Be(tenant.TenantId);
        context.Items["TenantType"].Should().Be(TenantType.Regulator.ToString());
        context.Items["RegulatorCode"].Should().Be("CBN");
        context.Response.Headers.SetCookie.ToString().Should().Contain("ImpersonateTenantId=");
    }

    private static MetadataDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MetadataDbContext(options);
    }

    private static Tenant CreateTenant(string slug)
    {
        var tenant = Tenant.Create(slug.ToUpperInvariant(), slug, TenantType.Regulator, $"{slug}@regos.app");
        tenant.Activate();
        return tenant;
    }
}
