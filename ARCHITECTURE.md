# RegOS™ — Complete System Architecture

**Purpose:** This document provides a comprehensive architecture reference for AI agents tasked with expanding RegOS™ into a complete multi-tenant system.

---

## 1. Executive Summary

RegOS™ is a **regulatory return collection and validation engine** built for the Central Bank of Nigeria (CBN). It automates ingestion, structural validation, formula evaluation, and cross-sheet reconciliation of financial returns submitted in XML format by Finance Companies (FCs).

### What the System Does
1. **Template Management** — Define return templates (schemas) with fields, sections, formulas, and validation rules
2. **XML Submission** — Financial institutions submit regulatory returns as XML files
3. **Multi-Phase Validation** — 4-phase pipeline: Type/Range → Intra-Sheet Formulas → Cross-Sheet Rules → Business Rules
4. **Dynamic Schema** — DDL engine creates/migrates SQL tables on-the-fly from template metadata
5. **Maker-Checker Workflow** — Submission approval flow with role-based access
6. **Reporting & Compliance** — Dashboard, compliance reports, audit trail, certificates

### Current State
- Single database instance (not multi-tenant)
- Institution-based data partitioning via `institution_id` foreign keys
- 103 return template tables derived from CBN Excel workbook
- Two Blazor Server portals (Admin + Institution) + REST API

---

## 2. Technology Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| Runtime | .NET | 10.0 (Preview) |
| Web Framework | ASP.NET Core Minimal APIs | 10.0 |
| UI Framework | Blazor Server (Interactive SSR) | 10.0 |
| ORM (Metadata) | Entity Framework Core | 8.0.11 |
| ORM (Dynamic Data) | Dapper | 2.1.35 |
| Database | SQL Server | 2022 |
| Auth (Blazor) | Cookie-based ASP.NET Core Auth | — |
| Auth (API) | API Key via middleware | — |
| Logging | Serilog | 8.0.3 |
| API Docs | Swashbuckle/Swagger | 6.9.0 |
| Testing | xUnit + FluentAssertions | — |
| Caching | IMemoryCache + ConcurrentDictionary | — |
| Containerization | Docker Compose | — |

---

## 3. Solution Structure (Clean Architecture)

```
RegOS™/
├── src/
│   ├── FC.Engine.Domain/          # Core domain — entities, enums, abstractions (interfaces)
│   ├── FC.Engine.Application/     # Business logic — services, orchestrators, DTOs
│   ├── FC.Engine.Infrastructure/  # Data access — EF Core, Dapper, validation engines
│   ├── FC.Engine.Api/             # REST API — Minimal API endpoints, middleware
│   ├── FC.Engine.Admin/           # Admin portal — Blazor Server (template/rule mgmt)
│   ├── FC.Engine.Portal/          # FI Portal — Blazor Server (submissions, approvals)
│   └── FC.Engine.Migrator/        # DB migration & seeding console app
├── tests/
│   ├── FC.Engine.Domain.Tests/
│   ├── FC.Engine.Infrastructure.Tests/
│   └── FC.Engine.Integration.Tests/
├── docker/                         # Docker Compose + Dockerfiles
├── scripts/                        # SQL seed scripts
├── schema.sql                      # Source-of-truth: 103 return table DDL
├── SOLUTION_DESIGN.md              # Detailed design document
└── formula_catalog.json            # Formula definitions catalog
```

### Dependency Flow
```
Domain ← Application ← Infrastructure
                ↑              ↑
              Admin          Portal
                ↑              ↑
               API          Migrator
```

