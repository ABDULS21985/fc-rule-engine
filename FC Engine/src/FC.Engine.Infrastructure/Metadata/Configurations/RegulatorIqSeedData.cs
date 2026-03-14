using FC.Engine.Domain.Entities;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

internal static class RegIqSeedData
{
    internal static readonly DateTime SeedDate = new(2026, 3, 14, 0, 0, 0, DateTimeKind.Utc);

    internal static readonly RegIqConfig[] Configs =
    [
        new RegIqConfig
        {
            Id = 1,
            ConfigKey = "rate.queries_per_minute",
            ConfigValue = "30",
            Description = "Maximum RegulatorIQ queries per regulator per minute.",
            EffectiveFrom = SeedDate,
            CreatedBy = "SYSTEM",
            CreatedAt = SeedDate
        },
        new RegIqConfig
        {
            Id = 2,
            ConfigKey = "llm.model",
            ConfigValue = "claude-3-5-sonnet-latest",
            Description = "Default model used by RegulatorIQ for LLM-assisted analysis.",
            EffectiveFrom = SeedDate,
            CreatedBy = "SYSTEM",
            CreatedAt = SeedDate
        },
        new RegIqConfig
        {
            Id = 3,
            ConfigKey = "llm.temperature",
            ConfigValue = "0.1",
            Description = "Default RegulatorIQ response temperature.",
            EffectiveFrom = SeedDate,
            CreatedBy = "SYSTEM",
            CreatedAt = SeedDate
        }
    ];

    internal static readonly RegIqIntent[] Intents =
    [
        new RegIqIntent
        {
            Id = 1,
            IntentCode = "ENTITY_PROFILE",
            Category = "REGULATOR",
            DisplayName = "Entity Intelligence Profile",
            Description = "Build a regulator-facing profile for a supervised institution.",
            ExampleQuery = "Give me a full profile of Access Bank",
            PrimaryDataSource = "MULTI",
            RequiresRegulatorContext = true,
            IsEnabled = true,
            SortOrder = 1,
            CreatedAt = SeedDate
        },
        new RegIqIntent
        {
            Id = 2,
            IntentCode = "SECTOR_SUMMARY",
            Category = "REGULATOR",
            DisplayName = "Sector Intelligence Summary",
            Description = "Summarise cross-entity sector intelligence for the current scope.",
            ExampleQuery = "Show me a sector health summary for commercial banks",
            PrimaryDataSource = "MULTI",
            RequiresRegulatorContext = true,
            IsEnabled = true,
            SortOrder = 2,
            CreatedAt = SeedDate
        },
        new RegIqIntent
        {
            Id = 3,
            IntentCode = "CHS_RANKING",
            Category = "REGULATOR",
            DisplayName = "Compliance Health Ranking",
            Description = "Rank institutions by their latest Compliance Health Score.",
            ExampleQuery = "Rank DMBs by compliance health score",
            PrimaryDataSource = "RG-32",
            RequiresRegulatorContext = true,
            IsEnabled = true,
            SortOrder = 3,
            CreatedAt = SeedDate
        }
    ];

    internal static readonly RegIqQueryTemplate[] QueryTemplates =
    [
        new RegIqQueryTemplate
        {
            Id = 1,
            IntentCode = "CHS_RANKING",
            TemplateCode = "CHS_RANKING_LATEST",
            DisplayName = "Latest CHS Ranking",
            Description = "Rank supervised institutions by their most recent Compliance Health Score.",
            SqlTemplate =
                """
                WITH latest_chs AS (
                    SELECT
                        s.TenantId,
                        s.OverallScore,
                        s.Rating,
                        s.ComputedAt,
                        ROW_NUMBER() OVER (PARTITION BY s.TenantId ORDER BY s.ComputedAt DESC) AS rn
                    FROM chs_score_snapshots s
                )
                SELECT TOP (@Limit)
                    l.TenantId AS tenant_id,
                    i.InstitutionName AS institution_name,
                    COALESCE(licence.Code, i.LicenseType, '') AS licence_category,
                    CAST(l.OverallScore AS decimal(10,2)) AS chs_score,
                    l.Rating AS rating,
                    l.ComputedAt AS computed_at
                FROM latest_chs l
                INNER JOIN institutions i ON i.TenantId = l.TenantId
                OUTER APPLY (
                    SELECT TOP (1) lt.Code
                    FROM tenant_licence_types tlt
                    INNER JOIN licence_types lt ON lt.Id = tlt.LicenceTypeId
                    WHERE tlt.TenantId = l.TenantId
                      AND tlt.IsActive = 1
                    ORDER BY tlt.EffectiveDate DESC, tlt.Id DESC
                ) licence
                WHERE l.rn = 1
                  AND (@LicenceCategory IS NULL OR @LicenceCategory = '' OR COALESCE(licence.Code, i.LicenseType, '') = @LicenceCategory)
                ORDER BY l.OverallScore DESC, i.InstitutionName ASC;
                """,
            ParameterSchema = """{"LicenceCategory":"string?","Limit":"int"}""",
            ResultFormat = "TABLE",
            VisualizationType = "ranking",
            Scope = "SECTOR_WIDE",
            ClassificationLevel = "RESTRICTED",
            DataSourcesJson = """["RG-32"]""",
            CrossTenantEnabled = true,
            RequiresEntityContext = false,
            IsActive = true,
            SortOrder = 1,
            CreatedAt = SeedDate,
            UpdatedAt = SeedDate
        }
    ];

