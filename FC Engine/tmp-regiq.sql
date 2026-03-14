BEGIN TRANSACTION;
GO

CREATE TABLE [meta].[complianceiq_config] (
    [Id] int NOT NULL IDENTITY,
    [ConfigKey] nvarchar(100) NOT NULL,
    [ConfigValue] nvarchar(max) NOT NULL,
    [Description] nvarchar(500) NOT NULL,
    [EffectiveFrom] datetime2(3) NOT NULL,
    [EffectiveTo] datetime2(3) NULL,
    [CreatedBy] nvarchar(100) NOT NULL,
    [CreatedAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_complianceiq_config] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [meta].[complianceiq_conversations] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [UserId] nvarchar(100) NOT NULL,
    [UserRole] nvarchar(60) NOT NULL,
    [IsRegulatorContext] bit NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [StartedAt] datetime2(3) NOT NULL,
    [LastActivityAt] datetime2(3) NOT NULL,
    [TurnCount] int NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_complianceiq_conversations] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [meta].[complianceiq_field_synonyms] (
    [Id] int NOT NULL IDENTITY,
    [Synonym] nvarchar(200) NOT NULL,
    [FieldCode] nvarchar(80) NOT NULL,
    [ModuleCode] nvarchar(40) NULL,
    [RegulatorCode] nvarchar(20) NULL,
    [ConfidenceBoost] decimal(5,2) NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_complianceiq_field_synonyms] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [meta].[complianceiq_intents] (
    [Id] int NOT NULL IDENTITY,
    [IntentCode] nvarchar(40) NOT NULL,
    [Category] nvarchar(40) NOT NULL,
    [DisplayName] nvarchar(120) NOT NULL,
    [Description] nvarchar(500) NOT NULL,
    [RequiresRegulatorContext] bit NOT NULL,
    [IsEnabled] bit NOT NULL,
    [SortOrder] int NOT NULL,
    [CreatedAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_complianceiq_intents] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [meta].[complianceiq_quick_questions] (
    [Id] int NOT NULL IDENTITY,
    [QuestionText] nvarchar(220) NOT NULL,
    [Category] nvarchar(40) NOT NULL,
    [IconClass] nvarchar(80) NOT NULL,
    [RequiresRegulatorContext] bit NOT NULL,
    [IsActive] bit NOT NULL,
    [SortOrder] int NOT NULL,
    CONSTRAINT [PK_complianceiq_quick_questions] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [meta].[complianceiq_templates] (
    [Id] int NOT NULL IDENTITY,
    [IntentCode] nvarchar(40) NOT NULL,
    [TemplateCode] nvarchar(60) NOT NULL,
    [DisplayName] nvarchar(150) NOT NULL,
    [Description] nvarchar(500) NOT NULL,
    [TemplateBody] nvarchar(max) NOT NULL,
    [ParameterSchema] nvarchar(max) NOT NULL,
    [ResultFormat] nvarchar(20) NOT NULL,
    [VisualizationType] nvarchar(30) NOT NULL,
    [RequiresRegulatorContext] bit NOT NULL,
    [IsActive] bit NOT NULL,
    [SortOrder] int NOT NULL,
    [CreatedAt] datetime2(3) NOT NULL,
    [UpdatedAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_complianceiq_templates] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [meta].[complianceiq_turns] (
    [Id] int NOT NULL IDENTITY,
    [ConversationId] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [UserId] nvarchar(100) NOT NULL,
    [UserRole] nvarchar(60) NOT NULL,
    [TurnNumber] int NOT NULL,
    [QueryText] nvarchar(max) NOT NULL,
    [IntentCode] nvarchar(40) NOT NULL,
    [IntentConfidence] decimal(5,4) NOT NULL,
    [ExtractedEntitiesJson] nvarchar(max) NOT NULL,
    [TemplateCode] nvarchar(60) NOT NULL,
    [ResolvedParametersJson] nvarchar(max) NOT NULL,
    [ExecutedPlan] nvarchar(max) NOT NULL,
    [RowCount] int NOT NULL,
    [ExecutionTimeMs] int NOT NULL,
    [ResponseText] nvarchar(max) NOT NULL,
    [ResponseDataJson] nvarchar(max) NOT NULL,
    [VisualizationType] nvarchar(30) NOT NULL,
    [ConfidenceLevel] nvarchar(10) NOT NULL,
    [CitationsJson] nvarchar(max) NOT NULL,
    [FollowUpSuggestionsJson] nvarchar(max) NOT NULL,
    [TotalTimeMs] int NOT NULL,
    [ErrorMessage] nvarchar(500) NULL,
    [CreatedAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_complianceiq_turns] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_complianceiq_turns_complianceiq_conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [meta].[complianceiq_conversations] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [meta].[complianceiq_feedback] (
    [Id] int NOT NULL IDENTITY,
    [TurnId] int NOT NULL,
    [UserId] nvarchar(100) NOT NULL,
    [Rating] smallint NOT NULL,
    [FeedbackText] nvarchar(1000) NULL,
    [CreatedAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_complianceiq_feedback] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_complianceiq_feedback_complianceiq_turns_TurnId] FOREIGN KEY ([TurnId]) REFERENCES [meta].[complianceiq_turns] ([Id]) ON DELETE CASCADE
);
GO

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ConfigKey', N'ConfigValue', N'CreatedAt', N'CreatedBy', N'Description', N'EffectiveFrom', N'EffectiveTo') AND [object_id] = OBJECT_ID(N'[meta].[complianceiq_config]'))
    SET IDENTITY_INSERT [meta].[complianceiq_config] ON;
INSERT INTO [meta].[complianceiq_config] ([Id], [ConfigKey], [ConfigValue], [CreatedAt], [CreatedBy], [Description], [EffectiveFrom], [EffectiveTo])
VALUES (1, N'rate.queries_per_minute', N'10', '2026-03-12T00:00:00.000Z', N'SYSTEM', N'Maximum ComplianceIQ queries per user per minute.', '2026-03-12T00:00:00.000Z', NULL),
(2, N'rate.queries_per_hour', N'100', '2026-03-12T00:00:00.000Z', N'SYSTEM', N'Maximum ComplianceIQ queries per user per hour.', '2026-03-12T00:00:00.000Z', NULL),
(3, N'rate.queries_per_day', N'500', '2026-03-12T00:00:00.000Z', N'SYSTEM', N'Maximum ComplianceIQ queries per user per day.', '2026-03-12T00:00:00.000Z', NULL),
(4, N'response.max_rows', N'25', '2026-03-12T00:00:00.000Z', N'SYSTEM', N'Maximum grounded rows returned in a ComplianceIQ answer.', '2026-03-12T00:00:00.000Z', NULL),
(5, N'trend.default_periods', N'8', '2026-03-12T00:00:00.000Z', N'SYSTEM', N'Default lookback window for trend questions when the user does not specify one.', '2026-03-12T00:00:00.000Z', NULL),
(6, N'confidence.high_threshold', N'0.85', '2026-03-12T00:00:00.000Z', N'SYSTEM', N'Minimum confidence for a HIGH-confidence response label.', '2026-03-12T00:00:00.000Z', NULL),
(7, N'confidence.medium_threshold', N'0.60', '2026-03-12T00:00:00.000Z', N'SYSTEM', N'Minimum confidence for a MEDIUM-confidence response label.', '2026-03-12T00:00:00.000Z', NULL),
(8, N'help.welcome_message', N'Welcome to ComplianceIQ. Ask about returns, deadlines, anomalies, peer benchmarks, compliance health, or regulator knowledge.', '2026-03-12T00:00:00.000Z', N'SYSTEM', N'Welcome message shown in the ComplianceIQ chat surface.', '2026-03-12T00:00:00.000Z', NULL),
(9, N'scenario.default_npl_multiplier', N'2.0', '2026-03-12T00:00:00.000Z', N'SYSTEM', N'Fallback NPL multiplier for scenario analysis when a user says doubled without a numeric multiplier.', '2026-03-12T00:00:00.000Z', NULL);
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ConfigKey', N'ConfigValue', N'CreatedAt', N'CreatedBy', N'Description', N'EffectiveFrom', N'EffectiveTo') AND [object_id] = OBJECT_ID(N'[meta].[complianceiq_config]'))
    SET IDENTITY_INSERT [meta].[complianceiq_config] OFF;
GO

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ConfidenceBoost', N'CreatedAt', N'FieldCode', N'IsActive', N'ModuleCode', N'RegulatorCode', N'Synonym') AND [object_id] = OBJECT_ID(N'[meta].[complianceiq_field_synonyms]'))
    SET IDENTITY_INSERT [meta].[complianceiq_field_synonyms] ON;
INSERT INTO [meta].[complianceiq_field_synonyms] ([Id], [ConfidenceBoost], [CreatedAt], [FieldCode], [IsActive], [ModuleCode], [RegulatorCode], [Synonym])
VALUES (1, 1.0, '2026-03-12T00:00:00.000Z', N'carratio', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'car'),
(2, 1.0, '2026-03-12T00:00:00.000Z', N'carratio', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'capital adequacy'),
(3, 1.0, '2026-03-12T00:00:00.000Z', N'carratio', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'capital adequacy ratio'),
(4, 1.0, '2026-03-12T00:00:00.000Z', N'nplratio', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'npl'),
(5, 1.0, '2026-03-12T00:00:00.000Z', N'nplratio', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'npl ratio'),
(6, 1.0, '2026-03-12T00:00:00.000Z', N'nplratio', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'non performing loans'),
(7, 1.0, '2026-03-12T00:00:00.000Z', N'nplamount', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'npl amount'),
(8, 1.0, '2026-03-12T00:00:00.000Z', N'liquidityratio', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'liquidity'),
(9, 1.0, '2026-03-12T00:00:00.000Z', N'liquidityratio', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'liquidity ratio'),
(10, 1.0, '2026-03-12T00:00:00.000Z', N'liquidityratio', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'lcr'),
(11, 1.0, '2026-03-12T00:00:00.000Z', N'loandepositratio', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'loan to deposit ratio'),
(12, 1.0, '2026-03-12T00:00:00.000Z', N'loandepositratio', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'ldr'),
(13, 1.0, '2026-03-12T00:00:00.000Z', N'roa', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'roa'),
(14, 1.0, '2026-03-12T00:00:00.000Z', N'roa', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'return on assets'),
(15, 1.0, '2026-03-12T00:00:00.000Z', N'roe', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'roe'),
(16, 1.0, '2026-03-12T00:00:00.000Z', N'roe', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'return on equity'),
(17, 1.0, '2026-03-12T00:00:00.000Z', N'netinterestmargin', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'nim'),
(18, 1.0, '2026-03-12T00:00:00.000Z', N'netinterestmargin', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'net interest margin'),
(19, 1.0, '2026-03-12T00:00:00.000Z', N'totalassets', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'total assets'),
(20, 1.0, '2026-03-12T00:00:00.000Z', N'totalliabilities', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'total liabilities'),
(21, 1.0, '2026-03-12T00:00:00.000Z', N'shareholdersfunds', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'shareholders funds'),
(22, 1.0, '2026-03-12T00:00:00.000Z', N'riskweightedassets', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'risk weighted assets'),
(23, 1.0, '2026-03-12T00:00:00.000Z', N'riskweightedassets', CAST(1 AS bit), N'CBN_PRUDENTIAL', N'CBN', N'rwa'),
(24, 1.0, '2026-03-12T00:00:00.000Z', N'insureddeposits', CAST(1 AS bit), N'NDIC_SRF', N'NDIC', N'insured deposits'),
(25, 1.0, '2026-03-12T00:00:00.000Z', N'depositpremiumdue', CAST(1 AS bit), N'NDIC_SRF', N'NDIC', N'deposit premium'),
(26, 1.0, '2026-03-12T00:00:00.000Z', N'grosspremium', CAST(1 AS bit), N'NAICOM_QR', N'NAICOM', N'gross premium'),
(27, 1.0, '2026-03-12T00:00:00.000Z', N'lossratio', CAST(1 AS bit), N'NAICOM_QR', N'NAICOM', N'loss ratio'),
(28, 1.0, '2026-03-12T00:00:00.000Z', N'combinedratio', CAST(1 AS bit), N'NAICOM_QR', N'NAICOM', N'combined ratio'),
(29, 1.0, '2026-03-12T00:00:00.000Z', N'solvencymargin', CAST(1 AS bit), N'NAICOM_QR', N'NAICOM', N'solvency margin'),
(30, 1.0, '2026-03-12T00:00:00.000Z', N'netcapital', CAST(1 AS bit), N'SEC_CMO', N'SEC', N'net capital'),
(31, 1.0, '2026-03-12T00:00:00.000Z', N'liquidcapital', CAST(1 AS bit), N'SEC_CMO', N'SEC', N'liquid capital'),
(32, 1.0, '2026-03-12T00:00:00.000Z', N'clientassetsaum', CAST(1 AS bit), N'SEC_CMO', N'SEC', N'assets under management'),
(33, 1.0, '2026-03-12T00:00:00.000Z', N'clientassetsaum', CAST(1 AS bit), N'SEC_CMO', N'SEC', N'aum'),
(34, 1.0, '2026-03-12T00:00:00.000Z', N'tradevolume', CAST(1 AS bit), N'SEC_CMO', N'SEC', N'trade volume'),
(35, 1.0, '2026-03-12T00:00:00.000Z', N'segregationratio', CAST(1 AS bit), N'SEC_CMO', N'SEC', N'segregation ratio');
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ConfidenceBoost', N'CreatedAt', N'FieldCode', N'IsActive', N'ModuleCode', N'RegulatorCode', N'Synonym') AND [object_id] = OBJECT_ID(N'[meta].[complianceiq_field_synonyms]'))
    SET IDENTITY_INSERT [meta].[complianceiq_field_synonyms] OFF;
GO

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Category', N'CreatedAt', N'Description', N'DisplayName', N'IntentCode', N'IsEnabled', N'RequiresRegulatorContext', N'SortOrder') AND [object_id] = OBJECT_ID(N'[meta].[complianceiq_intents]'))
    SET IDENTITY_INSERT [meta].[complianceiq_intents] ON;
INSERT INTO [meta].[complianceiq_intents] ([Id], [Category], [CreatedAt], [Description], [DisplayName], [IntentCode], [IsEnabled], [RequiresRegulatorContext], [SortOrder])
VALUES (1, N'DATA', '2026-03-12T00:00:00.000Z', N'Retrieve the latest grounded value for a regulatory field.', N'Current Value', N'CURRENT_VALUE', CAST(1 AS bit), CAST(0 AS bit), 1),
(2, N'DATA', '2026-03-12T00:00:00.000Z', N'Show historical movement for a field across recent filing periods.', N'Trend', N'TREND', CAST(1 AS bit), CAST(0 AS bit), 2),
(3, N'BENCHMARK', '2026-03-12T00:00:00.000Z', N'Compare a metric with the peer median or peer band.', N'Peer Comparison', N'COMPARISON_PEER', CAST(1 AS bit), CAST(0 AS bit), 3),
(4, N'BENCHMARK', '2026-03-12T00:00:00.000Z', N'Compare a metric between two filing periods.', N'Period Comparison', N'COMPARISON_PERIOD', CAST(1 AS bit), CAST(0 AS bit), 4),
(5, N'CALENDAR', '2026-03-12T00:00:00.000Z', N'List upcoming or overdue filing deadlines.', N'Deadline', N'DEADLINE', CAST(1 AS bit), CAST(0 AS bit), 5),
(6, N'KNOWLEDGE', '2026-03-12T00:00:00.000Z', N'Search regulatory guidance, circulars, and knowledge-base content.', N'Regulatory Lookup', N'REGULATORY_LOOKUP', CAST(1 AS bit), CAST(0 AS bit), 6),
(7, N'STATUS', '2026-03-12T00:00:00.000Z', N'Summarise compliance health and key compliance posture indicators.', N'Compliance Status', N'COMPLIANCE_STATUS', CAST(1 AS bit), CAST(0 AS bit), 7),
(8, N'QUALITY', '2026-03-12T00:00:00.000Z', N'Summarise anomaly findings and data quality signals.', N'Anomaly Status', N'ANOMALY_STATUS', CAST(1 AS bit), CAST(0 AS bit), 8),
(9, N'ANALYSIS', '2026-03-12T00:00:00.000Z', N'Project simple prudential what-if scenarios such as an NPL shock.', N'Scenario', N'SCENARIO', CAST(1 AS bit), CAST(0 AS bit), 9),
(10, N'DISCOVERY', '2026-03-12T00:00:00.000Z', N'Search validation history and filing evidence.', N'Search', N'SEARCH', CAST(1 AS bit), CAST(0 AS bit), 10),
(11, N'REGULATOR', '2026-03-12T00:00:00.000Z', N'Aggregate a metric across supervised institutions.', N'Sector Aggregate', N'SECTOR_AGGREGATE', CAST(1 AS bit), CAST(1 AS bit), 11),
(12, N'REGULATOR', '2026-03-12T00:00:00.000Z', N'Compare named institutions on a selected metric.', N'Entity Compare', N'ENTITY_COMPARE', CAST(1 AS bit), CAST(1 AS bit), 12),
(13, N'REGULATOR', '2026-03-12T00:00:00.000Z', N'Rank institutions by anomaly pressure or data quality weakness.', N'Risk Ranking', N'RISK_RANKING', CAST(1 AS bit), CAST(1 AS bit), 13),
(14, N'SYSTEM', '2026-03-12T00:00:00.000Z', N'Explain the kinds of questions ComplianceIQ can answer.', N'Help', N'HELP', CAST(1 AS bit), CAST(0 AS bit), 14),
(15, N'SYSTEM', '2026-03-12T00:00:00.000Z', N'Fallback when a question is ambiguous or unsupported.', N'Clarification', N'UNCLEAR', CAST(1 AS bit), CAST(0 AS bit), 15),
(16, N'REGULATOR', '2026-03-12T00:00:00.000Z', N'Build a composite supervisory profile for one supervised institution.', N'Entity Intelligence Profile', N'ENTITY_PROFILE', CAST(1 AS bit), CAST(1 AS bit), 16),
(17, N'REGULATOR', '2026-03-12T00:00:00.000Z', N'Show a sector-level trend for a metric across time.', N'Sector Metric Trend', N'SECTOR_TREND', CAST(1 AS bit), CAST(1 AS bit), 17),
(18, N'REGULATOR', '2026-03-12T00:00:00.000Z', N'Rank supervised institutions by a requested metric.', N'Top or Bottom Ranking', N'TOP_N_RANKING', CAST(1 AS bit), CAST(1 AS bit), 18),
(19, N'REGULATOR', '2026-03-12T00:00:00.000Z', N'Show overdue, pending, or due filing status across entities.', N'Filing Status Check', N'FILING_STATUS', CAST(1 AS bit), CAST(1 AS bit), 19),
(20, N'REGULATOR', '2026-03-12T00:00:00.000Z', N'Rank supervised institutions by filing timeliness and delinquency.', N'Filing Delinquency Ranking', N'FILING_DELINQUENCY', CAST(1 AS bit), CAST(1 AS bit), 20),
(21, N'REGULATOR', '2026-03-12T00:00:00.000Z', N'Rank institutions by Compliance Health Score.', N'Compliance Health Ranking', N'CHS_RANKING', CAST(1 AS bit), CAST(1 AS bit), 21),
(22, N'REGULATOR', '2026-03-12T00:00:00.000Z', N'Return the compliance health breakdown for one entity.', N'Entity Compliance Health', N'CHS_ENTITY', CAST(1 AS bit), CAST(1 AS bit), 22),
(23, N'REGULATOR', '2026-03-12T00:00:00.000Z', N'Summarise early warning flags across supervised entities.', N'Early Warning Status', N'EWI_STATUS', CAST(1 AS bit), CAST(1 AS bit), 23),
(24, N'SYSTEMIC', '2026-03-12T00:00:00.000Z', N'Return a system-wide supervisory risk dashboard.', N'Systemic Risk Overview', N'SYSTEMIC_DASHBOARD', CAST(1 AS bit), CAST(1 AS bit), 24),
(25, N'SYSTEMIC', '2026-03-12T00:00:00.000Z', N'Analyse contagion effects and interbank spillovers around a named institution.', N'Contagion Analysis', N'CONTAGION_QUERY', CAST(1 AS bit), CAST(1 AS bit), 25),
(26, N'SYSTEMIC', '2026-03-12T00:00:00.000Z', N'List available stress scenarios or return stress-test outputs.', N'Stress Test Results', N'STRESS_SCENARIOS', CAST(1 AS bit), CAST(1 AS bit), 26),
(27, N'REGULATOR', '2026-03-12T00:00:00.000Z', N'Summarise sanctions-screening and AML exposure across entities.', N'Sanctions Exposure', N'SANCTIONS_EXPOSURE', CAST(1 AS bit), CAST(1 AS bit), 27),
(28, N'REGULATOR', '2026-03-12T00:00:00.000Z', N'Generate a comprehensive supervisory briefing for one institution.', N'Examination Briefing', N'EXAMINATION_BRIEF', CAST(1 AS bit), CAST(1 AS bit), 28),
(29, N'REGULATOR', '2026-03-12T00:00:00.000Z', N'Show outstanding or overdue supervisory actions and recommendations.', N'Supervisory Actions', N'SUPERVISORY_ACTIONS', CAST(1 AS bit), CAST(1 AS bit), 29),
(30, N'REGULATOR', '2026-03-12T00:00:00.000Z', N'Return cross-border, group, harmonisation, or divergence intelligence.', N'Cross-Border Intelligence', N'CROSS_BORDER', CAST(1 AS bit), CAST(1 AS bit), 30),
(31, N'REGULATOR', '2026-03-12T00:00:00.000Z', N'Return policy simulation and what-if impact outputs.', N'Policy Impact', N'POLICY_IMPACT', CAST(1 AS bit), CAST(1 AS bit), 31),
(32, N'REGULATOR', '2026-03-12T00:00:00.000Z', N'Aggregate validation-error hotspots across institutions and templates.', N'Validation Hotspot', N'VALIDATION_HOTSPOT', CAST(1 AS bit), CAST(1 AS bit), 32);
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Category', N'CreatedAt', N'Description', N'DisplayName', N'IntentCode', N'IsEnabled', N'RequiresRegulatorContext', N'SortOrder') AND [object_id] = OBJECT_ID(N'[meta].[complianceiq_intents]'))
    SET IDENTITY_INSERT [meta].[complianceiq_intents] OFF;
GO

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Category', N'IconClass', N'IsActive', N'QuestionText', N'RequiresRegulatorContext', N'SortOrder') AND [object_id] = OBJECT_ID(N'[meta].[complianceiq_quick_questions]'))
    SET IDENTITY_INSERT [meta].[complianceiq_quick_questions] ON;
INSERT INTO [meta].[complianceiq_quick_questions] ([Id], [Category], [IconClass], [IsActive], [QuestionText], [RequiresRegulatorContext], [SortOrder])
VALUES (1, N'DATA', N'bi-bank', CAST(1 AS bit), N'What is our current CAR?', CAST(0 AS bit), 1),
(2, N'TREND', N'bi-graph-up', CAST(1 AS bit), N'Show NPL trend over the last 8 quarters', CAST(0 AS bit), 2),
(3, N'CALENDAR', N'bi-calendar-event', CAST(1 AS bit), N'When is our next filing due?', CAST(0 AS bit), 3),
(4, N'QUALITY', N'bi-exclamation-triangle', CAST(1 AS bit), N'Do we have any anomalies in our latest return?', CAST(0 AS bit), 4),
(5, N'STATUS', N'bi-heart-pulse', CAST(1 AS bit), N'What is our compliance health score?', CAST(0 AS bit), 5),
(6, N'BENCHMARK', N'bi-people', CAST(1 AS bit), N'How does our liquidity compare to peers?', CAST(0 AS bit), 6),
(7, N'REGULATOR', N'bi-sort-numeric-down', CAST(1 AS bit), N'Rank institutions by anomaly density', CAST(1 AS bit), 7),
(8, N'REGULATOR', N'bi-bar-chart', CAST(1 AS bit), N'What is aggregate CAR across commercial banks?', CAST(1 AS bit), 8),
(9, N'REGULATOR', N'bi-diagram-3', CAST(1 AS bit), N'Compare Access Bank and GTBank on NPL ratio', CAST(1 AS bit), 9),
(10, N'KNOWLEDGE', N'bi-journal-text', CAST(1 AS bit), N'What does BSD/DIR/2024/003 require?', CAST(0 AS bit), 10);
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Category', N'IconClass', N'IsActive', N'QuestionText', N'RequiresRegulatorContext', N'SortOrder') AND [object_id] = OBJECT_ID(N'[meta].[complianceiq_quick_questions]'))
    SET IDENTITY_INSERT [meta].[complianceiq_quick_questions] OFF;
GO

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CreatedAt', N'Description', N'DisplayName', N'IntentCode', N'IsActive', N'ParameterSchema', N'RequiresRegulatorContext', N'ResultFormat', N'SortOrder', N'TemplateBody', N'TemplateCode', N'UpdatedAt', N'VisualizationType') AND [object_id] = OBJECT_ID(N'[meta].[complianceiq_templates]'))
    SET IDENTITY_INSERT [meta].[complianceiq_templates] ON;
INSERT INTO [meta].[complianceiq_templates] ([Id], [CreatedAt], [Description], [DisplayName], [IntentCode], [IsActive], [ParameterSchema], [RequiresRegulatorContext], [ResultFormat], [SortOrder], [TemplateBody], [TemplateCode], [UpdatedAt], [VisualizationType])
VALUES (1, '2026-03-12T00:00:00.000Z', N'Use the latest accepted submission that contains the requested field.', N'Latest Field Value', N'CURRENT_VALUE', CAST(1 AS bit), N'{"fieldCode":"string"}', CAST(0 AS bit), N'SCALAR', 1, N'Latest accepted submission -> extract requested metric -> cite module and period.', N'CV_SINGLE_FIELD', '2026-03-12T00:00:00.000Z', N'number'),
(2, '2026-03-12T00:00:00.000Z', N'Return the latest prudential key ratio bundle for the institution.', N'Latest Key Ratios', N'CURRENT_VALUE', CAST(1 AS bit), N'{"moduleCode":"string"}', CAST(0 AS bit), N'TABLE', 2, N'Latest accepted submission -> extract key ratios -> return tabular bundle.', N'CV_KEY_RATIOS', '2026-03-12T00:00:00.000Z', N'table'),
(3, '2026-03-12T00:00:00.000Z', N'Return the requested metric across multiple periods.', N'Field Trend', N'TREND', CAST(1 AS bit), N'{"fieldCode":"string","periodCount":"int"}', CAST(0 AS bit), N'TIMESERIES', 1, N'Accepted submissions ordered by period -> extract metric -> return timeseries.', N'TR_FIELD_HISTORY', '2026-03-12T00:00:00.000Z', N'lineChart'),
(4, '2026-03-12T00:00:00.000Z', N'Compare an institution metric with peer aggregates.', N'Peer Comparison', N'COMPARISON_PEER', CAST(1 AS bit), N'{"fieldCode":"string","licenceCategory":"string"}', CAST(0 AS bit), N'SCALAR', 1, N'Latest accepted submission + active peer stats -> compute deviation from peer median.', N'CP_PEER_METRIC', '2026-03-12T00:00:00.000Z', N'gauge'),
(5, '2026-03-12T00:00:00.000Z', N'Compare the same metric between two periods.', N'Two Period Comparison', N'COMPARISON_PERIOD', CAST(1 AS bit), N'{"fieldCode":"string","periodA":"string","periodB":"string"}', CAST(0 AS bit), N'TABLE', 1, N'Locate two accepted periods for tenant -> extract same field -> compute deltas.', N'CPR_TWO_PERIODS', '2026-03-12T00:00:00.000Z', N'barChart'),
(6, '2026-03-12T00:00:00.000Z', N'Return upcoming and overdue filing calendar items.', N'Filing Calendar', N'DEADLINE', CAST(1 AS bit), N'{"regulatorCode":"string?","overdueOnly":"bool"}', CAST(0 AS bit), N'TABLE', 1, N'Tenant return periods with module deadlines -> classify due, overdue, upcoming.', N'DL_CALENDAR', '2026-03-12T00:00:00.000Z', N'table'),
(7, '2026-03-12T00:00:00.000Z', N'Search knowledge-base and knowledge-graph records.', N'Knowledge Lookup', N'REGULATORY_LOOKUP', CAST(1 AS bit), N'{"keyword":"string"}', CAST(0 AS bit), N'LIST', 1, N'Knowledge articles + knowledge graph nodes/edges -> rank top regulatory matches.', N'RL_KNOWLEDGE', '2026-03-12T00:00:00.000Z', N'table'),
(8, '2026-03-12T00:00:00.000Z', N'Return the latest Compliance Health Score and pillars.', N'Compliance Health', N'COMPLIANCE_STATUS', CAST(1 AS bit), N'{}', CAST(0 AS bit), N'SCALAR', 1, N'Current CHS snapshot -> summarise overall and pillar scores.', N'CS_HEALTH_SCORE', '2026-03-12T00:00:00.000Z', N'gauge'),
(9, '2026-03-12T00:00:00.000Z', N'Return the latest anomaly report or detailed findings for a module.', N'Latest Anomaly Report', N'ANOMALY_STATUS', CAST(1 AS bit), N'{"moduleCode":"string?"}', CAST(0 AS bit), N'TABLE', 1, N'Latest anomaly report -> optionally expand findings for requested module.', N'AS_LATEST_REPORT', '2026-03-12T00:00:00.000Z', N'table'),
(10, '2026-03-12T00:00:00.000Z', N'Project CAR, NPL ratio, and LDR from an NPL shock.', N'CAR NPL Scenario', N'SCENARIO', CAST(1 AS bit), N'{"scenarioMultiplier":"decimal"}', CAST(0 AS bit), N'SCALAR', 1, N'Latest prudential submission -> apply NPL multiplier -> recompute CAR/NPL/LDR.', N'SC_CAR_NPL', '2026-03-12T00:00:00.000Z', N'number'),
(11, '2026-03-12T00:00:00.000Z', N'Return submissions with validation errors or keyword matches.', N'Validation Error Search', N'SEARCH', CAST(1 AS bit), N'{"keyword":"string?"}', CAST(0 AS bit), N'TABLE', 1, N'Validation reports joined to submissions -> count errors and warnings.', N'SR_VALIDATION_ERRORS', '2026-03-12T00:00:00.000Z', N'table'),
(12, '2026-03-12T00:00:00.000Z', N'Aggregate a field across supervised institutions.', N'Sector Aggregate', N'SECTOR_AGGREGATE', CAST(1 AS bit), N'{"fieldCode":"string","periodCode":"string?","licenceCategory":"string?"}', CAST(1 AS bit), N'AGGREGATE', 1, N'Cross-tenant accepted submissions for regulator context -> aggregate metric.', N'SA_FIELD_AGGREGATE', '2026-03-12T00:00:00.000Z', N'barChart'),
(13, '2026-03-12T00:00:00.000Z', N'Compare named institutions for a selected metric.', N'Entity Compare', N'ENTITY_COMPARE', CAST(1 AS bit), N'{"fieldCode":"string","entityNames":"string[]"}', CAST(1 AS bit), N'TABLE', 1, N'Cross-tenant accepted submissions -> extract requested metric for named institutions.', N'EC_ENTITY_COMPARE', '2026-03-12T00:00:00.000Z', N'barChart'),
(14, '2026-03-12T00:00:00.000Z', N'Rank institutions by anomaly density or quality score.', N'Anomaly Ranking', N'RISK_RANKING', CAST(1 AS bit), N'{"periodCode":"string?","moduleCode":"string?"}', CAST(1 AS bit), N'TABLE', 1, N'Cross-tenant anomaly reports -> order by quality score ascending.', N'RR_ANOMALY_RANKING', '2026-03-12T00:00:00.000Z', N'ranking'),
(15, '2026-03-12T00:00:00.000Z', N'Return the composite supervisory profile for one named institution.', N'Entity Intelligence Profile', N'ENTITY_PROFILE', CAST(1 AS bit), N'{"entityNames":"string[]","periodCode":"string?"}', CAST(1 AS bit), N'PROFILE', 1, N'Resolve entity -> call regulator intelligence profile service -> return profile and citations.', N'EP_ENTITY_PROFILE', '2026-03-12T00:00:00.000Z', N'profile'),
(16, '2026-03-12T00:00:00.000Z', N'Return a sector-level trend for the requested metric.', N'Sector Trend', N'SECTOR_TREND', CAST(1 AS bit), N'{"fieldCode":"string","periodCount":"int","licenceCategory":"string?","periodCode":"string?"}', CAST(1 AS bit), N'TIMESERIES', 1, N'Resolve metric and sector filter -> call sector analytics trend services -> return time-series output.', N'ST_SECTOR_TREND', '2026-03-12T00:00:00.000Z', N'lineChart'),
(17, '2026-03-12T00:00:00.000Z', N'Rank entities by a requested supervisory metric.', N'Metric Ranking', N'TOP_N_RANKING', CAST(1 AS bit), N'{"fieldCode":"string","limit":"int","direction":"string","licenceCategory":"string?","periodCode":"string?"}', CAST(1 AS bit), N'TABLE', 1, N'Resolve field, sort direction, and requested count -> run cross-tenant metric ranking.', N'TN_METRIC_RANKING', '2026-03-12T00:00:00.000Z', N'ranking'),
(18, '2026-03-12T00:00:00.000Z', N'Return overdue, due, or pending filings across supervised institutions.', N'Filing Status', N'FILING_STATUS', CAST(1 AS bit), N'{"licenceCategory":"string?","periodCode":"string?","status":"string?"}', CAST(1 AS bit), N'TABLE', 1, N'Resolve regulator scope and filing status filters -> query filing calendar and SLA state cross-tenant.', N'FS_FILING_STATUS', '2026-03-12T00:00:00.000Z', N'table'),
(19, '2026-03-12T00:00:00.000Z', N'Rank institutions by filing lateness and timeliness.', N'Filing Delinquency Ranking', N'FILING_DELINQUENCY', CAST(1 AS bit), N'{"licenceCategory":"string?","periodCode":"string?","limit":"int"}', CAST(1 AS bit), N'TABLE', 1, N'Cross-tenant filing SLA records -> aggregate late or overdue counts -> order by delinquency.', N'FD_TIMELINESS_RANKING', '2026-03-12T00:00:00.000Z', N'ranking'),
(20, '2026-03-12T00:00:00.000Z', N'Rank supervised institutions by current Compliance Health Score.', N'CHS Ranking', N'CHS_RANKING', CAST(1 AS bit), N'{"licenceCategory":"string?","limit":"int"}', CAST(1 AS bit), N'TABLE', 1, N'Cross-tenant CHS watch list and sector summary -> order entities by current score.', N'CR_CHS_RANKING', '2026-03-12T00:00:00.000Z', N'ranking'),
(21, '2026-03-12T00:00:00.000Z', N'Return the compliance health scorecard for one institution.', N'Entity CHS Breakdown', N'CHS_ENTITY', CAST(1 AS bit), N'{"entityNames":"string[]"}', CAST(1 AS bit), N'PROFILE', 1, N'Resolve entity -> load current Compliance Health Score -> return overall and pillar breakdown.', N'CE_CHS_ENTITY', '2026-03-12T00:00:00.000Z', N'gauge'),
(22, '2026-03-12T00:00:00.000Z', N'Return current early-warning flags across supervised entities.', N'Early Warning Status', N'EWI_STATUS', CAST(1 AS bit), N'{"licenceCategory":"string?","entityNames":"string[]?"}', CAST(1 AS bit), N'TABLE', 1, N'Compute early warning flags for the regulator scope -> optionally filter by entity or licence type.', N'EWI_SECTOR_STATUS', '2026-03-12T00:00:00.000Z', N'heatmap'),
(23, '2026-03-12T00:00:00.000Z', N'Return a regulator-facing systemic risk dashboard.', N'Systemic Dashboard', N'SYSTEMIC_DASHBOARD', CAST(1 AS bit), N'{"periodCode":"string?"}', CAST(1 AS bit), N'PROFILE', 1, N'Call systemic risk dashboard service -> return cross-entity KPIs, alerts, and component scores.', N'SD_SYSTEMIC_DASHBOARD', '2026-03-12T00:00:00.000Z', N'dashboard'),
(24, '2026-03-12T00:00:00.000Z', N'Run contagion analysis for a named institution or scenario.', N'Contagion Analysis', N'CONTAGION_QUERY', CAST(1 AS bit), N'{"entityNames":"string[]","scenarioCode":"string?"}', CAST(1 AS bit), N'PROFILE', 1, N'Resolve entity and scenario context -> call contagion analysis service -> return systemic spillover view.', N'CQ_CONTAGION_ANALYSIS', '2026-03-12T00:00:00.000Z', N'network'),
(25, '2026-03-12T00:00:00.000Z', N'Return available stress scenarios or results for a named scenario.', N'Stress Scenarios', N'STRESS_SCENARIOS', CAST(1 AS bit), N'{"scenarioName":"string?","periodCode":"string?"}', CAST(1 AS bit), N'TABLE', 1, N'List available scenarios, or route a named scenario request to the stress-testing service.', N'SS_STRESS_SCENARIOS', '2026-03-12T00:00:00.000Z', N'table'),
(26, '2026-03-12T00:00:00.000Z', N'Summarise sanctions-screening exposure for one or more entities.', N'Sanctions Exposure', N'SANCTIONS_EXPOSURE', CAST(1 AS bit), N'{"entityNames":"string[]?","licenceCategory":"string?"}', CAST(1 AS bit), N'TABLE', 1, N'Resolve entity scope -> query sanctions screening results -> aggregate counts and highest risk outcomes.', N'SX_SANCTIONS_EXPOSURE', '2026-03-12T00:00:00.000Z', N'table'),
(27, '2026-03-12T00:00:00.000Z', N'Generate a multi-source supervisory briefing for one entity.', N'Examination Briefing', N'EXAMINATION_BRIEF', CAST(1 AS bit), N'{"entityNames":"string[]"}', CAST(1 AS bit), N'PROFILE', 1, N'Resolve entity -> call regulator examination briefing service -> return composite focus areas and evidence.', N'EB_EXAMINATION_BRIEF', '2026-03-12T00:00:00.000Z', N'profile'),
(28, '2026-03-12T00:00:00.000Z', N'Return open, overdue, or recommended supervisory actions.', N'Supervisory Actions', N'SUPERVISORY_ACTIONS', CAST(1 AS bit), N'{"entityNames":"string[]?","status":"string?"}', CAST(1 AS bit), N'TABLE', 1, N'Call supervisory action or dashboard services -> return action backlog and status indicators.', N'SV_SUPERVISORY_ACTIONS', '2026-03-12T00:00:00.000Z', N'table'),
(29, '2026-03-12T00:00:00.000Z', N'Return pan-African group, harmonisation, or divergence intelligence.', N'Cross-Border Intelligence', N'CROSS_BORDER', CAST(1 AS bit), N'{"entityNames":"string[]?","jurisdiction":"string?"}', CAST(1 AS bit), N'TABLE', 1, N'Resolve group or jurisdiction scope -> call cross-border dashboard services -> return summary tables and indicators.', N'CB_CROSS_BORDER', '2026-03-12T00:00:00.000Z', N'table'),
(30, '2026-03-12T00:00:00.000Z', N'Return policy simulation scenarios and their institution or sector impacts.', N'Policy Impact', N'POLICY_IMPACT', CAST(1 AS bit), N'{"scenarioName":"string?","licenceCategory":"string?"}', CAST(1 AS bit), N'TABLE', 1, N'Resolve policy scenario request -> call policy simulation services -> return impact outputs.', N'PI_POLICY_IMPACT', '2026-03-12T00:00:00.000Z', N'barChart'),
(31, '2026-03-12T00:00:00.000Z', N'Aggregate recurring validation errors across supervised institutions.', N'Validation Hotspot', N'VALIDATION_HOTSPOT', CAST(1 AS bit), N'{"fieldCode":"string?","licenceCategory":"string?","periodCode":"string?","limit":"int"}', CAST(1 AS bit), N'TABLE', 1, N'Cross-tenant validation errors -> group by rule, field, or institution -> return hotspot counts.', N'VH_VALIDATION_HOTSPOT', '2026-03-12T00:00:00.000Z', N'heatmap');
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CreatedAt', N'Description', N'DisplayName', N'IntentCode', N'IsActive', N'ParameterSchema', N'RequiresRegulatorContext', N'ResultFormat', N'SortOrder', N'TemplateBody', N'TemplateCode', N'UpdatedAt', N'VisualizationType') AND [object_id] = OBJECT_ID(N'[meta].[complianceiq_templates]'))
    SET IDENTITY_INSERT [meta].[complianceiq_templates] OFF;
GO

CREATE INDEX [IX_complianceiq_config_lookup] ON [meta].[complianceiq_config] ([ConfigKey], [EffectiveTo]);
GO

CREATE INDEX [IX_complianceiq_conversations_tenant_user] ON [meta].[complianceiq_conversations] ([TenantId], [UserId], [LastActivityAt]);
GO

CREATE INDEX [IX_complianceiq_feedback_turn_user] ON [meta].[complianceiq_feedback] ([TurnId], [UserId]);
GO

CREATE INDEX [IX_complianceiq_field_synonyms_field] ON [meta].[complianceiq_field_synonyms] ([FieldCode], [ModuleCode]);
GO

CREATE INDEX [IX_complianceiq_field_synonyms_lookup] ON [meta].[complianceiq_field_synonyms] ([Synonym], [IsActive]);
GO

CREATE UNIQUE INDEX [UX_complianceiq_intents_code] ON [meta].[complianceiq_intents] ([IntentCode]);
GO

CREATE INDEX [IX_complianceiq_quick_questions_lookup] ON [meta].[complianceiq_quick_questions] ([RequiresRegulatorContext], [IsActive], [SortOrder]);
GO

CREATE INDEX [IX_complianceiq_templates_intent] ON [meta].[complianceiq_templates] ([IntentCode], [IsActive]);
GO

CREATE UNIQUE INDEX [UX_complianceiq_templates_code] ON [meta].[complianceiq_templates] ([TemplateCode]);
GO

CREATE INDEX [IX_complianceiq_turns_conversation] ON [meta].[complianceiq_turns] ([ConversationId], [TurnNumber]);
GO

CREATE INDEX [IX_complianceiq_turns_tenant] ON [meta].[complianceiq_turns] ([TenantId], [CreatedAt]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260314090510_AddComplianceIqSchema', N'8.0.11');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [meta].[regiq_access_log] (
    [Id] bigint NOT NULL IDENTITY,
    [RegulatorTenantId] uniqueidentifier NOT NULL,
    [ConversationId] uniqueidentifier NULL,
    [TurnId] int NULL,
    [RegulatorId] nvarchar(100) NOT NULL,
    [RegulatorAgency] varchar(10) NOT NULL,
    [RegulatorRole] varchar(40) NOT NULL,
    [QueryText] nvarchar(max) NOT NULL,
    [ResponseSummary] nvarchar(1000) NOT NULL,
    [ClassificationLevel] varchar(20) NOT NULL,
    [EntitiesAccessedJson] nvarchar(max) NOT NULL,
    [PrimaryEntityTenantId] uniqueidentifier NULL,
    [DataSourcesAccessedJson] nvarchar(max) NOT NULL,
    [FilterContextJson] nvarchar(max) NULL,
    [IpAddress] varchar(45) NULL,
    [SessionId] varchar(100) NULL,
    [AccessedAt] datetime2(3) NOT NULL,
    [RetainUntil] datetime2(3) NOT NULL,
    CONSTRAINT [PK_regiq_access_log] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_regiq_access_log_classification] CHECK ([ClassificationLevel] IN ('UNCLASSIFIED','RESTRICTED','CONFIDENTIAL')),
    CONSTRAINT [CK_regiq_access_log_data_sources_json] CHECK (ISJSON([DataSourcesAccessedJson]) = 1),
    CONSTRAINT [CK_regiq_access_log_entities_json] CHECK (ISJSON([EntitiesAccessedJson]) = 1),
    CONSTRAINT [FK_regiq_access_log_tenants_RegulatorTenantId] FOREIGN KEY ([RegulatorTenantId]) REFERENCES [tenants] ([TenantId]) ON DELETE NO ACTION
);
GO

CREATE TABLE [meta].[regiq_config] (
    [Id] int NOT NULL IDENTITY,
    [ConfigKey] varchar(100) NOT NULL,
    [ConfigValue] nvarchar(max) NOT NULL,
    [Description] nvarchar(500) NOT NULL,
    [EffectiveFrom] datetime2(3) NOT NULL,
    [EffectiveTo] datetime2(3) NULL,
    [CreatedBy] varchar(100) NOT NULL,
    [CreatedAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_regiq_config] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [meta].[regiq_conversation] (
    [Id] uniqueidentifier NOT NULL,
    [RegulatorTenantId] uniqueidentifier NOT NULL,
    [RegulatorId] nvarchar(100) NOT NULL,
    [RegulatorRole] varchar(40) NOT NULL,
    [RegulatorAgency] varchar(10) NOT NULL,
    [ClassificationLevel] varchar(20) NOT NULL,
    [Scope] varchar(20) NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [StartedAt] datetime2(3) NOT NULL,
    [LastActivityAt] datetime2(3) NOT NULL,
    [TurnCount] int NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_regiq_conversation] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_regiq_conversation_classification] CHECK ([ClassificationLevel] IN ('UNCLASSIFIED','RESTRICTED','CONFIDENTIAL')),
    CONSTRAINT [CK_regiq_conversation_scope] CHECK ([Scope] IN ('SECTOR_WIDE','ENTITY_SPECIFIC','COMPARATIVE','SYSTEMIC','HELP')),
    CONSTRAINT [FK_regiq_conversation_tenants_RegulatorTenantId] FOREIGN KEY ([RegulatorTenantId]) REFERENCES [tenants] ([TenantId]) ON DELETE NO ACTION
);
GO

CREATE TABLE [meta].[regiq_intent] (
    [Id] int NOT NULL IDENTITY,
    [IntentCode] varchar(40) NOT NULL,
    [Category] varchar(40) NOT NULL,
    [DisplayName] nvarchar(120) NOT NULL,
    [Description] nvarchar(500) NOT NULL,
    [ExampleQuery] nvarchar(250) NOT NULL,
    [PrimaryDataSource] varchar(40) NOT NULL,
    [RequiresRegulatorContext] bit NOT NULL,
    [IsEnabled] bit NOT NULL,
    [SortOrder] int NOT NULL,
    [CreatedAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_regiq_intent] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [meta].[regiq_query_template] (
    [Id] int NOT NULL IDENTITY,
    [IntentCode] varchar(40) NOT NULL,
    [TemplateCode] varchar(80) NOT NULL,
    [DisplayName] nvarchar(150) NOT NULL,
    [Description] nvarchar(500) NOT NULL,
    [SqlTemplate] nvarchar(max) NOT NULL,
    [ParameterSchema] nvarchar(max) NOT NULL,
    [ResultFormat] varchar(20) NOT NULL,
    [VisualizationType] varchar(30) NOT NULL,
    [Scope] varchar(20) NOT NULL,
    [ClassificationLevel] varchar(20) NOT NULL,
    [DataSourcesJson] nvarchar(max) NOT NULL,
    [CrossTenantEnabled] bit NOT NULL,
    [RequiresEntityContext] bit NOT NULL,
    [IsActive] bit NOT NULL,
    [SortOrder] int NOT NULL,
    [CreatedAt] datetime2(3) NOT NULL,
    [UpdatedAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_regiq_query_template] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_regiq_query_template_classification] CHECK ([ClassificationLevel] IN ('UNCLASSIFIED','RESTRICTED','CONFIDENTIAL')),
    CONSTRAINT [CK_regiq_query_template_data_sources_json] CHECK (ISJSON([DataSourcesJson]) = 1),
    CONSTRAINT [CK_regiq_query_template_parameter_schema_json] CHECK (ISJSON([ParameterSchema]) = 1),
    CONSTRAINT [CK_regiq_query_template_scope] CHECK ([Scope] IN ('SECTOR_WIDE','ENTITY_SPECIFIC','COMPARATIVE','SYSTEMIC','HELP'))
);
GO

CREATE TABLE [meta].[regulatoriq_entity_aliases] (
    [Id] bigint NOT NULL IDENTITY,
    [TenantId] uniqueidentifier NULL,
    [CanonicalName] nvarchar(200) NOT NULL,
    [Alias] nvarchar(200) NOT NULL,
    [NormalizedAlias] varchar(200) NOT NULL,
    [AliasType] varchar(30) NOT NULL DEFAULT 'NAME',
    [LicenceCategory] varchar(50) NOT NULL,
    [RegulatorAgency] varchar(10) NOT NULL,
    [InstitutionType] varchar(20) NOT NULL,
    [HoldingCompanyName] nvarchar(200) NULL,
    [GeoTag] nvarchar(100) NULL,
    [MatchPriority] int NOT NULL,
    [IsPrimary] bit NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_regulatoriq_entity_aliases] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_regulatoriq_entity_aliases_alias_type] CHECK ([AliasType] IN ('NAME','ABBREVIATION','HOLDING_COMPANY','COMMON')),
    CONSTRAINT [FK_regulatoriq_entity_aliases_tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [tenants] ([TenantId]) ON DELETE SET NULL
);
GO

