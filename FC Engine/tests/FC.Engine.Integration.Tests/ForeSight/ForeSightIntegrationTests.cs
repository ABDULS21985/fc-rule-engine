using System.Text;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;
using Xunit;

namespace FC.Engine.Integration.Tests.ForeSight;

public sealed class ForeSightFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("YourStrong!Passw0rd")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        await EnsureForeSightSupportTablesAsync(db);
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

    private static async Task EnsureForeSightSupportTablesAsync(MetadataDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            IF SCHEMA_ID(N'meta') IS NULL
                EXEC(N'CREATE SCHEMA [meta]');

            IF OBJECT_ID(N'[meta].[anomaly_model_versions]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[anomaly_model_versions]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_anomaly_model_versions] PRIMARY KEY,
                    [ModuleCode] VARCHAR(40) NOT NULL,
                    [RegulatorCode] VARCHAR(10) NOT NULL,
                    [VersionNumber] INT NOT NULL,
                    [Status] VARCHAR(20) NOT NULL,
                    [TrainingStartedAt] DATETIME2(3) NOT NULL,
                    [TrainingCompletedAt] DATETIME2(3) NULL,
                    [SubmissionCount] INT NOT NULL CONSTRAINT [DF_anomaly_model_versions_SubmissionCount] DEFAULT (0),
                    [ObservationCount] INT NOT NULL CONSTRAINT [DF_anomaly_model_versions_ObservationCount] DEFAULT (0),
                    [TenantCount] INT NOT NULL CONSTRAINT [DF_anomaly_model_versions_TenantCount] DEFAULT (0),
                    [PeriodCount] INT NOT NULL CONSTRAINT [DF_anomaly_model_versions_PeriodCount] DEFAULT (0),
                    [PromotedAt] DATETIME2(3) NULL,
                    [PromotedBy] VARCHAR(100) NULL,
                    [RetiredAt] DATETIME2(3) NULL,
                    [Notes] NVARCHAR(2000) NULL,
                    [CreatedAt] DATETIME2(3) NOT NULL CONSTRAINT [DF_anomaly_model_versions_CreatedAt] DEFAULT (SYSUTCDATETIME())
                );
            END;

            IF OBJECT_ID(N'[meta].[anomaly_reports]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[anomaly_reports]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_anomaly_reports] PRIMARY KEY,
                    [TenantId] UNIQUEIDENTIFIER NOT NULL,
                    [InstitutionId] INT NOT NULL,
                    [InstitutionName] NVARCHAR(200) NOT NULL,
                    [SubmissionId] INT NOT NULL,
                    [ModuleCode] VARCHAR(40) NOT NULL,
                    [RegulatorCode] VARCHAR(10) NOT NULL,
                    [PeriodCode] VARCHAR(20) NOT NULL,
                    [ModelVersionId] INT NOT NULL,
                    [OverallQualityScore] DECIMAL(6,2) NOT NULL CONSTRAINT [DF_anomaly_reports_OverallQualityScore] DEFAULT (100),
                    [TotalFieldsAnalysed] INT NOT NULL CONSTRAINT [DF_anomaly_reports_TotalFieldsAnalysed] DEFAULT (0),
                    [TotalFindings] INT NOT NULL CONSTRAINT [DF_anomaly_reports_TotalFindings] DEFAULT (0),
                    [AlertCount] INT NOT NULL CONSTRAINT [DF_anomaly_reports_AlertCount] DEFAULT (0),
                    [WarningCount] INT NOT NULL CONSTRAINT [DF_anomaly_reports_WarningCount] DEFAULT (0),
                    [InfoCount] INT NOT NULL CONSTRAINT [DF_anomaly_reports_InfoCount] DEFAULT (0),
                    [RelationshipFindings] INT NOT NULL CONSTRAINT [DF_anomaly_reports_RelationshipFindings] DEFAULT (0),
                    [TemporalFindings] INT NOT NULL CONSTRAINT [DF_anomaly_reports_TemporalFindings] DEFAULT (0),
                    [PeerFindings] INT NOT NULL CONSTRAINT [DF_anomaly_reports_PeerFindings] DEFAULT (0),
                    [TrafficLight] VARCHAR(10) NOT NULL CONSTRAINT [DF_anomaly_reports_TrafficLight] DEFAULT ('GREEN'),
                    [NarrativeSummary] NVARCHAR(2000) NOT NULL CONSTRAINT [DF_anomaly_reports_NarrativeSummary] DEFAULT (N''),
                    [AnalysedAt] DATETIME2(3) NOT NULL CONSTRAINT [DF_anomaly_reports_AnalysedAt] DEFAULT (SYSUTCDATETIME()),
                    [AnalysisDurationMs] INT NULL,
                    [CreatedAt] DATETIME2(3) NOT NULL CONSTRAINT [DF_anomaly_reports_CreatedAt] DEFAULT (SYSUTCDATETIME())
                );

                CREATE UNIQUE INDEX [IX_anomaly_reports_submission_model]
                    ON [meta].[anomaly_reports] ([SubmissionId], [ModelVersionId]);
            END;

            IF OBJECT_ID(N'[meta].[complianceiq_conversations]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[complianceiq_conversations]
                (
                    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_complianceiq_conversations] PRIMARY KEY,
                    [TenantId] UNIQUEIDENTIFIER NOT NULL,
                    [UserId] NVARCHAR(100) NOT NULL,
                    [UserRole] NVARCHAR(60) NOT NULL,
                    [IsRegulatorContext] BIT NOT NULL CONSTRAINT [DF_complianceiq_conversations_IsRegulatorContext] DEFAULT (0),
                    [Title] NVARCHAR(200) NOT NULL,
                    [StartedAt] DATETIME2(3) NOT NULL,
                    [LastActivityAt] DATETIME2(3) NOT NULL,
                    [TurnCount] INT NOT NULL CONSTRAINT [DF_complianceiq_conversations_TurnCount] DEFAULT (0),
                    [IsActive] BIT NOT NULL CONSTRAINT [DF_complianceiq_conversations_IsActive] DEFAULT (1)
                );

                CREATE INDEX [IX_complianceiq_conversations_tenant_user]
                    ON [meta].[complianceiq_conversations] ([TenantId], [UserId], [LastActivityAt]);
            END;

            IF OBJECT_ID(N'[meta].[complianceiq_turns]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[complianceiq_turns]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_complianceiq_turns] PRIMARY KEY,
                    [ConversationId] UNIQUEIDENTIFIER NOT NULL,
                    [TenantId] UNIQUEIDENTIFIER NOT NULL,
                    [UserId] NVARCHAR(100) NOT NULL,
                    [UserRole] NVARCHAR(60) NOT NULL,
                    [TurnNumber] INT NOT NULL,
                    [QueryText] NVARCHAR(MAX) NOT NULL,
                    [IntentCode] NVARCHAR(40) NOT NULL,
                    [IntentConfidence] DECIMAL(5,4) NOT NULL,
                    [ExtractedEntitiesJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_complianceiq_turns_ExtractedEntitiesJson] DEFAULT (N'{{}}'),
                    [TemplateCode] NVARCHAR(60) NOT NULL,
                    [ResolvedParametersJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_complianceiq_turns_ResolvedParametersJson] DEFAULT (N'{{}}'),
                    [ExecutedPlan] NVARCHAR(MAX) NOT NULL,
                    [RowCount] INT NOT NULL CONSTRAINT [DF_complianceiq_turns_RowCount] DEFAULT (0),
                    [ExecutionTimeMs] INT NOT NULL CONSTRAINT [DF_complianceiq_turns_ExecutionTimeMs] DEFAULT (0),
                    [ResponseText] NVARCHAR(MAX) NOT NULL,
                    [ResponseDataJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_complianceiq_turns_ResponseDataJson] DEFAULT (N'[]'),
                    [VisualizationType] NVARCHAR(30) NOT NULL CONSTRAINT [DF_complianceiq_turns_VisualizationType] DEFAULT (N'text'),
                    [ConfidenceLevel] NVARCHAR(10) NOT NULL,
                    [CitationsJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_complianceiq_turns_CitationsJson] DEFAULT (N'[]'),
                    [FollowUpSuggestionsJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_complianceiq_turns_FollowUpSuggestionsJson] DEFAULT (N'[]'),
                    [TotalTimeMs] INT NOT NULL CONSTRAINT [DF_complianceiq_turns_TotalTimeMs] DEFAULT (0),
                    [ErrorMessage] NVARCHAR(500) NULL,
                    [CreatedAt] DATETIME2(3) NOT NULL
                );

                CREATE INDEX [IX_complianceiq_turns_conversation]
                    ON [meta].[complianceiq_turns] ([ConversationId], [TurnNumber]);

                CREATE INDEX [IX_complianceiq_turns_tenant]
                    ON [meta].[complianceiq_turns] ([TenantId], [CreatedAt]);
            END;

            IF OBJECT_ID(N'[dbo].[chs_score_snapshots]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[chs_score_snapshots]
                (
                    [Id] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_chs_score_snapshots] PRIMARY KEY,
                    [TenantId] UNIQUEIDENTIFIER NOT NULL,
                    [PeriodLabel] NVARCHAR(20) NOT NULL,
                    [ComputedAt] DATETIME2(3) NOT NULL,
                    [OverallScore] DECIMAL(5,2) NOT NULL,
                    [Rating] INT NOT NULL,
                    [FilingTimeliness] DECIMAL(5,2) NOT NULL CONSTRAINT [DF_chs_score_snapshots_FilingTimeliness] DEFAULT (0),
                    [DataQuality] DECIMAL(5,2) NOT NULL CONSTRAINT [DF_chs_score_snapshots_DataQuality] DEFAULT (0),
                    [RegulatoryCapital] DECIMAL(5,2) NOT NULL CONSTRAINT [DF_chs_score_snapshots_RegulatoryCapital] DEFAULT (0),
                    [AuditGovernance] DECIMAL(5,2) NOT NULL CONSTRAINT [DF_chs_score_snapshots_AuditGovernance] DEFAULT (0),
                    [Engagement] DECIMAL(5,2) NOT NULL CONSTRAINT [DF_chs_score_snapshots_Engagement] DEFAULT (0)
                );

                CREATE UNIQUE INDEX [IX_chs_score_snapshots_tenant_period]
                    ON [dbo].[chs_score_snapshots] ([TenantId], [PeriodLabel]);

                CREATE INDEX [IX_chs_score_snapshots_computed]
                    ON [dbo].[chs_score_snapshots] ([ComputedAt]);
            END;

            IF OBJECT_ID(N'[dbo].[subscription_plans]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[subscription_plans]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_subscription_plans] PRIMARY KEY,
                    [PlanCode] NVARCHAR(30) NOT NULL,
                    [PlanName] NVARCHAR(100) NOT NULL,
                    [Description] NVARCHAR(500) NULL,
                    [Tier] INT NOT NULL CONSTRAINT [DF_subscription_plans_Tier] DEFAULT (0),
                    [MaxModules] INT NOT NULL CONSTRAINT [DF_subscription_plans_MaxModules] DEFAULT (1),
                    [MaxUsersPerEntity] INT NOT NULL CONSTRAINT [DF_subscription_plans_MaxUsersPerEntity] DEFAULT (10),
                    [MaxEntities] INT NOT NULL CONSTRAINT [DF_subscription_plans_MaxEntities] DEFAULT (1),
                    [MaxApiCallsPerMonth] INT NOT NULL CONSTRAINT [DF_subscription_plans_MaxApiCallsPerMonth] DEFAULT (0),
                    [MaxStorageMb] INT NOT NULL CONSTRAINT [DF_subscription_plans_MaxStorageMb] DEFAULT (500),
                    [BasePriceMonthly] DECIMAL(18,2) NOT NULL,
                    [BasePriceAnnual] DECIMAL(18,2) NOT NULL,
                    [TrialDays] INT NOT NULL CONSTRAINT [DF_subscription_plans_TrialDays] DEFAULT (14),
                    [Features] NVARCHAR(MAX) NULL,
                    [IsActive] BIT NOT NULL CONSTRAINT [DF_subscription_plans_IsActive] DEFAULT (1),
                    [DisplayOrder] INT NOT NULL CONSTRAINT [DF_subscription_plans_DisplayOrder] DEFAULT (0),
                    [CreatedAt] DATETIME2(3) NOT NULL CONSTRAINT [DF_subscription_plans_CreatedAt] DEFAULT (SYSUTCDATETIME())
                );

                CREATE UNIQUE INDEX [IX_subscription_plans_code]
                    ON [dbo].[subscription_plans] ([PlanCode]);
            END;

            IF OBJECT_ID(N'[dbo].[subscriptions]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[subscriptions]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_subscriptions] PRIMARY KEY,
                    [TenantId] UNIQUEIDENTIFIER NOT NULL,
                    [PlanId] INT NOT NULL,
                    [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_subscriptions_Status] DEFAULT (N'Trial'),
                    [BillingFrequency] NVARCHAR(10) NOT NULL CONSTRAINT [DF_subscriptions_BillingFrequency] DEFAULT (N'Monthly'),
                    [CurrentPeriodStart] DATETIME2(3) NOT NULL,
                    [CurrentPeriodEnd] DATETIME2(3) NOT NULL,
                    [TrialEndsAt] DATETIME2(3) NULL,
                    [GracePeriodEndsAt] DATETIME2(3) NULL,
                    [CancelledAt] DATETIME2(3) NULL,
                    [CancellationReason] NVARCHAR(500) NULL,
                    [CreatedAt] DATETIME2(3) NOT NULL CONSTRAINT [DF_subscriptions_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                    [UpdatedAt] DATETIME2(3) NOT NULL CONSTRAINT [DF_subscriptions_UpdatedAt] DEFAULT (SYSUTCDATETIME())
                );

                CREATE INDEX [IX_subscriptions_tenant_status]
                    ON [dbo].[subscriptions] ([TenantId], [Status]);
            END;

            IF OBJECT_ID(N'[dbo].[invoices]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[invoices]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_invoices] PRIMARY KEY,
                    [TenantId] UNIQUEIDENTIFIER NOT NULL,
                    [InvoiceNumber] NVARCHAR(50) NOT NULL,
                    [SubscriptionId] INT NOT NULL,
                    [PeriodStart] DATE NOT NULL,
                    [PeriodEnd] DATE NOT NULL,
                    [Subtotal] DECIMAL(18,2) NOT NULL,
                    [VatRate] DECIMAL(5,4) NOT NULL CONSTRAINT [DF_invoices_VatRate] DEFAULT (0.0750),
                    [VatAmount] DECIMAL(18,2) NOT NULL,
                    [TotalAmount] DECIMAL(18,2) NOT NULL,
                    [Currency] NVARCHAR(3) NOT NULL CONSTRAINT [DF_invoices_Currency] DEFAULT (N'NGN'),
                    [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_invoices_Status] DEFAULT (N'Draft'),
                    [IssuedAt] DATETIME2(3) NULL,
                    [DueDate] DATE NULL,
                    [PaidAt] DATETIME2(3) NULL,
                    [VoidedAt] DATETIME2(3) NULL,
                    [VoidReason] NVARCHAR(500) NULL,
                    [Notes] NVARCHAR(1000) NULL,
                    [CreatedAt] DATETIME2(3) NOT NULL CONSTRAINT [DF_invoices_CreatedAt] DEFAULT (SYSUTCDATETIME())
                );

                CREATE UNIQUE INDEX [IX_invoices_number]
                    ON [dbo].[invoices] ([InvoiceNumber]);

                CREATE INDEX [IX_invoices_tenant_status_due]
                    ON [dbo].[invoices] ([TenantId], [Status], [DueDate]);
            END;

            IF OBJECT_ID(N'[dbo].[usage_records]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[usage_records]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_usage_records] PRIMARY KEY,
                    [TenantId] UNIQUEIDENTIFIER NOT NULL,
                    [RecordDate] DATE NOT NULL,
                    [ActiveUsers] INT NOT NULL CONSTRAINT [DF_usage_records_ActiveUsers] DEFAULT (0),
                    [ActiveEntities] INT NOT NULL CONSTRAINT [DF_usage_records_ActiveEntities] DEFAULT (0),
                    [ActiveModules] INT NOT NULL CONSTRAINT [DF_usage_records_ActiveModules] DEFAULT (0),
                    [ReturnsSubmitted] INT NOT NULL CONSTRAINT [DF_usage_records_ReturnsSubmitted] DEFAULT (0),
                    [StorageUsedMb] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_usage_records_StorageUsedMb] DEFAULT (0),
                    [ApiCallCount] INT NOT NULL CONSTRAINT [DF_usage_records_ApiCallCount] DEFAULT (0)
                );

                CREATE UNIQUE INDEX [IX_usage_records_tenant_recorddate]
                    ON [dbo].[usage_records] ([TenantId], [RecordDate]);
            END;

            IF OBJECT_ID(N'[dbo].[partner_support_tickets]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[partner_support_tickets]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_partner_support_tickets] PRIMARY KEY,
                    [TenantId] UNIQUEIDENTIFIER NOT NULL,
                    [PartnerTenantId] UNIQUEIDENTIFIER NOT NULL,
                    [RaisedByUserId] INT NOT NULL,
                    [RaisedByUserName] NVARCHAR(100) NOT NULL,
                    [Title] NVARCHAR(200) NOT NULL,
                    [Description] NVARCHAR(2000) NOT NULL,
                    [Priority] NVARCHAR(20) NOT NULL CONSTRAINT [DF_partner_support_tickets_Priority] DEFAULT (N'Normal'),
                    [Status] NVARCHAR(20) NOT NULL CONSTRAINT [DF_partner_support_tickets_Status] DEFAULT (N'Open'),
                    [EscalationLevel] INT NOT NULL CONSTRAINT [DF_partner_support_tickets_EscalationLevel] DEFAULT (0),
                    [EscalatedAt] DATETIME2(3) NULL,
                    [EscalatedByUserId] INT NULL,
                    [SlaDueAt] DATETIME2(3) NOT NULL,
                    [CreatedAt] DATETIME2(3) NOT NULL CONSTRAINT [DF_partner_support_tickets_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                    [UpdatedAt] DATETIME2(3) NOT NULL CONSTRAINT [DF_partner_support_tickets_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
                    [ResolvedAt] DATETIME2(3) NULL
                );

                CREATE INDEX [IX_partner_support_tickets_partner_status_priority]
                    ON [dbo].[partner_support_tickets] ([PartnerTenantId], [Status], [Priority]);
            END;

            IF OBJECT_ID(N'[meta].[prudential_metrics]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[prudential_metrics]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_prudential_metrics] PRIMARY KEY,
                    [InstitutionId] INT NOT NULL,
                    [RegulatorCode] VARCHAR(10) NOT NULL,
                    [InstitutionType] VARCHAR(20) NOT NULL,
                    [AsOfDate] DATE NOT NULL,
                    [PeriodCode] VARCHAR(20) NOT NULL,
                    [CAR] DECIMAL(18,4) NULL,
                    [NPLRatio] DECIMAL(18,4) NULL,
                    [LCR] DECIMAL(18,4) NULL,
                    [ProvisioningCoverage] DECIMAL(18,4) NULL,
                    [CreatedAt] DATETIME2(3) NOT NULL CONSTRAINT [DF_prudential_metrics_CreatedAt] DEFAULT (SYSUTCDATETIME())
                );

                CREATE INDEX [IX_prudential_metrics_institution_asof]
                    ON [meta].[prudential_metrics] ([InstitutionId], [AsOfDate]);
            END;

            IF OBJECT_ID(N'[meta].[ewi_triggers]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[ewi_triggers]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ewi_triggers] PRIMARY KEY,
                    [EWICode] VARCHAR(50) NOT NULL,
                    [InstitutionId] INT NOT NULL,
                    [RegulatorCode] VARCHAR(10) NOT NULL,
                    [PeriodCode] VARCHAR(20) NOT NULL,
                    [Severity] VARCHAR(20) NOT NULL,
                    [TriggerValue] DECIMAL(18,4) NOT NULL,
                    [ThresholdValue] DECIMAL(18,4) NOT NULL,
                    [ComputationRunId] UNIQUEIDENTIFIER NOT NULL,
                    [IsActive] BIT NOT NULL CONSTRAINT [DF_ewi_triggers_IsActive] DEFAULT (1),
                    [IsSystemic] BIT NOT NULL CONSTRAINT [DF_ewi_triggers_IsSystemic] DEFAULT (0),
                    [CreatedAt] DATETIME2(3) NOT NULL CONSTRAINT [DF_ewi_triggers_CreatedAt] DEFAULT (SYSUTCDATETIME())
                );

                CREATE INDEX [IX_ewi_triggers_institution_active]
                    ON [meta].[ewi_triggers] ([InstitutionId], [IsActive], [Severity]);
            END;

            IF OBJECT_ID(N'[meta].[camels_ratings]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[camels_ratings]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_camels_ratings] PRIMARY KEY,
                    [InstitutionId] INT NOT NULL,
                    [RegulatorCode] VARCHAR(10) NOT NULL,
                    [PeriodCode] VARCHAR(20) NOT NULL,
                    [AsOfDate] DATE NOT NULL,
                    [ComputedAt] DATETIME2(3) NOT NULL CONSTRAINT [DF_camels_ratings_ComputedAt] DEFAULT (SYSUTCDATETIME()),
                    [CapitalScore] TINYINT NOT NULL,
                    [AssetQualityScore] TINYINT NOT NULL,
                    [ManagementScore] TINYINT NOT NULL,
                    [EarningsScore] TINYINT NOT NULL,
                    [LiquidityScore] TINYINT NOT NULL,
                    [SensitivityScore] TINYINT NOT NULL,
                    [CompositeScore] DECIMAL(5,2) NOT NULL,
                    [RiskBand] VARCHAR(20) NOT NULL,
                    [ComputationRunId] UNIQUEIDENTIFIER NOT NULL
                );

                CREATE INDEX [IX_camels_ratings_institution_computed]
                    ON [meta].[camels_ratings] ([InstitutionId], [ComputedAt]);
            END;
            """);
    }
}

public sealed class ForeSightIntegrationTests : IClassFixture<ForeSightFixture>
{
    private readonly ForeSightFixture _fixture;

    public ForeSightIntegrationTests(ForeSightFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAllPredictionsAsync_PersistsPredictionsAlerts_AndTenantDashboard()
    {
        await using var db = _fixture.CreateDbContext();
        var stressed = await SeedScenarioAsync(db, "stressed", stressed: true);
        var stable = await SeedScenarioAsync(db, "stable", stressed: false);
        var service = BuildService(db);

        await service.RunAllPredictionsAsync(stressed.TenantId, "TEST");
        await service.RunAllPredictionsAsync(stable.TenantId, "TEST");
        db.ChangeTracker.Clear();

        var stressedPredictions = await db.ForeSightPredictions
            .AsNoTracking()
            .Where(x => x.TenantId == stressed.TenantId)
            .ToListAsync();

        stressedPredictions.Should().Contain(x => x.ModelCode == ForeSightModelCodes.FilingRisk && !x.IsSuppressed);
        stressedPredictions.Should().Contain(x => x.ModelCode == ForeSightModelCodes.CapitalBreach && !x.IsSuppressed);
        stressedPredictions.Should().Contain(x => x.ModelCode == ForeSightModelCodes.ComplianceTrend && !x.IsSuppressed);
        stressedPredictions.Should().Contain(x => x.ModelCode == ForeSightModelCodes.ChurnRisk && !x.IsSuppressed);
        stressedPredictions.Should().Contain(x => x.ModelCode == ForeSightModelCodes.RegulatoryAction && !x.IsSuppressed);

        var stressedAlerts = await db.ForeSightAlerts
            .AsNoTracking()
            .Where(x => x.TenantId == stressed.TenantId)
            .ToListAsync();

        stressedAlerts.Should().NotBeEmpty();
        stressedAlerts.Should().Contain(x => x.AlertType == "FILING_RISK");
        stressedAlerts.Should().Contain(x => x.AlertType == "REG_ACTION_PRIORITY");

        var dashboard = await service.GetTenantDashboardAsync(stressed.TenantId);
        dashboard.FilingRisks.Should().NotBeEmpty();
        dashboard.CapitalForecasts.Should().NotBeEmpty();
        dashboard.ComplianceForecast.Should().NotBeNull();
        dashboard.Alerts.Should().NotBeEmpty();

        var stableDashboard = await service.GetTenantDashboardAsync(stable.TenantId);
        stableDashboard.FilingRisks.Should().NotBeEmpty();
        stableDashboard.Alerts.Count.Should().BeLessThan(dashboard.Alerts.Count);
    }

    [Fact]
    public async Task CrossTenantRankings_ReturnHighestRiskTenantFirst()
    {
        await using var db = _fixture.CreateDbContext();
        var stressed = await SeedScenarioAsync(db, "rank-stressed", stressed: true);
        var stable = await SeedScenarioAsync(db, "rank-stable", stressed: false);
        var service = BuildService(db);

        await service.RunAllPredictionsAsync(stressed.TenantId, "TEST");
        await service.RunAllPredictionsAsync(stable.TenantId, "TEST");

        var regulatory = await service.GetRegulatoryRiskRankingAsync("CBN", "DMB");
        regulatory.Should().HaveCountGreaterThanOrEqualTo(2);
        regulatory.Select(x => x.InterventionProbability).Should().BeInDescendingOrder();
        regulatory.First().TenantId.Should().Be(stressed.TenantId);

        var churn = await service.GetChurnRiskDashboardAsync();
        churn.Should().HaveCountGreaterThanOrEqualTo(2);
        churn.Select(x => x.ChurnProbability).Should().BeInDescendingOrder();
        churn.First().TenantId.Should().Be(stressed.TenantId);
    }

    [Fact]
    public async Task ExportFilingRiskReportAsync_ReturnsPdfDocument()
    {
        await using var db = _fixture.CreateDbContext();
        var stressed = await SeedScenarioAsync(db, "export", stressed: true);
        var service = BuildService(db);

        await service.RunAllPredictionsAsync(stressed.TenantId, "TEST");

        var bytes = await service.ExportFilingRiskReportAsync(stressed.TenantId);
        bytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(bytes.Take(4).ToArray()).Should().Be("%PDF");
    }

    private ForeSightService BuildService(MetadataDbContext db)
    {
        return new ForeSightService(
            db,
            new TestDbConnectionFactory(_fixture.ConnectionString),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<ForeSightService>.Instance);
    }

    private async Task<SeededScenario> SeedScenarioAsync(MetadataDbContext db, string label, bool stressed)
    {
        await EnsureJurisdictionAsync(db);

        var tenant = Tenant.Create(
            stressed ? $"ForeSight Stress {label}" : $"ForeSight Stable {label}",
            $"foresight-{label}-{Guid.NewGuid():N}"[..24],
            TenantType.Institution,
            $"{label}@example.com");
        tenant.Activate();

        db.Tenants.Add(tenant);

        var institution = new Institution
        {
            TenantId = tenant.TenantId,
            JurisdictionId = 1,
            InstitutionCode = $"FS-{label[..Math.Min(label.Length, 8)].ToUpperInvariant()}",
            InstitutionName = stressed ? $"Stress Bank {label}" : $"Stable Bank {label}",
            LicenseType = "DMB",
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddMonths(-12),
            LastSubmissionAt = DateTime.UtcNow.AddDays(-3),
            SubscriptionTier = "Premium"
        };
        db.Institutions.Add(institution);

        var licenceType = await GetOrCreateLicenceTypeAsync(db, "DMB", "Deposit Money Bank", "CBN");
        db.TenantLicenceTypes.Add(new TenantLicenceType
        {
            TenantId = tenant.TenantId,
            LicenceTypeId = licenceType.Id,
            EffectiveDate = DateTime.UtcNow.AddYears(-1),
            IsActive = true,
            RegistrationNumber = $"REG-{Guid.NewGuid():N}"[..12]
        });

        var module = new Module
        {
            JurisdictionId = 1,
            ModuleCode = $"CBN_FS_{label.ToUpperInvariant().Replace("-", "_")}",
            ModuleName = $"CBN ForeSight Return {label}",
            RegulatorCode = "CBN",
            Description = "ForeSight integration-test module",
            SheetCount = 1,
            DefaultFrequency = "Monthly",
            IsActive = true,
            DisplayOrder = 1,
            DeadlineOffsetDays = 15,
            CreatedAt = DateTime.UtcNow.AddMonths(-6)
        };
        db.Modules.Add(module);
        await db.SaveChangesAsync();

        var template = new ReturnTemplate
        {
            ModuleId = module.Id,
            ReturnCode = module.ModuleCode,
            Name = module.ModuleName,
            Description = "Integration-test template",
            Frequency = ReturnFrequency.Monthly,
            StructuralCategory = StructuralCategory.FixedRow,
            PhysicalTableName = $"rt_{module.ModuleCode.ToLowerInvariant()}",
            XmlRootElement = "Return",
            XmlNamespace = "urn:regos:test",
            IsSystemTemplate = true,
            InstitutionType = "DMB",
            CreatedAt = DateTime.UtcNow.AddMonths(-6),
            CreatedBy = "test",
            UpdatedAt = DateTime.UtcNow.AddMonths(-1),
            UpdatedBy = "test"
        };
        db.ReturnTemplates.Add(template);
        await db.SaveChangesAsync();

        var templateVersion = new TemplateVersion
        {
            TenantId = null,
            TemplateId = template.Id,
            VersionNumber = 1,
            Status = TemplateStatus.Published,
            EffectiveFrom = DateTime.UtcNow.Date.AddMonths(-6),
            PublishedAt = DateTime.UtcNow.AddMonths(-6),
            ApprovedAt = DateTime.UtcNow.AddMonths(-6),
            ApprovedBy = "test",
            CreatedAt = DateTime.UtcNow.AddMonths(-6),
            CreatedBy = "test"
        };
        db.TemplateVersions.Add(templateVersion);
        await db.SaveChangesAsync();

        for (var index = 1; index <= 10; index++)
        {
            db.TemplateFields.Add(new TemplateField
            {
                TemplateVersionId = templateVersion.Id,
                FieldName = $"field_{index}",
                DisplayName = $"Field {index}",
                XmlElementName = $"Field{index}",
                SectionName = "Main",
                SectionOrder = 1,
                FieldOrder = index,
                DataType = FieldDataType.Decimal,
                SqlType = "decimal(18,2)",
                IsRequired = true,
                CreatedAt = DateTime.UtcNow.AddMonths(-6)
            });
        }

        var today = DateTime.UtcNow.Date;
        var historicalPeriods = new List<ReturnPeriod>();
        for (var monthsBack = 6; monthsBack >= 1; monthsBack--)
        {
            var reportDate = new DateTime(today.AddMonths(-monthsBack).Year, today.AddMonths(-monthsBack).Month, 1);
            var period = new ReturnPeriod
            {
                TenantId = tenant.TenantId,
                ModuleId = module.Id,
                Year = reportDate.Year,
                Month = reportDate.Month,
                Frequency = "Monthly",
                ReportingDate = reportDate,
                IsOpen = false,
                CreatedAt = reportDate,
                DeadlineDate = reportDate.AddDays(15),
                Status = "Completed",
                NotificationLevel = 0
            };
            historicalPeriods.Add(period);
            db.ReturnPeriods.Add(period);
        }

        var upcomingPeriod = new ReturnPeriod
        {
            TenantId = tenant.TenantId,
            ModuleId = module.Id,
            Year = today.Year,
            Month = today.Month,
            Frequency = "Monthly",
            ReportingDate = new DateTime(today.Year, today.Month, 1),
            IsOpen = true,
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            DeadlineDate = today.AddDays(stressed ? 2 : 12),
            Status = stressed ? "DueSoon" : "Open",
            NotificationLevel = stressed ? 3 : 1
        };
        db.ReturnPeriods.Add(upcomingPeriod);

        var anomalyModelVersion = new AnomalyModelVersion
        {
            ModuleCode = module.ModuleCode,
            RegulatorCode = "CBN",
            VersionNumber = 1,
            Status = "ACTIVE",
            TrainingStartedAt = DateTime.UtcNow.AddMonths(-6),
            TrainingCompletedAt = DateTime.UtcNow.AddMonths(-5),
            SubmissionCount = 120,
            ObservationCount = 1200,
            TenantCount = 20,
            PeriodCount = 12,
            PromotedAt = DateTime.UtcNow.AddMonths(-5),
            PromotedBy = "test",
            CreatedAt = DateTime.UtcNow.AddMonths(-6)
        };
        db.AnomalyModelVersions.Add(anomalyModelVersion);

        var snapshotScores = stressed
            ? new[] { 72m, 68m, 63m, 58m, 53m, 49m }
            : new[] { 78m, 80m, 81m, 83m, 84m, 86m };

        for (var index = 0; index < snapshotScores.Length; index++)
        {
            var overall = snapshotScores[index];
            db.ChsScoreSnapshots.Add(new ChsScoreSnapshot
            {
                TenantId = tenant.TenantId,
                PeriodLabel = $"2026-W{index + 1:D2}",
                ComputedAt = DateTime.UtcNow.AddDays(-35 + (index * 7)),
                OverallScore = overall,
                Rating = overall >= 80m ? 2 : overall >= 70m ? 3 : overall >= 60m ? 4 : 5,
                FilingTimeliness = stressed ? overall - 12m : overall - 2m,
                DataQuality = stressed ? overall - 8m : overall - 1m,
                RegulatoryCapital = stressed ? overall - 10m : overall,
                AuditGovernance = stressed ? overall - 6m : overall,
                Engagement = stressed ? overall - 14m : overall - 1m
            });
        }

        var plan = await GetOrCreatePlanAsync(db);
        var subscription = new Subscription
        {
            TenantId = tenant.TenantId,
            PlanId = plan.Id,
            CurrentPeriodStart = DateTime.UtcNow.AddMonths(-1),
            CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1),
            TrialEndsAt = DateTime.UtcNow.AddDays(-20),
            CreatedAt = DateTime.UtcNow.AddMonths(-2),
            UpdatedAt = DateTime.UtcNow.AddDays(-2)
        };
        subscription.Activate();
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();

        db.Invoices.Add(new Invoice
        {
            TenantId = tenant.TenantId,
            SubscriptionId = subscription.Id,
            InvoiceNumber = $"INV-{Guid.NewGuid():N}"[..12],
            PeriodStart = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)),
            PeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow),
            Subtotal = 100000m,
            VatAmount = 7500m,
            TotalAmount = 107500m,
            Currency = "NGN",
            Status = stressed ? InvoiceStatus.Overdue : InvoiceStatus.Paid,
            IssuedAt = DateTime.UtcNow.AddDays(-40),
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(stressed ? -25 : -5)),
            PaidAt = stressed ? null : DateTime.UtcNow.AddDays(-3),
            CreatedAt = DateTime.UtcNow.AddDays(-45)
        });

        db.ReturnDrafts.Add(new ReturnDraft
        {
            TenantId = tenant.TenantId,
            InstitutionId = institution.Id,
            ReturnCode = module.ModuleCode,
            Period = $"{upcomingPeriod.Year}-{upcomingPeriod.Month:00}",
            DataJson = stressed
                ? """{"field_1":120.5,"field_2":18.3}"""
                : """{"field_1":120.5,"field_2":18.3,"field_3":14.2,"field_4":13.1,"field_5":11.8,"field_6":8.5,"field_7":7.4,"field_8":6.1}""",
            LastSavedAt = DateTime.UtcNow.AddHours(-6),
            SavedBy = "tester"
        });

        db.PartnerSupportTickets.Add(new PartnerSupportTicket
        {
            TenantId = tenant.TenantId,
            PartnerTenantId = tenant.TenantId,
            RaisedByUserId = 1,
            RaisedByUserName = "Tester",
            Title = "Support follow-up",
            Description = "Integration-test support ticket",
            Status = stressed ? PartnerSupportTicketStatus.Open : PartnerSupportTicketStatus.Resolved,
            EscalationLevel = stressed ? 2 : 0,
            SlaDueAt = DateTime.UtcNow.AddDays(2),
            CreatedAt = DateTime.UtcNow.AddDays(-8),
            UpdatedAt = DateTime.UtcNow.AddDays(-1),
            ResolvedAt = stressed ? null : DateTime.UtcNow.AddDays(-1)
        });

        var conversation = new ComplianceIqConversation
        {
            TenantId = tenant.TenantId,
            UserId = "tester",
            UserRole = "ComplianceOfficer",
            IsRegulatorContext = false,
            Title = "ForeSight Test Conversation",
            StartedAt = DateTime.UtcNow.AddDays(-20),
            LastActivityAt = DateTime.UtcNow.AddDays(-2),
            TurnCount = stressed ? 1 : 8,
            IsActive = true
        };
        db.ComplianceIqConversations.Add(conversation);
        await db.SaveChangesAsync();

        for (var index = 0; index < (stressed ? 1 : 8); index++)
        {
            db.ComplianceIqTurns.Add(new ComplianceIqTurn
            {
                ConversationId = conversation.Id,
                TenantId = tenant.TenantId,
                UserId = "tester",
                UserRole = "ComplianceOfficer",
                TurnNumber = index + 1,
                QueryText = "Show filing obligations",
                IntentCode = "DEADLINE",
                IntentConfidence = 0.92m,
                TemplateCode = "DL_CALENDAR",
                ExecutedPlan = "test",
                ResponseText = "ok",
                ConfidenceLevel = "HIGH",
                CreatedAt = DateTime.UtcNow.AddDays(-(10 - index))
            });
        }

        for (var day = 60; day >= 1; day -= 3)
        {
            var when = DateTime.UtcNow.Date.AddDays(-day);
            db.UsageRecords.Add(new UsageRecord
            {
                TenantId = tenant.TenantId,
                RecordDate = DateOnly.FromDateTime(when),
                ActiveUsers = stressed
                    ? (day <= 30 ? 1 : 7)
                    : (day <= 30 ? 10 : 9),
                ActiveEntities = 1,
                ActiveModules = stressed
                    ? (day <= 30 ? 1 : 3)
                    : 4,
                ReturnsSubmitted = stressed
                    ? (day <= 30 ? 0 : 2)
                    : (day <= 30 ? 3 : 3),
                StorageUsedMb = 120m,
                ApiCallCount = stressed ? 3 : 30
            });
        }

        await db.SaveChangesAsync();

        var pastSubmissions = new List<FC.Engine.Domain.Entities.Submission>();
        for (var index = 0; index < historicalPeriods.Count; index++)
        {
            var period = historicalPeriods[index];
            var late = stressed ? index >= 1 : false;
            var submission = new FC.Engine.Domain.Entities.Submission
            {
                TenantId = tenant.TenantId,
                InstitutionId = institution.Id,
                ReturnPeriodId = period.Id,
                ReturnCode = module.ModuleCode,
                TemplateVersionId = templateVersion.Id,
                Status = SubmissionStatus.Accepted,
                SubmittedAt = period.DeadlineDate.AddDays(late ? 2 : -1),
                CreatedAt = period.ReportingDate.AddDays(5)
            };
            pastSubmissions.Add(submission);
            db.Submissions.Add(submission);
        }

        db.Submissions.Add(new FC.Engine.Domain.Entities.Submission
        {
            TenantId = tenant.TenantId,
            InstitutionId = institution.Id,
            ReturnPeriodId = upcomingPeriod.Id,
            ReturnCode = module.ModuleCode,
            TemplateVersionId = templateVersion.Id,
            Status = stressed ? SubmissionStatus.Draft : SubmissionStatus.PendingApproval,
            SubmittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        });

        await db.SaveChangesAsync();

        for (var index = 0; index < historicalPeriods.Count; index++)
        {
            var period = historicalPeriods[index];
            var submission = pastSubmissions[index];
            var late = stressed ? index >= 1 : false;

            db.FilingSlaRecords.Add(new FilingSlaRecord
            {
                TenantId = tenant.TenantId,
                ModuleId = module.Id,
                PeriodId = period.Id,
                SubmissionId = submission.Id,
                PeriodEndDate = period.ReportingDate,
                DeadlineDate = period.DeadlineDate,
                SubmittedDate = submission.SubmittedAt,
                DaysToDeadline = late ? -2 : 1,
                OnTime = !late
            });
        }

        await db.SaveChangesAsync();

        var recentSubmissionId = pastSubmissions[^1].Id;
        db.AnomalyReports.Add(new AnomalyReport
        {
            TenantId = tenant.TenantId,
            InstitutionId = institution.Id,
            InstitutionName = institution.InstitutionName,
            SubmissionId = recentSubmissionId,
            ModuleCode = module.ModuleCode,
            RegulatorCode = "CBN",
            PeriodCode = $"{historicalPeriods[^1].Year}-{historicalPeriods[^1].Month:00}",
            ModelVersionId = anomalyModelVersion.Id,
            OverallQualityScore = stressed ? 42m : 94m,
            TotalFieldsAnalysed = 10,
            TotalFindings = stressed ? 6 : 1,
            AlertCount = stressed ? 3 : 0,
            WarningCount = stressed ? 2 : 1,
            InfoCount = stressed ? 1 : 0,
            TrafficLight = stressed ? "RED" : "GREEN",
            NarrativeSummary = "Test anomaly report",
            AnalysedAt = DateTime.UtcNow.AddDays(-2),
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        });

        for (var day = 60; day >= 1; day -= stressed ? 10 : 5)
        {
            db.LoginAttempts.Add(new LoginAttempt
            {
                TenantId = tenant.TenantId,
                Username = "tester",
                Succeeded = true,
                AttemptedAt = DateTime.UtcNow.AddDays(-day),
                IpAddress = "127.0.0.1"
            });
        }

        if (!stressed)
        {
            for (var day = 6; day >= 1; day--)
            {
                db.LoginAttempts.Add(new LoginAttempt
                {
                    TenantId = tenant.TenantId,
                    Username = "tester",
                    Succeeded = true,
                    AttemptedAt = DateTime.UtcNow.AddDays(-day),
                    IpAddress = "127.0.0.1"
                });
            }
        }
        else
        {
            db.LoginAttempts.Add(new LoginAttempt
            {
                TenantId = tenant.TenantId,
                Username = "tester",
                Succeeded = true,
                AttemptedAt = DateTime.UtcNow.AddDays(-5),
                IpAddress = "127.0.0.1"
            });
        }

        await db.SaveChangesAsync();

        await using var conn = new SqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await SeedPrudentialSignalsAsync(conn, institution.Id, stressed);

        return new SeededScenario(tenant.TenantId, institution.Id, institution.InstitutionName);
    }

    private static async Task SeedPrudentialSignalsAsync(SqlConnection conn, int institutionId, bool stressed)
    {
        var periods = stressed
            ? new[]
            {
                ("2024-Q4", new DateTime(2024, 12, 31), 18.5m, 4.8m, 128m, 91m),
                ("2025-Q1", new DateTime(2025, 3, 31), 17.2m, 5.6m, 121m, 87m),
                ("2025-Q2", new DateTime(2025, 6, 30), 16.1m, 6.4m, 116m, 83m),
                ("2025-Q3", new DateTime(2025, 9, 30), 15.4m, 7.5m, 111m, 79m),
                ("2025-Q4", new DateTime(2025, 12, 31), 14.8m, 8.7m, 106m, 74m),
                ("2026-Q1", new DateTime(2026, 3, 31), 14.1m, 9.6m, 101m, 70m)
            }
            : new[]
            {
                ("2024-Q4", new DateTime(2024, 12, 31), 21.0m, 3.2m, 138m, 102m),
                ("2025-Q1", new DateTime(2025, 3, 31), 21.3m, 3.1m, 139m, 103m),
                ("2025-Q2", new DateTime(2025, 6, 30), 21.5m, 3.0m, 140m, 104m),
                ("2025-Q3", new DateTime(2025, 9, 30), 21.8m, 2.9m, 141m, 105m),
                ("2025-Q4", new DateTime(2025, 12, 31), 22.1m, 2.8m, 142m, 106m),
                ("2026-Q1", new DateTime(2026, 3, 31), 22.3m, 2.7m, 143m, 107m)
            };

        foreach (var row in periods)
        {
            await using var command = conn.CreateCommand();
            command.CommandText =
                """
                INSERT INTO meta.prudential_metrics
                (InstitutionId, RegulatorCode, InstitutionType, AsOfDate, PeriodCode, CAR, NPLRatio, LCR, ProvisioningCoverage)
                VALUES (@InstitutionId, 'CBN', 'DMB', @AsOfDate, @PeriodCode, @CAR, @NPLRatio, @LCR, @ProvisioningCoverage);
                """;
            command.Parameters.AddWithValue("@InstitutionId", institutionId);
            command.Parameters.AddWithValue("@AsOfDate", row.Item2);
            command.Parameters.AddWithValue("@PeriodCode", row.Item1);
            command.Parameters.AddWithValue("@CAR", row.Item3);
            command.Parameters.AddWithValue("@NPLRatio", row.Item4);
            command.Parameters.AddWithValue("@LCR", row.Item5);
            command.Parameters.AddWithValue("@ProvisioningCoverage", row.Item6);
            await command.ExecuteNonQueryAsync();
        }

        if (stressed)
        {
            foreach (var severity in new[] { "CRITICAL", "CRITICAL", "HIGH" })
            {
                await using var command = conn.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO meta.ewi_triggers
                    (EWICode, InstitutionId, RegulatorCode, PeriodCode, Severity, TriggerValue, ThresholdValue, ComputationRunId, IsActive, IsSystemic)
                    VALUES ('CAPITAL_STRESS', @InstitutionId, 'CBN', '2026-Q1', @Severity, 1.0, 1.0, @RunId, 1, 0);
                    """;
                command.Parameters.AddWithValue("@InstitutionId", institutionId);
                command.Parameters.AddWithValue("@Severity", severity);
                command.Parameters.AddWithValue("@RunId", Guid.NewGuid());
                await command.ExecuteNonQueryAsync();
            }
        }

        await using (var camels = conn.CreateCommand())
        {
            camels.CommandText =
                """
                INSERT INTO meta.camels_ratings
                (InstitutionId, RegulatorCode, PeriodCode, AsOfDate, CapitalScore, AssetQualityScore, ManagementScore, EarningsScore, LiquidityScore, SensitivityScore, CompositeScore, RiskBand, ComputationRunId)
                VALUES (@InstitutionId, 'CBN', '2026-Q1', @AsOfDate, @CapitalScore, @AssetQualityScore, @ManagementScore, @EarningsScore, @LiquidityScore, @SensitivityScore, @CompositeScore, @RiskBand, @RunId);
                """;
            camels.Parameters.AddWithValue("@InstitutionId", institutionId);
            camels.Parameters.AddWithValue("@AsOfDate", new DateTime(2026, 3, 31));
            camels.Parameters.AddWithValue("@CapitalScore", stressed ? (byte)4 : (byte)2);
            camels.Parameters.AddWithValue("@AssetQualityScore", stressed ? (byte)4 : (byte)2);
            camels.Parameters.AddWithValue("@ManagementScore", stressed ? (byte)4 : (byte)2);
            camels.Parameters.AddWithValue("@EarningsScore", stressed ? (byte)4 : (byte)2);
            camels.Parameters.AddWithValue("@LiquidityScore", stressed ? (byte)4 : (byte)2);
            camels.Parameters.AddWithValue("@SensitivityScore", stressed ? (byte)4 : (byte)2);
            camels.Parameters.AddWithValue("@CompositeScore", stressed ? 4.20m : 2.10m);
            camels.Parameters.AddWithValue("@RiskBand", stressed ? "RED" : "GREEN");
            camels.Parameters.AddWithValue("@RunId", Guid.NewGuid());
            await camels.ExecuteNonQueryAsync();
        }
    }

    private static async Task EnsureJurisdictionAsync(MetadataDbContext db)
    {
        if (await db.Jurisdictions.AnyAsync(x => x.Id == 1))
        {
            return;
        }

        db.Jurisdictions.Add(new Jurisdiction
        {
            Id = 1,
            CountryCode = "NG",
            CountryName = "Nigeria",
            Currency = "NGN",
            Timezone = "Africa/Lagos",
            RegulatoryBodies = """["CBN","NDIC","SEC","NAICOM"]""",
            DataResidencyRegion = "SouthAfricaNorth",
            IsActive = true
        });

        await db.SaveChangesAsync();
    }

    private static async Task<LicenceType> GetOrCreateLicenceTypeAsync(MetadataDbContext db, string code, string name, string regulator)
    {
        var existing = await db.LicenceTypes.FirstOrDefaultAsync(x => x.Code == code && x.Regulator == regulator);
        if (existing is not null)
        {
            return existing;
        }

        var licence = new LicenceType
        {
            Code = code,
            Name = name,
            Regulator = regulator,
            Description = name,
            IsActive = true,
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow.AddYears(-1)
        };

        db.LicenceTypes.Add(licence);
        await db.SaveChangesAsync();
        return licence;
    }

    private static async Task<SubscriptionPlan> GetOrCreatePlanAsync(MetadataDbContext db)
    {
        var existing = await db.SubscriptionPlans.FirstOrDefaultAsync(x => x.PlanCode == "FS-INT");
        if (existing is not null)
        {
            return existing;
        }

        var plan = new SubscriptionPlan
        {
            PlanCode = "FS-INT",
            PlanName = "ForeSight Integration",
            Description = "Integration-test plan",
            Tier = 3,
            MaxModules = 20,
            MaxUsersPerEntity = 50,
            MaxEntities = 5,
            MaxApiCallsPerMonth = 10000,
            MaxStorageMb = 1024,
            BasePriceMonthly = 50000m,
            BasePriceAnnual = 500000m,
            TrialDays = 14,
            Features = """["foresight","complianceiq"]""",
            IsActive = true,
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow.AddYears(-1)
        };

        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync();
        return plan;
    }

    private sealed record SeededScenario(Guid TenantId, int InstitutionId, string InstitutionName);

    private sealed class TestDbConnectionFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;

        public TestDbConnectionFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<System.Data.IDbConnection> CreateConnectionAsync(Guid? tenantId, CancellationToken ct = default)
        {
            var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            _ = tenantId;
            return connection;
        }
    }
}
