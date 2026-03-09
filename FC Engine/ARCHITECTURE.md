# RegOS™ - Metadata-Driven Architecture

## Overview

RegOS™ is a **metadata-driven** CBN DFIS FC Returns Data Processing Engine. Instead of hardcoding 103+ return template definitions in C# classes, all template structures, validation formulas, and business rules are stored as database metadata and interpreted at runtime.

**Key benefit**: CBN business users can create, modify, and publish new return templates entirely through the Admin Portal — zero code changes, zero deployments.

## Technology Stack

| Component | Technology |
|-----------|-----------|
| Runtime | .NET 10.0 |
| Metadata DB | SQL Server 2022 + EF Core (pooled) |
| Dynamic Data | Dapper (parameterized SQL against runtime-generated tables) |
| Admin Portal | Blazor Server (Interactive SSR) |
| API | ASP.NET Minimal API |
| Auth (Admin) | Cookie-based with RBAC (Admin/Approver/Viewer) |
| Auth (API) | API key middleware (X-Api-Key header) |
| Caching | ConcurrentDictionary singleton with hosted-service warmup |
| Logging | Serilog (structured, console sink) |
| Containers | Docker Compose (SQL Server + Migrator + API + Admin) |
| Testing | xUnit + FluentAssertions + Moq (125+ tests) |

## Solution Structure

```
RegOS™/
├── src/
│   ├── FC.Engine.Domain/              # Entities, interfaces, value objects, enums
│   │   ├── Abstractions/              # ITemplateRepository, IFormulaRepository, IDdlEngine, etc.
│   │   ├── DataRecord/                # ReturnDataRecord, ReturnDataRow (universal container)
│   │   ├── Entities/                  # Submission, Institution, PortalUser, etc.
│   │   ├── Enums/                     # FieldDataType, FormulaType, PortalRole, etc.
│   │   ├── Metadata/                  # ReturnTemplate, TemplateVersion, TemplateField
│   │   ├── Validation/                # IntraSheetFormula, CrossSheetRule, BusinessRule
│   │   └── ValueObjects/              # ReturnCode, FieldValue, ReportingPeriod
│   │
│   ├── FC.Engine.Application/         # Services, orchestrators, DTOs
│   │   ├── DTOs/                      # TemplateDto, FormulaDto, SubmissionResultDto
│   │   └── Services/                  # TemplateService, AuthService, IngestionOrchestrator,
│   │                                  # ValidationOrchestrator, SeedService, FormulaService
│   │
│   ├── FC.Engine.Infrastructure/      # All implementations
│   │   ├── Audit/                     # AuditLogger
│   │   ├── Caching/                   # TemplateMetadataCache, CacheWarmupService
│   │   ├── DynamicSchema/             # DdlEngine, DdlMigrationExecutor, SqlTypeMapper
│   │   ├── Metadata/                  # MetadataDbContext, EF configurations, repositories
│   │   ├── Persistence/               # GenericDataRepository, DynamicSqlBuilder
│   │   ├── Validation/                # FormulaEvaluator, CrossSheetValidator,
│   │   │                              # ExpressionParser, ExpressionTokenizer
│   │   └── Xml/                       # GenericXmlParser, XsdGenerator
│   │
│   ├── FC.Engine.Api/                 # Submission API (Minimal API)
│   │   ├── Endpoints/                 # SubmissionEndpoints, TemplateEndpoints, SchemaEndpoints
│   │   └── Middleware/                # ApiKeyMiddleware
│   │
│   ├── FC.Engine.Admin/               # Admin Portal (Blazor Server)
│   │   └── Components/
│   │       ├── Layout/                # MainLayout, NavMenu, LoginLayout
│   │       └── Pages/                 # Dashboard, Templates/, Formulas/, Validation/,
│   │                                  # Submissions/, Audit/, Account/
│   │
│   └── FC.Engine.Migrator/            # DB migration + metadata seeding
│
├── tests/
│   ├── FC.Engine.Domain.Tests/        # 47 unit tests
│   └── FC.Engine.Infrastructure.Tests/# 78 unit tests
│
└── docker/
    ├── docker-compose.yml             # SQL Server + Migrator + API + Admin
    ├── docker-compose.override.yml    # Dev overrides
    ├── Dockerfile.api
    ├── Dockerfile.admin
    └── Dockerfile.migrator
```

