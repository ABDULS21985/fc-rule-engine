using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Tests.Services;

public class ApiKeyServiceTests
{
    private static MetadataDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new MetadataDbContext(options);
    }

    [Fact]
    public async Task New_API_Key_Format_regos_live_prefix()
    {
        await using var db = CreateDb();
        var tenant = Tenant.Create("ApiKey Tenant", "api-key-tenant", TenantType.Institution);
        tenant.Activate();
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var sut = new ApiKeyService(db);
        var created = await sut.CreateApiKey(tenant.TenantId, 11, new CreateApiKeyRequest
        {
            Description = "Integration key",
            Permissions = new List<string> { "submission.create" }
        });

        created.RawKey.Should().StartWith("regos_live_");
        created.Prefix.Length.Should().BeLessThanOrEqualTo(20);
        created.Message.Should().Contain("Save this key securely");

        var validated = await sut.ValidateApiKey(created.RawKey, "127.0.0.1");
        validated.Should().NotBeNull();
        validated!.TenantId.Should().Be(tenant.TenantId);
        validated.Permissions.Should().Contain("submission.create");
    }

    [Fact]
    public async Task Expired_API_Key_Rejected()
    {
        await using var db = CreateDb();
        var tenant = Tenant.Create("ApiKey Expired", "api-key-expired", TenantType.Institution);
        tenant.Activate();
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var sut = new ApiKeyService(db);
        var created = await sut.CreateApiKey(tenant.TenantId, 22, new CreateApiKeyRequest
        {
            Description = "Expired",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1)
        });

        var validated = await sut.ValidateApiKey(created.RawKey, "127.0.0.1");
        validated.Should().BeNull();
    }

    [Fact]
    public async Task API_Key_Permissions_Enforced()
    {
        await using var db = CreateDb();
        var tenant = Tenant.Create("ApiKey Scoped", "api-key-scoped", TenantType.Institution);
        tenant.Activate();
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var sut = new ApiKeyService(db);
        var created = await sut.CreateApiKey(tenant.TenantId, 23, new CreateApiKeyRequest
        {
            Description = "Scoped",
            Permissions = new List<string> { "submission.create", "report.read" }
        });

        var validated = await sut.ValidateApiKey(created.RawKey, "127.0.0.1");
        validated.Should().NotBeNull();
        validated!.Permissions.Should().Contain(new[] { "submission.create", "report.read" });
        validated.Permissions.Should().NotContain("submission.approve");
    }
}
