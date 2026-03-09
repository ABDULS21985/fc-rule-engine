using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FC.Engine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCaaSSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── CaaSPartners ─────────────────────────────────────────────
            migrationBuilder.Sql("""
                IF OBJECT_ID('CaaSPartners', 'U') IS NULL
                BEGIN
                    CREATE TABLE CaaSPartners (
                        Id                   INT IDENTITY(1,1) PRIMARY KEY,
                        PartnerCode          VARCHAR(30)      NOT NULL,
                        PartnerName          NVARCHAR(150)    NOT NULL,
                        ContactEmail         NVARCHAR(150)    NOT NULL,
                        Tier                 VARCHAR(20)      NOT NULL DEFAULT 'STARTER',
                        InstitutionId        INT              NOT NULL,
                        IsActive             BIT              NOT NULL DEFAULT 1,
                        WhiteLabelName       NVARCHAR(100)    NULL,
                        WhiteLabelLogoUrl    NVARCHAR(500)    NULL,
                        WhiteLabelPrimaryColor VARCHAR(7)     NULL,
                        AllowedModuleCodes   NVARCHAR(MAX)    NULL,
                        WebhookUrl           NVARCHAR(500)    NULL,
                        WebhookSecret        NVARCHAR(128)    NULL,
                        CreatedAt            DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
                        UpdatedAt            DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT UQ_CaaSPartners_Code UNIQUE (PartnerCode)
                    );
                END
                """);

            // ── CaaSApiKeys ──────────────────────────────────────────────
            migrationBuilder.Sql("""
                IF OBJECT_ID('CaaSApiKeys', 'U') IS NULL
                BEGIN
                    CREATE TABLE CaaSApiKeys (
                        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
                        PartnerId           INT              NOT NULL,
                        KeyPrefix           VARCHAR(12)      NOT NULL,
                        KeyHash             VARCHAR(64)      NOT NULL,
                        DisplayName         NVARCHAR(100)    NOT NULL,
                        Environment         VARCHAR(10)      NOT NULL DEFAULT 'LIVE',
                        IsActive            BIT              NOT NULL DEFAULT 1,
                        ExpiresAt           DATETIME2(3)     NULL,
                        LastUsedAt          DATETIME2(3)     NULL,
                        RevokedAt           DATETIME2(3)     NULL,
                        RevokedByUserId     INT              NULL,
                        CreatedByUserId     INT              NOT NULL,
                        CreatedAt           DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT FK_CaaSApiKeys_Partner
                            FOREIGN KEY (PartnerId) REFERENCES CaaSPartners(Id),
                        CONSTRAINT UQ_CaaSApiKeys_Hash UNIQUE (KeyHash)
                    );

                    CREATE INDEX IX_CaaSApiKeys_Hash    ON CaaSApiKeys (KeyHash);
                    CREATE INDEX IX_CaaSApiKeys_Partner ON CaaSApiKeys (PartnerId, IsActive);
                END
                """);

            // ── CaaSRequests ─────────────────────────────────────────────
            migrationBuilder.Sql("""
                IF OBJECT_ID('CaaSRequests', 'U') IS NULL
                BEGIN
                    CREATE TABLE CaaSRequests (
                        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
                        PartnerId           INT              NOT NULL,
                        ApiKeyId            BIGINT           NOT NULL,
                        RequestId           UNIQUEIDENTIFIER NOT NULL,
                        Endpoint            VARCHAR(100)     NOT NULL,
                        HttpMethod          VARCHAR(10)      NOT NULL,
                        ModuleCode          VARCHAR(30)      NULL,
                        ResponseStatusCode  INT              NOT NULL,
                        DurationMs          INT              NOT NULL,
                        RequestBodyHash     VARCHAR(64)      NULL,
                        IpAddress           VARCHAR(45)      NULL,
                        UserAgent           NVARCHAR(300)    NULL,
                        CreatedAt           DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME()
                    );

                    CREATE INDEX IX_CaaSRequests_Partner   ON CaaSRequests (PartnerId, CreatedAt DESC);
                    CREATE INDEX IX_CaaSRequests_RequestId ON CaaSRequests (RequestId);
                    CREATE INDEX IX_CaaSRequests_Endpoint  ON CaaSRequests (Endpoint, CreatedAt DESC);
                END
                """);

            // ── CaaSValidationSessions ───────────────────────────────────
            migrationBuilder.Sql("""
                IF OBJECT_ID('CaaSValidationSessions', 'U') IS NULL
                BEGIN
                    CREATE TABLE CaaSValidationSessions (
                        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
                        PartnerId           INT              NOT NULL,
                        SessionToken        VARCHAR(64)      NOT NULL,
                        ModuleCode          VARCHAR(30)      NOT NULL,
                        PeriodCode          VARCHAR(10)      NOT NULL,
                        SubmittedData       NVARCHAR(MAX)    NOT NULL,
                        ValidationResult    NVARCHAR(MAX)    NULL,
                        IsValid             BIT              NULL,
                        ExpiresAt           DATETIME2(3)     NOT NULL,
                        ConvertedToReturnId BIGINT           NULL,
                        CreatedAt           DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
                        UpdatedAt           DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT UQ_CaaSValidationSessions_Token UNIQUE (SessionToken)
                    );

                    CREATE INDEX IX_CaaSValidationSessions_Partner
                        ON CaaSValidationSessions (PartnerId, ExpiresAt);
                END
                """);

            // ── CaaSWebhookDeliveries ────────────────────────────────────
            migrationBuilder.Sql("""
                IF OBJECT_ID('CaaSWebhookDeliveries', 'U') IS NULL
                BEGIN
                    CREATE TABLE CaaSWebhookDeliveries (
                        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
                        PartnerId           INT              NOT NULL,
                        EventType           VARCHAR(60)      NOT NULL,
                        Payload             NVARCHAR(MAX)    NOT NULL,
                        HmacSignature       VARCHAR(128)     NOT NULL,
                        AttemptCount        INT              NOT NULL DEFAULT 0,
                        MaxAttempts         INT              NOT NULL DEFAULT 5,
                        Status              VARCHAR(20)      NOT NULL DEFAULT 'PENDING',
                        LastAttemptAt       DATETIME2(3)     NULL,
                        DeliveredAt         DATETIME2(3)     NULL,
                        LastHttpStatus      INT              NULL,
                        LastErrorMessage    NVARCHAR(1000)   NULL,
                        NextRetryAt         DATETIME2(3)     NULL,
                        CreatedAt           DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME()
                    );

                    CREATE INDEX IX_CaaSWebhookDeliveries_Partner
                        ON CaaSWebhookDeliveries (PartnerId, Status);
                    CREATE INDEX IX_CaaSWebhookDeliveries_NextRetry
                        ON CaaSWebhookDeliveries (NextRetryAt, Status);
                END
                """);

            // ── CaaSCoreBankingConnections ───────────────────────────────
            migrationBuilder.Sql("""
                IF OBJECT_ID('CaaSCoreBankingConnections', 'U') IS NULL
                BEGIN
                    CREATE TABLE CaaSCoreBankingConnections (
                        Id                   INT IDENTITY(1,1) PRIMARY KEY,
                        PartnerId            INT              NOT NULL,
                        SystemType           VARCHAR(20)      NOT NULL,
                        ConnectionName       NVARCHAR(100)    NOT NULL,
                        BaseUrl              NVARCHAR(500)    NULL,
                        DatabaseServer       NVARCHAR(200)    NULL,
                        CredentialSecretName NVARCHAR(100)    NOT NULL,
                        FieldMappingJson     NVARCHAR(MAX)    NOT NULL,
                        IsActive             BIT              NOT NULL DEFAULT 1,
                        LastTestedAt         DATETIME2(3)     NULL,
                        LastTestResult       NVARCHAR(500)    NULL,
                        CreatedAt            DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT FK_CaaSCoreBankingConnections_Partner
                            FOREIGN KEY (PartnerId) REFERENCES CaaSPartners(Id)
                    );

                    CREATE INDEX IX_CaaSCoreBankingConnections_Partner
                        ON CaaSCoreBankingConnections (PartnerId, SystemType);
                END
                """);

            // ── CaaSAutoFilingSchedules ──────────────────────────────────
            migrationBuilder.Sql("""
                IF OBJECT_ID('CaaSAutoFilingSchedules', 'U') IS NULL
                BEGIN
                    CREATE TABLE CaaSAutoFilingSchedules (
                        Id                      INT IDENTITY(1,1) PRIMARY KEY,
                        PartnerId               INT              NOT NULL,
                        ModuleCode              VARCHAR(30)      NOT NULL,
                        CoreBankingConnectionId INT              NOT NULL,
                        CronExpression          VARCHAR(100)     NOT NULL,
                        AutoSubmitIfClean       BIT              NOT NULL DEFAULT 0,
                        NotifyEmails            NVARCHAR(500)    NULL,
                        IsActive                BIT              NOT NULL DEFAULT 1,
                        LastRunAt               DATETIME2(3)     NULL,
                        LastRunStatus           VARCHAR(20)      NULL,
                        NextRunAt               DATETIME2(3)     NULL,
                        CreatedAt               DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT FK_CaaSAutoFilingSchedules_Partner
                            FOREIGN KEY (PartnerId) REFERENCES CaaSPartners(Id),
                        CONSTRAINT FK_CaaSAutoFilingSchedules_Connection
                            FOREIGN KEY (CoreBankingConnectionId)
                            REFERENCES CaaSCoreBankingConnections(Id)
                    );

                    CREATE INDEX IX_CaaSAutoFilingSchedules_NextRun
                        ON CaaSAutoFilingSchedules (NextRunAt, IsActive);
                END
                """);

            // ── CaaSAutoFilingRuns ───────────────────────────────────────
            migrationBuilder.Sql("""
                IF OBJECT_ID('CaaSAutoFilingRuns', 'U') IS NULL
                BEGIN
                    CREATE TABLE CaaSAutoFilingRuns (
                        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
                        ScheduleId          INT              NOT NULL,
                        PartnerId           INT              NOT NULL,
                        ModuleCode          VARCHAR(30)      NOT NULL,
                        PeriodCode          VARCHAR(10)      NOT NULL,
                        Phase               VARCHAR(20)      NOT NULL,
                        ValidationSessionId BIGINT           NULL,
                        ReturnInstanceId    BIGINT           NULL,
                        BatchId             BIGINT           NULL,
                        IsClean             BIT              NULL,
                        ErrorMessage        NVARCHAR(2000)   NULL,
                        StartedAt           DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
                        CompletedAt         DATETIME2(3)     NULL,
                        CONSTRAINT FK_CaaSAutoFilingRuns_Schedule
                            FOREIGN KEY (ScheduleId) REFERENCES CaaSAutoFilingSchedules(Id)
                    );

                    CREATE INDEX IX_CaaSAutoFilingRuns_Schedule
                        ON CaaSAutoFilingRuns (ScheduleId, StartedAt DESC);
                    CREATE INDEX IX_CaaSAutoFilingRuns_Partner
                        ON CaaSAutoFilingRuns (PartnerId, Phase);
                END
                """);

            // ── Seed Data — Example Partners (Nigerian regulatory context) ─
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM CaaSPartners WHERE Id = 1)
                BEGIN
                    SET IDENTITY_INSERT CaaSPartners ON;

                    INSERT INTO CaaSPartners
                        (Id, PartnerCode, PartnerName, ContactEmail, Tier, InstitutionId,
                         WhiteLabelName, AllowedModuleCodes)
                    VALUES
                        (1, 'PALMPAY-NG', 'PalmPay Nigeria Limited',
                         'compliance@palmpay.com', 'GROWTH', 42,
                         'PalmPay Compliance Engine',
                         '["PSP_FINTECH","PSP_MONTHLY","NFIU_STR"]'),
                        (2, 'KUDA-MFB', 'Kuda Microfinance Bank',
                         'regulatory@kuda.com', 'GROWTH', 57,
                         'Kuda Regulatory Hub',
                         '["MFB_MONTHLY","MFB_QUARTERLY","NFIU_CTR"]'),
                        (3, 'CARBON-FIN', 'Carbon Finance Limited',
                         'fintech@carbon.ng', 'STARTER', 83,
                         NULL,
                         '["PSP_FINTECH","PSP_MONTHLY"]'),
                        (4, 'MONIEPOINT-MFB', 'Moniepoint Microfinance Bank',
                         'compliance@moniepoint.com', 'ENTERPRISE', 18,
                         'Moniepoint Compliance',
                         '["MFB_MONTHLY","MFB_QUARTERLY","DMB_WEEKLY","NFIU_CTR","NFIU_STR"]');

                    SET IDENTITY_INSERT CaaSPartners OFF;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS CaaSAutoFilingRuns;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS CaaSAutoFilingSchedules;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS CaaSCoreBankingConnections;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS CaaSWebhookDeliveries;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS CaaSValidationSessions;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS CaaSRequests;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS CaaSApiKeys;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS CaaSPartners;");
        }
    }
}
