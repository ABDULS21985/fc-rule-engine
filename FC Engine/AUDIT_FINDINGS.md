# RegOS™ — Endpoint Audit Findings

**Date:** 2026-03-08
**Branch:** `RegOS`
**Scope:** All HTTP endpoints across API (5100), Admin (5200), Portal (5300), Regulator (8080)

---

## 1. Endpoint Inventory

### API Server — 49 endpoints across 9 groups

| Group | Count | Routes |
|---|---|---|
| Auth | 3 | `/api/v1/auth/{login,refresh,revoke}` |
| Submissions | 3 | `/api/v1/submissions/{returnCode}` POST, `/{id}` GET, `/institution/{instId}` GET |
| Templates | 9 | `/api/v1/templates/` CRUD, versions lifecycle, formulas |
| Schemas | 4 | `/api/v1/schemas/{xsd,seed,seed-formulas,published}` |
| Data Feed | 3 | `/api/v1/returns/{returnCode}/{data,mappings}` |
| Filing Calendar | 2 | `/api/v1/filing-calendar/{rag,deadline-override}` |
| Privacy | 6 | `/api/v1/privacy/{dsar,breaches}` |
| Historical Migration | 12 | `/api/v1/migration/{jobs,tracker}` |
| Webhooks | 7 | `/api/v1/webhooks/` CRUD + rotate + test + deliveries |
| Health | 4 | `/health`, `/health/live`, `/health/ready`, `/metrics` |
| **v2 duplicates** | 2 | `/api/v2/schemas/`, `/api/v2/submissions/` |

### Blazor Portals — Form + session endpoints

| Portal | Endpoints |
|---|---|
| Admin (5200) | `/account/login`, `/account/logout`, `/api/session/ping`, `/platform/tenants/export`, impersonation |
| Portal (5300) | `/account/login`, `/account/logout`, `/exports/{id}/download`, SignalR hubs |
| Regulator (8080) | `/account/login`, `/account/logout`, workspace report |

### SignalR Hubs — 2

| Hub | Route |
|---|---|
| NotificationHub | `/hubs/notifications` |
| ReturnLockHub | `/hubs/returnlock` |

---

## 2. Critical Defects Found & Fixed

### CRIT-1: Undefined Authorization Policies (Runtime Crash)

**Issue:** `PrivacyEndpoints.cs` referenced `"CanViewSubmissions"` and `"CanApproveSubmissions"` (plural) but `AuthorizationPolicyExtensions.cs` only defined singular `"CanApproveSubmission"`. This causes `InvalidOperationException` at runtime — **all privacy endpoints were broken.**

**Root cause:** Typo / naming inconsistency between endpoint files and policy registration.

**Fix:** Added `CanViewSubmissions` (→ `SubmissionRead`) and `CanApproveSubmissions` (→ `SubmissionApprove`) policies in `AuthorizationPolicyExtensions.cs`.

**Files changed:**
- `src/FC.Engine.Infrastructure/Auth/AuthorizationPolicyExtensions.cs`

---

### CRIT-2: Zero Authorization on Template Endpoints (DDL Injection Risk)

**Issue:** All 9 template endpoints had **no** `.RequireAuthorization()`. Any authenticated user could create templates, add fields, and **publish DDL changes that modify the database schema**.

**Root cause:** Authorization was never added to `TemplateEndpoints.cs`.

**Fix:** Added `CanReadTemplates` to read endpoints, `CanEditTemplates` to mutation endpoints, `CanPublishTemplates` to publish/preview-ddl. Defined corresponding policies in `AuthorizationPolicyExtensions.cs`.

**Files changed:**
- `src/FC.Engine.Api/Endpoints/TemplateEndpoints.cs`
- `src/FC.Engine.Infrastructure/Auth/AuthorizationPolicyExtensions.cs`

---

### CRIT-3: Zero Authorization on Schema Seed Endpoints

**Issue:** `POST /schemas/seed` and `POST /schemas/seed-formulas` could be called by anyone. These re-seed the entire template/formula database.

