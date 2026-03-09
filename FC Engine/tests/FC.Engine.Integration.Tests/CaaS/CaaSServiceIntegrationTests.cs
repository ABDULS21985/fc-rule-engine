using Dapper;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models.BatchSubmission;
using FC.Engine.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Data;
using Testcontainers.MsSql;
using Testcontainers.Redis;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace FC.Engine.Integration.Tests.CaaS;

[Collection("CaaSIntegration")]
public sealed class CaaSServiceIntegrationTests
    : IClassFixture<CaaSTestFixture>
{
    private readonly CaaSTestFixture _fx;

    public CaaSServiceIntegrationTests(CaaSTestFixture fx) => _fx = fx;

    [Fact]
    public async Task ValidateAsync_ValidPspData_ReturnsIsValidTrue()
    {
        var partner = _fx.PalmPayPartner;

        var request = new CaaSValidateRequest(
            ModuleCode: "PSP_FINTECH",
            PeriodCode: "2026-03",
            Fields: new Dictionary<string, object?>
            {
                ["TOTAL_TXN_VALUE"]  = 8_450_000_000m,
                ["TOTAL_TXN_COUNT"]  = 2_340_000m,
                ["FAILED_TXN_VALUE"] = 210_000_000m,
                ["FAILED_TXN_COUNT"] = 45_000m,
                ["SETTLEMENT_FLOAT"] = 1_200_000_000m,
                ["CUSTOMER_FUNDS"]   = 3_100_000_000m,
                ["ESCROW_BALANCE"]   = 500_000_000m
            },
            PersistSession: true);

        var result = await _fx.CaaSService.ValidateAsync(partner, request, Guid.NewGuid());

        Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(e => e.Message)));
        Assert.Equal(0, result.ErrorCount);
        Assert.NotNull(result.SessionToken);
        Assert.True(result.ComplianceScore >= 90.0);
    }

    [Fact]
    public async Task ValidateAsync_MissingRequiredField_ReturnsError()
    {
        var partner = _fx.PalmPayPartner;

        var request = new CaaSValidateRequest(
            "PSP_FINTECH", "2026-03",
            new Dictionary<string, object?>
            {
                ["TOTAL_TXN_VALUE"] = 1_000_000m
                // TOTAL_TXN_COUNT intentionally omitted
            });

        var result = await _fx.CaaSService.ValidateAsync(partner, request, Guid.NewGuid());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.FieldCode == "TOTAL_TXN_COUNT");
    }

    [Fact]
    public async Task SubmitAsync_WithValidSession_CreatesReturnAndBatch()
    {
        var partner = _fx.PalmPayPartner;

        // First validate (persist session)
        var validateReq = new CaaSValidateRequest(
            "PSP_FINTECH", "2026-03",
            new Dictionary<string, object?>
            {
                ["TOTAL_TXN_VALUE"]  = 5_000_000_000m,
                ["TOTAL_TXN_COUNT"]  = 1_000_000m,
                ["FAILED_TXN_VALUE"] = 100_000_000m,
                ["FAILED_TXN_COUNT"] = 20_000m,
                ["SETTLEMENT_FLOAT"] = 800_000_000m,
                ["CUSTOMER_FUNDS"]   = 2_000_000_000m,
                ["ESCROW_BALANCE"]   = 400_000_000m
            }, PersistSession: true);

        var validated = await _fx.CaaSService.ValidateAsync(partner, validateReq, Guid.NewGuid());
        Assert.True(validated.IsValid);
        Assert.NotNull(validated.SessionToken);

        // Then submit
        var submitReq = new CaaSSubmitRequest(
            SessionToken: validated.SessionToken,
            ModuleCode: null, PeriodCode: null, Fields: null,
            RegulatorCode: "CBN",
            SubmittedByExternalUserId: 9001);

        var result = await _fx.CaaSService.SubmitAsync(partner, submitReq, Guid.NewGuid());

        Assert.True(result.Success, result.ErrorMessage ?? "Submit failed");
        Assert.NotNull(result.BatchReference);
        Assert.NotNull(result.ReceiptReference);
        Assert.True(result.ReturnInstanceId > 0);
    }

    [Fact]
    public async Task ApiKeyService_CreateValidateRevoke_FullLifecycle()
    {
        var (rawKey, info) = await _fx.ApiKeyService.CreateKeyAsync(
            partnerId: 1, displayName: "Test Key",
            environment: CaaSEnvironment.Test,
            expiresAt: DateTimeOffset.UtcNow.AddYears(1),
            createdByUserId: 101);

        Assert.True(rawKey.StartsWith("regos_test_"));
        Assert.True(info.IsActive);

        // Validate
        var resolved = await _fx.ApiKeyService.ValidateKeyAsync(rawKey);
        Assert.NotNull(resolved);
        Assert.Equal(1, resolved!.PartnerId);

        // Revoke
        await _fx.ApiKeyService.RevokeKeyAsync(1, info.KeyId, 101);

        // Should no longer resolve
        var revoked = await _fx.ApiKeyService.ValidateKeyAsync(rawKey);
        Assert.Null(revoked);
    }

    [Fact]
    public async Task RateLimiter_StarterTier_BlocksOver100RequestsPerMinute()
    {
        const int partnerId = 99_999; // isolated test partner
        const PartnerTier tier = PartnerTier.Starter;

        // Allow first 100
        for (var i = 0; i < 100; i++)
        {
            var r = await _fx.RateLimiter.CheckAndIncrementAsync(partnerId, tier);
            Assert.True(r.Allowed, $"Request {i + 1} should be allowed.");
        }

        // 101st should be blocked
        var blocked = await _fx.RateLimiter.CheckAndIncrementAsync(partnerId, tier);
        Assert.False(blocked.Allowed);
        Assert.Equal(0, blocked.Remaining);
        Assert.True(blocked.RetryAfterSeconds > 0);
    }

    [Fact]
    public async Task WebhookDispatcher_Enqueue_DeliversToWireMock()
    {
        await _fx.WebhookDispatcher.EnqueueAsync(
            partnerId: 1,
            eventType: WebhookEventType.FilingCompleted,
            payload: new { moduleCode = "PSP_FINTECH", period = "2026-03",
                           receiptReference = "CBN-RCP-2026-001" });

        await _fx.WebhookDispatcher.ProcessPendingAsync();

        var calls = _fx.WebhookServer.FindLogEntries(
            Request.Create()
                .WithPath("/webhook/partner-1")
                .UsingPost());

        Assert.NotEmpty(calls);
    }

    [Fact]
    public async Task GetDeadlines_PalmPayModules_ReturnsUpcomingDeadlines()
    {
        // Act — PalmPay is entitled to PSP_FINTECH, PSP_MONTHLY, NFIU_STR
        var result = await _fx.CaaSService.GetDeadlinesAsync(
            _fx.PalmPayPartner, Guid.NewGuid());

        // Assert — all returned deadlines must be within entitled modules
        Assert.NotNull(result.Upcoming);
        Assert.All(result.Upcoming, d =>
            Assert.Contains(d.ModuleCode, _fx.PalmPayPartner.AllowedModuleCodes,
                StringComparer.OrdinalIgnoreCase));

        // Verify no past deadlines leak through (query window = today + 90 days)
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.All(result.Upcoming, d => Assert.False(d.IsOverdue));
        Assert.All(result.Upcoming, d => Assert.True(d.DaysRemaining >= 0));
    }
}

