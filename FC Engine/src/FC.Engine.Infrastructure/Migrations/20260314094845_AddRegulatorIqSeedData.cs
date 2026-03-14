using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FC.Engine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRegulatorIqSeedData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "meta",
                table: "regiq_config",
                columns: new[] { "Id", "ConfigKey", "ConfigValue", "CreatedAt", "CreatedBy", "Description", "EffectiveFrom", "EffectiveTo" },
                values: new object[,]
                {
                    { 1, "rate.queries_per_minute", "30", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "SYSTEM", "Maximum RegulatorIQ queries per regulator per minute.", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { 2, "llm.model", "claude-3-5-sonnet-latest", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "SYSTEM", "Default model used by RegulatorIQ for LLM-assisted analysis.", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { 3, "llm.temperature", "0.1", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "SYSTEM", "Default RegulatorIQ response temperature.", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), null }
                });

            migrationBuilder.InsertData(
                schema: "meta",
                table: "regiq_intent",
                columns: new[] { "Id", "Category", "CreatedAt", "Description", "DisplayName", "ExampleQuery", "IntentCode", "IsEnabled", "PrimaryDataSource", "RequiresRegulatorContext", "SortOrder" },
                values: new object[,]
                {
                    { 1, "REGULATOR", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "Build a regulator-facing profile for a supervised institution.", "Entity Intelligence Profile", "Give me a full profile of Access Bank", "ENTITY_PROFILE", true, "MULTI", true, 1 },
                    { 2, "REGULATOR", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "Summarise cross-entity sector intelligence for the current scope.", "Sector Intelligence Summary", "Show me a sector health summary for commercial banks", "SECTOR_SUMMARY", true, "MULTI", true, 2 },
                    { 3, "REGULATOR", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "Rank institutions by their latest Compliance Health Score.", "Compliance Health Ranking", "Rank DMBs by compliance health score", "CHS_RANKING", true, "RG-32", true, 3 }
                });

            migrationBuilder.InsertData(
                schema: "meta",
                table: "regiq_query_template",
                columns: new[] { "Id", "ClassificationLevel", "CreatedAt", "CrossTenantEnabled", "DataSourcesJson", "Description", "DisplayName", "IntentCode", "IsActive", "ParameterSchema", "RequiresEntityContext", "ResultFormat", "Scope", "SortOrder", "SqlTemplate", "TemplateCode", "UpdatedAt", "VisualizationType" },
                values: new object[] { 1, "RESTRICTED", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), true, "[\"RG-32\"]", "Rank supervised institutions by their most recent Compliance Health Score.", "Latest CHS Ranking", "CHS_RANKING", true, "{\"LicenceCategory\":\"string?\",\"Limit\":\"int\"}", false, "TABLE", "SECTOR_WIDE", 1, "WITH latest_chs AS (\n    SELECT\n        s.TenantId,\n        s.OverallScore,\n        s.Rating,\n        s.ComputedAt,\n        ROW_NUMBER() OVER (PARTITION BY s.TenantId ORDER BY s.ComputedAt DESC) AS rn\n    FROM chs_score_snapshots s\n)\nSELECT TOP (@Limit)\n    l.TenantId AS tenant_id,\n    i.InstitutionName AS institution_name,\n    COALESCE(licence.Code, i.LicenseType, '') AS licence_category,\n    CAST(l.OverallScore AS decimal(10,2)) AS chs_score,\n    l.Rating AS rating,\n    l.ComputedAt AS computed_at\nFROM latest_chs l\nINNER JOIN institutions i ON i.TenantId = l.TenantId\nOUTER APPLY (\n    SELECT TOP (1) lt.Code\n    FROM tenant_licence_types tlt\n    INNER JOIN licence_types lt ON lt.Id = tlt.LicenceTypeId\n    WHERE tlt.TenantId = l.TenantId\n      AND tlt.IsActive = 1\n    ORDER BY tlt.EffectiveDate DESC, tlt.Id DESC\n) licence\nWHERE l.rn = 1\n  AND (@LicenceCategory IS NULL OR @LicenceCategory = '' OR COALESCE(licence.Code, i.LicenseType, '') = @LicenceCategory)\nORDER BY l.OverallScore DESC, i.InstitutionName ASC;", "CHS_RANKING_LATEST", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "ranking" });

            migrationBuilder.InsertData(
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                columns: new[] { "Id", "Alias", "AliasType", "CanonicalName", "CreatedAt", "GeoTag", "HoldingCompanyName", "InstitutionType", "IsActive", "IsPrimary", "LicenceCategory", "MatchPriority", "NormalizedAlias", "RegulatorAgency", "TenantId" },
                values: new object[,]
                {
                    { 1L, "Access Bank Plc", "NAME", "Access Bank Plc", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "NG", "Access Holdings Plc", "BANK", true, true, "DMB", 10, "access bank plc", "CBN", null },
                    { 2L, "Access Bank", "COMMON", "Access Bank Plc", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "NG", "Access Holdings Plc", "BANK", true, false, "DMB", 20, "access bank", "CBN", null },
                    { 3L, "Access", "COMMON", "Access Bank Plc", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "NG", "Access Holdings Plc", "BANK", true, false, "DMB", 30, "access", "CBN", null },
                    { 4L, "Zenith Bank Plc", "NAME", "Zenith Bank Plc", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "NG", null, "BANK", true, true, "DMB", 10, "zenith bank plc", "CBN", null },
                    { 5L, "Zenith Bank", "COMMON", "Zenith Bank Plc", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "NG", null, "BANK", true, false, "DMB", 20, "zenith bank", "CBN", null },
                    { 6L, "Zenith", "COMMON", "Zenith Bank Plc", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "NG", null, "BANK", true, false, "DMB", 30, "zenith", "CBN", null },
                    { 7L, "Guaranty Trust Bank Plc", "NAME", "Guaranty Trust Bank Plc", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "NG", "GTCO Plc", "BANK", true, true, "DMB", 10, "guaranty trust bank plc", "CBN", null },
                    { 8L, "GTBank", "COMMON", "Guaranty Trust Bank Plc", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "NG", "GTCO Plc", "BANK", true, false, "DMB", 20, "gtbank", "CBN", null },
                    { 9L, "GT Bank", "COMMON", "Guaranty Trust Bank Plc", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "NG", "GTCO Plc", "BANK", true, false, "DMB", 30, "gt bank", "CBN", null },
                    { 10L, "Guaranty Trust", "COMMON", "Guaranty Trust Bank Plc", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "NG", "GTCO Plc", "BANK", true, false, "DMB", 40, "guaranty trust", "CBN", null },
                    { 11L, "GTCO", "HOLDING_COMPANY", "Guaranty Trust Bank Plc", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "NG", "GTCO Plc", "BANK", true, false, "DMB", 50, "gtco", "CBN", null },
                    { 12L, "First Bank Nigeria Limited", "NAME", "First Bank Nigeria Limited", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "NG", "FBN Holdings Plc", "BANK", true, true, "DMB", 10, "first bank nigeria limited", "CBN", null },
                    { 13L, "First Bank", "COMMON", "First Bank Nigeria Limited", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "NG", "FBN Holdings Plc", "BANK", true, false, "DMB", 20, "first bank", "CBN", null },
                    { 14L, "First", "COMMON", "First Bank Nigeria Limited", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "NG", "FBN Holdings Plc", "BANK", true, false, "DMB", 30, "first", "CBN", null },
                    { 15L, "FBN", "ABBREVIATION", "First Bank Nigeria Limited", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "NG", "FBN Holdings Plc", "BANK", true, false, "DMB", 40, "fbn", "CBN", null },
                    { 16L, "FBNH", "HOLDING_COMPANY", "First Bank Nigeria Limited", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "NG", "FBN Holdings Plc", "BANK", true, false, "DMB", 50, "fbnh", "CBN", null },
                    { 17L, "First City Monument Bank Plc", "NAME", "First City Monument Bank Plc", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "NG", "FCMB Group Plc", "BANK", true, true, "DMB", 10, "first city monument bank plc", "CBN", null },
                    { 18L, "FCMB", "ABBREVIATION", "First City Monument Bank Plc", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "NG", "FCMB Group Plc", "BANK", true, false, "DMB", 20, "fcmb", "CBN", null },
                    { 19L, "First City Monument", "COMMON", "First City Monument Bank Plc", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "NG", "FCMB Group Plc", "BANK", true, false, "DMB", 30, "first city monument", "CBN", null },
                    { 20L, "First", "COMMON", "First City Monument Bank Plc", new DateTime(2026, 3, 14, 0, 0, 0, 0, DateTimeKind.Utc), "NG", "FCMB Group Plc", "BANK", true, false, "DMB", 40, "first", "CBN", null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regiq_config",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regiq_config",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regiq_config",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regiq_intent",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regiq_intent",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regiq_intent",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regiq_query_template",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                keyColumn: "Id",
                keyValue: 1L);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                keyColumn: "Id",
                keyValue: 2L);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                keyColumn: "Id",
                keyValue: 3L);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                keyColumn: "Id",
                keyValue: 4L);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                keyColumn: "Id",
                keyValue: 5L);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                keyColumn: "Id",
                keyValue: 6L);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                keyColumn: "Id",
                keyValue: 7L);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                keyColumn: "Id",
                keyValue: 8L);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                keyColumn: "Id",
                keyValue: 9L);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                keyColumn: "Id",
                keyValue: 10L);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                keyColumn: "Id",
                keyValue: 11L);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                keyColumn: "Id",
                keyValue: 12L);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                keyColumn: "Id",
                keyValue: 13L);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                keyColumn: "Id",
                keyValue: 14L);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                keyColumn: "Id",
                keyValue: 15L);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                keyColumn: "Id",
                keyValue: 16L);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                keyColumn: "Id",
                keyValue: 17L);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                keyColumn: "Id",
                keyValue: 18L);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                keyColumn: "Id",
                keyValue: 19L);

            migrationBuilder.DeleteData(
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                keyColumn: "Id",
                keyValue: 20L);
        }
    }
}
