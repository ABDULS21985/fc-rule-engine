-- ============================================================================
-- METADATA SCHEMA: Template Registry & Validation Rules
-- CBN DFIS FC Returns Data Processing Engine
-- Run this FIRST before any other migrations
-- ============================================================================

-- Create the meta schema
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'meta')
    EXEC('CREATE SCHEMA meta');
GO

-- ============================================================================
-- A. TEMPLATE REGISTRY
-- ============================================================================

-- Template lifecycle statuses
CREATE TABLE meta.template_statuses (
    id          INT IDENTITY(1,1) PRIMARY KEY,
    name        VARCHAR(20) NOT NULL UNIQUE,
    description VARCHAR(200)
);

INSERT INTO meta.template_statuses (name, description) VALUES
    ('Draft', 'Template is being designed, not yet available for submissions'),
    ('Review', 'Template submitted for approval, pending review'),
    ('Published', 'Template is active and accepting submissions'),
    ('Deprecated', 'Template version superseded by a newer version'),
    ('Retired', 'Template permanently deactivated');

-- Master table: one row per return template
CREATE TABLE meta.return_templates (
    id                  INT IDENTITY(1,1) PRIMARY KEY,
    return_code         VARCHAR(20) NOT NULL UNIQUE,
    name                NVARCHAR(255) NOT NULL,
    description         NVARCHAR(1000),
    frequency           VARCHAR(20) NOT NULL,           -- Monthly, Quarterly, SemiAnnual, Computed
    structural_category VARCHAR(20) NOT NULL,            -- FixedRow, MultiRow, ItemCoded
    physical_table_name VARCHAR(128) NOT NULL UNIQUE,
    xml_root_element    VARCHAR(128) NOT NULL,
    xml_namespace       VARCHAR(255) NOT NULL,
    is_system_template  BIT NOT NULL DEFAULT 0,
    owner_department    VARCHAR(50) DEFAULT 'DFIS',
    institution_type    VARCHAR(10) DEFAULT 'FC',
    created_at          DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by          NVARCHAR(100) NOT NULL,
    updated_at          DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_by          NVARCHAR(100) NOT NULL,

    CONSTRAINT CK_template_frequency CHECK (frequency IN ('Monthly', 'Quarterly', 'SemiAnnual', 'Computed')),
    CONSTRAINT CK_template_category CHECK (structural_category IN ('FixedRow', 'MultiRow', 'ItemCoded'))
);

-- Version tracking: every published change creates a new version
CREATE TABLE meta.template_versions (
    id                  INT IDENTITY(1,1) PRIMARY KEY,
    template_id         INT NOT NULL REFERENCES meta.return_templates(id),
    version_number      INT NOT NULL,
    status_id           INT NOT NULL REFERENCES meta.template_statuses(id),
    effective_from      DATE,
    effective_to        DATE,
    change_summary      NVARCHAR(1000),
    approved_by         NVARCHAR(100),
    approved_at         DATETIME2,
    published_at        DATETIME2,
    ddl_script          NVARCHAR(MAX),
    rollback_script     NVARCHAR(MAX),
    created_at          DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by          NVARCHAR(100) NOT NULL,

    CONSTRAINT UQ_template_version UNIQUE (template_id, version_number)
);

CREATE INDEX IX_template_versions_template ON meta.template_versions(template_id);
CREATE INDEX IX_template_versions_status ON meta.template_versions(status_id);

