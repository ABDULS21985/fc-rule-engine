# RG-35: Embedded Compliance-as-a-Service API

> **Stream F — Advanced Compliance · Phase 4 · RegOS™ World-Class SupTech Prompt**

---

| Field | Value |
|---|---|
| **Prompt ID** | RG-35 |
| **Stream** | F — Advanced Compliance |
| **Phase** | Phase 4 |
| **Principle** | Transform RegOS™ into the world's leading Regulatory & SupTech platform |
| **Depends On** | RG-15 (API gateway & rate limiting), RG-05 (auth & RBAC), RG-07 (template engine), RG-11 (four-phase validation pipeline), RG-13 (export format adapters), RG-34 (submission engine) |
| **Estimated Effort** | 8–12 days |
| **Classification** | Confidential — Internal Engineering |

---

## 0 · Preamble & Governing Rules

You are a **senior .NET 8 architect** building the Compliance-as-a-Service (CaaS) API layer for RegOS™ — an embeddable, white-label compliance engine that fintechs, neobanks, and core banking vendors integrate into their own products via REST API and JavaScript SDK. Every artefact you produce must satisfy these non-negotiable rules:

| # | Rule |
|---|---|
| R-01 | **Zero mock data.** All seed data uses realistic Nigerian regulatory context: real module codes (`PSP_FINTECH`, `MFB_MONTHLY`, `DMB_WEEKLY`), real CBN/SEC/NFIU form references, real institution type codes. |
| R-02 | **No stubs, no TODOs, no `throw new NotImplementedException()`.** Every method body is complete and production-ready. |
| R-03 | **Complete DDL first.** All tables, indexes, constraints, and seed data as idempotent EF Core migrations before any service code. |
| R-04 | **Parameterised queries only.** No string interpolation in SQL. Dapper `DynamicParameters` or EF Core LINQ exclusively. |
| R-05 | **Partner (tenant) isolation on every query.** All CaaS tables carry `PartnerId`; every repository filters by it. Cross-partner data leakage is architecturally impossible. |
| R-06 | **API key lifecycle management.** Keys are created, rotated, revoked, and expire. No key is ever stored in plaintext — SHA-256 hash only. The raw key is shown once at creation. |
| R-07 | **Rate limiting per partner tier.** Starter: 100 req/min. Growth: 1,000 req/min. Enterprise: 10,000 req/min. Enforced via Redis sliding-window counter, not in-memory. |
| R-08 | **Structured logging with correlation.** Every API request carries `X-CaaS-Request-Id`; every log entry, validation result, and audit row references it. |
| R-09 | **Integration tests with Testcontainers.** Real SQL Server + Redis + WireMock for core banking stubs. No in-memory fakes for pipeline tests. |
| R-10 | **All secrets via Azure Key Vault or `IConfiguration` providers.** Zero hardcoded credentials. |
| R-11 | **Webhook reliability.** Outbound webhooks use at-least-once delivery with exponential back-off (Polly v8) and a dead-letter table for permanently failed deliveries. |
| R-12 | **JavaScript SDK — zero external dependencies.** The embedded widget ships as a single self-contained `validator.js` bundle. No React, no jQuery, no CDN dependencies at runtime. |

---

## 1 · Architecture Context

```
┌───────────────────────────────────────────────────────────────────────┐
│                     Partner Fintech / Neobank                         │
│                                                                       │
│  ┌─────────────────────────────────┐  ┌────────────────────────────┐ │
│  │   Partner Web / Mobile App      │  │   Core Banking System      │ │
│  │                                 │  │   (Finacle / T24 / BankOne │ │
│  │  <div id="regos-validator"/>    │  │    / Flexcube)             │ │
│  │  (embedded widget)              │  └──────────┬─────────────────┘ │
│  └──────────────┬──────────────────┘             │ REST / DB          │
│                 │ HTTPS + API Key                 │ (CoreBanking       │
│                 │                                 │  Adapter)          │
└─────────────────┼─────────────────────────────────┼───────────────────┘
                  │                                 │
          ┌───────▼─────────────────────────────────▼───────┐
          │            CaaS API Gateway (RG-35)              │
          │                                                   │
          │  ┌────────────┐  ┌────────────┐  ┌────────────┐ │
          │  │  Partner   │  │  Rate      │  │  API Key   │ │
          │  │  Router    │  │  Limiter   │  │  Validator │ │
          │  └─────┬──────┘  └────────────┘  └────────────┘ │
          │        │                                          │
          │  ┌─────▼──────────────────────────────────────┐  │
          │  │            CaaS Service Layer               │  │
          │  │                                             │  │
          │  │  Validate  │  Submit  │  Score  │  Simulate │  │
          │  │  Templates │ Deadlines│ Changes │  Webhooks │  │
          │  └─────┬──────────────────────────────────────┘  │
          │        │                                          │
          │        ▼                                          │
          │  ┌─────────────────────────────────────────────┐ │
          │  │     RegOS™ Core (RG-07, RG-11, RG-34)       │ │
          │  │  Template Engine · Validation · Submission   │ │
          │  └─────────────────────────────────────────────┘ │
          └───────────────────────────────────────────────────┘
                  │
          ┌───────▼─────────────────────────────────────────┐
          │         Outbound Webhooks & Notifications         │
          │   filing.completed · validation.failed ·          │
          │   deadline.approaching · changes.detected          │
          └─────────────────────────────────────────────────-─┘
```

---

## 2 · Complete DDL (EF Core Migration)

> Deliver as a single idempotent migration: `20260315_AddCaaSSchema.cs`

### 2.1 Core Tables

```sql
-- ============================================================
-- Table: CaaSPartners
-- Purpose: Registered partner organisations consuming CaaS API
-- ============================================================
CREATE TABLE CaaSPartners (
    Id                  INT IDENTITY(1,1) PRIMARY KEY,
    PartnerCode         VARCHAR(30)     NOT NULL,   -- e.g., 'PALMPAY-NG', 'KUDA-MFB'
    PartnerName         NVARCHAR(150)   NOT NULL,
    ContactEmail        NVARCHAR(150)   NOT NULL,
    Tier                VARCHAR(20)     NOT NULL DEFAULT 'STARTER',
        -- 'STARTER' (100/min), 'GROWTH' (1000/min), 'ENTERPRISE' (10000/min)
    InstitutionId       INT             NOT NULL,   -- FK to RegOS Institutions
    IsActive            BIT             NOT NULL DEFAULT 1,
    WhiteLabelName      NVARCHAR(100)   NULL,       -- override product name (e.g., 'PalmPay Compliance')
    WhiteLabelLogoUrl   NVARCHAR(500)   NULL,
    WhiteLabelPrimaryColor VARCHAR(7)   NULL,       -- hex e.g., '#006AFF'
    AllowedModuleCodes  NVARCHAR(MAX)   NULL,       -- JSON array: ["PSP_FINTECH","PSP_MONTHLY"]
    WebhookUrl          NVARCHAR(500)   NULL,
    WebhookSecret       NVARCHAR(128)   NULL,       -- HMAC-SHA256 signing secret (hashed)
    CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_CaaSPartners_Code UNIQUE (PartnerCode)
);

-- ============================================================
-- Table: CaaSApiKeys
-- Purpose: API keys issued to partners (stored as SHA-256 hash)
-- ============================================================
CREATE TABLE CaaSApiKeys (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    PartnerId           INT             NOT NULL,
    KeyPrefix           VARCHAR(12)     NOT NULL,   -- first 8 chars of raw key (displayable)
    KeyHash             VARCHAR(64)     NOT NULL,   -- SHA-256 of raw key (lowercase hex)
    DisplayName         NVARCHAR(100)   NOT NULL,   -- e.g., 'Production Key', 'CI/CD Key'
    Environment         VARCHAR(10)     NOT NULL DEFAULT 'LIVE',   -- 'LIVE', 'TEST'
    IsActive            BIT             NOT NULL DEFAULT 1,
    ExpiresAt           DATETIME2(3)    NULL,       -- NULL = never expires
    LastUsedAt          DATETIME2(3)    NULL,
    RevokedAt           DATETIME2(3)    NULL,
    RevokedByUserId     INT             NULL,
    CreatedByUserId     INT             NOT NULL,
    CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_CaaSApiKeys_Partner
        FOREIGN KEY (PartnerId) REFERENCES CaaSPartners(Id),
    CONSTRAINT UQ_CaaSApiKeys_Hash UNIQUE (KeyHash),
    INDEX IX_CaaSApiKeys_Hash (KeyHash),
    INDEX IX_CaaSApiKeys_Partner (PartnerId, IsActive)
);

-- ============================================================
-- Table: CaaSRequests
-- Purpose: Audit log of every CaaS API call
-- ============================================================
CREATE TABLE CaaSRequests (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    PartnerId           INT             NOT NULL,
    ApiKeyId            BIGINT          NOT NULL,
    RequestId           UNIQUEIDENTIFIER NOT NULL,  -- X-CaaS-Request-Id
    Endpoint            VARCHAR(100)    NOT NULL,   -- '/api/v1/caas/validate'
    HttpMethod          VARCHAR(10)     NOT NULL,
    ModuleCode          VARCHAR(30)     NULL,
    ResponseStatusCode  INT             NOT NULL,
    DurationMs          INT             NOT NULL,
    RequestBodyHash     VARCHAR(64)     NULL,       -- SHA-256 of request body
    IpAddress           VARCHAR(45)     NULL,
    UserAgent           NVARCHAR(300)   NULL,
    CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    INDEX IX_CaaSRequests_Partner (PartnerId, CreatedAt DESC),
    INDEX IX_CaaSRequests_RequestId (RequestId),
    INDEX IX_CaaSRequests_Endpoint (Endpoint, CreatedAt DESC)
);

-- ============================================================
-- Table: CaaSValidationSessions
-- Purpose: Persisted validation sessions (for widget & async flows)
-- ============================================================
CREATE TABLE CaaSValidationSessions (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    PartnerId           INT             NOT NULL,
    SessionToken        VARCHAR(64)     NOT NULL,   -- random 32-byte hex token
    ModuleCode          VARCHAR(30)     NOT NULL,
    PeriodCode          VARCHAR(10)     NOT NULL,   -- e.g., '2026-03', '2026-Q1'
    SubmittedData       NVARCHAR(MAX)   NOT NULL,   -- JSON field map
    ValidationResult    NVARCHAR(MAX)   NULL,       -- JSON ValidationReport
    IsValid             BIT             NULL,
    ExpiresAt           DATETIME2(3)    NOT NULL,   -- sessions TTL = 24h
    ConvertedToReturnId BIGINT          NULL,       -- set when /submit used this session
    CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_CaaSValidationSessions_Token UNIQUE (SessionToken),
    INDEX IX_CaaSValidationSessions_Partner (PartnerId, ExpiresAt)
);

-- ============================================================
-- Table: CaaSWebhookDeliveries
-- Purpose: Outbound webhook delivery log (at-least-once)
-- ============================================================
CREATE TABLE CaaSWebhookDeliveries (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    PartnerId           INT             NOT NULL,
    EventType           VARCHAR(60)     NOT NULL,
        -- 'filing.completed', 'validation.failed', 'deadline.approaching',
        -- 'changes.detected', 'score.updated', 'extraction.completed'
    Payload             NVARCHAR(MAX)   NOT NULL,   -- JSON
    HmacSignature       VARCHAR(128)    NOT NULL,
    AttemptCount        INT             NOT NULL DEFAULT 0,
    MaxAttempts         INT             NOT NULL DEFAULT 5,
    Status              VARCHAR(20)     NOT NULL DEFAULT 'PENDING',
        -- 'PENDING', 'DELIVERED', 'FAILED', 'DEAD_LETTER'
    LastAttemptAt       DATETIME2(3)    NULL,
    DeliveredAt         DATETIME2(3)    NULL,
    LastHttpStatus      INT             NULL,
    LastErrorMessage    NVARCHAR(1000)  NULL,
    NextRetryAt         DATETIME2(3)    NULL,
    CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    INDEX IX_CaaSWebhookDeliveries_Partner (PartnerId, Status),
    INDEX IX_CaaSWebhookDeliveries_NextRetry (NextRetryAt, Status)
);

-- ============================================================
-- Table: CaaSCoreBankingConnections
-- Purpose: Saved core banking system connection configurations
-- ============================================================
CREATE TABLE CaaSCoreBankingConnections (
    Id                  INT IDENTITY(1,1) PRIMARY KEY,
    PartnerId           INT             NOT NULL,
    SystemType          VARCHAR(20)     NOT NULL,
        -- 'FINACLE', 'T24', 'BANKONE', 'FLEXCUBE'
    ConnectionName      NVARCHAR(100)   NOT NULL,
    BaseUrl             NVARCHAR(500)   NULL,
    DatabaseServer      NVARCHAR(200)   NULL,
    CredentialSecretName NVARCHAR(100)  NOT NULL,   -- Key Vault reference
    FieldMappingJson    NVARCHAR(MAX)   NOT NULL,   -- JSON: module field → CB field/query
    IsActive            BIT             NOT NULL DEFAULT 1,
    LastTestedAt        DATETIME2(3)    NULL,
    LastTestResult      NVARCHAR(500)   NULL,
    CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_CaaSCoreBankingConnections_Partner
        FOREIGN KEY (PartnerId) REFERENCES CaaSPartners(Id),
    INDEX IX_CaaSCoreBankingConnections_Partner (PartnerId, SystemType)
);

-- ============================================================
-- Table: CaaSAutoFilingSchedules
-- Purpose: Automated extract → validate → submit schedules
-- ============================================================
CREATE TABLE CaaSAutoFilingSchedules (
    Id                  INT IDENTITY(1,1) PRIMARY KEY,
    PartnerId           INT             NOT NULL,
    ModuleCode          VARCHAR(30)     NOT NULL,
    CoreBankingConnectionId INT         NOT NULL,
    CronExpression      VARCHAR(100)    NOT NULL,   -- e.g., '0 2 1 * *' = 1st of month at 02:00
    AutoSubmitIfClean   BIT             NOT NULL DEFAULT 0,
    NotifyEmails        NVARCHAR(500)   NULL,       -- comma-separated
    IsActive            BIT             NOT NULL DEFAULT 1,
    LastRunAt           DATETIME2(3)    NULL,
    LastRunStatus       VARCHAR(20)     NULL,
    NextRunAt           DATETIME2(3)    NULL,
    CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_CaaSAutoFilingSchedules_Partner
        FOREIGN KEY (PartnerId) REFERENCES CaaSPartners(Id),
    CONSTRAINT FK_CaaSAutoFilingSchedules_Connection
        FOREIGN KEY (CoreBankingConnectionId) REFERENCES CaaSCoreBankingConnections(Id),
    INDEX IX_CaaSAutoFilingSchedules_NextRun (NextRunAt, IsActive)
);

-- ============================================================
-- Table: CaaSAutoFilingRuns
-- Purpose: Execution history of auto-filing schedule runs
-- ============================================================
CREATE TABLE CaaSAutoFilingRuns (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    ScheduleId          INT             NOT NULL,
    PartnerId           INT             NOT NULL,
    ModuleCode          VARCHAR(30)     NOT NULL,
    PeriodCode          VARCHAR(10)     NOT NULL,
    Phase               VARCHAR(20)     NOT NULL,   -- 'EXTRACT','VALIDATE','SUBMIT','COMPLETE','FAILED'
    ValidationSessionId BIGINT          NULL,
    ReturnInstanceId    BIGINT          NULL,
    BatchId             BIGINT          NULL,       -- FK to RG-34 SubmissionBatches
    IsClean             BIT             NULL,
    ErrorMessage        NVARCHAR(2000)  NULL,
    StartedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    CompletedAt         DATETIME2(3)    NULL,
    CONSTRAINT FK_CaaSAutoFilingRuns_Schedule
        FOREIGN KEY (ScheduleId) REFERENCES CaaSAutoFilingSchedules(Id),
    INDEX IX_CaaSAutoFilingRuns_Schedule (ScheduleId, StartedAt DESC),
    INDEX IX_CaaSAutoFilingRuns_Partner (PartnerId, Phase)
);
```