CREATE TABLE [meta].[regiq_turn] (
    [Id] int NOT NULL IDENTITY,
    [ConversationId] uniqueidentifier NOT NULL,
    [RegulatorTenantId] uniqueidentifier NOT NULL,
    [RegulatorId] nvarchar(100) NOT NULL,
    [RegulatorRole] varchar(40) NOT NULL,
    [TurnNumber] int NOT NULL,
    [QueryText] nvarchar(max) NOT NULL,
    [IntentCode] varchar(40) NOT NULL,
    [IntentConfidence] decimal(5,4) NOT NULL,
    [ExtractedEntitiesJson] nvarchar(max) NOT NULL,
    [TemplateCode] varchar(80) NOT NULL,
    [ResolvedParametersJson] nvarchar(max) NOT NULL,
    [ExecutedPlan] nvarchar(max) NOT NULL,
    [RowCount] int NOT NULL,
    [ExecutionTimeMs] int NOT NULL,
    [ResponseText] nvarchar(max) NOT NULL,
    [ResponseDataJson] nvarchar(max) NOT NULL,
    [VisualizationType] varchar(30) NOT NULL,
    [ConfidenceLevel] varchar(10) NOT NULL,
    [CitationsJson] nvarchar(max) NOT NULL,
    [FollowUpSuggestionsJson] nvarchar(max) NOT NULL,
    [TotalTimeMs] int NOT NULL,
    [EntitiesQueriedJson] nvarchar(max) NOT NULL,
    [DataSourcesAccessedJson] nvarchar(max) NOT NULL,
    [ClassificationLevel] varchar(20) NOT NULL,
    [RegulatorAgencyFilterApplied] varchar(10) NULL,
    [PrimaryEntityTenantId] uniqueidentifier NULL,
    [ErrorMessage] nvarchar(500) NULL,
    [CreatedAt] datetime2(3) NOT NULL,
    CONSTRAINT [PK_regiq_turn] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_regiq_turn_citations_json] CHECK (ISJSON([CitationsJson]) = 1),
    CONSTRAINT [CK_regiq_turn_classification] CHECK ([ClassificationLevel] IN ('UNCLASSIFIED','RESTRICTED','CONFIDENTIAL')),
    CONSTRAINT [CK_regiq_turn_data_sources_json] CHECK (ISJSON([DataSourcesAccessedJson]) = 1),
    CONSTRAINT [CK_regiq_turn_entities_queried_json] CHECK (ISJSON([EntitiesQueriedJson]) = 1),
    CONSTRAINT [CK_regiq_turn_extracted_entities_json] CHECK (ISJSON([ExtractedEntitiesJson]) = 1),
    CONSTRAINT [CK_regiq_turn_followups_json] CHECK (ISJSON([FollowUpSuggestionsJson]) = 1),
    CONSTRAINT [CK_regiq_turn_resolved_parameters_json] CHECK (ISJSON([ResolvedParametersJson]) = 1),
    CONSTRAINT [CK_regiq_turn_response_data_json] CHECK (ISJSON([ResponseDataJson]) = 1),
    CONSTRAINT [FK_regiq_turn_regiq_conversation_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [meta].[regiq_conversation] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_regiq_turn_tenants_RegulatorTenantId] FOREIGN KEY ([RegulatorTenantId]) REFERENCES [tenants] ([TenantId]) ON DELETE NO ACTION
);
GO