-- Field definitions: one row per column/field in a template version
CREATE TABLE meta.template_fields (
    id                  INT IDENTITY(1,1) PRIMARY KEY,
    template_version_id INT NOT NULL REFERENCES meta.template_versions(id),
    field_name          VARCHAR(128) NOT NULL,
    display_name        NVARCHAR(255) NOT NULL,
    xml_element_name    VARCHAR(128) NOT NULL,
    line_code           VARCHAR(20),
    section_name        NVARCHAR(100),
    section_order       INT NOT NULL DEFAULT 0,
    field_order         INT NOT NULL DEFAULT 0,
    data_type           VARCHAR(30) NOT NULL,           -- Money, Integer, Decimal, Text, Date, Boolean, Percentage
    sql_type            VARCHAR(50) NOT NULL,            -- DECIMAL(20,2), VARCHAR(255), INT, DATE, BIT
    is_required         BIT NOT NULL DEFAULT 0,
    is_computed         BIT NOT NULL DEFAULT 0,
    is_key_field        BIT NOT NULL DEFAULT 0,          -- true for serial_no, item_code
    default_value       NVARCHAR(100),
    min_value           NVARCHAR(100),
    max_value           NVARCHAR(100),
    max_length          INT,
    allowed_values      NVARCHAR(MAX),                   -- JSON array: ["Secured","Unsecured"]
    reference_table     VARCHAR(128),
    reference_column    VARCHAR(128),
    help_text           NVARCHAR(500),
    is_ytd_field        BIT NOT NULL DEFAULT 0,
    ytd_source_field_id INT REFERENCES meta.template_fields(id),
    created_at          DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT UQ_field_per_version UNIQUE (template_version_id, field_name),
    CONSTRAINT CK_field_data_type CHECK (data_type IN ('Money', 'Integer', 'Decimal', 'Text', 'Date', 'Boolean', 'Percentage'))
);

CREATE INDEX IX_template_fields_version ON meta.template_fields(template_version_id);

-- Predefined item codes for ItemCoded templates
CREATE TABLE meta.template_item_codes (
    id                  INT IDENTITY(1,1) PRIMARY KEY,
    template_version_id INT NOT NULL REFERENCES meta.template_versions(id),
    item_code           VARCHAR(20) NOT NULL,
    item_description    NVARCHAR(255) NOT NULL,
    sort_order          INT NOT NULL DEFAULT 0,
    is_total_row        BIT NOT NULL DEFAULT 0,
    created_at          DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT UQ_item_code_per_version UNIQUE (template_version_id, item_code)
);

-- Template sections for UI grouping
CREATE TABLE meta.template_sections (
    id                  INT IDENTITY(1,1) PRIMARY KEY,
    template_version_id INT NOT NULL REFERENCES meta.template_versions(id),
    section_name        NVARCHAR(100) NOT NULL,
    section_order       INT NOT NULL DEFAULT 0,
    description         NVARCHAR(500),
    is_repeating        BIT NOT NULL DEFAULT 0,

    CONSTRAINT UQ_section_per_version UNIQUE (template_version_id, section_name)
);

-- ============================================================================
-- B. VALIDATION RULE METADATA
-- ============================================================================

-- Intra-sheet formulas: single-template field-level validation
CREATE TABLE meta.intra_sheet_formulas (
    id                  INT IDENTITY(1,1) PRIMARY KEY,
    template_version_id INT NOT NULL REFERENCES meta.template_versions(id),
    rule_code           VARCHAR(50) NOT NULL,
    rule_name           NVARCHAR(255) NOT NULL,
    formula_type        VARCHAR(30) NOT NULL,            -- Sum, Difference, Equals, GreaterThan, LessThan,
                                                         -- GreaterThanOrEqual, Between, Ratio, Custom, Required
    target_field_name   VARCHAR(128) NOT NULL,
    target_line_code    VARCHAR(20),
    operand_fields      NVARCHAR(MAX) NOT NULL,          -- JSON: ["cash_notes","cash_coins"]
    operand_line_codes  NVARCHAR(MAX),                   -- JSON: ["10110","10120"]
    custom_expression   NVARCHAR(1000),                  -- for Custom type: "A + B - C"
    tolerance_amount    DECIMAL(20,2) DEFAULT 0,
    tolerance_percent   DECIMAL(10,4),
    severity            VARCHAR(10) NOT NULL DEFAULT 'Error',
    error_message       NVARCHAR(500),
    is_active           BIT NOT NULL DEFAULT 1,
    sort_order          INT NOT NULL DEFAULT 0,
    created_at          DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by          NVARCHAR(100) NOT NULL,

    CONSTRAINT UQ_intra_rule UNIQUE (template_version_id, rule_code),
    CONSTRAINT CK_formula_type CHECK (formula_type IN (
        'Sum', 'Difference', 'Equals', 'GreaterThan', 'LessThan',
        'GreaterThanOrEqual', 'LessThanOrEqual', 'Between', 'Ratio', 'Custom', 'Required'
    )),
    CONSTRAINT CK_formula_severity CHECK (severity IN ('Error', 'Warning', 'Info'))
);