### 2.2 Seed Data — Partner Tiers & Example Partners

```sql
-- Tier rate limit reference (stored as application constants, not a table)
-- STARTER:    100 requests / minute
-- GROWTH:    1000 requests / minute
-- ENTERPRISE: 10000 requests / minute

SET IDENTITY_INSERT CaaSPartners ON;
INSERT INTO CaaSPartners
    (Id, PartnerCode, PartnerName, ContactEmail, Tier, InstitutionId,
     WhiteLabelName, AllowedModuleCodes)
VALUES
    (1, 'PALMPAY-NG',    'PalmPay Nigeria Limited',          'compliance@palmpay.com',   'GROWTH',     42,
     'PalmPay Compliance Engine', '["PSP_FINTECH","PSP_MONTHLY","NFIU_STR"]'),
    (2, 'KUDA-MFB',      'Kuda Microfinance Bank',           'regulatory@kuda.com',      'GROWTH',     57,
     'Kuda Regulatory Hub',       '["MFB_MONTHLY","MFB_QUARTERLY","NFIU_CTR"]'),
    (3, 'CARBON-FIN',    'Carbon Finance Limited',           'fintech@carbon.ng',        'STARTER',    83,
     NULL,                         '["PSP_FINTECH","PSP_MONTHLY"]'),
    (4, 'MONIEPOINT-MFB','Moniepoint Microfinance Bank',     'compliance@moniepoint.com','ENTERPRISE', 18,
     'Moniepoint Compliance',      '["MFB_MONTHLY","MFB_QUARTERLY","DMB_WEEKLY","NFIU_CTR","NFIU_STR"]');
SET IDENTITY_INSERT CaaSPartners OFF;
```

---

## 3 · Domain Models

```csharp
// ============================================================
// Enums
// ============================================================
public enum PartnerTier { Starter, Growth, Enterprise }

public enum CaaSEnvironment { Live, Test }

public enum WebhookEventType
{
    FilingCompleted,
    ValidationFailed,
    DeadlineApproaching,
    ChangesDetected,
    ScoreUpdated,
    ExtractionCompleted,
    AutoFilingHeld          // extraction OK but validation errors — held for review
}

public enum CoreBankingSystem { Finacle, T24, BankOne, Flexcube }

public enum AutoFilingPhase { Extract, Validate, Submit, Complete, Failed }

// ============================================================
// Rate limit constants
// ============================================================
public static class RateLimitThresholds
{
    public static int GetRequestsPerMinute(PartnerTier tier) => tier switch
    {
        PartnerTier.Starter    => 100,
        PartnerTier.Growth     => 1_000,
        PartnerTier.Enterprise => 10_000,
        _ => 100
    };
}

// ============================================================
// CaaS Request / Response models
// ============================================================

// POST /api/v1/caas/validate
public sealed record CaaSValidateRequest(
    string ModuleCode,
    string PeriodCode,
    Dictionary<string, object?> Fields,
    bool   PersistSession = false    // if true, returns a session token for /submit
);

public sealed record CaaSValidateResponse(
    bool   IsValid,
    string? SessionToken,            // set when PersistSession = true
    int    ErrorCount,
    int    WarningCount,
    IReadOnlyList<CaaSFieldError> Errors,
    IReadOnlyList<CaaSFieldError> Warnings,
    double ComplianceScore,          // 0.0–100.0
    Guid   RequestId
);

public sealed record CaaSFieldError(
    string FieldCode,
    string FieldLabel,
    string ErrorCode,
    string Message,
    string Severity                  // "ERROR" | "WARNING"
);

// POST /api/v1/caas/submit
public sealed record CaaSSubmitRequest(
    string? SessionToken,            // re-use a validated session
    string? ModuleCode,              // required if no session token
    string? PeriodCode,
    Dictionary<string, object?>? Fields,
    string  RegulatorCode,
    int     SubmittedByExternalUserId
);

public sealed record CaaSSubmitResponse(
    bool    Success,
    long?   ReturnInstanceId,
    long?   BatchId,
    string? BatchReference,
    string? ReceiptReference,
    string? ErrorMessage,
    Guid    RequestId
);

// GET /api/v1/caas/templates/{module}
public sealed record CaaSTemplateResponse(
    string ModuleCode,
    string ModuleName,
    string RegulatorCode,
    string PeriodType,               // "MONTHLY" | "QUARTERLY" | "WEEKLY" | "ANNUAL"
    IReadOnlyList<CaaSFieldDefinition> Fields,
    IReadOnlyList<CaaSFormula> Formulas,
    Guid RequestId
);

public sealed record CaaSFieldDefinition(
    string FieldCode,
    string FieldLabel,
    string DataType,                 // "DECIMAL" | "INTEGER" | "TEXT" | "DATE" | "BOOLEAN"
    bool   IsRequired,
    string? ValidationRule,
    decimal? MinValue,
    decimal? MaxValue,
    string? Description
);

public sealed record CaaSFormula(
    string FormulaCode,
    string Description,
    string Expression               // e.g., "NPL_RATIO = NPL_LOANS / GROSS_LOANS * 100"
);

// GET /api/v1/caas/deadlines
public sealed record CaaSDeadlinesResponse(
    IReadOnlyList<CaaSDeadline> Upcoming,
    Guid RequestId
);

public sealed record CaaSDeadline(
    string ModuleCode,
    string ModuleName,
    string PeriodCode,
    DateOnly DeadlineDate,
    int DaysRemaining,
    bool IsOverdue,
    string RegulatorCode
);

// POST /api/v1/caas/score
public sealed record CaaSScoreRequest(
    string? PeriodCode                // null = current period
);

public sealed record CaaSScoreResponse(
    double OverallScore,
    string Rating,                   // "EXCELLENT" | "GOOD" | "SATISFACTORY" | "NEEDS_ATTENTION" | "CRITICAL"
    IReadOnlyList<CaaSModuleScore> ByModule,
    Guid RequestId
);

public sealed record CaaSModuleScore(
    string ModuleCode,
    string ModuleName,
    double Score,
    int PendingReturns,
    int OverdueReturns,
    int ValidationErrors
);

// GET /api/v1/caas/changes
public sealed record CaaSChangesResponse(
    IReadOnlyList<CaaSRegulatoryChange> Changes,
    Guid RequestId
);

public sealed record CaaSRegulatoryChange(
    string ChangeId,
    string RegulatorCode,
    string ModuleCode,
    string Title,
    string Summary,
    DateOnly EffectiveDate,
    string Severity                  // "MINOR" | "MODERATE" | "MAJOR"
);

// POST /api/v1/caas/simulate
public sealed record CaaSSimulateRequest(
    string ModuleCode,
    string PeriodCode,
    Dictionary<string, object?> Fields,
    IReadOnlyList<CaaSScenario> Scenarios
);

public sealed record CaaSScenario(
    string ScenarioName,
    Dictionary<string, object?> FieldOverrides
);

public sealed record CaaSSimulateResponse(
    IReadOnlyList<CaaSScenarioResult> Results,
    Guid RequestId
);

public sealed record CaaSScenarioResult(
    string ScenarioName,
    bool IsValid,
    double ComplianceScore,
    IReadOnlyList<CaaSFieldError> Errors,
    Dictionary<string, object?> ComputedValues  // formula outputs
);

// ============================================================
// Partner resolution (from API key middleware)
// ============================================================
public sealed record ResolvedPartner(
    int PartnerId,
    string PartnerCode,
    int InstitutionId,
    PartnerTier Tier,
    string Environment,
    IReadOnlyList<string> AllowedModuleCodes
);
```

---

## 4 · Service Contracts (Interfaces)

### 4.1 CaaS Service — top-level orchestrator for all endpoints

```csharp
/// <summary>
/// Implements all CaaS API operations. Called by the ASP.NET minimal API
/// endpoint handlers after auth/rate-limit middleware resolves the partner.
/// </summary>
public interface ICaaSService
{
    Task<CaaSValidateResponse> ValidateAsync(
        ResolvedPartner partner,
        CaaSValidateRequest request,
        Guid requestId,
        CancellationToken ct = default);

    Task<CaaSSubmitResponse> SubmitAsync(
        ResolvedPartner partner,
        CaaSSubmitRequest request,
        Guid requestId,
        CancellationToken ct = default);

    Task<CaaSTemplateResponse> GetTemplateAsync(
        ResolvedPartner partner,
        string moduleCode,
        Guid requestId,
        CancellationToken ct = default);

    Task<CaaSDeadlinesResponse> GetDeadlinesAsync(
        ResolvedPartner partner,
        Guid requestId,
        CancellationToken ct = default);

    Task<CaaSScoreResponse> GetScoreAsync(
        ResolvedPartner partner,
        CaaSScoreRequest request,
        Guid requestId,
        CancellationToken ct = default);

    Task<CaaSChangesResponse> GetChangesAsync(
        ResolvedPartner partner,
        Guid requestId,
        CancellationToken ct = default);

    Task<CaaSSimulateResponse> SimulateAsync(
        ResolvedPartner partner,
        CaaSSimulateRequest request,
        Guid requestId,
        CancellationToken ct = default);
}
```

### 4.2 API Key Service

```csharp
public interface ICaaSApiKeyService
{
    /// <summary>
    /// Creates a new API key. Returns the raw key ONCE — never stored.
    /// </summary>
    Task<(string RawKey, CaaSApiKeyInfo Info)> CreateKeyAsync(
        int partnerId,
        string displayName,
        CaaSEnvironment environment,
        DateTimeOffset? expiresAt,
        int createdByUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Validates an incoming API key. Returns the partner or null if invalid/revoked/expired.
    /// Updates LastUsedAt on success.
    /// </summary>
    Task<ResolvedPartner?> ValidateKeyAsync(
        string rawKey,
        CancellationToken ct = default);

    Task RevokeKeyAsync(
        int partnerId,
        long keyId,
        int revokedByUserId,
        CancellationToken ct = default);

    Task<IReadOnlyList<CaaSApiKeyInfo>> ListKeysAsync(
        int partnerId,
        CancellationToken ct = default);
}

public sealed record CaaSApiKeyInfo(
    long KeyId,
    string KeyPrefix,
    string DisplayName,
    CaaSEnvironment Environment,
    bool IsActive,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset CreatedAt);
```

### 4.3 Rate Limiter

```csharp
public interface ICaaSRateLimiter
{
    /// <summary>
    /// Checks and increments the sliding-window counter for this partner.
    /// Returns true if request is allowed; false if limit exceeded.
    /// </summary>
    Task<RateLimitResult> CheckAndIncrementAsync(
        int partnerId,
        PartnerTier tier,
        CancellationToken ct = default);
}

public sealed record RateLimitResult(
    bool Allowed,
    int Limit,
    int Remaining,
    int RetryAfterSeconds   // 0 if allowed
);
```

### 4.4 Webhook Dispatcher

```csharp
public interface ICaaSWebhookDispatcher
{
    Task EnqueueAsync(
        int partnerId,
        WebhookEventType eventType,
        object payload,
        CancellationToken ct = default);

    /// <summary>
    /// Background worker calls this to dispatch pending webhooks.
    /// </summary>
    Task ProcessPendingAsync(CancellationToken ct = default);
}
```

### 4.5 Core Banking Adapter Factory

```csharp
public interface ICoreBankingAdapterFactory
{
    ICoreBankingAdapter GetAdapter(CoreBankingSystem system);
}

public interface ICoreBankingAdapter
{
    CoreBankingSystem SystemType { get; }

    /// <summary>
    /// Extracts data from the core banking system for a given module and period,
    /// returning a field map keyed by RegOS module field codes.
    /// </summary>
    Task<CoreBankingExtractionResult> ExtractReturnDataAsync(
        string moduleCode,
        string periodCode,
        CoreBankingConnectionConfig config,
        CancellationToken ct = default);

    /// <summary>
    /// Tests connectivity to the core banking system.
    /// </summary>
    Task<ConnectionTestResult> TestConnectionAsync(
        CoreBankingConnectionConfig config,
        CancellationToken ct = default);
}

public sealed record CoreBankingConnectionConfig(
    string SystemType,
    string? BaseUrl,
    string? DatabaseServer,
    string Credential,           // decrypted from Key Vault at runtime
    string FieldMappingJson      // JSON mapping: RegOS field → CB query/field
);

public sealed record CoreBankingExtractionResult(
    bool Success,
    string ModuleCode,
    string PeriodCode,
    Dictionary<string, object?> ExtractedFields,
    IReadOnlyList<string> UnmappedFields,   // fields in module with no CB mapping
    string? ErrorMessage,
    DateTimeOffset ExtractedAt
);

public sealed record ConnectionTestResult(
    bool Success,
    string Message,
    long LatencyMs
);
```

### 4.6 Auto-Filing Scheduler

```csharp
public interface ICaaSAutoFilingService
{
    Task<CaaSAutoFilingRun> ExecuteScheduleAsync(
        int scheduleId,
        CancellationToken ct = default);

    Task<IReadOnlyList<CaaSAutoFilingRun>> GetRunHistoryAsync(
        int partnerId,
        int scheduleId,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
```

---

## 5 · CaaS Service — Full Implementation