- **Domain** has ZERO dependencies (pure C#)
- **Application** depends only on Domain
- **Infrastructure** depends on Domain + Application + NuGet packages
- **Admin/Portal/Api** depend on all lower layers

---

## 4. Domain Layer (`FC.Engine.Domain`)

### 4.1 Entities

| Entity | File | Purpose |
|--------|------|---------|
| `Submission` | `Entities/Submission.cs` | Tracks XML return submissions with status lifecycle |
| `ValidationReport` | `Entities/ValidationReport.cs` | Aggregates validation errors/warnings per submission |
| `ValidationError` | `Entities/ValidationError.cs` | Individual validation failure (rule, field, severity) |
| `Institution` | `Entities/Institution.cs` | Financial institution with maker-checker settings |
| `InstitutionUser` | `Entities/InstitutionUser.cs` | FI Portal user with institution FK, role |
| `PortalUser` | `Entities/PortalUser.cs` | Admin portal user with password hash, lockout |
| `SubmissionApproval` | `Entities/SubmissionApproval.cs` | Maker-checker approval workflow record |
| `PortalNotification` | `Entities/PortalNotification.cs` | In-app notification with read/unread state |
| `ReturnPeriod` | `Entities/ReturnPeriod.cs` | Reporting period (year, month, date, open flag) |
| `LoginAttempt` | `Entities/LoginAttempt.cs` | Security audit (IP, success/failure, reason) |
| `PasswordResetToken` | `Entities/PasswordResetToken.cs` | One-time password reset with 1hr expiry |

### 4.2 Template Metadata

| Entity | File | Purpose |
|--------|------|---------|
| `ReturnTemplate` | `Metadata/ReturnTemplate.cs` | Template definition (return code, table name, namespace) |
| `TemplateVersion` | `Metadata/TemplateVersion.cs` | Version lifecycle: Draft → Review → Published → Deprecated |
| `TemplateField` | `Metadata/TemplateField.cs` | Field definition (data type, required, min/max, length) |
| `TemplateSection` | `Metadata/TemplateSection.cs` | Logical field grouping |
| `TemplateItemCode` | `Metadata/TemplateItemCode.cs` | Allowed codes for ItemCoded structural category |
| `IntraSheetFormula` | `Metadata/IntraSheetFormula.cs` | Intra-sheet validation (Sum, Equals, Between, etc.) |

### 4.3 Validation Rules

| Entity | File | Purpose |
|--------|------|---------|
| `CrossSheetRule` | `Validation/CrossSheetRule.cs` | Multi-template validation with operands + expressions |
| `BusinessRule` | `Validation/BusinessRule.cs` | Complex business logic tied to submission context |

### 4.4 Value Objects

| Class | File | Purpose |
|-------|------|---------|
| `ReturnCode` | `ValueObjects/ReturnCode.cs` | Strongly typed return code with parsing |
| `ReportingPeriod` | `ValueObjects/ReportingPeriod.cs` | Year/month tuple with date calculations |
| `ReturnDataRecord` | `DataRecord/ReturnDataRecord.cs` | Parsed XML data structure with typed rows |

### 4.5 Enumerations

| Enum | Values |
|------|--------|
| `SubmissionStatus` | Draft, Parsing, Validating, Accepted, AcceptedWithWarnings, Rejected, PendingApproval, ApprovalRejected |
| `ApprovalStatus` | Pending, Approved, Rejected |
| `TemplateStatus` | Draft, Review, Published, Deprecated |
| `ReturnFrequency` | Monthly, Quarterly, SemiAnnual, Annual |
| `StructuralCategory` | FixedRow, MultiRow, ItemCoded |
| `FieldDataType` | Text, Integer, Decimal, Money, Percentage, Date, Boolean |
| `FormulaType` | Sum, Difference, Equals, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, Between, Ratio, Custom, Required |
| `ValidationSeverity` | Error, Warning |
| `ValidationCategory` | TypeRange, IntraSheet, CrossSheet, Schema, Business |
| `PortalRole` | Admin, Operator, Viewer |
| `InstitutionRole` | Admin, Maker, Checker, Viewer |

### 4.6 Abstractions (Interfaces)

All 18 interfaces live in `Domain/Abstractions/`:

**Repositories:**
- `ISubmissionRepository` — Submission CRUD + queries by institution/period
- `ITemplateRepository` — Template + version CRUD with eager loading
- `IFormulaRepository` — Formulas, cross-sheet rules, business rules
- `IGenericDataRepository` — Dynamic table read/write via Dapper
- `IInstitutionRepository` — Institution configuration
- `IInstitutionUserRepository` — FI Portal user management
- `IPortalUserRepository` — Admin user management
- `ISubmissionApprovalRepository` — Approval workflow
- `IPortalNotificationRepository` — Notification persistence

**Services/Engines:**
- `IFormulaEvaluator` — Evaluate intra-sheet formulas
- `ICrossSheetValidator` — Validate cross-template rules
- `IBusinessRuleEvaluator` — Evaluate business rules
- `ITemplateMetadataCache` — Thread-safe template cache
- `IDdlEngine` — Generate CREATE/ALTER TABLE SQL
- `IDdlMigrationExecutor` — Execute DDL against database
- `IGenericXmlParser` — Parse XML to ReturnDataRecord
- `IXsdGenerator` — Generate XSD from template metadata
- `IAuditLogger` — Audit trail logging
- `ISqlTypeMapper` — Map FieldDataType → SQL Server types

---

## 5. Application Layer (`FC.Engine.Application`)

### 5.1 Core Services

| Service | File | Responsibility |
|---------|------|----------------|
| `IngestionOrchestrator` | `Services/IngestionOrchestrator.cs` | End-to-end XML submission processing (create → XSD validate → parse → validate → persist) |
| `ValidationOrchestrator` | `Services/ValidationOrchestrator.cs` | 4-phase validation pipeline orchestration |
| `TemplateService` | `Services/TemplateService.cs` | Template CRUD, auto-generates table names + XML namespaces |
| `TemplateVersioningService` | `Services/TemplateVersioningService.cs` | Version lifecycle (Draft→Review→Published), DDL generation |
| `FormulaService` | `Services/FormulaService.cs` | CRUD for intra-sheet formulas, cross-sheet rules, business rules |
| `AuthService` | `Services/AuthService.cs` | Admin portal auth (PBKDF2-SHA256, 100k iterations, lockout) |
| `InstitutionAuthService` | `Services/InstitutionAuthService.cs` | FI Portal auth (same PBKDF2, lockout: 5 attempts → 15min) |
| `SeedService` | `Services/SeedService.cs` | Parse schema.sql and seed templates |
| `FormulaSeedService` | `Services/FormulaSeedService.cs` | Seed formula definitions |
| `CrossSheetRuleSeedService` | `Services/CrossSheetRuleSeedService.cs` | Seed cross-sheet validation rules |
| `FormulaCatalogSeeder` | `Services/FormulaCatalogSeeder.cs` | Seed from formula_catalog.json |
| `BusinessRuleSeedService` | `Services/BusinessRuleSeedService.cs` | Seed business rules |

### 5.2 DTOs

| DTO | File | Purpose |
|-----|------|---------|
| `SubmissionDto` | `DTOs/SubmissionDto.cs` | Submission result, validation report transfer |
| `TemplateDto` | `DTOs/TemplateDto.cs` | Template detail, field list, version info |

### 5.3 Validation Pipeline Detail

```
IngestionOrchestrator.Process()
  1. Create Submission record (status: Parsing)
  2. XSD Validation (IXsdGenerator → validate XML structure)
  3. XML Parsing (IGenericXmlParser → ReturnDataRecord)
  4. ValidationOrchestrator.Validate()
     ├── Phase 1: Type/Range (required, min/max, length, allowed values)
     ├── Phase 2: Intra-Sheet Formulas (Sum, Equals, Between, etc.)
     ├── Phase 3: Cross-Sheet Rules (skip if Phase 2 fails)
     └── Phase 4: Business Rules
  5. Persist data to dynamic table (if valid)
  6. Return SubmissionResultDto with status + ValidationReport
```

---

## 6. Infrastructure Layer (`FC.Engine.Infrastructure`)

### 6.1 Data Access

| Component | File | Purpose |
|-----------|------|---------|
| `MetadataDbContext` | `Metadata/MetadataDbContext.cs` | EF Core DbContext for all metadata + operational tables |
| `GenericDataRepository` | `Persistence/GenericDataRepository.cs` | Dapper-based dynamic table I/O (replaces 103 EF entity classes) |
| `DynamicSqlBuilder` | `Persistence/DynamicSqlBuilder.cs` | Builds INSERT/SELECT/UPDATE/DELETE for any physical table |
| `SubmissionRepository` | `Persistence/Repositories/SubmissionRepository.cs` | Submission queries with eager loading |

**EF Core Configurations** (`Metadata/Configurations/`):
- `ReturnTemplateConfiguration.cs` — Template + Version + Field + Section + ItemCode
- `OperationalConfigurations.cs` — Submission, ValidationReport, ReturnPeriod
- `SecurityConfigurations.cs` — LoginAttempt, PasswordResetToken
- `PortalUserConfiguration.cs` — Admin users
- `InstitutionUserConfiguration.cs` — FI Portal users
- `SubmissionApprovalConfiguration.cs` — Approval workflow
- `PortalNotificationConfiguration.cs` — Notifications
- `ValidationRuleConfigurations.cs` — CrossSheetRule, BusinessRule

### 6.2 Metadata Repositories

| Repository | Interface | Purpose |
|------------|-----------|---------|
| `TemplateRepository` | `ITemplateRepository` | Template + version CRUD with eager loading |
| `FormulaRepository` | `IFormulaRepository` | Formula and rule management |
| `InstitutionRepository` | `IInstitutionRepository` | Institution configuration |
| `InstitutionUserRepository` | `IInstitutionUserRepository` | FI Portal users |
| `PortalUserRepository` | `IPortalUserRepository` | Admin users |
| `SubmissionApprovalRepository` | `ISubmissionApprovalRepository` | Approval records |
| `PortalNotificationRepository` | `IPortalNotificationRepository` | Notifications |
| `LoginAttemptRepository` | — | Login audit |
| `PasswordResetTokenRepository` | — | Reset tokens |

### 6.3 Validation Engines

| Engine | File | Purpose |
|--------|------|---------|
| `FormulaEvaluator` | `Validation/FormulaEvaluator.cs` | Evaluates intra-sheet formulas (Sum, Difference, Equals, Between, Ratio, Custom, Required) with tolerance matching |
| `CrossSheetValidator` | `Validation/CrossSheetValidator.cs` | Validates cross-template rules, loads dependent records from other tables |
| `BusinessRuleEvaluator` | `Validation/BusinessRuleEvaluator.cs` | Evaluates business rules with conditional logic |
| `ExpressionParser` | `Validation/ExpressionParser.cs` | Parses mathematical/logical expressions with variable substitution |
| `ExpressionTokenizer` | `Validation/ExpressionTokenizer.cs` | Tokenizes expression strings |

### 6.4 Dynamic Schema Management

| Component | File | Purpose |
|-----------|------|---------|
| `DdlEngine` | `DynamicSchema/DdlEngine.cs` | Generates CREATE TABLE and ALTER TABLE SQL from template metadata |
| `DdlMigrationExecutor` | `DynamicSchema/DdlMigrationExecutor.cs` | Executes DDL against SQL Server, logs changes |
| `SqlTypeMapper` | `DynamicSchema/SqlTypeMapper.cs` | Maps FieldDataType enum → SQL Server types |

### 6.5 XML & Schema

| Component | File | Purpose |
|-----------|------|---------|
| `GenericXmlParser` | `Xml/GenericXmlParser.cs` | Parses XML → ReturnDataRecord using template metadata |
| `XsdGenerator` | `Xml/XsdGenerator.cs` | Generates XSD schemas from template metadata, caches results |

### 6.6 Caching

| Component | File | Purpose |
|-----------|------|---------|
| `TemplateMetadataCache` | `Caching/TemplateMetadataCache.cs` | Thread-safe ConcurrentDictionary, lazy loads published templates |
| `CacheWarmupService` | `Caching/CacheWarmupService.cs` | IHostedService that preloads all published templates on startup |

### 6.7 Audit

| Component | File | Purpose |
|-----------|------|---------|
| `AuditLogger` | `Audit/AuditLogger.cs` | Persists all entity changes with before/after snapshots |

### 6.8 Dependency Injection (`DependencyInjection.cs`)

```csharp
// Database
AddDbContextPool<MetadataDbContext>(connectionString)
AddScoped<IDbConnection>(SqlConnection)  // Dapper

// All repositories (10+)
AddScoped<ITemplateRepository, TemplateRepository>()
AddScoped<ISubmissionRepository, SubmissionRepository>()
// ... etc

// Validation engines
AddScoped<IFormulaEvaluator, FormulaEvaluator>()
AddScoped<ICrossSheetValidator, CrossSheetValidator>()
AddScoped<IBusinessRuleEvaluator, BusinessRuleEvaluator>()

// Dynamic schema
AddScoped<IDdlEngine, DdlEngine>()
AddScoped<IDdlMigrationExecutor, DdlMigrationExecutor>()
AddSingleton<ISqlTypeMapper, SqlTypeMapper>()

// XML & Schema
AddScoped<IGenericXmlParser, GenericXmlParser>()
AddScoped<IXsdGenerator, XsdGenerator>()

// Caching
AddSingleton<ITemplateMetadataCache, TemplateMetadataCache>()
AddHostedService<CacheWarmupService>()

// Audit
AddScoped<IAuditLogger, AuditLogger>()
```

---

## 7. API Layer (`FC.Engine.Api`)

### 7.1 REST Endpoints

**Submission Endpoints:**
| Method | Route | Purpose |
|--------|-------|---------|
| `POST` | `/api/submissions/{returnCode}` | Submit XML return (Content-Type: application/xml) |
| `GET` | `/api/submissions/{id}` | Get submission with validation report |
| `GET` | `/api/submissions/institution/{institutionId}` | List submissions for institution |

**Template Endpoints:**
| Method | Route | Purpose |
|--------|-------|---------|
| `GET` | `/api/templates` | List all published templates |
| `GET` | `/api/templates/{returnCode}` | Get template detail with versions/fields |
| `POST` | `/api/templates` | Create new template |
| `POST` | `/api/templates/{id}/versions` | Create draft version |
| `POST` | `/api/templates/{id}/versions/{vid}/fields` | Add field to version |
| `POST` | `/api/templates/{id}/versions/{vid}/submit` | Submit version for review |
| `POST` | `/api/templates/{id}/versions/{vid}/preview-ddl` | Preview DDL changes |
| `POST` | `/api/templates/{id}/versions/{vid}/publish` | Publish version |
| `GET` | `/api/templates/{id}/versions/{vid}/formulas` | Get formulas for version |

**Schema Endpoints:**
| Method | Route | Purpose |
|--------|-------|---------|
| `GET` | `/api/schemas/{returnCode}/xsd` | Generate XSD for template |
| `GET` | `/api/schemas/published` | List published templates |
| `POST` | `/api/schemas/seed` | Seed from schema.sql |
| `POST` | `/api/schemas/seed-formulas` | Seed formulas |

**Health:**
| Method | Route | Purpose |
|--------|-------|---------|
| `GET` | `/health` | Health check |

### 7.2 Authentication
- `ApiKeyMiddleware` validates `X-Api-Key` header
- Skips `/health` and `/swagger` routes

---

## 8. Admin Portal (`FC.Engine.Admin`)

### 8.1 Pages

| Page | Route | Purpose |
|------|-------|---------|
| `Dashboard` | `/` | System overview, template stats |
| `Login` | `/account/login` | Admin login |
| `Logout` | `/account/logout` | Logout |
| `ForgotPassword` | `/account/forgot-password` | Password reset request |
| `ResetPassword` | `/account/reset-password` | Password reset form |
| `UserManagement` | `/account/users` | Admin user CRUD |
| `TemplateList` | `/templates` | Template catalog management |
| `TemplateDetail` | `/templates/{id}` | Template editor (fields, sections, versions) |
| `FormulaList` | `/formulas` | Formula catalog |
| `BusinessRuleList` | `/validation/rules` | Business rule management |
| `CrossSheetRuleList` | `/validation/cross-sheet` | Cross-sheet rule management |
| `CrossSheetRuleDetail` | `/validation/cross-sheet/{id}` | Cross-sheet rule editor |
| `SubmissionList` | `/submissions` | All submissions browser |
| `SubmissionDetail` | `/submissions/{id}` | Submission review |
| `AuditLog` | `/audit` | Audit trail viewer |
| `ImpactAnalysis` | `/analysis/impact` | Schema change impact analysis |

### 8.2 Auth Policies
```csharp
"AdminOnly" → RequireRole("Admin")
"ApproverOrAdmin" → RequireRole("Approver", "Admin")
Cookie: "FC.Admin.Auth", 4hr expiry, sliding window
```

### 8.3 Shared Components
- `AccordionPanel` — Collapsible content
- `AppAlert` — Alert notifications
- `AppModal` — Modal dialogs
- `ConfirmDialog` — Confirmation overlays
- `RingChart` — Circular progress/chart
- `ToastContainer` — Toast notifications

### 8.4 Services
- `DialogService` — Modal state management
- `ToastService` — Toast notification display

---

## 9. Institution Portal (`FC.Engine.Portal`)

### 9.1 Pages

| Page | Route | Purpose |
|------|-------|---------|
| `Home` | `/` | Dashboard (compliance score, deadlines, recent submissions) |
| `Login` | `/login` | Institution user login |
| `SubmitReturn` | `/submit` | 5-step submission wizard |
| `BulkSubmit` | `/submit/bulk` | Batch XML upload |
| `SubmissionList` | `/submissions` | My submissions list |
| `SubmissionDetail` | `/submissions/{id}` | Submission detail with validation |
| `DataEntryForm` | `/submit/form` | Dynamic form-based data entry |
| `PendingApprovals` | `/approvals` | Approval queue (Checker/Admin only) |
| `CrossSheetDashboard` | `/validation/cross-sheet` | Cross-sheet rule status |
| `QuickValidate` | `/validate` | Dry-run validation (3 modes) |
| `ValidationReport` | `/reports/validation` | Detailed validation results |
| `ComplianceReport` | `/reports/compliance` | Compliance metrics + trend |
| `ComplianceCertificate` | `/reports/certificate` | Printable compliance cert |
| `AuditTrailReport` | `/reports/audit` | Audit trail (Admin/Checker) |
| `TemplateBrowser` | `/templates` | Template catalog |
| `TemplateDetail` | `/templates/{id}` | Template field specifications |
| `DownloadSchemas` | `/schemas` | XSD downloads + sample XML |
| `ReportingCalendar` | `/calendar` | Calendar view of deadlines |
| `InstitutionProfile` | `/institution` | Institution details |
| `TeamMembers` | `/institution/team` | User management (Admin only) |
| `InstitutionSettings` | `/institution/settings` | Settings (Admin only) |
| `UserSettings` | `/settings` | User profile + security |
| `Notifications` | `/notifications` | Full notification history |
| `HelpHome` | `/help` | Help center hub |
| `GettingStarted` | `/help/getting-started` | Onboarding guide |
| `SubmissionGuide` | `/help/submission-guide` | How-to guide |
| `ValidationErrors` | `/help/validation-errors` | Error reference |
| `Faq` | `/help/faq` | FAQ |
| `ContactSupport` | `/help/support` | Contact form |

### 9.2 Auth Policies
```csharp
"InstitutionUser" → RequireAuthenticatedUser()
"InstitutionAdmin" → RequireClaim(Role, "Admin")
"InstitutionMaker" → RequireClaim(Role, "Maker", "Admin")
"InstitutionChecker" → RequireClaim(Role, "Checker", "Admin")
Cookie: "FC.Portal.Auth", 4hr expiry, HttpOnly, Strict SameSite
```

### 9.3 User Claims
```
NameIdentifier, Name, Email, DisplayName, Role, InstitutionId, InstitutionName
```

### 9.4 Portal Services (17 total)

| Service | File | Responsibility |
|---------|------|----------------|
| `DashboardService` | `Services/DashboardService.cs` | Dashboard metrics with 5-min cache |
| `SubmissionService` | `Services/SubmissionService.cs` | Submission wizard, maker-checker routing |
| `ApprovalService` | `Services/ApprovalService.cs` | Approval workflow (prevents self-approval) |
| `NotificationService` | `Services/NotificationService.cs` | 6 notification types, broadcast support |
| `DryRunValidationService` | `Services/DryRunValidationService.cs` | 3 validation modes (Full, Schema, Field) |
| `CrossSheetDashboardService` | `Services/CrossSheetDashboardService.cs` | Cross-sheet rule tracking |
| `ExportService` | `Services/ExportService.cs` | Export to CSV/PDF |
| `FormDataToXmlService` | `Services/FormDataToXmlService.cs` | Form → XML conversion |
| `TemplateBrowserService` | `Services/TemplateBrowserService.cs` | Template discovery + search |
| `UserSettingsService` | `Services/UserSettingsService.cs` | User preferences |
| `InstitutionManagementService` | `Services/InstitutionManagementService.cs` | Institution user/settings CRUD |
| `CalendarService` | `Services/CalendarService.cs` | Deadline calendar |
| `DialogService` | `Services/DialogService.cs` | Modal state management |
| `ToastService` | `Services/ToastService.cs` | Toast display |

### 9.5 Shared Components
- `AccordionPanel` — Collapsible content
- `ConfirmDialog` — Confirmation dialogs
- `HelpLayout` — Help page wrapper
- `NotificationBell` — Top-bar notification dropdown
- `ToastContainer` — Toast notifications

---

## 10. Database Architecture

### 10.1 Two Data Domains

**Metadata/Operational Tables** (EF Core — `MetadataDbContext`):
```
ReturnTemplates          → template definitions
TemplateVersions         → version lifecycle
TemplateFields           → field definitions per version
TemplateSections         → field groupings
TemplateItemCodes        → allowed codes for ItemCoded
IntraSheetFormulas       → intra-sheet validation rules
CrossSheetRules          → cross-template rules
CrossSheetRuleOperands   → rule operand definitions
CrossSheetRuleExpressions → rule expressions with tolerance
BusinessRules            → business validation rules
Submissions              → submission tracking
ValidationReports        → validation results
ValidationErrors         → individual errors/warnings
Institutions             → financial institution records
InstitutionUsers         → FI Portal users
PortalUsers              → Admin portal users
SubmissionApprovals      → maker-checker records
PortalNotifications      → in-app notifications
ReturnPeriods            → reporting periods
LoginAttempts            → security audit
PasswordResetTokens      → password reset tokens
AuditLogs                → entity change audit trail
```

**Dynamic Data Tables** (Dapper — `GenericDataRepository`):
```
103 tables derived from schema.sql, e.g.:
  mfcr_300    — Monthly Statement of Financial Position
  mfcr_1000   — Statement of Comprehensive Income
  mfcr_100    — Memorandum Items
  mfcr_302    — Schedule of Balances with Banks
  mfcr_304    — Schedule of Secured Money at Call
  ... (103 total, each mapping to a CBN return template)
```

### 10.2 Structural Categories

| Category | Description | Example |
|----------|-------------|---------|
| **FixedRow** | Single row per submission, fixed columns | mfcr_300 (Balance Sheet) |
| **MultiRow** | Multiple rows per submission, variable length | mfcr_302 (Bank Balances schedule) |
| **ItemCoded** | Rows keyed by item code | Sector-based breakdowns |

### 10.3 Key Relationships
```
Institution (1) ──→ (N) InstitutionUser
Institution (1) ──→ (N) Submission
Submission (1) ──→ (1) ValidationReport ──→ (N) ValidationError
Submission (1) ──→ (N) SubmissionApproval
ReturnTemplate (1) ──→ (N) TemplateVersion ──→ (N) TemplateField
TemplateVersion (1) ──→ (N) IntraSheetFormula
ReturnTemplate (1) ──→ (N) CrossSheetRule
```

### 10.4 Reference Tables
```
institutions       — FI registry (code, name, type, address, email, phone)
return_periods     — Reporting dates (type, year, month, quarter)
return_submissions — Submission tracking (institution_id, period_id, return_code, status)
bank_codes         — Bank code reference
sectors            — Sector codes
sub_sectors        — Sub-sector codes
states             — Nigerian states
local_governments  — LGA reference
```

---

## 11. Authentication & Authorization

### 11.1 Admin Portal Auth
- **Mechanism:** Cookie-based (`FC.Admin.Auth`)
- **Password:** PBKDF2-SHA256, 100,000 iterations, random salt
- **Lockout:** 5 failed attempts → 15-minute lockout
- **Session:** 4-hour expiry with sliding window
- **Roles:** Admin, Operator, Viewer

### 11.2 Institution Portal Auth
- **Mechanism:** Cookie-based (`FC.Portal.Auth`)
- **Password:** Same PBKDF2-SHA256 scheme
- **Lockout:** Same 5 attempts → 15-minute policy
- **Session:** 4-hour expiry, HttpOnly, Strict SameSite
- **Roles:** Admin, Maker, Checker, Viewer
- **Features:** Must-change-password on first login, login attempt logging

### 11.3 API Auth
- **Mechanism:** `X-Api-Key` header via `ApiKeyMiddleware`
- **Exempt Routes:** `/health`, `/swagger`

### 11.4 Maker-Checker Workflow
```
Maker submits return
  → If institution.MakerCheckerEnabled:
      → Status: PendingApproval
      → Notification → Checkers/Admins
      → Checker reviews:
          → Approve → Status: Accepted/AcceptedWithWarnings
          → Reject → Status: ApprovalRejected → Maker notified
  → If NOT enabled:
      → Status: Accepted/Rejected (direct)
```

Self-approval prevention: Checkers cannot approve their own submissions.

---

## 12. Caching Strategy

| Cache | Type | Scope | TTL | Invalidation |
|-------|------|-------|-----|-------------|
| TemplateMetadataCache | ConcurrentDictionary | Singleton | Indefinite | On template publish |
| Dashboard data | IMemoryCache | Per-institution | 5 minutes | Time-based |
| CrossSheet data | IMemoryCache | Per-institution | 5 minutes | Time-based |
| XSD schemas | Internal dictionary | Singleton | Indefinite | On template publish |

Warmup: `CacheWarmupService` (IHostedService) preloads all published templates on application start.

---

## 13. UI Architecture

### 13.1 Design System
- **No external UI framework** — 100% custom CSS design system
- **CSS Variables** for all design tokens (colors, spacing, typography, shadows)
- **Fonts:** Inter (body), Plus Jakarta Sans (headings), SF Mono (code)
- **Color Theme:** Institutional Green (#006B3F) + Gold (#C8A415) accent
- **Grid:** 8px base spacing system
- **Responsive:** Breakpoints at 1200px, 992px, 768px, 640px, 480px

### 13.2 CSS Files
- Admin: `wwwroot/css/app.css` (8,491 lines)
- Portal: `wwwroot/css/portal.css` (9,635 lines)
- Mirror architecture with identical token system

### 13.3 JavaScript Interop (`portal.js`)
- Clipboard, file download, print, debounce
- Form field extraction and validation styling
- Formula total display updates
- Focus trapping and screen reader announcements

### 13.4 Accessibility
- ARIA labels and roles throughout
- Keyboard navigation (ESC for modals, Tab order)
- Skip-to-content links
- Reduced motion support
- Semantic HTML

---

## 14. Key Architectural Patterns

| Pattern | Where Used |
|---------|-----------|
| **Clean Architecture** | Domain → Application → Infrastructure → Presentation |
| **Repository Pattern** | All data access through repository interfaces |
| **Orchestrator Pattern** | IngestionOrchestrator, ValidationOrchestrator |
| **Maker-Checker** | Submission approval workflow |
| **Strategy Pattern** | FormulaEvaluator (per formula type), DdlEngine (CREATE vs ALTER) |
| **Factory Pattern** | NotificationService (typed notifications) |
| **Cache-Aside** | TemplateMetadataCache (lazy load from DB) |
| **Dynamic Schema** | DDL Engine generates SQL from metadata |

---

## 15. Data Flow Diagrams

### 15.1 Submission Flow
```
Institution User uploads XML
  → Portal SubmitReturn.razor (5-step wizard)
    → SubmissionService.ProcessSubmission()
      → IngestionOrchestrator.Process()
        → Create Submission record
        → XsdGenerator.Generate() → Validate XML structure
        → GenericXmlParser.Parse() → ReturnDataRecord
        → ValidationOrchestrator.Validate()
          → Phase 1: Type/Range checks
          → Phase 2: FormulaEvaluator (intra-sheet)
          → Phase 3: CrossSheetValidator (cross-template)
          → Phase 4: BusinessRuleEvaluator
        → GenericDataRepository.Save() (if valid)
        → SubmissionRepository.Update() (status + report)
      → Check MakerChecker → route to approval or finalize
      → NotificationService.NotifySubmissionResult()
```

### 15.2 Template Lifecycle
```
Admin creates template
  → TemplateService.CreateTemplate()
    → Auto-generate table name, XML namespace
  → Add fields to version
    → TemplateService.AddFieldToVersion()
  → Submit for review
    → TemplateVersioningService.SubmitForReview()
  → Preview DDL
    → TemplateVersioningService.PreviewDdl()
    → DdlEngine.GenerateCreateTable() or GenerateAlterTable()
  → Publish
    → TemplateVersioningService.Publish()
    → DdlMigrationExecutor.Execute() (run DDL)
    → TemplateMetadataCache.Invalidate()
    → AuditLogger.Log()
```

---

## 16. Current Multi-Tenancy Status

### What EXISTS (partial tenancy):
- `Institution` entity with `institution_id` foreign keys on submissions
- `InstitutionUser` tied to specific institution
- `InstitutionId` claim in auth cookies
- Data queries filtered by `institutionId` parameter
- Maker-checker enabled per institution (settings JSON)

### What is MISSING for full multi-tenancy:
- **No tenant isolation at database level** — single DB, single schema
- **No tenant context middleware** — no automatic tenant resolution
- **No tenant-scoped connection strings** — all tenants share one connection
- **No tenant-specific configuration** — branding, features, limits
- **No tenant admin/management portal** — no super-admin for tenant provisioning
- **No data isolation enforcement** — relies on query parameters, not enforced at DB/ORM level
- **No tenant-aware caching** — cache keys not scoped by tenant
- **No rate limiting per tenant** — no resource quotas
- **No tenant onboarding/offboarding automation**
- **Dynamic data tables (103 tables) are shared** — no tenant-scoped tables
- **No tenant billing/subscription model**
- **No tenant-specific audit isolation**

---

## 17. Deployment Architecture

### Docker Compose Setup
```
docker/
├── docker-compose.yml
├── docker-compose.override.yml
├── Dockerfile.api
├── Dockerfile.admin
├── Dockerfile.portal
└── .env.example
```

**Services:**
- `fc-api` — REST API
- `fc-admin` — Admin Blazor portal
- `fc-portal` — Institution Blazor portal
- `fc-migrator` — Database migration (run once)
- `sql-server` — SQL Server 2022

---

## 18. Testing

```
tests/
├── FC.Engine.Domain.Tests/          # Entity, value object, metadata tests
├── FC.Engine.Infrastructure.Tests/  # Service, DDL, parser, formula tests
└── FC.Engine.Integration.Tests/     # End-to-end tests (empty scaffold)
```

**Test Coverage Areas:**
- ReturnDataRecord, Domain entities, TemplateVersion lifecycle
- DdlEngine (CREATE/ALTER TABLE generation)
- DynamicSqlBuilder (INSERT/SELECT queries)
- ExpressionParser, ExpressionTokenizer
- FormulaEvaluator
- AuthService
- FormulaCatalogSeeder, FormulaService
- TemplateService, TemplateVersioningService
- IngestionOrchestrator + ValidationOrchestrator

---

## 19. File Reference (Complete Paths)

### Domain Layer
```
src/FC.Engine.Domain/
├── Abstractions/
│   ├── IAuditLogger.cs
│   ├── IBusinessRuleEvaluator.cs
│   ├── ICrossSheetValidator.cs
│   ├── IDdlEngine.cs
│   ├── IDdlMigrationExecutor.cs
│   ├── IFormulaEvaluator.cs
│   ├── IFormulaRepository.cs
│   ├── IGenericDataRepository.cs
│   ├── IGenericXmlParser.cs
│   ├── IInstitutionRepository.cs
│   ├── IInstitutionUserRepository.cs
│   ├── IPortalNotificationRepository.cs
│   ├── IPortalUserRepository.cs
│   ├── ISqlTypeMapper.cs
│   ├── ISubmissionApprovalRepository.cs
│   ├── ISubmissionRepository.cs
│   ├── ITemplateMetadataCache.cs
│   ├── ITemplateRepository.cs
│   └── IXsdGenerator.cs
├── DataRecord/
│   └── ReturnDataRecord.cs
├── Entities/
│   ├── Institution.cs
│   ├── InstitutionUser.cs
│   ├── LoginAttempt.cs
│   ├── PasswordResetToken.cs
│   ├── PortalNotification.cs
│   ├── PortalUser.cs
│   ├── ReturnPeriod.cs
│   ├── Submission.cs
│   ├── SubmissionApproval.cs
│   ├── ValidationError.cs
│   └── ValidationReport.cs
├── Enums/
│   ├── ApprovalStatus.cs
│   ├── FieldDataType.cs
│   ├── FormulaType.cs
│   ├── InstitutionRole.cs
│   ├── PortalRole.cs
│   ├── ReturnFrequency.cs
│   ├── StructuralCategory.cs
│   ├── SubmissionStatus.cs
│   ├── TemplateStatus.cs
│   ├── ValidationCategory.cs
│   └── ValidationSeverity.cs
├── Metadata/
│   ├── IntraSheetFormula.cs
│   ├── ReturnTemplate.cs
│   ├── TemplateField.cs
│   ├── TemplateItemCode.cs
│   ├── TemplateSection.cs
│   └── TemplateVersion.cs
├── Validation/
│   ├── BusinessRule.cs
│   └── CrossSheetRule.cs
└── ValueObjects/
    ├── ReportingPeriod.cs
    └── ReturnCode.cs
```

### Application Layer
```
src/FC.Engine.Application/
├── DTOs/
│   ├── SubmissionDto.cs
│   └── TemplateDto.cs
└── Services/
    ├── AuthService.cs
    ├── BusinessRuleSeedService.cs
    ├── CrossSheetRuleSeedService.cs
    ├── FormulaCatalogSeeder.cs
    ├── FormulaSeedService.cs
    ├── FormulaService.cs
    ├── IngestionOrchestrator.cs
    ├── InstitutionAuthService.cs
    ├── SeedService.cs
    ├── TemplateService.cs
    ├── TemplateVersioningService.cs
    └── ValidationOrchestrator.cs
```

### Infrastructure Layer
```
src/FC.Engine.Infrastructure/
├── Audit/
│   └── AuditLogger.cs
├── Caching/
│   ├── CacheWarmupService.cs
│   └── TemplateMetadataCache.cs
├── DependencyInjection.cs
├── DynamicSchema/
│   ├── DdlEngine.cs
│   ├── DdlMigrationExecutor.cs
│   └── SqlTypeMapper.cs
├── Metadata/
│   ├── MetadataDbContext.cs
│   ├── Configurations/ (8 files)
│   ├── Migrations/ (7 files)
│   └── Repositories/ (9 files)
├── Persistence/
│   ├── DynamicSqlBuilder.cs
│   ├── GenericDataRepository.cs
│   └── Repositories/
│       └── SubmissionRepository.cs
├── Validation/
│   ├── BusinessRuleEvaluator.cs
│   ├── CrossSheetValidator.cs
│   ├── ExpressionParser.cs
│   ├── ExpressionTokenizer.cs
│   └── FormulaEvaluator.cs
└── Xml/
    ├── GenericXmlParser.cs
    └── XsdGenerator.cs
```

### API Layer
```
src/FC.Engine.Api/
├── Program.cs
├── Endpoints/
│   ├── SchemaEndpoints.cs
│   ├── SubmissionEndpoints.cs
│   └── TemplateEndpoints.cs
└── Middleware/
    └── ApiKeyMiddleware.cs
```

### Admin Portal
```
src/FC.Engine.Admin/
├── Program.cs
├── appsettings.json
├── Services/ (DialogService.cs, ToastService.cs)
├── Components/
│   ├── App.razor, Routes.razor, _Imports.razor
│   ├── Layout/ (LoginLayout, MainLayout, NavMenu)
│   ├── Shared/ (AccordionPanel, AppAlert, AppModal, ConfirmDialog, RingChart, ToastContainer)
│   └── Pages/ (16 pages across 7 areas)
└── wwwroot/ (css/app.css, favicon.svg, images/cbn-logo.svg)
```

### Portal
```
src/FC.Engine.Portal/
├── Program.cs
├── appsettings.json
├── Services/ (17 service files)
├── Components/
│   ├── App.razor, Routes.razor, _Imports.razor
│   ├── Layout/ (LoginLayout, NavMenu, PortalLayout)
│   ├── Shared/ (AccordionPanel, ConfirmDialog, HelpLayout, NotificationBell, ToastContainer)
│   └── Pages/ (30 pages across 10 areas)
└── wwwroot/ (css/portal.css, js/portal.js, favicon.svg, images/cbn-logo.svg)
```

---

## 20. Configuration Reference

### Connection String
```
Server=localhost,1433;Database=FcEngine;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=true
```

### Logging (Serilog)
- Console + File sinks
- Rolling daily files, 30-day retention
- `logs/portal-.log`, `logs/admin-.log`

### Key Config Values
- Cookie expiry: 4 hours (both portals)
- Password lockout: 5 attempts → 15 minutes
- Password hash: PBKDF2-SHA256, 100,000 iterations
- Reset token: 1-hour validity
- Dashboard cache: 5-minute TTL
- Template cache: Indefinite (invalidated on publish)

---

## 21. Schema.sql Summary

The `schema.sql` file contains DDL for **103 return template tables** derived from the CBN Excel workbook "DFIS - FC Return Templates". These represent the complete regulatory return structure for Finance Companies:

**Table naming convention:** `mfcr_XXX` (Monthly FC Return) or `qfcr_XXX` (Quarterly)

**Key return types include:**
- MFCR 300 — Statement of Financial Position (Balance Sheet)
- MFCR 1000 — Statement of Comprehensive Income
- MFCR 100 — Memorandum Items
- MFCR 302-314 — Schedules of balances, placements, money at call
- MFCR 316-340 — Securities, derivatives, treasury bills, bonds
- MFCR 342-380 — Loans, advances, credit classifications
- MFCR 400-500 — Other assets, liabilities, equity details
- QFCR series — Quarterly returns (risk, capital adequacy, etc.)

Each table has:
- `id` (SERIAL PRIMARY KEY)
- `submission_id` (FK to return_submissions)
- Domain-specific columns with item codes as comments
- `created_at` timestamp

---

*This document provides the complete architectural blueprint for AI agents to understand and expand RegOS™ into a full multi-tenant system.*