// ── Test Fixture ──────────────────────────────────────────────────────────────

public sealed class CaaSTestFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("CaaS_Test_P@ss1!")
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public ICaaSService CaaSService { get; private set; } = null!;
    public ICaaSApiKeyService ApiKeyService { get; private set; } = null!;
    public ICaaSRateLimiter RateLimiter { get; private set; } = null!;
    public ICaaSWebhookDispatcher WebhookDispatcher { get; private set; } = null!;
    public WireMockServer WebhookServer { get; private set; } = null!;
    public ResolvedPartner PalmPayPartner { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();
        await _redisContainer.StartAsync();

        var connectionString = _sqlContainer.GetConnectionString();

        await new DatabaseMigrator(connectionString).MigrateAsync();

        // WireMock for webhooks — start before seeding so we know the URL
        WebhookServer = WireMockServer.Start();
        WebhookServer.Given(
            Request.Create().WithPath("/webhook/partner-1").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        await SeedTestDataAsync(connectionString, WebhookServer.Url!);

        // Build DI
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<IDbConnectionFactory>(new DirectConnectionFactory(connectionString));
        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(_redisContainer.GetConnectionString()));

        // In-test stubs for complex dependencies
        services.AddSingleton<IValidationPipeline, TestValidationPipeline>();
        services.AddSingleton<ITemplateEngine, TestTemplateEngine>();
        services.AddSingleton<ISubmissionOrchestrator, TestSubmissionOrchestrator>();

        services.AddScoped<ICaaSService, CaaSService>();
        services.AddScoped<ICaaSApiKeyService, CaaSApiKeyService>();
        services.AddSingleton<ICaaSRateLimiter, CaaSRedisRateLimiter>();
        services.AddScoped<ICaaSWebhookDispatcher, CaaSWebhookDispatcher>();

        services.AddHttpClient("Webhook").ConfigureHttpClient(c =>
            c.BaseAddress = new Uri(WebhookServer.Url!));

        var sp = services.BuildServiceProvider();
        CaaSService       = sp.GetRequiredService<ICaaSService>();
        ApiKeyService     = sp.GetRequiredService<ICaaSApiKeyService>();
        RateLimiter       = sp.GetRequiredService<ICaaSRateLimiter>();
        WebhookDispatcher = sp.GetRequiredService<ICaaSWebhookDispatcher>();

        PalmPayPartner = new ResolvedPartner(
            PartnerId: 1, PartnerCode: "PALMPAY-NG", InstitutionId: 42,
            Tier: PartnerTier.Growth, Environment: "LIVE",
            AllowedModuleCodes: new[] { "PSP_FINTECH", "PSP_MONTHLY", "NFIU_STR" });
    }

    private static async Task SeedTestDataAsync(string connectionString, string wireMockUrl)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE CaaSPartners SET WebhookUrl = @Url, WebhookSecret = 'test-webhook-secret-sha256' WHERE Id = 1",
            new { Url = wireMockUrl.TrimEnd('/') + "/webhook/partner-1" });
    }

    public async Task DisposeAsync()
    {
        WebhookServer.Stop();
        await _sqlContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }
}

