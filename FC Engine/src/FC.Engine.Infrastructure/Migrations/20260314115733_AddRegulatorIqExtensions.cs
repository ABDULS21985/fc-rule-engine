using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FC.Engine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRegulatorIqExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClassificationLevel",
                schema: "meta",
                table: "complianceiq_turns",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "RESTRICTED");

            migrationBuilder.AddColumn<string>(
                name: "DataSourcesUsed",
                schema: "meta",
                table: "complianceiq_turns",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EntitiesAccessedJson",
                schema: "meta",
                table: "complianceiq_turns",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RegulatorAgency",
                schema: "meta",
                table: "complianceiq_turns",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExaminationTargetTenantId",
                schema: "meta",
                table: "complianceiq_conversations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsExaminationSession",
                schema: "meta",
                table: "complianceiq_conversations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Scope",
                schema: "meta",
                table: "complianceiq_conversations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.InsertData(
                schema: "meta",
                table: "complianceiq_config",
                columns: new[] { "Id", "ConfigKey", "ConfigValue", "CreatedAt", "CreatedBy", "Description", "EffectiveFrom", "EffectiveTo" },
                values: new object[,]
                {
                    { 10, "rate.regulator_queries_per_minute", "30", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "SYSTEM", "Maximum RegulatorIQ queries per regulator per minute.", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { 11, "rate.regulator_queries_per_hour", "300", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "SYSTEM", "Maximum RegulatorIQ queries per regulator per hour.", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { 12, "rate.regulator_queries_per_day", "1500", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "SYSTEM", "Maximum RegulatorIQ queries per regulator per day.", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_complianceiq_turns_classification",
                schema: "meta",
                table: "complianceiq_turns",
                columns: new[] { "ClassificationLevel", "CreatedAt" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_complianceiq_turns_classification",
                schema: "meta",
                table: "complianceiq_turns",
                sql: "[ClassificationLevel] IN ('UNCLASSIFIED','RESTRICTED','CONFIDENTIAL')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_complianceiq_turns_entities_json",
                schema: "meta",
                table: "complianceiq_turns",
                sql: "ISJSON([EntitiesAccessedJson]) = 1");

            migrationBuilder.CreateIndex(
                name: "IX_complianceiq_conversations_exam_target",
                schema: "meta",
                table: "complianceiq_conversations",
                column: "ExaminationTargetTenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_complianceiq_turns_classification",
                schema: "meta",
                table: "complianceiq_turns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_complianceiq_turns_classification",
                schema: "meta",
                table: "complianceiq_turns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_complianceiq_turns_entities_json",
                schema: "meta",
                table: "complianceiq_turns");

            migrationBuilder.DropIndex(
                name: "IX_complianceiq_conversations_exam_target",
                schema: "meta",
                table: "complianceiq_conversations");

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "complianceiq_config",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "complianceiq_config",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "complianceiq_config",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.DropColumn(
                name: "ClassificationLevel",
                schema: "meta",
                table: "complianceiq_turns");

            migrationBuilder.DropColumn(
                name: "DataSourcesUsed",
                schema: "meta",
                table: "complianceiq_turns");

            migrationBuilder.DropColumn(
                name: "EntitiesAccessedJson",
                schema: "meta",
                table: "complianceiq_turns");

            migrationBuilder.DropColumn(
                name: "RegulatorAgency",
                schema: "meta",
                table: "complianceiq_turns");

            migrationBuilder.DropColumn(
                name: "ExaminationTargetTenantId",
                schema: "meta",
                table: "complianceiq_conversations");

            migrationBuilder.DropColumn(
                name: "IsExaminationSession",
                schema: "meta",
                table: "complianceiq_conversations");

            migrationBuilder.DropColumn(
                name: "Scope",
                schema: "meta",
                table: "complianceiq_conversations");
        }
    }
}
