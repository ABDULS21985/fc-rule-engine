using DotNet.Testcontainers.Builders;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models.BatchSubmission;
using FC.Engine.Infrastructure.Metadata;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Testcontainers.MsSql;
using Xunit;

namespace FC.Engine.Integration.Tests.Submission;

/// <summary>
/// Shared Testcontainers fixture for the RG-34 submission orchestrator tests.
/// R-08: One real SQL Server 2022 container per test class run — no in-memory fakes.
/// The container is started once, migrations run once; each test seeds its own
/// isolated data using unique suffixes.
/// </summary>
public sealed class SubmissionTestFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public SubmissionTestFixture()
    {
        _container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword("YourStrong!Passw0rd")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1433))
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        // Apply all EF Core migrations — creates the full schema (R-03: real DDL, no hand-rolled SQL)
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        await using var db = new MetadataDbContext(options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public MetadataDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new MetadataDbContext(options);
    }
}

/// <summary>
/// RG-34 submission orchestrator integration tests.
///
/// R-05: Tenant isolation — all queries filter by InstitutionId.
/// R-06: Idempotency — duplicate submissions rejected via UQ_submission_items_idempotent.
/// R-07: Correlation ID — same GUID propagated to audit log, events, and batch record.
/// R-08: No in-memory fakes — real SQL Server via Testcontainers.
/// </summary>
public sealed class SubmissionOrchestratorIntegrationTests : IClassFixture<SubmissionTestFixture>
{
    private readonly SubmissionTestFixture _fixture;

    public SubmissionOrchestratorIntegrationTests(SubmissionTestFixture fixture)
    {
        _fixture = fixture;
    }

    // ── Tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitBatchAsync_ValidSubmission_CreatesBatchAndReturnsSuccess()
    {
        await using var db = _fixture.CreateDbContext();
        var (orchestrator, adapter, _) = BuildOrchestrator(db);
        var (institutionId, submissionId) = await SeedTestDataAsync(db, "CBN");

        var result = await orchestrator.SubmitBatchAsync(
            institutionId, "CBN",
            new[] { submissionId }, submittedByUserId: 1,
            CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.BatchId.Should().BeGreaterThan(0);
        result.Status.Should().Be("SUBMITTED");
        result.Receipt.Should().NotBeNull();
        result.Receipt!.ReceiptReference.Should().NotBeNullOrWhiteSpace();
        result.CorrelationId.Should().NotBe(Guid.Empty, "R-07: correlation ID must be assigned");

        var batch = await db.SubmissionBatches
            .Include(b => b.Items)
            .Include(b => b.Receipts)
            .FirstOrDefaultAsync(b => b.Id == result.BatchId);

        batch.Should().NotBeNull();
        batch!.RegulatorCode.Should().Be("CBN");
        batch.Status.Should().Be("SUBMITTED");
        batch.InstitutionId.Should().Be(institutionId);
        batch.CorrelationId.Should().Be(result.CorrelationId, "R-07: correlation ID stored in batch");
        batch.Items.Should().HaveCount(1);
        batch.Receipts.Should().HaveCount(1, "receipt must be persisted after successful dispatch");
    }

    [Fact]
    public async Task SubmitBatchAsync_DuplicateSubmission_R06_IdempotencyRejectsDuplicate()
    {
        await using var db = _fixture.CreateDbContext();
        var (orchestrator, _, _) = BuildOrchestrator(db);
        var (institutionId, submissionId) = await SeedTestDataAsync(db, "CBN");

        // First submission succeeds
        var first = await orchestrator.SubmitBatchAsync(
            institutionId, "CBN",
            new[] { submissionId }, submittedByUserId: 1,
            CancellationToken.None);
        first.Success.Should().BeTrue("first submission must succeed");

        // Second submission of the same submissionId — UQ_submission_items_idempotent fires
        var second = await orchestrator.SubmitBatchAsync(
            institutionId, "CBN",
            new[] { submissionId }, submittedByUserId: 1,
            CancellationToken.None);

        second.Success.Should().BeFalse("R-06: duplicate submission must be rejected");
        second.ErrorMessage.Should().Contain("already in an active batch",
            "error must identify idempotency as the cause");

        // Exactly one SUBMITTED (first) and one FAILED (duplicate attempt)
        var batches = await db.SubmissionBatches
            .Where(b => b.InstitutionId == institutionId && b.RegulatorCode == "CBN")
            .OrderBy(b => b.Id)
            .ToListAsync();

        batches.Should().HaveCount(2);
        batches[0].Status.Should().Be("SUBMITTED");
        batches[1].Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task SubmitBatchAsync_UnknownRegulator_FailsBeforeCreatingBatch()
    {
        await using var db = _fixture.CreateDbContext();
        var (orchestrator, _, _) = BuildOrchestrator(db);
        var (institutionId, submissionId) = await SeedTestDataAsync(db, "CBN");

        var result = await orchestrator.SubmitBatchAsync(
            institutionId, "NONEXISTENT",
            new[] { submissionId }, submittedByUserId: 1,
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("NONEXISTENT");

        // No batch should have been created for the unknown regulator
        var count = await db.SubmissionBatches
            .CountAsync(b => b.InstitutionId == institutionId && b.RegulatorCode == "NONEXISTENT");
        count.Should().Be(0, "no batch must be created when the channel is not configured");
    }

    [Fact]
    public async Task SubmitBatchAsync_TenantIsolation_R05_SubmissionsFromOtherInstitutionAreRejected()
    {
        await using var db = _fixture.CreateDbContext();
        var (orchestrator, _, _) = BuildOrchestrator(db);

        // Seed submission belonging to institution A
        var (_, submissionId) = await SeedTestDataAsync(db, "CBN");

        // Try to submit it as institution B (cross-institution attempt)
        const int wrongInstitutionId = 77777;
        var result = await orchestrator.SubmitBatchAsync(
            wrongInstitutionId, "CBN",
            new[] { submissionId }, submittedByUserId: 1,
            CancellationToken.None);

        result.Success.Should().BeFalse("R-05: cross-institution submission must be rejected");
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RetryBatchAsync_FailedBatch_RedispatchesToAdapterAndSucceeds()
    {
        await using var db = _fixture.CreateDbContext();
        var (orchestrator, adapter, _) = BuildOrchestrator(db);
        var (institutionId, submissionId) = await SeedTestDataAsync(db, "CBN");

        var initial = await orchestrator.SubmitBatchAsync(
            institutionId, "CBN",
            new[] { submissionId }, submittedByUserId: 1,
            CancellationToken.None);
        initial.Success.Should().BeTrue("test setup failed");

        // Force batch to FAILED to simulate a prior dispatch failure
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE meta.submission_batches SET Status = 'FAILED', LastError = 'Simulated network error' WHERE Id = {0}",
            initial.BatchId);

        var dispatchCountBefore = adapter.DispatchCallCount;

        var retryResult = await orchestrator.RetryBatchAsync(institutionId, initial.BatchId, CancellationToken.None);

        retryResult.Success.Should().BeTrue(retryResult.ErrorMessage);
        retryResult.Status.Should().Be("SUBMITTED");
        adapter.DispatchCallCount.Should().Be(dispatchCountBefore + 1,
            "retry must trigger exactly one additional dispatch");

        var updatedBatch = await db.SubmissionBatches.AsNoTracking()
            .FirstAsync(b => b.Id == initial.BatchId);
        updatedBatch.Status.Should().Be("SUBMITTED");
    }

    [Fact]
    public async Task RetryBatchAsync_BatchInSubmittedStatus_RejectsRetryWithoutDispatch()
    {
        await using var db = _fixture.CreateDbContext();
        var (orchestrator, adapter, _) = BuildOrchestrator(db);
        var (institutionId, submissionId) = await SeedTestDataAsync(db, "CBN");

        var initial = await orchestrator.SubmitBatchAsync(
            institutionId, "CBN",
            new[] { submissionId }, submittedByUserId: 1,
            CancellationToken.None);
        initial.Success.Should().BeTrue();

        // Attempt retry on a non-failed batch
        var retryResult = await orchestrator.RetryBatchAsync(institutionId, initial.BatchId, CancellationToken.None);

        retryResult.Success.Should().BeFalse();
        retryResult.ErrorMessage.Should().Contain("SUBMITTED",
            "error message must state the current status");
        adapter.DispatchCallCount.Should().Be(1, "adapter must not be invoked a second time");
    }

    [Fact]
    public async Task RefreshStatusAsync_SubmittedBatch_QueriesAdapterAndReturnsResult()
    {
        await using var db = _fixture.CreateDbContext();
        var (orchestrator, adapter, _) = BuildOrchestrator(db);
        var (institutionId, submissionId) = await SeedTestDataAsync(db, "CBN");

        var initial = await orchestrator.SubmitBatchAsync(
            institutionId, "CBN",
            new[] { submissionId }, submittedByUserId: 1,
            CancellationToken.None);
        initial.Success.Should().BeTrue();

        // Configure adapter to return ACKNOWLEDGED on next status check
        adapter.StatusToReturn = BatchSubmissionStatusValue.Acknowledged;

        var refreshResult = await orchestrator.RefreshStatusAsync(institutionId, initial.BatchId, CancellationToken.None);

        refreshResult.Should().NotBeNull();
        refreshResult.BatchId.Should().Be(initial.BatchId);
        refreshResult.PreviousStatus.Should().Be("SUBMITTED",
            "the previous status must reflect the state before refresh");
    }

    [Fact]
    public async Task SubmitBatchAsync_CorrelationId_R07_SameIdPropagatedToAuditLog()
    {
        await using var db = _fixture.CreateDbContext();
        Guid capturedCorrelationId = Guid.Empty;

        var auditMock = new Mock<ISubmissionBatchAuditLogger>();
        auditMock
            .Setup(a => a.LogAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<Guid>(),
                It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        auditMock
            .Setup(a => a.LogAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<Guid>(),
                "BATCH_CREATED", It.IsAny<object?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback<long, int, Guid, string, object?, int?, CancellationToken>(
                (_, _, cid, _, _, _, _) => capturedCorrelationId = cid)
            .Returns(Task.CompletedTask);

        var (institutionId, submissionId) = await SeedTestDataAsync(db, "CBN");
        var orchestrator = BuildOrchestratorWithCustomAudit(db, auditMock.Object);

        var result = await orchestrator.SubmitBatchAsync(
            institutionId, "CBN",
            new[] { submissionId }, submittedByUserId: 1,
            CancellationToken.None);

        result.CorrelationId.Should().NotBe(Guid.Empty, "R-07: correlation ID must be generated");
        capturedCorrelationId.Should().Be(result.CorrelationId,
            "R-07: the same correlation ID must flow through orchestrator → audit log");
    }

    // ── Seed helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Creates a complete data chain for a test:
    /// Tenant → Institution → ReturnPeriod → Submission + RegulatoryChannel.
    /// Uses a UUID suffix to ensure isolation between concurrent tests.
    /// </summary>
    private static async Task<(int institutionId, int submissionId)> SeedTestDataAsync(
        MetadataDbContext db, string regulatorCode)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var tenant = Tenant.Create(
            $"RG34 Test Tenant {suffix}"[..30],
            $"rg34-test-{suffix}"[..20],
            TenantType.Institution,
            $"rg34-{suffix}@test.local");
        tenant.Activate();
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var institution = new Institution
        {
            TenantId = tenant.TenantId,
            InstitutionCode = $"RG34-{suffix}",
            InstitutionName = $"RG-34 Test Bank {suffix}",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Institutions.Add(institution);
        await db.SaveChangesAsync();

        var period = new ReturnPeriod
        {
            TenantId = tenant.TenantId,
            Year = 2026,
            Month = 3,
            Frequency = "Monthly",
            ReportingDate = new DateTime(2026, 3, 31),
            DeadlineDate = new DateTime(2026, 4, 14),
            IsOpen = true,
            Status = "Open",
            CreatedAt = DateTime.UtcNow
        };
        db.ReturnPeriods.Add(period);
        await db.SaveChangesAsync();

        var submission = Submission.Create(institution.Id, period.Id, "SRF-001", tenant.TenantId);
        submission.MarkAccepted();
        db.Submissions.Add(submission);
        await db.SaveChangesAsync();

        // Seed regulatory channel if not already present (unique constraint on RegulatorCode)
        if (!await db.RegulatoryChannels.AnyAsync(c => c.RegulatorCode == regulatorCode && c.IsActive))
        {
            db.RegulatoryChannels.Add(new RegulatoryChannel
            {
                RegulatorCode = regulatorCode,
                RegulatorName = $"{regulatorCode} Test Channel",
                PortalName = $"{regulatorCode} Portal",
                IntegrationMethod = "REST",
                BaseUrl = "https://stub.test.local",
                IsActive = true,
                RequiresCertificate = false,
                TimeoutSeconds = 30
            });
            await db.SaveChangesAsync();
        }

        return (institution.Id, submission.Id);
    }

    // ── Orchestrator factory helpers ─────────────────────────────────────

    private static (SubmissionOrchestrator orchestrator, StubChannelAdapter adapter, Mock<ISubmissionEventPublisher> events)
        BuildOrchestrator(MetadataDbContext db)
    {
        var signerMock = BuildSignerMock();
        var evidenceMock = BuildEvidenceMock();

        var eventsMock = new Mock<ISubmissionEventPublisher>();
        eventsMock
            .Setup(e => e.PublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var auditMock = new Mock<ISubmissionBatchAuditLogger>();
        auditMock
            .Setup(a => a.LogAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<Guid>(),
                It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var adapter = new StubChannelAdapter();
        var orchestrator = new SubmissionOrchestrator(
            db, signerMock.Object, evidenceMock.Object, auditMock.Object,
            eventsMock.Object, new[] { adapter },
            NullLogger<SubmissionOrchestrator>.Instance);

        return (orchestrator, adapter, eventsMock);
    }

    private static SubmissionOrchestrator BuildOrchestratorWithCustomAudit(
        MetadataDbContext db, ISubmissionBatchAuditLogger audit)
    {
        var eventsMock = new Mock<ISubmissionEventPublisher>();
        eventsMock
            .Setup(e => e.PublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new SubmissionOrchestrator(
            db, BuildSignerMock().Object, BuildEvidenceMock().Object, audit,
            eventsMock.Object, new[] { new StubChannelAdapter() },
            NullLogger<SubmissionOrchestrator>.Instance);
    }

    private static Mock<ISubmissionSigningService> BuildSignerMock()
    {
        var mock = new Mock<ISubmissionSigningService>();
        mock.Setup(s => s.SignPayloadAsync(It.IsAny<int>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchSignatureInfo(
                "THUMBPRINT-STUB", "RSA-SHA512",
                new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
                "deadbeef-hash-stub", DateTimeOffset.UtcNow, null));
        mock.Setup(s => s.VerifySignatureAsync(It.IsAny<BatchSignatureInfo>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return mock;
    }

    private static Mock<IEvidencePackageService> BuildEvidenceMock()
    {
        var mock = new Mock<IEvidencePackageService>();
        // Throws to exercise the graceful fallback path in the orchestrator
        mock.Setup(e => e.GenerateAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Evidence service not available in test environment."));
        return mock;
    }

    // ── Stub channel adapter ─────────────────────────────────────────────

    /// <summary>
    /// In-process stub simulating a regulatory channel adapter.
    /// Returns deterministic receipts; no network calls made.
    /// </summary>
    private sealed class StubChannelAdapter : IRegulatoryChannelAdapter
    {
        public string RegulatorCode => "CBN";
        public string IntegrationMethod => "REST";
        public int DispatchCallCount { get; private set; }
        public BatchSubmissionStatusValue StatusToReturn { get; set; } = BatchSubmissionStatusValue.Submitted;

        public Task<BatchRegulatorReceipt> DispatchAsync(
            DispatchPayload payload, CancellationToken ct = default)
        {
            DispatchCallCount++;
            var receipt = new BatchRegulatorReceipt(
                ReceiptReference: $"STUB-{Guid.NewGuid():N}"[..20],
                ReceiptTimestamp: DateTimeOffset.UtcNow,
                HttpStatusCode: 200,
                RawResponse: "{\"status\":\"ACCEPTED\",\"message\":\"stub\"}");
            return Task.FromResult(receipt);
        }

        public Task<BatchRegulatorStatusResponse> CheckStatusAsync(
            string receiptReference, CancellationToken ct = default)
        {
            return Task.FromResult(new BatchRegulatorStatusResponse(
                receiptReference,
                StatusToReturn.ToString().ToUpperInvariant(),
                StatusToReturn,
                null,
                DateTimeOffset.UtcNow));
        }

        public Task<IReadOnlyList<BatchRegulatorQueryDto>> FetchQueriesAsync(
            string receiptReference, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BatchRegulatorQueryDto>>([]);

        public Task<BatchRegulatorReceipt> SubmitQueryResponseAsync(
            QueryResponsePayload payload, CancellationToken ct = default)
            => Task.FromResult(new BatchRegulatorReceipt(
                $"STUB-QR-{Guid.NewGuid():N}"[..20],
                DateTimeOffset.UtcNow, 200, "{}"));
    }
}