## Core Concepts

### 1. Metadata Database Schema (`meta.*`)

All template definitions live in the `meta` schema:

| Table | Purpose |
|-------|---------|
| `meta.return_templates` | Master registry (return_code, name, structural_category, physical_table_name) |
| `meta.template_versions` | Version lifecycle: Draft → Review → Published → Deprecated |
| `meta.template_fields` | Field definitions per version (name, data_type, sql_type, line_code, constraints) |
| `meta.template_item_codes` | Predefined item codes for ItemCoded templates |
| `meta.template_sections` | UI grouping for fields |
| `meta.intra_sheet_formulas` | Per-template formulas (Sum, Difference, Ratio, Equals, Custom) |
| `meta.cross_sheet_rules` | Multi-template validation rules |
| `meta.cross_sheet_rule_operands` | Operand definitions with aliases (A, B, C...) |
| `meta.cross_sheet_rule_expressions` | Expressions like "A = B + C" |
| `meta.business_rules` | Generic rules (DateCheck, ThresholdCheck, Completeness) |
| `meta.audit_log` | Every metadata change with old/new JSON values |
| `meta.ddl_migrations` | CREATE/ALTER TABLE scripts with rollback |
| `meta.portal_users` | Portal user accounts with roles |

### 2. Structural Categories

Templates fall into three categories, determining how data is stored and parsed:

| Category | Row Pattern | Key Column | Example |
|----------|------------|------------|---------|
| **FixedRow** | Single row per submission | — | MFCR 300 (Balance Sheet) |
| **MultiRow** | N rows keyed by serial number | `serial_no` | MFCR 800 (Top 20 Borrowers) |
| **ItemCoded** | N rows keyed by predefined item code | `item_code` | MFCR 400 (Classification by Sector) |

### 3. Universal Data Container

```csharp
public class ReturnDataRecord
{
    public string ReturnCode { get; set; }
    public StructuralCategory Category { get; set; }
    public List<ReturnDataRow> Rows { get; set; }
}

public class ReturnDataRow
{
    public Dictionary<string, object?> Values { get; set; }  // field_name → typed value
}
```

Replaces all 103 typed `IReturnData` classes. Type safety enforced at metadata level via `FieldDataType`.

### 4. Generic Pipeline

The entire submission pipeline is template-agnostic:

```
POST /api/submissions/{returnCode}
  → TemplateMetadataCache.GetPublishedTemplate()
  → XsdGenerator.GenerateSchema() (validates XML structure)
  → GenericXmlParser.Parse() (metadata-driven extraction)
  → ReturnDataRecord
  → ValidationOrchestrator:
      1. Type/Range checks (from field metadata)
      2. IntraSheet formulas (FormulaEvaluator)
      3. CrossSheet rules (CrossSheetValidator)
      4. Business rules (BusinessRuleEvaluator)
  → GenericDataRepository.Save() (Dapper → physical table)
  → ValidationReport response
```

## Key Components

### DdlEngine
Generates `CREATE TABLE` for new templates and `ALTER TABLE ADD COLUMN` for modifications. **Never drops columns** (data preservation). Produces forward + rollback scripts, recorded in `meta.ddl_migrations`.

### FormulaEvaluator
Evaluates all intra-sheet formula types from metadata:
- **Sum**: target = sum(operands)
- **Difference**: target = A - B
- **Equals**: target = operand
- **Ratio**: target = A / B
- **GreaterThan / Between**: comparison checks
- **Custom**: arbitrary expressions via ExpressionParser

Supports tolerance (absolute amount and percentage).

### ExpressionParser
Shunting-yard algorithm for custom expressions (e.g., `A + B - C`, `A >= B * 0.125`). Supports arithmetic (`+`, `-`, `*`, `/`) and comparison (`=`, `!=`, `>`, `<`, `>=`, `<=`) operators. Prevents code injection by limiting to safe arithmetic.