// ── Database Migrator ─────────────────────────────────────────────────────────

internal sealed class DatabaseMigrator
{
    private readonly string _connectionString;

    public DatabaseMigrator(string connectionString) => _connectionString = connectionString;

    public async Task MigrateAsync()
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync("""
            IF OBJECT_ID('CaaSPartners', 'U') IS NULL
            CREATE TABLE CaaSPartners (
                Id                     INT IDENTITY(1,1) PRIMARY KEY,
                PartnerCode            VARCHAR(30)       NOT NULL,
                PartnerName            NVARCHAR(150)     NOT NULL,
                ContactEmail           NVARCHAR(150)     NOT NULL,
                Tier                   VARCHAR(20)       NOT NULL DEFAULT 'STARTER',
                InstitutionId          INT               NOT NULL,
                IsActive               BIT               NOT NULL DEFAULT 1,
                WhiteLabelName         NVARCHAR(100)     NULL,
                WhiteLabelLogoUrl      NVARCHAR(500)     NULL,
                WhiteLabelPrimaryColor VARCHAR(7)        NULL,
                AllowedModuleCodes     NVARCHAR(MAX)     NULL,
                WebhookUrl             NVARCHAR(500)     NULL,
                WebhookSecret          NVARCHAR(128)     NULL,
                CreatedAt              DATETIME2(3)      NOT NULL DEFAULT SYSUTCDATETIME(),
                UpdatedAt              DATETIME2(3)      NOT NULL DEFAULT SYSUTCDATETIME(),
                CONSTRAINT UQ_CaaSPartners_Code UNIQUE (PartnerCode)
            );
            """);

