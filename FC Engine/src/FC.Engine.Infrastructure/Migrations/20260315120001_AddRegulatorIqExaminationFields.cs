using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FC.Engine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRegulatorIqExaminationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ExaminationTargetTenantId",
                schema: "meta",
                table: "regiq_conversation",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsExaminationSession",
                schema: "meta",
                table: "regiq_conversation",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.DropCheckConstraint(
                name: "CK_regiq_conversation_scope",
                schema: "meta",
                table: "regiq_conversation");

            migrationBuilder.AddCheckConstraint(
                name: "CK_regiq_conversation_scope",
                schema: "meta",
                table: "regiq_conversation",
                sql: "[Scope] IN ('SECTOR','ENTITY','SYSTEMIC','SECTOR_WIDE','ENTITY_SPECIFIC','COMPARATIVE','HELP')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_regiq_conversation_scope",
                schema: "meta",
                table: "regiq_conversation");

            migrationBuilder.AddCheckConstraint(
                name: "CK_regiq_conversation_scope",
                schema: "meta",
                table: "regiq_conversation",
                sql: "[Scope] IN ('SECTOR_WIDE','ENTITY_SPECIFIC','COMPARATIVE','SYSTEMIC','HELP')");

            migrationBuilder.DropColumn(
                name: "ExaminationTargetTenantId",
                schema: "meta",
                table: "regiq_conversation");

            migrationBuilder.DropColumn(
                name: "IsExaminationSession",
                schema: "meta",
                table: "regiq_conversation");
        }
    }
}