CREATE INDEX IX_intra_sheet_formulas_version ON meta.intra_sheet_formulas(template_version_id);

-- Cross-sheet rules: validation spanning two or more templates
CREATE TABLE meta.cross_sheet_rules (
    id                  INT IDENTITY(1,1) PRIMARY KEY,
    rule_code           VARCHAR(50) NOT NULL UNIQUE,
    rule_name           NVARCHAR(255) NOT NULL,
    description         NVARCHAR(1000),
    severity            VARCHAR(10) NOT NULL DEFAULT 'Error',
    is_active           BIT NOT NULL DEFAULT 1,
    created_at          DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by          NVARCHAR(100) NOT NULL,

    CONSTRAINT CK_cross_severity CHECK (severity IN ('Error', 'Warning', 'Info'))
);

-- Cross-sheet rule operands
CREATE TABLE meta.cross_sheet_rule_operands (
    id                  INT IDENTITY(1,1) PRIMARY KEY,
    rule_id             INT NOT NULL REFERENCES meta.cross_sheet_rules(id),
    operand_alias       VARCHAR(10) NOT NULL,            -- 'A', 'B', 'C'
    template_return_code VARCHAR(20) NOT NULL,
    field_name          VARCHAR(128) NOT NULL,
    line_code           VARCHAR(20),
    aggregate_function  VARCHAR(20),                     -- NULL (FixedRow), SUM, COUNT, MAX, MIN, AVG
    filter_item_code    VARCHAR(20),
    sort_order          INT NOT NULL DEFAULT 0,

    CONSTRAINT UQ_cross_operand UNIQUE (rule_id, operand_alias)
);

CREATE INDEX IX_cross_operands_rule ON meta.cross_sheet_rule_operands(rule_id);

-- Cross-sheet rule expression
CREATE TABLE meta.cross_sheet_rule_expressions (
    id                  INT IDENTITY(1,1) PRIMARY KEY,
    rule_id             INT NOT NULL REFERENCES meta.cross_sheet_rules(id) UNIQUE,
    expression          NVARCHAR(1000) NOT NULL,         -- 'A = B' or 'A >= B * 0.125'
    tolerance_amount    DECIMAL(20,2) DEFAULT 0,
    tolerance_percent   DECIMAL(10,4),
    error_message       NVARCHAR(500)
);

-- Business rules: general-purpose rules
CREATE TABLE meta.business_rules (
    id                  INT IDENTITY(1,1) PRIMARY KEY,
    rule_code           VARCHAR(50) NOT NULL UNIQUE,
    rule_name           NVARCHAR(255) NOT NULL,
    description         NVARCHAR(1000),
    rule_type           VARCHAR(30) NOT NULL,            -- DateCheck, ThresholdCheck, Completeness, Custom
    expression          NVARCHAR(1000),
    applies_to_templates NVARCHAR(MAX),                  -- JSON: ["MFCR 300","FC CAR 2"] or "*"
    applies_to_fields   NVARCHAR(MAX),                   -- JSON: ["field_name"] or NULL
    severity            VARCHAR(10) NOT NULL DEFAULT 'Error',
    is_active           BIT NOT NULL DEFAULT 1,
    created_at          DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by          NVARCHAR(100) NOT NULL,

    CONSTRAINT CK_biz_rule_type CHECK (rule_type IN ('DateCheck', 'ThresholdCheck', 'Completeness', 'Custom')),
    CONSTRAINT CK_biz_severity CHECK (severity IN ('Error', 'Warning', 'Info'))
);

-- ============================================================================
-- C. AUDIT AND MIGRATION TRACKING
-- ============================================================================

-- Audit trail: every change to metadata
CREATE TABLE meta.audit_log (
    id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    entity_type         VARCHAR(50) NOT NULL,
    entity_id           INT NOT NULL,
    action              VARCHAR(20) NOT NULL,
    old_values          NVARCHAR(MAX),
    new_values          NVARCHAR(MAX),
    performed_by        NVARCHAR(100) NOT NULL,
    performed_at        DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    ip_address          VARCHAR(45),
    correlation_id      UNIQUEIDENTIFIER
);