**Root cause:** No auth on `SchemaEndpoints.cs`.

**Fix:** Added `PlatformAdmin` policy to seed endpoints, `CanReadTemplates` to read endpoints.

**Files changed:**
- `src/FC.Engine.Api/Endpoints/SchemaEndpoints.cs`
- `src/FC.Engine.Infrastructure/Auth/AuthorizationPolicyExtensions.cs`

---

### CRIT-4: IDOR on Submission Read Endpoints

**Issue:** `GET /submissions/{id}` and `GET /submissions/institution/{institutionId}` had no authorization. Any authenticated user could read any submission by guessing IDs.

**Root cause:** `.RequireAuthorization()` never called on GET routes in `SubmissionEndpoints.cs`.

**Fix:** Added `CanViewSubmissions` policy to both GET endpoints.

**Files changed:**
- `src/FC.Engine.Api/Endpoints/SubmissionEndpoints.cs`

---

### CRIT-5: NullReferenceException in `/schemas/published`

**Issue:** `SchemaEndpoints.cs:76-77` accessed `t.CurrentVersion.Fields.Count` without null check. Templates without a published version crash the endpoint.

**Root cause:** Cache may contain templates where `CurrentVersion` is null.

**Fix:** Changed to `t.CurrentVersion?.Fields.Count ?? 0`.

**Files changed:**
- `src/FC.Engine.Api/Endpoints/SchemaEndpoints.cs`

---

### CRIT-6: Webhook IDOR — No Tenant Scoping on Mutations

**Issue:** PUT, DELETE, rotate-secret, test, and deliveries endpoints accepted an `id` with no tenant check. Users from Tenant A could modify Tenant B's webhooks.

**Root cause:** Only GET/POST endpoints checked `ITenantContext`. Mutation endpoints skipped it.

**Fix:** Added `ITenantContext` injection and tenant check to all mutation endpoints.

**Files changed:**
- `src/FC.Engine.Api/Endpoints/WebhookEndpoints.cs`

---

### CRIT-7: Client-Supplied CreatedBy in Webhook Creation

**Issue:** `CreateWebhookRequest.CreatedBy` was sent by the client and used directly. Any caller could impersonate another user.

**Root cause:** Request DTO included `CreatedBy` field.

**Fix:** Removed `CreatedBy` from the DTO. Now resolved server-side from `ClaimsPrincipal`.

**Files changed:**
- `src/FC.Engine.Api/Endpoints/WebhookEndpoints.cs`

---

### CRIT-8: Breach Report TenantId Override

**Issue:** `POST /privacy/breaches` used `report.TenantId ??= tenantContext.CurrentTenantId`. If a client sent a TenantId, it was trusted — allowing cross-tenant breach reports.

**Root cause:** Null-coalescing instead of unconditional assignment.

**Fix:** Changed to `report.TenantId = tenantContext.CurrentTenantId` (always server-side).

**Files changed:**
- `src/FC.Engine.Api/Endpoints/PrivacyEndpoints.cs`

---

## 3. High-Severity Defects Found & Fixed

### HIGH-1: Hardcoded "system" Audit User in Templates

**Issue:** All template mutations logged `"system"` as the acting user. The actual authenticated user was never captured.

**Fix:** Added `ClaimsPrincipal` injection and `ResolveUserIdentity()` helper. All mutations now use the real user identity.

**Files changed:**
- `src/FC.Engine.Api/Endpoints/TemplateEndpoints.cs`

---

### HIGH-2: MFA Challenge Returns HTTP 200

**Issue:** When MFA was required, `/auth/login` returned 200 with `{requiresMfa: true}`. Clients could not distinguish "MFA needed" from "login succeeded" at the HTTP status level.

**Fix:** Changed to return `401 Unauthorized` with the MFA challenge payload.

**Files changed:**
- `src/FC.Engine.Api/Endpoints/AuthEndpoints.cs`

---

### HIGH-3: No Input Validation on Login

**Issue:** Empty email/password passed through to the database query.