        await conn.ExecuteAsync("""
            IF OBJECT_ID('CaaSApiKeys', 'U') IS NULL
            BEGIN
                CREATE TABLE CaaSApiKeys (
                    Id              BIGINT IDENTITY(1,1) PRIMARY KEY,
                    PartnerId       INT           NOT NULL,
                    KeyPrefix       VARCHAR(12)   NOT NULL,
                    KeyHash         VARCHAR(64)   NOT NULL,
                    DisplayName     NVARCHAR(100) NOT NULL,
                    Environment     VARCHAR(10)   NOT NULL DEFAULT 'LIVE',
                    IsActive        BIT           NOT NULL DEFAULT 1,
                    ExpiresAt       DATETIME2(3)  NULL,
                    LastUsedAt      DATETIME2(3)  NULL,
                    RevokedAt       DATETIME2(3)  NULL,
                    RevokedByUserId INT           NULL,
                    CreatedByUserId INT           NOT NULL,
                    CreatedAt       DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT FK_CaaSApiKeys_Partner FOREIGN KEY (PartnerId) REFERENCES CaaSPartners(Id),
                    CONSTRAINT UQ_CaaSApiKeys_Hash UNIQUE (KeyHash)
                );
                CREATE INDEX IX_CaaSApiKeys_Hash    ON CaaSApiKeys (KeyHash);
                CREATE INDEX IX_CaaSApiKeys_Partner ON CaaSApiKeys (PartnerId, IsActive);
            END
            """);

        await conn.ExecuteAsync("""
            IF OBJECT_ID('CaaSValidationSessions', 'U') IS NULL
            BEGIN
                CREATE TABLE CaaSValidationSessions (
                    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
                    PartnerId           INT           NOT NULL,
                    SessionToken        VARCHAR(64)   NOT NULL,
                    ModuleCode          VARCHAR(30)   NOT NULL,
                    PeriodCode          VARCHAR(10)   NOT NULL,
                    SubmittedData       NVARCHAR(MAX) NOT NULL,
                    ValidationResult    NVARCHAR(MAX) NULL,
                    IsValid             BIT           NULL,
                    ExpiresAt           DATETIME2(3)  NOT NULL,
                    ConvertedToReturnId BIGINT        NULL,
                    CreatedAt           DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
                    UpdatedAt           DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT UQ_CaaSValidationSessions_Token UNIQUE (SessionToken)
                );
                CREATE INDEX IX_CaaSValidationSessions_Partner
                    ON CaaSValidationSessions (PartnerId, ExpiresAt);
            END
            """);

        await conn.ExecuteAsync("""
            IF OBJECT_ID('CaaSWebhookDeliveries', 'U') IS NULL
            BEGIN
                CREATE TABLE CaaSWebhookDeliveries (
                    Id               BIGINT IDENTITY(1,1) PRIMARY KEY,
                    PartnerId        INT           NOT NULL,
                    EventType        VARCHAR(60)   NOT NULL,
                    Payload          NVARCHAR(MAX) NOT NULL,
                    HmacSignature    VARCHAR(128)  NOT NULL,
                    AttemptCount     INT           NOT NULL DEFAULT 0,
                    MaxAttempts      INT           NOT NULL DEFAULT 5,
                    Status           VARCHAR(20)   NOT NULL DEFAULT 'PENDING',
                    LastAttemptAt    DATETIME2(3)  NULL,
                    DeliveredAt      DATETIME2(3)  NULL,
                    LastHttpStatus   INT           NULL,
                    LastErrorMessage NVARCHAR(1000) NULL,
                    NextRetryAt      DATETIME2(3)  NULL,
                    CreatedAt        DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME()
                );
                CREATE INDEX IX_CaaSWebhookDeliveries_Partner
                    ON CaaSWebhookDeliveries (PartnerId, Status);
                CREATE INDEX IX_CaaSWebhookDeliveries_NextRetry
                    ON CaaSWebhookDeliveries (NextRetryAt, Status);
            END
            """);