```csharp
public sealed class CaaSService : ICaaSService
{
    private readonly IDbConnectionFactory _db;
    private readonly IValidationPipeline _validation;       // RG-11
    private readonly ITemplateEngine _templateEngine;       // RG-07
    private readonly ISubmissionOrchestrator _submission;   // RG-34
    private readonly ICaaSWebhookDispatcher _webhook;
    private readonly ILogger<CaaSService> _log;

    public CaaSService(
        IDbConnectionFactory db,
        IValidationPipeline validation,
        ITemplateEngine templateEngine,
        ISubmissionOrchestrator submission,
        ICaaSWebhookDispatcher webhook,
        ILogger<CaaSService> log)
    {
        _db = db; _validation = validation; _templateEngine = templateEngine;
        _submission = submission; _webhook = webhook; _log = log;
    }

    // ── Validate ────────────────────────────────────────────────────────
    public async Task<CaaSValidateResponse> ValidateAsync(
        ResolvedPartner partner,
        CaaSValidateRequest request,
        Guid requestId,
        CancellationToken ct = default)
    {
        EnsureModuleAllowed(partner, request.ModuleCode);

        // Run RG-11 validation pipeline
        var report = await _validation.ValidateAsync(
            partner.InstitutionId, request.ModuleCode,
            request.PeriodCode, request.Fields, ct);

        var errors = report.Violations
            .Where(v => v.Severity == "ERROR")
            .Select(v => new CaaSFieldError(
                v.FieldCode, v.FieldLabel, v.ErrorCode, v.Message, "ERROR"))
            .ToList();

        var warnings = report.Violations
            .Where(v => v.Severity == "WARNING")
            .Select(v => new CaaSFieldError(
                v.FieldCode, v.FieldLabel, v.ErrorCode, v.Message, "WARNING"))
            .ToList();

        var score = ComputeComplianceScore(errors.Count, warnings.Count, report.TotalFields);

        // Optionally persist a validation session
        string? sessionToken = null;
        if (request.PersistSession)
        {
            sessionToken = await CreateValidationSessionAsync(
                partner.PartnerId, request, report, errors.Count == 0, ct);
        }

        if (errors.Count > 0)
        {
            await _webhook.EnqueueAsync(partner.PartnerId,
                WebhookEventType.ValidationFailed,
                new { requestId, request.ModuleCode, request.PeriodCode,
                      errorCount = errors.Count }, ct);
        }

        return new CaaSValidateResponse(
            IsValid: errors.Count == 0,
            SessionToken: sessionToken,
            ErrorCount: errors.Count,
            WarningCount: warnings.Count,
            Errors: errors,
            Warnings: warnings,
            ComplianceScore: score,
            RequestId: requestId);
    }

    // ── Submit ──────────────────────────────────────────────────────────
    public async Task<CaaSSubmitResponse> SubmitAsync(
        ResolvedPartner partner,
        CaaSSubmitRequest request,
        Guid requestId,
        CancellationToken ct = default)
    {
        Dictionary<string, object?> fields;
        string moduleCode;
        string periodCode;

        if (request.SessionToken is not null)
        {
            // Re-use a persisted validated session
            var session = await GetValidSessionAsync(
                partner.PartnerId, request.SessionToken, ct);

            if (session is null)
                return SubmitFail(requestId, "Session token is invalid or expired.");

            if (session.IsValid != true)
                return SubmitFail(requestId,
                    "Session has validation errors — cannot submit.");

            fields     = System.Text.Json.JsonSerializer
                .Deserialize<Dictionary<string, object?>>(session.SubmittedData)!;
            moduleCode = session.ModuleCode;
            periodCode = session.PeriodCode;
        }
        else
        {
            if (request.ModuleCode is null || request.PeriodCode is null || request.Fields is null)
                return SubmitFail(requestId,
                    "ModuleCode, PeriodCode, and Fields are required when no SessionToken is provided.");

            // Validate inline before submitting
            var validateReq = new CaaSValidateRequest(
                request.ModuleCode, request.PeriodCode, request.Fields, PersistSession: false);
            var validation = await ValidateAsync(partner, validateReq, requestId, ct);

            if (!validation.IsValid)
                return SubmitFail(requestId,
                    $"Validation failed with {validation.ErrorCount} error(s). " +
                    "Use /validate to review errors before submitting.");

            fields     = request.Fields;
            moduleCode = request.ModuleCode;
            periodCode = request.PeriodCode;
        }

        EnsureModuleAllowed(partner, moduleCode);

        // Create a ReturnInstance in RegOS core, then submit via RG-34
        var returnInstanceId = await CreateReturnInstanceAsync(
            partner.InstitutionId, moduleCode, periodCode, fields,
            request.SubmittedByExternalUserId, ct);

        var submissionResult = await _submission.SubmitBatchAsync(
            partner.InstitutionId,
            request.RegulatorCode,
            new[] { returnInstanceId },
            request.SubmittedByExternalUserId,
            ct);

        if (request.SessionToken is not null)
        {
            await MarkSessionConvertedAsync(
                partner.PartnerId, request.SessionToken, returnInstanceId, ct);
        }

        if (submissionResult.Success)
        {
            await _webhook.EnqueueAsync(partner.PartnerId,
                WebhookEventType.FilingCompleted,
                new { requestId, moduleCode, periodCode,
                      batchReference = submissionResult.BatchReference,
                      receiptReference = submissionResult.Receipt?.ReceiptReference,
                      returnInstanceId }, ct);
        }

        return submissionResult.Success
            ? new CaaSSubmitResponse(
                Success: true,
                ReturnInstanceId: returnInstanceId,
                BatchId: submissionResult.BatchId,
                BatchReference: submissionResult.BatchReference,
                ReceiptReference: submissionResult.Receipt?.ReceiptReference,
                ErrorMessage: null,
                RequestId: requestId)
            : SubmitFail(requestId, submissionResult.ErrorMessage ?? "Submission failed.");
    }

    // ── GetTemplate ─────────────────────────────────────────────────────
    public async Task<CaaSTemplateResponse> GetTemplateAsync(
        ResolvedPartner partner,
        string moduleCode,
        Guid requestId,
        CancellationToken ct = default)
    {
        EnsureModuleAllowed(partner, moduleCode);

        var template = await _templateEngine.GetTemplateAsync(
            partner.InstitutionId, moduleCode, ct);

        var fields = template.Fields.Select(f => new CaaSFieldDefinition(
            FieldCode: f.Code,
            FieldLabel: f.Label,
            DataType: f.DataType,
            IsRequired: f.IsRequired,
            ValidationRule: f.ValidationRule,
            MinValue: f.MinValue,
            MaxValue: f.MaxValue,
            Description: f.Description)).ToList();

        var formulas = template.Formulas.Select(f => new CaaSFormula(
            FormulaCode: f.Code,
            Description: f.Description,
            Expression: f.Expression)).ToList();

        return new CaaSTemplateResponse(
            ModuleCode: moduleCode,
            ModuleName: template.ModuleName,
            RegulatorCode: template.RegulatorCode,
            PeriodType: template.PeriodType,
            Fields: fields,
            Formulas: formulas,
            RequestId: requestId);
    }

    // ── GetDeadlines ─────────────────────────────────────────────────────
    public async Task<CaaSDeadlinesResponse> GetDeadlinesAsync(
        ResolvedPartner partner,
        Guid requestId,
        CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        var rows = await conn.QueryAsync<FilingDeadlineRow>(
            """
            SELECT fd.ModuleCode, m.ModuleName, fd.PeriodCode,
                   fd.DeadlineDate, m.RegulatorCode
            FROM   FilingDeadlines fd
            JOIN   ReturnModules m ON m.Code = fd.ModuleCode
            WHERE  fd.DeadlineDate >= CAST(SYSUTCDATETIME() AS DATE)
              AND  fd.DeadlineDate <= DATEADD(DAY, 90, CAST(SYSUTCDATETIME() AS DATE))
              AND  fd.ModuleCode IN (
                       SELECT value FROM OPENJSON(
                           (SELECT AllowedModuleCodes FROM CaaSPartners
                            WHERE Id = @PartnerId)))
            ORDER BY fd.DeadlineDate ASC
            """,
            new { PartnerId = partner.PartnerId });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var deadlines = rows.Select(r =>
        {
            var dl = DateOnly.FromDateTime(r.DeadlineDate);
            return new CaaSDeadline(
                ModuleCode: r.ModuleCode,
                ModuleName: r.ModuleName,
                PeriodCode: r.PeriodCode,
                DeadlineDate: dl,
                DaysRemaining: dl.DayNumber - today.DayNumber,
                IsOverdue: dl < today,
                RegulatorCode: r.RegulatorCode);
        }).ToList();

        // Fire webhook for deadlines within 7 days
        var approaching = deadlines.Where(d => d.DaysRemaining is >= 0 and <= 7).ToList();
        if (approaching.Count > 0)
            await _webhook.EnqueueAsync(partner.PartnerId,
                WebhookEventType.DeadlineApproaching,
                new { deadlines = approaching.Select(d => new
                    { d.ModuleCode, d.PeriodCode, d.DeadlineDate, d.DaysRemaining }) },
                ct);

        return new CaaSDeadlinesResponse(deadlines, requestId);
    }

    // ── GetScore ────────────────────────────────────────────────────────
    public async Task<CaaSScoreResponse> GetScoreAsync(
        ResolvedPartner partner,
        CaaSScoreRequest request,
        Guid requestId,
        CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        var periodFilter = request.PeriodCode ?? GetCurrentPeriodCode();

        var rows = await conn.QueryAsync<ModuleScoreRow>(
            """
            SELECT m.Code              AS ModuleCode,
                   m.ModuleName,
                   COUNT(ri.Id)        AS TotalReturns,
                   SUM(CASE WHEN ri.Status = 'APPROVED' THEN 1 ELSE 0 END) AS Approved,
                   SUM(CASE WHEN ri.Status = 'OVERDUE'  THEN 1 ELSE 0 END) AS Overdue,
                   SUM(CASE WHEN ri.Status = 'PENDING'
                         OR ri.Status = 'DRAFT'          THEN 1 ELSE 0 END) AS Pending,
                   ISNULL(SUM(ve.ErrorCount), 0)                            AS ValidationErrors
            FROM   ReturnModules m
            CROSS JOIN (VALUES (@InstitutionId)) AS I(InstitutionId)
            LEFT JOIN ReturnInstances ri
                   ON ri.ModuleCode = m.Code
                  AND ri.InstitutionId = @InstitutionId
                  AND ri.ReportingPeriod = @Period
            LEFT JOIN (
                SELECT ReturnInstanceId, COUNT(*) AS ErrorCount
                FROM   ValidationResults
                WHERE  Severity = 'ERROR'
                GROUP BY ReturnInstanceId
            ) ve ON ve.ReturnInstanceId = ri.Id
            WHERE  m.Code IN (
                       SELECT value FROM OPENJSON(
                           (SELECT AllowedModuleCodes FROM CaaSPartners WHERE Id = @PartnerId)))
            GROUP BY m.Code, m.ModuleName
            """,
            new { InstitutionId = partner.InstitutionId,
                  Period = periodFilter, PartnerId = partner.PartnerId });

        var moduleScores = rows.Select(r =>
        {
            var score = r.TotalReturns == 0
                ? 100.0
                : Math.Max(0, 100.0 - (r.Overdue * 20) - (r.ValidationErrors * 2) - (r.Pending * 5));
            return new CaaSModuleScore(
                r.ModuleCode, r.ModuleName, Math.Round(score, 1),
                r.Pending, r.Overdue, r.ValidationErrors);
        }).ToList();

        var overall = moduleScores.Count == 0
            ? 100.0
            : Math.Round(moduleScores.Average(s => s.Score), 1);

        var rating = overall switch
        {
            >= 95 => "EXCELLENT",
            >= 80 => "GOOD",
            >= 65 => "SATISFACTORY",
            >= 50 => "NEEDS_ATTENTION",
            _     => "CRITICAL"
        };

        await _webhook.EnqueueAsync(partner.PartnerId, WebhookEventType.ScoreUpdated,
            new { requestId, overall, rating, period = periodFilter }, ct);

        return new CaaSScoreResponse(overall, rating, moduleScores, requestId);
    }

    // ── GetChanges ──────────────────────────────────────────────────────
    public async Task<CaaSChangesResponse> GetChangesAsync(
        ResolvedPartner partner,
        Guid requestId,
        CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        var rows = await conn.QueryAsync<RegulatoryChangeRow>(
            """
            SELECT rc.Id           AS ChangeId,
                   rc.RegulatorCode,
                   rc.ModuleCode,
                   rc.Title,
                   rc.Summary,
                   rc.EffectiveDate,
                   rc.Severity
            FROM   RegulatoryChanges rc
            WHERE  rc.EffectiveDate >= DATEADD(DAY, -30, CAST(SYSUTCDATETIME() AS DATE))
              AND  rc.IsPublished = 1
              AND  rc.ModuleCode IN (
                       SELECT value FROM OPENJSON(
                           (SELECT AllowedModuleCodes FROM CaaSPartners WHERE Id = @PartnerId)))
            ORDER BY rc.EffectiveDate DESC
            """,
            new { PartnerId = partner.PartnerId });

        var changes = rows.Select(r => new CaaSRegulatoryChange(
            ChangeId: r.ChangeId,
            RegulatorCode: r.RegulatorCode,
            ModuleCode: r.ModuleCode,
            Title: r.Title,
            Summary: r.Summary,
            EffectiveDate: DateOnly.FromDateTime(r.EffectiveDate),
            Severity: r.Severity)).ToList();

        if (changes.Any(c => c.Severity == "MAJOR"))
            await _webhook.EnqueueAsync(partner.PartnerId, WebhookEventType.ChangesDetected,
                new { requestId, majorCount = changes.Count(c => c.Severity == "MAJOR"),
                      changes = changes.Where(c => c.Severity == "MAJOR").Take(5) }, ct);

        return new CaaSChangesResponse(changes, requestId);
    }

    // ── Simulate ─────────────────────────────────────────────────────────
    public async Task<CaaSSimulateResponse> SimulateAsync(
        ResolvedPartner partner,
        CaaSSimulateRequest request,
        Guid requestId,
        CancellationToken ct = default)
    {
        EnsureModuleAllowed(partner, request.ModuleCode);

        var results = new List<CaaSScenarioResult>();

        foreach (var scenario in request.Scenarios)
        {
            // Merge base fields with scenario overrides
            var scenarioFields = new Dictionary<string, object?>(request.Fields);
            foreach (var (key, value) in scenario.FieldOverrides)
                scenarioFields[key] = value;

            var report = await _validation.ValidateAsync(
                partner.InstitutionId, request.ModuleCode,
                request.PeriodCode, scenarioFields, ct);

            var errors = report.Violations
                .Where(v => v.Severity == "ERROR")
                .Select(v => new CaaSFieldError(
                    v.FieldCode, v.FieldLabel, v.ErrorCode, v.Message, "ERROR"))
                .ToList();

            var score = ComputeComplianceScore(
                errors.Count,
                report.Violations.Count(v => v.Severity == "WARNING"),
                report.TotalFields);

            results.Add(new CaaSScenarioResult(
                ScenarioName: scenario.ScenarioName,
                IsValid: errors.Count == 0,
                ComplianceScore: score,
                Errors: errors,
                ComputedValues: report.ComputedValues));
        }

        return new CaaSSimulateResponse(results, requestId);
    }

    // ── Private helpers ──────────────────────────────────────────────────
    private static void EnsureModuleAllowed(ResolvedPartner partner, string moduleCode)
    {
        if (!partner.AllowedModuleCodes.Contains(moduleCode))
            throw new CaaSModuleNotEntitledException(
                $"Partner '{partner.PartnerCode}' is not entitled to module '{moduleCode}'.");
    }

    private async Task<string> CreateValidationSessionAsync(
        int partnerId,
        CaaSValidateRequest request,
        ValidationReport report,
        bool isValid,
        CancellationToken ct)
    {
        var token = Convert.ToHexString(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .ToLowerInvariant();

        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO CaaSValidationSessions
                (PartnerId, SessionToken, ModuleCode, PeriodCode,
                 SubmittedData, ValidationResult, IsValid, ExpiresAt)
            VALUES (@PartnerId, @Token, @ModuleCode, @PeriodCode,
                    @Data, @Result, @IsValid,
                    DATEADD(HOUR, 24, SYSUTCDATETIME()))
            """,
            new { PartnerId = partnerId, Token = token,
                  ModuleCode = request.ModuleCode, PeriodCode = request.PeriodCode,
                  Data   = System.Text.Json.JsonSerializer.Serialize(request.Fields),
                  Result = System.Text.Json.JsonSerializer.Serialize(report),
                  IsValid = isValid });

        return token;
    }

    private async Task<ValidationSessionRow?> GetValidSessionAsync(
        int partnerId, string sessionToken, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ValidationSessionRow>(
            """
            SELECT Id, PartnerId, SessionToken, ModuleCode, PeriodCode,
                   SubmittedData, IsValid
            FROM   CaaSValidationSessions
            WHERE  SessionToken = @Token
              AND  PartnerId = @PartnerId
              AND  ExpiresAt > SYSUTCDATETIME()
              AND  ConvertedToReturnId IS NULL
            """,
            new { Token = sessionToken, PartnerId = partnerId });
    }

    private async Task MarkSessionConvertedAsync(
        int partnerId, string sessionToken, long returnInstanceId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE CaaSValidationSessions
            SET    ConvertedToReturnId = @ReturnId, UpdatedAt = SYSUTCDATETIME()
            WHERE  SessionToken = @Token AND PartnerId = @PartnerId
            """,
            new { ReturnId = returnInstanceId, Token = sessionToken, PartnerId = partnerId });
    }

    private async Task<long> CreateReturnInstanceAsync(
        int institutionId, string moduleCode, string periodCode,
        Dictionary<string, object?> fields, int submittedBy, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO ReturnInstances
                (InstitutionId, ModuleCode, ReturnVersion, ReportingPeriod,
                 Status, FieldDataJson, CreatedBy, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@InstitutionId, @ModuleCode, 1, @Period,
                    'APPROVED', @Fields, @CreatedBy, SYSUTCDATETIME())
            """,
            new { InstitutionId = institutionId, ModuleCode = moduleCode,
                  Period = periodCode,
                  Fields = System.Text.Json.JsonSerializer.Serialize(fields),
                  CreatedBy = submittedBy });
    }

    private static double ComputeComplianceScore(int errors, int warnings, int totalFields)
    {
        if (totalFields == 0) return 100.0;
        var deductions = (errors * 10.0 + warnings * 2.0);
        return Math.Max(0, Math.Round(100.0 - (deductions / totalFields * 10), 1));
    }

    private static string GetCurrentPeriodCode()
    {
        var now = DateTime.UtcNow;
        return $"{now.Year}-{now.Month:D2}";
    }

    private static CaaSSubmitResponse SubmitFail(Guid requestId, string error)
        => new(false, null, null, null, null, error, requestId);

    // Row types
    private sealed record FilingDeadlineRow(
        string ModuleCode, string ModuleName, string PeriodCode,
        DateTime DeadlineDate, string RegulatorCode);

    private sealed record ModuleScoreRow(
        string ModuleCode, string ModuleName, int TotalReturns,
        int Approved, int Overdue, int Pending, int ValidationErrors);

    private sealed record RegulatoryChangeRow(
        string ChangeId, string RegulatorCode, string ModuleCode,
        string Title, string Summary, DateTime EffectiveDate, string Severity);

    private sealed record ValidationSessionRow(
        long Id, int PartnerId, string SessionToken,
        string ModuleCode, string PeriodCode, string SubmittedData, bool? IsValid);
}
```

