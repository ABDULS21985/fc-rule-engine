using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FC.Engine.Infrastructure.Metadata.Migrations;

[DbContext(typeof(MetadataDbContext))]
[Migration("20260326120000_AddForeSightSchema")]
public partial class AddForeSightSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "meta");

        // ── foresight_config ──────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "foresight_config",
            schema: "meta",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ConfigKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                ConfigValue = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                EffectiveFrom = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                EffectiveTo = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                CreatedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_foresight_config", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "UX_foresight_config_key_effective",
            schema: "meta",
            table: "foresight_config",
            columns: new[] { "ConfigKey", "EffectiveFrom" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_foresight_config_active",
            schema: "meta",
            table: "foresight_config",
            column: "ConfigKey",
            filter: "[EffectiveTo] IS NULL");

        // ── foresight_model_versions ──────────────────────────────────
        migrationBuilder.CreateTable(
            name: "foresight_model_versions",
            schema: "meta",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ModelCode = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                VersionNumber = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                TrainedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                ObservationsCount = table.Column<int>(type: "int", nullable: false),
                AccuracyMetric = table.Column<decimal>(type: "decimal(5,4)", nullable: true),
                AccuracyMetricName = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_foresight_model_versions", x => x.Id);
                table.CheckConstraint("CK_foresight_model_versions_status", "[Status] IN ('ACTIVE','RETIRED','TRAINING','FAILED')");
            });

        migrationBuilder.CreateIndex(
            name: "UX_foresight_model_version",
            schema: "meta",
            table: "foresight_model_versions",
            columns: new[] { "ModelCode", "VersionNumber" },
            unique: true);

        // ── foresight_feature_definitions ─────────────────────────────
        migrationBuilder.CreateTable(
            name: "foresight_feature_definitions",
            schema: "meta",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ModelCode = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                FeatureName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                FeatureLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                DataSource = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                DefaultWeight = table.Column<decimal>(type: "decimal(8,4)", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_foresight_feature_definitions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "UX_foresight_feature_definition",
            schema: "meta",
            table: "foresight_feature_definitions",
            columns: new[] { "ModelCode", "FeatureName" },
            unique: true);

        // ── foresight_regulatory_thresholds ───────────────────────────
        migrationBuilder.CreateTable(
            name: "foresight_regulatory_thresholds",
            schema: "meta",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Regulator = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                LicenceCategory = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                MetricCode = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                MetricLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                ThresholdValue = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                ThresholdType = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                SeverityIfBreached = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                CircularReference = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_foresight_regulatory_thresholds", x => x.Id);
                table.CheckConstraint("CK_foresight_thresholds_type", "[ThresholdType] IN ('MINIMUM','MAXIMUM')");
                table.CheckConstraint("CK_foresight_thresholds_severity", "[SeverityIfBreached] IN ('WARNING','CRITICAL')");
            });

        migrationBuilder.CreateIndex(
            name: "IX_foresight_threshold_lookup",
            schema: "meta",
            table: "foresight_regulatory_thresholds",
            columns: new[] { "Regulator", "LicenceCategory", "MetricCode", "ThresholdValue" });

        // ── foresight_predictions ─────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "foresight_predictions",
            schema: "meta",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ModelCode = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                ModelVersionId = table.Column<int>(type: "int", nullable: false),
                PredictionDate = table.Column<DateTime>(type: "date", nullable: false),
                HorizonLabel = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                HorizonDate = table.Column<DateTime>(type: "date", nullable: true),
                PredictedValue = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                ConfidenceLower = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                ConfidenceUpper = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                ConfidenceScore = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                RiskBand = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                TargetModuleCode = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                TargetPeriodCode = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false, defaultValue: ""),
                TargetMetric = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false, defaultValue: ""),
                TargetLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                Explanation = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                RootCauseNarrative = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                Recommendation = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                RootCausePillar = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, defaultValue: ""),
                FeatureImportanceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                IsSuppressed = table.Column<bool>(type: "bit", nullable: false),
                SuppressionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_foresight_predictions", x => x.Id);
                table.CheckConstraint("CK_foresight_predictions_risk", "[RiskBand] IN ('CRITICAL','HIGH','MEDIUM','LOW','NONE')");
                table.ForeignKey(
                    name: "FK_foresight_predictions_model_versions",
                    column: x => x.ModelVersionId,
                    principalSchema: "meta",
                    principalTable: "foresight_model_versions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_foresight_predictions_tenant",
            schema: "meta",
            table: "foresight_predictions",
            columns: new[] { "TenantId", "ModelCode", "PredictionDate" });

        migrationBuilder.CreateIndex(
            name: "IX_foresight_predictions_risk",
            schema: "meta",
            table: "foresight_predictions",
            columns: new[] { "ModelCode", "RiskBand", "IsSuppressed" });

        migrationBuilder.CreateIndex(
            name: "UX_foresight_predictions_scope",
            schema: "meta",
            table: "foresight_predictions",
            columns: new[] { "TenantId", "ModelCode", "PredictionDate", "HorizonLabel", "TargetModuleCode", "TargetPeriodCode", "TargetMetric" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_foresight_predictions_ModelVersionId",
            schema: "meta",
            table: "foresight_predictions",
            column: "ModelVersionId");

        // ── foresight_prediction_features ─────────────────────────────
        migrationBuilder.CreateTable(
            name: "foresight_prediction_features",
            schema: "meta",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PredictionId = table.Column<long>(type: "bigint", nullable: false),
                FeatureName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                FeatureLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                RawValue = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                NormalizedValue = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                Weight = table.Column<decimal>(type: "decimal(8,4)", nullable: false),
                ContributionScore = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                ImpactDirection = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_foresight_prediction_features", x => x.Id);
                table.ForeignKey(
                    name: "FK_foresight_prediction_features_predictions",
                    column: x => x.PredictionId,
                    principalSchema: "meta",
                    principalTable: "foresight_predictions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_foresight_prediction_features_prediction",
            schema: "meta",
            table: "foresight_prediction_features",
            columns: new[] { "PredictionId", "FeatureName" });

        // ── foresight_alerts ──────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "foresight_alerts",
            schema: "meta",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PredictionId = table.Column<long>(type: "bigint", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AlertType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                Severity = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                Recommendation = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                RecipientRole = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                IsRead = table.Column<bool>(type: "bit", nullable: false),
                ReadBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                ReadAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                IsDismissed = table.Column<bool>(type: "bit", nullable: false),
                DismissedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                DismissedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                DispatchedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_foresight_alerts", x => x.Id);
                table.CheckConstraint("CK_foresight_alerts_type", "[AlertType] IN ('FILING_RISK','CAPITAL_WARNING','CAPITAL_CRITICAL','CHS_DECLINE','CHURN_RISK','REG_ACTION_PRIORITY')");
                table.CheckConstraint("CK_foresight_alerts_severity", "[Severity] IN ('INFO','WARNING','CRITICAL')");
                table.ForeignKey(
                    name: "FK_foresight_alerts_predictions",
                    column: x => x.PredictionId,
                    principalSchema: "meta",
                    principalTable: "foresight_predictions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_foresight_alerts_lookup",
            schema: "meta",
            table: "foresight_alerts",
            columns: new[] { "TenantId", "IsRead", "IsDismissed", "DispatchedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_foresight_alerts_type",
            schema: "meta",
            table: "foresight_alerts",
            columns: new[] { "TenantId", "AlertType", "DispatchedAt" });

        // ── Seed data (raw SQL to avoid EF model-mapping requirement) ──
        migrationBuilder.Sql(@"
SET IDENTITY_INSERT [meta].[foresight_config] ON;
INSERT INTO [meta].[foresight_config] ([Id],[ConfigKey],[ConfigValue],[Description],[EffectiveFrom],[EffectiveTo],[CreatedBy],[CreatedAt]) VALUES
(1,'filing.risk_high_threshold','0.70','Probability threshold for a high filing-risk alert.','2026-03-13',NULL,'SYSTEM','2026-03-13'),
(2,'filing.risk_medium_threshold','0.40','Probability threshold for a medium filing-risk classification.','2026-03-13',NULL,'SYSTEM','2026-03-13'),
(3,'filing.alert_horizon_days','14','First warning horizon, in days before deadline, for high filing-risk returns.','2026-03-13',NULL,'SYSTEM','2026-03-13'),
(4,'filing.escalation_horizon_days','7','Escalation horizon, in days before deadline, when risk remains elevated.','2026-03-13',NULL,'SYSTEM','2026-03-13'),
(5,'filing.critical_horizon_days','3','Critical alert horizon, in days before deadline, for unresolved filing risk.','2026-03-13',NULL,'SYSTEM','2026-03-13'),
(6,'filing.min_history_periods','4','Minimum filing history periods required before unsuppressed filing-risk predictions are shown.','2026-03-13',NULL,'SYSTEM','2026-03-13'),
(7,'capital.warning_buffer','2.0','Buffer in percentage points above or below a threshold that maps to a warning.','2026-03-13',NULL,'SYSTEM','2026-03-13'),
(8,'capital.forecast_quarters','2','Number of quarters to project ahead for prudential metrics.','2026-03-13',NULL,'SYSTEM','2026-03-13'),
(9,'capital.min_data_points','6','Minimum quarterly observations required before capital forecasts become visible.','2026-03-13',NULL,'SYSTEM','2026-03-13'),
(10,'chs.decline_alert_threshold','5.0','Projected CHS decline in points that triggers an advisory alert.','2026-03-13',NULL,'SYSTEM','2026-03-13'),
(11,'chs.forecast_periods','3','Number of future scoring periods to project for CHS trend forecasting.','2026-03-13',NULL,'SYSTEM','2026-03-13'),
(12,'churn.high_threshold','0.70','Probability threshold for classifying a tenant as high churn risk.','2026-03-13',NULL,'SYSTEM','2026-03-13'),
(13,'churn.medium_threshold','0.40','Probability threshold for classifying a tenant as medium churn risk.','2026-03-13',NULL,'SYSTEM','2026-03-13'),
(14,'churn.lookback_days','90','Lookback window in days for platform-engagement and churn features.','2026-03-13',NULL,'SYSTEM','2026-03-13'),
(15,'regaction.high_threshold','0.60','Probability threshold for regulator-priority classification.','2026-03-13',NULL,'SYSTEM','2026-03-13'),
(16,'regaction.forecast_months','6','Advisory horizon in months for supervisory-intervention probability.','2026-03-13',NULL,'SYSTEM','2026-03-13'),
(17,'prediction.min_confidence','0.55','Predictions below this confidence score are persisted as suppressed.','2026-03-13',NULL,'SYSTEM','2026-03-13'),
(18,'prediction.suppress_low_data','true','Suppress predictions when the historical dataset is below the configured minimum.','2026-03-13',NULL,'SYSTEM','2026-03-13'),
(19,'alert.cooldown_hours','24','Minimum number of hours between equivalent advisory alerts for the same prediction target.','2026-03-13',NULL,'SYSTEM','2026-03-13'),
(20,'prediction.stale_after_hours','24','Hours after which a tenant dashboard run is considered stale and will be recomputed on demand.','2026-03-13',NULL,'SYSTEM','2026-03-13');
SET IDENTITY_INSERT [meta].[foresight_config] OFF;

SET IDENTITY_INSERT [meta].[foresight_model_versions] ON;
INSERT INTO [meta].[foresight_model_versions] ([Id],[ModelCode],[VersionNumber],[Status],[TrainedAt],[ObservationsCount],[AccuracyMetric],[AccuracyMetricName],[Notes],[CreatedAt]) VALUES
(1,'FILING_RISK',1,'ACTIVE','2026-03-13',0,0.8125,'ROC_AUC','Weighted logistic seed tuned for Nigerian filing behaviour.','2026-03-13'),
(2,'CAPITAL_BREACH',1,'ACTIVE','2026-03-13',0,0.7680,'MAPE','Holt linear smoothing for CAR, NPL and liquidity trajectories.','2026-03-13'),
(3,'CHS_TREND',1,'ACTIVE','2026-03-13',0,0.7425,'RMSE','Weighted moving-average projection for CHS deterioration and recovery.','2026-03-13'),
(4,'CHURN_RISK',1,'ACTIVE','2026-03-13',0,0.7934,'ROC_AUC','Tenant-engagement classifier for subscription-retention risk.','2026-03-13'),
(5,'REG_ACTION',1,'ACTIVE','2026-03-13',0,0.7811,'ROC_AUC','Composite supervisory-priority classifier combining EWI, CHS, anomaly and timeliness pressure.','2026-03-13');
SET IDENTITY_INSERT [meta].[foresight_model_versions] OFF;

SET IDENTITY_INSERT [meta].[foresight_feature_definitions] ON;
INSERT INTO [meta].[foresight_feature_definitions] ([Id],[ModelCode],[FeatureName],[FeatureLabel],[Description],[DataSource],[DefaultWeight],[IsActive]) VALUES
(1,'FILING_RISK','days_to_deadline','Days to Deadline','Normalized countdown pressure before the regulatory deadline.','return_periods',0.22,1),
(2,'FILING_RISK','historical_late_rate','Historical Late Rate','Late-filing share for the same module across recent filing periods.','filing_sla_records',0.18,1),
(3,'FILING_RISK','draft_completeness_gap','Draft Completeness Gap','Share of expected template fields that are still blank in the current draft.','return_drafts',0.16,1),
(4,'FILING_RISK','preparation_stage','Preparation Stage','Preparation stage from draft through submitted/accepted.','return_submissions',0.14,1),
(5,'FILING_RISK','login_activity_gap','Login Activity Gap','Deficit in recent portal-login intensity for the tenant.','login_attempts',0.08,1),
(6,'FILING_RISK','recent_late_count','Recent Late Count','Count of recent late or overdue filings across the last four quarters.','filing_sla_records',0.08,1),
(7,'FILING_RISK','anomaly_pressure','Anomaly Pressure','Residual anomaly pressure from the latest accepted return for the module.','anomaly_reports',0.07,1),
(8,'FILING_RISK','concurrent_filings','Concurrent Filing Load','Number of open filing obligations clustered around the same deadline window.','return_periods',0.07,1),
(9,'CAPITAL_BREACH','threshold_buffer','Threshold Buffer','Distance between the current metric and its closest regulatory threshold.','prudential_metrics',0.30,1),
(10,'CAPITAL_BREACH','trend_slope','Trend Slope','Linear direction of travel across recent prudential periods.','prudential_metrics',0.24,1),
(11,'CAPITAL_BREACH','volatility','Volatility','Observed variability in the metric across recent periods.','prudential_metrics',0.16,1),
(12,'CAPITAL_BREACH','credit_stress_proxy','Credit Stress Proxy','Credit-quality or liquidity co-metric stress surrounding the forecasted threshold.','prudential_metrics',0.16,1),
(13,'CAPITAL_BREACH','ewi_pressure','EWI Pressure','Active prudential early warnings already signalling weakness.','ewi_triggers',0.14,1),
(14,'CHS_TREND','overall_trend_slope','Overall Trend Slope','Recent CHS trajectory over the latest scoring periods.','chs_score_snapshots',0.25,1),
(15,'CHS_TREND','filing_pillar','Filing Timeliness Pillar','Current filing timeliness pillar score.','chs_score_snapshots',0.18,1),
(16,'CHS_TREND','data_quality_pillar','Data Quality Pillar','Current data-quality pillar score.','chs_score_snapshots',0.18,1),
(17,'CHS_TREND','capital_pillar','Regulatory Capital Pillar','Current regulatory-capital pillar score.','chs_score_snapshots',0.16,1),
(18,'CHS_TREND','engagement_pillar','Engagement Pillar','Current engagement pillar score.','chs_score_snapshots',0.13,1),
(19,'CHS_TREND','consecutive_declines','Consecutive Declines','Count of consecutive score periods with decline.','chs_score_snapshots',0.10,1),
(20,'CHURN_RISK','login_trend','Login Trend','Trend in successful portal-login activity.','login_attempts',0.23,1),
(21,'CHURN_RISK','usage_drop','Usage Drop','Drop in active users, entities, modules or return submissions across recent months.','usage_records',0.20,1),
(22,'CHURN_RISK','complianceiq_gap','ComplianceIQ Activity Gap','Deficit in recent natural-language compliance-query usage.','complianceiq_turns',0.14,1),
(23,'CHURN_RISK','support_pressure','Support Pressure','Support-ticket escalation and unresolved support burden.','partner_support_tickets',0.12,1),
(24,'CHURN_RISK','payment_delay','Payment Delay','Delayed invoice settlement and overdue billing posture.','invoices,payments',0.17,1),
(25,'CHURN_RISK','filing_timeliness_gap','Filing Timeliness Gap','Recent decline in on-time filing performance, used as an engagement signal.','filing_sla_records',0.14,1),
(26,'REG_ACTION','critical_ewi_count','Critical EWI Count','Number of active critical prudential or conduct EWIs.','ewi_triggers',0.26,1),
(27,'REG_ACTION','chs_deficit','CHS Deficit','Distance between current compliance health and full-score posture.','chs_score_snapshots',0.18,1),
(28,'REG_ACTION','anomaly_pressure','Anomaly Pressure','Severity and density of unresolved anomaly findings.','anomaly_reports',0.18,1),
(29,'REG_ACTION','filing_delinquency','Filing Delinquency','Late, overdue, or deteriorating filing posture across recent obligations.','filing_sla_records,return_periods',0.14,1),
(30,'REG_ACTION','capital_proximity','Capital Proximity','How close prudential capital and liquidity metrics are to binding thresholds.','prudential_metrics',0.16,1),
(31,'REG_ACTION','camels_pressure','CAMELS Pressure','Composite CAMELS severity from the latest supervisory scoring.','camels_ratings',0.08,1);
SET IDENTITY_INSERT [meta].[foresight_feature_definitions] OFF;

SET IDENTITY_INSERT [meta].[foresight_regulatory_thresholds] ON;
INSERT INTO [meta].[foresight_regulatory_thresholds] ([Id],[Regulator],[LicenceCategory],[MetricCode],[MetricLabel],[ThresholdValue],[ThresholdType],[SeverityIfBreached],[Description],[CircularReference],[IsActive]) VALUES
(1,'CBN','DMB','CAR','Capital Adequacy Ratio',15.0,'MINIMUM','CRITICAL','Deposit Money Banks should maintain at least 15%% CAR.','BSD/1/2004',1),
(2,'CBN','MFB','CAR','Capital Adequacy Ratio',10.0,'MINIMUM','CRITICAL','Microfinance banks should maintain at least 10%% CAR.','RPSD/DIR/GEN/12/005',1),
(3,'CBN','PMB','CAR','Capital Adequacy Ratio',10.0,'MINIMUM','CRITICAL','Primary mortgage banks should maintain at least 10%% CAR.','BSD/1/2004',1),
(4,'CBN','DMB','NPL','Non-Performing Loan Ratio',5.0,'MAXIMUM','WARNING','CBN prudential guidance flags NPL ratios above 5%% for closer monitoring.','BSD/DIR/GEN/VOL.2/06',1),
(5,'CBN','DMB','NPL','Non-Performing Loan Ratio',10.0,'MAXIMUM','CRITICAL','NPL ratios above 10%% require enhanced supervisory attention.','BSD/DIR/GEN/VOL.2/06',1),
(6,'CBN','MFB','NPL','Non-Performing Loan Ratio',5.0,'MAXIMUM','WARNING','Microfinance-bank portfolios above 5%% NPL should be escalated for review.','RPSD/DIR/GEN/12/005',1),
(7,'CBN','DMB','LCR','Liquidity Coverage Ratio',100.0,'MINIMUM','CRITICAL','Liquidity coverage ratio should remain at or above 100%%.','BSD/DIR/GEN/VOL.2/11',1),
(8,'CBN','MFB','LCR','Liquidity Coverage Ratio',100.0,'MINIMUM','CRITICAL','Microfinance-bank liquidity coverage ratio should remain at or above 100%%.','RPSD/DIR/GEN/12/005',1),
(9,'NAICOM','GENERAL_INSURER','SOLVENCY_MARGIN','Solvency Margin',100.0,'MINIMUM','CRITICAL','General insurers should sustain a solvency margin above 100%%.','NAICOM Act 1997',1),
(10,'NAICOM','LIFE_INSURER','SOLVENCY_MARGIN','Solvency Margin',100.0,'MINIMUM','CRITICAL','Life insurers should sustain a solvency margin above 100%%.','NAICOM Act 1997',1),
(11,'SEC','BROKER_DEALER','CAPITAL_ADEQUACY','Capital Adequacy',10.0,'MINIMUM','CRITICAL','Broker-dealers should maintain capital adequacy above 10%%.','SEC Rules 2013',1),
(12,'SEC','BROKER_DEALER','SEGREGATION_RATIO','Client Funds Segregation Ratio',100.0,'MINIMUM','CRITICAL','Client funds should remain fully segregated.','ISA 2007 s.148',1);
SET IDENTITY_INSERT [meta].[foresight_regulatory_thresholds] OFF;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "foresight_alerts", schema: "meta");
        migrationBuilder.DropTable(name: "foresight_prediction_features", schema: "meta");
        migrationBuilder.DropTable(name: "foresight_predictions", schema: "meta");
        migrationBuilder.DropTable(name: "foresight_regulatory_thresholds", schema: "meta");
        migrationBuilder.DropTable(name: "foresight_feature_definitions", schema: "meta");
        migrationBuilder.DropTable(name: "foresight_model_versions", schema: "meta");
        migrationBuilder.DropTable(name: "foresight_config", schema: "meta");
    }
}
