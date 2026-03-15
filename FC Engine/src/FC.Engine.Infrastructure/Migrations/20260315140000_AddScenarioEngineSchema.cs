using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FC.Engine.Infrastructure.Migrations;

[DbContext(typeof(MetadataDbContext))]
[Migration("20260315140000_AddScenarioEngineSchema")]
/// <summary>
/// Persisted Scenario Simulation Engine schema.
/// Creates meta.scenario_definitions (regulator-scoped sandbox scenarios) and
/// meta.scenario_results (cached computation output per scenario run).
/// Nested collections (Overrides, MacroShocks, AffectedModules, KeyMetrics,
/// Breaches, ProFormaFields) are stored as NVARCHAR(MAX) JSON.
/// </summary>
public partial class AddScenarioEngineSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'meta')
BEGIN
    EXEC('CREATE SCHEMA meta');
END
""");

        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM sys.tables t
               JOIN  sys.schemas  s ON s.schema_id = t.schema_id
               WHERE s.name = 'meta' AND t.name = 'scenario_definitions')
BEGIN
    CREATE TABLE meta.scenario_definitions (
        Id                  INT IDENTITY(1,1)   PRIMARY KEY,
        RegulatorCode       VARCHAR(10)         NOT NULL,
        Name                NVARCHAR(200)       NOT NULL,
        Description         NVARCHAR(2000)      NULL,
        TemplateId          VARCHAR(80)         NULL,
        Status              VARCHAR(20)         NOT NULL DEFAULT 'Draft',
        Scope               VARCHAR(30)         NOT NULL DEFAULT 'Single',
        OverridesJson       NVARCHAR(MAX)       NOT NULL DEFAULT '[]',
        MacroShocksJson     NVARCHAR(MAX)       NOT NULL DEFAULT '[]',
        AffectedModulesJson NVARCHAR(MAX)       NOT NULL DEFAULT '[]',
        CreatedAt           DATETIME2(3)        NOT NULL DEFAULT SYSUTCDATETIME(),
        CompletedAt         DATETIME2(3)        NULL
    );

    CREATE INDEX IX_scenario_definitions_Regulator
        ON meta.scenario_definitions (RegulatorCode, CreatedAt DESC);
END
""");

        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM sys.tables t
               JOIN  sys.schemas  s ON s.schema_id = t.schema_id
               WHERE s.name = 'meta' AND t.name = 'scenario_results')
BEGIN
    CREATE TABLE meta.scenario_results (
        Id                  INT IDENTITY(1,1)   PRIMARY KEY,
        ScenarioId          INT                 NOT NULL,
        ScenarioName        NVARCHAR(200)       NOT NULL,
        RunAt               DATETIME2(3)        NOT NULL DEFAULT SYSUTCDATETIME(),
        DurationMs          BIGINT              NOT NULL DEFAULT 0,
        KeyMetricsJson      NVARCHAR(MAX)       NOT NULL DEFAULT '[]',
        BreachesJson        NVARCHAR(MAX)       NOT NULL DEFAULT '[]',
        ProFormaFieldsJson  NVARCHAR(MAX)       NOT NULL DEFAULT '[]',
        TotalFieldsAffected INT                 NOT NULL DEFAULT 0,
        FormulasRecomputed  INT                 NOT NULL DEFAULT 0,
        ValidationErrors    INT                 NOT NULL DEFAULT 0,
        ValidationWarnings  INT                 NOT NULL DEFAULT 0,

        CONSTRAINT FK_scenario_results_definition
            FOREIGN KEY (ScenarioId)
            REFERENCES meta.scenario_definitions(Id)
            ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX UX_scenario_results_ScenarioId
        ON meta.scenario_results (ScenarioId);
END
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS meta.scenario_results;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS meta.scenario_definitions;");
    }
}