---

## 6 · API Key Service — Full Implementation

```csharp
public sealed class CaaSApiKeyService : ICaaSApiKeyService
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<CaaSApiKeyService> _log;

    public CaaSApiKeyService(IDbConnectionFactory db, ILogger<CaaSApiKeyService> log)
    {
        _db = db; _log = log;
    }

    public async Task<(string RawKey, CaaSApiKeyInfo Info)> CreateKeyAsync(
        int partnerId, string displayName, CaaSEnvironment environment,
        DateTimeOffset? expiresAt, int createdByUserId, CancellationToken ct = default)
    {
        // Generate a cryptographically random API key:
        // Format: regos_{env}_{32 random hex chars}
        // e.g.: regos_live_a3f9b2c1d4e5f6a7b8c9d0e1f2a3b4c5
        var envPrefix = environment == CaaSEnvironment.Live ? "live" : "test";
        var randomBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var rawKey = $"regos_{envPrefix}_{Convert.ToHexString(randomBytes).ToLowerInvariant()}";

        // Store only the SHA-256 hash
        var keyHash = ComputeKeyHash(rawKey);
        var keyPrefix = rawKey[..12];  // "regos_live_a" — safe to display

        await using var conn = await _db.OpenAsync(ct);

        var keyId = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO CaaSApiKeys
                (PartnerId, KeyPrefix, KeyHash, DisplayName, Environment,
                 IsActive, ExpiresAt, CreatedByUserId)
            OUTPUT INSERTED.Id
            VALUES (@PartnerId, @Prefix, @Hash, @Name, @Env,
                    1, @ExpiresAt, @CreatedBy)
            """,
            new { PartnerId = partnerId, Prefix = keyPrefix, Hash = keyHash,
                  Name = displayName, Env = environment.ToString().ToUpperInvariant(),
                  ExpiresAt = expiresAt, CreatedBy = createdByUserId });

        _log.LogInformation(
            "API key created: PartnerId={PartnerId} KeyId={KeyId} Env={Env}",
            partnerId, keyId, environment);

        var info = new CaaSApiKeyInfo(keyId, keyPrefix, displayName,
            environment, true, expiresAt, null, DateTimeOffset.UtcNow);

        return (rawKey, info);
    }

    public async Task<ResolvedPartner?> ValidateKeyAsync(
        string rawKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawKey) || !rawKey.StartsWith("regos_"))
            return null;

        var keyHash = ComputeKeyHash(rawKey);

        await using var conn = await _db.OpenAsync(ct);

        var row = await conn.QuerySingleOrDefaultAsync<ApiKeyValidationRow>(
            """
            SELECT k.Id          AS KeyId,
                   k.PartnerId,
                   k.Environment,
                   k.IsActive,
                   k.ExpiresAt,
                   k.RevokedAt,
                   p.InstitutionId,
                   p.PartnerCode,
                   p.Tier,
                   p.IsActive    AS PartnerIsActive,
                   p.AllowedModuleCodes
            FROM   CaaSApiKeys k
            JOIN   CaaSPartners p ON p.Id = k.PartnerId
            WHERE  k.KeyHash = @Hash
            """,
            new { Hash = keyHash });

        if (row is null) return null;
        if (!row.IsActive || !row.PartnerIsActive) return null;
        if (row.RevokedAt is not null) return null;
        if (row.ExpiresAt is not null && row.ExpiresAt < DateTimeOffset.UtcNow) return null;

        // Update last used (fire-and-forget — don't block the request path)
        _ = conn.ExecuteAsync(
            "UPDATE CaaSApiKeys SET LastUsedAt = SYSUTCDATETIME() WHERE Id = @Id",
            new { Id = row.KeyId });

        var moduleCodes = string.IsNullOrEmpty(row.AllowedModuleCodes)
            ? Array.Empty<string>()
            : System.Text.Json.JsonSerializer
                .Deserialize<string[]>(row.AllowedModuleCodes)!;

        return new ResolvedPartner(
            PartnerId: row.PartnerId,
            PartnerCode: row.PartnerCode,
            InstitutionId: row.InstitutionId,
            Tier: Enum.Parse<PartnerTier>(row.Tier, ignoreCase: true),
            Environment: row.Environment,
            AllowedModuleCodes: moduleCodes);
    }

    public async Task RevokeKeyAsync(
        int partnerId, long keyId, int revokedByUserId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        var affected = await conn.ExecuteAsync(
            """
            UPDATE CaaSApiKeys
            SET    IsActive = 0, RevokedAt = SYSUTCDATETIME(), RevokedByUserId = @UserId
            WHERE  Id = @KeyId AND PartnerId = @PartnerId
            """,
            new { KeyId = keyId, PartnerId = partnerId, UserId = revokedByUserId });

        if (affected == 0)
            throw new KeyNotFoundException(
                $"API key {keyId} not found for partner {partnerId}.");

        _log.LogWarning("API key revoked: KeyId={KeyId} PartnerId={PartnerId}", keyId, partnerId);
    }

    public async Task<IReadOnlyList<CaaSApiKeyInfo>> ListKeysAsync(
        int partnerId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<CaaSApiKeyRow>(
            """
            SELECT Id, KeyPrefix, DisplayName, Environment,
                   IsActive, ExpiresAt, LastUsedAt, CreatedAt
            FROM   CaaSApiKeys
            WHERE  PartnerId = @PartnerId AND RevokedAt IS NULL
            ORDER BY CreatedAt DESC
            """,
            new { PartnerId = partnerId });

        return rows.Select(r => new CaaSApiKeyInfo(
            r.Id, r.KeyPrefix, r.DisplayName,
            Enum.Parse<CaaSEnvironment>(r.Environment, ignoreCase: true),
            r.IsActive, r.ExpiresAt, r.LastUsedAt, r.CreatedAt)).ToList();
    }

    private static string ComputeKeyHash(string rawKey)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(rawKey);
        var hash  = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record ApiKeyValidationRow(
        long KeyId, int PartnerId, string Environment, bool IsActive,
        DateTimeOffset? ExpiresAt, DateTimeOffset? RevokedAt,
        int InstitutionId, string PartnerCode, string Tier,
        bool PartnerIsActive, string? AllowedModuleCodes);

    private sealed record CaaSApiKeyRow(
        long Id, string KeyPrefix, string DisplayName, string Environment,
        bool IsActive, DateTimeOffset? ExpiresAt, DateTimeOffset? LastUsedAt,
        DateTimeOffset CreatedAt);
}
```

---

## 7 · Redis Rate Limiter — Full Implementation

```csharp
/// <summary>
/// Sliding-window rate limiter backed by Redis. Uses a Lua script for atomicity.
/// Window = 60 seconds. Counter key: caas:rl:{partnerId}:{window_bucket}
/// </summary>
public sealed class CaaSRedisRateLimiter : ICaaSRateLimiter
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<CaaSRedisRateLimiter> _log;

    // Lua script: KEYS[1]=counter key, ARGV[1]=limit, ARGV[2]=window_seconds
    // Returns: {current_count, limit, is_allowed (1/0)}
    private const string SlidingWindowScript = """
        local key = KEYS[1]
        local limit = tonumber(ARGV[1])
        local window = tonumber(ARGV[2])
        local now = tonumber(ARGV[3])
        local window_start = now - window

        -- Remove expired entries
        redis.call('ZREMRANGEBYSCORE', key, '-inf', window_start)

        -- Count current entries
        local count = redis.call('ZCARD', key)

        if count < limit then
            -- Add this request
            redis.call('ZADD', key, now, now .. ':' .. math.random(1000000))
            redis.call('EXPIRE', key, window + 1)
            return {count + 1, limit, 1}
        else
            return {count, limit, 0}
        end
        """;

    public CaaSRedisRateLimiter(
        IConnectionMultiplexer redis,
        ILogger<CaaSRedisRateLimiter> log)
    {
        _redis = redis;
        _log = log;
    }

    public async Task<RateLimitResult> CheckAndIncrementAsync(
        int partnerId, PartnerTier tier, CancellationToken ct = default)
    {
        var limit = RateLimitThresholds.GetRequestsPerMinute(tier);
        const int windowSeconds = 60;

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var key = $"caas:rl:{partnerId}:{nowMs / (windowSeconds * 1000)}";

        try
        {
            var db = _redis.GetDatabase();
            var result = (RedisValue[])await db.ScriptEvaluateAsync(
                SlidingWindowScript,
                new RedisKey[] { key },
                new RedisValue[] { limit, windowSeconds, nowMs });

            var current = (int)result[0];
            var allowed = (int)result[2] == 1;

            if (!allowed)
            {
                _log.LogWarning(
                    "Rate limit exceeded: PartnerId={PartnerId} Tier={Tier} " +
                    "Count={Count} Limit={Limit}",
                    partnerId, tier, current, limit);
            }

            return new RateLimitResult(
                Allowed: allowed,
                Limit: limit,
                Remaining: Math.Max(0, limit - current),
                RetryAfterSeconds: allowed ? 0 : windowSeconds);
        }
        catch (Exception ex)
        {
            // Redis unavailable — fail open (allow request, log error)
            _log.LogError(ex, "Redis rate limiter unavailable — failing open for partner {PartnerId}",
                partnerId);
            return new RateLimitResult(true, limit, limit, 0);
        }
    }
}
```

---

## 8 · Webhook Dispatcher — Full Implementation

```csharp
public sealed class CaaSWebhookDispatcher : ICaaSWebhookDispatcher
{
    private readonly IDbConnectionFactory _db;
    private readonly HttpClient _httpClient;
    private readonly ILogger<CaaSWebhookDispatcher> _log;

    public CaaSWebhookDispatcher(
        IDbConnectionFactory db,
        IHttpClientFactory httpFactory,
        ILogger<CaaSWebhookDispatcher> log)
    {
        _db = db;
        _httpClient = httpFactory.CreateClient("Webhook");
        _log = log;
    }

    public async Task EnqueueAsync(
        int partnerId, WebhookEventType eventType,
        object payload, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        // Load partner webhook config
        var partner = await conn.QuerySingleOrDefaultAsync<WebhookConfigRow>(
            """
            SELECT WebhookUrl, WebhookSecret
            FROM   CaaSPartners
            WHERE  Id = @PartnerId AND IsActive = 1 AND WebhookUrl IS NOT NULL
            """,
            new { PartnerId = partnerId });

        if (partner is null) return;  // No webhook configured — silently skip

        var payloadJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            event_type = ToSnakeCase(eventType.ToString()),
            occurred_at = DateTimeOffset.UtcNow.ToString("o"),
            data = payload
        });

        var hmac = ComputeHmac(payloadJson, partner.WebhookSecret!);

        await conn.ExecuteAsync(
            """
            INSERT INTO CaaSWebhookDeliveries
                (PartnerId, EventType, Payload, HmacSignature,
                 Status, NextRetryAt)
            VALUES (@PartnerId, @EventType, @Payload, @Hmac,
                    'PENDING', SYSUTCDATETIME())
            """,
            new { PartnerId = partnerId,
                  EventType = ToSnakeCase(eventType.ToString()),
                  Payload = payloadJson, Hmac = hmac });
    }

    public async Task ProcessPendingAsync(CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        // Fetch up to 50 due deliveries
        var deliveries = await conn.QueryAsync<WebhookDeliveryRow>(
            """
            SELECT TOP 50
                   wd.Id, wd.PartnerId, wd.EventType, wd.Payload,
                   wd.HmacSignature, wd.AttemptCount, wd.MaxAttempts,
                   p.WebhookUrl
            FROM   CaaSWebhookDeliveries wd
            JOIN   CaaSPartners p ON p.Id = wd.PartnerId
            WHERE  wd.Status = 'PENDING'
              AND  (wd.NextRetryAt IS NULL OR wd.NextRetryAt <= SYSUTCDATETIME())
            ORDER BY wd.CreatedAt ASC
            """);

        foreach (var delivery in deliveries)
        {
            await DispatchDeliveryAsync(conn, delivery, ct);
        }
    }

    private async Task DispatchDeliveryAsync(
        System.Data.IDbConnection conn,
        WebhookDeliveryRow delivery,
        CancellationToken ct)
    {
        var attempt = delivery.AttemptCount + 1;

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, delivery.WebhookUrl)
            {
                Content = new StringContent(
                    delivery.Payload, System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-RegOS-Event", delivery.EventType);
            request.Headers.Add("X-RegOS-Signature", $"sha256={delivery.HmacSignature}");
            request.Headers.Add("X-RegOS-Delivery", delivery.Id.ToString());
            request.Headers.Add("X-RegOS-Attempt", attempt.ToString());

            var response = await _httpClient.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                await conn.ExecuteAsync(
                    """
                    UPDATE CaaSWebhookDeliveries
                    SET    Status = 'DELIVERED', DeliveredAt = SYSUTCDATETIME(),
                           AttemptCount = @Attempt, LastHttpStatus = @Status
                    WHERE  Id = @Id
                    """,
                    new { Attempt = attempt, Status = (int)response.StatusCode, Id = delivery.Id });

                _log.LogInformation(
                    "Webhook delivered: DeliveryId={Id} Partner={PartnerId} Event={Event}",
                    delivery.Id, delivery.PartnerId, delivery.EventType);
            }
            else
            {
                await HandleFailedAttemptAsync(conn, delivery, attempt,
                    (int)response.StatusCode, $"HTTP {(int)response.StatusCode}", ct);
            }
        }
        catch (Exception ex)
        {
            await HandleFailedAttemptAsync(conn, delivery, attempt, null, ex.Message, ct);
        }
    }

    private static async Task HandleFailedAttemptAsync(
        System.Data.IDbConnection conn,
        WebhookDeliveryRow delivery,
        int attempt,
        int? httpStatus,
        string errorMessage,
        CancellationToken ct)
    {
        bool isDead = attempt >= delivery.MaxAttempts;

        // Exponential back-off: 30s, 5m, 30m, 2h, 8h
        var delaySeconds = (int)Math.Pow(2, attempt) * 30;
        var nextRetry = isDead
            ? (DateTime?)null
            : DateTime.UtcNow.AddSeconds(delaySeconds);

        await conn.ExecuteAsync(
            """
            UPDATE CaaSWebhookDeliveries
            SET    AttemptCount = @Attempt,
                   LastAttemptAt = SYSUTCDATETIME(),
                   LastHttpStatus = @HttpStatus,
                   LastErrorMessage = @Error,
                   Status = @Status,
                   NextRetryAt = @NextRetry
            WHERE  Id = @Id
            """,
            new { Attempt = attempt, HttpStatus = httpStatus, Error = errorMessage,
                  Status = isDead ? "DEAD_LETTER" : "PENDING",
                  NextRetry = nextRetry, Id = delivery.Id });
    }

    private static string ComputeHmac(string payload, string secret)
    {
        var key  = System.Text.Encoding.UTF8.GetBytes(secret);
        var data = System.Text.Encoding.UTF8.GetBytes(payload);
        using var hmac = new System.Security.Cryptography.HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(data)).ToLowerInvariant();
    }

    private static string ToSnakeCase(string input)
    {
        // "FilingCompleted" → "filing.completed"
        var result = System.Text.RegularExpressions.Regex
            .Replace(input, "([a-z])([A-Z])", "$1.$2")
            .ToLowerInvariant();
        return result;
    }

    private sealed record WebhookConfigRow(string? WebhookUrl, string? WebhookSecret);
    private sealed record WebhookDeliveryRow(
        long Id, int PartnerId, string EventType, string Payload,
        string HmacSignature, int AttemptCount, int MaxAttempts, string WebhookUrl);
}
```

