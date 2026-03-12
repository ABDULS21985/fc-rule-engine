using FC.Engine.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Metadata;

/// <summary>
/// Scoped factory for creating short-lived <see cref="MetadataDbContext"/> instances
/// in Blazor Server components where the circuit-scoped DbContext causes concurrency issues.
///
/// In Blazor Server, "scoped" = circuit lifetime (entire user session). A single DbContext
/// shared across concurrent async operations (fire-and-forget handlers, timer callbacks,
/// parallel event handlers) violates EF Core's single-thread requirement.
///
/// This factory resolves the scoped DbContextOptions (which includes the RLS interceptor)
/// and ITenantContext from the current scope, then creates isolated DbContext instances
/// that each manage their own connection and change tracker.
///
/// Usage in .razor files:
///   @inject IDbContextFactory&lt;MetadataDbContext&gt; DbFactory
///   ...
///   await using var db = await DbFactory.CreateDbContextAsync();
///   var data = await db.SomeTable.AsNoTracking().ToListAsync();
/// </summary>
internal sealed class MetadataDbContextFactory(
    DbContextOptions<MetadataDbContext> options,
    ITenantContext tenantContext)
    : IDbContextFactory<MetadataDbContext>
{
    public MetadataDbContext CreateDbContext() => new(options, tenantContext);
}