        // ReturnInstances — required by CaaSService.CreateReturnInstanceAsync
        await conn.ExecuteAsync("""
            IF OBJECT_ID('ReturnInstances', 'U') IS NULL
            CREATE TABLE ReturnInstances (
                Id               BIGINT IDENTITY(1,1) PRIMARY KEY,
                InstitutionId    INT           NOT NULL,
                ModuleCode       VARCHAR(30)   NOT NULL,
                ReturnVersion    INT           NOT NULL DEFAULT 1,
                ReportingPeriod  VARCHAR(10)   NOT NULL,
                Status           VARCHAR(20)   NOT NULL DEFAULT 'DRAFT',
                FieldDataJson    NVARCHAR(MAX) NULL,
                CreatedBy        INT           NOT NULL,
                CreatedAt        DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME()
            );
            """);

        // Seed partner
        await conn.ExecuteAsync("""
            IF NOT EXISTS (SELECT 1 FROM CaaSPartners WHERE Id = 1)
            BEGIN
                SET IDENTITY_INSERT CaaSPartners ON;
                INSERT INTO CaaSPartners
                    (Id, PartnerCode, PartnerName, ContactEmail, Tier, InstitutionId, AllowedModuleCodes)
                VALUES
                    (1, 'PALMPAY-NG', 'PalmPay Nigeria Limited',
                     'compliance@palmpay.com', 'GROWTH', 42,
                     '["PSP_FINTECH","PSP_MONTHLY","NFIU_STR"]');
                SET IDENTITY_INSERT CaaSPartners OFF;
            END
            """);

        // ReturnModules + FilingDeadlines — required by CaaSService.GetDeadlinesAsync
        await conn.ExecuteAsync("""
            IF OBJECT_ID('ReturnModules', 'U') IS NULL
            CREATE TABLE ReturnModules (
                Code          VARCHAR(30)   NOT NULL PRIMARY KEY,
                ModuleName    NVARCHAR(150) NOT NULL,
                RegulatorCode VARCHAR(10)   NOT NULL,
                Frequency     VARCHAR(20)   NOT NULL DEFAULT 'MONTHLY'
            );
            """);

        await conn.ExecuteAsync("""
            IF OBJECT_ID('FilingDeadlines', 'U') IS NULL
            CREATE TABLE FilingDeadlines (
                Id          BIGINT IDENTITY(1,1) PRIMARY KEY,
                ModuleCode  VARCHAR(30)  NOT NULL,
                PeriodCode  VARCHAR(10)  NOT NULL,
                DeadlineDate DATE         NOT NULL
            );
            """);

        // Seed module + deadline within 90-day window (30 days from now)
        await conn.ExecuteAsync("""
            IF NOT EXISTS (SELECT 1 FROM ReturnModules WHERE Code = 'PSP_FINTECH')
            BEGIN
                INSERT INTO ReturnModules (Code, ModuleName, RegulatorCode, Frequency)
                VALUES ('PSP_FINTECH', 'PSP Fintech Monthly Return', 'CBN', 'MONTHLY'),
                       ('PSP_MONTHLY', 'PSP Monthly Reconciliation',  'CBN', 'MONTHLY'),
                       ('NFIU_STR',   'Suspicious Transaction Report','NFIU','MONTHLY');
            END
            """);

        await conn.ExecuteAsync("""
            IF NOT EXISTS (SELECT 1 FROM FilingDeadlines WHERE ModuleCode = 'PSP_FINTECH')
            BEGIN
                INSERT INTO FilingDeadlines (ModuleCode, PeriodCode, DeadlineDate)
                VALUES ('PSP_FINTECH', '2026-03', DATEADD(DAY, 30, CAST(SYSUTCDATETIME() AS DATE))),
                       ('PSP_MONTHLY', '2026-03', DATEADD(DAY, 45, CAST(SYSUTCDATETIME() AS DATE)));
            END
            """);
    }
}

// ── Direct connection factory (no tenant RLS — used by CaaS services) ─────────

internal sealed class DirectConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public DirectConnectionFactory(string connectionString) => _connectionString = connectionString;

    public async Task<IDbConnection> CreateConnectionAsync(
        Guid? tenantId, CancellationToken ct = default)
    {
        var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}

// ── In-test stubs ─────────────────────────────────────────────────────────────