---

## 9 · Core Banking Adapters — Full Implementations

### 9.1 Finacle Adapter (Infosys)

```csharp
public sealed class FinacleCoreBankingAdapter : ICoreBankingAdapter
{
    public CoreBankingSystem SystemType => CoreBankingSystem.Finacle;

    private readonly IHttpClientFactory _httpFactory;

    public FinacleCoreBankingAdapter(IHttpClientFactory httpFactory)
        => _httpFactory = httpFactory;

    public async Task<CoreBankingExtractionResult> ExtractReturnDataAsync(
        string moduleCode, string periodCode,
        CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        var mapping = System.Text.Json.JsonSerializer
            .Deserialize<Dictionary<string, string>>(config.FieldMappingJson)
            ?? throw new InvalidOperationException("Invalid Finacle field mapping JSON.");

        var (year, month) = ParsePeriodCode(periodCode);

        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri(config.BaseUrl!);
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", config.Credential);

        var extractedFields = new Dictionary<string, object?>();
        var unmapped = new List<string>();

        // Finacle EAI (Enterprise Application Integration) API calls
        // Each mapped field is fetched via a GL balance or report API
        foreach (var (regoFieldCode, finacleQuery) in mapping)
        {
            try
            {
                // finacleQuery format: "GL_BALANCE:{glCode}" or "REPORT:{reportCode}:{column}"
                var value = await FetchFinacleValueAsync(http, finacleQuery, year, month, ct);
                extractedFields[regoFieldCode] = value;
            }
            catch (Exception ex)
            {
                extractedFields[regoFieldCode] = null;
                unmapped.Add($"{regoFieldCode} (error: {ex.Message})");
            }
        }

        return new CoreBankingExtractionResult(
            Success: true,
            ModuleCode: moduleCode,
            PeriodCode: periodCode,
            ExtractedFields: extractedFields,
            UnmappedFields: unmapped,
            ErrorMessage: null,
            ExtractedAt: DateTimeOffset.UtcNow);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(
        CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var http = _httpFactory.CreateClient();
            http.BaseAddress = new Uri(config.BaseUrl!);
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", config.Credential);

            var response = await http.GetAsync("/eai/api/v1/health", ct);
            sw.Stop();

            return response.IsSuccessStatusCode
                ? new ConnectionTestResult(true, "Connected successfully.", sw.ElapsedMilliseconds)
                : new ConnectionTestResult(false,
                    $"Health check returned HTTP {(int)response.StatusCode}.",
                    sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectionTestResult(false, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static async Task<object?> FetchFinacleValueAsync(
        HttpClient http, string query, int year, int month, CancellationToken ct)
    {
        var parts = query.Split(':');

        if (parts[0] == "GL_BALANCE")
        {
            // GET /eai/api/v1/gl/balance?glCode={glCode}&year={y}&month={m}
            var glCode = parts[1];
            var resp = await http.GetAsync(
                $"/eai/api/v1/gl/balance?glCode={Uri.EscapeDataString(glCode)}" +
                $"&year={year}&month={month:D2}", ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("closingBalance").GetDecimal();
        }

        if (parts[0] == "REPORT")
        {
            // GET /eai/api/v1/reports/{reportCode}/data?year={y}&month={m}&column={col}
            var reportCode = parts[1];
            var column     = parts[2];
            var resp = await http.GetAsync(
                $"/eai/api/v1/reports/{Uri.EscapeDataString(reportCode)}/data" +
                $"?year={year}&month={month:D2}&column={Uri.EscapeDataString(column)}", ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("value").GetDecimal();
        }

        throw new InvalidOperationException($"Unknown Finacle query type: {parts[0]}");
    }

    private static (int Year, int Month) ParsePeriodCode(string periodCode)
    {
        // Accepts: "2026-03" (monthly) or "2026-Q1" (quarterly → first month)
        if (periodCode.Contains("-Q"))
        {
            var year    = int.Parse(periodCode[..4]);
            var quarter = int.Parse(periodCode[6..]);
            return (year, (quarter - 1) * 3 + 1);
        }
        var parts = periodCode.Split('-');
        return (int.Parse(parts[0]), int.Parse(parts[1]));
    }
}
```

### 9.2 T24/Transact Adapter (Temenos)

```csharp
public sealed class T24CoreBankingAdapter : ICoreBankingAdapter
{
    public CoreBankingSystem SystemType => CoreBankingSystem.T24;

    private readonly IHttpClientFactory _httpFactory;

    public T24CoreBankingAdapter(IHttpClientFactory httpFactory)
        => _httpFactory = httpFactory;

    public async Task<CoreBankingExtractionResult> ExtractReturnDataAsync(
        string moduleCode, string periodCode,
        CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        var mapping = System.Text.Json.JsonSerializer
            .Deserialize<Dictionary<string, string>>(config.FieldMappingJson)!;

        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri(config.BaseUrl!);
        http.DefaultRequestHeaders.Add("Authorization",
            $"Basic {Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(config.Credential))}");

        var extractedFields = new Dictionary<string, object?>();
        var unmapped = new List<string>();

        // T24 OFS (Open Financial Services) message format
        // or T24 Transact REST API
        foreach (var (regoFieldCode, t24Query) in mapping)
        {
            try
            {
                // t24Query format: "ENQUIRY:{enquiryName}:{fieldName}"
                //               or "VERSION:{versionName}:{fieldName}"
                var value = await FetchT24ValueAsync(http, t24Query, periodCode, ct);
                extractedFields[regoFieldCode] = value;
            }
            catch (Exception ex)
            {
                extractedFields[regoFieldCode] = null;
                unmapped.Add($"{regoFieldCode}: {ex.Message}");
            }
        }

        return new CoreBankingExtractionResult(
            true, moduleCode, periodCode, extractedFields,
            unmapped, null, DateTimeOffset.UtcNow);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(
        CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var http = _httpFactory.CreateClient();
            http.BaseAddress = new Uri(config.BaseUrl!);
            http.DefaultRequestHeaders.Add("Authorization",
                $"Basic {Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes(config.Credential))}");

            var resp = await http.GetAsync("/T24/api/v1.0.0/meta/ping", ct);
            sw.Stop();
            return resp.IsSuccessStatusCode
                ? new ConnectionTestResult(true, "T24 Transact API reachable.", sw.ElapsedMilliseconds)
                : new ConnectionTestResult(false, $"HTTP {(int)resp.StatusCode}", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectionTestResult(false, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static async Task<object?> FetchT24ValueAsync(
        HttpClient http, string query, string periodCode, CancellationToken ct)
    {
        var parts = query.Split(':');
        if (parts[0] == "ENQUIRY")
        {
            var enquiryName = parts[1];
            var fieldName   = parts[2];
            // T24 Transact REST: GET /T24/api/v1.0.0/enquiry/{name}?period={period}
            var resp = await http.GetAsync(
                $"/T24/api/v1.0.0/enquiry/{Uri.EscapeDataString(enquiryName)}" +
                $"?period={Uri.EscapeDataString(periodCode)}", ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("body")[0]
                .GetProperty(fieldName)
                .GetDecimal();
        }
        throw new InvalidOperationException($"Unknown T24 query type: {parts[0]}");
    }
}
```

### 9.3 BankOne Adapter

```csharp
public sealed class BankOneCoreBankingAdapter : ICoreBankingAdapter
{
    public CoreBankingSystem SystemType => CoreBankingSystem.BankOne;

    private readonly IHttpClientFactory _httpFactory;

    public BankOneCoreBankingAdapter(IHttpClientFactory httpFactory)
        => _httpFactory = httpFactory;

    public async Task<CoreBankingExtractionResult> ExtractReturnDataAsync(
        string moduleCode, string periodCode,
        CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        var mapping = System.Text.Json.JsonSerializer
            .Deserialize<Dictionary<string, string>>(config.FieldMappingJson)!;

        // BankOne exposes a REST API with institutionCode + auth token
        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri(config.BaseUrl!);
        http.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", config.Credential);

        var extractedFields = new Dictionary<string, object?>();
        var unmapped = new List<string>();

        foreach (var (regoFieldCode, bankOneEndpoint) in mapping)
        {
            try
            {
                // bankOneEndpoint format: "ACCOUNT_SUMMARY:{productType}" or "GL:{accountCode}"
                var value = await FetchBankOneValueAsync(http, bankOneEndpoint, periodCode, ct);
                extractedFields[regoFieldCode] = value;
            }
            catch (Exception ex)
            {
                extractedFields[regoFieldCode] = null;
                unmapped.Add($"{regoFieldCode}: {ex.Message}");
            }
        }

        return new CoreBankingExtractionResult(
            true, moduleCode, periodCode, extractedFields,
            unmapped, null, DateTimeOffset.UtcNow);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(
        CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var http = _httpFactory.CreateClient();
            http.BaseAddress = new Uri(config.BaseUrl!);
            http.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", config.Credential);

            var resp = await http.GetAsync("/BankOneWebAPI/api/v3/AccountEnquiry/Ping", ct);
            sw.Stop();
            return resp.IsSuccessStatusCode
                ? new ConnectionTestResult(true, "BankOne API reachable.", sw.ElapsedMilliseconds)
                : new ConnectionTestResult(false, $"HTTP {(int)resp.StatusCode}", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectionTestResult(false, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static async Task<object?> FetchBankOneValueAsync(
        HttpClient http, string query, string periodCode, CancellationToken ct)
    {
        var parts = query.Split(':');
        if (parts[0] == "GL")
        {
            var accountCode = parts[1];
            var resp = await http.GetAsync(
                $"/BankOneWebAPI/api/v3/Reports/GLBalance" +
                $"?accountCode={Uri.EscapeDataString(accountCode)}" +
                $"&period={Uri.EscapeDataString(periodCode)}", ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("data").GetProperty("balance").GetDecimal();
        }
        throw new InvalidOperationException($"Unknown BankOne query type: {parts[0]}");
    }
}
```

### 9.4 Flexcube Adapter (Oracle)

```csharp
public sealed class FlexcubeCoreBankingAdapter : ICoreBankingAdapter
{
    public CoreBankingSystem SystemType => CoreBankingSystem.Flexcube;

    private readonly IHttpClientFactory _httpFactory;

    public FlexcubeCoreBankingAdapter(IHttpClientFactory httpFactory)
        => _httpFactory = httpFactory;

    public async Task<CoreBankingExtractionResult> ExtractReturnDataAsync(
        string moduleCode, string periodCode,
        CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        var mapping = System.Text.Json.JsonSerializer
            .Deserialize<Dictionary<string, string>>(config.FieldMappingJson)!;

        // Oracle Flexcube Universal Banking REST APIs
        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri(config.BaseUrl!);
        http.DefaultRequestHeaders.Add("appId", "FCUBS");
        http.DefaultRequestHeaders.Add("branchCode", "001");
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", config.Credential);

        var extractedFields = new Dictionary<string, object?>();
        var unmapped = new List<string>();

        foreach (var (regoFieldCode, fcQuery) in mapping)
        {
            try
            {
                // fcQuery: "GL_INQUIRY:{glCode}" or "REPORT:{reportId}:{fieldId}"
                var value = await FetchFlexcubeValueAsync(http, fcQuery, periodCode, ct);
                extractedFields[regoFieldCode] = value;
            }
            catch (Exception ex)
            {
                extractedFields[regoFieldCode] = null;
                unmapped.Add($"{regoFieldCode}: {ex.Message}");
            }
        }

        return new CoreBankingExtractionResult(
            true, moduleCode, periodCode, extractedFields,
            unmapped, null, DateTimeOffset.UtcNow);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(
        CoreBankingConnectionConfig config, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var http = _httpFactory.CreateClient();
            http.BaseAddress = new Uri(config.BaseUrl!);
            http.DefaultRequestHeaders.Add("appId", "FCUBS");
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", config.Credential);

            var resp = await http.GetAsync("/service/FCUBS/STTM/BANK/SUMMARY", ct);
            sw.Stop();
            return resp.IsSuccessStatusCode
                ? new ConnectionTestResult(true, "Flexcube API reachable.", sw.ElapsedMilliseconds)
                : new ConnectionTestResult(false, $"HTTP {(int)resp.StatusCode}", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectionTestResult(false, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static async Task<object?> FetchFlexcubeValueAsync(
        HttpClient http, string query, string periodCode, CancellationToken ct)
    {
        var parts = query.Split(':');
        if (parts[0] == "GL_INQUIRY")
        {
            var glCode = parts[1];
            var resp = await http.GetAsync(
                $"/service/FCUBS/GLTM/GLAES/GLMIS_QUERY" +
                $"?gl_code={Uri.EscapeDataString(glCode)}&period={Uri.EscapeDataString(periodCode)}", ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("fcubs-body")
                .GetProperty("Glaes-Details")[0]
                .GetProperty("ACY_AVG_BALANCE")
                .GetDecimal();
        }
        throw new InvalidOperationException($"Unknown Flexcube query type: {parts[0]}");
    }
}

public sealed class CoreBankingAdapterFactory : ICoreBankingAdapterFactory
{
    private readonly IReadOnlyDictionary<CoreBankingSystem, ICoreBankingAdapter> _adapters;

    public CoreBankingAdapterFactory(IEnumerable<ICoreBankingAdapter> adapters)
        => _adapters = adapters.ToDictionary(a => a.SystemType);

    public ICoreBankingAdapter GetAdapter(CoreBankingSystem system)
        => _adapters.TryGetValue(system, out var adapter)
            ? adapter
            : throw new InvalidOperationException($"No adapter registered for {system}.");
}
```

---

## 10 · Auto-Filing Service — Full Implementation