CREATE INDEX [IX_regiq_access_log_primary_entity] ON [meta].[regiq_access_log] ([PrimaryEntityTenantId]);
GO

CREATE INDEX [IX_regiq_access_log_regulator] ON [meta].[regiq_access_log] ([RegulatorTenantId], [AccessedAt]);
GO

CREATE INDEX [IX_regiq_access_log_retention] ON [meta].[regiq_access_log] ([RetainUntil]);
GO

CREATE INDEX [IX_regiq_config_lookup] ON [meta].[regiq_config] ([ConfigKey], [EffectiveTo]);
GO

CREATE INDEX [IX_regiq_conversation_regulator] ON [meta].[regiq_conversation] ([RegulatorTenantId], [RegulatorId], [LastActivityAt]);
GO

CREATE UNIQUE INDEX [UX_regiq_intent_code] ON [meta].[regiq_intent] ([IntentCode]);
GO

CREATE INDEX [IX_regiq_query_template_intent] ON [meta].[regiq_query_template] ([IntentCode], [IsActive]);
GO

CREATE UNIQUE INDEX [UX_regiq_query_template_code] ON [meta].[regiq_query_template] ([TemplateCode]);
GO

CREATE INDEX [IX_regiq_turn_conversation] ON [meta].[regiq_turn] ([ConversationId], [TurnNumber]);
GO

