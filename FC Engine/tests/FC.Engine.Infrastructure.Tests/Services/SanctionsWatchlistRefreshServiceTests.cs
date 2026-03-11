using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FC.Engine.Infrastructure.Tests.Services;

public class SanctionsWatchlistRefreshServiceTests
{
    [Fact]
    public async Task RefreshIfStaleAsync_Materializes_When_Catalog_Is_Empty()
    {
        await using var db = CreateDbContext(nameof(RefreshIfStaleAsync_Materializes_When_Catalog_Is_Empty));
        var catalogService = new SanctionsWatchlistCatalogService(db);
        var refreshService = new SanctionsWatchlistRefreshService(catalogService, NullLogger<SanctionsWatchlistRefreshService>.Instance);

        var refreshed = await refreshService.RefreshIfStaleAsync(TimeSpan.FromHours(24));
        var state = await catalogService.LoadAsync();

        refreshed.Should().BeTrue();
        state.Sources.Should().HaveCount(7);
        state.Entries.Should().HaveCount(16);
        state.Sources.Should().Contain(x => x.SourceCode == "UN");
        state.Entries.Should().Contain(x => x.SourceCode == "OFAC" && x.PrimaryName == "BOKO HARAM");
    }

    [Fact]
    public async Task RefreshIfStaleAsync_Does_Not_Rematerialize_When_Catalog_Is_Fresh()
    {
        await using var db = CreateDbContext(nameof(RefreshIfStaleAsync_Does_Not_Rematerialize_When_Catalog_Is_Fresh));
        var catalogService = new SanctionsWatchlistCatalogService(db);
        var refreshService = new SanctionsWatchlistRefreshService(catalogService, NullLogger<SanctionsWatchlistRefreshService>.Instance);

        await refreshService.RefreshBaselineAsync();
        var before = await catalogService.LoadAsync();

        var refreshed = await refreshService.RefreshIfStaleAsync(TimeSpan.FromHours(24));
        var after = await catalogService.LoadAsync();

        refreshed.Should().BeFalse();
        after.MaterializedAt.Should().Be(before.MaterializedAt);
        after.Sources.Should().HaveCount(before.Sources.Count);
        after.Entries.Should().HaveCount(before.Entries.Count);
    }

    [Fact]
    public async Task RefreshIfStaleAsync_Rematerializes_When_Catalog_Is_Stale()
    {
        await using var db = CreateDbContext(nameof(RefreshIfStaleAsync_Rematerializes_When_Catalog_Is_Stale));
        var catalogService = new SanctionsWatchlistCatalogService(db);
        var refreshService = new SanctionsWatchlistRefreshService(catalogService, NullLogger<SanctionsWatchlistRefreshService>.Instance);

        await refreshService.RefreshBaselineAsync();
        var staleAt = DateTime.UtcNow.AddDays(-3);

        foreach (var source in db.SanctionsCatalogSources)
        {
            source.MaterializedAt = staleAt;
        }

        foreach (var entry in db.SanctionsCatalogEntries)
        {
            entry.MaterializedAt = staleAt;
        }

        await db.SaveChangesAsync();

        var refreshed = await refreshService.RefreshIfStaleAsync(TimeSpan.FromHours(24));
        var after = await catalogService.LoadAsync();

        refreshed.Should().BeTrue();
        after.MaterializedAt.Should().NotBeNull();
        after.MaterializedAt.Should().BeAfter(staleAt);
    }

    private static MetadataDbContext CreateDbContext(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new MetadataDbContext(options);
    }
}