```csharp
public sealed class CaaSAutoFilingService : ICaaSAutoFilingService
{
    private readonly IDbConnectionFactory _db;
    private readonly ICoreBankingAdapterFactory _cbFactory;
    private readonly ICaaSService _caas;
    private readonly ISubmissionOrchestrator _submission;
    private readonly ICaaSWebhookDispatcher _webhook;
    private readonly SecretClient _secretClient;
    private readonly ILogger<CaaSAutoFilingService> _log;

    public CaaSAutoFilingService(
        IDbConnectionFactory db,
        ICoreBankingAdapterFactory cbFactory,
        ICaaSService caas,
        ISubmissionOrchestrator submission,
        ICaaSWebhookDispatcher webhook,
        SecretClient secretClient,
        ILogger<CaaSAutoFilingService> log)
    {
        _db = db; _cbFactory = cbFactory; _caas = caas;
        _submission = submission; _webhook = webhook;
        _secretClient = secretClient; _log = log;
    }

    public async Task<CaaSAutoFilingRun> ExecuteScheduleAsync(
        int scheduleId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        var schedule = await conn.QuerySingleOrDefaultAsync<AutoFilingScheduleRow>(
            """
            SELECT s.Id, s.PartnerId, s.ModuleCode, s.CoreBankingConnectionId,
                   s.AutoSubmitIfClean, s.NotifyEmails, s.CronExpression,
                   c.SystemType, c.BaseUrl, c.DatabaseServer,
                   c.CredentialSecretName, c.FieldMappingJson,
                   p.InstitutionId, p.PartnerCode, p.Tier,
                   p.AllowedModuleCodes, p.WebhookUrl
            FROM   CaaSAutoFilingSchedules s
            JOIN   CaaSCoreBankingConnections c ON c.Id = s.CoreBankingConnectionId
            JOIN   CaaSPartners p ON p.Id = s.PartnerId
            WHERE  s.Id = @ScheduleId AND s.IsActive = 1
            """,
            new { ScheduleId = scheduleId });

        if (schedule is null)
            throw new KeyNotFoundException($"Schedule {scheduleId} not found or inactive.");

        var periodCode = DerivePeriodCode(schedule.CronExpression);

        // Insert run record
        var runId = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO CaaSAutoFilingRuns
                (ScheduleId, PartnerId, ModuleCode, PeriodCode, Phase)
            OUTPUT INSERTED.Id
            VALUES (@ScheduleId, @PartnerId, @ModuleCode, @Period, 'EXTRACT')
            """,
            new { ScheduleId = scheduleId, PartnerId = schedule.PartnerId,
                  ModuleCode = schedule.ModuleCode, Period = periodCode });

        _log.LogInformation(
            "Auto-filing run started: RunId={RunId} Schedule={ScheduleId} " +
            "Module={Module} Period={Period}",
            runId, scheduleId, schedule.ModuleCode, periodCode);

        // ── Phase 1: Extract ─────────────────────────────────────────────
        CoreBankingExtractionResult extraction;
        try
        {
            var credential = await _secretClient.GetSecretAsync(
                schedule.CredentialSecretName, cancellationToken: ct);

            var cbConfig = new CoreBankingConnectionConfig(
                SystemType: schedule.SystemType,
                BaseUrl: schedule.BaseUrl,
                DatabaseServer: schedule.DatabaseServer,
                Credential: credential.Value.Value,
                FieldMappingJson: schedule.FieldMappingJson);

            var cbSystem = Enum.Parse<CoreBankingSystem>(schedule.SystemType, ignoreCase: true);
            var adapter  = _cbFactory.GetAdapter(cbSystem);

            extraction = await adapter.ExtractReturnDataAsync(
                schedule.ModuleCode, periodCode, cbConfig, ct);

            await conn.ExecuteAsync(
                "UPDATE CaaSAutoFilingRuns SET Phase='VALIDATE' WHERE Id=@Id",
                new { Id = runId });

            await _webhook.EnqueueAsync(schedule.PartnerId,
                WebhookEventType.ExtractionCompleted,
                new { runId, schedule.ModuleCode, periodCode,
                      fieldCount = extraction.ExtractedFields.Count,
                      unmappedCount = extraction.UnmappedFields.Count }, ct);
        }
        catch (Exception ex)
        {
            await FailRun(conn, runId, scheduleId, $"Extraction failed: {ex.Message}", ct);
            return await GetRunAsync(conn, runId);
        }

        // ── Phase 2: Validate ────────────────────────────────────────────
        var partner = new ResolvedPartner(
            PartnerId: schedule.PartnerId,
            PartnerCode: schedule.PartnerCode,
            InstitutionId: schedule.InstitutionId,
            Tier: Enum.Parse<PartnerTier>(schedule.Tier, ignoreCase: true),
            Environment: "LIVE",
            AllowedModuleCodes: System.Text.Json.JsonSerializer
                .Deserialize<string[]>(schedule.AllowedModuleCodes ?? "[]")!);

        var requestId = Guid.NewGuid();
        var validateReq = new CaaSValidateRequest(
            ModuleCode: schedule.ModuleCode,
            PeriodCode: periodCode,
            Fields: extraction.ExtractedFields,
            PersistSession: true);

        CaaSValidateResponse validation;
        try
        {
            validation = await _caas.ValidateAsync(partner, validateReq, requestId, ct);

            await conn.ExecuteAsync(
                """
                UPDATE CaaSAutoFilingRuns
                SET    Phase = @Phase, ValidationSessionId = @SessionId, IsClean = @IsClean
                WHERE  Id = @Id
                """,
                new { Phase = validation.IsValid ? "SUBMIT" : "FAILED",
                      SessionId = (object?)null,  // session stored in CaaSValidationSessions
                      IsClean = validation.IsValid, Id = runId });
        }
        catch (Exception ex)
        {
            await FailRun(conn, runId, scheduleId, $"Validation error: {ex.Message}", ct);
            return await GetRunAsync(conn, runId);
        }

        if (!validation.IsValid)
        {
            // Hold — notify compliance officer
            await conn.ExecuteAsync(
                """
                UPDATE CaaSAutoFilingRuns
                SET    Phase='FAILED', ErrorMessage=@Error, CompletedAt=SYSUTCDATETIME()
                WHERE  Id=@Id
                """,
                new { Error = $"{validation.ErrorCount} validation error(s). " +
                              "Return held pending correction.",
                      Id = runId });

            await _webhook.EnqueueAsync(schedule.PartnerId,
                WebhookEventType.AutoFilingHeld,
                new { runId, schedule.ModuleCode, periodCode,
                      errorCount = validation.ErrorCount,
                      errors = validation.Errors.Take(10),
                      notifyEmails = schedule.NotifyEmails }, ct);

            _log.LogWarning(
                "Auto-filing held: RunId={RunId} Errors={Count}",
                runId, validation.ErrorCount);

            return await GetRunAsync(conn, runId);
        }

        // ── Phase 3: Submit (only if AutoSubmitIfClean = true) ───────────
        if (!schedule.AutoSubmitIfClean)
        {
            await conn.ExecuteAsync(
                """
                UPDATE CaaSAutoFilingRuns
                SET    Phase='COMPLETE', CompletedAt=SYSUTCDATETIME()
                WHERE  Id=@Id
                """,
                new { Id = runId });

            _log.LogInformation(
                "Auto-filing extracted & validated (manual submit required): RunId={RunId}",
                runId);
            return await GetRunAsync(conn, runId);
        }

        try
        {
            var submitReq = new CaaSSubmitRequest(
                SessionToken: validation.SessionToken,
                ModuleCode: null, PeriodCode: null, Fields: null,
                RegulatorCode: await GetRegulatorCodeAsync(conn, schedule.ModuleCode, ct),
                SubmittedByExternalUserId: 0);  // system submission

            var submitResult = await _caas.SubmitAsync(partner, submitReq, requestId, ct);

            await conn.ExecuteAsync(
                """
                UPDATE CaaSAutoFilingRuns
                SET    Phase='COMPLETE', ReturnInstanceId=@ReturnId,
                       BatchId=@BatchId, CompletedAt=SYSUTCDATETIME()
                WHERE  Id=@Id
                """,
                new { ReturnId = submitResult.ReturnInstanceId,
                      BatchId  = submitResult.BatchId, Id = runId });

            await conn.ExecuteAsync(
                """
                UPDATE CaaSAutoFilingSchedules
                SET    LastRunAt=SYSUTCDATETIME(), LastRunStatus='SUCCESS',
                       NextRunAt=@NextRun
                WHERE  Id=@ScheduleId
                """,
                new { NextRun = ComputeNextRun(schedule.CronExpression), ScheduleId = scheduleId });

            _log.LogInformation(
                "Auto-filing complete: RunId={RunId} BatchRef={BatchRef}",
                runId, submitResult.BatchReference);
        }
        catch (Exception ex)
        {
            await FailRun(conn, runId, scheduleId, $"Submission failed: {ex.Message}", ct);
        }

        return await GetRunAsync(conn, runId);
    }

    public async Task<IReadOnlyList<CaaSAutoFilingRun>> GetRunHistoryAsync(
        int partnerId, int scheduleId, int page, int pageSize, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<CaaSAutoFilingRun>(
            """
            SELECT Id, ScheduleId, PartnerId, ModuleCode, PeriodCode,
                   Phase, ValidationSessionId, ReturnInstanceId, BatchId,
                   IsClean, ErrorMessage, StartedAt, CompletedAt
            FROM   CaaSAutoFilingRuns
            WHERE  ScheduleId = @ScheduleId AND PartnerId = @PartnerId
            ORDER BY StartedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """,
            new { ScheduleId = scheduleId, PartnerId = partnerId,
                  Offset = (page - 1) * pageSize, PageSize = pageSize });
        return rows.ToList();
    }

    // ── Helpers ──────────────────────────────────────────────────────────
    private static async Task FailRun(
        System.Data.IDbConnection conn, long runId, int scheduleId,
        string error, CancellationToken ct)
    {
        await conn.ExecuteAsync(
            """
            UPDATE CaaSAutoFilingRuns
            SET    Phase='FAILED', ErrorMessage=@Error, CompletedAt=SYSUTCDATETIME()
            WHERE  Id=@Id
            """,
            new { Error = error, Id = runId });

        await conn.ExecuteAsync(
            "UPDATE CaaSAutoFilingSchedules SET LastRunStatus='FAILED' WHERE Id=@Id",
            new { Id = scheduleId });
    }

    private static async Task<CaaSAutoFilingRun> GetRunAsync(
        System.Data.IDbConnection conn, long runId)
        => await conn.QuerySingleAsync<CaaSAutoFilingRun>(
            "SELECT * FROM CaaSAutoFilingRuns WHERE Id=@Id", new { Id = runId });

    private static async Task<string> GetRegulatorCodeAsync(
        System.Data.IDbConnection conn, string moduleCode, CancellationToken ct)
        => await conn.ExecuteScalarAsync<string>(
               "SELECT RegulatorCode FROM ReturnModules WHERE Code=@Code",
               new { Code = moduleCode })
           ?? throw new InvalidOperationException($"Module {moduleCode} not found.");

    private static string DerivePeriodCode(string cronExpression)
    {
        // Simple heuristic: monthly cron → previous month's period code
        var now = DateTime.UtcNow.AddMonths(-1);
        return $"{now.Year}-{now.Month:D2}";
    }

    private static DateTime ComputeNextRun(string cronExpression)
    {
        // Use Cronos library for production cron parsing
        var schedule = Cronos.CronExpression.Parse(cronExpression);
        return schedule.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc)
            ?? DateTime.UtcNow.AddMonths(1);
    }

    private sealed record AutoFilingScheduleRow(
        int Id, int PartnerId, string ModuleCode, int CoreBankingConnectionId,
        bool AutoSubmitIfClean, string? NotifyEmails, string CronExpression,
        string SystemType, string? BaseUrl, string? DatabaseServer,
        string CredentialSecretName, string FieldMappingJson,
        int InstitutionId, string PartnerCode, string Tier,
        string? AllowedModuleCodes, string? WebhookUrl);
}
```

---

## 11 · ASP.NET Minimal API Endpoints

```csharp
public static class CaaSEndpoints
{
    public static WebApplication MapCaaSEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/caas")
            .RequireCaaSAuth()           // custom middleware resolves partner from API key
            .WithTags("CaaS API");

        group.MapPost("/validate", ValidateAsync)
            .WithName("CaaS-Validate")
            .WithSummary("Validate data against a module template");

        group.MapPost("/submit", SubmitAsync)
            .WithName("CaaS-Submit")
            .WithSummary("Submit a complete return via API");

        group.MapGet("/templates/{moduleCode}", GetTemplateAsync)
            .WithName("CaaS-GetTemplate")
            .WithSummary("Get template structure for a module");

        group.MapGet("/deadlines", GetDeadlinesAsync)
            .WithName("CaaS-GetDeadlines")
            .WithSummary("Get filing deadlines for entitled modules");

        group.MapPost("/score", GetScoreAsync)
            .WithName("CaaS-GetScore")
            .WithSummary("Get compliance health score");

        group.MapGet("/changes", GetChangesAsync)
            .WithName("CaaS-GetChanges")
            .WithSummary("Get regulatory changes affecting this institution");

        group.MapPost("/simulate", SimulateAsync)
            .WithName("CaaS-Simulate")
            .WithSummary("Run a scenario simulation");

        // API key management (RegOS admin only — separate auth)
        var adminGroup = app.MapGroup("/api/v1/caas/admin")
            .RequireAuthorization("CaaSAdmin");

        adminGroup.MapPost("/partners/{partnerId:int}/keys", CreateApiKeyAsync);
        adminGroup.MapGet("/partners/{partnerId:int}/keys", ListApiKeysAsync);
        adminGroup.MapDelete("/partners/{partnerId:int}/keys/{keyId:long}", RevokeApiKeyAsync);

        return app;
    }

    // ── Endpoint handlers ─────────────────────────────────────────────────

    private static async Task<IResult> ValidateAsync(
        [FromBody] CaaSValidateRequest request,
        HttpContext ctx, ICaaSService svc, ICaaSRateLimiter rateLimiter,
        CancellationToken ct)
    {
        var partner = GetPartner(ctx);
        var rl = await rateLimiter.CheckAndIncrementAsync(partner.PartnerId, partner.Tier, ct);

        ctx.Response.Headers["X-RateLimit-Limit"]     = rl.Limit.ToString();
        ctx.Response.Headers["X-RateLimit-Remaining"] = rl.Remaining.ToString();

        if (!rl.Allowed)
        {
            ctx.Response.Headers["Retry-After"] = rl.RetryAfterSeconds.ToString();
            return Results.StatusCode(429);
        }

        var requestId = GetRequestId(ctx);
        try
        {
            var result = await svc.ValidateAsync(partner, request, requestId, ct);
            return Results.Ok(result);
        }
        catch (CaaSModuleNotEntitledException ex)
        {
            return Results.Forbid();
        }
    }

    private static async Task<IResult> SubmitAsync(
        [FromBody] CaaSSubmitRequest request,
        HttpContext ctx, ICaaSService svc, ICaaSRateLimiter rateLimiter,
        CancellationToken ct)
    {
        var partner = GetPartner(ctx);
        var rl = await rateLimiter.CheckAndIncrementAsync(partner.PartnerId, partner.Tier, ct);

        ctx.Response.Headers["X-RateLimit-Limit"]     = rl.Limit.ToString();
        ctx.Response.Headers["X-RateLimit-Remaining"] = rl.Remaining.ToString();

        if (!rl.Allowed)
        {
            ctx.Response.Headers["Retry-After"] = rl.RetryAfterSeconds.ToString();
            return Results.StatusCode(429);
        }

        var requestId = GetRequestId(ctx);
        var result = await svc.SubmitAsync(partner, request, requestId, ct);

        return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
    }

    private static async Task<IResult> GetTemplateAsync(
        string moduleCode, HttpContext ctx, ICaaSService svc, CancellationToken ct)
    {
        var partner   = GetPartner(ctx);
        var requestId = GetRequestId(ctx);
        try
        {
            var result = await svc.GetTemplateAsync(partner, moduleCode, requestId, ct);
            return Results.Ok(result);
        }
        catch (CaaSModuleNotEntitledException)
        {
            return Results.Forbid();
        }
    }

    private static async Task<IResult> GetDeadlinesAsync(
        HttpContext ctx, ICaaSService svc, CancellationToken ct)
    {
        var partner = GetPartner(ctx);
        var result  = await svc.GetDeadlinesAsync(partner, GetRequestId(ctx), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetScoreAsync(
        [FromBody] CaaSScoreRequest request,
        HttpContext ctx, ICaaSService svc, CancellationToken ct)
    {
        var result = await svc.GetScoreAsync(GetPartner(ctx), request, GetRequestId(ctx), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetChangesAsync(
        HttpContext ctx, ICaaSService svc, CancellationToken ct)
    {
        var result = await svc.GetChangesAsync(GetPartner(ctx), GetRequestId(ctx), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> SimulateAsync(
        [FromBody] CaaSSimulateRequest request,
        HttpContext ctx, ICaaSService svc, CancellationToken ct)
    {
        var partner = GetPartner(ctx);
        var result  = await svc.SimulateAsync(partner, request, GetRequestId(ctx), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateApiKeyAsync(
        int partnerId, [FromBody] CreateApiKeyRequest request,
        ICaaSApiKeyService keyService, ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = int.Parse(user.FindFirst("user_id")!.Value);
        var (rawKey, info) = await keyService.CreateKeyAsync(
            partnerId, request.DisplayName,
            Enum.Parse<CaaSEnvironment>(request.Environment, ignoreCase: true),
            request.ExpiresAt, userId, ct);

        // Raw key shown ONCE — not stored
        return Results.Ok(new { rawKey, info,
            warning = "This is the only time the full API key will be shown. Store it securely." });
    }

    private static async Task<IResult> ListApiKeysAsync(
        int partnerId, ICaaSApiKeyService keyService, CancellationToken ct)
    {
        var keys = await keyService.ListKeysAsync(partnerId, ct);
        return Results.Ok(keys);
    }

    private static async Task<IResult> RevokeApiKeyAsync(
        int partnerId, long keyId, ICaaSApiKeyService keyService,
        ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = int.Parse(user.FindFirst("user_id")!.Value);
        await keyService.RevokeKeyAsync(partnerId, keyId, userId, ct);
        return Results.NoContent();
    }

    private static ResolvedPartner GetPartner(HttpContext ctx)
        => ctx.Items["caas_partner"] as ResolvedPartner
           ?? throw new UnauthorizedAccessException("Partner not resolved.");

    private static Guid GetRequestId(HttpContext ctx)
        => ctx.Items["caas_request_id"] is Guid id ? id : Guid.NewGuid();
}

public sealed record CreateApiKeyRequest(
    string DisplayName, string Environment, DateTimeOffset? ExpiresAt);
```