CREATE INDEX [IX_regiq_turn_primary_entity] ON [meta].[regiq_turn] ([PrimaryEntityTenantId]);
GO

CREATE INDEX [IX_regiq_turn_regulator] ON [meta].[regiq_turn] ([RegulatorTenantId], [CreatedAt]);
GO

CREATE INDEX [IX_regulatoriq_entity_aliases_lookup] ON [meta].[regulatoriq_entity_aliases] ([NormalizedAlias], [IsActive]);
GO

CREATE INDEX [IX_regulatoriq_entity_aliases_regulator] ON [meta].[regulatoriq_entity_aliases] ([RegulatorAgency], [LicenceCategory]);
GO

CREATE INDEX [IX_regulatoriq_entity_aliases_tenant] ON [meta].[regulatoriq_entity_aliases] ([TenantId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260314093051_AddRegulatorIqSchema', N'8.0.11');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ConfigKey', N'ConfigValue', N'CreatedAt', N'CreatedBy', N'Description', N'EffectiveFrom', N'EffectiveTo') AND [object_id] = OBJECT_ID(N'[meta].[regiq_config]'))
    SET IDENTITY_INSERT [meta].[regiq_config] ON;
INSERT INTO [meta].[regiq_config] ([Id], [ConfigKey], [ConfigValue], [CreatedAt], [CreatedBy], [Description], [EffectiveFrom], [EffectiveTo])
VALUES (1, 'rate.queries_per_minute', N'30', '2026-03-14T00:00:00.000Z', 'SYSTEM', N'Maximum RegulatorIQ queries per regulator per minute.', '2026-03-14T00:00:00.000Z', NULL),
(2, 'llm.model', N'claude-3-5-sonnet-latest', '2026-03-14T00:00:00.000Z', 'SYSTEM', N'Default model used by RegulatorIQ for LLM-assisted analysis.', '2026-03-14T00:00:00.000Z', NULL),
(3, 'llm.temperature', N'0.1', '2026-03-14T00:00:00.000Z', 'SYSTEM', N'Default RegulatorIQ response temperature.', '2026-03-14T00:00:00.000Z', NULL);
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ConfigKey', N'ConfigValue', N'CreatedAt', N'CreatedBy', N'Description', N'EffectiveFrom', N'EffectiveTo') AND [object_id] = OBJECT_ID(N'[meta].[regiq_config]'))
    SET IDENTITY_INSERT [meta].[regiq_config] OFF;
