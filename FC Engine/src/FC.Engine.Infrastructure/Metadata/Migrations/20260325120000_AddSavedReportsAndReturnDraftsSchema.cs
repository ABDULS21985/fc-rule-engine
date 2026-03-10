using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FC.Engine.Infrastructure.Metadata.Migrations;

[DbContext(typeof(MetadataDbContext))]
[Migration("20260325120000_AddSavedReportsAndReturnDraftsSchema")]
public partial class AddSavedReportsAndReturnDraftsSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "return_drafts",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                InstitutionId = table.Column<int>(type: "int", nullable: false),
                ReturnCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Period = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                DataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                LastSavedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                SavedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_return_drafts", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_return_drafts_tenant_last_saved",
            table: "return_drafts",
            columns: new[] { "TenantId", "LastSavedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_return_drafts_unique_scope",
            table: "return_drafts",
            columns: new[] { "TenantId", "InstitutionId", "ReturnCode", "Period" },
            unique: true);

        migrationBuilder.CreateTable(
            name: "saved_reports",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                InstitutionId = table.Column<int>(type: "int", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                Definition = table.Column<string>(type: "nvarchar(max)", nullable: false),
                IsShared = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                ScheduleCron = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                ScheduleFormat = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                ScheduleRecipients = table.Column<string>(type: "nvarchar(max)", nullable: true),
                IsScheduleActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                LastRunAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_saved_reports", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_saved_reports_tenant",
            table: "saved_reports",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_saved_reports_tenant_institution",
            table: "saved_reports",
            columns: new[] { "TenantId", "InstitutionId" });

        migrationBuilder.CreateIndex(
            name: "IX_saved_reports_schedule",
            table: "saved_reports",
            columns: new[] { "IsScheduleActive", "LastRunAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "saved_reports");
        migrationBuilder.DropTable(name: "return_drafts");
    }
}