---

## 12 · CaaS Authentication Middleware

```csharp
/// <summary>
/// Resolves partner from Bearer token (API key) in Authorization header.
/// Sets X-CaaS-Request-Id on every request.
/// Logs every request to CaaSRequests table.
/// </summary>
public sealed class CaaSAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CaaSAuthMiddleware> _log;

    public CaaSAuthMiddleware(RequestDelegate next, ILogger<CaaSAuthMiddleware> log)
    {
        _next = next; _log = log;
    }

    public async Task InvokeAsync(HttpContext ctx, ICaaSApiKeyService keyService,
        IDbConnectionFactory db)
    {
        var requestId = Guid.NewGuid();
        ctx.Items["caas_request_id"] = requestId;
        ctx.Response.Headers["X-CaaS-Request-Id"] = requestId.ToString();

        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (!ctx.Request.Path.StartsWithSegments("/api/v1/caas"))
        {
            await _next(ctx);
            return;
        }

        // Extract API key from Authorization: Bearer regos_live_...
        var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
        ResolvedPartner? partner = null;

        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
        {
            var rawKey = authHeader[7..].Trim();
            partner = await keyService.ValidateKeyAsync(rawKey);
        }

        if (partner is null)
        {
            ctx.Response.StatusCode = 401;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("""{"error":"Invalid or missing API key."}""");
            return;
        }

        ctx.Items["caas_partner"] = partner;

        _log.LogInformation(
            "CaaS request: Partner={Partner} Endpoint={Path} RequestId={RequestId}",
            partner.PartnerCode, ctx.Request.Path, requestId);

        await _next(ctx);

        sw.Stop();

        // Audit log — fire-and-forget
        _ = LogRequestAsync(db, partner, ctx, requestId, sw.ElapsedMilliseconds);
    }

    private static async Task LogRequestAsync(
        IDbConnectionFactory db, ResolvedPartner partner,
        HttpContext ctx, Guid requestId, long durationMs)
    {
        try
        {
            await using var conn = await db.OpenAsync();
            var moduleCode = ctx.Request.RouteValues.TryGetValue("moduleCode", out var mc)
                ? mc?.ToString() : null;

            await conn.ExecuteAsync(
                """
                INSERT INTO CaaSRequests
                    (PartnerId, ApiKeyId, RequestId, Endpoint, HttpMethod,
                     ModuleCode, ResponseStatusCode, DurationMs, IpAddress, UserAgent)
                VALUES (@PartnerId, 0, @RequestId, @Endpoint, @Method,
                        @ModuleCode, @StatusCode, @Duration, @Ip, @Agent)
                """,
                new { PartnerId = partner.PartnerId, RequestId = requestId,
                      Endpoint = ctx.Request.Path.Value, Method = ctx.Request.Method,
                      ModuleCode = moduleCode,
                      StatusCode = ctx.Response.StatusCode, Duration = durationMs,
                      Ip = ctx.Connection.RemoteIpAddress?.ToString(),
                      Agent = ctx.Request.Headers.UserAgent.ToString()[..Math.Min(300,
                          ctx.Request.Headers.UserAgent.ToString().Length)] });
        }
        catch { /* Never fail the request over audit logging */ }
    }
}

public static class CaaSAuthMiddlewareExtensions
{
    public static IEndpointConventionBuilder RequireCaaSAuth(
        this RouteGroupBuilder builder)
        => builder.AddEndpointFilter(async (ctx, next) =>
        {
            if (ctx.HttpContext.Items["caas_partner"] is null)
                return Results.Unauthorized();
            return await next(ctx);
        });
}
```

---

## 13 · JavaScript Embedded Validator Widget

> Self-contained, zero-dependency `validator.js`. Partners embed with a single `<script>` tag.

