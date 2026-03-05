using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FC.Engine.Infrastructure.Tests.Services;

public class BenchmarkingServiceTests
{
    [Fact]
    public async Task GetPeerBenchmark_Returns_Null_When_Feature_Is_Not_Enabled()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(nameof(GetPeerBenchmark_Returns_Null_When_Feature_Is_Not_Enabled));
        db.Modules.Add(new Module
        {
            Id = 1,
            ModuleCode = "FC",
            ModuleName = "FC Returns",
            RegulatorCode = "CBN",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var entitlementSvc = new Mock<IEntitlementService>();
        entitlementSvc
            .Setup(x => x.HasFeatureAccess(tenantId, "peer_benchmarking", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new BenchmarkingService(db, cache, entitlementSvc.Object, NullLogger<BenchmarkingService>.Instance);

        var result = await sut.GetPeerBenchmark(tenantId, "FC");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPeerBenchmark_Returns_Aggregated_Anonymous_Metrics()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenantC = Guid.NewGuid();

        await using var db = CreateDb(nameof(GetPeerBenchmark_Returns_Aggregated_Anonymous_Metrics));

        db.Modules.Add(new Module
        {
            Id = 2,
            ModuleCode = "FC",
            ModuleName = "FC Returns",
            RegulatorCode = "CBN",
            CreatedAt = DateTime.UtcNow
        });

        db.TenantLicenceTypes.AddRange(
            new TenantLicenceType { TenantId = tenantA, LicenceTypeId = 9, EffectiveDate = DateTime.UtcNow, IsActive = true },
            new TenantLicenceType { TenantId = tenantB, LicenceTypeId = 9, EffectiveDate = DateTime.UtcNow, IsActive = true },
            new TenantLicenceType { TenantId = tenantC, LicenceTypeId = 9, EffectiveDate = DateTime.UtcNow, IsActive = true });

        db.FilingSlaRecords.AddRange(
            new FilingSlaRecord { TenantId = tenantA, ModuleId = 2, PeriodId = 1001, DaysToDeadline = 5, OnTime = true },
            new FilingSlaRecord { TenantId = tenantA, ModuleId = 2, PeriodId = 1002, DaysToDeadline = 3, OnTime = true },
            new FilingSlaRecord { TenantId = tenantB, ModuleId = 2, PeriodId = 1003, DaysToDeadline = 1, OnTime = true },
            new FilingSlaRecord { TenantId = tenantC, ModuleId = 2, PeriodId = 1004, DaysToDeadline = -2, OnTime = false });

        await db.SaveChangesAsync();

        var entitlementSvc = new Mock<IEntitlementService>();
        entitlementSvc
            .Setup(x => x.HasFeatureAccess(tenantA, "peer_benchmarking", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new BenchmarkingService(db, cache, entitlementSvc.Object, NullLogger<BenchmarkingService>.Instance);

        var result = await sut.GetPeerBenchmark(tenantA, "FC");

        result.Should().NotBeNull();
        result!.TenantAverageDays.Should().Be(4m);
        result.PeerMedianDays.Should().Be(2m);
        result.PeerP25Days.Should().Be(0.25m);
        result.PeerP75Days.Should().Be(3.5m);
        result.Percentile.Should().Be(75);
        result.PeerCount.Should().Be(3);
    }

    private static MetadataDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new MetadataDbContext(options);
    }
}