GO

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Category', N'CreatedAt', N'Description', N'DisplayName', N'ExampleQuery', N'IntentCode', N'IsEnabled', N'PrimaryDataSource', N'RequiresRegulatorContext', N'SortOrder') AND [object_id] = OBJECT_ID(N'[meta].[regiq_intent]'))
    SET IDENTITY_INSERT [meta].[regiq_intent] ON;
INSERT INTO [meta].[regiq_intent] ([Id], [Category], [CreatedAt], [Description], [DisplayName], [ExampleQuery], [IntentCode], [IsEnabled], [PrimaryDataSource], [RequiresRegulatorContext], [SortOrder])
VALUES (1, 'REGULATOR', '2026-03-14T00:00:00.000Z', N'Build a regulator-facing profile for a supervised institution.', N'Entity Intelligence Profile', N'Give me a full profile of Access Bank', 'ENTITY_PROFILE', CAST(1 AS bit), 'MULTI', CAST(1 AS bit), 1),
(2, 'REGULATOR', '2026-03-14T00:00:00.000Z', N'Summarise cross-entity sector intelligence for the current scope.', N'Sector Intelligence Summary', N'Show me a sector health summary for commercial banks', 'SECTOR_SUMMARY', CAST(1 AS bit), 'MULTI', CAST(1 AS bit), 2),
(3, 'REGULATOR', '2026-03-14T00:00:00.000Z', N'Rank institutions by their latest Compliance Health Score.', N'Compliance Health Ranking', N'Rank DMBs by compliance health score', 'CHS_RANKING', CAST(1 AS bit), 'RG-32', CAST(1 AS bit), 3);
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Category', N'CreatedAt', N'Description', N'DisplayName', N'ExampleQuery', N'IntentCode', N'IsEnabled', N'PrimaryDataSource', N'RequiresRegulatorContext', N'SortOrder') AND [object_id] = OBJECT_ID(N'[meta].[regiq_intent]'))
    SET IDENTITY_INSERT [meta].[regiq_intent] OFF;