CREATE INDEX IX_audit_log_entity ON meta.audit_log(entity_type, entity_id);
CREATE INDEX IX_audit_log_performed ON meta.audit_log(performed_at DESC);

-- DDL migration history
CREATE TABLE meta.ddl_migrations (
    id                  INT IDENTITY(1,1) PRIMARY KEY,
    template_id         INT NOT NULL REFERENCES meta.return_templates(id),
    version_from        INT,
    version_to          INT NOT NULL,
    migration_type      VARCHAR(20) NOT NULL,            -- CreateTable, AddColumn, AlterColumn, DropColumn
    ddl_script          NVARCHAR(MAX) NOT NULL,
    rollback_script     NVARCHAR(MAX) NOT NULL,
    executed_at         DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    executed_by         NVARCHAR(100) NOT NULL,
    execution_duration_ms INT,
    is_rolled_back      BIT NOT NULL DEFAULT 0,
    rolled_back_at      DATETIME2,
    rolled_back_by      NVARCHAR(100)
);

CREATE INDEX IX_ddl_migrations_template ON meta.ddl_migrations(template_id);

-- Template publish queue
CREATE TABLE meta.publish_queue (
    id                  INT IDENTITY(1,1) PRIMARY KEY,
    template_version_id INT NOT NULL REFERENCES meta.template_versions(id),
    status              VARCHAR(20) NOT NULL DEFAULT 'Pending',
    ddl_script          NVARCHAR(MAX) NOT NULL,
    error_message       NVARCHAR(MAX),
    queued_at           DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    queued_by           NVARCHAR(100) NOT NULL,
    processed_at        DATETIME2,
    retry_count         INT NOT NULL DEFAULT 0,

    CONSTRAINT CK_queue_status CHECK (status IN ('Pending', 'Processing', 'Completed', 'Failed'))
);

-- Saved Reports (user-created report definitions with optional scheduling)
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'saved_reports')
CREATE TABLE dbo.saved_reports (
    Id                  INT IDENTITY(1,1) PRIMARY KEY,
    TenantId            UNIQUEIDENTIFIER NOT NULL,
    InstitutionId       INT NOT NULL,
    Name                NVARCHAR(200) NOT NULL,
    Description         NVARCHAR(500) NULL,
    Definition          NVARCHAR(MAX) NOT NULL,
    IsShared            BIT NOT NULL DEFAULT 0,
    CreatedByUserId     INT NOT NULL,
    ScheduleCron        NVARCHAR(100) NULL,
    ScheduleFormat      NVARCHAR(10) NULL,
    ScheduleRecipients  NVARCHAR(MAX) NULL,
    IsScheduleActive    BIT NOT NULL DEFAULT 0,
    LastRunAt           DATETIME2 NULL,
    CreatedAt           DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt           DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_saved_reports_TenantId')
    CREATE INDEX IX_saved_reports_TenantId ON dbo.saved_reports(TenantId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_saved_reports_TenantId_InstitutionId')
    CREATE INDEX IX_saved_reports_TenantId_InstitutionId ON dbo.saved_reports(TenantId, InstitutionId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_saved_reports_IsScheduleActive_LastRunAt')
    CREATE INDEX IX_saved_reports_IsScheduleActive_LastRunAt ON dbo.saved_reports(IsScheduleActive, LastRunAt);

-- ============================================================================
-- D. OPERATIONAL TABLE ENHANCEMENTS
-- ============================================================================

-- Add template_version_id to return_submissions (if table exists)
-- This links each submission to the specific template version used
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'return_submissions')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                   WHERE TABLE_NAME = 'return_submissions' AND COLUMN_NAME = 'template_version_id')
    BEGIN
        ALTER TABLE dbo.return_submissions ADD
            template_version_id INT REFERENCES meta.template_versions(id),
            raw_xml             NVARCHAR(MAX),
            parsed_data_json    NVARCHAR(MAX),
            processing_duration_ms INT;
    END
END

PRINT 'Metadata schema created successfully.';
