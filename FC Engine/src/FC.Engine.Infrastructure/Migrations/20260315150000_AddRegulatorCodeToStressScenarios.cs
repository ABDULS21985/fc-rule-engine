using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FC.Engine.Infrastructure.Migrations;

[DbContext(typeof(MetadataDbContext))]
[Migration("20260315150000_AddRegulatorCodeToStressScenarios")]
/// <summary>
/// Adds optional RegulatorCode scoping to StressScenarios so that each regulatory
/// body can maintain its own private scenario catalogue alongside the shared
/// platform-level scenarios (RegulatorCode IS NULL = platform-wide / visible to all).
///
/// Existing rows are left with RegulatorCode = NULL which preserves backward
/// compatibility — the MacroPrudential page shows NULL-coded scenarios to every regulator
/// and additionally shows the regulator's own private scenarios.
/// </summary>
public partial class AddRegulatorCodeToStressScenarios : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Add column (nullable so existing rows are unaffected)
        migrationBuilder.Sql("""
IF NOT EXISTS (
    SELECT 1
    FROM   sys.columns c
    JOIN   sys.tables  t ON t.object_id = c.object_id
    WHERE  t.name    = 'StressScenarios'
      AND  c.name    = 'RegulatorCode'
)
BEGIN
    ALTER TABLE StressScenarios
        ADD RegulatorCode VARCHAR(10) NULL;
END
""");

        // Index to support per-regulator scenario catalogue queries efficiently
        migrationBuilder.Sql("""
IF NOT EXISTS (
    SELECT 1
    FROM   sys.indexes
    WHERE  name      = 'IX_StressScenarios_RegulatorCode'
      AND  object_id = OBJECT_ID('StressScenarios')
)
BEGIN
    CREATE INDEX IX_StressScenarios_RegulatorCode
        ON StressScenarios (RegulatorCode, IsActive, Category);
END
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
IF EXISTS (
    SELECT 1
    FROM   sys.indexes
    WHERE  name      = 'IX_StressScenarios_RegulatorCode'
      AND  object_id = OBJECT_ID('StressScenarios')
)
    DROP INDEX IX_StressScenarios_RegulatorCode ON StressScenarios;
""");

        migrationBuilder.Sql("""
IF EXISTS (
    SELECT 1
    FROM   sys.columns c
    JOIN   sys.tables  t ON t.object_id = c.object_id
    WHERE  t.name = 'StressScenarios' AND c.name = 'RegulatorCode'
)
BEGIN
    ALTER TABLE StressScenarios DROP COLUMN RegulatorCode;
END
""");
    }
}
