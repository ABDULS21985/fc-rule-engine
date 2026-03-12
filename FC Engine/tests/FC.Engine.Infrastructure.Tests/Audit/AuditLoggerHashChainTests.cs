using FC.Engine.Domain.Abstractions;
using FC.Engine.Infrastructure.Audit;
using FC.Engine.Infrastructure.Metadata;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FC.Engine.Infrastructure.Tests.Audit;

public class AuditLoggerHashChainTests
{
    [Fact]
    public async Task First_Entry_Has_GENESIS_PreviousHash()
    {
        await using var db = CreateDb(nameof(First_Entry_Has_GENESIS_PreviousHash));
        var tenantId = Guid.NewGuid();
        var sut = CreateLogger(db, tenantId);

        await sut.Log("Submission", 1, "Create", null, new { Status = "Draft" }, "user1");

        var entry = await db.AuditLog.SingleAsync();
        entry.PreviousHash.Should().Be("GENESIS");
        entry.SequenceNumber.Should().Be(1);
        entry.Hash.Should().NotBeNullOrEmpty();
        entry.Hash.Should().HaveLength(64); // SHA-256 hex length
    }

    [Fact]
    public async Task Hash_Is_Deterministic_For_Same_Inputs()
    {
        var hash1 = AuditLogger.ComputeHash(1, "Submission", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Guid.Empty, "user1", "Submission", 1, "Create", null, null, "GENESIS");
        var hash2 = AuditLogger.ComputeHash(1, "Submission", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Guid.Empty, "user1", "Submission", 1, "Create", null, null, "GENESIS");

        hash1.Should().Be(hash2);
    }

    [Fact]
    public async Task Sequential_Entries_Form_Valid_Chain()
    {
        await using var db = CreateDb(nameof(Sequential_Entries_Form_Valid_Chain));
        var tenantId = Guid.NewGuid();
        var sut = CreateLogger(db, tenantId);

        await sut.Log("Submission", 1, "Create", null, new { Status = "Draft" }, "user1");
        await sut.Log("Submission", 1, "Update", new { Status = "Draft" }, new { Status = "Submitted" }, "user1");
        await sut.Log("Submission", 1, "Approve", null, null, "checker1");

        var entries = await db.AuditLog.OrderBy(e => e.SequenceNumber).ToListAsync();
        entries.Should().HaveCount(3);

        entries[0].PreviousHash.Should().Be("GENESIS");
        entries[1].PreviousHash.Should().Be(entries[0].Hash);
        entries[2].PreviousHash.Should().Be(entries[1].Hash);

        entries[0].SequenceNumber.Should().Be(1);
        entries[1].SequenceNumber.Should().Be(2);
        entries[2].SequenceNumber.Should().Be(3);
    }

    [Fact]
    public async Task Different_Tenants_Have_Independent_Chains()
    {
        await using var db = CreateDb(nameof(Different_Tenants_Have_Independent_Chains));
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var loggerA = CreateLogger(db, tenantA);
        var loggerB = CreateLogger(db, tenantB);

        await loggerA.Log("Submission", 1, "Create", null, null, "userA");
        await loggerB.Log("Submission", 2, "Create", null, null, "userB");

        var entriesA = await db.AuditLog.Where(e => e.TenantId == tenantA).ToListAsync();
        var entriesB = await db.AuditLog.Where(e => e.TenantId == tenantB).ToListAsync();

        entriesA.Should().ContainSingle();
        entriesB.Should().ContainSingle();

        // Both should start from GENESIS independently
        entriesA[0].PreviousHash.Should().Be("GENESIS");
        entriesA[0].SequenceNumber.Should().Be(1);
        entriesB[0].PreviousHash.Should().Be("GENESIS");
        entriesB[0].SequenceNumber.Should().Be(1);
    }

    [Fact]
    public async Task Hash_Changes_With_Different_Inputs()
    {
        var hash1 = AuditLogger.ComputeHash(1, "Submission", DateTime.UtcNow,
            Guid.Empty, "user1", "Submission", 1, "Create", null, null, "GENESIS");
        var hash2 = AuditLogger.ComputeHash(1, "Submission", DateTime.UtcNow,
            Guid.Empty, "user1", "Submission", 1, "Update", null, null, "GENESIS");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public async Task Log_Truncates_Overlong_Action_Codes_To_Fit_Audit_Log_Schema()
    {
        await using var db = CreateDb(nameof(Log_Truncates_Overlong_Action_Codes_To_Fit_Audit_Log_Schema));
        var tenantId = Guid.NewGuid();
        var sut = CreateLogger(db, tenantId);
        var action = new string('A', 80);

        await sut.Log("Submission", 1, action, null, new { Status = "Draft" }, "user1");

        var entry = await db.AuditLog.SingleAsync();
        entry.Action.Should().HaveLength(64);
        entry.Action.Should().Be(action[..64]);
    }

    private static MetadataDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new MetadataDbContext(options);
    }

    private static AuditLogger CreateLogger(MetadataDbContext db, Guid tenantId)
    {
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.CurrentTenantId).Returns(tenantId);
        return new AuditLogger(db, tenantContext.Object);
    }
}