**Fix:** Added `string.IsNullOrWhiteSpace` check returning 400.

**Files changed:**
- `src/FC.Engine.Api/Endpoints/AuthEndpoints.cs`

---

### HIGH-4: No RefreshToken Validation

**Issue:** Empty/null `RefreshToken` in `/auth/refresh` and `/auth/revoke` passed to JWT service.

**Fix:** Added validation returning 400 for empty tokens.

**Files changed:**
- `src/FC.Engine.Api/Endpoints/AuthEndpoints.cs`

---

### HIGH-5: Bare `catch` Swallows All Exceptions

**Issue:** `SubmissionEndpoints.cs:35-38` used `catch {}` for SLA tracking, swallowing `OutOfMemoryException` etc.

**Fix:** Changed to `catch (Exception ex)` with `ILoggerFactory` injection and `LogWarning`.

**Files changed:**
- `src/FC.Engine.Api/Endpoints/SubmissionEndpoints.cs`

---

### HIGH-6: Filing Calendar ResolveUserId Returns 0

**Issue:** If user ID claim couldn't be parsed, `ResolveUserId` returned `0`. Deadline overrides were recorded under user 0.

**Fix:** Changed return type to `int?`, handler returns `Forbid()` when null.

**Files changed:**
- `src/FC.Engine.Api/Endpoints/FilingCalendarEndpoints.cs`

---

### HIGH-7: Inconsistent Tenant Context Error Codes

**Issue:** Four different patterns for missing tenant: 401, 403, 400, or not checked at all.

**Fix:** Standardized all to `Results.Forbid()` (403). FilingCalendarEndpoints changed from 401.

**Files changed:**
- `src/FC.Engine.Api/Endpoints/FilingCalendarEndpoints.cs`

---

### HIGH-8: Past Deadline Not Validated

**Issue:** `POST /filing-calendar/deadline-override` accepted any date, including past dates and `DateTime.MinValue`.

**Fix:** Added `request.NewDeadline.Date < DateTime.UtcNow.Date` check.

**Files changed:**
- `src/FC.Engine.Api/Endpoints/FilingCalendarEndpoints.cs`

---

### HIGH-9: Missing Parameter Validation on Submit

**Issue:** `institutionId` and `returnPeriodId` query params defaulted to 0 without validation.

**Fix:** Added `<= 0` checks returning 400.

**Files changed:**
- `src/FC.Engine.Api/Endpoints/SubmissionEndpoints.cs`

---

## 4. Medium-Severity Defects Found & Fixed

### MED-1: Unbounded `take` on Webhook Deliveries

**Issue:** No upper bound on `take` param — caller could request millions of records.

**Fix:** Capped at 500; default 50.

**Files changed:**
- `src/FC.Engine.Api/Endpoints/WebhookEndpoints.cs`

---

### MED-2: Unbounded `take` on Migration Staged Records

**Issue:** Same as MED-1 for historical migration staged review.

**Fix:** Capped at 1000; default 200.

**Files changed:**
- `src/FC.Engine.Api/Endpoints/HistoricalMigrationEndpoints.cs`

---

### MED-3: Wrong Auth Policy for GET Mappings

**Issue:** `GET /returns/{returnCode}/mappings/{integrationName}` required `CanCreateSubmission` (write perm) for a read operation.

**Fix:** Changed to `CanViewSubmissions`.

**Files changed:**
- `src/FC.Engine.Api/Endpoints/DataFeedEndpoints.cs`

---

### MED-4: DSAR/Breach Creation Returns 200 Instead of 201

**Issue:** Resource creation endpoints returned `Results.Ok()` instead of `Results.Created()`.

**Fix:** Changed to `Results.Created(...)`.

**Files changed:**
- `src/FC.Engine.Api/Endpoints/PrivacyEndpoints.cs`

---

## 5. Smoke Test Coverage Created

**New file:** `scripts/smoke-test.sh`

