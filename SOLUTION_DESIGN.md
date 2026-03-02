# FC Engine — Solution Design Document

**Version:** 1.0
**Last Updated:** March 2026
**Status:** Current

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [System Context](#2-system-context)
3. [Architecture Overview](#3-architecture-overview)
4. [Solution Structure](#4-solution-structure)
5. [Domain Model](#5-domain-model)
6. [Application Services](#6-application-services)
7. [Infrastructure Layer](#7-infrastructure-layer)
8. [API Layer](#8-api-layer)
9. [Admin Portal](#9-admin-portal)
10. [Data Architecture](#10-data-architecture)
11. [Validation Engine](#11-validation-engine)
12. [Dynamic Schema Management](#12-dynamic-schema-management)
13. [Authentication & Authorization](#13-authentication--authorization)
14. [Caching Strategy](#14-caching-strategy)
15. [Seeding & Data Initialization](#15-seeding--data-initialization)
16. [Deployment Architecture](#16-deployment-architecture)
17. [Testing Strategy](#17-testing-strategy)
18. [Cross-Cutting Concerns](#18-cross-cutting-concerns)
19. [Key Design Decisions](#19-key-design-decisions)
20. [Appendix](#20-appendix)

---

## 1. Executive Summary

FC Engine is a regulatory return collection and validation engine built for financial institutions under the Central Bank of Nigeria (CBN) supervisory framework. It automates the ingestion, structural validation, formula evaluation, and cross-sheet reconciliation of financial returns submitted in XML format.

The system manages 103+ return templates across four reporting frequencies (Monthly, Quarterly, Semi-Annual, Computed), with each template following a controlled versioning lifecycle. Submitted returns pass through a five-phase validation pipeline that enforces XSD schema compliance, type/range constraints, intra-sheet formulas, cross-sheet reconciliation, and configurable business rules.

### Key Capabilities

- **Template Management** — Versioned field schemas with lifecycle control (Draft → Review → Published → Deprecated)
- **XML Ingestion** — Accept XML submissions, validate against dynamically generated XSD schemas, parse into structured records
- **Multi-Phase Validation** — Five-phase pipeline: XSD → Type/Range → Intra-Sheet → Cross-Sheet → Business Rules
- **Dynamic Schema Management** — Auto-generate and execute DDL to create/alter physical database tables as templates evolve
- **Expression Engine** — Built-in tokenizer and parser for evaluating mathematical, comparison, and aggregate expressions
- **Admin Portal** — Blazor Server application for template management, submission review, rule configuration, and audit

---

## 2. System Context

### External Actors

| Actor | Description | Interface |
|-------|-------------|-----------|
| **Financial Institutions** | Submit XML returns for regulatory reporting | REST API (XML payload) |
| **Supervisory Staff** | Review submissions, manage templates, configure rules | Admin Portal (Blazor) |
| **CBN Systems** | Source of template definitions and formula catalogs | Seed files (schema.sql, formula_catalog.json) |

### System Boundary

```
┌─────────────────────────────────────────────────────────────────────┐
│                        FC Engine System                              │
│                                                                      │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────────────┐   │
│  │   REST API    │    │ Admin Portal │    │    Migrator CLI       │   │
│  │  (Minimal API)│    │(Blazor Server)│    │ (Database Init)      │   │
│  └──────┬───────┘    └──────┬───────┘    └──────────┬───────────┘   │
│         │                   │                        │               │
│         └───────────┬───────┘────────────────────────┘               │
│                     │                                                │
│         ┌───────────▼───────────┐                                    │
│         │  Application Services  │                                   │
│         │  (Orchestrators, etc.) │                                   │
│         └───────────┬───────────┘                                    │
│                     │                                                │
│         ┌───────────▼───────────┐                                    │
│         │   Infrastructure       │                                   │
│         │  (EF Core, Dapper,     │                                   │
│         │   Validation, DDL)     │                                   │
│         └───────────┬───────────┘                                    │
│                     │                                                │
│            ┌────────▼────────┐                                       │
│            │  SQL Server 2022 │                                      │
│            │  (Metadata +     │                                      │
│            │   Dynamic Data)  │                                      │
│            └─────────────────┘                                       │
└─────────────────────────────────────────────────────────────────────┘
```

### Reporting Frequencies & Return Code Prefixes

| Prefix | Frequency | Example | Description |
|--------|-----------|---------|-------------|
| `MFCR` | Monthly | MFCR 300 | Monthly Financial Condition Report |
| `QFCR` | Quarterly | QFCR 364 | Quarterly Financial Condition Report |
| `SFCR` | Semi-Annual | SFCR 400 | Semi-Annual Financial Condition Report |
| `FC` | Computed | FC 100 | Computed/Derived Financial Condition |

---

## 3. Architecture Overview

### Architectural Style

The solution follows **Clean Architecture** (Onion Architecture) principles with strict dependency inversion. Dependencies flow inward — outer layers depend on inner layers, never the reverse.

```
┌──────────────────────────────────────────────────────────────────┐
│                       PRESENTATION LAYER                          │
│                                                                    │
│   FC.Engine.Api          FC.Engine.Admin       FC.Engine.Migrator  │
│   (Minimal API)          (Blazor Server)       (Console App)       │
│                                                                    │
├──────────────────────────────────────────────────────────────────┤
│                       APPLICATION LAYER                            │
│                                                                    │
│   FC.Engine.Application                                            │
│   Orchestrators · Services · DTOs                                  │
│                                                                    │
├──────────────────────────────────────────────────────────────────┤
│                        DOMAIN LAYER                                │
│                                                                    │
│   FC.Engine.Domain                                                 │
│   Entities · Value Objects · Enums · Abstractions                  │
│                                                                    │
├──────────────────────────────────────────────────────────────────┤
│                     INFRASTRUCTURE LAYER                           │
│                                                                    │
│   FC.Engine.Infrastructure                                         │
│   EF Core · Dapper · Validation · XML · DDL Engine · Caching      │
│                                                                    │
└──────────────────────────────────────────────────────────────────┘
```

### Dependency Graph

```
FC.Engine.Api ────────────┐
FC.Engine.Admin ──────────┤
FC.Engine.Migrator ───────┤
                          ▼
              FC.Engine.Application ──────► FC.Engine.Domain
                          │                       ▲
                          ▼                       │
              FC.Engine.Infrastructure ───────────┘
```

### Technology Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| Runtime | .NET | 10.0 (Preview) |
| API Framework | ASP.NET Core Minimal APIs | 10.0 |
| Admin UI | Blazor Server (Interactive SSR) | 10.0 |
| ORM (Metadata) | Entity Framework Core | 8.0.11 |
| ORM (Dynamic Data) | Dapper | 2.1.35 |
| Database | SQL Server | 2022 |
| SQL Client | Microsoft.Data.SqlClient | 5.2.2 |
| Logging | Serilog.AspNetCore | 8.0.3 |
| API Docs | Swashbuckle.AspNetCore (Swagger) | 6.9.0 |
| In-Memory Cache | Microsoft.Extensions.Caching.Memory | 8.0.1 |
| Testing | xUnit + FluentAssertions + Moq | 2.9.2 / 6.12.2 / 4.20.72 |
| Containers | Docker Compose | Latest |

---

## 4. Solution Structure

```
FC Engine/
├── FCEngine.sln                          # Solution file (9 projects)
│
├── src/
│   ├── FC.Engine.Domain/                 # Core domain (zero dependencies)
│   │   ├── Abstractions/                 # 15 interfaces (repository + service contracts)
│   │   ├── DataRecord/                   # ReturnDataRecord, ReturnDataRow
│   │   ├── Entities/                     # Submission, Institution, ReturnPeriod,
│   │   │                                 # ValidationReport, ValidationError, PortalUser
│   │   ├── Enums/                        # 8 enums (status, types, categories)
│   │   ├── Metadata/                     # ReturnTemplate, TemplateVersion, TemplateField,
│   │   │                                 # IntraSheetFormula, TemplateItemCode, TemplateSection
│   │   ├── Validation/                   # BusinessRule, CrossSheetRule + operands/expressions
│   │   └── ValueObjects/                 # ReturnCode (immutable, self-parsing)
│   │
│   ├── FC.Engine.Application/            # Application services & orchestrators
│   │   ├── DTOs/                         # TemplateDto hierarchy, SubmissionResultDto, FormulaDto
│   │   └── Services/
│   │       ├── AuthService               # PBKDF2 password hashing, login validation
│   │       ├── IngestionOrchestrator     # End-to-end XML submission processing
│   │       ├── ValidationOrchestrator    # Multi-phase validation pipeline
│   │       ├── TemplateService           # Template CRUD with audit
│   │       ├── TemplateVersioningService # Version lifecycle (Draft→Review→Published)
│   │       ├── FormulaService            # Formula CRUD (intra-sheet, cross-sheet, business)
│   │       ├── SeedService               # Template seeding from schema.sql
│   │       ├── FormulaSeedService        # Formula derivation from column patterns
│   │       ├── FormulaCatalogSeeder      # Formula seeding from JSON catalog
│   │       └── CrossSheetRuleSeedService # 45 predefined cross-sheet rules
│   │
│   ├── FC.Engine.Infrastructure/         # External concerns implementation
│   │   ├── Audit/                        # AuditLogger (JSON state serialization)
│   │   ├── Caching/                      # TemplateMetadataCache, CacheWarmupService
│   │   ├── DynamicSchema/               # DdlEngine, DdlMigrationExecutor, SqlTypeMapper
│   │   ├── Metadata/                    # MetadataDbContext, 8 EF configurations, 5 repositories
│   │   ├── Migrations/                  # EF Core migrations
│   │   ├── Persistence/                 # DynamicSqlBuilder, GenericDataRepository (Dapper)
│   │   ├── Validation/                  # ExpressionTokenizer, ExpressionParser,
│   │   │                                # FormulaEvaluator, CrossSheetValidator,
│   │   │                                # BusinessRuleEvaluator
│   │   └── Xml/                         # GenericXmlParser, XsdGenerator
│   │
│   ├── FC.Engine.Api/                   # REST API (Minimal API)
│   │   ├── Endpoints/                   # SubmissionEndpoints, TemplateEndpoints, SchemaEndpoints
│   │   └── Middleware/                  # ApiKeyMiddleware
│   │
│   ├── FC.Engine.Admin/                 # Admin Portal (Blazor Server)
│   │   └── Components/
│   │       ├── Layout/                  # MainLayout, LoginLayout, NavMenu
│   │       └── Pages/                   # 12 Blazor pages across 6 sections
│   │
│   └── FC.Engine.Migrator/             # Database migration & seeding console app
│
├── tests/
│   ├── FC.Engine.Domain.Tests/          # Domain entity/value object unit tests
│   ├── FC.Engine.Infrastructure.Tests/  # Service + infrastructure unit tests
│   └── FC.Engine.Integration.Tests/     # End-to-end integration tests (WebApplicationFactory)
│
├── docker/
│   ├── docker-compose.yml               # Production: sqlserver, migrator, api, admin
│   ├── docker-compose.override.yml      # Development port overrides
│   ├── Dockerfile.api                   # Multi-stage API image
│   ├── Dockerfile.admin                 # Multi-stage Admin image
│   ├── Dockerfile.migrator              # Migrator image
│   └── .env.example                     # Environment variable template
│
└── schema.sql                           # Source-of-truth DDL for 103+ return table schemas
```

---

## 5. Domain Model

### 5.1 Entity Relationship Diagram

```
                            ┌─────────────────────┐
                            │   ReturnTemplate     │
                            │─────────────────────│
                            │ Id                   │
                            │ ReturnCode (unique)  │
                            │ Name                 │
                            │ Frequency            │
                            │ StructuralCategory   │
                            │ PhysicalTableName    │
                            │ XmlRootElement       │
                            │ XmlNamespace         │
                            │ IsSystemTemplate     │
                            │ OwnerDepartment      │
                            │ InstitutionType      │
                            └─────────┬───────────┘
                                      │ 1:N
                            ┌─────────▼───────────┐
                            │   TemplateVersion    │
                            │─────────────────────│
                            │ Id                   │
                            │ TemplateId (FK)      │
                            │ VersionNumber        │
                            │ Status               │
                            │ EffectiveFrom/To     │
                            │ DdlScript            │
                            │ RollbackScript       │
                            │ ApprovedBy/At        │
                            │ PublishedAt           │
                            └──┬──────┬────────┬──┘
                               │      │        │
                    ┌──────────┘      │        └──────────┐
                    │ 1:N             │ 1:N                │ 1:N
          ┌─────────▼──────┐  ┌──────▼──────────┐  ┌─────▼─────────────┐
          │ TemplateField  │  │TemplateItemCode  │  │IntraSheetFormula  │
          │────────────────│  │─────────────────│  │───────────────────│
          │ FieldName      │  │ ItemCode         │  │ RuleCode          │
          │ DisplayName    │  │ ItemName         │  │ FormulaType       │
          │ XmlElementName │  │ SortOrder        │  │ TargetFieldName   │
          │ LineCode       │  └─────────────────┘  │ OperandFields[]   │
          │ DataType       │                        │ CustomExpression  │
          │ SqlType        │                        │ ToleranceAmount   │
          │ IsRequired     │                        │ Severity          │
          │ IsComputed     │                        │ IsActive          │
          │ IsKeyField     │                        │ SortOrder         │
          │ Min/Max/Length │                        └───────────────────┘
          └────────────────┘

  ┌─────────────────┐       ┌────────────────────────────────────────┐
  │   Submission     │       │           CrossSheetRule                │
  │─────────────────│       │────────────────────────────────────────│
  │ InstitutionId   │◄──┐  │ RuleCode (unique)                      │
  │ ReturnPeriodId  │   │  │ RuleName                               │
  │ ReturnCode      │   │  │ Severity                               │
  │ Status          │   │  │ IsActive                               │
  │ SubmittedAt     │   │  └────────────┬──────────────┬───────────┘
  │ ProcessingMs    │   │               │ 1:N          │ 1:1
  └────────┬────────┘   │    ┌──────────▼─────────┐  ┌─▼──────────────────┐
           │ 1:1        │    │CrossSheetRuleOperand│  │CrossSheetRuleExpr  │
  ┌────────▼────────┐   │    │────────────────────│  │────────────────────│
  │ValidationReport │   │    │ OperandAlias (A,B) │  │ Expression          │
  │─────────────────│   │    │ TemplateReturnCode │  │ ToleranceAmount     │
  │ IsValid*        │   │    │ FieldName          │  │ TolerancePercent    │
  │ ErrorCount*     │   │    │ AggregateFunction  │  │ ErrorMessage        │
  │ WarningCount*   │   │    │ FilterItemCode     │  └────────────────────┘
  └────────┬────────┘   │    └────────────────────┘
           │ 1:N        │
  ┌────────▼────────┐   │    ┌──────────────────┐
  │ValidationError  │   │    │  BusinessRule      │
  │─────────────────│   │    │──────────────────│
  │ RuleId          │   │    │ RuleCode (unique) │
  │ Field           │   │    │ RuleType          │
  │ Message         │   │    │ Expression        │
  │ Severity        │   │    │ AppliesToTemplates│
  │ Category        │   │    │ AppliesToFields   │
  │ Expected/Actual │   │    │ Severity          │
  └─────────────────┘   │    └──────────────────┘
                         │
  ┌─────────────────┐   │    ┌──────────────────┐
  │  Institution     │───┘    │  PortalUser       │
  │─────────────────│         │──────────────────│
  │ InstitutionCode │         │ Username (unique) │
  │ InstitutionName │         │ DisplayName       │
  │ LicenseType     │         │ Email (unique)    │
                              │ PasswordHash      │
  ┌─────────────────┐         │ Role              │
  │  ReturnPeriod    │         │ IsActive          │
  │─────────────────│         │ LastLoginAt       │
  │ PeriodDate      │         └──────────────────┘
  │ Frequency       │
  └─────────────────┘
                    (* = computed property, not persisted)
```

### 5.2 Enumerations

| Enum | Values | Purpose |
|------|--------|---------|
| `SubmissionStatus` | Draft, Parsing, Validating, Accepted, AcceptedWithWarnings, Rejected | Submission lifecycle states |
| `TemplateStatus` | Draft, Review, Published, Deprecated, Retired | Version lifecycle states |
| `FieldDataType` | Money, Integer, Decimal, Text, Date, Boolean, Percentage | Domain-level data type classification |
| `StructuralCategory` | FixedRow, MultiRow, ItemCoded | Template row structure type |
| `ReturnFrequency` | Monthly, Quarterly, SemiAnnual, Computed | Reporting frequency |
| `FormulaType` | Sum, Difference, Equals, GreaterThan, LessThan, GreaterThanOrEqual, LessThanOrEqual, Between, Ratio, Custom, Required | Intra-sheet formula operation type |
| `ValidationSeverity` | Info, Warning, Error | Validation finding severity |
| `PortalRole` | Viewer, Approver, Admin | Portal user authorization role |

### 5.3 Value Objects

#### ReturnCode

Immutable value object that encapsulates return code parsing and transformation logic:

```
Input: "MFCR 300" | "mfcr300" | " MFCR  300 "
  ↓ Parse()
ReturnCode { Value: "MFCR 300", Prefix: "MFCR", Number: "300" }
  ↓
ToTableName()      → "mfcr_300"
ToXmlRootElement() → "MFCR300"
ToXmlNamespace()   → "urn:cbn:dfis:fc:mfcr300"
```

Supports case-insensitive equality comparison. Handles prefixes: MFCR, QFCR, SFCR, FC.

### 5.4 Data Record Model

`ReturnDataRecord` is a universal container that replaces per-template data classes:

```
ReturnDataRecord
├── ReturnCode: string
├── TemplateVersionId: int
├── Category: StructuralCategory
└── Rows: List<ReturnDataRow>
    └── ReturnDataRow
        ├── RowKey: string? (serial_no | item_code | null)
        └── Fields: Dictionary<string, object?> (case-insensitive)
```

| Category | Row Behavior |
|----------|-------------|
| FixedRow | Exactly one row, `RowKey = null`, access via `SingleRow` |
| MultiRow | N rows, `RowKey = serial_no (1, 2, 3...)` |
| ItemCoded | N rows, `RowKey = item_code` |

### 5.5 Domain Abstractions (Interfaces)

The Domain layer defines 15 abstractions implemented by Infrastructure:

| Interface | Responsibility |
|-----------|---------------|
| `ITemplateRepository` | Template CRUD with version/field/formula eager loading |
| `IFormulaRepository` | Intra-sheet formula, cross-sheet rule, business rule CRUD |
| `ISubmissionRepository` | Submission tracking with validation report loading |
| `IGenericDataRepository` | Dynamic data table CRUD via Dapper |
| `IPortalUserRepository` | Portal user management |
| `IDdlEngine` | DDL script generation (CREATE TABLE, ALTER TABLE) |
| `IDdlMigrationExecutor` | DDL execution with rollback tracking |
| `ISqlTypeMapper` | FieldDataType → SQL Server type mapping |
| `IFormulaEvaluator` | Intra-sheet formula validation engine |
| `ICrossSheetValidator` | Cross-sheet rule validation with data fetching |
| `IBusinessRuleEvaluator` | Business rule evaluation (completeness, date, threshold, custom) |
| `IGenericXmlParser` | XML → ReturnDataRecord parsing |
| `IXsdGenerator` | Dynamic XSD schema generation with caching |
| `ITemplateMetadataCache` | Published template metadata cache |
| `IAuditLogger` | Audit trail logging with JSON state serialization |

---

## 6. Application Services

### 6.1 Service Architecture

```
                      ┌─────────────────────────────┐
                      │    Presentation Layer         │
                      │  (API Endpoints / Blazor)     │
                      └──────────────┬────────────────┘
                                     │
          ┌──────────────────────────┼──────────────────────────┐
          │                          │                          │
┌─────────▼─────────┐  ┌────────────▼────────────┐  ┌─────────▼──────────┐
│IngestionOrchestrator│ │ValidationOrchestrator   │  │TemplateService      │
│   (Submission)      │ │  (Multi-phase pipeline)  │ │TemplateVersioning   │
│                     │ │                          │  │FormulaService       │
│ Coordinates:        │ │ Coordinates:             │  │AuthService          │
│ • Cache lookup      │ │ • Type/Range validation  │  │                     │
│ • XSD validation    │ │ • Formula evaluation     │  │ CRUD + Audit +      │
│ • XML parsing       │ │ • Cross-sheet validation │  │ Cache invalidation  │
│ • Validation        │ │ • Business rule eval     │  │                     │
│ • Data persistence  │ │                          │  │                     │
│ • Status tracking   │ │                          │  │                     │
└─────────────────────┘ └──────────────────────────┘  └────────────────────┘
          │                          │                          │
          └──────────────────────────┼──────────────────────────┘
                                     │
                      ┌──────────────▼────────────────┐
                      │   Domain Abstractions          │
                      │  (Repositories, Validators,    │
                      │   Cache, Audit, DDL)           │
                      └───────────────────────────────┘
```

### 6.2 IngestionOrchestrator

The primary entry point for XML submission processing. Coordinates the full lifecycle:

```
Process(xmlStream, returnCode, institutionId, returnPeriodId)
│
├── 1. Create Submission record (Status: Draft)
├── 2. Resolve published template from cache
├── 3. Mark Submission → Parsing
├── 4. Validate XML against generated XSD schema
│   ├── On schema errors → Reject submission, return errors
│   └── On pass → continue
├── 5. Parse XML into ReturnDataRecord
├── 6. Mark Submission → Validating
├── 7. Run ValidationOrchestrator.Validate()
├── 8. Attach ValidationReport to Submission
├── 9. If valid (no errors):
│   ├── Delete previous submission data
│   ├── Persist parsed data to dynamic table
│   └── Mark → Accepted or AcceptedWithWarnings
├── 10. If invalid → Mark → Rejected
└── 11. Record processing duration, return SubmissionResultDto
```

**Dependencies:** ITemplateMetadataCache, IXsdGenerator, IGenericXmlParser, IGenericDataRepository, ISubmissionRepository, ValidationOrchestrator

### 6.3 ValidationOrchestrator

Coordinates the four-phase validation pipeline:

```
Validate(record, submission, institutionId, returnPeriodId)
│
├── Phase 1: Type/Range Validation (static, inline)
│   ├── Required field null checks
│   ├── Numeric range checks (min/max)
│   ├── Text length checks (maxLength)
│   └── Allowed values checks (enum validation)
│
├── Phase 2: Intra-Sheet Formula Evaluation
│   └── IFormulaEvaluator.Evaluate(record)
│       ├── Sum, Difference, Equals, Comparison, Between, Ratio
│       ├── Custom expression evaluation
│       └── Required field checks
│
├── Phase 3: Cross-Sheet Validation (conditional: only if Phase 2 has no errors)
│   └── ICrossSheetValidator.Validate(record, institutionId, returnPeriodId)
│       ├── Fetches data from other template tables
│       ├── Resolves operand values (with aggregates)
│       └── Evaluates cross-sheet expressions with tolerance
│
├── Phase 4: Business Rule Evaluation
│   └── IBusinessRuleEvaluator.Evaluate(record, submission)
│       ├── Completeness checks
│       ├── Date checks (not future)
│       ├── Threshold checks (expression-based)
│       └── Custom expression evaluation
│
└── Return ValidationReport (IsValid = no errors across all phases)
```

### 6.4 TemplateVersioningService

Manages the template version lifecycle with DDL generation and execution:

```
CreateNewDraftVersion(templateId, createdBy)
├── Fetch template
├── If published version exists → deep clone fields, item codes, formulas
├── Create Draft version with incremented number
└── Audit log

SubmitForReview(templateId, versionId, submittedBy)
├── Validate version has fields
├── Transition: Draft → Review
└── Audit log

PreviewDdl(templateId, versionId)
├── Get version + previous published
├── No previous → GenerateCreateTable()
├── Has previous → GenerateAlterTable()
└── Return DdlScript (no execution)

Publish(templateId, versionId, approvedBy)
├── Generate DDL (CREATE or ALTER)
├── Execute DDL via DdlMigrationExecutor
├── Store DDL scripts on version
├── Deprecate previous published version
├── Publish new version (Status → Published)
├── Invalidate metadata cache + XSD cache
└── Audit log
```

### 6.5 Service Dependency Matrix

| Service | Repositories | Validators | Infrastructure | Other Services |
|---------|-------------|------------|----------------|----------------|
| IngestionOrchestrator | SubmissionRepo, DataRepo | — | Cache, XsdGen, XmlParser | ValidationOrchestrator |
| ValidationOrchestrator | — | FormulaEval, CrossSheetVal, BusinessRuleEval | Cache | — |
| TemplateService | TemplateRepo | — | Cache, SqlTypeMapper, AuditLogger | — |
| TemplateVersioningService | TemplateRepo | — | DdlEngine, MigrationExecutor, Cache, XsdGen, AuditLogger | — |
| FormulaService | FormulaRepo, TemplateRepo | — | AuditLogger | — |
| AuthService | PortalUserRepo | — | — | — |
| SeedService | TemplateRepo | — | — | — |
| FormulaSeedService | TemplateRepo | — | — | — |
| FormulaCatalogSeeder | TemplateRepo, FormulaRepo | — | — | — |
| CrossSheetRuleSeedService | FormulaRepo | — | — | — |

---

## 7. Infrastructure Layer

### 7.1 Dependency Injection Configuration

All Infrastructure services are registered in `DependencyInjection.cs`:

| Registration | Lifetime | Notes |
|-------------|----------|-------|
| `MetadataDbContext` | Pooled (Scoped) | CommandTimeout: 30s, RetryOnFailure: 3 |
| `IDbConnection` (SqlConnection) | Scoped | For Dapper dynamic data access |
| `ITemplateRepository` | Scoped | EF Core based |
| `IFormulaRepository` | Scoped | EF Core based |
| `ISubmissionRepository` | Scoped | EF Core based |
| `IPortalUserRepository` | Scoped | EF Core based |
| `IGenericDataRepository` | Scoped | Dapper based |
| `IDdlEngine` | Scoped | DDL generation |
| `IDdlMigrationExecutor` | Scoped | DDL execution |
| `ISqlTypeMapper` | **Singleton** | Stateless mapping |
| `ExpressionParser` | **Singleton** | Stateless evaluation |
| `ExpressionTokenizer` | **Singleton** | Stateless tokenization |
| `IFormulaEvaluator` | Scoped | Uses ExpressionParser |
| `ICrossSheetValidator` | Scoped | Uses ExpressionParser + DataRepo |
| `IBusinessRuleEvaluator` | Scoped | Uses ExpressionParser |
| `IGenericXmlParser` | Scoped | Uses Cache |
| `IXsdGenerator` | Scoped | Has internal ConcurrentDictionary cache |
| `ITemplateMetadataCache` | **Singleton** | ConcurrentDictionary + scope factory |
| `CacheWarmupService` | **HostedService** | Warms cache on startup |
| `IAuditLogger` | Scoped | EF Core based |

### 7.2 EF Core Configuration

The `MetadataDbContext` manages 17 DbSet entities across two schema namespaces:

**Metadata Schema (`meta.*`):**
- `meta.return_templates` — Template definitions
- `meta.template_versions` — Version history (unique: TemplateId + VersionNumber)
- `meta.template_fields` — Field definitions (unique: VersionId + FieldName)
- `meta.template_item_codes` — Item code mappings (unique: VersionId + ItemCode)
- `meta.template_sections` — Section groupings (unique: VersionId + SectionName)
- `meta.intra_sheet_formulas` — Formula rules (unique: VersionId + RuleCode)
- `meta.cross_sheet_rules` — Cross-template rules (unique: RuleCode)
- `meta.cross_sheet_rule_operands` — Rule operands (unique: RuleId + Alias)
- `meta.cross_sheet_rule_expressions` — Rule expressions (unique: RuleId, 1:1)
- `meta.business_rules` — Business rules (unique: RuleCode)
- `meta.portal_users` — User accounts (unique: Username, Email)
- `meta.audit_log` — Audit trail
- `meta.ddl_migrations` — DDL execution history

**Operational Schema (`dbo.*`):**
- `return_submissions` — Submission tracking
- `institutions` — Institution reference data
- `return_periods` — Period reference data
- `validation_reports` — Validation results
- `validation_errors` — Validation error details

**Key Configuration Patterns:**
- Enum properties stored as strings (not integers) for readability
- Computed properties (`IsValid`, `HasErrors`, etc.) ignored in EF mapping
- Unique indexes on natural keys (ReturnCode, RuleCode, Username)
- Composite unique indexes on relationship keys (TemplateId + VersionNumber)
- Cascade delete from parent to child collections

### 7.3 Repository Implementations

| Repository | ORM | Key Queries |
|-----------|-----|-------------|
| `TemplateRepository` | EF Core | Eager loads: Versions → Fields/ItemCodes/Formulas. Filters by ReturnCode, Frequency |
| `FormulaRepository` | EF Core | Active-only filters. Cross-sheet rules loaded with Operands + Expression. Soft delete (IsActive=false) |
| `SubmissionRepository` | EF Core | Includes: Institution, ReturnPeriod, ValidationReport → Errors. Ordered by SubmittedAt desc |
| `PortalUserRepository` | EF Core | Lookup by Username, ordered list for management |
| `GenericDataRepository` | Dapper | Parameterized INSERT/SELECT/DELETE on dynamic tables. Uses DynamicSqlBuilder for SQL generation |

### 7.4 Dynamic SQL Builder

Generates parameterized SQL for dynamic data tables:

```sql
-- INSERT (only non-null fields included)
INSERT INTO dbo.[mfcr_300] ([submission_id], [cash_notes], [total_cash])
VALUES (@submission_id, @cash_notes, @total_cash)

-- SELECT by submission
SELECT id, submission_id, [cash_notes], [total_cash]
FROM dbo.[mfcr_300]
WHERE submission_id = @submissionId ORDER BY id

-- SELECT by institution + period (cross-table join)
SELECT d.id, d.submission_id, d.[cash_notes], d.[total_cash]
FROM dbo.[mfcr_300] d
INNER JOIN dbo.return_submissions s ON d.submission_id = s.id
WHERE s.institution_id = @institutionId AND s.return_period_id = @returnPeriodId
```

**Safety:** All identifiers validated against `^[a-z_][a-z0-9_]*$` regex. Column names wrapped in brackets. All values parameterized.

---

## 8. API Layer

### 8.1 Endpoint Design

The API uses ASP.NET Core Minimal APIs with three endpoint groups:

#### Submission Endpoints (`/api/submissions`)

| Method | Route | Service | Response |
|--------|-------|---------|----------|
| `POST` | `/{returnCode}` | IngestionOrchestrator.Process() | 200 (Accepted) / 422 (Rejected) |
| `GET` | `/{id:int}` | SubmissionRepository.GetByIdWithReport() | SubmissionResultDto |
| `GET` | `/institution/{institutionId:int}` | SubmissionRepository.GetByInstitution() | List of submissions |

The POST endpoint accepts raw XML in the request body. Query parameters `institutionId` and `returnPeriodId` identify the submitter and reporting period.

#### Template Endpoints (`/api/templates`)

| Method | Route | Service | Purpose |
|--------|-------|---------|---------|
| `GET` | `/` | TemplateService.GetAllTemplates() | List all templates |
| `GET` | `/{returnCode}` | TemplateService.GetTemplateDetail() | Full template detail |
| `POST` | `/` | TemplateService.CreateTemplate() | Create template |
| `POST` | `/{id}/versions` | VersioningService.CreateNewDraftVersion() | Create draft |
| `POST` | `/{id}/versions/{vid}/fields` | TemplateService.AddFieldToVersion() | Add field to draft |
| `POST` | `/{id}/versions/{vid}/submit` | VersioningService.SubmitForReview() | Submit for review |
| `POST` | `/{id}/versions/{vid}/preview-ddl` | VersioningService.PreviewDdl() | Preview DDL |
| `POST` | `/{id}/versions/{vid}/publish` | VersioningService.Publish() | Publish version |
| `GET` | `/{id}/versions/{vid}/formulas` | FormulaService.GetIntraSheetFormulas() | Get formulas |

#### Schema Endpoints (`/api/schemas`)

| Method | Route | Service | Purpose |
|--------|-------|---------|---------|
| `GET` | `/{returnCode}/xsd` | XsdGenerator.GenerateSchemaXml() | Get XSD schema |
| `GET` | `/published` | TemplateMetadataCache.GetAllPublished() | List published templates |
| `POST` | `/seed` | SeedService.SeedFromSchema() | Seed templates from schema.sql |
| `POST` | `/seed-formulas` | Multiple seed services | Seed formulas and rules |

### 8.2 Middleware Pipeline

```
Request → Serilog Logging → API Key Auth → Routing → Endpoint Handler → Response
```

**API Key Authentication** (`ApiKeyMiddleware`):
- Header: `X-Api-Key`
- Exempt paths: `/health`, `/swagger/*`
- Empty API key config = authentication disabled
- Returns 401 with error JSON on failure

### 8.3 Health Check

`GET /health` — Returns 200 OK (unprotected). Used by Docker healthchecks.

---

## 9. Admin Portal

### 9.1 Application Architecture

The Admin Portal is a **Blazor Server** application using Interactive Server-Side Rendering (SSR). All UI logic executes on the server with SignalR managing the real-time connection to the browser.

### 9.2 Layout Structure

```
App.razor (root)
└── Routes.razor (router)
    ├── LoginLayout.razor (unauthenticated)
    │   └── Login.razor
    │
    └── MainLayout.razor (authenticated)
        ├── NavMenu.razor (sidebar, role-based)
        └── @Body (page content)
```

**MainLayout** features:
- Fixed 250px sidebar with navigation
- Top bar with breadcrumbs
- User info display with role badge
- Logout link

### 9.3 Page Inventory

| Page | Route | Auth Policy | Key Services |
|------|-------|-------------|--------------|
| Dashboard | `/` | Authenticated | ITemplateMetadataCache, ISubmissionRepository |
| Login | `/login` | Anonymous | AuthService, IHttpContextAccessor |
| Logout | `/logout` | Anonymous | — |
| User Management | `/users` | AdminOnly | IPortalUserRepository, AuthService |
| Template List | `/templates` | Authenticated | ITemplateMetadataCache, TemplateService |
| Template Detail | `/templates/{ReturnCode}` | Authenticated | TemplateService, VersioningService, IXsdGenerator |
| Submission List | `/submissions` | Authenticated | ISubmissionRepository |
| Submission Detail | `/submissions/{Id:int}` | Authenticated | ISubmissionRepository |
| Formula List | `/formulas` | Authenticated | ITemplateMetadataCache, FormulaService |
| Business Rules | `/business-rules` | Authenticated | IFormulaRepository, FormulaService |
| Cross-Sheet Rules | `/cross-sheet-rules` | Authenticated | IFormulaRepository, FormulaService |
| Impact Analysis | `/impact-analysis` | ApproverOrAdmin | ITemplateMetadataCache, IFormulaRepository |
| Audit Log | `/audit` | ApproverOrAdmin | MetadataDbContext (via scope) |

### 9.4 Key Page Behaviors

**Template Detail** — The most feature-rich page. Provides:
- Version selector with action buttons based on status
- DDL preview panel (expandable)
- XSD schema display (toggleable)
- Field management (add fields to Draft versions)
- Formula count with link to FormulaList
- Item code display for ItemCoded templates

**Formula List** — Full formula CRUD with:
- Search by rule code, field name, or description
- Filter by FormulaType and Template
- Inline add/edit form that adapts based on selected template (populates field dropdowns)
- Operand fields stored as JSON, displayed as comma-separated

**Cross-Sheet Rules** — Dynamic operand builder:
- Add/remove operands with auto-incrementing aliases (A, B, C...)
- Template return code and field name selectors
- Aggregate function dropdown (SUM, COUNT, MAX, MIN, AVG)
- Expression, tolerance, and error message configuration

---

## 10. Data Architecture

### 10.1 Dual-Database Strategy

FC Engine employs a dual-ORM strategy with a single SQL Server instance:

```
┌─────────────────────────────────────────────────┐
│                   SQL Server 2022                 │
│                                                   │
│  ┌──────────────────┐  ┌──────────────────────┐  │
│  │  Metadata Tables   │  │  Dynamic Data Tables  │  │
│  │  (EF Core 8.0)    │  │  (Dapper 2.1)         │  │
│  │                    │  │                        │  │
│  │  meta.* schema     │  │  dbo.mfcr_300         │  │
│  │  • return_templates│  │  dbo.mfcr_301         │  │
│  │  • template_versions│ │  dbo.qfcr_364         │  │
│  │  • template_fields │  │  dbo.sfcr_400         │  │
│  │  • intra_sheet_*   │  │  ... (103+ tables)    │  │
│  │  • cross_sheet_*   │  │                        │  │
│  │  • business_rules  │  │  Created/altered by    │  │
│  │  • submissions     │  │  DDL Engine at publish │  │
│  │  • validation_*    │  │                        │  │
│  │  • portal_users    │  │  Queried/inserted by   │  │
│  │  • audit_log       │  │  GenericDataRepository │  │
│  │  • ddl_migrations  │  │                        │  │
│  └──────────────────┘  └──────────────────────────┘  │
└─────────────────────────────────────────────────┘
```

**Rationale:**
- **EF Core** for metadata: Strong typing, migrations, change tracking, relationships — ideal for the stable schema of templates, rules, and submissions
- **Dapper** for dynamic data: Lightweight, raw SQL capability needed for tables whose schemas are defined at runtime and vary per template

### 10.2 Dynamic Table Schema

Each published template creates a physical SQL table with this structure:

```sql
CREATE TABLE dbo.[mfcr_300] (
    [id]            INT IDENTITY(1,1) PRIMARY KEY,
    [submission_id] INT NOT NULL REFERENCES dbo.return_submissions(id),
    -- Template fields (dynamic, from TemplateField definitions):
    [cash_notes]        DECIMAL(20,2) NULL,
    [cash_coins]        DECIMAL(20,2) NULL,
    [total_cash]        DECIMAL(20,2) NULL,
    -- ... more fields based on template definition
    [created_at]    DATETIME2 DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_mfcr_300_submission_id ON dbo.[mfcr_300]([submission_id]);
```

**ALTER TABLE** is generated when a new version is published:
- New fields → `ADD COLUMN`
- Changed types → `ALTER COLUMN` (only safe widening: INT→BIGINT, VARCHAR width increase, DECIMAL precision increase)
- Removed fields → Commented (not dropped) for safety

### 10.3 SQL Type Mapping

| FieldDataType | SQL Type | Notes |
|--------------|----------|-------|
| Money | `DECIMAL(20,2)` | Financial amounts with 2 decimal places |
| Decimal | `DECIMAL(20,4)` | General precision |
| Percentage | `DECIMAL(10,4)` | Ratio/percentage values |
| Integer | `INT` | Whole numbers |
| Text | `NVARCHAR(255)` | Unicode text |
| Date | `DATE` | Date without time |
| Boolean | `BIT` | True/false |

`SqlType` can be overridden per-field via the `SqlType` property, giving full control when the default mapping is insufficient.

---

## 11. Validation Engine

### 11.1 Expression Engine Architecture

The validation engine includes a custom expression parser built using the Shunting-yard algorithm:

```
Expression String: "A + B * C >= D"
        │
        ▼
┌──────────────────┐
│ExpressionTokenizer│
│                   │
│ Lexical Analysis  │
│ → Number tokens   │
│ → Variable tokens │
│ → Operator tokens │
│ → Function tokens │
│ → Comparison ops  │
│ → Parentheses     │
└────────┬─────────┘
         │ List<Token>
         ▼
┌──────────────────┐
│ ExpressionParser  │
│                   │
│ Shunting-yard     │
│ Algorithm:        │
│ 1. Split at       │
│    comparison op  │
│ 2. Convert each   │
│    side to RPN    │
│ 3. Evaluate RPN   │
│ 4. Compare sides  │
└────────┬─────────┘
         │
         ▼
ExpressionResult {
  Passes: bool,
  LeftValue: decimal,
  RightValue: decimal?
}
```

**Token Types:**

| Type | Examples |
|------|---------|
| Number | `123`, `45.67`, `.5` |
| Variable | `A`, `B`, `total_assets`, `cash_notes` |
| Operator | `+`, `-`, `*`, `/` |
| Comparison | `=`, `!=`, `>`, `<`, `>=`, `<=` |
| Function | `SUM`, `COUNT`, `MAX`, `MIN`, `AVG`, `ABS` |
| Parentheses | `(`, `)` |

**Operator Precedence:**
- `*`, `/` → Precedence 2 (higher)
- `+`, `-` → Precedence 1 (lower)

**Special Behaviors:**
- Division by zero returns 0 (safe, no exception)
- Unknown variables default to 0
- Mismatched parentheses throw `ArgumentException`
- Functions are case-insensitive

### 11.2 Intra-Sheet Formula Evaluation

The `FormulaEvaluator` validates formulas within a single template's data:

| FormulaType | Logic | Example |
|------------|-------|---------|
| Sum | `SUM(operands) = target ± tolerance` | total_cash = cash_notes + cash_coins |
| Difference | `operand[0] - operand[1] = target ± tolerance` | net_income = gross_income - expenses |
| Equals | `operand[0] = target ± tolerance` | balance = total_assets |
| GreaterThan | `target > operand[0]` | capital_ratio > minimum |
| LessThan | `target < operand[0]` | concentration < limit |
| GreaterThanOrEqual | `target >= operand[0] - tolerance` | — |
| LessThanOrEqual | `target <= operand[0] + tolerance` | — |
| Between | `lower - tolerance <= target <= upper + tolerance` | ratio within range |
| Ratio | `target = numerator / denominator` | return_on_assets |
| Custom | `ExpressionParser.Evaluate(expression, variables)` | Complex multi-operand expressions |
| Required | `All operand fields are non-null` | Required field completeness |

**Tolerance Handling:**
```
WithinTolerance(actual, expected, formula):
  Absolute: |actual - expected| ≤ ToleranceAmount
  Percent:  (|actual - expected| / |expected|) × 100 ≤ TolerancePercent
  Pass if EITHER tolerance check passes
```

**Multi-row evaluation:** For MultiRow/ItemCoded templates, each formula is evaluated against every row. Error messages include the row key for identification.

### 11.3 Cross-Sheet Validation

The `CrossSheetValidator` validates rules across different templates:

```
For each CrossSheetRule applicable to current template:
│
├── For each operand (A, B, C...):
│   ├── If same template → use current record
│   ├── If different template → fetch from database (cached per rule evaluation)
│   └── Resolve value:
│       ├── FixedRow → field value from single row
│       └── MultiRow/ItemCoded:
│           ├── Filter by item code (if FilterItemCode set)
│           └── Apply aggregate: SUM, COUNT, MAX, MIN, AVG, or first value
│
├── Build variable map: { "A": 100.50, "B": 100.50 }
├── Evaluate expression via ExpressionParser: "A = B"
├── Check tolerance: |left - right| ≤ tolerance
└── Create ValidationError if fails (includes operand summary)
```

**Example Rule (XS-001):**
```
Rule:  Total Assets = Total Liabilities + Equity
Operand A: MFCR 300, total_assets (Balance Sheet)
Operand B: MFCR 300, total_liabilities_and_equity
Expression: A = B
Tolerance: 0.01
```

### 11.4 Business Rule Evaluation

Four rule types, each with distinct evaluation logic:

| Rule Type | Logic |
|-----------|-------|
| **Completeness** | Check that specified fields (from `AppliesToFields` JSON) are non-null across all rows |
| **DateCheck** | Validate date fields are not in the future (compared to UTC now) |
| **ThresholdCheck** | Evaluate `Expression` per row using ExpressionParser; error if expression fails |
| **Custom** | Evaluate `Expression` per row using ExpressionParser; error if expression fails |

Rules can apply to specific templates (`AppliesToTemplates`: JSON array of return codes) or all templates (`"*"`).

---

## 12. Dynamic Schema Management

### 12.1 DDL Engine

The `DdlEngine` generates SQL Server DDL scripts from template definitions:

**CREATE TABLE** (first version published):
```sql
CREATE TABLE dbo.[mfcr_300] (
    [id] INT IDENTITY(1,1) PRIMARY KEY,
    [submission_id] INT NOT NULL
        REFERENCES dbo.return_submissions(id),
    [cash_notes] DECIMAL(20,2) NULL,
    [cash_coins] DECIMAL(20,2) NULL,
    [total_cash] DECIMAL(20,2) NOT NULL DEFAULT 0,
    [created_at] DATETIME2 DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_mfcr_300_submission_id
    ON dbo.[mfcr_300]([submission_id]);
```

**ALTER TABLE** (subsequent version published):
```sql
-- New field added in version 2
ALTER TABLE dbo.[mfcr_300]
    ADD [new_field] DECIMAL(20,2) NULL;

-- Widened type (INT → BIGINT)
ALTER TABLE dbo.[mfcr_300]
    ALTER COLUMN [existing_field] BIGINT NULL;

-- Removed field (commented, not dropped)
-- Column [old_field] removed in version 2 (preserved)
```

**Rollback Scripts** are auto-generated for each DDL operation:
- CREATE TABLE → DROP TABLE
- ADD COLUMN → DROP COLUMN
- ALTER COLUMN → reverse ALTER

**Safety:**
- Table/column names validated against `^[a-z_][a-z0-9_]*$`
- Only safe widening conversions allowed (INT→BIGINT, VARCHAR width increase, DECIMAL precision increase)

### 12.2 DDL Migration Executor

Executes DDL outside the EF Core transaction (required for DDL statements):

```
Execute(templateId, versionFrom, versionTo, ddlScript, executedBy)
│
├── Execute raw SQL against database
├── Measure execution time (Stopwatch)
├── Record DdlMigrationRecord:
│   ├── TemplateId, VersionFrom, VersionTo
│   ├── MigrationType (CreateTable | AlterTable)
│   ├── DdlScript, RollbackScript
│   ├── ExecutedAt, ExecutedBy
│   └── ExecutionDurationMs
├── On success → MigrationResult(true, null)
└── On SqlException → MigrationResult(false, errorMessage)

Rollback(migrationId, rolledBackBy)
├── Retrieve migration record
├── Execute RollbackScript
├── Update: IsRolledBack=true, RolledBackAt, RolledBackBy
└── Return MigrationResult
```

---

## 13. Authentication & Authorization

### 13.1 API Authentication

| Aspect | Detail |
|--------|--------|
| Scheme | API Key |
| Header | `X-Api-Key` |
| Configuration | `ApiKey` setting in appsettings.json |
| Exempt Paths | `/health`, `/swagger/*` |
| Empty Key | Authentication disabled |
| Comparison | Case-sensitive string match |
| Failure Response | 401 with JSON error body |

### 13.2 Admin Portal Authentication

| Aspect | Detail |
|--------|--------|
| Scheme | Cookie-based (ASP.NET Core CookieAuthentication) |
| Login Path | `/login` |
| Session Duration | 8 hours with sliding expiration |
| Cookie Flags | HttpOnly=true, SameSite=Strict |
| Password Storage | PBKDF2 with HMACSHA256 |
| Password Format | `base64(salt):base64(hash)` |
| PBKDF2 Parameters | 100,000 iterations, 16-byte salt, 32-byte hash |
| Timing Attack Protection | Fixed-time comparison via `CryptographicOperations.FixedTimeEquals` |

**Claims created on login:**
- `NameIdentifier` → User ID
- `Name` → Username
- `Email` → User email
- `Role` → Portal role (Viewer/Approver/Admin)
- `DisplayName` → User display name

### 13.3 Authorization Policies

| Policy | Required Role(s) | Used By |
|--------|-----------------|---------|
| `Authenticated` | Any authenticated user | Most pages |
| `ApproverOrAdmin` | Approver or Admin | Impact Analysis, Audit Log |
| `AdminOnly` | Admin only | User Management, Publishing |

### 13.4 Role Permissions Matrix

| Capability | Viewer | Approver | Admin |
|-----------|--------|----------|-------|
| View templates, submissions, reports | Yes | Yes | Yes |
| View formulas, rules | Yes | Yes | Yes |
| View impact analysis | No | Yes | Yes |
| View audit log | No | Yes | Yes |
| Create templates | No | Yes | Yes |
| Create draft versions | No | Yes | Yes |
| Add fields to drafts | No | Yes | Yes |
| Add/edit/delete formulas | No | Yes | Yes |
| Submit for review | No | Yes | Yes |
| Preview DDL | No | No | Yes |
| Publish versions | No | No | Yes |
| Manage users | No | No | Yes |

---

## 14. Caching Strategy

### 14.1 Template Metadata Cache

**Implementation:** `TemplateMetadataCache` — Singleton with `ConcurrentDictionary<string, CachedTemplate>`

```
┌──────────────┐     ┌────────────────────┐     ┌──────────────┐
│  API/Admin    │────►│TemplateMetadata    │────►│ SQL Server   │
│  Request      │     │Cache (Singleton)   │     │ (only on     │
│               │◄────│                    │◄────│  cache miss) │
│               │     │ ConcurrentDict     │     │              │
└──────────────┘     └────────────────────┘     └──────────────┘
```

**Cache Entry Structure:**
```
CachedTemplate {
  TemplateId, ReturnCode, Name, StructuralCategory,
  PhysicalTableName, XmlRootElement, XmlNamespace,
  CurrentVersion {
    Id, VersionNumber,
    Fields[] (ordered by FieldOrder),
    ItemCodes[],
    IntraSheetFormulas[] (active only, ordered by SortOrder)
  }
}
```

**Cache Behavior:**
- **Key:** `ReturnCode.ToUpperInvariant()` (case-insensitive)
- **Warmup:** `CacheWarmupService` (IHostedService) loads all published templates at startup
- **Invalidation:** Explicit on version publish (`Invalidate(returnCode)`)
- **Thread Safety:** ConcurrentDictionary for concurrent read/write
- **Scope Handling:** Creates DI scope for database access (required since cache is Singleton but DbContext is Scoped)

### 14.2 XSD Schema Cache

**Implementation:** Internal to `XsdGenerator` — `ConcurrentDictionary<string, XmlSchemaSet>`

- Caches compiled XSD schemas per return code
- Invalidated on version publish
- Regenerated on next access

---

## 15. Seeding & Data Initialization

### 15.1 Migrator Execution Pipeline

The `FC.Engine.Migrator` console app runs a six-step initialization:

```
Step 1: Apply EF Core Migrations
  └── Creates metadata and operational tables

Step 2: Execute Metadata Schema Script (optional)
  └── Additional indexes, constraints, custom SQL

Step 3: Seed Templates from schema.sql (if AutoSeed=true)
  └── SeedService.SeedFromSchema()
  └── Creates 103+ templates with fields, publishes them

Step 4: Seed Intra-Sheet Formulas from Schema Patterns
  └── FormulaSeedService.SeedFormulasFromSchema()
  └── Derives formulas from column naming conventions (total_*, net_*)

Step 5: Seed Formulas from JSON Catalog (optional)
  └── FormulaCatalogSeeder.SeedFromCatalog()
  └── CBN official formula definitions with item codes

Step 6: Seed Cross-Sheet Rules
  └── CrossSheetRuleSeedService.SeedCrossSheetRules()
  └── 45 predefined rules (XS-001 through XS-045)

Step 7: Create Default Admin User (if no users exist)
  └── Username: "admin", Password: from config or "Admin@123"
  └── Role: Admin
```

### 15.2 Template Seeding from Schema.sql

The `SeedService` parses `schema.sql` CREATE TABLE statements:

1. **Parse** SQL to extract table names and column definitions
2. **Skip** system tables (institutions, return_periods, etc.)
3. **Derive** return code from table name (`mfcr_300` → `MFCR 300`)
4. **Derive** frequency from prefix, structural category from columns (serial_no → MultiRow, item_code → ItemCoded)
5. **Create** template with auto-generated physical table name, XML root, and namespace
6. **Add fields** from columns: map SQL types to domain types, detect computed fields (total_*, net_*), detect YTD fields
7. **Publish immediately** (templates already exist in database, DDL not needed)

### 15.3 Formula Derivation from Schema Patterns

The `FormulaSeedService` analyzes column naming patterns to derive validation formulas:

| Pattern | Rule | Example |
|---------|------|---------|
| `total_*` columns | SUM of preceding non-total columns | `total_cash = cash_notes + cash_coins` |
| `net_*` columns | DIFFERENCE of two operands | `net_income = total_income - total_expenses` |
| `carrying_amount_*` | DIFFERENCE | `carrying_amount = book_value - impairment` |
| `profit_after_tax` | DIFFERENCE | `profit_after_tax = profit_before_tax - tax_expense` |
| `total_comprehensive_income` | SUM | `= profit_after_tax + other_comprehensive_income` |
| `*_ytd` columns | YTD mirror of non-YTD formula | Creates matching _ytd variant |

### 15.4 Cross-Sheet Rule Categories (45 Rules)

| Category | Rule Range | Count | Description |
|----------|-----------|-------|-------------|
| Balance Sheet Integrity | XS-001 to XS-010 | 10 | Asset/liability reconciliation |
| Income Statement Integrity | XS-011 to XS-020 | 10 | Revenue/expense reconciliation |
| Cross-Return Consistency | XS-021 to XS-035 | 15 | Template-to-template validation |
| Quarterly Cross-Checks | XS-036 to XS-045 | 10 | Quarterly vs monthly consistency |

**Example Rules:**
- **XS-001:** Total Assets = Total Liabilities + Equity (MFCR 300)
- **XS-011:** Net Interest Income = Interest Income - Interest Expense
- **XS-021:** Loan Impairment cross-check between templates (Warning severity)
- **XS-036:** Quarterly vs Monthly totals consistency (Warning severity)

---

## 16. Deployment Architecture

### 16.1 Docker Compose Topology

```
┌──────────────────────────────────────────────────────────┐
│                    Docker Compose                         │
│                                                           │
│  ┌─────────────────┐                                      │
│  │   sqlserver      │  SQL Server 2022                    │
│  │   Port: 1433     │  Developer Edition                  │
│  │   Volume: data   │  Healthcheck: SELECT 1              │
│  └────────┬────────┘                                      │
│           │ healthy                                       │
│  ┌────────▼────────┐                                      │
│  │   migrator       │  One-shot migration + seeding       │
│  │                  │  Mounts: schema.sql, scripts/ (ro)  │
│  │                  │  Exit: 0 on success, 1 on failure   │
│  └────────┬────────┘                                      │
│           │ completed_successfully                        │
│     ┌─────┴─────┐                                         │
│     │           │                                         │
│  ┌──▼──────────┐  ┌──────────────┐                       │
│  │  api         │  │  admin        │                      │
│  │  Port: 5100  │  │  Port: 5200   │                      │
│  │  (prod)      │  │  (prod)       │                      │
│  │  Port: 5000  │  │  Port: 5001   │                      │
│  │  (dev)       │  │  (dev)        │                      │
│  │  Health: GET │  │  Health: GET  │                      │
│  │  /health     │  │  /            │                      │
│  └──────────────┘  └──────────────┘                      │
│                                                           │
└──────────────────────────────────────────────────────────┘
```

### 16.2 Container Images

All images use multi-stage builds:

| Image | Base | SDK | Entry Point |
|-------|------|-----|-------------|
| API | `dotnet/aspnet:10.0-preview` | `dotnet/sdk:10.0-preview` | `dotnet FC.Engine.Api.dll` |
| Admin | `dotnet/aspnet:10.0-preview` | `dotnet/sdk:10.0-preview` | `dotnet FC.Engine.Admin.dll` |
| Migrator | `dotnet/aspnet:10.0-preview` | `dotnet/sdk:10.0-preview` | `dotnet FC.Engine.Migrator.dll` |

### 16.3 Environment Configuration

| Variable | Purpose | Default |
|----------|---------|---------|
| `ConnectionStrings__FcEngine` | SQL Server connection string | — |
| `SA_PASSWORD` | SQL Server SA password | `YourStrong@Passw0rd` |
| `ADMIN_PASSWORD` | Default admin user password | `Admin@123` |
| `API_KEY` | API key (empty = auth disabled) | — |
| `Seeding__SchemaFilePath` | Path to schema.sql | `/app/schema.sql` |
| `Seeding__MetadataSchemaPath` | Path to metadata schema SQL | — |
| `Seeding__FormulaCatalogPath` | Path to formula catalog JSON | — |
| `Seeding__AutoSeed` | Auto-seed on migration | `true` |
| `ASPNETCORE_ENVIRONMENT` | ASP.NET environment | `Production` |

### 16.4 Service Dependencies & Health Checks

```
sqlserver  ──(healthy)──►  migrator  ──(completed)──►  api
                                      ──(completed)──►  admin
```

| Service | Health Check | Interval | Retries | Start Period |
|---------|-------------|----------|---------|--------------|
| sqlserver | `sqlcmd "SELECT 1"` | 10s | 10 | 30s |
| api | `curl /health` | 10s | 5 | 15s |
| admin | `curl /` | 10s | 5 | 15s |

---

## 17. Testing Strategy

### 17.1 Test Project Structure

| Project | Scope | Tests | Framework |
|---------|-------|-------|-----------|
| `FC.Engine.Domain.Tests` | Domain entities, value objects, data records | ~35 | xUnit + FluentAssertions |
| `FC.Engine.Infrastructure.Tests` | Services, validators, persistence, DDL | ~100+ | xUnit + FluentAssertions + Moq |
| `FC.Engine.Integration.Tests` | End-to-end API tests | — | xUnit + WebApplicationFactory |

### 17.2 Domain Tests

| Test Class | Covers | Key Verifications |
|-----------|--------|-------------------|
| `ReturnDataRecordTests` | ReturnDataRecord, ReturnDataRow | Field storage/retrieval, type conversion, category handling, case-insensitive access |
| `DomainEntityTests` | Submission lifecycle, ValidationReport, ReturnTemplate, CrossSheetRule | State transitions (Draft→Accepted), error aggregation, version management, operand handling |
| `TemplateVersionTests` | TemplateVersion lifecycle | Draft→Review→Published transitions, field/formula management, DDL script storage |
| `ReturnCodeTests` | ReturnCode value object | Parsing, normalization, ToTableName/ToXmlRootElement/ToXmlNamespace, equality |

### 17.3 Infrastructure Tests

| Test Class | Covers | Mocking |
|-----------|--------|---------|
| `AuthServiceTests` (14 tests) | Login, user creation, password hashing, change password | IPortalUserRepository |
| `FormulaServiceTests` (17 tests) | Formula CRUD, cross-sheet rules, business rules, audit logging | IFormulaRepository, ITemplateRepository, IAuditLogger |
| `TemplateServiceTests` (18 tests) | Template creation, field addition, detail retrieval, SQL type mapping | ITemplateRepository, IAuditLogger, ITemplateMetadataCache, ISqlTypeMapper |
| `TemplateVersioningServiceTests` (19 tests) | Draft creation with cloning, review submission, DDL preview, publishing with deprecation | IDdlEngine, IDdlMigrationExecutor, ITemplateMetadataCache, IXsdGenerator, IAuditLogger |
| `OrchestratorTests` | Multi-phase validation pipeline, phase gating logic | ITemplateMetadataCache, IFormulaEvaluator, ICrossSheetValidator, IBusinessRuleEvaluator |
| `FormulaCatalogSeederTests` (5 tests) | JSON catalog seeding, item code mapping, skip logic, error recording | ITemplateRepository, IFormulaRepository |
| `ExpressionTokenizerTests` (11 tests) | Tokenization: numbers, variables, operators, functions, errors | None (unit) |
| `ExpressionParserTests` (18 tests) | Arithmetic, comparisons, precedence, parentheses, functions, division by zero | None (unit) |
| `FormulaEvaluatorTests` | Sum/Difference/Custom formulas, tolerance, multi-row evaluation | ITemplateMetadataCache |
| `GenericXmlParserTests` | FixedRow/MultiRow/ItemCoded XML parsing, namespace handling | ITemplateMetadataCache |
| `DynamicSqlBuilderTests` (6 tests) | SQL generation, parameterization, injection prevention | None (unit) |
| `DdlEngineTests` | CREATE/ALTER TABLE generation, type widening, rollback scripts | ISqlTypeMapper |

### 17.4 Testing Patterns

**Arrange-Act-Assert (AAA):** All tests follow the AAA pattern with clear separation.

**Mocking with Moq:**
```csharp
// Setup
var mockRepo = new Mock<ITemplateRepository>();
mockRepo.Setup(r => r.GetByReturnCode("MFCR 300", It.IsAny<CancellationToken>()))
    .ReturnsAsync(template);

// Capture arguments
ReturnTemplate capturedTemplate = null!;
mockRepo.Setup(r => r.Update(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()))
    .Callback<ReturnTemplate, CancellationToken>((t, _) => capturedTemplate = t)
    .Returns(Task.CompletedTask);

// Verify
mockRepo.Verify(r => r.Update(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()), Times.Once);
mockRepo.Verify(r => r.Add(It.IsAny<ReturnTemplate>(), It.IsAny<CancellationToken>()), Times.Never);
```

**Theory-based Tests:** Parameterized tests with `[InlineData]` for multiple input scenarios.

**FluentAssertions:**
```csharp
result.Should().NotBeNull();
result.Status.Should().Be(SubmissionStatus.Accepted);
result.ErrorCount.Should().Be(0);
action.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
result.PasswordHash.Should().Contain(":");
```

---

## 18. Cross-Cutting Concerns

### 18.1 Audit Logging

All mutation operations are audited via `IAuditLogger`:

```
AuditLogEntry {
  EntityType: "Template" | "TemplateVersion" | "IntraSheetFormula" | "CrossSheetRule" | "BusinessRule" | "PortalUser"
  EntityId:   int
  Action:     "Created" | "Updated" | "Deleted" | "SubmittedForReview" | "Published" | ...
  OldValues:  JSON (before state, null for creates)
  NewValues:  JSON (after state, null for deletes)
  PerformedBy: string (username)
  PerformedAt: DateTime (UTC)
}
```

Audit entries are stored in `meta.audit_log` and viewable in the Admin Portal's Audit Log page (ApproverOrAdmin access).

### 18.2 Structured Logging

Serilog is configured across all applications:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.EntityFrameworkCore": "Warning"
      }
    }
  }
}
```

Development mode uses `Debug` minimum level.

### 18.3 Error Handling Patterns

| Pattern | Used In | Behavior |
|---------|---------|----------|
| **Throwing InvalidOperationException** | Service methods | Resource not found, invalid state transitions, constraint violations |
| **Returning null/empty** | Repository queries, optional lookups | Template not found, no formulas |
| **Error accumulation** | ValidationOrchestrator, SeedService | Collect all errors, don't fail fast |
| **Try-catch with error result** | IngestionOrchestrator | Catch all exceptions, create SYSTEM error, record duration |
| **Idempotent seeding** | All seed services | Check for existing data before seeding |

### 18.4 Database Resilience

EF Core is configured with retry-on-failure:
```
CommandTimeout: 30 seconds
RetryOnFailure: 3 attempts
```

DDL execution uses separate connection (outside EF transaction) for DDL compatibility.

---

## 19. Key Design Decisions

### 19.1 Dual ORM Strategy (EF Core + Dapper)

**Decision:** Use EF Core for metadata tables (fixed schema) and Dapper for dynamic data tables (schema defined at runtime).

**Rationale:** Template definitions create physical tables whose columns are determined by template field configurations. EF Core cannot map to tables whose schema changes at runtime. Dapper's raw SQL capability and parameterized query support make it ideal for this dynamic access pattern.

### 19.2 ReturnDataRecord Universal Container

**Decision:** Replace 103+ per-template strongly-typed data classes with a single generic `ReturnDataRecord` using a dictionary-based field store.

**Rationale:** Templates are user-configurable and their field schemas change with versions. A generic container avoids code generation and recompilation when templates change, while maintaining type conversion support through `GetDecimal()`, `GetString()`, `GetDateTime()` methods.

### 19.3 Expression Parser (Custom vs. Library)

**Decision:** Build a custom expression tokenizer and parser using the Shunting-yard algorithm rather than using a library like NCalc or Flee.

**Rationale:** The expression language is purpose-built for financial validation — it needs comparison operators, tolerance-aware evaluation, aggregate functions, and variable resolution from return data records. A custom implementation gives full control over these domain-specific requirements.

### 19.4 Singleton Cache with Scope Factory

**Decision:** `TemplateMetadataCache` is registered as Singleton but creates DI scopes for database access.

**Rationale:** The cache must outlive individual requests for performance, but EF Core DbContext is Scoped. Using `IServiceScopeFactory` to create transient scopes when loading from database bridges this lifetime mismatch cleanly.

### 19.5 Soft Delete for Validation Rules

**Decision:** FormulaRepository uses `IsActive = false` instead of physical deletion.

**Rationale:** Historical validation reports reference rule codes. Soft deletion preserves referential integrity and audit trail while removing rules from active validation.

### 19.6 Enum-as-String Storage

**Decision:** All enum properties are stored as strings in the database, not integers.

**Rationale:** Improves database readability and debugging. Status values like "Published" and "Draft" are human-readable without requiring enum mapping lookups.

### 19.7 DDL Safety (No Column Drops)

**Decision:** The DDL engine never drops columns — removed fields are commented out in ALTER scripts.

**Rationale:** Dropping columns can cause data loss. Preserving columns (even unused ones) maintains backward compatibility with previously submitted data.

### 19.8 XSD Schema Generation at Runtime

**Decision:** Generate XSD schemas dynamically from template definitions rather than storing static XSD files.

**Rationale:** Templates are versioned and fields can change between versions. Dynamic generation ensures schemas always match the current published template version without manual synchronization.

---

## 20. Appendix

### 20.1 Configuration Files

| File | Location | Purpose |
|------|----------|---------|
| `appsettings.json` (API) | `src/FC.Engine.Api/` | API connection string, API key, seeding config, Serilog |
| `appsettings.json` (Admin) | `src/FC.Engine.Admin/` | Admin connection string, seeding config, Serilog |
| `appsettings.Development.json` | Both API and Admin | Development overrides (local schema path, debug logging) |
| `docker-compose.yml` | `docker/` | Production service definitions |
| `docker-compose.override.yml` | `docker/` | Development port and environment overrides |
| `.env.example` | `docker/` | Environment variable template |
| `schema.sql` | Root | Source-of-truth DDL for 103+ return table schemas |

### 20.2 Running Commands

```bash
# Apply EF Core migrations
cd "FC Engine"
dotnet ef database update --project src/FC.Engine.Infrastructure --startup-project src/FC.Engine.Api

# Add a new migration
dotnet ef migrations add <Name> --project src/FC.Engine.Infrastructure --startup-project src/FC.Engine.Api

# Run all tests
dotnet test

# Run specific test project
dotnet test tests/FC.Engine.Domain.Tests
dotnet test tests/FC.Engine.Infrastructure.Tests

# Docker Compose (production)
cd docker && docker compose up -d

# Docker Compose (development)
cd docker && docker compose -f docker-compose.yml -f docker-compose.override.yml up -d

# Start API locally
dotnet run --project src/FC.Engine.Api

# Start Admin locally
dotnet run --project src/FC.Engine.Admin

# Run migrator locally
dotnet run --project src/FC.Engine.Migrator
```

### 20.3 Default Credentials

| System | Username | Password | Notes |
|--------|----------|----------|-------|
| Admin Portal | `admin` | `Admin@123` | Created by Migrator if no users exist |
| SQL Server | `sa` | `YourStrong@Passw0rd` | Docker default |
| API | — | Via `API_KEY` env var | Empty = auth disabled |

### 20.4 Key File Reference

| File | Description |
|------|-------------|
| [FCEngine.sln](FC Engine/FCEngine.sln) | Solution file |
| [ReturnTemplate.cs](FC Engine/src/FC.Engine.Domain/Metadata/ReturnTemplate.cs) | Core template entity with version management |
| [TemplateVersion.cs](FC Engine/src/FC.Engine.Domain/Metadata/TemplateVersion.cs) | Version lifecycle (Draft → Review → Published) |
| [Submission.cs](FC Engine/src/FC.Engine.Domain/Entities/Submission.cs) | Submission entity with status transitions |
| [ReturnDataRecord.cs](FC Engine/src/FC.Engine.Domain/DataRecord/ReturnDataRecord.cs) | Universal data container |
| [ReturnCode.cs](FC Engine/src/FC.Engine.Domain/ValueObjects/ReturnCode.cs) | Value object with parsing/transformation |
| [IngestionOrchestrator.cs](FC Engine/src/FC.Engine.Application/Services/IngestionOrchestrator.cs) | XML submission processing pipeline |
| [ValidationOrchestrator.cs](FC Engine/src/FC.Engine.Application/Services/ValidationOrchestrator.cs) | Multi-phase validation coordinator |
| [ExpressionParser.cs](FC Engine/src/FC.Engine.Infrastructure/Validation/ExpressionParser.cs) | Shunting-yard expression evaluator |
| [DdlEngine.cs](FC Engine/src/FC.Engine.Infrastructure/DynamicSchema/DdlEngine.cs) | DDL script generation |
| [MetadataDbContext.cs](FC Engine/src/FC.Engine.Infrastructure/Metadata/MetadataDbContext.cs) | EF Core context with 17 DbSets |
| [DependencyInjection.cs](FC Engine/src/FC.Engine.Infrastructure/DependencyInjection.cs) | Infrastructure service registration |
| [Program.cs (API)](FC Engine/src/FC.Engine.Api/Program.cs) | API startup configuration |
| [Program.cs (Admin)](FC Engine/src/FC.Engine.Admin/Program.cs) | Admin portal startup configuration |
| [Program.cs (Migrator)](FC Engine/src/FC.Engine.Migrator/Program.cs) | Database migration orchestration |
