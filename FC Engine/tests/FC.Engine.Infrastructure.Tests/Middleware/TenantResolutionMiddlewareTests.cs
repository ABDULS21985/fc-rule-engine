using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace FC.Engine.Infrastructure.Tests.Middleware;

public class TenantResolutionMiddlewareTests : IDisposable
{
    private readonly MetadataDbContext _db;
    private readonly IMemoryCache _cache;

    public TenantResolutionMiddlewareTests()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new MetadataDbContext(options);
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    public void Dispose()
    {
        _cache.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task Custom_Domain_Resolves_To_Correct_Tenant()
    {
        var tenant = await SeedTenant("zenith-bank", "compliance.zenithbank.com");

        var subscriptionSvc = new Mock<ISubscriptionService>();
        subscriptionSvc
            .Setup(s => s.HasFeature(tenant.TenantId, "custom_domain", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("compliance.zenithbank.com");

        var calledNext = false;
        var sut = new TenantResolutionMiddleware(_ =>
        {
            calledNext = true;
            return Task.CompletedTask;
        });

        await sut.InvokeAsync(context, _db, _cache, subscriptionSvc.Object);

        calledNext.Should().BeTrue();
        context.Items["TenantId"].Should().Be(tenant.TenantId);
    }

    [Fact]
    public async Task Subdomain_Resolves_To_Correct_Tenant()
    {
        var tenant = await SeedTenant("zenith-bank", null);

        var subscriptionSvc = new Mock<ISubscriptionService>();
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("zenith-bank.regos.app");

        var sut = new TenantResolutionMiddleware(_ => Task.CompletedTask);
        await sut.InvokeAsync(context, _db, _cache, subscriptionSvc.Object);

        context.Items["TenantId"].Should().Be(tenant.TenantId);
    }

    [Fact]
    public async Task Unknown_Domain_Falls_Through()
    {
        await SeedTenant("known-tenant", "known.example.com");

        var subscriptionSvc = new Mock<ISubscriptionService>();
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("evil.com");

        var sut = new TenantResolutionMiddleware(_ => Task.CompletedTask);
        await sut.InvokeAsync(context, _db, _cache, subscriptionSvc.Object);

        context.Items.ContainsKey("TenantId").Should().BeFalse();
    }

    [Fact]
    public async Task Custom_Domain_Without_Feature_Redirects_To_Subdomain()
    {
        var tenant = await SeedTenant("zenith-bank", "compliance.zenithbank.com");

        var subscriptionSvc = new Mock<ISubscriptionService>();
        subscriptionSvc
            .Setup(s => s.HasFeature(tenant.TenantId, "custom_domain", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Path = "/login";
        context.Request.QueryString = new QueryString("?next=%2Fdashboard");
        context.Request.Host = new HostString("compliance.zenithbank.com");

        var calledNext = false;
        var sut = new TenantResolutionMiddleware(_ =>
        {
            calledNext = true;
            return Task.CompletedTask;
        });

        await sut.InvokeAsync(context, _db, _cache, subscriptionSvc.Object);

        calledNext.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status302Found);
        context.Response.Headers.Location.ToString()
            .Should().Be("https://zenith-bank.regos.app/login?next=%2Fdashboard");
    }

    private async Task<Tenant> SeedTenant(string slug, string? customDomain)
    {
        var tenant = Tenant.Create($"{slug} ltd", slug, TenantType.Institution, $"{slug}@test.local");
        tenant.CustomDomain = customDomain;
        tenant.Activate();

        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        return tenant;
    }
}
