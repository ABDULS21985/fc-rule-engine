#nullable enable
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FC.Engine.Infrastructure.Metadata.Migrations;

[DbContext(typeof(MetadataDbContext))]
[Migration("20260305090000_AddSubscriptionBillingPlanManagement")]
public partial class AddSubscriptionBillingPlanManagement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ───────────────────────────────────────────────────────────
        // 1) Extend subscription_plans schema for RG-03 model
        // ───────────────────────────────────────────────────────────
        migrationBuilder.Sql(@"
            IF COL_LENGTH('dbo.subscription_plans', 'Tier') IS NULL
                ALTER TABLE dbo.subscription_plans ADD Tier INT NOT NULL CONSTRAINT DF_subscription_plans_Tier DEFAULT 0;

            IF COL_LENGTH('dbo.subscription_plans', 'MaxEntities') IS NULL
                ALTER TABLE dbo.subscription_plans ADD MaxEntities INT NOT NULL CONSTRAINT DF_subscription_plans_MaxEntities DEFAULT 1;

            IF COL_LENGTH('dbo.subscription_plans', 'MaxApiCallsPerMonth') IS NULL
                ALTER TABLE dbo.subscription_plans ADD MaxApiCallsPerMonth INT NOT NULL CONSTRAINT DF_subscription_plans_MaxApiCallsPerMonth DEFAULT 0;

            IF COL_LENGTH('dbo.subscription_plans', 'MaxStorageMb') IS NULL
                ALTER TABLE dbo.subscription_plans ADD MaxStorageMb INT NOT NULL CONSTRAINT DF_subscription_plans_MaxStorageMb DEFAULT 500;

            IF COL_LENGTH('dbo.subscription_plans', 'BasePriceMonthly') IS NULL
                ALTER TABLE dbo.subscription_plans ADD BasePriceMonthly DECIMAL(18,2) NOT NULL CONSTRAINT DF_subscription_plans_BasePriceMonthly DEFAULT 0;

            IF COL_LENGTH('dbo.subscription_plans', 'BasePriceAnnual') IS NULL
                ALTER TABLE dbo.subscription_plans ADD BasePriceAnnual DECIMAL(18,2) NOT NULL CONSTRAINT DF_subscription_plans_BasePriceAnnual DEFAULT 0;

            IF COL_LENGTH('dbo.subscription_plans', 'TrialDays') IS NULL
                ALTER TABLE dbo.subscription_plans ADD TrialDays INT NOT NULL CONSTRAINT DF_subscription_plans_TrialDays DEFAULT 14;

            IF COL_LENGTH('dbo.subscription_plans', 'Features') IS NOT NULL
            BEGIN
                DECLARE @featuresDefaultConstraint NVARCHAR(128);
                SELECT @featuresDefaultConstraint = dc.name
                FROM sys.default_constraints dc
                INNER JOIN sys.columns c
                    ON dc.parent_object_id = c.object_id
                    AND dc.parent_column_id = c.column_id
                WHERE dc.parent_object_id = OBJECT_ID('dbo.subscription_plans')
                    AND c.name = 'Features';

                IF @featuresDefaultConstraint IS NOT NULL
                    EXEC(N'ALTER TABLE dbo.subscription_plans DROP CONSTRAINT [' + @featuresDefaultConstraint + N'];');

                ALTER TABLE dbo.subscription_plans ALTER COLUMN Features NVARCHAR(MAX) NULL;
            END;

            IF COL_LENGTH('dbo.subscription_plans', 'MaxEntities') IS NOT NULL
            BEGIN
                DECLARE @maxEntitiesBackfillSql NVARCHAR(MAX);
                SET @maxEntitiesBackfillSql =
                    N'UPDATE dbo.subscription_plans SET MaxEntities = ISNULL(MaxEntities, '
                    + CASE
                        WHEN COL_LENGTH('dbo.subscription_plans', 'MaxInstitutions') IS NOT NULL
                            THEN N'ISNULL(MaxInstitutions, 1)'
                        ELSE N'1'
                      END
                    + N');';
                EXEC sp_executesql @maxEntitiesBackfillSql;
            END;
        ");

        migrationBuilder.Sql(@"
            DELETE FROM dbo.subscription_plans;

            INSERT INTO dbo.subscription_plans
                (PlanCode, PlanName, Description, Tier, MaxModules, MaxUsersPerEntity, MaxEntities, MaxApiCallsPerMonth,
                 MaxStorageMb, BasePriceMonthly, BasePriceAnnual, TrialDays, Features, IsActive, DisplayOrder)
            VALUES
                ('STARTER', 'Starter',
                 'Basic dashboard, email notifications, starter module bundle.',
                 1, 2, 10, 1, 0, 500, 150000.00, 1500000.00, 14,
                 '[""dashboard_basic"",""email_notifications""]', 1, 1),

                ('PROFESSIONAL', 'Professional',
                 'API access, Excel bulk upload, SMS notifications, multi-module operations.',
                 2, 5, 25, 3, 100000, 1024, 350000.00, 3500000.00, 14,
                 '[""dashboard_basic"",""email_notifications"",""api_access"",""excel_bulk_upload"",""sms_notifications""]', 1, 2),

                ('ENTERPRISE', 'Enterprise',
                 'SSO, custom domain, advanced report builder, enterprise support.',
                 3, 10, 50, 10, 500000, 5120, 750000.00, 7500000.00, 14,
                 '[""dashboard_basic"",""email_notifications"",""api_access"",""excel_bulk_upload"",""sms_notifications"",""sso"",""custom_domain"",""report_builder""]', 1, 3),

                ('GROUP', 'Group',
                 'Consolidation, benchmarking, dedicated support for holding structures.',
                 4, 14, 100, 25, 1000000, 10240, 1500000.00, 15000000.00, 14,
                 '[""all_features"",""consolidation"",""benchmarking"",""dedicated_support""]', 1, 4),

                ('REGULATOR', 'Regulator',
                 'Regulator workspace with sector analytics and supervision tooling. Custom commercial terms apply.',
                 5, 1000, 5000, 1000, 0, 20480, 0.00, 0.00, 0,
                 '[""all_features"",""sector_analytics"",""examination_workspace""]', 1, 5),

                ('WHITE_LABEL', 'White Label',
                 'Full white-label partner management with custom commercial terms.',
                 6, 1000, 5000, 5000, 0, 51200, 0.00, 0.00, 0,
                 '[""all_features"",""white_label"",""partner_management"",""custom_branding""]', 1, 6);
        ");

        // ───────────────────────────────────────────────────────────
        // 2) Create billing/subscription core tables
        // ───────────────────────────────────────────────────────────
        migrationBuilder.Sql(@"
            IF OBJECT_ID(N'dbo.plan_module_pricing', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.plan_module_pricing (
                    Id                  INT             IDENTITY(1,1) PRIMARY KEY,
                    PlanId              INT             NOT NULL REFERENCES dbo.subscription_plans(Id),
                    ModuleId            INT             NOT NULL REFERENCES dbo.modules(Id),
                    PriceMonthly        DECIMAL(18,2)   NOT NULL,
                    PriceAnnual         DECIMAL(18,2)   NOT NULL,
                    IsIncludedInBase    BIT             NOT NULL DEFAULT 0,
                    CONSTRAINT UQ_plan_module_pricing UNIQUE (PlanId, ModuleId)
                );

                CREATE INDEX IX_plan_module_pricing_PlanId ON dbo.plan_module_pricing(PlanId);
                CREATE INDEX IX_plan_module_pricing_ModuleId ON dbo.plan_module_pricing(ModuleId);
            END;

            IF OBJECT_ID(N'dbo.subscriptions', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.subscriptions (
                    Id                  INT                 IDENTITY(1,1) PRIMARY KEY,
                    TenantId            UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    PlanId              INT                 NOT NULL REFERENCES dbo.subscription_plans(Id),
                    Status              NVARCHAR(20)        NOT NULL DEFAULT 'Trial',
                    BillingFrequency    NVARCHAR(10)        NOT NULL DEFAULT 'Monthly',
                    CurrentPeriodStart  DATETIME2           NOT NULL,
                    CurrentPeriodEnd    DATETIME2           NOT NULL,
                    TrialEndsAt         DATETIME2           NULL,
                    GracePeriodEndsAt   DATETIME2           NULL,
                    CancelledAt         DATETIME2           NULL,
                    CancellationReason  NVARCHAR(500)       NULL,
                    CreatedAt           DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
                    UpdatedAt           DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME()
                );

                CREATE INDEX IX_subscriptions_TenantId ON dbo.subscriptions(TenantId);
                CREATE INDEX IX_subscriptions_TenantId_Status ON dbo.subscriptions(TenantId, Status);
            END;

            IF OBJECT_ID(N'dbo.subscription_modules', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.subscription_modules (
                    Id              INT             IDENTITY(1,1) PRIMARY KEY,
                    SubscriptionId  INT             NOT NULL REFERENCES dbo.subscriptions(Id),
                    ModuleId        INT             NOT NULL REFERENCES dbo.modules(Id),
                    ActivatedAt     DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
                    DeactivatedAt   DATETIME2       NULL,
                    PriceMonthly    DECIMAL(18,2)   NOT NULL,
                    PriceAnnual     DECIMAL(18,2)   NOT NULL,
                    IsActive        BIT             NOT NULL DEFAULT 1,
                    CONSTRAINT UQ_subscription_modules UNIQUE (SubscriptionId, ModuleId)
                );

                CREATE INDEX IX_subscription_modules_SubscriptionId_IsActive
                    ON dbo.subscription_modules(SubscriptionId, IsActive);
            END;

            IF OBJECT_ID(N'dbo.invoices', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.invoices (
                    Id              INT                 IDENTITY(1,1) PRIMARY KEY,
                    TenantId        UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    InvoiceNumber   NVARCHAR(50)        NOT NULL,
                    SubscriptionId  INT                 NOT NULL REFERENCES dbo.subscriptions(Id),
                    PeriodStart     DATE                NOT NULL,
                    PeriodEnd       DATE                NOT NULL,
                    Subtotal        DECIMAL(18,2)       NOT NULL,
                    VatRate         DECIMAL(5,4)        NOT NULL DEFAULT 0.0750,
                    VatAmount       DECIMAL(18,2)       NOT NULL,
                    TotalAmount     DECIMAL(18,2)       NOT NULL,
                    Currency        NVARCHAR(3)         NOT NULL DEFAULT 'NGN',
                    Status          NVARCHAR(20)        NOT NULL DEFAULT 'Draft',
                    IssuedAt        DATETIME2           NULL,
                    DueDate         DATE                NULL,
                    PaidAt          DATETIME2           NULL,
                    VoidedAt        DATETIME2           NULL,
                    VoidReason      NVARCHAR(500)       NULL,
                    Notes           NVARCHAR(1000)      NULL,
                    CreatedAt       DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT UQ_invoices_InvoiceNumber UNIQUE (InvoiceNumber)
                );

                CREATE INDEX IX_invoices_TenantId ON dbo.invoices(TenantId);
                CREATE INDEX IX_invoices_TenantId_Status_DueDate ON dbo.invoices(TenantId, Status, DueDate);
            END;

            IF OBJECT_ID(N'dbo.invoice_line_items', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.invoice_line_items (
                    Id              INT             IDENTITY(1,1) PRIMARY KEY,
                    InvoiceId       INT             NOT NULL REFERENCES dbo.invoices(Id) ON DELETE CASCADE,
                    LineType        NVARCHAR(20)    NOT NULL,
                    Description     NVARCHAR(200)   NOT NULL,
                    ModuleId        INT             NULL REFERENCES dbo.modules(Id),
                    Quantity        INT             NOT NULL DEFAULT 1,
                    UnitPrice       DECIMAL(18,2)   NOT NULL,
                    LineTotal       DECIMAL(18,2)   NOT NULL,
                    DisplayOrder    INT             NOT NULL DEFAULT 0
                );

                CREATE INDEX IX_invoice_line_items_InvoiceId_DisplayOrder
                    ON dbo.invoice_line_items(InvoiceId, DisplayOrder);
            END;

            IF OBJECT_ID(N'dbo.payments', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.payments (
                    Id                      INT                 IDENTITY(1,1) PRIMARY KEY,
                    TenantId                UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    InvoiceId               INT                 NOT NULL REFERENCES dbo.invoices(Id),
                    Amount                  DECIMAL(18,2)       NOT NULL,
                    Currency                NVARCHAR(3)         NOT NULL DEFAULT 'NGN',
                    PaymentMethod           NVARCHAR(30)        NOT NULL,
                    PaymentReference        NVARCHAR(100)       NULL,
                    ProviderTransactionId   NVARCHAR(100)       NULL,
                    ProviderName            NVARCHAR(30)        NULL,
                    Status                  NVARCHAR(20)        NOT NULL DEFAULT 'Pending',
                    PaidAt                  DATETIME2           NULL,
                    FailureReason           NVARCHAR(500)       NULL,
                    CreatedAt               DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME()
                );

                CREATE INDEX IX_payments_TenantId ON dbo.payments(TenantId);
                CREATE INDEX IX_payments_InvoiceId ON dbo.payments(InvoiceId);
            END;

            IF OBJECT_ID(N'dbo.usage_records', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.usage_records (
                    Id                  INT                 IDENTITY(1,1) PRIMARY KEY,
                    TenantId            UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    RecordDate          DATE                NOT NULL,
                    ActiveUsers         INT                 NOT NULL DEFAULT 0,
                    ActiveEntities      INT                 NOT NULL DEFAULT 0,
                    ActiveModules       INT                 NOT NULL DEFAULT 0,
                    ReturnsSubmitted    INT                 NOT NULL DEFAULT 0,
                    StorageUsedMb       DECIMAL(18,2)       NOT NULL DEFAULT 0,
                    ApiCallCount        INT                 NOT NULL DEFAULT 0,
                    CONSTRAINT UQ_usage_records UNIQUE (TenantId, RecordDate)
                );

                CREATE INDEX IX_usage_records_TenantId ON dbo.usage_records(TenantId);
            END;
        ");

        // ───────────────────────────────────────────────────────────
        // 3) Seed plan-module pricing matrix
        // ───────────────────────────────────────────────────────────
        migrationBuilder.Sql(@"
            DELETE FROM dbo.plan_module_pricing;

            DECLARE @starter INT = (SELECT Id FROM dbo.subscription_plans WHERE PlanCode = 'STARTER');
            DECLARE @professional INT = (SELECT Id FROM dbo.subscription_plans WHERE PlanCode = 'PROFESSIONAL');
            DECLARE @enterprise INT = (SELECT Id FROM dbo.subscription_plans WHERE PlanCode = 'ENTERPRISE');
            DECLARE @group INT = (SELECT Id FROM dbo.subscription_plans WHERE PlanCode = 'GROUP');
            DECLARE @regulator INT = (SELECT Id FROM dbo.subscription_plans WHERE PlanCode = 'REGULATOR');
            DECLARE @whiteLabel INT = (SELECT Id FROM dbo.subscription_plans WHERE PlanCode = 'WHITE_LABEL');

            DECLARE @pricing TABLE (
                PlanId INT,
                ModuleCode NVARCHAR(30),
                PriceMonthly DECIMAL(18,2),
                PriceAnnual DECIMAL(18,2),
                IsIncludedInBase BIT
            );

            -- STARTER
            INSERT INTO @pricing VALUES
                (@starter, 'FC_RETURNS', 0, 0, 1),
                (@starter, 'BDC_CBN', 50000, 500000, 0),
                (@starter, 'MFB_PAR', 50000, 500000, 0),
                (@starter, 'PSP_FINTECH', 60000, 600000, 0),
                (@starter, 'NDIC_RETURNS', 40000, 400000, 0),
                (@starter, 'NFIU_AML', 40000, 400000, 0),
                (@starter, 'INSURANCE_NAICOM', 60000, 600000, 0),
                (@starter, 'PFA_PENCOM', 60000, 600000, 0),
                (@starter, 'CMO_SEC', 60000, 600000, 0),
                (@starter, 'DFI_CBN', 50000, 500000, 0),
                (@starter, 'PMB_CBN', 50000, 500000, 0),
                (@starter, 'IMTO_CBN', 50000, 500000, 0);

            -- PROFESSIONAL
            INSERT INTO @pricing VALUES
                (@professional, 'FC_RETURNS', 0, 0, 1),
                (@professional, 'BDC_CBN', 40000, 400000, 0),
                (@professional, 'MFB_PAR', 40000, 400000, 0),
                (@professional, 'PSP_FINTECH', 50000, 500000, 0),
                (@professional, 'NDIC_RETURNS', 30000, 300000, 0),
                (@professional, 'NFIU_AML', 30000, 300000, 0),
                (@professional, 'INSURANCE_NAICOM', 50000, 500000, 0),
                (@professional, 'PFA_PENCOM', 50000, 500000, 0),
                (@professional, 'CMO_SEC', 50000, 500000, 0),
                (@professional, 'DFI_CBN', 40000, 400000, 0),
                (@professional, 'PMB_CBN', 40000, 400000, 0),
                (@professional, 'IMTO_CBN', 40000, 400000, 0);

            -- ENTERPRISE
            INSERT INTO @pricing VALUES
                (@enterprise, 'FC_RETURNS', 0, 0, 1),
                (@enterprise, 'BDC_CBN', 30000, 300000, 0),
                (@enterprise, 'DMB_BASEL3', 100000, 1000000, 0),
                (@enterprise, 'MFB_PAR', 30000, 300000, 0),
                (@enterprise, 'PSP_FINTECH', 40000, 400000, 0),
                (@enterprise, 'NDIC_RETURNS', 25000, 250000, 0),
                (@enterprise, 'NFIU_AML', 25000, 250000, 0),
                (@enterprise, 'INSURANCE_NAICOM', 40000, 400000, 0),
                (@enterprise, 'PFA_PENCOM', 40000, 400000, 0),
                (@enterprise, 'CMO_SEC', 40000, 400000, 0),
                (@enterprise, 'DFI_CBN', 30000, 300000, 0),
                (@enterprise, 'PMB_CBN', 30000, 300000, 0),
                (@enterprise, 'IMTO_CBN', 30000, 300000, 0),
                (@enterprise, 'FATF_EVAL', 75000, 750000, 0),
                (@enterprise, 'ESG_CLIMATE', 75000, 750000, 0);

            -- GROUP
            INSERT INTO @pricing VALUES
                (@group, 'FC_RETURNS', 0, 0, 1),
                (@group, 'BDC_CBN', 25000, 250000, 0),
                (@group, 'DMB_BASEL3', 80000, 800000, 0),
                (@group, 'MFB_PAR', 25000, 250000, 0),
                (@group, 'PSP_FINTECH', 30000, 300000, 0),
                (@group, 'NDIC_RETURNS', 20000, 200000, 0),
                (@group, 'NFIU_AML', 20000, 200000, 0),
                (@group, 'INSURANCE_NAICOM', 30000, 300000, 0),
                (@group, 'PFA_PENCOM', 30000, 300000, 0),
                (@group, 'CMO_SEC', 30000, 300000, 0),
                (@group, 'DFI_CBN', 25000, 250000, 0),
                (@group, 'PMB_CBN', 25000, 250000, 0),
                (@group, 'IMTO_CBN', 25000, 250000, 0),
                (@group, 'FATF_EVAL', 60000, 600000, 0),
                (@group, 'ESG_CLIMATE', 60000, 600000, 0);

            -- REGULATOR + WHITE_LABEL: all modules included in base
            INSERT INTO @pricing (PlanId, ModuleCode, PriceMonthly, PriceAnnual, IsIncludedInBase)
            SELECT @regulator, ModuleCode, 0, 0, 1 FROM dbo.modules;

            INSERT INTO @pricing (PlanId, ModuleCode, PriceMonthly, PriceAnnual, IsIncludedInBase)
            SELECT @whiteLabel, ModuleCode, 0, 0, 1 FROM dbo.modules;

            INSERT INTO dbo.plan_module_pricing (PlanId, ModuleId, PriceMonthly, PriceAnnual, IsIncludedInBase)
            SELECT DISTINCT
                p.PlanId,
                m.Id,
                p.PriceMonthly,
                p.PriceAnnual,
                p.IsIncludedInBase
            FROM @pricing p
            INNER JOIN dbo.modules m ON m.ModuleCode = p.ModuleCode;
        ");

        // ───────────────────────────────────────────────────────────
        // 4) Bootstrap subscriptions for existing tenants + module activation
        // ───────────────────────────────────────────────────────────
        migrationBuilder.Sql(@"
            DECLARE @groupPlanId INT = (SELECT Id FROM dbo.subscription_plans WHERE PlanCode = 'GROUP');
            DECLARE @now DATETIME2 = SYSUTCDATETIME();
            DECLARE @periodStart DATETIME2 = CAST(DATEFROMPARTS(YEAR(@now), MONTH(@now), 1) AS DATETIME2);

            INSERT INTO dbo.subscriptions
                (TenantId, PlanId, Status, BillingFrequency, CurrentPeriodStart, CurrentPeriodEnd, TrialEndsAt, CreatedAt, UpdatedAt)
            SELECT
                t.TenantId,
                @groupPlanId,
                'Active',
                'Monthly',
                @periodStart,
                DATEADD(MONTH, 1, @periodStart),
                NULL,
                SYSUTCDATETIME(),
                SYSUTCDATETIME()
            FROM dbo.tenants t
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.subscriptions s
                WHERE s.TenantId = t.TenantId
                  AND s.Status NOT IN ('Cancelled', 'Expired')
            );

            INSERT INTO dbo.subscription_modules
                (SubscriptionId, ModuleId, ActivatedAt, PriceMonthly, PriceAnnual, IsActive)
            SELECT DISTINCT
                s.Id,
                lmm.ModuleId,
                SYSUTCDATETIME(),
                pmp.PriceMonthly,
                pmp.PriceAnnual,
                1
            FROM dbo.subscriptions s
            INNER JOIN dbo.tenant_licence_types tlt
                ON tlt.TenantId = s.TenantId AND tlt.IsActive = 1
            INNER JOIN dbo.licence_module_matrix lmm
                ON lmm.LicenceTypeId = tlt.LicenceTypeId
            INNER JOIN dbo.plan_module_pricing pmp
                ON pmp.PlanId = s.PlanId AND pmp.ModuleId = lmm.ModuleId
            WHERE s.Status IN ('Trial','Active','PastDue','Suspended')
              AND NOT EXISTS (
                  SELECT 1
                  FROM dbo.subscription_modules sm
                  WHERE sm.SubscriptionId = s.Id
                    AND sm.ModuleId = lmm.ModuleId
              );
        ");

        // ───────────────────────────────────────────────────────────
        // 5) Rebuild RLS policy to include new TenantId tables
        // ───────────────────────────────────────────────────────────
        migrationBuilder.Sql(@"
            IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = 'TenantSecurityPolicy')
                DROP SECURITY POLICY dbo.TenantSecurityPolicy;

            DECLARE @sql NVARCHAR(MAX) = N'CREATE SECURITY POLICY dbo.TenantSecurityPolicy' + CHAR(13);
            DECLARE @first BIT = 1;

            DECLARE table_cursor CURSOR FOR
                SELECT DISTINCT s.name AS SchemaName, t.name AS TableName
                FROM sys.columns c
                INNER JOIN sys.tables t ON c.object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE c.name = 'TenantId'
                  AND t.name != 'tenants'
                ORDER BY s.name, t.name;

            DECLARE @schemaName NVARCHAR(128), @tableName NVARCHAR(128);
            OPEN table_cursor;
            FETCH NEXT FROM table_cursor INTO @schemaName, @tableName;

            WHILE @@FETCH_STATUS = 0
            BEGIN
                IF @first = 1
                BEGIN
                    SET @sql = @sql + N'    ADD FILTER PREDICATE dbo.fn_TenantFilter(TenantId) ON [' + @schemaName + N'].[' + @tableName + N'],' + CHAR(13);
                    SET @sql = @sql + N'    ADD BLOCK PREDICATE dbo.fn_TenantFilter(TenantId) ON [' + @schemaName + N'].[' + @tableName + N']';
                    SET @first = 0;
                END
                ELSE
                BEGIN
                    SET @sql = @sql + N',' + CHAR(13);
                    SET @sql = @sql + N'    ADD FILTER PREDICATE dbo.fn_TenantFilter(TenantId) ON [' + @schemaName + N'].[' + @tableName + N'],' + CHAR(13);
                    SET @sql = @sql + N'    ADD BLOCK PREDICATE dbo.fn_TenantFilter(TenantId) ON [' + @schemaName + N'].[' + @tableName + N']';
                END

                FETCH NEXT FROM table_cursor INTO @schemaName, @tableName;
            END

            CLOSE table_cursor;
            DEALLOCATE table_cursor;

            SET @sql = @sql + CHAR(13) + N'    WITH (STATE = ON);';
            EXEC sp_executesql @sql;
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = 'TenantSecurityPolicy')
                DROP SECURITY POLICY dbo.TenantSecurityPolicy;
        ");

        migrationBuilder.Sql(@"
            DROP TABLE IF EXISTS dbo.usage_records;
            DROP TABLE IF EXISTS dbo.payments;
            DROP TABLE IF EXISTS dbo.invoice_line_items;
            DROP TABLE IF EXISTS dbo.invoices;
            DROP TABLE IF EXISTS dbo.subscription_modules;
            DROP TABLE IF EXISTS dbo.subscriptions;
            DROP TABLE IF EXISTS dbo.plan_module_pricing;
        ");

        migrationBuilder.Sql(@"
            -- Drop default constraints for RG-03 columns before dropping columns.
            DECLARE @dropDefaults NVARCHAR(MAX) = N'';

            SELECT @dropDefaults = @dropDefaults +
                N'ALTER TABLE dbo.subscription_plans DROP CONSTRAINT [' + dc.name + N'];' + CHAR(13)
            FROM sys.default_constraints dc
            INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
            INNER JOIN sys.tables t ON t.object_id = c.object_id
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = 'dbo'
              AND t.name = 'subscription_plans'
              AND c.name IN (
                    'Tier',
                    'MaxEntities',
                    'MaxApiCallsPerMonth',
                    'MaxStorageMb',
                    'BasePriceMonthly',
                    'BasePriceAnnual',
                    'TrialDays'
              );

            IF LEN(@dropDefaults) > 0
                EXEC sp_executesql @dropDefaults;

            IF COL_LENGTH('dbo.subscription_plans', 'Tier') IS NOT NULL
                ALTER TABLE dbo.subscription_plans DROP COLUMN Tier;

            IF COL_LENGTH('dbo.subscription_plans', 'MaxEntities') IS NOT NULL
                ALTER TABLE dbo.subscription_plans DROP COLUMN MaxEntities;

            IF COL_LENGTH('dbo.subscription_plans', 'MaxApiCallsPerMonth') IS NOT NULL
                ALTER TABLE dbo.subscription_plans DROP COLUMN MaxApiCallsPerMonth;

            IF COL_LENGTH('dbo.subscription_plans', 'MaxStorageMb') IS NOT NULL
                ALTER TABLE dbo.subscription_plans DROP COLUMN MaxStorageMb;

            IF COL_LENGTH('dbo.subscription_plans', 'BasePriceMonthly') IS NOT NULL
                ALTER TABLE dbo.subscription_plans DROP COLUMN BasePriceMonthly;

            IF COL_LENGTH('dbo.subscription_plans', 'BasePriceAnnual') IS NOT NULL
                ALTER TABLE dbo.subscription_plans DROP COLUMN BasePriceAnnual;

            IF COL_LENGTH('dbo.subscription_plans', 'TrialDays') IS NOT NULL
                ALTER TABLE dbo.subscription_plans DROP COLUMN TrialDays;
        ");

        migrationBuilder.Sql(@"
            DELETE FROM dbo.subscription_plans;

            INSERT INTO dbo.subscription_plans
                (PlanCode, PlanName, Description, MaxInstitutions, MaxUsersPerEntity, MaxModules, AllModulesIncluded, Features, DisplayOrder)
            VALUES
                ('STARTER', 'Starter', 'Basic plan for small institutions with a single regulatory module. Ideal for finance companies, BDCs, and other single-licence entities.', 1, 10, 1, 0, 'xml_submission,validation,reporting', 1),
                ('PROFESSIONAL', 'Professional', 'Mid-tier plan for institutions that need multiple regulatory modules, API access, and advanced reporting capabilities.', 3, 25, 5, 0, 'xml_submission,validation,reporting,api_access,bulk_upload,advanced_reporting', 2),
                ('ENTERPRISE', 'Enterprise', 'Full-featured plan for large institutions requiring all modules, unlimited users, and premium support including white-label capabilities.', 10, 100, 999, 1, 'xml_submission,validation,reporting,api_access,bulk_upload,advanced_reporting,white_label,priority_support,custom_branding', 3),
                ('GROUP', 'Group', 'Holding group plan for conglomerates managing multiple subsidiaries across different licence categories with consolidated reporting.', 50, 200, 999, 1, 'xml_submission,validation,reporting,api_access,bulk_upload,advanced_reporting,white_label,priority_support,custom_branding,consolidated_reporting,subsidiary_management', 4);
        ");

        migrationBuilder.Sql(@"
            DECLARE @sql NVARCHAR(MAX) = N'CREATE SECURITY POLICY dbo.TenantSecurityPolicy' + CHAR(13);
            DECLARE @first BIT = 1;

            DECLARE table_cursor CURSOR FOR
                SELECT DISTINCT s.name AS SchemaName, t.name AS TableName
                FROM sys.columns c
                INNER JOIN sys.tables t ON c.object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE c.name = 'TenantId'
                  AND t.name != 'tenants'
                ORDER BY s.name, t.name;

            DECLARE @schemaName NVARCHAR(128), @tableName NVARCHAR(128);
            OPEN table_cursor;
            FETCH NEXT FROM table_cursor INTO @schemaName, @tableName;

            WHILE @@FETCH_STATUS = 0
            BEGIN
                IF @first = 1
                BEGIN
                    SET @sql = @sql + N'    ADD FILTER PREDICATE dbo.fn_TenantFilter(TenantId) ON [' + @schemaName + N'].[' + @tableName + N'],' + CHAR(13);
                    SET @sql = @sql + N'    ADD BLOCK PREDICATE dbo.fn_TenantFilter(TenantId) ON [' + @schemaName + N'].[' + @tableName + N']';
                    SET @first = 0;
                END
                ELSE
                BEGIN
                    SET @sql = @sql + N',' + CHAR(13);
                    SET @sql = @sql + N'    ADD FILTER PREDICATE dbo.fn_TenantFilter(TenantId) ON [' + @schemaName + N'].[' + @tableName + N'],' + CHAR(13);
                    SET @sql = @sql + N'    ADD BLOCK PREDICATE dbo.fn_TenantFilter(TenantId) ON [' + @schemaName + N'].[' + @tableName + N']';
                END

                FETCH NEXT FROM table_cursor INTO @schemaName, @tableName;
            END

            CLOSE table_cursor;
            DEALLOCATE table_cursor;

            SET @sql = @sql + CHAR(13) + N'    WITH (STATE = ON);';
            EXEC sp_executesql @sql;
        ");
    }
}