GO

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ClassificationLevel', N'CreatedAt', N'CrossTenantEnabled', N'DataSourcesJson', N'Description', N'DisplayName', N'IntentCode', N'IsActive', N'ParameterSchema', N'RequiresEntityContext', N'ResultFormat', N'Scope', N'SortOrder', N'SqlTemplate', N'TemplateCode', N'UpdatedAt', N'VisualizationType') AND [object_id] = OBJECT_ID(N'[meta].[regiq_query_template]'))
    SET IDENTITY_INSERT [meta].[regiq_query_template] ON;
INSERT INTO [meta].[regiq_query_template] ([Id], [ClassificationLevel], [CreatedAt], [CrossTenantEnabled], [DataSourcesJson], [Description], [DisplayName], [IntentCode], [IsActive], [ParameterSchema], [RequiresEntityContext], [ResultFormat], [Scope], [SortOrder], [SqlTemplate], [TemplateCode], [UpdatedAt], [VisualizationType])
VALUES (1, 'RESTRICTED', '2026-03-14T00:00:00.000Z', CAST(1 AS bit), N'["RG-32"]', N'Rank supervised institutions by their most recent Compliance Health Score.', N'Latest CHS Ranking', 'CHS_RANKING', CAST(1 AS bit), N'{"LicenceCategory":"string?","Limit":"int"}', CAST(0 AS bit), 'TABLE', 'SECTOR_WIDE', 1, CONCAT(CAST(N'WITH latest_chs AS (' AS nvarchar(max)), nchar(10), N'    SELECT', nchar(10), N'        s.TenantId,', nchar(10), N'        s.OverallScore,', nchar(10), N'        s.Rating,', nchar(10), N'        s.ComputedAt,', nchar(10), N'        ROW_NUMBER() OVER (PARTITION BY s.TenantId ORDER BY s.ComputedAt DESC) AS rn', nchar(10), N'    FROM chs_score_snapshots s', nchar(10), N')', nchar(10), N'SELECT TOP (@Limit)', nchar(10), N'    l.TenantId AS tenant_id,', nchar(10), N'    i.InstitutionName AS institution_name,', nchar(10), N'    COALESCE(licence.Code, i.LicenseType, '''') AS licence_category,', nchar(10), N'    CAST(l.OverallScore AS decimal(10,2)) AS chs_score,', nchar(10), N'    l.Rating AS rating,', nchar(10), N'    l.ComputedAt AS computed_at', nchar(10), N'FROM latest_chs l', nchar(10), N'INNER JOIN institutions i ON i.TenantId = l.TenantId', nchar(10), N'OUTER APPLY (', nchar(10), N'    SELECT TOP (1) lt.Code', nchar(10), N'    FROM tenant_licence_types tlt', nchar(10), N'    INNER JOIN licence_types lt ON lt.Id = tlt.LicenceTypeId', nchar(10), N'    WHERE tlt.TenantId = l.TenantId', nchar(10), N'      AND tlt.IsActive = 1', nchar(10), N'    ORDER BY tlt.EffectiveDate DESC, tlt.Id DESC', nchar(10), N') licence', nchar(10), N'WHERE l.rn = 1', nchar(10), N'  AND (@LicenceCategory IS NULL OR @LicenceCategory = '''' OR COALESCE(licence.Code, i.LicenseType, '''') = @LicenceCategory)', nchar(10), N'ORDER BY l.OverallScore DESC, i.InstitutionName ASC;'), 'CHS_RANKING_LATEST', '2026-03-14T00:00:00.000Z', 'ranking');
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ClassificationLevel', N'CreatedAt', N'CrossTenantEnabled', N'DataSourcesJson', N'Description', N'DisplayName', N'IntentCode', N'IsActive', N'ParameterSchema', N'RequiresEntityContext', N'ResultFormat', N'Scope', N'SortOrder', N'SqlTemplate', N'TemplateCode', N'UpdatedAt', N'VisualizationType') AND [object_id] = OBJECT_ID(N'[meta].[regiq_query_template]'))
    SET IDENTITY_INSERT [meta].[regiq_query_template] OFF;
GO

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Alias', N'AliasType', N'CanonicalName', N'CreatedAt', N'GeoTag', N'HoldingCompanyName', N'InstitutionType', N'IsActive', N'IsPrimary', N'LicenceCategory', N'MatchPriority', N'NormalizedAlias', N'RegulatorAgency', N'TenantId') AND [object_id] = OBJECT_ID(N'[meta].[regulatoriq_entity_aliases]'))
    SET IDENTITY_INSERT [meta].[regulatoriq_entity_aliases] ON;
INSERT INTO [meta].[regulatoriq_entity_aliases] ([Id], [Alias], [AliasType], [CanonicalName], [CreatedAt], [GeoTag], [HoldingCompanyName], [InstitutionType], [IsActive], [IsPrimary], [LicenceCategory], [MatchPriority], [NormalizedAlias], [RegulatorAgency], [TenantId])
VALUES (CAST(1 AS bigint), N'Access Bank Plc', 'NAME', N'Access Bank Plc', '2026-03-14T00:00:00.000Z', N'NG', N'Access Holdings Plc', 'BANK', CAST(1 AS bit), CAST(1 AS bit), 'DMB', 10, 'access bank plc', 'CBN', NULL),
(CAST(2 AS bigint), N'Access Bank', 'COMMON', N'Access Bank Plc', '2026-03-14T00:00:00.000Z', N'NG', N'Access Holdings Plc', 'BANK', CAST(1 AS bit), CAST(0 AS bit), 'DMB', 20, 'access bank', 'CBN', NULL),
(CAST(3 AS bigint), N'Access', 'COMMON', N'Access Bank Plc', '2026-03-14T00:00:00.000Z', N'NG', N'Access Holdings Plc', 'BANK', CAST(1 AS bit), CAST(0 AS bit), 'DMB', 30, 'access', 'CBN', NULL),
(CAST(4 AS bigint), N'Zenith Bank Plc', 'NAME', N'Zenith Bank Plc', '2026-03-14T00:00:00.000Z', N'NG', NULL, 'BANK', CAST(1 AS bit), CAST(1 AS bit), 'DMB', 10, 'zenith bank plc', 'CBN', NULL),
(CAST(5 AS bigint), N'Zenith Bank', 'COMMON', N'Zenith Bank Plc', '2026-03-14T00:00:00.000Z', N'NG', NULL, 'BANK', CAST(1 AS bit), CAST(0 AS bit), 'DMB', 20, 'zenith bank', 'CBN', NULL),
(CAST(6 AS bigint), N'Zenith', 'COMMON', N'Zenith Bank Plc', '2026-03-14T00:00:00.000Z', N'NG', NULL, 'BANK', CAST(1 AS bit), CAST(0 AS bit), 'DMB', 30, 'zenith', 'CBN', NULL),
(CAST(7 AS bigint), N'Guaranty Trust Bank Plc', 'NAME', N'Guaranty Trust Bank Plc', '2026-03-14T00:00:00.000Z', N'NG', N'GTCO Plc', 'BANK', CAST(1 AS bit), CAST(1 AS bit), 'DMB', 10, 'guaranty trust bank plc', 'CBN', NULL),
(CAST(8 AS bigint), N'GTBank', 'COMMON', N'Guaranty Trust Bank Plc', '2026-03-14T00:00:00.000Z', N'NG', N'GTCO Plc', 'BANK', CAST(1 AS bit), CAST(0 AS bit), 'DMB', 20, 'gtbank', 'CBN', NULL),
(CAST(9 AS bigint), N'GT Bank', 'COMMON', N'Guaranty Trust Bank Plc', '2026-03-14T00:00:00.000Z', N'NG', N'GTCO Plc', 'BANK', CAST(1 AS bit), CAST(0 AS bit), 'DMB', 30, 'gt bank', 'CBN', NULL),
(CAST(10 AS bigint), N'Guaranty Trust', 'COMMON', N'Guaranty Trust Bank Plc', '2026-03-14T00:00:00.000Z', N'NG', N'GTCO Plc', 'BANK', CAST(1 AS bit), CAST(0 AS bit), 'DMB', 40, 'guaranty trust', 'CBN', NULL),
(CAST(11 AS bigint), N'GTCO', 'HOLDING_COMPANY', N'Guaranty Trust Bank Plc', '2026-03-14T00:00:00.000Z', N'NG', N'GTCO Plc', 'BANK', CAST(1 AS bit), CAST(0 AS bit), 'DMB', 50, 'gtco', 'CBN', NULL),
(CAST(12 AS bigint), N'First Bank Nigeria Limited', 'NAME', N'First Bank Nigeria Limited', '2026-03-14T00:00:00.000Z', N'NG', N'FBN Holdings Plc', 'BANK', CAST(1 AS bit), CAST(1 AS bit), 'DMB', 10, 'first bank nigeria limited', 'CBN', NULL),
(CAST(13 AS bigint), N'First Bank', 'COMMON', N'First Bank Nigeria Limited', '2026-03-14T00:00:00.000Z', N'NG', N'FBN Holdings Plc', 'BANK', CAST(1 AS bit), CAST(0 AS bit), 'DMB', 20, 'first bank', 'CBN', NULL),
(CAST(14 AS bigint), N'First', 'COMMON', N'First Bank Nigeria Limited', '2026-03-14T00:00:00.000Z', N'NG', N'FBN Holdings Plc', 'BANK', CAST(1 AS bit), CAST(0 AS bit), 'DMB', 30, 'first', 'CBN', NULL),
(CAST(15 AS bigint), N'FBN', 'ABBREVIATION', N'First Bank Nigeria Limited', '2026-03-14T00:00:00.000Z', N'NG', N'FBN Holdings Plc', 'BANK', CAST(1 AS bit), CAST(0 AS bit), 'DMB', 40, 'fbn', 'CBN', NULL),
(CAST(16 AS bigint), N'FBNH', 'HOLDING_COMPANY', N'First Bank Nigeria Limited', '2026-03-14T00:00:00.000Z', N'NG', N'FBN Holdings Plc', 'BANK', CAST(1 AS bit), CAST(0 AS bit), 'DMB', 50, 'fbnh', 'CBN', NULL),
(CAST(17 AS bigint), N'First City Monument Bank Plc', 'NAME', N'First City Monument Bank Plc', '2026-03-14T00:00:00.000Z', N'NG', N'FCMB Group Plc', 'BANK', CAST(1 AS bit), CAST(1 AS bit), 'DMB', 10, 'first city monument bank plc', 'CBN', NULL),
(CAST(18 AS bigint), N'FCMB', 'ABBREVIATION', N'First City Monument Bank Plc', '2026-03-14T00:00:00.000Z', N'NG', N'FCMB Group Plc', 'BANK', CAST(1 AS bit), CAST(0 AS bit), 'DMB', 20, 'fcmb', 'CBN', NULL),
(CAST(19 AS bigint), N'First City Monument', 'COMMON', N'First City Monument Bank Plc', '2026-03-14T00:00:00.000Z', N'NG', N'FCMB Group Plc', 'BANK', CAST(1 AS bit), CAST(0 AS bit), 'DMB', 30, 'first city monument', 'CBN', NULL),
(CAST(20 AS bigint), N'First', 'COMMON', N'First City Monument Bank Plc', '2026-03-14T00:00:00.000Z', N'NG', N'FCMB Group Plc', 'BANK', CAST(1 AS bit), CAST(0 AS bit), 'DMB', 40, 'first', 'CBN', NULL);
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Alias', N'AliasType', N'CanonicalName', N'CreatedAt', N'GeoTag', N'HoldingCompanyName', N'InstitutionType', N'IsActive', N'IsPrimary', N'LicenceCategory', N'MatchPriority', N'NormalizedAlias', N'RegulatorAgency', N'TenantId') AND [object_id] = OBJECT_ID(N'[meta].[regulatoriq_entity_aliases]'))
    SET IDENTITY_INSERT [meta].[regulatoriq_entity_aliases] OFF;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260314094845_AddRegulatorIqSeedData', N'8.0.11');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [meta].[complianceiq_turns] ADD [ClassificationLevel] nvarchar(20) NOT NULL DEFAULT N'RESTRICTED';
GO

ALTER TABLE [meta].[complianceiq_turns] ADD [DataSourcesUsed] nvarchar(500) NOT NULL DEFAULT N'';
GO

ALTER TABLE [meta].[complianceiq_turns] ADD [EntitiesAccessedJson] nvarchar(max) NOT NULL DEFAULT N'';
GO

ALTER TABLE [meta].[complianceiq_turns] ADD [RegulatorAgency] nvarchar(20) NULL;
GO

ALTER TABLE [meta].[complianceiq_conversations] ADD [ExaminationTargetTenantId] uniqueidentifier NULL;
GO

ALTER TABLE [meta].[complianceiq_conversations] ADD [IsExaminationSession] bit NOT NULL DEFAULT CAST(0 AS bit);
GO

ALTER TABLE [meta].[complianceiq_conversations] ADD [Scope] nvarchar(20) NULL;
GO

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ConfigKey', N'ConfigValue', N'CreatedAt', N'CreatedBy', N'Description', N'EffectiveFrom', N'EffectiveTo') AND [object_id] = OBJECT_ID(N'[meta].[complianceiq_config]'))
    SET IDENTITY_INSERT [meta].[complianceiq_config] ON;
INSERT INTO [meta].[complianceiq_config] ([Id], [ConfigKey], [ConfigValue], [CreatedAt], [CreatedBy], [Description], [EffectiveFrom], [EffectiveTo])
VALUES (10, N'rate.regulator_queries_per_minute', N'30', '2026-03-12T00:00:00.000Z', N'SYSTEM', N'Maximum RegulatorIQ queries per regulator per minute.', '2026-03-12T00:00:00.000Z', NULL),
(11, N'rate.regulator_queries_per_hour', N'300', '2026-03-12T00:00:00.000Z', N'SYSTEM', N'Maximum RegulatorIQ queries per regulator per hour.', '2026-03-12T00:00:00.000Z', NULL),
(12, N'rate.regulator_queries_per_day', N'1500', '2026-03-12T00:00:00.000Z', N'SYSTEM', N'Maximum RegulatorIQ queries per regulator per day.', '2026-03-12T00:00:00.000Z', NULL);
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ConfigKey', N'ConfigValue', N'CreatedAt', N'CreatedBy', N'Description', N'EffectiveFrom', N'EffectiveTo') AND [object_id] = OBJECT_ID(N'[meta].[complianceiq_config]'))
    SET IDENTITY_INSERT [meta].[complianceiq_config] OFF;
GO

CREATE INDEX [IX_complianceiq_turns_classification] ON [meta].[complianceiq_turns] ([ClassificationLevel], [CreatedAt]);
GO

ALTER TABLE [meta].[complianceiq_turns] ADD CONSTRAINT [CK_complianceiq_turns_classification] CHECK ([ClassificationLevel] IN ('UNCLASSIFIED','RESTRICTED','CONFIDENTIAL'));
GO

ALTER TABLE [meta].[complianceiq_turns] ADD CONSTRAINT [CK_complianceiq_turns_entities_json] CHECK (ISJSON([EntitiesAccessedJson]) = 1);
GO

CREATE INDEX [IX_complianceiq_conversations_exam_target] ON [meta].[complianceiq_conversations] ([ExaminationTargetTenantId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260314115733_AddRegulatorIqExtensions', N'8.0.11');
GO

COMMIT;
GO

