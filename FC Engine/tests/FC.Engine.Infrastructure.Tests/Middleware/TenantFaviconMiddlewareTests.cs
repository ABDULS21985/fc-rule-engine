using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace FC.Engine.Infrastructure.Tests.Middleware;

public class TenantFaviconMiddlewareTests
{
    [Fact]
    public async Task Tenant_Favicon_Is_Redirected_When_Configured()
    {
        var tenantId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.Path = "/favicon.ico";

        var brandingService = new StubBrandingService(tenantId, BrandingConfig.WithDefaults(new BrandingConfig
        {
            FaviconUrl = "https://cdn.example.com/tenant/favicon.ico"
        }));

        var tenantContext = new StubTenantContext(tenantId);

        var calledNext = false;
        var sut = new TenantFaviconMiddleware(_ =>
        {
            calledNext = true;
            return Task.CompletedTask;
        });

        await sut.InvokeAsync(context, brandingService, tenantContext);

        calledNext.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status302Found);
        context.Response.Headers.Location.ToString().Should().Be("https://cdn.example.com/tenant/favicon.ico");
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public StubTenantContext(Guid tenantId)
        {
            CurrentTenantId = tenantId;
        }

        public Guid? CurrentTenantId { get; }
        public bool IsPlatformAdmin => false;
        public Guid? ImpersonatingTenantId => null;
    }

    private sealed class StubBrandingService : ITenantBrandingService
    {
        private readonly Guid _tenantId;
        private readonly BrandingConfig _config;

        public StubBrandingService(Guid tenantId, BrandingConfig config)
        {
            _tenantId = tenantId;
            _config = config;
        }

        public Task<BrandingConfig> GetBrandingConfig(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult(tenantId == _tenantId ? _config : BrandingConfig.WithDefaults());

        public Task UpdateBrandingConfig(Guid tenantId, BrandingConfig config, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<string> UploadLogo(Guid tenantId, Stream fileStream, string fileName, string contentType, CancellationToken ct = default)
            => Task.FromResult(string.Empty);

        public Task<string> UploadFavicon(Guid tenantId, Stream fileStream, string fileName, string contentType, CancellationToken ct = default)
            => Task.FromResult(string.Empty);

        public Task InvalidateCache(Guid tenantId, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
