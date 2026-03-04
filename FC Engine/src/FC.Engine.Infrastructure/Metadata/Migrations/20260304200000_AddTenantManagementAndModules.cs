#nullable enable
using Microsoft.EntityFrameworkCore.Migrations;

namespace FC.Engine.Infrastructure.Metadata.Migrations;

public partial class AddTenantManagementAndModules : Migration
{
    private static readonly Guid LegacyTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ═══════════════════════════════════════════════════════════
        // Step 1: Extend tenants table with new columns
        // ═══════════════════════════════════════════════════════════
        migrationBuilder.Sql(@"
            ALTER TABLE dbo.tenants ADD
                TenantType      NVARCHAR(30)    NOT NULL DEFAULT 'Institution',
                Address         NVARCHAR(500)   NULL,
                TaxId           NVARCHAR(50)    NULL,
                RcNumber        NVARCHAR(50)    NULL,
                FiscalYearStartMonth INT        NOT NULL DEFAULT 1,
                Timezone        NVARCHAR(100)   NOT NULL DEFAULT 'Africa/Lagos',
                DefaultCurrency NVARCHAR(3)     NOT NULL DEFAULT 'NGN',
                BrandingConfig  NVARCHAR(MAX)   NULL,
                CustomDomain    NVARCHAR(255)   NULL,
                DeactivatedAt   DATETIME2       NULL,
                MaxInstitutions INT             NOT NULL DEFAULT 1,
                MaxUsersPerEntity INT           NOT NULL DEFAULT 10;
        ");

        // Update legacy tenant to Active status
        migrationBuilder.Sql($@"
            UPDATE dbo.tenants
            SET TenantStatus = 'Active', TenantType = 'Institution'
            WHERE TenantId = '{LegacyTenantId}';
        ");

        // ═══════════════════════════════════════════════════════════
        // Step 2: Create licence_types table + seed 11 rows
        // ═══════════════════════════════════════════════════════════
        migrationBuilder.Sql(@"
            CREATE TABLE dbo.licence_types (
                Id              INT             IDENTITY(1,1) PRIMARY KEY,
                Code            NVARCHAR(20)    NOT NULL,
                Name            NVARCHAR(200)   NOT NULL,
                Regulator       NVARCHAR(50)    NOT NULL,
                Description     NVARCHAR(500)   NULL,
                IsActive        BIT             NOT NULL DEFAULT 1,
                DisplayOrder    INT             NOT NULL DEFAULT 0,
                CreatedAt       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
            );
            CREATE UNIQUE INDEX IX_licence_types_Code ON dbo.licence_types(Code);
        ");

        migrationBuilder.Sql(@"
            INSERT INTO dbo.licence_types (Code, Name, Regulator, Description, DisplayOrder) VALUES
            ('FC',        'Finance Company',                         'CBN',    'Finance companies regulated by CBN',                    1),
            ('BDC',       'Bureau De Change',                        'CBN',    'Bureau De Change operators regulated by CBN',            2),
            ('DMB',       'Deposit Money Bank',                      'CBN',    'Commercial banks regulated by CBN',                     3),
            ('MFB',       'Microfinance Bank',                       'CBN',    'Microfinance banks regulated by CBN',                   4),
            ('PMB',       'Primary Mortgage Bank',                   'CBN',    'Primary mortgage banks regulated by CBN',               5),
            ('PSP',       'Payment Service Provider / Fintech',      'CBN',    'Payment service providers and fintechs regulated by CBN', 6),
            ('DFI',       'Development Finance Institution',         'CBN',    'Development finance institutions regulated by CBN',     7),
            ('IMTO',      'International Money Transfer Operator',   'CBN',    'IMTOs regulated by CBN',                               8),
            ('INSURANCE', 'Insurance Company',                       'NAICOM', 'Insurance companies regulated by NAICOM',               9),
            ('PFA',       'Pension Fund Administrator',              'PenCom', 'Pension fund administrators regulated by PenCom',       10),
            ('CMO',       'Capital Market Operator',                 'SEC',    'Capital market operators regulated by SEC',             11);
        ");

        // ═══════════════════════════════════════════════════════════
        // Step 3: Create modules table + seed 15 rows
        // ═══════════════════════════════════════════════════════════
        migrationBuilder.Sql(@"
            CREATE TABLE dbo.modules (
                Id                  INT             IDENTITY(1,1) PRIMARY KEY,
                ModuleCode          NVARCHAR(30)    NOT NULL,
                ModuleName          NVARCHAR(200)   NOT NULL,
                RegulatorCode       NVARCHAR(20)    NOT NULL,
                Description         NVARCHAR(1000)  NULL,
                SheetCount          INT             NOT NULL DEFAULT 0,
                DefaultFrequency    NVARCHAR(20)    NOT NULL DEFAULT 'Monthly',
                IsActive            BIT             NOT NULL DEFAULT 1,
                DisplayOrder        INT             NOT NULL DEFAULT 0,
                CreatedAt           DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
            );
            CREATE UNIQUE INDEX IX_modules_ModuleCode ON dbo.modules(ModuleCode);
        ");

        migrationBuilder.Sql(@"
            INSERT INTO dbo.modules (ModuleCode, ModuleName, RegulatorCode, Description, SheetCount, DefaultFrequency, DisplayOrder) VALUES
            ('FC_RETURNS',        'Finance Company Returns',       'CBN',      'CBN Finance Company regulatory returns (103 sheets)',       103, 'Monthly',    1),
            ('BDC_CBN',           'BDC Regulatory Returns',        'CBN',      'Bureau De Change CBN regulatory returns',                   12,  'Monthly',    2),
            ('DMB_BASEL3',        'DMB Basel III Returns',          'CBN',      'Deposit Money Bank Basel III regulatory returns',            15,  'Monthly',    3),
            ('MFB_PAR',           'MFB PAR Returns',               'CBN',      'Microfinance Bank Portfolio At Risk returns',                12,  'Monthly',    4),
            ('PSP_FINTECH',       'PSP/Fintech Returns',           'CBN',      'Payment Service Provider/Fintech regulatory returns',       14,  'Monthly',    5),
            ('DFI_CBN',           'DFI Returns',                   'CBN',      'Development Finance Institution regulatory returns',        12,  'Quarterly',  6),
            ('PMB_CBN',           'PMB Returns',                   'CBN',      'Primary Mortgage Bank regulatory returns',                  12,  'Monthly',    7),
            ('IMTO_CBN',          'IMTO Returns',                  'CBN',      'International Money Transfer Operator returns',              12,  'Monthly',    8),
            ('INSURANCE_NAICOM',  'Insurance Returns',             'NAICOM',   'Insurance company regulatory returns',                     12,  'Quarterly',  9),
            ('PFA_PENCOM',        'PFA Returns',                   'PenCom',   'Pension Fund Administrator regulatory returns',             12,  'Monthly',   10),
            ('CMO_SEC',           'Capital Market Returns',         'SEC',      'Capital market operator regulatory returns',               13,  'Monthly',   11),
            ('NDIC_RETURNS',      'NDIC Deposit Insurance Returns','NDIC',     'NDIC deposit insurance reporting returns',                 11,  'Quarterly', 12),
            ('NFIU_AML',          'NFIU AML/CFT Returns',          'NFIU',     'Anti-money laundering and counter-terrorism financing',     12,  'Monthly',   13),
            ('FATF_EVAL',         'FATF Mutual Evaluation',         'INTERNAL', 'FATF mutual evaluation assessment returns',                13,  'Annual',    14),
            ('ESG_CLIMATE',       'ESG/Climate TCFD Returns',       'INTERNAL', 'ESG and climate-related TCFD disclosure returns',          13,  'Annual',    15);
        ");

        // ═══════════════════════════════════════════════════════════
        // Step 4: Create licence_module_matrix + seed mappings
        // ═══════════════════════════════════════════════════════════
        migrationBuilder.Sql(@"
            CREATE TABLE dbo.licence_module_matrix (
                Id              INT     IDENTITY(1,1) PRIMARY KEY,
                LicenceTypeId   INT     NOT NULL REFERENCES dbo.licence_types(Id) ON DELETE CASCADE,
                ModuleId        INT     NOT NULL REFERENCES dbo.modules(Id) ON DELETE CASCADE,
                IsRequired      BIT     NOT NULL DEFAULT 0,
                IsOptional      BIT     NOT NULL DEFAULT 1
            );
            CREATE UNIQUE INDEX IX_licence_module_matrix_LT_M
                ON dbo.licence_module_matrix(LicenceTypeId, ModuleId);
        ");

        // Insert all licence-module mappings
        migrationBuilder.Sql(@"
            -- FC: Required = FC_RETURNS, NDIC_RETURNS, NFIU_AML; Optional = FATF_EVAL, ESG_CLIMATE
            INSERT INTO dbo.licence_module_matrix (LicenceTypeId, ModuleId, IsRequired, IsOptional)
            SELECT lt.Id, m.Id,
                CASE WHEN m.ModuleCode IN ('FC_RETURNS','NDIC_RETURNS','NFIU_AML') THEN 1 ELSE 0 END,
                CASE WHEN m.ModuleCode IN ('FATF_EVAL','ESG_CLIMATE') THEN 1 ELSE 0 END
            FROM dbo.licence_types lt
            CROSS JOIN dbo.modules m
            WHERE lt.Code = 'FC'
              AND m.ModuleCode IN ('FC_RETURNS','NDIC_RETURNS','NFIU_AML','FATF_EVAL','ESG_CLIMATE');

            -- BDC: Required = BDC_CBN, NFIU_AML; Optional = FATF_EVAL
            INSERT INTO dbo.licence_module_matrix (LicenceTypeId, ModuleId, IsRequired, IsOptional)
            SELECT lt.Id, m.Id,
                CASE WHEN m.ModuleCode IN ('BDC_CBN','NFIU_AML') THEN 1 ELSE 0 END,
                CASE WHEN m.ModuleCode IN ('FATF_EVAL') THEN 1 ELSE 0 END
            FROM dbo.licence_types lt
            CROSS JOIN dbo.modules m
            WHERE lt.Code = 'BDC'
              AND m.ModuleCode IN ('BDC_CBN','NFIU_AML','FATF_EVAL');

            -- DMB: Required = DMB_BASEL3, NDIC_RETURNS, NFIU_AML; Optional = FATF_EVAL, ESG_CLIMATE
            INSERT INTO dbo.licence_module_matrix (LicenceTypeId, ModuleId, IsRequired, IsOptional)
            SELECT lt.Id, m.Id,
                CASE WHEN m.ModuleCode IN ('DMB_BASEL3','NDIC_RETURNS','NFIU_AML') THEN 1 ELSE 0 END,
                CASE WHEN m.ModuleCode IN ('FATF_EVAL','ESG_CLIMATE') THEN 1 ELSE 0 END
            FROM dbo.licence_types lt
            CROSS JOIN dbo.modules m
            WHERE lt.Code = 'DMB'
              AND m.ModuleCode IN ('DMB_BASEL3','NDIC_RETURNS','NFIU_AML','FATF_EVAL','ESG_CLIMATE');

            -- MFB: Required = MFB_PAR, NDIC_RETURNS, NFIU_AML; Optional = FATF_EVAL, ESG_CLIMATE
            INSERT INTO dbo.licence_module_matrix (LicenceTypeId, ModuleId, IsRequired, IsOptional)
            SELECT lt.Id, m.Id,
                CASE WHEN m.ModuleCode IN ('MFB_PAR','NDIC_RETURNS','NFIU_AML') THEN 1 ELSE 0 END,
                CASE WHEN m.ModuleCode IN ('FATF_EVAL','ESG_CLIMATE') THEN 1 ELSE 0 END
            FROM dbo.licence_types lt
            CROSS JOIN dbo.modules m
            WHERE lt.Code = 'MFB'
              AND m.ModuleCode IN ('MFB_PAR','NDIC_RETURNS','NFIU_AML','FATF_EVAL','ESG_CLIMATE');

            -- PMB: Required = PMB_CBN, NDIC_RETURNS, NFIU_AML; Optional = FATF_EVAL
            INSERT INTO dbo.licence_module_matrix (LicenceTypeId, ModuleId, IsRequired, IsOptional)
            SELECT lt.Id, m.Id,
                CASE WHEN m.ModuleCode IN ('PMB_CBN','NDIC_RETURNS','NFIU_AML') THEN 1 ELSE 0 END,
                CASE WHEN m.ModuleCode IN ('FATF_EVAL') THEN 1 ELSE 0 END
            FROM dbo.licence_types lt
            CROSS JOIN dbo.modules m
            WHERE lt.Code = 'PMB'
              AND m.ModuleCode IN ('PMB_CBN','NDIC_RETURNS','NFIU_AML','FATF_EVAL');

            -- PSP: Required = PSP_FINTECH, NFIU_AML; Optional = NDIC_RETURNS, FATF_EVAL
            INSERT INTO dbo.licence_module_matrix (LicenceTypeId, ModuleId, IsRequired, IsOptional)
            SELECT lt.Id, m.Id,
                CASE WHEN m.ModuleCode IN ('PSP_FINTECH','NFIU_AML') THEN 1 ELSE 0 END,
                CASE WHEN m.ModuleCode IN ('NDIC_RETURNS','FATF_EVAL') THEN 1 ELSE 0 END
            FROM dbo.licence_types lt
            CROSS JOIN dbo.modules m
            WHERE lt.Code = 'PSP'
              AND m.ModuleCode IN ('PSP_FINTECH','NFIU_AML','NDIC_RETURNS','FATF_EVAL');

            -- DFI: Required = DFI_CBN, NFIU_AML; Optional = ESG_CLIMATE, FATF_EVAL
            INSERT INTO dbo.licence_module_matrix (LicenceTypeId, ModuleId, IsRequired, IsOptional)
            SELECT lt.Id, m.Id,
                CASE WHEN m.ModuleCode IN ('DFI_CBN','NFIU_AML') THEN 1 ELSE 0 END,
                CASE WHEN m.ModuleCode IN ('ESG_CLIMATE','FATF_EVAL') THEN 1 ELSE 0 END
            FROM dbo.licence_types lt
            CROSS JOIN dbo.modules m
            WHERE lt.Code = 'DFI'
              AND m.ModuleCode IN ('DFI_CBN','NFIU_AML','ESG_CLIMATE','FATF_EVAL');

            -- IMTO: Required = IMTO_CBN, NFIU_AML; Optional = FATF_EVAL
            INSERT INTO dbo.licence_module_matrix (LicenceTypeId, ModuleId, IsRequired, IsOptional)
            SELECT lt.Id, m.Id,
                CASE WHEN m.ModuleCode IN ('IMTO_CBN','NFIU_AML') THEN 1 ELSE 0 END,
                CASE WHEN m.ModuleCode IN ('FATF_EVAL') THEN 1 ELSE 0 END
            FROM dbo.licence_types lt
            CROSS JOIN dbo.modules m
            WHERE lt.Code = 'IMTO'
              AND m.ModuleCode IN ('IMTO_CBN','NFIU_AML','FATF_EVAL');

            -- INSURANCE: Required = INSURANCE_NAICOM, NFIU_AML; Optional = FATF_EVAL, ESG_CLIMATE
            INSERT INTO dbo.licence_module_matrix (LicenceTypeId, ModuleId, IsRequired, IsOptional)
            SELECT lt.Id, m.Id,
                CASE WHEN m.ModuleCode IN ('INSURANCE_NAICOM','NFIU_AML') THEN 1 ELSE 0 END,
                CASE WHEN m.ModuleCode IN ('FATF_EVAL','ESG_CLIMATE') THEN 1 ELSE 0 END
            FROM dbo.licence_types lt
            CROSS JOIN dbo.modules m
            WHERE lt.Code = 'INSURANCE'
              AND m.ModuleCode IN ('INSURANCE_NAICOM','NFIU_AML','FATF_EVAL','ESG_CLIMATE');

            -- PFA: Required = PFA_PENCOM, NFIU_AML; Optional = FATF_EVAL
            INSERT INTO dbo.licence_module_matrix (LicenceTypeId, ModuleId, IsRequired, IsOptional)
            SELECT lt.Id, m.Id,
                CASE WHEN m.ModuleCode IN ('PFA_PENCOM','NFIU_AML') THEN 1 ELSE 0 END,
                CASE WHEN m.ModuleCode IN ('FATF_EVAL') THEN 1 ELSE 0 END
            FROM dbo.licence_types lt
            CROSS JOIN dbo.modules m
            WHERE lt.Code = 'PFA'
              AND m.ModuleCode IN ('PFA_PENCOM','NFIU_AML','FATF_EVAL');

            -- CMO: Required = CMO_SEC, NFIU_AML; Optional = FATF_EVAL, ESG_CLIMATE
            INSERT INTO dbo.licence_module_matrix (LicenceTypeId, ModuleId, IsRequired, IsOptional)
            SELECT lt.Id, m.Id,
                CASE WHEN m.ModuleCode IN ('CMO_SEC','NFIU_AML') THEN 1 ELSE 0 END,
                CASE WHEN m.ModuleCode IN ('FATF_EVAL','ESG_CLIMATE') THEN 1 ELSE 0 END
            FROM dbo.licence_types lt
            CROSS JOIN dbo.modules m
            WHERE lt.Code = 'CMO'
              AND m.ModuleCode IN ('CMO_SEC','NFIU_AML','FATF_EVAL','ESG_CLIMATE');
        ");

        // ═══════════════════════════════════════════════════════════
        // Step 5: Create tenant_licence_types table
        // ═══════════════════════════════════════════════════════════
        migrationBuilder.Sql(@"
            CREATE TABLE dbo.tenant_licence_types (
                Id                  INT                 IDENTITY(1,1) PRIMARY KEY,
                TenantId            UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                LicenceTypeId       INT                 NOT NULL REFERENCES dbo.licence_types(Id),
                RegistrationNumber  NVARCHAR(100)       NULL,
                EffectiveDate       DATE                NOT NULL,
                ExpiryDate          DATE                NULL,
                IsActive            BIT                 NOT NULL DEFAULT 1
            );
            CREATE UNIQUE INDEX IX_tenant_licence_types_TL
                ON dbo.tenant_licence_types(TenantId, LicenceTypeId);
            CREATE NONCLUSTERED INDEX IX_tenant_licence_types_TenantId
                ON dbo.tenant_licence_types(TenantId);
        ");

        // Assign FC licence to Legacy tenant
        migrationBuilder.Sql($@"
            INSERT INTO dbo.tenant_licence_types (TenantId, LicenceTypeId, EffectiveDate, IsActive)
            SELECT '{LegacyTenantId}', Id, CAST('2026-01-01' AS DATE), 1
            FROM dbo.licence_types WHERE Code = 'FC';
        ");

        // ═══════════════════════════════════════════════════════════
        // Step 6: Extend institutions with hierarchy columns
        // ═══════════════════════════════════════════════════════════
        migrationBuilder.Sql(@"
            ALTER TABLE dbo.institutions ADD
                ParentInstitutionId INT         NULL,
                EntityType          NVARCHAR(30) NOT NULL DEFAULT 'HeadOffice',
                BranchCode          NVARCHAR(20) NULL,
                Location            NVARCHAR(200) NULL;

            ALTER TABLE dbo.institutions
                ADD CONSTRAINT FK_institutions_ParentInstitution
                FOREIGN KEY (ParentInstitutionId) REFERENCES dbo.institutions(Id);
        ");

        // ═══════════════════════════════════════════════════════════
        // Step 7: Add ModuleId to return_templates
        // ═══════════════════════════════════════════════════════════
        migrationBuilder.Sql(@"
            ALTER TABLE meta.return_templates ADD
                ModuleId INT NULL;

            ALTER TABLE meta.return_templates
                ADD CONSTRAINT FK_return_templates_Module
                FOREIGN KEY (ModuleId) REFERENCES dbo.modules(Id)
                ON DELETE SET NULL;
        ");

        // Link existing FC templates to FC_RETURNS module
        migrationBuilder.Sql(@"
            UPDATE meta.return_templates
            SET ModuleId = (SELECT Id FROM dbo.modules WHERE ModuleCode = 'FC_RETURNS');
        ");

        // ═══════════════════════════════════════════════════════════
        // Step 8: Extend RLS policy to include tenant_licence_types
        // ═══════════════════════════════════════════════════════════
        migrationBuilder.Sql(@"
            -- Drop the existing security policy
            IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = 'TenantSecurityPolicy')
                DROP SECURITY POLICY dbo.TenantSecurityPolicy;

            -- Recreate with all tables that have TenantId (including new tenant_licence_types)
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
        // Drop and recreate security policy WITHOUT tenant_licence_types
        migrationBuilder.Sql(@"
            IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = 'TenantSecurityPolicy')
                DROP SECURITY POLICY dbo.TenantSecurityPolicy;
        ");

        // Remove ModuleId from return_templates
        migrationBuilder.Sql(@"
            ALTER TABLE meta.return_templates DROP CONSTRAINT IF EXISTS FK_return_templates_Module;
            ALTER TABLE meta.return_templates DROP COLUMN IF EXISTS ModuleId;
        ");

        // Remove hierarchy columns from institutions
        migrationBuilder.Sql(@"
            ALTER TABLE dbo.institutions DROP CONSTRAINT IF EXISTS FK_institutions_ParentInstitution;
            ALTER TABLE dbo.institutions DROP COLUMN IF EXISTS ParentInstitutionId;
            ALTER TABLE dbo.institutions DROP COLUMN IF EXISTS EntityType;
            ALTER TABLE dbo.institutions DROP COLUMN IF EXISTS BranchCode;
            ALTER TABLE dbo.institutions DROP COLUMN IF EXISTS Location;
        ");

        // Drop tenant_licence_types
        migrationBuilder.Sql(@"
            DROP TABLE IF EXISTS dbo.tenant_licence_types;
        ");

        // Drop licence_module_matrix
        migrationBuilder.Sql(@"
            DROP TABLE IF EXISTS dbo.licence_module_matrix;
        ");

        // Drop modules
        migrationBuilder.Sql(@"
            DROP TABLE IF EXISTS dbo.modules;
        ");

        // Drop licence_types
        migrationBuilder.Sql(@"
            DROP TABLE IF EXISTS dbo.licence_types;
        ");

        // Remove new tenant columns
        migrationBuilder.Sql(@"
            ALTER TABLE dbo.tenants DROP COLUMN IF EXISTS TenantType;
            ALTER TABLE dbo.tenants DROP COLUMN IF EXISTS Address;
            ALTER TABLE dbo.tenants DROP COLUMN IF EXISTS TaxId;
            ALTER TABLE dbo.tenants DROP COLUMN IF EXISTS RcNumber;
            ALTER TABLE dbo.tenants DROP COLUMN IF EXISTS FiscalYearStartMonth;
            ALTER TABLE dbo.tenants DROP COLUMN IF EXISTS Timezone;
            ALTER TABLE dbo.tenants DROP COLUMN IF EXISTS DefaultCurrency;
            ALTER TABLE dbo.tenants DROP COLUMN IF EXISTS BrandingConfig;
            ALTER TABLE dbo.tenants DROP COLUMN IF EXISTS CustomDomain;
            ALTER TABLE dbo.tenants DROP COLUMN IF EXISTS DeactivatedAt;
            ALTER TABLE dbo.tenants DROP COLUMN IF EXISTS MaxInstitutions;
            ALTER TABLE dbo.tenants DROP COLUMN IF EXISTS MaxUsersPerEntity;
        ");

        // Recreate RLS policy without new tables
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
