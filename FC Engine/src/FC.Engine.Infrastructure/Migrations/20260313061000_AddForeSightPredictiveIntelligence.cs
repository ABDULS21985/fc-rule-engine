using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FC.Engine.Infrastructure.Metadata.Migrations;

[DbContext(typeof(MetadataDbContext))]
[Migration("20260313061000_AddForeSightPredictiveIntelligence")]
public partial class AddForeSightPredictiveIntelligence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'[meta].[audit_log]', N'U') IS NOT NULL
               AND COL_LENGTH(N'meta.audit_log', N'Action') IS NOT NULL
            BEGIN
                ALTER TABLE [meta].[audit_log] ALTER COLUMN [Action] NVARCHAR(64) NOT NULL;
            END;
            """);

        if (ShouldSkipLegacyForeSightBootstrap())
        {
            return;
        }

        migrationBuilder.Sql(
            """
            CREATE TABLE [meta].[foresight_config]
            (
                [Id]            INT IDENTITY(1,1) NOT NULL,
                [ConfigKey]     VARCHAR(100) NOT NULL,
                [ConfigValue]   NVARCHAR(MAX) NOT NULL,
                [Description]   NVARCHAR(1000) NOT NULL,
                [EffectiveFrom] DATETIME2(3) NOT NULL CONSTRAINT [DF_foresight_config_EffectiveFrom] DEFAULT SYSUTCDATETIME(),
                [EffectiveTo]   DATETIME2(3) NULL,
                [CreatedBy]     VARCHAR(100) NOT NULL,
                [CreatedAt]     DATETIME2(3) NOT NULL CONSTRAINT [DF_foresight_config_CreatedAt] DEFAULT SYSUTCDATETIME(),
                CONSTRAINT [PK_foresight_config] PRIMARY KEY ([Id])
            );

            CREATE UNIQUE INDEX [UX_foresight_config_key_effective]
                ON [meta].[foresight_config] ([ConfigKey], [EffectiveFrom]);

            CREATE INDEX [IX_foresight_config_active]
                ON [meta].[foresight_config] ([ConfigKey])
                WHERE [EffectiveTo] IS NULL;

            CREATE TABLE [meta].[foresight_model_versions]
            (
                [Id]                 INT IDENTITY(1,1) NOT NULL,
                [ModelCode]          VARCHAR(30) NOT NULL,
                [VersionNumber]      INT NOT NULL,
                [Status]             VARCHAR(20) NOT NULL CONSTRAINT [DF_foresight_model_versions_Status] DEFAULT 'ACTIVE',
                [TrainedAt]          DATETIME2(3) NULL,
                [ObservationsCount]  INT NOT NULL CONSTRAINT [DF_foresight_model_versions_ObservationsCount] DEFAULT 0,
                [AccuracyMetric]     DECIMAL(5,4) NULL,
                [AccuracyMetricName] VARCHAR(50) NOT NULL CONSTRAINT [DF_foresight_model_versions_AccuracyMetricName] DEFAULT '',
                [Notes]              NVARCHAR(2000) NULL,
                [CreatedAt]          DATETIME2(3) NOT NULL CONSTRAINT [DF_foresight_model_versions_CreatedAt] DEFAULT SYSUTCDATETIME(),
                CONSTRAINT [PK_foresight_model_versions] PRIMARY KEY ([Id]),
                CONSTRAINT [CK_foresight_model_versions_status] CHECK ([Status] IN ('ACTIVE','RETIRED','TRAINING','FAILED'))
            );

            CREATE UNIQUE INDEX [UX_foresight_model_version]
                ON [meta].[foresight_model_versions] ([ModelCode], [VersionNumber]);

            CREATE TABLE [meta].[foresight_feature_definitions]
            (
                [Id]            INT IDENTITY(1,1) NOT NULL,
                [ModelCode]     VARCHAR(30) NOT NULL,
                [FeatureName]   VARCHAR(100) NOT NULL,
                [FeatureLabel]  NVARCHAR(200) NOT NULL,
                [Description]   NVARCHAR(1000) NOT NULL,
                [DataSource]    VARCHAR(100) NOT NULL,
                [DefaultWeight] DECIMAL(8,4) NOT NULL,
                [IsActive]      BIT NOT NULL CONSTRAINT [DF_foresight_feature_definitions_IsActive] DEFAULT 1,
                CONSTRAINT [PK_foresight_feature_definitions] PRIMARY KEY ([Id])
            );

            CREATE UNIQUE INDEX [UX_foresight_feature_definition]
                ON [meta].[foresight_feature_definitions] ([ModelCode], [FeatureName]);

            CREATE TABLE [meta].[foresight_regulatory_thresholds]
            (
                [Id]                 INT IDENTITY(1,1) NOT NULL,
                [Regulator]          VARCHAR(10) NOT NULL,
                [LicenceCategory]    VARCHAR(50) NOT NULL,
                [MetricCode]         VARCHAR(50) NOT NULL,
                [MetricLabel]        NVARCHAR(200) NOT NULL,
                [ThresholdValue]     DECIMAL(10,4) NOT NULL,
                [ThresholdType]      VARCHAR(10) NOT NULL,
                [SeverityIfBreached] VARCHAR(10) NOT NULL,
                [Description]        NVARCHAR(1000) NOT NULL,
                [CircularReference]  VARCHAR(100) NULL,
                [IsActive]           BIT NOT NULL CONSTRAINT [DF_foresight_regulatory_thresholds_IsActive] DEFAULT 1,
                CONSTRAINT [PK_foresight_regulatory_thresholds] PRIMARY KEY ([Id]),
                CONSTRAINT [CK_foresight_thresholds_type] CHECK ([ThresholdType] IN ('MINIMUM','MAXIMUM')),
                CONSTRAINT [CK_foresight_thresholds_severity] CHECK ([SeverityIfBreached] IN ('WARNING','CRITICAL'))
            );

            CREATE INDEX [IX_foresight_threshold_lookup]
                ON [meta].[foresight_regulatory_thresholds] ([Regulator], [LicenceCategory], [MetricCode], [ThresholdValue]);

            CREATE TABLE [meta].[foresight_predictions]
            (
                [Id]                    BIGINT IDENTITY(1,1) NOT NULL,
                [TenantId]              UNIQUEIDENTIFIER NOT NULL,
                [ModelCode]             VARCHAR(30) NOT NULL,
                [ModelVersionId]        INT NOT NULL,
                [PredictionDate]        DATE NOT NULL CONSTRAINT [DF_foresight_predictions_PredictionDate] DEFAULT CONVERT(date, SYSUTCDATETIME()),
                [HorizonLabel]          VARCHAR(30) NOT NULL,
                [HorizonDate]           DATE NULL,
                [PredictedValue]        DECIMAL(18,6) NOT NULL,
                [ConfidenceLower]       DECIMAL(18,6) NULL,
                [ConfidenceUpper]       DECIMAL(18,6) NULL,
                [ConfidenceScore]       DECIMAL(5,4) NOT NULL,
                [RiskBand]              VARCHAR(10) NOT NULL,
                [TargetModuleCode]      VARCHAR(40) NOT NULL CONSTRAINT [DF_foresight_predictions_TargetModuleCode] DEFAULT '',
                [TargetPeriodCode]      VARCHAR(40) NOT NULL CONSTRAINT [DF_foresight_predictions_TargetPeriodCode] DEFAULT '',
                [TargetMetric]          VARCHAR(60) NOT NULL CONSTRAINT [DF_foresight_predictions_TargetMetric] DEFAULT '',
                [TargetLabel]           NVARCHAR(200) NOT NULL CONSTRAINT [DF_foresight_predictions_TargetLabel] DEFAULT '',
                [Explanation]           NVARCHAR(2000) NOT NULL,
                [RootCauseNarrative]    NVARCHAR(2000) NOT NULL,
                [Recommendation]        NVARCHAR(2000) NOT NULL CONSTRAINT [DF_foresight_predictions_Recommendation] DEFAULT '',
                [RootCausePillar]       VARCHAR(100) NOT NULL CONSTRAINT [DF_foresight_predictions_RootCausePillar] DEFAULT '',
                [FeatureImportanceJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_foresight_predictions_FeatureImportanceJson] DEFAULT '[]',
                [IsSuppressed]          BIT NOT NULL CONSTRAINT [DF_foresight_predictions_IsSuppressed] DEFAULT 0,
                [SuppressionReason]     NVARCHAR(500) NULL,
                [CreatedAt]             DATETIME2(3) NOT NULL CONSTRAINT [DF_foresight_predictions_CreatedAt] DEFAULT SYSUTCDATETIME(),
                [UpdatedAt]             DATETIME2(3) NOT NULL CONSTRAINT [DF_foresight_predictions_UpdatedAt] DEFAULT SYSUTCDATETIME(),
                CONSTRAINT [PK_foresight_predictions] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_foresight_predictions_model_version] FOREIGN KEY ([ModelVersionId]) REFERENCES [meta].[foresight_model_versions] ([Id]),
                CONSTRAINT [CK_foresight_predictions_risk] CHECK ([RiskBand] IN ('CRITICAL','HIGH','MEDIUM','LOW','NONE'))
            );

            CREATE INDEX [IX_foresight_predictions_tenant]
                ON [meta].[foresight_predictions] ([TenantId], [ModelCode], [PredictionDate]);

            CREATE INDEX [IX_foresight_predictions_risk]
                ON [meta].[foresight_predictions] ([ModelCode], [RiskBand], [IsSuppressed]);

            CREATE UNIQUE INDEX [UX_foresight_predictions_scope]
                ON [meta].[foresight_predictions] ([TenantId], [ModelCode], [PredictionDate], [HorizonLabel], [TargetModuleCode], [TargetPeriodCode], [TargetMetric]);

            CREATE TABLE [meta].[foresight_prediction_features]
            (
                [Id]                BIGINT IDENTITY(1,1) NOT NULL,
                [PredictionId]      BIGINT NOT NULL,
                [FeatureName]       VARCHAR(100) NOT NULL,
                [FeatureLabel]      NVARCHAR(200) NOT NULL,
                [RawValue]          DECIMAL(18,6) NOT NULL,
                [NormalizedValue]   DECIMAL(18,6) NOT NULL,
                [Weight]            DECIMAL(8,4) NOT NULL,
                [ContributionScore] DECIMAL(18,6) NOT NULL,
                [ImpactDirection]   VARCHAR(40) NOT NULL,
                [CreatedAt]         DATETIME2(3) NOT NULL CONSTRAINT [DF_foresight_prediction_features_CreatedAt] DEFAULT SYSUTCDATETIME(),
                CONSTRAINT [PK_foresight_prediction_features] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_foresight_prediction_features_prediction] FOREIGN KEY ([PredictionId]) REFERENCES [meta].[foresight_predictions] ([Id]) ON DELETE CASCADE
            );

            CREATE INDEX [IX_foresight_prediction_features_prediction]
                ON [meta].[foresight_prediction_features] ([PredictionId], [FeatureName]);

            CREATE TABLE [meta].[foresight_alerts]
            (
                [Id]             INT IDENTITY(1,1) NOT NULL,
                [PredictionId]   BIGINT NOT NULL,
                [TenantId]       UNIQUEIDENTIFIER NOT NULL,
                [AlertType]      VARCHAR(30) NOT NULL,
                [Severity]       VARCHAR(10) NOT NULL,
                [Title]          NVARCHAR(300) NOT NULL,
                [Body]           NVARCHAR(2000) NOT NULL,
                [Recommendation] NVARCHAR(2000) NOT NULL CONSTRAINT [DF_foresight_alerts_Recommendation] DEFAULT '',
                [RecipientRole]  VARCHAR(100) NOT NULL,
                [IsRead]         BIT NOT NULL CONSTRAINT [DF_foresight_alerts_IsRead] DEFAULT 0,
                [ReadBy]         VARCHAR(100) NULL,
                [ReadAt]         DATETIME2(3) NULL,
                [IsDismissed]    BIT NOT NULL CONSTRAINT [DF_foresight_alerts_IsDismissed] DEFAULT 0,
                [DismissedBy]    VARCHAR(100) NULL,
                [DismissedAt]    DATETIME2(3) NULL,
                [DispatchedAt]   DATETIME2(3) NOT NULL CONSTRAINT [DF_foresight_alerts_DispatchedAt] DEFAULT SYSUTCDATETIME(),
                [CreatedAt]      DATETIME2(3) NOT NULL CONSTRAINT [DF_foresight_alerts_CreatedAt] DEFAULT SYSUTCDATETIME(),
                CONSTRAINT [PK_foresight_alerts] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_foresight_alerts_prediction] FOREIGN KEY ([PredictionId]) REFERENCES [meta].[foresight_predictions] ([Id]) ON DELETE CASCADE,
                CONSTRAINT [CK_foresight_alerts_type] CHECK ([AlertType] IN ('FILING_RISK','CAPITAL_WARNING','CAPITAL_CRITICAL','CHS_DECLINE','CHURN_RISK','REG_ACTION_PRIORITY')),
                CONSTRAINT [CK_foresight_alerts_severity] CHECK ([Severity] IN ('INFO','WARNING','CRITICAL'))
            );

            CREATE INDEX [IX_foresight_alerts_lookup]
                ON [meta].[foresight_alerts] ([TenantId], [IsRead], [IsDismissed], [DispatchedAt]);

            CREATE INDEX [IX_foresight_alerts_type]
                ON [meta].[foresight_alerts] ([TenantId], [AlertType], [DispatchedAt]);

            INSERT INTO [meta].[foresight_config] ([ConfigKey], [ConfigValue], [Description], [CreatedBy]) VALUES
            ('filing.risk_high_threshold', '0.70', 'Probability threshold for a high filing-risk alert.', 'SYSTEM'),
            ('filing.risk_medium_threshold', '0.40', 'Probability threshold for a medium filing-risk classification.', 'SYSTEM'),
            ('filing.alert_horizon_days', '14', 'First warning horizon, in days before deadline, for high filing-risk returns.', 'SYSTEM'),
            ('filing.escalation_horizon_days', '7', 'Escalation horizon, in days before deadline, when risk remains elevated.', 'SYSTEM'),
            ('filing.critical_horizon_days', '3', 'Critical alert horizon, in days before deadline, for unresolved filing risk.', 'SYSTEM'),
            ('filing.min_history_periods', '4', 'Minimum filing history periods required before unsuppressed filing-risk predictions are shown.', 'SYSTEM'),
            ('capital.warning_buffer', '2.0', 'Buffer in percentage points above or below a threshold that maps to a warning.', 'SYSTEM'),
            ('capital.forecast_quarters', '2', 'Number of quarters to project ahead for prudential metrics.', 'SYSTEM'),
            ('capital.min_data_points', '6', 'Minimum quarterly observations required before capital forecasts become visible.', 'SYSTEM'),
            ('chs.decline_alert_threshold', '5.0', 'Projected CHS decline in points that triggers an advisory alert.', 'SYSTEM'),
            ('chs.forecast_periods', '3', 'Number of future scoring periods to project for CHS trend forecasting.', 'SYSTEM'),
            ('churn.high_threshold', '0.70', 'Probability threshold for classifying a tenant as high churn risk.', 'SYSTEM'),
            ('churn.medium_threshold', '0.40', 'Probability threshold for classifying a tenant as medium churn risk.', 'SYSTEM'),
            ('churn.lookback_days', '90', 'Lookback window in days for platform-engagement and churn features.', 'SYSTEM'),
            ('regaction.high_threshold', '0.60', 'Probability threshold for regulator-priority classification.', 'SYSTEM'),
            ('regaction.forecast_months', '6', 'Advisory horizon in months for supervisory-intervention probability.', 'SYSTEM'),
            ('prediction.min_confidence', '0.55', 'Predictions below this confidence score are persisted as suppressed.', 'SYSTEM'),
            ('prediction.suppress_low_data', 'true', 'Suppress predictions when the historical dataset is below the configured minimum.', 'SYSTEM'),
            ('alert.cooldown_hours', '24', 'Minimum number of hours between equivalent advisory alerts for the same prediction target.', 'SYSTEM'),
            ('prediction.stale_after_hours', '24', 'Hours after which a tenant dashboard run is considered stale and will be recomputed on demand.', 'SYSTEM');

            INSERT INTO [meta].[foresight_model_versions] ([ModelCode], [VersionNumber], [Status], [TrainedAt], [ObservationsCount], [AccuracyMetric], [AccuracyMetricName], [Notes]) VALUES
            ('FILING_RISK', 1, 'ACTIVE', '2026-03-13T00:00:00.000', 0, 0.8125, 'ROC_AUC', 'Weighted logistic seed tuned for Nigerian filing behaviour.'),
            ('CAPITAL_BREACH', 1, 'ACTIVE', '2026-03-13T00:00:00.000', 0, 0.7680, 'MAPE', 'Holt linear smoothing for CAR, NPL and liquidity trajectories.'),
            ('CHS_TREND', 1, 'ACTIVE', '2026-03-13T00:00:00.000', 0, 0.7425, 'RMSE', 'Weighted moving-average projection for CHS deterioration and recovery.'),
            ('CHURN_RISK', 1, 'ACTIVE', '2026-03-13T00:00:00.000', 0, 0.7934, 'ROC_AUC', 'Tenant-engagement classifier for subscription-retention risk.'),
            ('REG_ACTION', 1, 'ACTIVE', '2026-03-13T00:00:00.000', 0, 0.7811, 'ROC_AUC', 'Composite supervisory-priority classifier combining EWI, CHS, anomaly and timeliness pressure.');

            INSERT INTO [meta].[foresight_feature_definitions] ([ModelCode], [FeatureName], [FeatureLabel], [Description], [DataSource], [DefaultWeight], [IsActive]) VALUES
            ('FILING_RISK', 'days_to_deadline', 'Days to Deadline', 'Normalized countdown pressure before the regulatory deadline.', 'return_periods', 0.2200, 1),
            ('FILING_RISK', 'historical_late_rate', 'Historical Late Rate', 'Late-filing share for the same module across recent filing periods.', 'filing_sla_records', 0.1800, 1),
            ('FILING_RISK', 'draft_completeness_gap', 'Draft Completeness Gap', 'Share of expected template fields that are still blank in the current draft.', 'return_drafts', 0.1600, 1),
            ('FILING_RISK', 'preparation_stage', 'Preparation Stage', 'Preparation stage from draft through submitted/accepted.', 'return_submissions', 0.1400, 1),
            ('FILING_RISK', 'login_activity_gap', 'Login Activity Gap', 'Deficit in recent portal-login intensity for the tenant.', 'login_attempts', 0.0800, 1),
            ('FILING_RISK', 'recent_late_count', 'Recent Late Count', 'Count of recent late or overdue filings across the last four quarters.', 'filing_sla_records', 0.0800, 1),
            ('FILING_RISK', 'anomaly_pressure', 'Anomaly Pressure', 'Residual anomaly pressure from the latest accepted return for the module.', 'anomaly_reports', 0.0700, 1),
            ('FILING_RISK', 'concurrent_filings', 'Concurrent Filing Load', 'Number of open filing obligations clustered around the same deadline window.', 'return_periods', 0.0700, 1),

            ('CAPITAL_BREACH', 'threshold_buffer', 'Threshold Buffer', 'Distance between the current metric and its closest regulatory threshold.', 'prudential_metrics', 0.3000, 1),
            ('CAPITAL_BREACH', 'trend_slope', 'Trend Slope', 'Linear direction of travel across recent prudential periods.', 'prudential_metrics', 0.2400, 1),
            ('CAPITAL_BREACH', 'volatility', 'Volatility', 'Observed variability in the metric across recent periods.', 'prudential_metrics', 0.1600, 1),
            ('CAPITAL_BREACH', 'credit_stress_proxy', 'Credit Stress Proxy', 'Credit-quality or liquidity co-metric stress surrounding the forecasted threshold.', 'prudential_metrics', 0.1600, 1),
            ('CAPITAL_BREACH', 'ewi_pressure', 'EWI Pressure', 'Active prudential early warnings already signalling weakness.', 'ewi_triggers', 0.1400, 1),

            ('CHS_TREND', 'overall_trend_slope', 'Overall Trend Slope', 'Recent CHS trajectory over the latest scoring periods.', 'chs_score_snapshots', 0.2500, 1),
            ('CHS_TREND', 'filing_pillar', 'Filing Timeliness Pillar', 'Current filing timeliness pillar score.', 'chs_score_snapshots', 0.1800, 1),
            ('CHS_TREND', 'data_quality_pillar', 'Data Quality Pillar', 'Current data-quality pillar score.', 'chs_score_snapshots', 0.1800, 1),
            ('CHS_TREND', 'capital_pillar', 'Regulatory Capital Pillar', 'Current regulatory-capital pillar score.', 'chs_score_snapshots', 0.1600, 1),
            ('CHS_TREND', 'engagement_pillar', 'Engagement Pillar', 'Current engagement pillar score.', 'chs_score_snapshots', 0.1300, 1),
            ('CHS_TREND', 'consecutive_declines', 'Consecutive Declines', 'Count of consecutive score periods with decline.', 'chs_score_snapshots', 0.1000, 1),

            ('CHURN_RISK', 'login_trend', 'Login Trend', 'Trend in successful portal-login activity.', 'login_attempts', 0.2300, 1),
            ('CHURN_RISK', 'usage_drop', 'Usage Drop', 'Drop in active users, entities, modules or return submissions across recent months.', 'usage_records', 0.2000, 1),
            ('CHURN_RISK', 'complianceiq_gap', 'ComplianceIQ Activity Gap', 'Deficit in recent natural-language compliance-query usage.', 'complianceiq_turns', 0.1400, 1),
            ('CHURN_RISK', 'support_pressure', 'Support Pressure', 'Support-ticket escalation and unresolved support burden.', 'partner_support_tickets', 0.1200, 1),
            ('CHURN_RISK', 'payment_delay', 'Payment Delay', 'Delayed invoice settlement and overdue billing posture.', 'invoices,payments', 0.1700, 1),
            ('CHURN_RISK', 'filing_timeliness_gap', 'Filing Timeliness Gap', 'Recent decline in on-time filing performance, used as an engagement signal.', 'filing_sla_records', 0.1400, 1),

            ('REG_ACTION', 'critical_ewi_count', 'Critical EWI Count', 'Number of active critical prudential or conduct EWIs.', 'ewi_triggers', 0.2600, 1),
            ('REG_ACTION', 'chs_deficit', 'CHS Deficit', 'Distance between current compliance health and full-score posture.', 'chs_score_snapshots', 0.1800, 1),
            ('REG_ACTION', 'anomaly_pressure', 'Anomaly Pressure', 'Severity and density of unresolved anomaly findings.', 'anomaly_reports', 0.1800, 1),
            ('REG_ACTION', 'filing_delinquency', 'Filing Delinquency', 'Late, overdue, or deteriorating filing posture across recent obligations.', 'filing_sla_records,return_periods', 0.1400, 1),
            ('REG_ACTION', 'capital_proximity', 'Capital Proximity', 'How close prudential capital and liquidity metrics are to binding thresholds.', 'prudential_metrics', 0.1600, 1),
            ('REG_ACTION', 'camels_pressure', 'CAMELS Pressure', 'Composite CAMELS severity from the latest supervisory scoring.', 'camels_ratings', 0.0800, 1);

            INSERT INTO [meta].[foresight_regulatory_thresholds] ([Regulator], [LicenceCategory], [MetricCode], [MetricLabel], [ThresholdValue], [ThresholdType], [SeverityIfBreached], [Description], [CircularReference], [IsActive]) VALUES
            ('CBN', 'DMB', 'CAR', 'Capital Adequacy Ratio', 15.0000, 'MINIMUM', 'CRITICAL', 'Deposit Money Banks should maintain at least 15% CAR for international and systemically important prudential posture.', 'BSD/1/2004', 1),
            ('CBN', 'MFB', 'CAR', 'Capital Adequacy Ratio', 10.0000, 'MINIMUM', 'CRITICAL', 'Microfinance banks should maintain at least 10% CAR.', 'RPSD/DIR/GEN/12/005', 1),
            ('CBN', 'PMB', 'CAR', 'Capital Adequacy Ratio', 10.0000, 'MINIMUM', 'CRITICAL', 'Primary mortgage banks should maintain at least 10% CAR.', 'BSD/1/2004', 1),
            ('CBN', 'DMB', 'NPL', 'Non-Performing Loan Ratio', 5.0000, 'MAXIMUM', 'WARNING', 'CBN prudential guidance flags NPL ratios above 5% for closer monitoring.', 'BSD/DIR/GEN/VOL.2/06', 1),
            ('CBN', 'DMB', 'NPL', 'Non-Performing Loan Ratio', 10.0000, 'MAXIMUM', 'CRITICAL', 'NPL ratios above 10% require enhanced supervisory attention.', 'BSD/DIR/GEN/VOL.2/06', 1),
            ('CBN', 'MFB', 'NPL', 'Non-Performing Loan Ratio', 5.0000, 'MAXIMUM', 'WARNING', 'Microfinance-bank portfolios above 5% NPL should be escalated for review.', 'RPSD/DIR/GEN/12/005', 1),
            ('CBN', 'DMB', 'LCR', 'Liquidity Coverage Ratio', 100.0000, 'MINIMUM', 'CRITICAL', 'Liquidity coverage ratio should remain at or above 100%.', 'BSD/DIR/GEN/VOL.2/11', 1),
            ('CBN', 'MFB', 'LCR', 'Liquidity Coverage Ratio', 100.0000, 'MINIMUM', 'CRITICAL', 'Microfinance-bank liquidity coverage ratio should remain at or above 100%.', 'RPSD/DIR/GEN/12/005', 1),
            ('NAICOM', 'GENERAL_INSURER', 'SOLVENCY_MARGIN', 'Solvency Margin', 100.0000, 'MINIMUM', 'CRITICAL', 'General insurers should sustain a solvency margin above 100%.', 'NAICOM Act 1997', 1),
            ('NAICOM', 'LIFE_INSURER', 'SOLVENCY_MARGIN', 'Solvency Margin', 100.0000, 'MINIMUM', 'CRITICAL', 'Life insurers should sustain a solvency margin above 100%.', 'NAICOM Act 1997', 1),
            ('SEC', 'BROKER_DEALER', 'CAPITAL_ADEQUACY', 'Capital Adequacy', 10.0000, 'MINIMUM', 'CRITICAL', 'Broker-dealers should maintain capital adequacy above 10%.', 'SEC Rules 2013', 1),
            ('SEC', 'BROKER_DEALER', 'SEGREGATION_RATIO', 'Client Funds Segregation Ratio', 100.0000, 'MINIMUM', 'CRITICAL', 'Client funds should remain fully segregated.', 'ISA 2007 s.148', 1);
            """);
    }

    private static bool ShouldSkipLegacyForeSightBootstrap()
    {
        // ForeSight schema ownership moved to Metadata/Migrations/20260326120000_AddForeSightSchema.
        // Keep this migration in the chain only for the audit_log column widening above.
        return true;
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'[meta].[audit_log]', N'U') IS NOT NULL
               AND COL_LENGTH(N'meta.audit_log', N'Action') IS NOT NULL
            BEGIN
                ALTER TABLE [meta].[audit_log] ALTER COLUMN [Action] NVARCHAR(20) NOT NULL;
            END;

            DROP TABLE IF EXISTS [meta].[foresight_alerts];
            DROP TABLE IF EXISTS [meta].[foresight_prediction_features];
            DROP TABLE IF EXISTS [meta].[foresight_predictions];
            DROP TABLE IF EXISTS [meta].[foresight_regulatory_thresholds];
            DROP TABLE IF EXISTS [meta].[foresight_feature_definitions];
            DROP TABLE IF EXISTS [meta].[foresight_model_versions];
            DROP TABLE IF EXISTS [meta].[foresight_config];
            """);
    }
}