/// <summary>
/// Validates a PSP_FINTECH field map — returns errors for any of the 7 required fields
/// that are missing.
/// </summary>
internal sealed class TestValidationPipeline : IValidationPipeline
{
    private static readonly HashSet<string> RequiredFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "TOTAL_TXN_VALUE", "TOTAL_TXN_COUNT", "FAILED_TXN_VALUE",
        "FAILED_TXN_COUNT", "SETTLEMENT_FLOAT", "CUSTOMER_FUNDS", "ESCROW_BALANCE"
    };

    public Task<CaaSValidationReport> ValidateAsync(
        int institutionId, string moduleCode, string periodCode,
        Dictionary<string, object?> fields, CancellationToken ct = default)
    {
        var violations = RequiredFields
            .Where(r => !fields.ContainsKey(r) || fields[r] is null)
            .Select(r => new CaaSViolation
            {
                FieldCode  = r,
                FieldLabel = r,
                ErrorCode  = "REQUIRED",
                Message    = $"{r} is required.",
                Severity   = "ERROR"
            })
            .ToArray();

        return Task.FromResult(new CaaSValidationReport
        {
            Violations  = violations,
            TotalFields = RequiredFields.Count
        });
    }
}

/// <summary>Returns a minimal PSP_FINTECH template.</summary>
internal sealed class TestTemplateEngine : ITemplateEngine
{
    public Task<CaaSModuleTemplate> GetTemplateAsync(
        int institutionId, string moduleCode, CancellationToken ct = default)
    {
        static CaaSTemplateFieldInfo F(string code, string label, string type, bool req)
            => new() { Code = code, Label = label, DataType = type, IsRequired = req };

        return Task.FromResult(new CaaSModuleTemplate
        {
            ModuleName    = "PSP Fintech Monthly",
            RegulatorCode = moduleCode,
            PeriodType    = "MONTHLY",
            Fields = new[]
            {
                F("TOTAL_TXN_VALUE",  "Total Transaction Value",  "DECIMAL", true),
                F("TOTAL_TXN_COUNT",  "Total Transaction Count",  "INTEGER", true),
                F("FAILED_TXN_VALUE", "Failed Transaction Value", "DECIMAL", true),
                F("FAILED_TXN_COUNT", "Failed Transaction Count", "INTEGER", true),
                F("SETTLEMENT_FLOAT", "Settlement Float",         "DECIMAL", true),
                F("CUSTOMER_FUNDS",   "Customer Funds",           "DECIMAL", true),
                F("ESCROW_BALANCE",   "Escrow Balance",           "DECIMAL", true)
            },
            Formulas = Array.Empty<CaaSFormulaInfo>()
        });
    }
}

/// <summary>Simulates an ISubmissionOrchestrator that always succeeds.</summary>
internal sealed class TestSubmissionOrchestrator : ISubmissionOrchestrator
{
    private long _batchId = 20_000;

    public Task<BatchSubmissionResult> SubmitBatchAsync(
        int institutionId, string regulatorCode,
        IReadOnlyList<int> submissionIds, int submittedByUserId,
        CancellationToken ct = default)
    {
        var bid = Interlocked.Increment(ref _batchId);
        var receipt = new BatchRegulatorReceipt(
            ReceiptReference: $"CBN-RCP-{bid}",
            ReceiptTimestamp: DateTimeOffset.UtcNow,
            HttpStatusCode: 200,
            RawResponse: null);

        return Task.FromResult(new BatchSubmissionResult(
            Success: true,
            BatchId: bid,
            BatchReference: $"BATCH-{bid}",
            Status: "SUBMITTED",
            Receipt: receipt,
            ErrorMessage: null,
            CorrelationId: Guid.NewGuid()));
    }

    public Task<BatchSubmissionResult> RetryBatchAsync(
        int institutionId, long batchId, CancellationToken ct = default)
        => SubmitBatchAsync(institutionId, "CBN", Array.Empty<int>(), 0, ct);

    public Task<BatchStatusRefreshResult> RefreshStatusAsync(
        int institutionId, long batchId, CancellationToken ct = default)
        => Task.FromResult(new BatchStatusRefreshResult(
            BatchId: batchId,
            PreviousStatus: "SUBMITTED",
            CurrentStatus: "SUBMITTED",
            StatusChanged: false,
            LatestReceipt: null));
}
