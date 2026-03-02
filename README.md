# FC Engine

A regulatory return collection and validation engine built for financial institutions. FC Engine automates the ingestion, structural validation, formula evaluation, and cross-sheet reconciliation of financial returns submitted in XML format.

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Solution Structure](#solution-structure)
- [Technology Stack](#technology-stack)
- [Key Features](#key-features)
- [API Endpoints](#api-endpoints)
- [Admin Portal](#admin-portal)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Database](#database)
- [Testing](#testing)

---

## Overview

FC Engine manages the full lifecycle of financial/regulatory return templates and submissions:

1. **Template Management** — Define return templates with versioned field schemas, item codes, and structural categories (FixedRow, MultiRow, ItemCoded).
2. **XML Ingestion** — Accept XML submissions, validate against dynamically generated XSD schemas, and parse data into structured records.
3. **Multi-Phase Validation** — Run a pipeline of type/range checks, intra-sheet formula evaluation, cross-sheet reconciliation, and configurable business rules.
4. **Dynamic Schema Management** — Automatically generate and execute DDL to create/alter physical database tables as templates evolve through versions.
5. **Admin Portal** — A Blazor Server application for managing templates, reviewing submissions, configuring validation rules, and monitoring audit logs.

---

## Architecture

The solution follows **Clean Architecture** principles with clear separation of concerns:

```
┌──────────────────────────────────────────────────┐
│                  Presentation                     │
│         FC.Engine.Api  │  FC.Engine.Admin          │
│        (Minimal API)   │  (Blazor Server)          │
├──────────────────────────────────────────────────┤
│                  Application                      │
│              FC.Engine.Application                 │
│     Orchestrators, Services, DTOs                 │
├──────────────────────────────────────────────────┤
│                    Domain                         │
│               FC.Engine.Domain                    │
│   Entities, Value Objects, Abstractions, Enums    │
├──────────────────────────────────────────────────┤
│                 Infrastructure                    │
│            FC.Engine.Infrastructure               │
│  EF Core, Dapper, Validation, XML, DDL Engine     │
└──────────────────────────────────────────────────┘
```

Dependencies flow inward — Infrastructure and Presentation depend on Domain/Application, never the reverse.

---

## Solution Structure

```
FC Engine/
├── src/
│   ├── FC.Engine.Domain/              # Core domain entities, enums, abstractions
│   │   ├── Abstractions/              # Repository & service interfaces
│   │   ├── DataRecord/                # ReturnDataRecord (parsed submission data)
│   │   ├── Entities/                  # Submission, Institution, PortalUser, ValidationReport
│   │   ├── Enums/                     # SubmissionStatus, TemplateStatus, FieldDataType, etc.
│   │   ├── Metadata/                  # ReturnTemplate, TemplateVersion, TemplateField, IntraSheetFormula
│   │   ├── Validation/                # BusinessRule, CrossSheetRule
│   │   └── ValueObjects/              # ReturnCode, ReportingPeriod
│   │
│   ├── FC.Engine.Application/         # Application services & orchestrators
│   │   ├── DTOs/                      # SubmissionDto, TemplateDto
│   │   └── Services/
│   │       ├── IngestionOrchestrator   # End-to-end XML submission processing
│   │       ├── ValidationOrchestrator  # Multi-phase validation pipeline
│   │       ├── TemplateService         # Template CRUD operations
│   │       ├── TemplateVersioningService # Version lifecycle (Draft → Review → Published)
│   │       ├── FormulaService          # Intra-sheet formula management
│   │       ├── AuthService             # User authentication (PBKDF2 hashing)
│   │       ├── SeedService             # Template seeding from schema.sql
│   │       ├── FormulaSeedService      # Formula seeding from column patterns
│   │       ├── FormulaCatalogSeeder    # Formula seeding from JSON catalog
│   │       └── CrossSheetRuleSeedService # Cross-sheet rule seeding
│   │
│   ├── FC.Engine.Infrastructure/      # External concerns implementation
│   │   ├── Audit/                     # AuditLogger
│   │   ├── Caching/                   # TemplateMetadataCache, CacheWarmupService
│   │   ├── DynamicSchema/             # DdlEngine, DdlMigrationExecutor, SqlTypeMapper
│   │   ├── Metadata/                  # MetadataDbContext, EF Core configurations, repositories
│   │   ├── Migrations/                # EF Core migrations
│   │   ├── Persistence/               # DynamicSqlBuilder, GenericDataRepository
│   │   ├── Validation/                # FormulaEvaluator, CrossSheetValidator, ExpressionParser
│   │   └── Xml/                       # GenericXmlParser, XsdGenerator
│   │
│   ├── FC.Engine.Api/                 # REST API (Minimal API)
│   │   ├── Endpoints/                 # SubmissionEndpoints, TemplateEndpoints, SchemaEndpoints
│   │   └── Middleware/                # ApiKeyMiddleware
│   │
│   ├── FC.Engine.Admin/               # Admin portal (Blazor Server)
│   │   └── Components/Pages/
│   │       ├── Dashboard              # Overview dashboard
│   │       ├── Templates/             # Template list & detail (version management)
│   │       ├── Submissions/           # Submission list & detail (validation reports)
│   │       ├── Formulas/              # Formula catalog browser
│   │       ├── Validation/            # Business rules & cross-sheet rules
│   │       ├── Analysis/              # Impact analysis
│   │       ├── Audit/                 # Audit log viewer
│   │       └── Account/               # Login, Logout, User management
│   │
│   └── FC.Engine.Migrator/           # Database migration & seeding console app
│
├── tests/
│   ├── FC.Engine.Domain.Tests/        # Domain entity & value object unit tests
│   ├── FC.Engine.Infrastructure.Tests/ # Service & repository unit tests
│   └── FC.Engine.Integration.Tests/   # End-to-end integration tests
│
├── docker/
│   ├── docker-compose.yml             # SQL Server, Migrator, API, Admin
│   ├── docker-compose.override.yml    # Development overrides
│   ├── Dockerfile.api                 # API container image
│   ├── Dockerfile.admin               # Admin portal container image
│   ├── Dockerfile.migrator            # Migrator container image
│   └── .env.example                   # Environment variable template
│
└── schema.sql                         # Source-of-truth DDL for return table schemas
```

---

## Technology Stack

| Layer | Technology |
|-------|-----------|
| Runtime | .NET 10 Preview |
| API | ASP.NET Core Minimal APIs |
| Admin UI | Blazor Server (Interactive SSR) |
| ORM (Metadata) | Entity Framework Core 8.0 (SQL Server) |
| ORM (Dynamic Data) | Dapper 2.1 |
| Database | SQL Server 2022 |
| Authentication | Cookie-based (Admin), API Key (API) |
| Logging | Serilog |
| API Docs | Swashbuckle / Swagger |
| Testing | xUnit, FluentAssertions |
| Containers | Docker Compose |

---

## Key Features

### Template Version Lifecycle

Templates follow a controlled versioning workflow:

```
Draft  →  Review  →  Published  →  Deprecated  →  Retired
```

- **Draft**: Fields, formulas, and item codes can be modified
- **Review**: Submitted for approval; DDL preview available
- **Published**: Active version; DDL executed to create/alter physical tables
- **Deprecated**: Superseded by a newer published version

### Multi-Phase Validation Pipeline

Each XML submission passes through validation phases in order:

| Phase | Description |
|-------|-------------|
| **1. XSD Schema** | Validates XML structure against dynamically generated XSD |
| **2. Type & Range** | Checks data types, required fields, min/max ranges, allowed values, max lengths |
| **3. Intra-Sheet Formulas** | Evaluates computed field formulas (e.g., totals = sum of components) |
| **4. Cross-Sheet Rules** | Reconciles values across different return templates with tolerance thresholds |
| **5. Business Rules** | Configurable rules (date checks, threshold checks, completeness, custom expressions) |

### Submission Statuses

```
Draft → Parsing → Validating → Accepted / AcceptedWithWarnings / Rejected
```

### Dynamic DDL Engine

When a template version is published, the DDL Engine:
- Generates `CREATE TABLE` / `ALTER TABLE` SQL for the return's physical table
- Produces rollback scripts for safe reversal
- Supports DDL preview before execution

### Template Structural Categories

| Category | Description |
|----------|-------------|
| **FixedRow** | Single-row returns with fixed fields |
| **MultiRow** | Multiple rows with the same field structure |
| **ItemCoded** | Rows identified by item codes (line items) |

### Expression Evaluation

Built-in expression parser and tokenizer for evaluating:
- Intra-sheet formulas (field-level computed validations)
- Cross-sheet rule expressions (e.g., `A = B`, `A >= B * 0.125`)
- Aggregate functions: `SUM`, `COUNT`, `MAX`, `MIN`, `AVG`

---

## API Endpoints

### Submissions

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/submissions/{returnCode}` | Submit an XML return for processing |
| `GET` | `/api/submissions/{id}` | Get submission with validation report |
| `GET` | `/api/submissions/institution/{institutionId}` | List submissions by institution |

### Templates

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/templates` | List all templates |
| `GET` | `/api/templates/{returnCode}` | Get template detail with versions |
| `POST` | `/api/templates` | Create a new template |
| `POST` | `/api/templates/{id}/versions` | Create a draft version |
| `POST` | `/api/templates/{id}/versions/{vid}/fields` | Add field to draft version |
| `POST` | `/api/templates/{id}/versions/{vid}/submit` | Submit version for review |
| `POST` | `/api/templates/{id}/versions/{vid}/preview-ddl` | Preview DDL changes |
| `POST` | `/api/templates/{id}/versions/{vid}/publish` | Publish version (executes DDL) |
| `GET` | `/api/templates/{id}/versions/{vid}/formulas` | Get version formulas |

### Schemas

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/schemas/{returnCode}/xsd` | Generate XSD schema for a template |
| `GET` | `/api/schemas/published` | List all published templates from cache |
| `POST` | `/api/schemas/seed` | Seed templates from schema.sql |
| `POST` | `/api/schemas/seed-formulas` | Seed formulas and cross-sheet rules |

### Health

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/health` | Health check endpoint |

**Authentication**: API endpoints are protected by an `X-Api-Key` header when configured. The `/health` and `/swagger` paths are excluded.

---

## Admin Portal

The Blazor Server admin portal provides:

| Page | Description |
|------|-------------|
| **Dashboard** | Overview of templates, submissions, and system status |
| **Templates** | Browse, create, and manage return templates |
| **Template Detail** | Version management — create drafts, add fields, submit for review, preview DDL, publish |
| **Submissions** | Browse submissions with status filtering |
| **Submission Detail** | View validation reports with error/warning breakdown |
| **Formulas** | Browse and search the formula catalog |
| **Business Rules** | View and manage business validation rules |
| **Cross-Sheet Rules** | View cross-sheet reconciliation rules with operands and expressions |
| **Impact Analysis** | Analyze the impact of template changes |
| **Audit Log** | Review system audit trail |
| **User Management** | Manage portal users and roles (Admin only) |

### Roles & Authorization

| Role | Permissions |
|------|-------------|
| **Viewer** | Read-only access to templates, submissions, and reports |
| **Approver** | Viewer permissions + approve/publish template versions |
| **Admin** | Full access including user management |

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (Preview)
- [Docker](https://www.docker.com/) & Docker Compose
- SQL Server 2022 (or use the Docker Compose setup)

### Quick Start with Docker Compose

1. **Clone the repository**:
   ```bash
   git clone <repository-url>
   cd "FC Engine"
   ```

2. **Configure environment variables**:
   ```bash
   cp docker/.env.example docker/.env
   # Edit docker/.env with your preferred passwords
   ```

3. **Start all services**:
   ```bash
   cd docker
   docker compose up -d
   ```

   This starts:
   - **SQL Server** on port `1433`
   - **Migrator** — runs EF Core migrations, seeds templates/formulas/admin user, then exits
   - **API** on port `5100` (production) or `5000` (development)
   - **Admin Portal** on port `5200` (production) or `5001` (development)

4. **Access the applications**:
   - API + Swagger UI: `http://localhost:5100/swagger`
   - Admin Portal: `http://localhost:5200`
   - Default admin credentials: `admin` / `Admin@123`

### Local Development (without Docker)

1. **Start SQL Server** (or point to an existing instance)

2. **Update connection string** in `src/FC.Engine.Api/appsettings.json` and `src/FC.Engine.Admin/appsettings.json`

3. **Run the migrator**:
   ```bash
   cd "FC Engine"
   dotnet run --project src/FC.Engine.Migrator
   ```

4. **Start the API**:
   ```bash
   dotnet run --project src/FC.Engine.Api
   ```

5. **Start the Admin Portal** (in a separate terminal):
   ```bash
   dotnet run --project src/FC.Engine.Admin
   ```

---

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `ConnectionStrings__FcEngine` | SQL Server connection string | — |
| `SA_PASSWORD` | SQL Server SA password | `YourStrong@Passw0rd` |
| `ADMIN_PASSWORD` | Default admin user password | `Admin@123` |
| `API_KEY` | API key for `X-Api-Key` authentication (empty = disabled) | — |
| `Seeding__SchemaFilePath` | Path to `schema.sql` for template seeding | `/app/schema.sql` |
| `Seeding__MetadataSchemaPath` | Path to metadata schema SQL script | — |
| `Seeding__FormulaCatalogPath` | Path to formula catalog JSON file | — |
| `Seeding__AutoSeed` | Auto-seed templates on migration | `true` |
| `DefaultAdmin__Password` | Default admin password for migrator | `Admin@123` |

### Serilog

Logging is configured via the `Serilog` section in `appsettings.json`. Default level is `Information` with `Warning` overrides for ASP.NET Core and EF Core internals.

---

## Database

FC Engine uses SQL Server with two logical schemas:

### Metadata Tables (EF Core)

Managed by Entity Framework Core migrations:

- `ReturnTemplates` — Template definitions
- `TemplateVersions` — Version history with lifecycle status
- `TemplateFields` — Field definitions per version
- `TemplateItemCodes` — Item code mappings
- `IntraSheetFormulas` — Formula definitions
- `BusinessRules` — Configurable validation rules
- `CrossSheetRules` / `CrossSheetRuleOperands` / `CrossSheetRuleExpressions` — Cross-template reconciliation rules
- `Submissions` / `ValidationReports` / `ValidationErrors` — Submission tracking
- `Institutions` / `ReturnPeriods` — Reference data
- `PortalUsers` — Admin portal user accounts
- `AuditLogs` — System audit trail

### Dynamic Data Tables (Dapper)

Physical tables created/altered by the DDL Engine when template versions are published. Each return template maps to a dedicated SQL table for storing submitted financial data.

### Running Migrations

```bash
# Apply migrations
cd "FC Engine"
dotnet ef database update --project src/FC.Engine.Infrastructure --startup-project src/FC.Engine.Api

# Add a new migration
dotnet ef migrations add <MigrationName> --project src/FC.Engine.Infrastructure --startup-project src/FC.Engine.Api
```

---

## Testing

The solution includes three test projects:

| Project | Scope | Framework |
|---------|-------|-----------|
| `FC.Engine.Domain.Tests` | Domain entities, value objects, enums | xUnit + FluentAssertions |
| `FC.Engine.Infrastructure.Tests` | Services (Auth, Formula, Orchestrator, Template, Versioning) | xUnit + FluentAssertions |
| `FC.Engine.Integration.Tests` | End-to-end API and processing tests | xUnit |

### Running Tests

```bash
cd "FC Engine"

# Run all tests
dotnet test

# Run a specific test project
dotnet test tests/FC.Engine.Domain.Tests
dotnet test tests/FC.Engine.Infrastructure.Tests
dotnet test tests/FC.Engine.Integration.Tests
```

---

## License

Proprietary. All rights reserved.
