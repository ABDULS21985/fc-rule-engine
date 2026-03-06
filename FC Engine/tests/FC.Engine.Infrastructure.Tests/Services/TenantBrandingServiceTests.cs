using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FC.Engine.Infrastructure.Tests.Services;

public class TenantBrandingServiceTests : IDisposable
{
    private readonly MetadataDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly FakeStorageService _storage;
    private readonly TenantBrandingService _sut;

    public TenantBrandingServiceTests()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new MetadataDbContext(options);
        _cache = new MemoryCache(new MemoryCacheOptions());
        _storage = new FakeStorageService();

        _sut = new TenantBrandingService(
            _db,
            _cache,
            _storage,
            new ServiceCollection().BuildServiceProvider());
    }

    public void Dispose()
    {
        _cache.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task Default_Theme_Renders_When_BrandingConfig_Is_Null()
    {
        var tenant = Tenant.Create("Tenant A", "tenant-a", TenantType.Institution, "a@test.local");
        tenant.Activate();
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        var config = await _sut.GetBrandingConfig(tenant.TenantId);

        config.PrimaryColor.Should().Be("#006B3F");
        config.SecondaryColor.Should().Be("#C8A415");
    }

    [Fact]
    public async Task Branding_Cache_Invalidates_On_Update()
    {
        var tenant = Tenant.Create("Tenant B", "tenant-b", TenantType.Institution, "b@test.local");
        tenant.Activate();
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        var first = await _sut.GetBrandingConfig(tenant.TenantId);
        first.PrimaryColor.Should().Be("#006B3F");

        var updated = BrandingConfig.WithDefaults(first);
        updated.PrimaryColor = "#0057B8";

        await _sut.UpdateBrandingConfig(tenant.TenantId, updated);
        var second = await _sut.GetBrandingConfig(tenant.TenantId);

        second.PrimaryColor.Should().Be("#0057B8");
    }

    [Fact]
    public async Task Logo_Upload_Stores_File_And_Updates_Config()
    {
        var tenant = Tenant.Create("Tenant C", "tenant-c", TenantType.Institution, "c@test.local");
        tenant.Activate();
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        await using var stream = new MemoryStream(new byte[128]);

        var url = await _sut.UploadLogo(tenant.TenantId, stream, "logo.png", "image/png");
        var config = await _sut.GetBrandingConfig(tenant.TenantId);

        url.Should().StartWith("/uploads/");
        config.LogoUrl.Should().Be(url);
        _storage.UploadedPaths.Should().ContainSingle();
    }

    [Fact]
    public async Task Logo_Rejects_Over_2MB()
    {
        var tenant = Tenant.Create("Tenant D", "tenant-d", TenantType.Institution, "d@test.local");
        tenant.Activate();
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        await using var stream = new MemoryStream(new byte[(2 * 1024 * 1024) + 1]);

        var act = async () => await _sut.UploadLogo(tenant.TenantId, stream, "logo.png", "image/png");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*under 2MB*");
    }

    [Fact]
    public async Task Logo_Rejects_Non_Image_Files()
    {
        var tenant = Tenant.Create("Tenant E", "tenant-e", TenantType.Institution, "e@test.local");
        tenant.Activate();
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        await using var stream = new MemoryStream(new byte[32]);

        var act = async () => await _sut.UploadLogo(tenant.TenantId, stream, "logo.txt", "text/plain");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*PNG*SVG*JPEG*ICO*");
    }

    private sealed class FakeStorageService : IFileStorageService
    {
        public List<string> UploadedPaths { get; } = new();

        public Task<string> UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default)
        {
            UploadedPaths.Add(path);
            return Task.FromResult($"/uploads/{path}");
        }

        public Task<Stream> DownloadAsync(string path, CancellationToken ct = default)
        {
            Stream stream = new MemoryStream();
            return Task.FromResult(stream);
        }

        public Task DeleteAsync(string path, CancellationToken ct = default)
        {
            UploadedPaths.Remove(path);
            return Task.CompletedTask;
        }

        public Task<string> UploadImmutableAsync(string path, Stream content, string contentType, CancellationToken ct = default)
        {
            UploadedPaths.Add(path);
            return Task.FromResult($"/uploads/{path}");
        }

        public string GetPublicUrl(string path) => $"/uploads/{path}";
    }
}
