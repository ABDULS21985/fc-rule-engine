#nullable enable
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FC.Engine.Infrastructure.Metadata.Migrations;

[DbContext(typeof(MetadataDbContext))]
[Migration("20260305180000_AddNotificationEngineRg06")]
public partial class AddNotificationEngineRg06 : Migration
{
    private static readonly Guid LegacyTenantId = new("00000000-0000-0000-0000-000000000001");

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($@"
            IF OBJECT_ID(N'dbo.email_templates', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.email_templates (
                    Id              INT                 IDENTITY(1,1) PRIMARY KEY,
                    TemplateCode    NVARCHAR(50)        NOT NULL,
                    TenantId        UNIQUEIDENTIFIER    NULL REFERENCES dbo.tenants(TenantId),
                    Subject         NVARCHAR(200)       NOT NULL,
                    HtmlBody        NVARCHAR(MAX)       NOT NULL,
                    PlainTextBody   NVARCHAR(MAX)       NULL,
                    Variables       NVARCHAR(MAX)       NULL,
                    IsActive        BIT                 NOT NULL DEFAULT 1
                );

                CREATE UNIQUE INDEX UX_email_templates_TemplateCode_TenantId
                    ON dbo.email_templates(TemplateCode, TenantId);
                CREATE INDEX IX_email_templates_TenantId
                    ON dbo.email_templates(TenantId);
            END;

            IF OBJECT_ID(N'dbo.notification_preferences', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.notification_preferences (
                    Id                  INT                 IDENTITY(1,1) PRIMARY KEY,
                    TenantId            UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    UserId              INT                 NOT NULL,
                    EventType           NVARCHAR(50)        NOT NULL,
                    InAppEnabled        BIT                 NOT NULL DEFAULT 1,
                    EmailEnabled        BIT                 NOT NULL DEFAULT 1,
                    SmsEnabled          BIT                 NOT NULL DEFAULT 0,
                    SmsQuietHoursStart  TIME                NULL,
                    SmsQuietHoursEnd    TIME                NULL,
                    CONSTRAINT UQ_notification_preferences_User_Event UNIQUE (UserId, EventType)
                );

                CREATE INDEX IX_notification_preferences_TenantId
                    ON dbo.notification_preferences(TenantId);
            END;

            IF OBJECT_ID(N'dbo.notification_deliveries', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.notification_deliveries (
                    Id                      INT                 IDENTITY(1,1) PRIMARY KEY,
                    TenantId                UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    NotificationEventType   NVARCHAR(50)        NOT NULL,
                    Channel                 NVARCHAR(10)        NOT NULL,
                    RecipientId             INT                 NOT NULL,
                    RecipientAddress        NVARCHAR(255)       NOT NULL,
                    Status                  NVARCHAR(20)        NOT NULL DEFAULT 'Queued',
                    AttemptCount            INT                 NOT NULL DEFAULT 0,
                    MaxAttempts             INT                 NOT NULL DEFAULT 3,
                    NextRetryAt             DATETIME2           NULL,
                    SentAt                  DATETIME2           NULL,
                    DeliveredAt             DATETIME2           NULL,
                    ProviderMessageId       NVARCHAR(100)       NULL,
                    ErrorMessage            NVARCHAR(500)       NULL,
                    Payload                 NVARCHAR(MAX)       NULL,
                    CreatedAt               DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME()
                );

                CREATE INDEX IX_notification_deliveries_TenantId
                    ON dbo.notification_deliveries(TenantId);
                CREATE INDEX IX_notification_deliveries_Status_NextRetryAt
                    ON dbo.notification_deliveries(Status, NextRetryAt);
                CREATE INDEX IX_notification_deliveries_RecipientId_CreatedAt
                    ON dbo.notification_deliveries(RecipientId, CreatedAt);
            END;

            IF COL_LENGTH('dbo.portal_notifications', 'EventType') IS NULL
                ALTER TABLE dbo.portal_notifications
                    ADD EventType NVARCHAR(80) NOT NULL CONSTRAINT DF_portal_notifications_EventType DEFAULT('system.announcement');

            IF COL_LENGTH('dbo.portal_notifications', 'Channel') IS NULL
                ALTER TABLE dbo.portal_notifications
                    ADD Channel NVARCHAR(20) NOT NULL CONSTRAINT DF_portal_notifications_Channel DEFAULT('InApp');

            IF COL_LENGTH('dbo.portal_notifications', 'Priority') IS NULL
                ALTER TABLE dbo.portal_notifications
                    ADD Priority INT NOT NULL CONSTRAINT DF_portal_notifications_Priority DEFAULT(1);

            IF COL_LENGTH('dbo.portal_notifications', 'RecipientEmail') IS NULL
                ALTER TABLE dbo.portal_notifications ADD RecipientEmail NVARCHAR(256) NULL;

            IF COL_LENGTH('dbo.portal_notifications', 'RecipientPhone') IS NULL
                ALTER TABLE dbo.portal_notifications ADD RecipientPhone NVARCHAR(32) NULL;

            IF COL_LENGTH('dbo.portal_notifications', 'MetadataJson') IS NOT NULL
                ALTER TABLE dbo.portal_notifications ALTER COLUMN MetadataJson NVARCHAR(4000) NULL;

            IF COL_LENGTH('meta.institution_users', 'PhoneNumber') IS NULL
                ALTER TABLE meta.institution_users ADD PhoneNumber NVARCHAR(32) NULL;

            IF COL_LENGTH('dbo.portal_notifications', 'TenantId') IS NULL
            BEGIN
                ALTER TABLE dbo.portal_notifications ADD TenantId UNIQUEIDENTIFIER NULL;

                UPDATE pn
                SET pn.TenantId = i.TenantId
                FROM dbo.portal_notifications pn
                INNER JOIN dbo.institutions i ON i.Id = pn.InstitutionId
                WHERE pn.TenantId IS NULL;

                UPDATE dbo.portal_notifications
                SET TenantId = '{LegacyTenantId}'
                WHERE TenantId IS NULL;

                ALTER TABLE dbo.portal_notifications ALTER COLUMN TenantId UNIQUEIDENTIFIER NOT NULL;
                ALTER TABLE dbo.portal_notifications
                    ADD CONSTRAINT FK_portal_notifications_tenants_TenantId
                    FOREIGN KEY (TenantId) REFERENCES dbo.tenants(TenantId);
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_portal_notifications_TenantId' AND object_id = OBJECT_ID('dbo.portal_notifications'))
                CREATE INDEX IX_portal_notifications_TenantId ON dbo.portal_notifications(TenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_portal_notifications_Tenant_Event_Created' AND object_id = OBJECT_ID('dbo.portal_notifications'))
                CREATE INDEX IX_portal_notifications_Tenant_Event_Created
                    ON dbo.portal_notifications(TenantId, EventType, CreatedAt);
        ");

        migrationBuilder.Sql(@"
            DECLARE @templates TABLE
            (
                TemplateCode NVARCHAR(50) NOT NULL,
                Subject NVARCHAR(200) NOT NULL,
                HtmlBody NVARCHAR(MAX) NOT NULL,
                PlainTextBody NVARCHAR(MAX) NULL,
                Variables NVARCHAR(MAX) NULL
            );

            INSERT INTO @templates (TemplateCode, Subject, HtmlBody, PlainTextBody, Variables) VALUES
            (
                'return.submitted_for_review',
                '{{ModuleName}} Return Submitted for Review - {{ReturnCode}} ({{PeriodLabel}})',
                '<p>Hello {{RecipientName}},</p><p>{{UserName}} submitted {{ReturnCode}} ({{PeriodLabel}}) for review.</p><p><a href=""{{ReviewUrl}}"">Open Review</a></p>',
                'Hello {{RecipientName}}, {{UserName}} submitted {{ReturnCode}} ({{PeriodLabel}}) for review. Review: {{ReviewUrl}}',
                '[""InstitutionName"",""UserName"",""ModuleName"",""ReturnCode"",""PeriodLabel"",""ReviewUrl""]'
            ),
            (
                'return.approved',
                'Your {{ModuleName}} Return Has Been Approved - {{PeriodLabel}}',
                '<p>Hello {{RecipientName}},</p><p>Your {{ModuleName}} return for {{PeriodLabel}} has been approved by {{ApprovedBy}}.</p>',
                'Your {{ModuleName}} return for {{PeriodLabel}} has been approved by {{ApprovedBy}}.',
                '[""InstitutionName"",""UserName"",""ModuleName"",""PeriodLabel"",""ApprovedBy""]'
            ),
            (
                'return.rejected',
                '{{ModuleName}} Return Rejected - Action Required',
                '<p>Hello {{RecipientName}},</p><p>Your {{ModuleName}} return for {{PeriodLabel}} was rejected by {{RejectedBy}}.</p><p>Reason: {{RejectionReason}}</p><p><a href=""{{EditUrl}}"">Fix Submission</a></p>',
                'Your {{ModuleName}} return for {{PeriodLabel}} was rejected by {{RejectedBy}}. Reason: {{RejectionReason}}. Edit: {{EditUrl}}',
                '[""InstitutionName"",""UserName"",""ModuleName"",""PeriodLabel"",""RejectedBy"",""RejectionReason"",""EditUrl""]'
            ),
            (
                'deadline.t30',
                '30 Days Until {{ModuleName}} Deadline - {{PeriodLabel}}',
                '<p>{{ModuleName}} for {{PeriodLabel}} is due on {{Deadline}}.</p><p>Days remaining: {{DaysRemaining}}</p>',
                '{{ModuleName}} for {{PeriodLabel}} is due on {{Deadline}}. Days remaining: {{DaysRemaining}}.',
                '[""InstitutionName"",""ModuleName"",""PeriodLabel"",""Deadline"",""DaysRemaining"",""SubmitUrl""]'
            ),
            (
                'deadline.t14',
                '14 Days Until {{ModuleName}} Deadline - {{PeriodLabel}}',
                '<p>{{ModuleName}} for {{PeriodLabel}} is due on {{Deadline}}.</p><p>Days remaining: {{DaysRemaining}}</p>',
                '{{ModuleName}} for {{PeriodLabel}} is due on {{Deadline}}. Days remaining: {{DaysRemaining}}.',
                '[""InstitutionName"",""ModuleName"",""PeriodLabel"",""Deadline"",""DaysRemaining"",""SubmitUrl""]'
            ),
            (
                'deadline.t7',
                '7 Days Until {{ModuleName}} Deadline - {{PeriodLabel}}',
                '<p>{{ModuleName}} for {{PeriodLabel}} is due on {{Deadline}}.</p><p>Only {{DaysRemaining}} days left.</p><p><a href=""{{SubmitUrl}}"">Submit now</a></p>',
                '{{ModuleName}} for {{PeriodLabel}} is due on {{Deadline}}. {{DaysRemaining}} days left. Submit: {{SubmitUrl}}',
                '[""InstitutionName"",""ModuleName"",""PeriodLabel"",""Deadline"",""DaysRemaining"",""SubmitUrl""]'
            ),
            (
                'deadline.t3',
                '3 Days Until {{ModuleName}} Deadline - {{PeriodLabel}}',
                '<p>Urgent: {{ModuleName}} for {{PeriodLabel}} is due on {{Deadline}}.</p>',
                'Urgent: {{ModuleName}} for {{PeriodLabel}} is due on {{Deadline}}.',
                '[""InstitutionName"",""ModuleName"",""PeriodLabel"",""Deadline"",""DaysRemaining"",""SubmitUrl""]'
            ),
            (
                'deadline.t1',
                'Deadline Tomorrow: {{ModuleName}} - {{PeriodLabel}}',
                '<p>Critical reminder: {{ModuleName}} return is due tomorrow ({{Deadline}}).</p><p><a href=""{{SubmitUrl}}"">Submit now</a></p>',
                'Critical reminder: {{ModuleName}} return is due tomorrow ({{Deadline}}). Submit: {{SubmitUrl}}',
                '[""InstitutionName"",""ModuleName"",""PeriodLabel"",""Deadline"",""SubmitUrl""]'
            ),
            (
                'deadline.overdue',
                'OVERDUE: {{ModuleName}} Return Past Deadline',
                '<p>{{ModuleName}} return for {{PeriodLabel}} is overdue by {{DaysOverdue}} day(s).</p><p><a href=""{{SubmitUrl}}"">Submit immediately</a></p>',
                '{{ModuleName}} return for {{PeriodLabel}} is overdue by {{DaysOverdue}} day(s). Submit immediately: {{SubmitUrl}}',
                '[""InstitutionName"",""ModuleName"",""PeriodLabel"",""Deadline"",""DaysOverdue"",""SubmitUrl""]'
            ),
            (
                'user.invited',
                'You have been invited to {{CompanyName}} on RegOS',
                '<p>Hello {{RecipientName}},</p><p>{{InvitedBy}} invited you to join {{CompanyName}} as {{Role}}.</p><p><a href=""{{SetupUrl}}"">Complete setup</a></p>',
                '{{InvitedBy}} invited you to join {{CompanyName}} as {{Role}}. Setup: {{SetupUrl}}',
                '[""InvitedBy"",""CompanyName"",""Role"",""SetupUrl""]'
            ),
            (
                'subscription.payment_overdue',
                'Invoice {{InvoiceNumber}} Is Overdue',
                '<p>Invoice {{InvoiceNumber}} is overdue.</p><p>Amount: {{Amount}}</p><p>Due date: {{DueDate}}</p><p><a href=""{{PayUrl}}"">Pay now</a></p>',
                'Invoice {{InvoiceNumber}} is overdue. Amount: {{Amount}}. Due date: {{DueDate}}. Pay: {{PayUrl}}',
                '[""CompanyName"",""InvoiceNumber"",""Amount"",""DueDate"",""PayUrl""]'
            ),
            (
                'subscription.suspended',
                'Subscription Suspended',
                '<p>Your subscription has been suspended due to unpaid invoices.</p>',
                'Your subscription has been suspended due to unpaid invoices.',
                '[""CompanyName""]'
            ),
            (
                'export.ready',
                '{{ExportName}} export is ready',
                '<p>Your requested export ({{ExportName}}) is ready for download.</p><p><a href=""{{ActionUrl}}"">Open export</a></p>',
                'Your requested export ({{ExportName}}) is ready. Open: {{ActionUrl}}',
                '[""ExportName"",""ActionUrl""]'
            );

            MERGE dbo.email_templates AS target
            USING (
                SELECT
                    TemplateCode,
                    CAST(NULL AS UNIQUEIDENTIFIER) AS TenantId,
                    Subject,
                    HtmlBody,
                    PlainTextBody,
                    Variables
                FROM @templates
            ) AS source
                ON target.TemplateCode = source.TemplateCode
               AND ((target.TenantId IS NULL AND source.TenantId IS NULL) OR target.TenantId = source.TenantId)
            WHEN MATCHED THEN
                UPDATE SET
                    Subject = source.Subject,
                    HtmlBody = source.HtmlBody,
                    PlainTextBody = source.PlainTextBody,
                    Variables = source.Variables,
                    IsActive = 1
            WHEN NOT MATCHED THEN
                INSERT (TemplateCode, TenantId, Subject, HtmlBody, PlainTextBody, Variables, IsActive)
                VALUES (source.TemplateCode, source.TenantId, source.Subject, source.HtmlBody, source.PlainTextBody, source.Variables, 1);
        ");

        migrationBuilder.Sql(@"
            IF OBJECT_ID(N'dbo.fn_TenantFilter', N'IF') IS NULL
            BEGIN
                EXEC('
                    CREATE FUNCTION dbo.fn_TenantFilter(@TenantId UNIQUEIDENTIFIER)
                    RETURNS TABLE
                    WITH SCHEMABINDING
                    AS
                    RETURN
                    SELECT 1 AS fn_accessResult
                    WHERE @TenantId = CAST(SESSION_CONTEXT(N''TenantId'') AS UNIQUEIDENTIFIER)
                       OR @TenantId IS NULL
                       OR SESSION_CONTEXT(N''TenantId'') IS NULL;');
            END;

            IF OBJECT_ID(N'dbo.TenantSecurityPolicy', N'SP') IS NOT NULL
                DROP SECURITY POLICY dbo.TenantSecurityPolicy;

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

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF OBJECT_ID(N'dbo.notification_deliveries', N'U') IS NOT NULL
                DROP TABLE dbo.notification_deliveries;

            IF OBJECT_ID(N'dbo.notification_preferences', N'U') IS NOT NULL
                DROP TABLE dbo.notification_preferences;

            IF OBJECT_ID(N'dbo.email_templates', N'U') IS NOT NULL
                DROP TABLE dbo.email_templates;

            IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_portal_notifications_Tenant_Event_Created' AND object_id = OBJECT_ID('dbo.portal_notifications'))
                DROP INDEX IX_portal_notifications_Tenant_Event_Created ON dbo.portal_notifications;

            IF COL_LENGTH('meta.institution_users', 'PhoneNumber') IS NOT NULL
                ALTER TABLE meta.institution_users DROP COLUMN PhoneNumber;

            IF COL_LENGTH('dbo.portal_notifications', 'RecipientPhone') IS NOT NULL
                ALTER TABLE dbo.portal_notifications DROP COLUMN RecipientPhone;

            IF COL_LENGTH('dbo.portal_notifications', 'RecipientEmail') IS NOT NULL
                ALTER TABLE dbo.portal_notifications DROP COLUMN RecipientEmail;

            IF COL_LENGTH('dbo.portal_notifications', 'Priority') IS NOT NULL
                ALTER TABLE dbo.portal_notifications DROP COLUMN Priority;

            IF COL_LENGTH('dbo.portal_notifications', 'Channel') IS NOT NULL
                ALTER TABLE dbo.portal_notifications DROP COLUMN Channel;

            IF COL_LENGTH('dbo.portal_notifications', 'EventType') IS NOT NULL
                ALTER TABLE dbo.portal_notifications DROP COLUMN EventType;
        ");
    }
}
