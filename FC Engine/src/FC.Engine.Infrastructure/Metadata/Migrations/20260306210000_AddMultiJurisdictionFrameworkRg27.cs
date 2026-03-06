#nullable enable
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FC.Engine.Infrastructure.Metadata.Migrations;

[DbContext(typeof(MetadataDbContext))]
[Migration("20260306210000_AddMultiJurisdictionFrameworkRg27")]
public partial class AddMultiJurisdictionFrameworkRg27 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF OBJECT_ID(N'dbo.jurisdictions', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.jurisdictions (
                    Id                  INT             IDENTITY(1,1) PRIMARY KEY,
                    CountryCode         NVARCHAR(2)     NOT NULL UNIQUE,
                    CountryName         NVARCHAR(100)   NOT NULL,
                    Currency            NVARCHAR(3)     NOT NULL,
                    Timezone            NVARCHAR(100)   NOT NULL,
                    RegulatoryBodies    NVARCHAR(MAX)   NOT NULL,
                    DateFormat          NVARCHAR(20)    NOT NULL DEFAULT 'dd/MM/yyyy',
                    DataProtectionLaw   NVARCHAR(100)   NULL,
                    DataResidencyRegion NVARCHAR(50)    NOT NULL,
                    IsActive            BIT             NOT NULL DEFAULT 0
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM dbo.jurisdictions WHERE CountryCode = 'NG')
            BEGIN
                SET IDENTITY_INSERT dbo.jurisdictions ON;
                INSERT INTO dbo.jurisdictions
                    (Id, CountryCode, CountryName, Currency, Timezone, RegulatoryBodies, DateFormat, DataProtectionLaw, DataResidencyRegion, IsActive)
                VALUES
                    (1, 'NG', 'Nigeria', 'NGN', 'Africa/Lagos', '[""CBN"",""NDIC"",""SEC"",""NAICOM"",""PenCom"",""NFIU""]', 'dd/MM/yyyy', 'NDPR/NDPA 2023', 'SouthAfricaNorth', 1);
                SET IDENTITY_INSERT dbo.jurisdictions OFF;
            END;

            IF NOT EXISTS (SELECT 1 FROM dbo.jurisdictions WHERE CountryCode = 'GH')
            BEGIN
                IF EXISTS (SELECT 1 FROM dbo.jurisdictions WHERE Id = 2)
                    INSERT INTO dbo.jurisdictions
                        (CountryCode, CountryName, Currency, Timezone, RegulatoryBodies, DateFormat, DataProtectionLaw, DataResidencyRegion, IsActive)
                    VALUES
                        ('GH', 'Ghana', 'GHS', 'Africa/Accra', '[""BOG"",""SEC_GH"",""NIC_GH""]', 'dd/MM/yyyy', 'Ghana DPA 2012', 'WestEurope', 0);
                ELSE
                BEGIN
                    SET IDENTITY_INSERT dbo.jurisdictions ON;
                    INSERT INTO dbo.jurisdictions
                        (Id, CountryCode, CountryName, Currency, Timezone, RegulatoryBodies, DateFormat, DataProtectionLaw, DataResidencyRegion, IsActive)
                    VALUES
                        (2, 'GH', 'Ghana', 'GHS', 'Africa/Accra', '[""BOG"",""SEC_GH"",""NIC_GH""]', 'dd/MM/yyyy', 'Ghana DPA 2012', 'WestEurope', 0);
                    SET IDENTITY_INSERT dbo.jurisdictions OFF;
                END
            END;

            IF NOT EXISTS (SELECT 1 FROM dbo.jurisdictions WHERE CountryCode = 'KE')
            BEGIN
                IF EXISTS (SELECT 1 FROM dbo.jurisdictions WHERE Id = 3)
                    INSERT INTO dbo.jurisdictions
                        (CountryCode, CountryName, Currency, Timezone, RegulatoryBodies, DateFormat, DataProtectionLaw, DataResidencyRegion, IsActive)
                    VALUES
                        ('KE', 'Kenya', 'KES', 'Africa/Nairobi', '[""CBK"",""CMA_KE"",""IRA_KE""]', 'dd/MM/yyyy', 'Kenya DPA 2019', 'UAE North', 0);
                ELSE
                BEGIN
                    SET IDENTITY_INSERT dbo.jurisdictions ON;
                    INSERT INTO dbo.jurisdictions
                        (Id, CountryCode, CountryName, Currency, Timezone, RegulatoryBodies, DateFormat, DataProtectionLaw, DataResidencyRegion, IsActive)
                    VALUES
                        (3, 'KE', 'Kenya', 'KES', 'Africa/Nairobi', '[""CBK"",""CMA_KE"",""IRA_KE""]', 'dd/MM/yyyy', 'Kenya DPA 2019', 'UAE North', 0);
                    SET IDENTITY_INSERT dbo.jurisdictions OFF;
                END
            END;

            IF COL_LENGTH('dbo.modules', 'JurisdictionId') IS NULL
                ALTER TABLE dbo.modules ADD JurisdictionId INT NULL;

            IF COL_LENGTH('dbo.institutions', 'JurisdictionId') IS NULL
                ALTER TABLE dbo.institutions ADD JurisdictionId INT NULL;

            IF COL_LENGTH('meta.institution_users', 'PreferredLanguage') IS NULL
                ALTER TABLE meta.institution_users
                    ADD PreferredLanguage NVARCHAR(10) NOT NULL
                        CONSTRAINT DF_institution_users_PreferredLanguage DEFAULT 'en';
        ");

        migrationBuilder.Sql(@"
            IF EXISTS (SELECT 1 FROM dbo.modules WHERE JurisdictionId IS NULL AND ModuleCode NOT IN ('FATF_EVAL', 'ESG_CLIMATE'))
                UPDATE dbo.modules
                SET JurisdictionId = 1
                WHERE JurisdictionId IS NULL
                  AND ModuleCode NOT IN ('FATF_EVAL', 'ESG_CLIMATE');

            IF EXISTS (SELECT 1 FROM dbo.institutions WHERE JurisdictionId IS NULL)
                UPDATE dbo.institutions
                SET JurisdictionId = 1
                WHERE JurisdictionId IS NULL;

            IF EXISTS (SELECT 1 FROM dbo.institutions WHERE JurisdictionId IS NULL)
                UPDATE dbo.institutions
                SET JurisdictionId = (SELECT TOP 1 Id FROM dbo.jurisdictions WHERE CountryCode = 'NG')
                WHERE JurisdictionId IS NULL;

            IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_modules_jurisdictions_JurisdictionId')
                ALTER TABLE dbo.modules DROP CONSTRAINT FK_modules_jurisdictions_JurisdictionId;

            IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_institutions_jurisdictions_JurisdictionId')
                ALTER TABLE dbo.institutions DROP CONSTRAINT FK_institutions_jurisdictions_JurisdictionId;

            ALTER TABLE dbo.institutions ALTER COLUMN JurisdictionId INT NOT NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_modules_jurisdictions_JurisdictionId')
                ALTER TABLE dbo.modules
                    ADD CONSTRAINT FK_modules_jurisdictions_JurisdictionId
                    FOREIGN KEY (JurisdictionId) REFERENCES dbo.jurisdictions(Id);

            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_institutions_jurisdictions_JurisdictionId')
                ALTER TABLE dbo.institutions
                    ADD CONSTRAINT FK_institutions_jurisdictions_JurisdictionId
                    FOREIGN KEY (JurisdictionId) REFERENCES dbo.jurisdictions(Id);

            DECLARE @fkName NVARCHAR(256);
            DECLARE @fkDropSql NVARCHAR(MAX);
            DECLARE fk_cursor CURSOR FAST_FORWARD FOR
                SELECT fk.name
                FROM sys.foreign_keys fk
                INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
                INNER JOIN sys.indexes ix ON fkc.referenced_object_id = ix.object_id
                WHERE ix.name = 'IX_modules_ModuleCode'
                  AND fkc.referenced_object_id = OBJECT_ID('dbo.modules');
            OPEN fk_cursor;
            FETCH NEXT FROM fk_cursor INTO @fkName;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                DECLARE @fkTable NVARCHAR(512);
                SELECT @fkTable = QUOTENAME(s.name) + '.' + QUOTENAME(t.name)
                FROM sys.foreign_keys fk2
                INNER JOIN sys.tables t ON fk2.parent_object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE fk2.name = @fkName;
                SET @fkDropSql = N'ALTER TABLE ' + @fkTable + N' DROP CONSTRAINT ' + QUOTENAME(@fkName);
                EXEC sp_executesql @fkDropSql;
                FETCH NEXT FROM fk_cursor INTO @fkName;
            END;
            CLOSE fk_cursor;
            DEALLOCATE fk_cursor;

            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_modules_ModuleCode' AND object_id = OBJECT_ID('dbo.modules'))
                DROP INDEX IX_modules_ModuleCode ON dbo.modules;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_modules_ModuleCode_JurisdictionId' AND object_id = OBJECT_ID('dbo.modules'))
                CREATE UNIQUE INDEX IX_modules_ModuleCode_JurisdictionId
                    ON dbo.modules(ModuleCode, JurisdictionId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_modules_JurisdictionId' AND object_id = OBJECT_ID('dbo.modules'))
                CREATE INDEX IX_modules_JurisdictionId ON dbo.modules(JurisdictionId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_institutions_JurisdictionId' AND object_id = OBJECT_ID('dbo.institutions'))
                CREATE INDEX IX_institutions_JurisdictionId ON dbo.institutions(JurisdictionId);

            IF OBJECT_ID(N'dbo.field_localisations', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.field_localisations (
                    Id                  INT             IDENTITY(1,1) PRIMARY KEY,
                    FieldId             INT             NOT NULL,
                    LanguageCode        NVARCHAR(5)     NOT NULL,
                    LocalisedLabel      NVARCHAR(200)   NOT NULL,
                    LocalisedHelpText   NVARCHAR(500)   NULL,
                    CONSTRAINT UQ_field_localisations_FieldId_LanguageCode UNIQUE (FieldId, LanguageCode),
                    CONSTRAINT FK_field_localisations_template_fields_FieldId
                        FOREIGN KEY (FieldId) REFERENCES meta.template_fields(Id) ON DELETE CASCADE
                );
            END;

            IF OBJECT_ID(N'dbo.jurisdiction_fx_rates', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.jurisdiction_fx_rates (
                    Id              INT             IDENTITY(1,1) PRIMARY KEY,
                    BaseCurrency    NVARCHAR(3)     NOT NULL,
                    QuoteCurrency   NVARCHAR(3)     NOT NULL,
                    Rate            DECIMAL(18,8)   NOT NULL,
                    RateDate        DATE            NOT NULL,
                    Source          NVARCHAR(50)    NOT NULL DEFAULT 'Manual',
                    CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT UQ_jurisdiction_fx_rates_BaseQuoteDate UNIQUE (BaseCurrency, QuoteCurrency, RateDate)
                );
            END;

            IF OBJECT_ID(N'dbo.consolidation_adjustments', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.consolidation_adjustments (
                    Id                  INT                 IDENTITY(1,1) PRIMARY KEY,
                    TenantId            UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    SourceInstitutionId INT                 NULL REFERENCES dbo.institutions(Id),
                    TargetInstitutionId INT                 NULL REFERENCES dbo.institutions(Id),
                    AdjustmentType      NVARCHAR(30)        NOT NULL DEFAULT 'Elimination',
                    Amount              DECIMAL(18,2)       NOT NULL,
                    Currency            NVARCHAR(3)         NOT NULL DEFAULT 'NGN',
                    Description         NVARCHAR(500)       NULL,
                    EffectiveDate       DATE                NOT NULL,
                    CreatedAt           DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME()
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_consolidation_adjustments_TenantId' AND object_id = OBJECT_ID('dbo.consolidation_adjustments'))
                CREATE INDEX IX_consolidation_adjustments_TenantId
                    ON dbo.consolidation_adjustments(TenantId);
        ");

        RebuildTenantSecurityPolicy(migrationBuilder);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF OBJECT_ID(N'dbo.consolidation_adjustments', N'U') IS NOT NULL
                DROP TABLE dbo.consolidation_adjustments;

            IF OBJECT_ID(N'dbo.jurisdiction_fx_rates', N'U') IS NOT NULL
                DROP TABLE dbo.jurisdiction_fx_rates;

            IF OBJECT_ID(N'dbo.field_localisations', N'U') IS NOT NULL
                DROP TABLE dbo.field_localisations;

            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_institutions_JurisdictionId' AND object_id = OBJECT_ID('dbo.institutions'))
                DROP INDEX IX_institutions_JurisdictionId ON dbo.institutions;

            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_modules_JurisdictionId' AND object_id = OBJECT_ID('dbo.modules'))
                DROP INDEX IX_modules_JurisdictionId ON dbo.modules;

            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_modules_ModuleCode_JurisdictionId' AND object_id = OBJECT_ID('dbo.modules'))
                DROP INDEX IX_modules_ModuleCode_JurisdictionId ON dbo.modules;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_modules_ModuleCode' AND object_id = OBJECT_ID('dbo.modules'))
                CREATE UNIQUE INDEX IX_modules_ModuleCode ON dbo.modules(ModuleCode);

            IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_modules_jurisdictions_JurisdictionId')
                ALTER TABLE dbo.modules DROP CONSTRAINT FK_modules_jurisdictions_JurisdictionId;

            IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_institutions_jurisdictions_JurisdictionId')
                ALTER TABLE dbo.institutions DROP CONSTRAINT FK_institutions_jurisdictions_JurisdictionId;

            IF COL_LENGTH('dbo.modules', 'JurisdictionId') IS NOT NULL
                ALTER TABLE dbo.modules DROP COLUMN JurisdictionId;

            IF COL_LENGTH('dbo.institutions', 'JurisdictionId') IS NOT NULL
                ALTER TABLE dbo.institutions DROP COLUMN JurisdictionId;

            IF COL_LENGTH('meta.institution_users', 'PreferredLanguage') IS NOT NULL
                ALTER TABLE meta.institution_users DROP COLUMN PreferredLanguage;

            IF OBJECT_ID(N'dbo.jurisdictions', N'U') IS NOT NULL
                DROP TABLE dbo.jurisdictions;
        ");

        RebuildTenantSecurityPolicy(migrationBuilder);
    }

    private static void RebuildTenantSecurityPolicy(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF OBJECT_ID(N'dbo.TenantSecurityPolicy', N'SP') IS NOT NULL
                DROP SECURITY POLICY dbo.TenantSecurityPolicy;

            IF OBJECT_ID(N'dbo.fn_TenantFilter', N'IF') IS NOT NULL
                DROP FUNCTION dbo.fn_TenantFilter;

            EXEC('
                CREATE FUNCTION dbo.fn_TenantFilter(@TenantId UNIQUEIDENTIFIER)
                RETURNS TABLE
                WITH SCHEMABINDING
                AS
                RETURN
                SELECT 1 AS fn_accessResult
                WHERE @TenantId = CAST(SESSION_CONTEXT(N''TenantId'') AS UNIQUEIDENTIFIER)
                   OR @TenantId IS NULL
                   OR SESSION_CONTEXT(N''TenantId'') IS NULL
                   OR EXISTS (
                        SELECT 1
                        FROM dbo.tenants p
                        WHERE p.TenantId = CAST(SESSION_CONTEXT(N''TenantId'') AS UNIQUEIDENTIFIER)
                          AND p.TenantType = N''WhiteLabelPartner''
                          AND EXISTS (
                                SELECT 1
                                FROM dbo.tenants c
                                WHERE c.TenantId = @TenantId
                                  AND c.ParentTenantId = p.TenantId
                          )
                   );');

            DECLARE @sql NVARCHAR(MAX) = N'CREATE SECURITY POLICY dbo.TenantSecurityPolicy' + CHAR(13);
            DECLARE @first BIT = 1;

            DECLARE tenant_cursor CURSOR FAST_FORWARD FOR
            SELECT s.name AS SchemaName, t.name AS TableName
            FROM sys.tables t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            INNER JOIN sys.columns c ON c.object_id = t.object_id
            WHERE c.name = 'TenantId'
              AND t.is_ms_shipped = 0;

            DECLARE @schemaName SYSNAME, @tableName SYSNAME;
            OPEN tenant_cursor;
            FETCH NEXT FROM tenant_cursor INTO @schemaName, @tableName;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                IF @first = 0 SET @sql += N',' + CHAR(13);
                SET @sql += N'    ADD FILTER PREDICATE dbo.fn_TenantFilter(TenantId) ON [' + @schemaName + N'].[' + @tableName + N']';
                SET @first = 0;
                FETCH NEXT FROM tenant_cursor INTO @schemaName, @tableName;
            END;
            CLOSE tenant_cursor;
            DEALLOCATE tenant_cursor;

            DECLARE tenant_cursor_block CURSOR FAST_FORWARD FOR
            SELECT s.name AS SchemaName, t.name AS TableName
            FROM sys.tables t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            INNER JOIN sys.columns c ON c.object_id = t.object_id
            WHERE c.name = 'TenantId'
              AND t.is_ms_shipped = 0;

            OPEN tenant_cursor_block;
            FETCH NEXT FROM tenant_cursor_block INTO @schemaName, @tableName;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @sql += N',' + CHAR(13)
                         + N'    ADD BLOCK PREDICATE dbo.fn_TenantFilter(TenantId) ON [' + @schemaName + N'].[' + @tableName + N']';
                FETCH NEXT FROM tenant_cursor_block INTO @schemaName, @tableName;
            END;
            CLOSE tenant_cursor_block;
            DEALLOCATE tenant_cursor_block;

            SET @sql += CHAR(13) + N'WITH (STATE = ON);';
            EXEC sp_executesql @sql;
        ");
    }
}
