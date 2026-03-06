#nullable enable
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FC.Engine.Infrastructure.Metadata.Migrations;

[DbContext(typeof(MetadataDbContext))]
[Migration("20260307010000_AddOnboardingAutomationRg29")]
public partial class AddOnboardingAutomationRg29 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('meta.template_fields', 'RegulatoryReference') IS NULL
                ALTER TABLE meta.template_fields ADD RegulatoryReference NVARCHAR(300) NULL;

            IF OBJECT_ID(N'dbo.knowledge_base_articles', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.knowledge_base_articles (
                    Id            INT             IDENTITY(1,1) PRIMARY KEY,
                    Title         NVARCHAR(200)   NOT NULL,
                    Content       NVARCHAR(MAX)   NOT NULL,
                    Category      NVARCHAR(50)    NOT NULL,
                    ModuleCode    NVARCHAR(30)    NULL,
                    Tags          NVARCHAR(MAX)   NULL,
                    DisplayOrder  INT             NOT NULL DEFAULT 0,
                    IsPublished   BIT             NOT NULL DEFAULT 1,
                    CreatedAt     DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
                );
            END;

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_knowledge_base_articles_IsPublished_Category_DisplayOrder'
                  AND object_id = OBJECT_ID('dbo.knowledge_base_articles'))
            BEGIN
                CREATE INDEX IX_knowledge_base_articles_IsPublished_Category_DisplayOrder
                    ON dbo.knowledge_base_articles(IsPublished, Category, DisplayOrder);
            END;

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_knowledge_base_articles_ModuleCode'
                  AND object_id = OBJECT_ID('dbo.knowledge_base_articles'))
            BEGIN
                CREATE INDEX IX_knowledge_base_articles_ModuleCode
                    ON dbo.knowledge_base_articles(ModuleCode);
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_knowledge_base_articles_ModuleCode'
                  AND object_id = OBJECT_ID('dbo.knowledge_base_articles'))
            BEGIN
                DROP INDEX IX_knowledge_base_articles_ModuleCode ON dbo.knowledge_base_articles;
            END;

            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_knowledge_base_articles_IsPublished_Category_DisplayOrder'
                  AND object_id = OBJECT_ID('dbo.knowledge_base_articles'))
            BEGIN
                DROP INDEX IX_knowledge_base_articles_IsPublished_Category_DisplayOrder ON dbo.knowledge_base_articles;
            END;

            IF OBJECT_ID(N'dbo.knowledge_base_articles', N'U') IS NOT NULL
                DROP TABLE dbo.knowledge_base_articles;

            IF COL_LENGTH('meta.template_fields', 'RegulatoryReference') IS NOT NULL
                ALTER TABLE meta.template_fields DROP COLUMN RegulatoryReference;
            """);
    }
}