    internal static readonly RegIqEntityAlias[] EntityAliases =
    [
        CreateAlias(1, "Access Bank Plc", "Access Bank Plc", "access bank plc", "NAME", "DMB", "CBN", "BANK", "Access Holdings Plc", 10, true),
        CreateAlias(2, "Access Bank Plc", "Access Bank", "access bank", "COMMON", "DMB", "CBN", "BANK", "Access Holdings Plc", 20),
        CreateAlias(3, "Access Bank Plc", "Access", "access", "COMMON", "DMB", "CBN", "BANK", "Access Holdings Plc", 30),
        CreateAlias(4, "Zenith Bank Plc", "Zenith Bank Plc", "zenith bank plc", "NAME", "DMB", "CBN", "BANK", null, 10, true),
        CreateAlias(5, "Zenith Bank Plc", "Zenith Bank", "zenith bank", "COMMON", "DMB", "CBN", "BANK", null, 20),
        CreateAlias(6, "Zenith Bank Plc", "Zenith", "zenith", "COMMON", "DMB", "CBN", "BANK", null, 30),
        CreateAlias(7, "Guaranty Trust Bank Plc", "Guaranty Trust Bank Plc", "guaranty trust bank plc", "NAME", "DMB", "CBN", "BANK", "GTCO Plc", 10, true),
        CreateAlias(8, "Guaranty Trust Bank Plc", "GTBank", "gtbank", "COMMON", "DMB", "CBN", "BANK", "GTCO Plc", 20),
        CreateAlias(9, "Guaranty Trust Bank Plc", "GT Bank", "gt bank", "COMMON", "DMB", "CBN", "BANK", "GTCO Plc", 30),
        CreateAlias(10, "Guaranty Trust Bank Plc", "Guaranty Trust", "guaranty trust", "COMMON", "DMB", "CBN", "BANK", "GTCO Plc", 40),
        CreateAlias(11, "Guaranty Trust Bank Plc", "GTCO", "gtco", "HOLDING_COMPANY", "DMB", "CBN", "BANK", "GTCO Plc", 50),
        CreateAlias(12, "First Bank Nigeria Limited", "First Bank Nigeria Limited", "first bank nigeria limited", "NAME", "DMB", "CBN", "BANK", "FBN Holdings Plc", 10, true),
        CreateAlias(13, "First Bank Nigeria Limited", "First Bank", "first bank", "COMMON", "DMB", "CBN", "BANK", "FBN Holdings Plc", 20),
        CreateAlias(14, "First Bank Nigeria Limited", "First", "first", "COMMON", "DMB", "CBN", "BANK", "FBN Holdings Plc", 30),
        CreateAlias(15, "First Bank Nigeria Limited", "FBN", "fbn", "ABBREVIATION", "DMB", "CBN", "BANK", "FBN Holdings Plc", 40),
        CreateAlias(16, "First Bank Nigeria Limited", "FBNH", "fbnh", "HOLDING_COMPANY", "DMB", "CBN", "BANK", "FBN Holdings Plc", 50),
        CreateAlias(17, "First City Monument Bank Plc", "First City Monument Bank Plc", "first city monument bank plc", "NAME", "DMB", "CBN", "BANK", "FCMB Group Plc", 10, true),
        CreateAlias(18, "First City Monument Bank Plc", "FCMB", "fcmb", "ABBREVIATION", "DMB", "CBN", "BANK", "FCMB Group Plc", 20),
        CreateAlias(19, "First City Monument Bank Plc", "First City Monument", "first city monument", "COMMON", "DMB", "CBN", "BANK", "FCMB Group Plc", 30),
        CreateAlias(20, "First City Monument Bank Plc", "First", "first", "COMMON", "DMB", "CBN", "BANK", "FCMB Group Plc", 40)
    ];

    private static RegIqEntityAlias CreateAlias(
        long id,
        string canonicalName,
        string alias,
        string normalizedAlias,
        string aliasType,
        string licenceCategory,
        string regulatorAgency,
        string institutionType,
        string? holdingCompanyName,
        int matchPriority,
        bool isPrimary = false)
    {
        return new RegIqEntityAlias
        {
            Id = id,
            TenantId = null,
            CanonicalName = canonicalName,
            Alias = alias,
            NormalizedAlias = normalizedAlias,
            AliasType = aliasType,
            LicenceCategory = licenceCategory,
            RegulatorAgency = regulatorAgency,
            InstitutionType = institutionType,
            HoldingCompanyName = holdingCompanyName,
            GeoTag = "NG",
            MatchPriority = matchPriority,
            IsPrimary = isPrimary,
            IsActive = true,
            CreatedAt = SeedDate
        };
    }
}