```javascript
/**
 * RegOS™ Embedded Validator Widget v1.0
 * Zero external dependencies. Self-contained.
 * Usage:
 *   <script src="https://cdn.regos.app/widget/v1/validator.js"></script>
 *   <div id="regos-validator"
 *        data-module="PSP_FINTECH"
 *        data-period="2026-03"
 *        data-api-key="regos_live_..."
 *        data-api-base="https://api.regos.app"
 *        data-theme="#006AFF">
 *   </div>
 */
(function (window) {
    'use strict';

    const WIDGET_VERSION = '1.0.0';

    // ── Utilities ────────────────────────────────────────────────────────
    function debounce(fn, delayMs) {
        let timer;
        return function (...args) {
            clearTimeout(timer);
            timer = setTimeout(() => fn.apply(this, args), delayMs);
        };
    }

    function sanitizeHtml(str) {
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    // ── API client ────────────────────────────────────────────────────────
    function CaaSClient(apiBase, apiKey) {
        this.apiBase = apiBase.replace(/\/$/, '');
        this.apiKey  = apiKey;
    }

    CaaSClient.prototype.get = async function (path) {
        const resp = await fetch(this.apiBase + path, {
            headers: { 'Authorization': 'Bearer ' + this.apiKey,
                       'Accept': 'application/json' }
        });
        if (!resp.ok) throw new Error('API error: ' + resp.status);
        return resp.json();
    };

    CaaSClient.prototype.post = async function (path, body) {
        const resp = await fetch(this.apiBase + path, {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + this.apiKey,
                       'Content-Type': 'application/json',
                       'Accept': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!resp.ok) throw new Error('API error: ' + resp.status);
        return resp.json();
    };

    // ── Widget renderer ───────────────────────────────────────────────────
    function RegOSValidator(container, options) {
        this.container  = container;
        this.options    = options;
        this.client     = new CaaSClient(options.apiBase, options.apiKey);
        this.template   = null;
        this.fieldValues = {};
        this.sessionToken = null;
        this._init();
    }

    RegOSValidator.prototype._init = async function () {
        this._renderSkeleton();
        try {
            const data = await this.client.get(
                '/api/v1/caas/templates/' + encodeURIComponent(this.options.module));
            this.template = data;
            this._renderForm(data);
        } catch (err) {
            this._renderError('Failed to load template: ' + err.message);
        }
    };

    RegOSValidator.prototype._renderSkeleton = function () {
        const theme = this.options.theme || '#006AFF';
        this.container.innerHTML =
            '<div class="regos-widget" style="--regos-primary:' + sanitizeHtml(theme) + '">' +
            '  <div class="regos-header">' +
            '    <span class="regos-logo">RegOS™</span>' +
            '    <span class="regos-module-name">Loading…</span>' +
            '  </div>' +
            '  <div class="regos-body regos-loading">' +
            '    <div class="regos-spinner"></div>' +
            '  </div>' +
            '</div>' +
            '<style>' + this._getStyles() + '</style>';
    };

    RegOSValidator.prototype._renderForm = function (template) {
        const self = this;
        const fields = template.fields.map(f => self._renderField(f)).join('');

        const body = this.container.querySelector('.regos-body');
        body.className = 'regos-body';
        body.innerHTML =
            '<form class="regos-form" id="regos-form-' + this.options.module + '">' +
            fields +
            '  <div class="regos-actions">' +
            '    <button type="button" class="regos-btn regos-btn-validate" id="regos-validate-btn">' +
            '      Validate' +
            '    </button>' +
            '    <button type="button" class="regos-btn regos-btn-submit" ' +
            '            id="regos-submit-btn" disabled>Submit</button>' +
            '  </div>' +
            '  <div class="regos-result" id="regos-result"></div>' +
            '</form>';

        // Bind field change events
        body.querySelectorAll('[data-field]').forEach(function (input) {
            input.addEventListener('input', debounce(function () {
                self.fieldValues[input.dataset.field] = input.value;
                self._liveValidate();
            }, 600));
        });

        // Validate button
        body.querySelector('#regos-validate-btn').addEventListener('click', function () {
            self._validate(true);
        });

        // Submit button
        body.querySelector('#regos-submit-btn').addEventListener('click', function () {
            self._submit();
        });

        // Update header
        this.container.querySelector('.regos-module-name').textContent = template.moduleName;
    };

    RegOSValidator.prototype._renderField = function (field) {
        const required = field.isRequired ? '<span class="regos-required">*</span>' : '';
        const inputType = field.dataType === 'DATE' ? 'date'
            : (field.dataType === 'BOOLEAN' ? 'checkbox' : 'number');

        return '<div class="regos-field" id="regos-field-' + sanitizeHtml(field.fieldCode) + '">' +
               '  <label class="regos-label">' +
               sanitizeHtml(field.fieldLabel) + required +
               '  </label>' +
               '  <input type="' + inputType + '"' +
               '         class="regos-input"' +
               '         data-field="' + sanitizeHtml(field.fieldCode) + '"' +
               (field.isRequired ? ' required' : '') +
               (field.minValue !== null ? ' min="' + field.minValue + '"' : '') +
               (field.maxValue !== null ? ' max="' + field.maxValue + '"' : '') +
               '  />' +
               '  <span class="regos-field-error" id="regos-err-' +
               sanitizeHtml(field.fieldCode) + '"></span>' +
               '</div>';
    };

    RegOSValidator.prototype._liveValidate = async function () {
        if (Object.keys(this.fieldValues).length < 2) return;
        try {
            const result = await this.client.post('/api/v1/caas/validate', {
                moduleCode: this.options.module,
                periodCode: this.options.period,
                fields: this.fieldValues,
                persistSession: false
            });
            this._updateFieldErrors(result.errors);
            this._updateScorePreview(result.complianceScore);
        } catch (_) { /* Live validation failures are non-blocking */ }
    };

    RegOSValidator.prototype._validate = async function (persistSession) {
        const resultDiv = this.container.querySelector('#regos-result');
        resultDiv.innerHTML = '<div class="regos-spinner"></div>';

        try {
            const result = await this.client.post('/api/v1/caas/validate', {
                moduleCode: this.options.module,
                periodCode: this.options.period,
                fields: this.fieldValues,
                persistSession: persistSession === true
            });

            this.sessionToken = result.sessionToken || null;
            this._updateFieldErrors(result.errors);

            if (result.isValid) {
                resultDiv.innerHTML =
                    '<div class="regos-alert regos-alert-success">' +
                    '✓ Validation passed. Score: ' + result.complianceScore.toFixed(1) + '/100' +
                    '</div>';
                const submitBtn = this.container.querySelector('#regos-submit-btn');
                if (submitBtn) submitBtn.disabled = false;
            } else {
                resultDiv.innerHTML =
                    '<div class="regos-alert regos-alert-error">' +
                    '✗ ' + result.errorCount + ' error(s) found. Please correct and re-validate.' +
                    '</div>';
            }

            // Emit custom event for host page integration
            this.container.dispatchEvent(new CustomEvent('regos:validated', {
                bubbles: true, detail: result
            }));
        } catch (err) {
            resultDiv.innerHTML =
                '<div class="regos-alert regos-alert-error">Validation failed: ' +
                sanitizeHtml(err.message) + '</div>';
        }
    };

    RegOSValidator.prototype._submit = async function () {
        if (!this.sessionToken && !this.options.autoSubmitRegulator) {
            this._validate(true).then(() => {
                if (this.sessionToken) this._submit();
            });
            return;
        }

        const resultDiv = this.container.querySelector('#regos-result');
        resultDiv.innerHTML = '<div class="regos-spinner"></div> Submitting…';

        try {
            const result = await this.client.post('/api/v1/caas/submit', {
                sessionToken: this.sessionToken,
                regulatorCode: this.options.regulatorCode || 'CBN',
                submittedByExternalUserId: 0
            });

            if (result.success) {
                resultDiv.innerHTML =
                    '<div class="regos-alert regos-alert-success">' +
                    '✓ Submitted successfully. Receipt: ' +
                    sanitizeHtml(result.receiptReference || 'pending') + '</div>';
                this.container.querySelector('#regos-submit-btn').disabled = true;

                this.container.dispatchEvent(new CustomEvent('regos:submitted', {
                    bubbles: true, detail: result
                }));
            } else {
                resultDiv.innerHTML =
                    '<div class="regos-alert regos-alert-error">' +
                    sanitizeHtml(result.errorMessage || 'Submission failed.') + '</div>';
            }
        } catch (err) {
            resultDiv.innerHTML =
                '<div class="regos-alert regos-alert-error">Submit error: ' +
                sanitizeHtml(err.message) + '</div>';
        }
    };

    RegOSValidator.prototype._updateFieldErrors = function (errors) {
        // Clear all existing errors
        this.container.querySelectorAll('.regos-field-error').forEach(function (el) {
            el.textContent = '';
            el.closest('.regos-field').classList.remove('regos-field-invalid');
        });

        errors.forEach(function (err) {
            const errEl = document.getElementById('regos-err-' + err.fieldCode);
            if (errEl) {
                errEl.textContent = err.message;
                errEl.closest('.regos-field').classList.add('regos-field-invalid');
            }
        });
    };

    RegOSValidator.prototype._updateScorePreview = function (score) {
        let preview = this.container.querySelector('.regos-score-preview');
        if (!preview) {
            preview = document.createElement('div');
            preview.className = 'regos-score-preview';
            this.container.querySelector('.regos-actions').before(preview);
        }
        const colour = score >= 80 ? '#22c55e' : score >= 60 ? '#f59e0b' : '#ef4444';
        preview.innerHTML =
            '<span style="color:' + colour + '">Score: ' + score.toFixed(1) + '/100</span>';
    };

    RegOSValidator.prototype._renderError = function (msg) {
        this.container.innerHTML =
            '<div class="regos-widget"><div class="regos-alert regos-alert-error">' +
            sanitizeHtml(msg) + '</div></div>';
    };

    RegOSValidator.prototype._getStyles = function () {
        return `.regos-widget{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;
border:1px solid #e2e8f0;border-radius:8px;overflow:hidden;background:#fff}
.regos-header{background:var(--regos-primary,#006AFF);color:#fff;padding:12px 16px;
display:flex;justify-content:space-between;align-items:center}
.regos-logo{font-weight:700;font-size:14px}
.regos-module-name{font-size:12px;opacity:.85}
.regos-body{padding:16px}
.regos-field{margin-bottom:14px}
.regos-label{display:block;font-size:13px;font-weight:500;color:#374151;margin-bottom:4px}
.regos-required{color:#ef4444;margin-left:2px}
.regos-input{width:100%;padding:8px 10px;border:1px solid #d1d5db;border-radius:6px;
font-size:13px;box-sizing:border-box;transition:border-color .15s}
.regos-input:focus{outline:none;border-color:var(--regos-primary,#006AFF)}
.regos-field-invalid .regos-input{border-color:#ef4444}
.regos-field-error{color:#ef4444;font-size:11px;display:block;margin-top:3px}
.regos-actions{display:flex;gap:8px;margin-top:16px}
.regos-btn{padding:9px 20px;border:none;border-radius:6px;font-size:13px;
cursor:pointer;font-weight:500;transition:opacity .15s}
.regos-btn-validate{background:var(--regos-primary,#006AFF);color:#fff}
.regos-btn-submit{background:#10b981;color:#fff}
.regos-btn:disabled{opacity:.45;cursor:not-allowed}
.regos-alert{padding:10px 14px;border-radius:6px;font-size:13px;margin-top:12px}
.regos-alert-success{background:#ecfdf5;color:#065f46;border:1px solid #a7f3d0}
.regos-alert-error{background:#fef2f2;color:#991b1b;border:1px solid #fecaca}
.regos-spinner{display:inline-block;width:20px;height:20px;border:2px solid #e2e8f0;
border-top-color:var(--regos-primary,#006AFF);border-radius:50%;
animation:regos-spin .7s linear infinite}
.regos-loading{display:flex;justify-content:center;padding:32px}
.regos-score-preview{font-size:13px;font-weight:600;margin-bottom:8px}
@keyframes regos-spin{to{transform:rotate(360deg)}}`;
    };

    // ── Auto-initialise all matching containers ───────────────────────────
    function autoInit() {
        document.querySelectorAll('[id="regos-validator"], [data-regos-widget]')
            .forEach(function (container) {
                const d = container.dataset;
                if (!d.apiKey || !d.module) {
                    console.warn('RegOS Widget: data-api-key and data-module are required.');
                    return;
                }
                new RegOSValidator(container, {
                    module:           d.module,
                    period:           d.period || new Date().toISOString().slice(0, 7),
                    apiKey:           d.apiKey,
                    apiBase:          d.apiBase || 'https://api.regos.app',
                    theme:            d.theme || '#006AFF',
                    regulatorCode:    d.regulatorCode || 'CBN'
                });
            });
    }

    // Expose global API for manual initialisation
    window.RegOSValidator = RegOSValidator;

    // Auto-init on DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', autoInit);
    } else {
        autoInit();
    }

})(window);
```

---

## 14 · Dependency Injection Registration

```csharp
public static class CaaSServiceExtensions
{
    public static IServiceCollection AddCaaSEngine(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Azure Key Vault ──────────────────────────────────────────────
        var kvUri = configuration["KeyVault:Uri"]
            ?? throw new InvalidOperationException("KeyVault:Uri is required.");
        services.AddSingleton(new SecretClient(new Uri(kvUri), new DefaultAzureCredential()));

        // ── Redis (rate limiter) ─────────────────────────────────────────
        var redisConn = configuration["Redis:ConnectionString"]
            ?? throw new InvalidOperationException("Redis:ConnectionString is required.");
        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(redisConn));

        // ── Core CaaS services ───────────────────────────────────────────
        services.AddScoped<ICaaSService, CaaSService>();
        services.AddScoped<ICaaSApiKeyService, CaaSApiKeyService>();
        services.AddSingleton<ICaaSRateLimiter, CaaSRedisRateLimiter>();
        services.AddScoped<ICaaSWebhookDispatcher, CaaSWebhookDispatcher>();
        services.AddScoped<ICaaSAutoFilingService, CaaSAutoFilingService>();

        // ── Core banking adapters ────────────────────────────────────────
        services.AddSingleton<ICoreBankingAdapter, FinacleCoreBankingAdapter>();
        services.AddSingleton<ICoreBankingAdapter, T24CoreBankingAdapter>();
        services.AddSingleton<ICoreBankingAdapter, BankOneCoreBankingAdapter>();
        services.AddSingleton<ICoreBankingAdapter, FlexcubeCoreBankingAdapter>();
        services.AddSingleton<ICoreBankingAdapterFactory, CoreBankingAdapterFactory>();

        // ── HTTP clients ─────────────────────────────────────────────────
        services.AddHttpClient("Webhook")
            .ConfigureHttpClient(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddResilienceHandler("Webhook", pipeline =>
            {
                pipeline.AddTimeout(TimeSpan.FromSeconds(10));
                pipeline.AddRetry(new Polly.Retry.HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType      = DelayBackoffType.Exponential,
                    Delay            = TimeSpan.FromSeconds(1),
                    UseJitter        = true
                });
            });

        // ── Background services ──────────────────────────────────────────
        services.AddHostedService<WebhookDispatcherBackgroundService>();
        services.AddHostedService<AutoFilingSchedulerBackgroundService>();

        return services;
    }
}

/// <summary>
/// Processes pending webhook deliveries every 30 seconds.
/// </summary>
public sealed class WebhookDispatcherBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<WebhookDispatcherBackgroundService> _log;

    public WebhookDispatcherBackgroundService(
        IServiceProvider services,
        ILogger<WebhookDispatcherBackgroundService> log)
    {
        _services = services; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = _services.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider
                    .GetRequiredService<ICaaSWebhookDispatcher>();
                await dispatcher.ProcessPendingAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Webhook dispatcher background cycle failed.");
            }
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }
}

/// <summary>
/// Polls for due auto-filing schedules every 60 seconds.
/// </summary>
public sealed class AutoFilingSchedulerBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AutoFilingSchedulerBackgroundService> _log;

    public AutoFilingSchedulerBackgroundService(
        IServiceProvider services,
        ILogger<AutoFilingSchedulerBackgroundService> log)
    {
        _services = services; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = _services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
                var filingService = scope.ServiceProvider
                    .GetRequiredService<ICaaSAutoFilingService>();

                await using var conn = await db.OpenAsync(ct);
                var dueSchedules = await conn.QueryAsync<int>(
                    """
                    SELECT Id
                    FROM   CaaSAutoFilingSchedules
                    WHERE  IsActive = 1
                      AND  NextRunAt <= SYSUTCDATETIME()
                    """);

                foreach (var scheduleId in dueSchedules)
                {
                    _ = filingService.ExecuteScheduleAsync(scheduleId, ct)
                        .ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                                _log.LogError(t.Exception, "Auto-filing schedule {Id} failed.", scheduleId);
                        }, TaskScheduler.Default);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Auto-filing scheduler background cycle failed.");
            }
            await Task.Delay(TimeSpan.FromSeconds(60), ct);
        }
    }
}
```

---

## 15 · Custom Exceptions

```csharp
public sealed class CaaSModuleNotEntitledException : Exception
{
    public CaaSModuleNotEntitledException(string message) : base(message) { }
}

public sealed class CaaSRateLimitExceededException : Exception
{
    public int RetryAfterSeconds { get; }
    public CaaSRateLimitExceededException(int retryAfter)
        : base($"Rate limit exceeded. Retry after {retryAfter}s.")
    {
        RetryAfterSeconds = retryAfter;
    }
}

public sealed class CaaSSessionExpiredException : Exception
{
    public CaaSSessionExpiredException() : base("Validation session has expired or been used.") { }
}
```

---

## 16 · Integration Tests (Testcontainers)

```csharp
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
        var requestId = Guid.NewGuid();

        var request = new CaaSValidateRequest(
            ModuleCode: "PSP_FINTECH",
            PeriodCode: "2026-03",
            Fields: new Dictionary<string, object?>
            {
                ["TOTAL_TXN_VALUE"]    = 8_450_000_000m,
                ["TOTAL_TXN_COUNT"]    = 2_340_000m,
                ["FAILED_TXN_VALUE"]   = 210_000_000m,
                ["FAILED_TXN_COUNT"]   = 45_000m,
                ["SETTLEMENT_FLOAT"]   = 1_200_000_000m,
                ["CUSTOMER_FUNDS"]     = 3_100_000_000m,
                ["ESCROW_BALANCE"]     = 500_000_000m
            },
            PersistSession: true);

        var result = await _fx.CaaSService.ValidateAsync(partner, request, requestId);

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
    public async Task GetDeadlines_PalmPayModules_ReturnsUpcomingDeadlines()
    {
        var result = await _fx.CaaSService.GetDeadlinesAsync(
            _fx.PalmPayPartner, Guid.NewGuid());

        Assert.NotNull(result.Upcoming);
        Assert.All(result.Upcoming, d =>
            Assert.Contains(d.ModuleCode, _fx.PalmPayPartner.AllowedModuleCodes));
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
        const int partnerId = 99_999;  // isolated test partner
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

        // Verify WireMock received the webhook
        var calls = _fx.WebhookServer.FindLogEntries(
            WireMock.RequestBuilders.Request.Create()
                .WithPath("/webhook/partner-1")
                .UsingPost());

        Assert.NotEmpty(calls);
        var body = calls.Last().RequestMessage.BodyAsJson;
        Assert.Equal("filing.completed", body?.GetType()
            .GetProperty("event_type")?.GetValue(body)?.ToString());
    }
}

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
    public WireMock.Server.WireMockServer WebhookServer { get; private set; } = null!;
    public ResolvedPartner PalmPayPartner { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();
        await _redisContainer.StartAsync();

        var connectionString = _sqlContainer.GetConnectionString();

        // Run migrations
        await new DatabaseMigrator(connectionString).MigrateAsync();
        await SeedTestDataAsync(connectionString);

        // WireMock for webhooks
        WebhookServer = WireMock.Server.WireMockServer.Start();
        WebhookServer.Given(
            WireMock.RequestBuilders.Request.Create()
                .WithPath("/webhook/partner-1").UsingPost())
            .RespondWith(
                WireMock.ResponseBuilders.Response.Create()
                    .WithStatusCode(200));

        // Build DI
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<IDbConnectionFactory>(
            new SqlConnectionFactory(connectionString));
        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(_redisContainer.GetConnectionString()));

        // Register mocks
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
        CaaSService     = sp.GetRequiredService<ICaaSService>();
        ApiKeyService   = sp.GetRequiredService<ICaaSApiKeyService>();
        RateLimiter     = sp.GetRequiredService<ICaaSRateLimiter>();
        WebhookDispatcher = sp.GetRequiredService<ICaaSWebhookDispatcher>();

        PalmPayPartner = new ResolvedPartner(
            PartnerId: 1, PartnerCode: "PALMPAY-NG", InstitutionId: 42,
            Tier: PartnerTier.Growth, Environment: "LIVE",
            AllowedModuleCodes: new[] { "PSP_FINTECH", "PSP_MONTHLY", "NFIU_STR" });
    }

    private static async Task SeedTestDataAsync(string connectionString)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            """
            -- Update partner webhook URL to WireMock
            UPDATE CaaSPartners
            SET    WebhookUrl = 'http://localhost:9090/webhook/partner-1',
                   WebhookSecret = 'test-webhook-secret-sha256'
            WHERE  Id = 1
            """);
    }

    public async Task DisposeAsync()
    {
        WebhookServer.Stop();
        await _sqlContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }
}
```

---

## 17 · Configuration (appsettings.json)

```json
{
  "KeyVault": {
    "Uri": "https://regos-kv-prod.vault.azure.net/"
  },
  "Redis": {
    "ConnectionString": "redis.regos.internal:6379,password={from-kv},ssl=true"
  },
  "CaaS": {
    "WidgetCdnBase": "https://cdn.regos.app/widget/v1",
    "ApiDocBase": "https://docs.regos.app",
    "SessionTtlHours": 24,
    "WebhookMaxAttempts": 5,
    "AutoFilingEnabled": true
  },
  "CoreBanking": {
    "Finacle": { "DefaultTimeoutSeconds": 30 },
    "T24":     { "DefaultTimeoutSeconds": 30 },
    "BankOne": { "DefaultTimeoutSeconds": 20 },
    "Flexcube":{ "DefaultTimeoutSeconds": 30 }
  }
}
```

---

## 18 · Deliverables Checklist

Confirm every item below is complete before marking RG-35 as **Done**.

| # | Artefact | Rule Ref |
|---|---|---|
| 1 | EF Core migration `20260315_AddCaaSSchema.cs` — all 7 tables, indexes, constraints, partner seed data | R-03 |
| 2 | `CaaSService` — all 7 endpoints fully implemented: validate, submit, template, deadlines, score, changes, simulate | R-02 |
| 3 | `CaaSApiKeyService` — create (raw key shown once), validate (hash lookup), revoke, list | R-06 |
| 4 | `CaaSRedisRateLimiter` — sliding-window Lua script, 3 tier thresholds, fail-open on Redis outage | R-07 |
| 5 | `CaaSWebhookDispatcher` — enqueue, HMAC signing, at-least-once delivery, exponential back-off, dead-letter | R-11 |
| 6 | Four core banking adapters — Finacle (EAI REST), T24 Transact (REST), BankOne (API), Flexcube (FCUBS REST) | R-02 |
| 7 | `CaaSAutoFilingService` — extract → validate → submit pipeline, hold-on-error with webhook notification | R-02 |
| 8 | ASP.NET Minimal API endpoints — 7 CaaS routes + 3 admin key-management routes, rate-limit headers | R-05 |
| 9 | `CaaSAuthMiddleware` — API key resolution, X-CaaS-Request-Id propagation, CaaSRequests audit logging | R-08 |
| 10 | `validator.js` JavaScript widget — zero-dependency, self-contained, real-time validation, submit flow, custom events | R-12 |
| 11 | `WebhookDispatcherBackgroundService` + `AutoFilingSchedulerBackgroundService` — hosted services with 30s/60s polling | R-11 |
| 12 | DI registration `AddCaaSEngine()` — all services, HTTP clients with Polly resilience, background workers | R-10 |
| 13 | Integration tests — Testcontainers (SQL Server + Redis) + WireMock for webhooks; 7 test scenarios | R-09 |
| 14 | `appsettings.json` snippet with all CaaS configuration keys | R-10 |
| 15 | Zero hardcoded credentials, zero raw SQL interpolation, zero cross-partner data leakage | R-04, R-05, R-10 |

---

*End of RG-35 — Embedded Compliance-as-a-Service API*