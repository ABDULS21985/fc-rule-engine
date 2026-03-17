using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Metadata.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FC.Engine.Infrastructure.Tests.Persistence;

public class InstitutionHierarchyTests : IDisposable
{
    private readonly MetadataDbContext _db;
    private readonly InstitutionRepository _sut;
    private readonly Guid _tenantId;

    public InstitutionHierarchyTests()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new MetadataDbContext(options);
        _sut = new InstitutionRepository(new TestDbContextFactory(_db));

        // Create tenant
        var tenant = Tenant.Create("HoldingGroup", "holding-group", TenantType.HoldingGroup);
        tenant.Activate();
        _db.Tenants.Add(tenant);
        _db.SaveChanges();
        _tenantId = tenant.TenantId;

        SeedHierarchy();
    }

    public void Dispose() => _db.Dispose();

    private void SeedHierarchy()
    {
        // HeadOffice (root)
        var headOffice = new Institution
        {
            TenantId = _tenantId,
            InstitutionCode = "HQ001",
            InstitutionName = "HoldCo Head Office",
            EntityType = EntityType.HeadOffice,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _db.Institutions.Add(headOffice);
        _db.SaveChanges();

        // Subsidiary 1
        var sub1 = new Institution
        {
            TenantId = _tenantId,
            InstitutionCode = "SUB001",
            InstitutionName = "Banking Subsidiary",
            EntityType = EntityType.Subsidiary,
            ParentInstitutionId = headOffice.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        // Subsidiary 2
        var sub2 = new Institution
        {
            TenantId = _tenantId,
            InstitutionCode = "SUB002",
            InstitutionName = "Insurance Subsidiary",
            EntityType = EntityType.Subsidiary,
            ParentInstitutionId = headOffice.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _db.Institutions.AddRange(sub1, sub2);
        _db.SaveChanges();

        // Branch under Subsidiary 1
        var branch = new Institution
        {
            TenantId = _tenantId,
            InstitutionCode = "BR001",
            InstitutionName = "Lagos Branch",
            EntityType = EntityType.Branch,
            BranchCode = "LG01",
            Location = "Lagos",
            ParentInstitutionId = sub1.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _db.Institutions.Add(branch);
        _db.SaveChanges();
    }

    [Fact]
    public async Task GetChildren_ReturnsDirectChildren()
    {
        var headOffice = await _db.Institutions.FirstAsync(i => i.InstitutionCode == "HQ001");

        var children = await _sut.GetChildren(headOffice.Id);

        children.Should().HaveCount(2);
        children.Select(c => c.InstitutionCode).Should()
            .BeEquivalentTo(new[] { "SUB001", "SUB002" });
    }

    [Fact]
    public async Task GetChildren_SubsidiaryHasBranch()
    {
        var sub1 = await _db.Institutions.FirstAsync(i => i.InstitutionCode == "SUB001");

        var children = await _sut.GetChildren(sub1.Id);

        children.Should().HaveCount(1);
        children[0].InstitutionCode.Should().Be("BR001");
        children[0].EntityType.Should().Be(EntityType.Branch);
    }

    [Fact]
    public async Task GetHierarchy_ReturnsAllDescendants()
    {
        var headOffice = await _db.Institutions.FirstAsync(i => i.InstitutionCode == "HQ001");

        var hierarchy = await _sut.GetHierarchy(headOffice.Id);

        hierarchy.Should().HaveCount(3); // sub1, sub2, branch
        hierarchy.Select(i => i.InstitutionCode).Should()
            .BeEquivalentTo(new[] { "SUB001", "SUB002", "BR001" });
    }

    [Fact]
    public async Task GetDescendantIds_ReturnsAllDescendantIds()
    {
        var headOffice = await _db.Institutions.FirstAsync(i => i.InstitutionCode == "HQ001");

        var ids = await _sut.GetDescendantIds(headOffice.Id);

        ids.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetByTenant_ReturnsAllInstitutions()
    {
        var institutions = await _sut.GetByTenant(_tenantId);

        institutions.Should().HaveCount(4); // HQ + 2 subs + 1 branch
    }

    [Fact]
    public async Task GetById_IncludesChildInstitutions()
    {
        var headOffice = await _db.Institutions.FirstAsync(i => i.InstitutionCode == "HQ001");

        var result = await _sut.GetById(headOffice.Id);

        result.Should().NotBeNull();
        result!.ChildInstitutions.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetDescendantIds_LeafNode_ReturnsEmpty()
    {
        var branch = await _db.Institutions.FirstAsync(i => i.InstitutionCode == "BR001");

        var ids = await _sut.GetDescendantIds(branch.Id);

        ids.Should().BeEmpty();
    }

    private sealed class TestDbContextFactory : IDbContextFactory<MetadataDbContext>
    {
        private readonly MetadataDbContext _db;

        public TestDbContextFactory(MetadataDbContext db) => _db = db;

        public MetadataDbContext CreateDbContext() => _db;

        public Task<MetadataDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_db);
    }
}