Tests 30+ endpoint behaviors across all three services:
- Health & infrastructure (4 tests)
- Auth anonymous flows (4 tests)
- All protected endpoints return 401 without token (18 tests)
- v2 endpoint routing (2 tests)
- Admin and Portal reachability (4 tests)

---

## 6. Files Changed Summary

| File | Changes |
|---|---|
| `src/FC.Engine.Infrastructure/Auth/AuthorizationPolicyExtensions.cs` | +7 policies: CanViewSubmissions, CanApproveSubmissions, CanEditTemplates, CanPublishTemplates, CanReadTemplates, PlatformAdmin |
| `src/FC.Engine.Api/Endpoints/AuthEndpoints.cs` | Input validation, MFA 401 status, refresh/revoke validation |
| `src/FC.Engine.Api/Endpoints/SubmissionEndpoints.cs` | Auth on GET routes, param validation, logged SLA exception |
| `src/FC.Engine.Api/Endpoints/TemplateEndpoints.cs` | Auth on all 9 endpoints, real user identity, ClaimsPrincipal |
| `src/FC.Engine.Api/Endpoints/SchemaEndpoints.cs` | Auth on all 4 endpoints, NullRef fix on CurrentVersion |
| `src/FC.Engine.Api/Endpoints/FilingCalendarEndpoints.cs` | 403 consistency, nullable userId, past deadline check |
| `src/FC.Engine.Api/Endpoints/PrivacyEndpoints.cs` | Tenant guard on access-package/erasure, breach TenantId, 201 status |
| `src/FC.Engine.Api/Endpoints/WebhookEndpoints.cs` | Tenant guard on all mutations, server-side CreatedBy, take cap |
| `src/FC.Engine.Api/Endpoints/DataFeedEndpoints.cs` | Read perm for GET mappings |
| `src/FC.Engine.Api/Endpoints/HistoricalMigrationEndpoints.cs` | take upper bound |
| `scripts/smoke-test.sh` | **New** — comprehensive smoke test suite |
| `AUDIT_FINDINGS.md` | **New** — this document |

---

## 7. Build Verification

```
dotnet build FCEngine.sln
Build succeeded. 0 Error(s), 9 Warning(s) (all pre-existing)
```

All 8 projects + 3 test projects compile successfully.

---

## 8. Residual Risks & Follow-Up Items

### Not Fixed (Requires Architecture/Service-Layer Changes)

| # | Risk | Severity | Notes |
|---|---|---|---|
| R-1 | No pagination on GET /templates/, /submissions/institution/, /privacy/dsar, /migration/jobs, /webhooks/, /schemas/published | Medium | Requires adding `page`/`pageSize` params to repo interfaces |
| R-2 | Mapping upserts run N individual DB calls with no transaction | Medium | Needs `IDbConnectionFactory` transaction scope in DataFeedEndpoints |
| R-3 | Rate limiter runs before authorization — anon users consume tenant quota | Medium | Move `UseRateLimiter()` after `UseAuthorization()` in Program.cs |
| R-4 | RSA key not disposed in `LoadRsaPublicSecurityKey` | Low | Add `using` block |
| R-5 | `/error` handler returns generic Problem() with no detail in Dev mode | Low | Add environment-conditional detail |
| R-6 | File size check in migration upload occurs after full buffer read | Low | Use `[RequestSizeLimit]` attribute |
| R-7 | No CORS configuration on API | Low | Add `AddCors()` / `UseCors()` if browser clients needed |
| R-8 | Webhook URL not validated for SSRF | Medium | Validate URL format, block internal IPs |
| R-9 | All Dockerfiles use `dotnet:10.0-preview` | Info | Update when .NET 10 GA releases |
| R-10 | MessagePack 2.5.108 has known vulnerability (NU1902) | Medium | Upgrade package |
| R-11 | No `.dockerignore` — COPY sends entire repo to daemon | Low | Create `.dockerignore` |
| R-12 | Seed reference data only covers 2024-2025 periods | Low | Add 2026 periods |
| R-13 | Regulator portal not in docker-compose | Info | Add service entry if needed for orchestration |