### CrossSheetValidator
Loads multi-template rules with operand aliases, fetches data from related templates, evaluates expressions. Supports aggregate functions (`SUM`, `COUNT`, `AVG`) for MultiRow sources.

### TemplateMetadataCache
`ConcurrentDictionary<string, CachedTemplate>` singleton. Eager-warmed on startup via `CacheWarmupService` (IHostedService). Invalidated on template publish.

## Authentication & Authorization

### Admin Portal (Cookie Auth)
- Three roles: **Viewer** (read-only), **Approver** (can approve/publish templates), **Admin** (full access + user management)
- All pages require `[Authorize]`
- Audit Log restricted to Approver + Admin
- User Management restricted to Admin only
- Default admin user seeded by Migrator on first run

### API (API Key)
- `X-Api-Key` header required for all endpoints (except `/health` and `/swagger`)
- Configurable via `ApiKey` setting / environment variable
- When `ApiKey` is empty, authentication is disabled (development mode)

## Docker Compose

Four services orchestrated with health checks:

```
sqlserver (2022) ← migrator (depends: db healthy)
                 ← api (depends: migrator completed) → port 5100
                 ← admin (depends: migrator completed) → port 5200
```

**Startup sequence**: SQL Server starts → health check passes → Migrator runs (EF migrations + metadata seeding + default admin user) → API and Admin start with cache warmup.

### Environment Variables
| Variable | Default | Purpose |
|----------|---------|---------|
| `SA_PASSWORD` | `YourStrong@Passw0rd` | SQL Server password |
| `ADMIN_PASSWORD` | `Admin@123` | Default admin portal password |
| `API_KEY` | (empty = disabled) | API authentication key |

## Seeding Pipeline (Migrator)

The Migrator runs six steps on startup:

1. **EF Core Migrations** — creates/updates metadata + operational tables
2. **Metadata Schema SQL** — additional indexes and constraints for `meta.*` tables
3. **Template Seeding** — parses `schema.sql` (103 tables) → seeds into `meta.return_templates` + `meta.template_fields`
4. **Formula Seeding** — detects column patterns (total columns, sub-totals) → creates intra-sheet formulas
5. **Cross-Sheet Rules** — seeds 25 cross-sheet validation rules (XS-001 through XS-045)
6. **Default Admin User** — creates `admin` user if no users exist

## Performance

- **DbContext Pooling**: `AddDbContextPool` with retry-on-failure for connection resilience
- **Cache Warmup**: All 103 templates loaded into memory at startup via `CacheWarmupService`
- **Singleton Cache**: Template metadata served from `ConcurrentDictionary` (zero DB hits for hot path)
- **Dapper for Data**: Parameterized SQL avoids EF tracking overhead for dynamic tables
- **Expression Parsing**: `ExpressionParser` and `ExpressionTokenizer` are singletons (compiled once)

## Data Flow: New Template Creation

```
Admin Portal → TemplateService.CreateTemplate()
  → meta.return_templates (new row)
  → meta.template_versions (Draft)
  → User adds fields → meta.template_fields rows
  → User adds formulas → meta.intra_sheet_formulas rows
  → Submit for Review (status → Review)
  → Approver publishes:
      → DdlEngine.GenerateCreateTable()
      → Preview DDL → Execute DDL
      → Physical table created in DB
      → meta.ddl_migrations recorded
      → Cache invalidated → Template live
```

## Data Flow: Template Modification

```
Admin Portal → CreateNewDraftVersion (clones current)
  → User adds/modifies field
  → Publish:
      → DdlEngine.GenerateAlterTable()
      → ALTER TABLE ADD COLUMN (never drops)
      → Old data preserved (NULL for new column)
      → New submissions include new field
```

## Testing

- **Domain Tests** (47): ReturnDataRecord, TemplateField, formula entities, value objects
- **Infrastructure Tests** (78): DdlEngine, GenericXmlParser, FormulaEvaluator, ExpressionParser, CrossSheetValidator, DynamicSqlBuilder, XsdGenerator

All tests use xUnit + FluentAssertions + Moq.
