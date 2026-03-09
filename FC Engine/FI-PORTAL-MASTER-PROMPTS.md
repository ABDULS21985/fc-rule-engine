# RegOS™ — Institution Portal: 20 Master Implementation Prompts

> **Purpose**: These 20 prompts are designed for AI agents to implement the CBN DFIS FC Returns Financial Institution (FI) Portal end-to-end. Each prompt is self-contained, comprehensive, and sequenced for dependency order. Execute them in order (1→20) for a clean build.

> **Context**: The RegOS™ already has a working **Admin Portal** (Blazor Server for CBN staff), a **REST API** (for XML submission), and full **backend infrastructure** (Domain, Application, Infrastructure layers with 298 passing tests). The FI Portal is a **new Blazor Server project** where regulated financial institutions (Microfinance Banks, Commercial Banks, etc.) log in to submit their FC returns, track compliance, and manage their reporting obligations.

---

## Prompt 1: Project Scaffolding & Solution Integration

### Objective
Create the `FC.Engine.Portal` Blazor Server project for the Financial Institution Portal, integrate it into the existing solution, and establish the foundational project structure.

### Existing Architecture Context
The RegOS™ solution lives at `/Users/mac/codes/fcs/RegOS™/` and contains these projects:
- `FC.Engine.Domain` — Entities, enums, abstractions, value objects (shared, **DO NOT modify**)
- `FC.Engine.Application` — Services, orchestrators, DTOs (shared, **DO NOT modify**)
- `FC.Engine.Infrastructure` — Repositories, validators, caching, XML parsing (shared, **DO NOT modify**)
- `FC.Engine.Api` — REST API with submission endpoints
- `FC.Engine.Admin` — CBN Admin Portal (Blazor Server) — **reference for patterns, do NOT modify**
- `FC.Engine.Migrator` — Database seeding and migration

### Requirements

1. **Create the project**:
   ```
   RegOS™/src/FC.Engine.Portal/
   ├── FC.Engine.Portal.csproj        # Target .NET 10.0, reference Domain + Application + Infrastructure
   ├── Program.cs                      # Blazor Server setup with auth, DI, middleware
   ├── appsettings.json                # Connection string, Serilog config
   ├── appsettings.Development.json
   ├── Components/
   │   ├── App.razor                   # HTML shell with Inter + Plus Jakarta Sans fonts
   │   ├── Routes.razor                # Router with auth redirect
   │   ├── _Imports.razor              # Global usings
   │   ├── Layout/
   │   │   ├── PortalLayout.razor      # Main layout (sidebar + topbar + content area)
   │   │   ├── AuthLayout.razor        # Login/register layout (no sidebar)
   │   │   └── NavMenu.razor           # FI-specific navigation
   │   ├── Shared/                     # Reusable components (to be populated later)
   │   └── Pages/                      # Page components (to be populated later)
   ├── Services/                       # Portal-specific services
   └── wwwroot/
       ├── css/
       │   └── portal.css              # FI Portal design system (based on CBN tokens)
       ├── images/
       │   └── cbn-logo.svg            # Copy from Admin
       └── favicon.svg                 # Copy from Admin
   ```

2. **Add to solution file** (`FCEngine.sln`): Register the new project.

3. **Program.cs must include**:
   - `AddInfrastructure(config)` — reuses the shared infrastructure layer
   - Scoped services: `IngestionOrchestrator`, `ValidationOrchestrator`, `AuthService`, `TemplateService`
   - Cookie authentication with `/login` path, 4-hour expiry (shorter than Admin's 8 hours)
   - Authorization policies: `"InstitutionUser"` (any authenticated FI user), `"InstitutionAdmin"` (institution admin role), `"InstitutionMaker"` (can submit), `"InstitutionChecker"` (can approve submissions)
   - Blazor Server with interactive server components
   - Serilog logging
   - Login POST endpoint at `/account/login` using `AuthService` — but authenticating **FI users** (not admin portal users)
   - Logout endpoint at `/account/logout`

4. **App.razor**:
   - Same HTML structure as Admin's App.razor but with `portal.css` instead of `app.css`
   - Include `favicon.svg`, `theme-color` meta tag with `#006B3F`
   - Google Fonts: Inter + Plus Jakarta Sans (same font loading pattern as Admin)

5. **portal.css** — Create the initial design token system:
   - Copy the CBN design tokens (CSS custom properties) from the Admin's `app.css` (`:root` section with `--cbn-*`, `--space-*`, `--radius-*`, `--shadow-*`, `--transition-*`, `--ease-*`, `--font-*` variables)
   - Add FI Portal-specific tokens: `--portal-sidebar-width`, `--portal-topbar-height`, etc.
   - Include base resets, typography, and utility classes
   - This CSS file will grow as subsequent prompts add section-specific styles

6. **PortalLayout.razor** — Main application shell:
   - Left sidebar with CBN logo, institution name, navigation
   - Top bar with breadcrumb, notification bell, user avatar/menu
   - Content area rendering `@Body`
   - Mobile-responsive with sidebar toggle
   - Pattern should follow Admin's `MainLayout.razor` but be distinct visually

7. **NavMenu.razor** — Institution-focused navigation groups:
   ```
   SUBMISSIONS
   ├── Dashboard
   ├── Submit Return
   ├── My Submissions
   └── Reporting Calendar

   RESOURCES
   ├── Template Browser
   ├── Download Schemas
   └── Help Center

   ACCOUNT (Institution Admin only)
   ├── My Institution
   ├── Team Members
   └── Settings
   ```

8. **Docker**: Add the Portal as a new service in `docker/docker-compose.yml` (port 5002, separate from Admin's 5001 and API's 5000).

### Technical Constraints
- The FI Portal **shares the same database** as Admin and API
- The FI Portal **reuses** Domain, Application, and Infrastructure — no duplication
- The FI Portal has its **own authentication** separate from Admin (different user types, different roles)
- Use the same rendering mode: `@rendermode="RenderMode.InteractiveServer"`
- The CSS must use the same CBN brand tokens but can have portal-specific class prefixes (e.g., `portal-*` instead of `dash-*`)
- Target .NET 10.0, C# with nullable enabled

### Verification
- `dotnet build` succeeds for the entire solution with 0 errors
- The Portal project starts and renders the login redirect
- All 298 existing tests still pass

---

## Prompt 2: Institution Authentication & User Management Domain

### Objective
Extend the domain model to support multi-tenant institution authentication. Financial institutions need their own user accounts separate from Admin portal users, with institution-level tenancy, maker-checker workflows, and role-based access.

### Existing Domain Context
The current `PortalUser` entity (in `FC.Engine.Domain/Entities/PortalUser.cs`) supports Admin portal users with roles: Admin, Editor, Viewer. The `Institution` entity has: Id, InstitutionCode, InstitutionName, LicenseType, IsActive.

### Requirements

1. **New entity: `InstitutionUser`** (in `FC.Engine.Domain/Entities/`):
   ```csharp
   public class InstitutionUser
   {
       public int Id { get; set; }
       public int InstitutionId { get; set; }          // FK to Institution
       public string Username { get; set; }             // Unique across all institutions
       public string Email { get; set; }
       public string DisplayName { get; set; }
       public string PasswordHash { get; set; }
       public InstitutionRole Role { get; set; }        // Admin, Maker, Checker, Viewer
       public bool IsActive { get; set; }
       public bool MustChangePassword { get; set; }     // Force password change on first login
       public DateTime CreatedAt { get; set; }
       public DateTime? LastLoginAt { get; set; }
       public string? LastLoginIp { get; set; }
       public int FailedLoginAttempts { get; set; }
       public DateTime? LockedUntil { get; set; }       // Account lockout after N failed attempts
       public Institution? Institution { get; set; }
   }
   ```

2. **New enum: `InstitutionRole`** (in `FC.Engine.Domain/Enums/`):
   ```csharp
   public enum InstitutionRole
   {
       Admin,      // Can manage institution users, settings
       Maker,      // Can create/upload submissions
       Checker,    // Can review and approve/reject submissions before final submit
       Viewer      // Read-only access to submissions and reports
   }
   ```

3. **Extend `Institution` entity** with:
   - `ContactEmail`, `ContactPhone`, `Address`
   - `MaxUsersAllowed` (int, default 10)
   - `SubscriptionTier` (string: "Basic", "Standard", "Premium")
   - `LastSubmissionAt` (DateTime?)
   - Navigation: `IReadOnlyList<InstitutionUser> Users`

4. **New entity: `SubmissionApproval`** (maker-checker workflow):
   ```csharp
   public class SubmissionApproval
   {
       public int Id { get; set; }
       public int SubmissionId { get; set; }
       public int RequestedByUserId { get; set; }       // Maker
       public int? ReviewedByUserId { get; set; }       // Checker
       public ApprovalStatus Status { get; set; }       // Pending, Approved, Rejected
       public string? ReviewerComments { get; set; }
       public DateTime RequestedAt { get; set; }
       public DateTime? ReviewedAt { get; set; }
       public Submission? Submission { get; set; }
       public InstitutionUser? RequestedBy { get; set; }
       public InstitutionUser? ReviewedBy { get; set; }
   }
   ```

5. **New enum: `ApprovalStatus`**: Pending, Approved, Rejected

6. **Extend `Submission` entity** with:
   - `SubmittedByUserId` (int?) — which institution user submitted it
   - `ApprovalRequired` (bool) — institution setting
   - Navigation: `SubmissionApproval? Approval`

7. **New abstraction: `IInstitutionUserRepository`** (in `FC.Engine.Domain/Abstractions/`):
   - `GetById(int id)`
   - `GetByUsername(string username)`
   - `GetByInstitution(int institutionId)`
   - `GetByEmail(string email)`
   - `Create(InstitutionUser user)`
   - `Update(InstitutionUser user)`
   - `UsernameExists(string username)`
   - `EmailExists(string email)`
   - `GetCountByInstitution(int institutionId)`

8. **New abstraction: `ISubmissionApprovalRepository`**:
   - `GetBySubmission(int submissionId)`
   - `GetPendingByInstitution(int institutionId)`
   - `Create(SubmissionApproval approval)`
   - `Update(SubmissionApproval approval)`

9. **New service: `InstitutionAuthService`** (in `FC.Engine.Application/Services/`):
   - `ValidateLogin(username, password)` → InstitutionUser? (with lockout logic: 5 failed attempts → 15 min lockout)
   - `CreateUser(institutionId, username, email, displayName, password, role)`
   - `ChangePassword(userId, oldPassword, newPassword)`
   - `ResetPassword(userId, newPassword)` (admin-only)
   - `DeactivateUser(userId)`
   - Same PBKDF2 hashing as existing `AuthService`

10. **EF Core configuration** (in `FC.Engine.Infrastructure/Metadata/Configurations/`):
    - `InstitutionUserConfiguration` — table `institution_users`, unique index on Username, index on InstitutionId
    - `SubmissionApprovalConfiguration` — table `submission_approvals`
    - Update `MetadataDbContext` with new DbSets
    - Create EF migration

11. **Infrastructure implementation**: `InstitutionUserRepository`, `SubmissionApprovalRepository`

### Verification
- All new entities compile without error
- EF migration generates correct SQL
- `InstitutionAuthService` correctly validates credentials with lockout
- Existing 298 tests still pass + new unit tests for InstitutionAuthService

---

## Prompt 3: Login, Registration & Password Management Pages

### Objective
Build the complete authentication experience for the FI Portal — login page, first-time password change, forgot password flow, and session management. This must be a premium, CBN-branded experience that conveys institutional trust and security.

### Design Requirements

1. **Login Page** (`/login`):
   - CBN logo centered at top
   - "Financial Institution Reporting Portal" title
   - "Central Bank of Nigeria — RegOS™" subtitle
   - Username field with institution icon
   - Password field with show/hide toggle
   - "Remember me" checkbox
   - "Sign In" button in CBN green
   - "Forgot Password?" link
   - Error states: invalid credentials, account locked (show remaining lockout time), account deactivated
   - Footer: "© 2024 Central Bank of Nigeria. All rights reserved."
   - Background: subtle gradient or pattern that conveys trust/authority
   - The login page must work with the `AuthLayout.razor` (no sidebar)

2. **Force Password Change Page** (`/change-password`):
   - Shown when `MustChangePassword = true` after login
   - Current password, new password, confirm password fields
   - Password strength indicator (min 8 chars, uppercase, lowercase, number, special char)
   - Real-time validation feedback on each keystroke
   - After successful change, redirect to dashboard

3. **Forgot Password Page** (`/forgot-password`):
   - Email input field
   - "Send Reset Link" button
   - Success message: "If an account exists with that email, a reset link has been sent"
   - Note: In this first phase, this can be a placeholder that shows the success message (email sending is a future enhancement)

4. **Session Management**:
   - Auto-redirect to `/login` on session expiry
   - Show a "Session Expiring" warning 5 minutes before expiry
   - Activity-based session extension (sliding expiration)

5. **Login POST endpoint** in `Program.cs`:
   - Call `InstitutionAuthService.ValidateLogin()`
   - On success: Create claims (NameIdentifier, Name, Email, DisplayName, Role, **InstitutionId**, **InstitutionName**)
   - On failure: Redirect with appropriate error code
   - Update `LastLoginAt` and `LastLoginIp`

### CSS Requirements
- Use the portal design token system
- Responsive: beautiful on mobile, tablet, and desktop
- Animations: subtle fade-in for the login card, smooth transitions on focus
- Follow the same visual language as the Admin login but with a distinct FI identity
- ARIA labels, keyboard navigation, focus indicators throughout

### Verification
- Login flow works end-to-end with valid/invalid credentials
- Lockout activates after 5 failed attempts
- Force password change flow works
- All pages are responsive and accessible
- Build succeeds with 0 errors

---

## Prompt 4: Institution Dashboard — Compliance Command Center

### Objective
Build the FI Portal dashboard as a compliance command center that gives institution users an immediate, at-a-glance view of their reporting status, upcoming deadlines, recent submissions, and compliance health.

### Design Vision
This is the first page users see after login. It must instantly communicate: "Here's where you stand with your CBN reporting obligations." Think Bloomberg Terminal meets modern fintech dashboard — data-dense but beautifully organized.

### Sections

1. **Welcome Header**:
   - "Good [morning/afternoon/evening], [DisplayName]"
   - Institution name and code as subtitle
   - Current date formatted: "Monday, 3 March 2026"
   - Quick action buttons: "Submit Return", "View Calendar"
   - A status indicator showing institution's overall compliance health (Green/Amber/Red)

2. **Compliance Score Card** (hero metric):
   - Large circular progress indicator showing compliance percentage
   - "X of Y returns submitted this period"
   - Trend arrow: up/down compared to last period
   - Color: green (≥90%), amber (70-89%), red (<70%)

3. **Stat Cards Row** (4 cards):
   - **Due This Month**: Count of returns due, with "X overdue" highlight in red if any
   - **Submitted**: Count submitted in current period, with accepted/rejected breakdown
   - **Pending Review**: Count awaiting checker approval (if maker-checker enabled)
   - **Validation Score**: Average validation pass rate across recent submissions

4. **Upcoming Deadlines** (next 5 due returns):
   - Table/card list showing: Return code, return name, due date, days remaining, status (Not Started / Draft / Submitted / Overdue)
   - Color-coded urgency: green (>7 days), amber (1-7 days), red (overdue)
   - Click to go directly to submission page for that return

5. **Recent Submissions** (last 10):
   - Table: Return code, period, submitted date, status badge (Accepted ✓ / AcceptedWithWarnings ⚠ / Rejected ✗), validation errors/warnings count
   - Click to go to submission detail

6. **Compliance Trend** (optional — CSS-only bar chart):
   - Last 6 months' submission rates as horizontal bars
   - Shows month name and % complete

### Data Requirements
- Query submissions for the current institution (from claims `InstitutionId`)
- Query return periods that are open for the institution's frequency
- Query templates applicable to the institution (based on frequency and institution type)
- Compute compliance metrics from submission data

### Technical Implementation
- Create a `DashboardService` in `FC.Engine.Portal/Services/` that aggregates all dashboard data
- Use `ISubmissionRepository`, `ITemplateMetadataCache`, and return period data
- Cache dashboard data for 5 minutes to avoid repeated queries

### CSS Requirements
- Premium stat cards with left accent borders (matching Admin dashboard pattern)
- Smooth entrance animations (staggered `iaFadeSlideUp`)
- Responsive: 4-column → 2-column → 1-column stat cards
- Print-friendly layout
- Loading skeleton while data loads
- CBN color palette throughout

### Verification
- Dashboard loads with real data for authenticated institution
- All metrics compute correctly
- Responsive across all viewports
- Loading states display correctly
- Build succeeds with 0 errors

---

## Prompt 5: Return Submission Workflow — Upload & Real-Time Validation

### Objective
Build the core submission experience — a multi-step wizard where institution users select a return template, upload their XML file, see real-time validation results, and submit their return. This is the most critical page in the entire portal.

### Workflow Steps

**Step 1: Select Return**
- Dropdown/searchable select showing all templates applicable to the institution
- Group by frequency (Monthly, Quarterly, Semi-Annual)
- Show for each: return code, name, due date for current period, status (already submitted?)
- If already submitted for current period, show warning with option to re-submit

**Step 2: Select Reporting Period**
- Show open periods for the selected template's frequency
- Default to the current/most recent open period
- Show period details: year, month, reporting date, deadline

**Step 3: Upload XML File**
- **Drag-and-drop zone**: Large dashed border area, "Drag your XML file here or click to browse"
- File type validation: only `.xml` files
- File size validation: max 10MB
- File name display after selection
- "Remove" button to clear selection
- **Alternative**: "Paste XML" button that opens a modal with a textarea for pasting XML content directly

**Step 4: Validation & Preview**
- After file upload, automatically trigger validation via `IngestionOrchestrator`
- Show a **real-time progress indicator** with phases:
  1. "Parsing XML..." (with spinner)
  2. "Validating schema..."
  3. "Checking field types and ranges..."
  4. "Evaluating formulas..."
  5. "Cross-sheet validation..."
  6. "Business rule checks..."
- After validation, show results:
  - **Summary**: Total errors, total warnings, overall status (Pass/Fail)
  - **Errors Table**: Sortable, filterable table showing each validation error with: Rule ID, Field, Message, Severity (Error/Warning), Category, Expected Value, Actual Value
  - **Error Categorization**: Group by category (Schema, TypeRange, IntraSheet, CrossSheet, Business) with counts
  - **Data Preview**: Show the parsed data in a tabular format so users can see what was extracted

**Step 5: Review & Submit**
- If validation passes (no errors, warnings OK):
  - Show green success banner: "Validation Passed — Ready to Submit"
  - "Submit Return" button
  - If maker-checker is enabled, "Submit for Approval" button instead
- If validation fails:
  - Show red error banner: "Validation Failed — X errors found"
  - Disable submit button
  - "Download Error Report" button (generates a summary)
  - "Fix & Re-upload" button that goes back to Step 3

### Technical Implementation

1. **Page**: `Components/Pages/Submissions/SubmitReturn.razor` at route `/submit`
2. **Use the existing `IngestionOrchestrator`** for the full pipeline
3. **File upload**: Use Blazor's `InputFile` component with `IBrowserFile`
4. **Streaming**: Read the uploaded file as a stream, pass to IngestionOrchestrator
5. **Progress**: Use a state machine with `StateHasChanged()` calls between phases (note: the orchestrator runs phases sequentially, so simulate progress by updating state before/after each phase)
6. **Validation Results**: Map from `SubmissionResultDto` → display model
7. **Data Preview**: Use `ReturnDataRecord` to build a preview table (fields as columns, rows as rows)

### CSS Requirements
- Step indicator at the top showing current step (circles connected by lines)
- Active step highlighted in CBN green, completed steps with check marks
- Drag-and-drop zone with dashed border, hover effect (border turns green)
- File upload animation
- Validation progress with animated spinner and phase checkmarks
- Error table with severity-colored left borders (red for errors, amber for warnings)
- Responsive: wizard works beautifully on mobile
- Print-friendly error report

### Verification
- Complete submission workflow works end-to-end
- XML file upload and validation produces correct results
- Error display matches actual validation errors
- Progress indicator updates through all phases
- Re-upload after failure works correctly
- Build succeeds with 0 errors

---

## Prompt 6: Submission History & Detail Pages

### Objective
Build the submission history list and detail pages where institution users can view all their past submissions, filter by status/period/template, drill into validation reports, and manage their submission records.

### Submission List Page (`/submissions`)

1. **Page Header**: "My Submissions" with total count badge
2. **Toolbar**:
   - Search by return code or submission ID
   - Filter by: Status (All, Accepted, AcceptedWithWarnings, Rejected), Period (dropdown of return periods), Template (dropdown of return codes)
   - Date range picker (from/to)
   - Sort by: Submitted date (default desc), Status, Return code
3. **Submission Table**:
   - Columns: Submission ID, Return Code, Return Name, Period (e.g., "Jan 2026"), Submitted Date, Status Badge, Errors, Warnings, Processing Time, Actions
   - Status badges: Green (Accepted), Amber (AcceptedWithWarnings), Red (Rejected), Gray (Draft/Parsing/Validating)
   - Row click → navigate to detail page
   - Pagination: 20 per page
4. **Empty State**: "No submissions yet. Start by submitting your first return." with link to `/submit`
5. **Loading State**: Skeleton rows

### Submission Detail Page (`/submissions/{id}`)

1. **Breadcrumb**: Dashboard / Submissions / MFCR 300 - Jan 2026
2. **Hero Section**:
   - Return code and name (large)
   - Period badge
   - Status badge (large, color-coded)
   - Submitted date/time
   - Processing duration
   - Submitted by user
3. **Validation Summary Cards** (3 cards):
   - Errors count (red if > 0)
   - Warnings count (amber if > 0)
   - Rules passed count (green)
4. **Validation Report** (main content area):
   - Tab 1: **All Issues** — combined error + warning table, sortable
   - Tab 2: **By Category** — accordion panels per category (Schema, TypeRange, IntraSheet, CrossSheet, Business) with count badges
   - Tab 3: **Data Preview** — the parsed data shown in tabular format (queried from physical data table via `IGenericDataRepository`)
   - Tab 4: **Raw XML** — syntax-highlighted XML view of the submitted file (from `Submission.RawXml`)
5. **Actions Panel**:
   - "Re-submit" button → navigates to `/submit` with pre-selected template and period
   - "Download Report" → generates printable validation report
   - "Download XML" → downloads the original submitted XML file
6. **Approval Section** (if maker-checker enabled):
   - Show approval status, reviewer name, review date, comments
   - If pending and user is Checker: "Approve" / "Reject" buttons with comments field

### Technical Implementation
- Query submissions scoped to the authenticated institution (from claims)
- Use `ISubmissionRepository.GetByInstitution()` for list
- Use `ISubmissionRepository.GetByIdWithReport()` for detail
- Use `IGenericDataRepository.GetBySubmission()` for data preview
- For tab navigation, use CSS-only tabs (no JS) or simple Blazor state toggling

### CSS Requirements
- Follow the existing Admin list page pattern (`list-*` CSS classes) but prefixed for portal
- Premium data table with hover rows, status badges, action buttons
- Detail page with hero section matching Admin's detail page pattern
- Tabs with bottom border indicator
- Validation error table with severity color-coding
- Raw XML display with monospace font and line numbers
- Print stylesheet for downloading validation report
- Responsive across all viewports

### Verification
- Submission list shows only the current institution's submissions
- Filters and search work correctly
- Pagination works
- Detail page shows all validation information correctly
- Data preview renders the physical table data
- Build succeeds with 0 errors

---

## Prompt 7: Reporting Calendar & Deadline Tracker

### Objective
Build a reporting calendar page that shows all upcoming and past reporting obligations for the institution, with deadline tracking, color-coded urgency, and quick-submit actions.

### Page: `/calendar`

1. **Calendar Header**:
   - Current month/year with navigation arrows (← March 2026 →)
   - Toggle between: Calendar View / List View / Timeline View
   - Filter by frequency: All, Monthly, Quarterly, Semi-Annual

2. **Calendar View** (default):
   - Monthly grid (7 columns for days of week)
   - Each day cell shows return codes due on that day
   - Color-coded dots: green (submitted), amber (pending), red (overdue), gray (upcoming)
   - Click on a day → expand to show returns due that day with status and submit link
   - Today highlighted with CBN green border

3. **List View**:
   - Sorted by due date (ascending)
   - Each row: Return code, return name, frequency badge, due date, days until due, status, action button
   - Status: "Submitted ✓", "Not Started", "Overdue (X days)", "Draft in Progress"
   - Group by month with section headers

4. **Timeline View**:
   - Vertical timeline with dots for each deadline
   - Past deadlines: green (met) or red (missed)
   - Future deadlines: amber (upcoming within 7 days) or gray (future)
   - Each node shows return details and quick action

5. **Period Summary Panel** (sidebar or top section):
   - "Returns due this month: X"
   - "Submitted: X"
   - "Outstanding: X"
   - "Overdue: X" (in red if > 0)
   - Progress bar: submitted/total

### Data Requirements
- For each template applicable to the institution (based on frequency and institution type):
  - Determine the due dates based on `ReturnPeriod` data
  - Check if a submission exists for each template+period combination
  - Calculate status: Submitted, Not Started, Overdue, Draft
- Due date calculation: Use the `ReturnPeriod.ReportingDate` as the deadline

### Technical Implementation
- Create a `CalendarService` in `FC.Engine.Portal/Services/`
- Queries: All templates for institution → all open return periods → existing submissions
- Build a `CalendarEntry` model: ReturnCode, TemplateName, DueDate, Period, Status, SubmissionId?
- CSS-only calendar grid (no JavaScript calendar library)

### CSS Requirements
- Clean monthly calendar grid with subtle borders
- Day cells with hover effects
- Return code chips inside day cells with status colors
- Smooth view transitions between Calendar/List/Timeline
- Responsive: calendar collapses to list on mobile
- Print-friendly version shows list view

### Verification
- Calendar correctly shows all reporting deadlines
- Submitted returns show as completed
- Overdue returns highlighted in red
- View switching works smoothly
- Responsive on all viewports

---

## Prompt 8: Template Browser & Schema Download Center

### Objective
Build a resource page where institution users can browse all return templates they're required to submit, view field definitions, download XSD schemas for XML generation, and access sample XML files.

### Page: `/templates`

1. **Page Header**: "Template Browser" with description: "Browse all return templates, view field requirements, and download schemas for XML generation"

2. **Template Grid/List**:
   - Search bar: search by code or name
   - Filters: Frequency (Monthly/Quarterly/Semi-Annual), Category (FixedRow/MultiRow/ItemCoded)
   - Card view (default) or Table view toggle
   - Each template card shows:
     - Return code (prominent, e.g., "MFCR 300")
     - Template name
     - Frequency badge (Monthly/Quarterly)
     - Category badge (FixedRow/MultiRow/ItemCoded)
     - Field count
     - Formula count
     - "View Details" button
     - "Download XSD" quick action button

3. **Template Detail Modal/Page** (`/templates/{returnCode}`):
   - Template header: code, name, description, frequency, category
   - **Fields Tab**: Table showing all fields with: Field Name, Display Name, Data Type, Required?, Constraints (min/max/allowed values), Section
   - **Formulas Tab**: Table showing validation formulas: Rule Code, Type, Target Field, Description, Severity
   - **Schema Tab**: Syntax-highlighted XSD display with "Copy" and "Download" buttons
   - **Sample XML Tab**: Auto-generated sample XML from field metadata with placeholder values
   - **Submission History**: Mini-table showing the institution's past submissions for this template

4. **Bulk Download Section**:
   - "Download All Schemas" button → generates a ZIP of all XSD files
   - "Download Template Guide" → PDF-like printable guide of all templates (use print CSS)

### Technical Implementation
- Use `ITemplateMetadataCache.GetAllPublishedTemplates()` for the list
- Use `IXsdGenerator.GenerateSchemaXml(returnCode)` for XSD content
- Generate sample XML from template fields: create valid XML with placeholder values based on data type (Money → "0.00", Text → "SAMPLE", Date → "2026-01-01", etc.)
- Use `ISubmissionRepository.GetByInstitution()` filtered by returnCode for history

### CSS Requirements
- Template cards with clean design, category-colored top border
- Schema display with monospace font, syntax highlighting (keywords in blue, attributes in green, values in red — all CSS-based)
- Responsive card grid: 3 columns → 2 → 1
- Tab navigation for detail view
- Copy button with "Copied!" feedback animation

### Verification
- All templates display correctly with accurate field counts
- XSD downloads are valid XML schemas
- Sample XML matches template structure
- Search and filters work correctly
- Build succeeds with 0 errors

---

## Prompt 9: Web-Based Data Entry Form (Alternative to XML Upload)

### Objective
Build a web-based data entry form that allows institution users to fill in return data directly in the browser instead of uploading XML. The form is dynamically generated from template metadata — any published template can be rendered as a form with zero hardcoded UI.

### Page: `/submit/form/{returnCode}`

### Design Approach

1. **Template Selection**: Same as the upload wizard Step 1 — user selects template and period, then chooses "Enter Data Manually" instead of "Upload XML"

2. **Dynamic Form Generation**:
   - Read `TemplateField` metadata from cache
   - Group fields by `SectionName`
   - Render each field as the appropriate HTML input based on `DataType`:
     - Money/Decimal/Percentage → `<input type="number" step="0.01">` with formatting
     - Integer → `<input type="number" step="1">`
     - Text → `<input type="text" maxlength="...">`
     - Date → `<input type="date">`
     - Boolean → `<input type="checkbox">`
   - Mark required fields with asterisk and `required` attribute
   - Show field constraints as helper text (min/max, allowed values)
   - Show `HelpText` as tooltip/info icon

3. **Structural Category Handling**:
   - **FixedRow**: Single form with all fields laid out in sections
   - **MultiRow**: Form with an "Add Row" button, each row is a repeating group of fields. Show a mini-table of existing rows with edit/delete actions
   - **ItemCoded**: One row per item code from `TemplateItemCode`. Show item code and description as row headers, fields as columns in a horizontal table

4. **Real-time Validation**:
   - Client-side validation: required fields, data types, min/max ranges
   - Show inline validation errors below each field
   - On "Validate" button click: generate XML from form data, pass through IngestionOrchestrator
   - Show validation results in a collapsible panel below the form

5. **Form Actions**:
   - "Save as Draft" → persist partial data (store as JSON in a new table or localStorage)
   - "Validate" → run full validation pipeline, show results
   - "Submit" → validate + submit (same as XML upload submit)
   - "Clear Form" → reset all fields
   - "Export as XML" → generate XML from current form data and download

6. **Auto-calculation**:
   - For Sum formulas: when operand fields change, auto-calculate the target field value and pre-fill it
   - Show a small "Calculated" badge on auto-computed fields
   - User can override auto-calculated values (with warning)

### Technical Implementation

1. **XML Generation from Form Data**: Build a service that converts form data (Dictionary<string, string>) → valid XML matching the template's XSD schema
2. **Use `ITemplateMetadataCache`** to get field definitions
3. **Use `IXsdGenerator`** to validate the generated XML
4. **Use `IngestionOrchestrator`** for full validation
5. **Form state**: Maintain form data as `Dictionary<string, string>` in component state
6. Create a `FormDataToXmlService` in `FC.Engine.Portal/Services/`

### CSS Requirements
- Clean form layout with section headers
- 2-column layout for fields (responsive to 1 column on mobile)
- Required field indicators with red asterisks
- Inline validation error styling (red border, error message below)
- Auto-calculated fields with subtle green background
- MultiRow table with add/edit/delete row actions
- ItemCoded table with sticky headers
- Form progress indicator (X of Y sections completed)

### Verification
- Forms render correctly for all 3 structural categories
- Required field validation works
- Data types enforce correct input
- Generated XML passes XSD validation
- Full submission pipeline works from form data
- Build succeeds with 0 errors

---

## Prompt 10: Validation Results Deep-Dive & Remediation Guidance

### Objective
Build a comprehensive validation results experience that not only shows errors but helps users understand and fix them. This is critical for reducing support burden — institution users should be self-sufficient in resolving validation issues.

### Enhancement to Submission Detail (`/submissions/{id}`)

1. **Validation Error Cards** (instead of just a table):
   - Each error is a card with:
     - Severity icon (Error ✗ in red, Warning ⚠ in amber, Info ℹ in blue)
     - Rule ID (e.g., "MFCR300-SUM-001")
     - Error message (clear, human-readable)
     - Field name with section context
     - Expected vs. Actual values (side-by-side comparison)
     - Category badge (Schema/TypeRange/IntraSheet/CrossSheet/Business)
     - **Remediation guidance**: A helpful text explaining how to fix the error
     - **Related fields**: For formula errors, show all operand fields and their values

2. **Remediation Guidance Engine** — Generate fix suggestions based on error category:
   - **Schema errors**: "The XML element 'XYZ' is not recognized. Check your XML structure matches the schema. [Download Schema]"
   - **TypeRange errors**: "The field '{field}' expects a {type} value between {min} and {max}. Current value: {actual}."
   - **IntraSheet Sum errors**: "The sum of {operand fields} should equal {target field}. Sum = {calculated sum}, Target = {target value}. Difference: {diff}."
   - **CrossSheet errors**: "The value of {field} in {template A} should equal {field} in {template B}. Check both returns for consistency."
   - **Business rule errors**: Render the rule's description as guidance

3. **Error Grouping Views**:
   - **By Severity**: Errors first, then warnings, then info
   - **By Category**: Accordion panels per validation phase
   - **By Field**: Group errors by field name (useful when one field has multiple issues)
   - **By Row** (for MultiRow/ItemCoded): Group by row key showing all errors per row

4. **Error Statistics Panel**:
   - Donut/ring chart showing error distribution by category (reuse RingChart component)
   - Bar chart showing errors per section/field
   - "Most Common Error" highlight

5. **Comparison View** (for re-submissions):
   - If this is a re-submission, show a diff between previous and current validation results
   - "Fixed: 5 errors" (in green), "New: 1 error" (in red), "Remaining: 3 errors" (in amber)
   - Timeline of all submission attempts for this template+period

6. **Export Options**:
   - "Download Detailed Report" (printable HTML with all errors, guidance, and data)
   - "Download CSV" (errors as CSV for spreadsheet analysis)
   - "Share with Team" (copy link to this submission's detail page)

### Technical Implementation
- Create a `RemediationService` in `FC.Engine.Portal/Services/` that generates guidance text from error metadata
- Use `ValidationError.Category`, `ValidationError.Field`, `ValidationError.ExpectedValue`, `ValidationError.ActualValue` to build contextual guidance
- For cross-sheet errors, use `ValidationError.ReferencedReturnCode` to link to the related template
- For comparison view, query previous submissions for the same template+period+institution

### CSS Requirements
- Error cards with severity-colored left border
- Expandable/collapsible error details
- Side-by-side expected vs. actual value display
- Remediation text in a subtle info box
- Ring charts for error distribution
- Smooth accordion animations for category grouping
- Print-optimized layout for report download

### Verification
- Remediation guidance generates correctly for all error categories
- Grouping views all work and switch smoothly
- Error statistics calculate correctly
- Comparison view shows differences between submissions
- Print report is well-formatted

---

## Prompt 11: Maker-Checker Approval Workflow

### Objective
Implement the full maker-checker approval workflow for institutions that require dual authorization before returns are officially submitted. A "Maker" prepares and uploads the return, and a "Checker" reviews and approves/rejects it before it's considered officially submitted.

### Workflow

1. **Institution Setting**: Add a toggle in institution settings to enable/disable maker-checker
2. **Maker Flow**:
   - Maker uploads XML or fills form → validation runs → if valid
   - Instead of "Submit Return", Maker sees "Submit for Approval"
   - Submission status becomes "PendingApproval" (new status)
   - Maker adds optional notes for the Checker
   - Notification appears in Checker's dashboard
3. **Checker Flow**:
   - Checker sees pending approvals on their dashboard and on a dedicated "Pending Approvals" page
   - Checker clicks into submission detail → reviews validation results, data preview
   - Checker can: "Approve" (submission finalizes as Accepted/AcceptedWithWarnings) or "Reject" (submission goes back to Maker with comments)
   - Checker must provide comments when rejecting
4. **Rejection Re-submission**:
   - Maker sees rejected submissions with Checker's comments
   - Maker can fix and re-submit, creating a new submission linked to the same approval chain

### New Components

1. **Pending Approvals Page** (`/approvals`):
   - List of submissions pending the current user's review
   - Table: Submission ID, Return Code, Period, Submitted By, Submitted Date, Validation Summary
   - Quick actions: Approve / Review / Reject
   - Only visible to Checker and Admin roles

2. **Approval Panel** (in Submission Detail):
   - Shows approval history: who submitted, who reviewed, when, comments
   - Approve/Reject buttons for Checkers
   - Comments text area for rejection reason
   - Timeline of approval events

3. **Extend `SubmissionStatus` enum**: Add `PendingApproval`, `ApprovalRejected`

4. **Notification badges**: Show count of pending approvals in NavMenu

### Technical Implementation
- Extend `Submission` entity with `ApprovalRequired` flag
- Create `SubmissionApproval` entity (from Prompt 2)
- Create `ApprovalService` in `FC.Engine.Portal/Services/`
- Update `IngestionOrchestrator` or create a wrapper that checks institution's maker-checker setting
- Add approval logic: on approve → finalize submission; on reject → update status

### CSS Requirements
- Approval panel with timeline visualization
- Approve button (green), Reject button (red) with confirmation modals
- Pending approval notification badge (red dot with count)
- Comments section with textarea styling
- Status timeline with connected dots

### Verification
- Maker-checker flow works end-to-end
- Checker can approve and reject with comments
- Rejected submissions can be re-submitted by Maker
- Notifications update correctly
- Non-checker users cannot see approval buttons

---

## Prompt 12: Institution Profile & Settings Management

### Objective
Build the institution profile and settings management pages where institution admins can view their institution's details, manage team members, and configure portal preferences.

### Pages

1. **Institution Profile** (`/institution`):
   - Institution name, code, license type
   - Contact details: email, phone, address
   - Account status: Active/Suspended
   - Subscription tier: Basic/Standard/Premium
   - Stats: Total users, total submissions, last submission date
   - Edit button (for institution admin only) to update contact details

2. **Team Members** (`/institution/team`):
   - Table of all institution users: Name, Email, Role, Status (Active/Inactive), Last Login
   - Add new member (institution admin only): name, email, role selector, temporary password
   - Edit member: change role, activate/deactivate
   - Deactivate member (with confirmation)
   - Enforce max user limit from `Institution.MaxUsersAllowed`
   - Show current user count vs. max: "5 of 10 user slots used"

3. **Settings** (`/institution/settings`):
   - Maker-Checker: Toggle on/off
   - Notification preferences: Email on submission result, email on deadline approaching
   - Default submission format: XML Upload / Manual Entry
   - Session timeout preference: 2 hours / 4 hours / 8 hours
   - Timezone preference for deadline display

### Technical Implementation
- Use `InstitutionAuthService` for user management
- Add `IInstitutionRepository` with update capability (extend existing)
- Create `InstitutionSettingsService` to manage settings (store in a new `institution_settings` JSON column or table)
- Authorization: Only `InstitutionRole.Admin` can manage team and settings

### CSS Requirements
- Clean profile card with avatar/initials and details
- Team member table with action buttons
- Settings page with toggle switches, select dropdowns
- Form validation with inline errors
- Responsive layout

### Verification
- Institution admin can manage team members
- User count limit is enforced
- Settings changes persist and take effect
- Non-admin users see read-only views

---

## Prompt 13: Notification System — In-App & Toast Alerts

### Objective
Build a comprehensive notification system for the FI Portal that keeps users informed about important events: submission results, approaching deadlines, approval requests, and system announcements.

### Components

1. **Toast Notifications** (reuse pattern from Admin):
   - Create `ToastService` and `ToastContainer` for the Portal (same pattern as Admin)
   - Variants: Success, Warning, Error, Info
   - Auto-dismiss with progress bar
   - Stack up to 5 notifications

2. **Notification Bell** (in TopBar):
   - Bell icon with red badge showing unread count
   - Dropdown panel on click showing recent notifications
   - Each notification: icon, title, message, timestamp, read/unread indicator
   - "Mark all as read" button
   - "View all" link → notification center page

3. **Notification Center** (`/notifications`):
   - Full list of all notifications
   - Filter by type: Submission, Deadline, Approval, System
   - Mark individual notifications as read
   - Clear read notifications

4. **Notification Types**:
   - **Submission Result**: "Your MFCR 300 return for Jan 2026 was [Accepted/Rejected]"
   - **Deadline Approaching**: "MFCR 300 is due in 3 days (March 6, 2026)"
   - **Approval Request**: "New submission pending your approval: MFCR 300 - Jan 2026"
   - **Approval Result**: "Your submission for MFCR 300 was [Approved/Rejected] by [Checker Name]"
   - **System Announcement**: "Maintenance window: March 10, 2026 2:00 AM - 4:00 AM UTC"

5. **Notification Storage**:
   - New entity: `PortalNotification` (UserId, Type, Title, Message, IsRead, CreatedAt, Link, Metadata JSON)
   - New repository: `INotificationRepository`
   - New service: `NotificationService` (create, mark read, get unread count, get paginated list)

### Technical Implementation
- Notifications are created by background processes or triggered by events (submission completion, deadline check)
- For real-time updates in Blazor Server: Use a timer-based polling approach (check every 30 seconds) or SignalR hub for push
- Badge count updates without full page reload using `StateHasChanged()`

### CSS Requirements
- Notification dropdown with subtle animation (slide down, fade in)
- Unread badge pulsing red dot
- Notification cards with type-specific icons
- Read vs. unread visual distinction
- Notification center with clean list layout
- Responsive dropdown on mobile

### Verification
- Notifications appear for all event types
- Unread count badge updates correctly
- Mark as read works
- Notification dropdown works on mobile
- Toast notifications display correctly

---

## Prompt 14: Help Center & Documentation Hub

### Objective
Build an in-app help center that provides documentation, FAQs, submission guides, and troubleshooting resources so institution users can be self-sufficient without contacting CBN support.

### Page: `/help`

1. **Help Home**:
   - Search bar: "Search for help..."
   - Quick links: "How to Submit a Return", "Understanding Validation Errors", "XML Schema Guide", "Contact Support"
   - Popular topics grid (6-8 cards)

2. **Getting Started Guide** (`/help/getting-started`):
   - Step-by-step visual guide for first-time users
   - Account setup → Download schema → Prepare XML → Upload → Review validation → Submit
   - Screenshots/illustrations (CSS mockups)

3. **Submission Guide** (`/help/submission-guide`):
   - Detailed walkthrough of the submission process
   - XML format requirements
   - Common XML errors and how to fix them
   - Sample XML for each structural category (FixedRow, MultiRow, ItemCoded)

4. **Validation Error Guide** (`/help/validation-errors`):
   - Catalog of all error types with explanations
   - Each error type: what it means, common causes, how to fix
   - Organized by validation category (Schema, TypeRange, IntraSheet, CrossSheet, Business)

5. **FAQ** (`/help/faq`):
   - Accordion-based FAQ (reuse AccordionPanel component)
   - Categories: Account, Submission, Validation, Technical, Compliance

6. **Contact Support** (`/help/support`):
   - CBN support contact details
   - Support ticket form (Name, Email, Category, Description, Attachment)
   - Note: Ticket form can be a placeholder in Phase 1

### Technical Implementation
- Content can be static Razor pages (no database needed)
- Search can be client-side text filtering
- Reuse AccordionPanel component for FAQ
- Link help articles from error remediation guidance (Prompt 10)

### CSS Requirements
- Clean documentation layout with sidebar navigation
- Code blocks with syntax highlighting for XML examples
- Step-by-step guides with numbered circles
- FAQ accordion styling
- Search with highlighting of matches
- Print-friendly for guide downloading

### Verification
- All help pages render correctly
- FAQ accordion works
- Search filters content appropriately
- Links from validation errors lead to correct help sections
- Responsive and accessible

---

## Prompt 15: Bulk Submission & Multi-Return Upload

### Objective
Build a bulk submission feature that allows institution users to upload multiple XML files at once (one per return template) and process them as a batch with aggregate results.

### Page: `/submit/bulk`

1. **Multi-File Upload Zone**:
   - Drag-and-drop area that accepts multiple `.xml` files
   - Shows file list after selection with: filename, detected return code (from XML root element), file size, status
   - Auto-detect return code from XML namespace/root element
   - Show warning for files that don't match any known template
   - Remove individual files from the batch

2. **Period Selection**:
   - Single period selector that applies to all files in the batch
   - Or per-file period override if different returns have different periods

3. **Batch Processing**:
   - "Validate All" button → processes each file sequentially
   - Progress indicator: "Processing 3 of 7 files..."
   - Per-file status updates: ✓ Passed / ✗ Failed / ⟳ Processing
   - Overall batch summary: X passed, Y failed, Z warnings

4. **Batch Results**:
   - Summary card: "5 of 7 returns passed validation"
   - Per-file results in expandable rows: return code, status, error count, warning count
   - Expand to see validation details per file
   - "Submit All Valid" button → submits only the ones that passed
   - "Download Batch Report" → combined validation report for all files

5. **Batch History**:
   - Past batch submissions shown as groups in submission history
   - Batch ID linking related submissions

### Technical Implementation
- Use `InputFile` with `multiple` attribute for multi-file upload
- Process files one at a time through `IngestionOrchestrator`
- Track batch as a group (use a correlation ID or batch entity)
- Stream each file, validate, collect results
- Allow partial submission (only valid files)

### CSS Requirements
- Multi-file drop zone with file list
- Per-file status indicators (checkmark, X, spinner)
- Batch progress bar
- Expandable per-file results
- Aggregate summary cards
- Responsive layout

### Verification
- Multiple XML files can be uploaded and processed
- Return code auto-detection works
- Batch results show correct per-file status
- Partial submission (only valid files) works
- Build succeeds with 0 errors

---

## Prompt 16: Print, Export & Compliance Reports

### Objective
Build comprehensive export and reporting capabilities so institution users can download, print, and share their compliance data in multiple formats.

### Features

1. **Submission Validation Report** (from Submission Detail):
   - CBN-branded PDF-like printable HTML report
   - Header: CBN logo, "Validation Report", institution name, date
   - Summary: Return code, period, status, error/warning counts
   - Full error listing with remediation guidance
   - Data preview table
   - Footer: "Generated by RegOS™ — Central Bank of Nigeria"
   - Use `@media print` CSS

2. **Compliance Certificate** (for accepted submissions):
   - Formal CBN-branded certificate-style document
   - "This certifies that [Institution Name] has successfully submitted [Return Code] for the period [Period] in compliance with CBN reporting requirements"
   - Submission ID, timestamp, validation status
   - Printable/downloadable

3. **Periodic Compliance Report** (`/reports/compliance`):
   - Monthly/quarterly summary of all submissions
   - Table: Return code, status, submission date, validation result
   - Overall compliance percentage
   - Returns that were missed/late
   - Exportable as CSV

4. **Data Export** (from Submission Detail):
   - Export submitted data as CSV
   - Export submitted data as Excel-compatible (CSV with proper formatting)
   - Export original XML
   - Export validation errors as CSV

5. **Audit Trail Report** (`/reports/audit`):
   - Institution's submission activity log
   - Table: Date, User, Action (Submitted/Resubmitted/Approved/Rejected), Return Code, Status
   - Date range filter
   - Export as CSV

### Technical Implementation
- Use `@media print` CSS for all printable reports
- For CSV export: build CSV string in C#, trigger browser download via JS interop
- Add a minimal JS file for download functionality: `portal.js`
- Create `ExportService` in `FC.Engine.Portal/Services/`

### CSS Requirements
- Print stylesheets with CBN branding
- Certificate layout with formal styling
- Report tables with alternating row colors
- Export buttons with download icon
- Preview modal before printing

### Verification
- Print layouts render correctly in browser print preview
- CSV exports contain correct data
- Compliance report calculations are accurate
- Certificate displays correctly for accepted submissions

---

## Prompt 17: Cross-Sheet Validation Dashboard

### Objective
Build a specialized dashboard that shows the status of cross-sheet validation rules across the institution's submissions. This helps users understand inter-template dependencies and resolve cross-sheet validation errors.

### Page: `/validation/cross-sheet`

1. **Overview Header**:
   - "Cross-Sheet Validation Status"
   - Description: "These rules validate consistency between different return templates"
   - Summary: "X of Y rules passing for current period"

2. **Rule Status Table**:
   - Each cross-sheet rule with: Rule Code, Rule Name, Status (Pass/Fail/Not Evaluated)
   - Templates involved (as chips/badges)
   - Expression (e.g., "MFCR100.total_assets = MFCR300.total_assets")
   - Last evaluated timestamp
   - Expand to see: operand values, expected result, actual result, tolerance
   - "Not Evaluated" = one or more required templates haven't been submitted yet

3. **Dependency Map**:
   - Visual representation of which templates are connected by cross-sheet rules
   - Nodes: templates (green if submitted, gray if pending)
   - Edges: rules connecting them (green if passing, red if failing)
   - CSS-only graph (similar to Impact Analysis dependency graph in Admin)

4. **Missing Dependencies Panel**:
   - List of templates that need to be submitted for cross-sheet validation to run
   - Quick action: "Submit Now" links for each missing template

5. **Historical Trend**:
   - Cross-sheet pass rate over last 6 periods (horizontal bar chart)

### Technical Implementation
- Use `IFormulaRepository.GetAllActiveCrossSheetRules()` for rules
- Use `ISubmissionRepository.GetByInstitution()` to check which templates have been submitted
- Use `ICrossSheetValidator` concepts to show rule status
- Create `CrossSheetDashboardService` in `FC.Engine.Portal/Services/`

### CSS Requirements
- Rule status cards with pass/fail indicators
- Dependency graph using CSS grid and connector lines
- Template node badges with status colors
- Expandable rule detail panels
- Responsive layout

### Verification
- Cross-sheet rules display correctly
- Status correctly reflects pass/fail based on submitted data
- Missing dependencies identified correctly
- Dependency map renders accurately

---

## Prompt 18: Real-Time Validation Preview (Pre-Submission Check)

### Objective
Build a pre-submission validation check feature that lets users validate their XML against the schema and rules without creating a formal submission. This is a "dry run" that doesn't persist any data.

### Page: `/validate` or Feature on Submit Page

1. **Quick Validate Mode**:
   - Lightweight interface: just template selector + file upload
   - "Check My File" button
   - Runs full validation pipeline but does NOT persist submission or data
   - Shows results immediately
   - No record is created — this is purely advisory

2. **Live Validation** (enhancement to data entry form):
   - As users fill in the web form, run intra-sheet formula validation in real-time
   - For Sum formulas: show running total and highlight mismatches
   - For Required fields: highlight missing values
   - For Range checks: highlight out-of-bounds values
   - Use green checkmarks for fields that pass, red indicators for failures
   - Update on each field change (debounced)

3. **Schema Check Only Mode**:
   - Just validate XML against XSD schema
   - Much faster than full validation
   - Useful for checking XML structure before filling in values

### Technical Implementation
- Modify or create a `DryRunValidationService` that:
  - Runs through IngestionOrchestrator pipeline but skips the persistence step
  - Does NOT create a Submission record
  - Returns validation results only
- For live form validation: implement client-side formula evaluation or use a lightweight server-side check via `FormulaEvaluator` on partial data
- Debounce form field changes (300ms delay before validation triggers)

### CSS Requirements
- Quick validate page with minimal, clean interface
- Real-time validation indicators (green checkmarks, red X marks, amber warnings)
- Running total display for sum formulas
- Field-level status badges
- Results panel with progressive disclosure

### Verification
- Dry run validation works without creating submissions
- Real-time form validation updates on field changes
- Schema-only check mode works correctly
- No data is persisted during pre-submission checks

---

## Prompt 19: Responsive Design, Accessibility & Performance Polish

### Objective
Comprehensive pass over the entire FI Portal to ensure production-grade responsive design, WCAG 2.1 AA accessibility compliance, performance optimization, and cross-browser compatibility.

### Responsive Design (all pages)

1. **Breakpoints**:
   - Desktop: ≥1200px (4-column layouts, full sidebar)
   - Tablet: 768-1199px (2-column, collapsible sidebar)
   - Mobile: <768px (1-column, bottom nav or hamburger menu)
   - Small mobile: <480px (compact spacing, stacked elements)

2. **Navigation**:
   - Desktop: Fixed left sidebar
   - Tablet: Collapsible sidebar with overlay
   - Mobile: Bottom navigation bar with key actions (Dashboard, Submit, History, Menu) OR hamburger menu

3. **Tables**:
   - Desktop: Full table with all columns
   - Tablet: Hide less important columns, add horizontal scroll
   - Mobile: Transform to card layout (each row becomes a card)

4. **Forms**:
   - Desktop: 2-column layout
   - Mobile: Single column, full-width inputs

### Accessibility (WCAG 2.1 AA)

1. **Semantic HTML**: Ensure all pages use proper `<main>`, `<nav>`, `<header>`, `<section>`, `<article>`, `<aside>`, `<button>`, etc.
2. **ARIA Labels**: Every interactive element has `aria-label` or `aria-labelledby`
3. **Focus Management**: Visible focus indicators, logical tab order, focus trapping in modals
4. **Screen Reader**: All images have alt text, decorative elements have `aria-hidden="true"`, live regions for dynamic content
5. **Keyboard Navigation**: Every action achievable via keyboard, Escape closes modals/dropdowns
6. **Color Contrast**: All text meets 4.5:1 contrast ratio (7:1 for small text)
7. **Reduced Motion**: `@media (prefers-reduced-motion: reduce)` disables all animations
8. **Skip Links**: "Skip to main content" link at top

### Performance

1. **Loading States**: Every page has skeleton loaders
2. **Lazy Loading**: Use `@rendermode` appropriately, consider streaming rendering for large data
3. **Caching**: Dashboard data cached for 5 minutes, template metadata cached in memory
4. **Bundle Size**: Minimal CSS, no unnecessary JavaScript
5. **Image Optimization**: SVG icons (no raster images), no external icon libraries

### Cross-Browser

1. Test layout in: Chrome, Firefox, Safari, Edge
2. Ensure CSS custom properties have fallbacks for older browsers
3. Verify print stylesheets in all browsers

### CSS Requirements
- Complete responsive stylesheet with all breakpoints
- Focus-visible indicators (2px solid CBN green outline)
- Skip-link styling (off-screen, visible on focus)
- Print stylesheet covering all pages
- Reduced motion media query for all animations
- High contrast mode adjustments

### Verification
- All pages render correctly at all breakpoints
- Tab through entire app with keyboard only
- Screen reader announces all content correctly
- All animations respect prefers-reduced-motion
- Print preview looks good for all printable pages

---

## Prompt 20: Portal Design System CSS — Premium Institutional Fintech Experience

### Objective
Create the complete, production-grade CSS design system for the FI Portal (`portal.css`). This is the master styling file that defines every visual element across all pages. The design must convey institutional trust, regulatory compliance, and modern fintech sophistication — appropriate for Central Bank of Nigeria financial institution users.

### Design Philosophy
- **Trust**: Clean, professional, conservative — this is a regulatory portal, not a consumer app
- **Clarity**: Data-dense pages must remain scannable and navigable
- **Consistency**: Every component follows the same visual language
- **Performance**: CSS-only interactions where possible, minimal JavaScript

### CSS Architecture (sections in `portal.css`)

1. **Section 1: Design Tokens** — All CSS custom properties
2. **Section 2: Base & Reset** — Box-sizing, typography, links, scrollbars
3. **Section 3: Layout Shell** — App shell, sidebar, topbar, content area
4. **Section 4: Navigation** — Nav groups, items, active states, badges
5. **Section 5: Authentication** — Login page, forms, backgrounds
6. **Section 6: Dashboard** — Stat cards, compliance score, deadline cards
7. **Section 7: Data Tables** — Full table system with sorting, status badges, pagination
8. **Section 8: Forms** — Input fields, selects, checkboxes, radio buttons, file upload
9. **Section 9: Submission Wizard** — Step indicator, progress phases, drag-drop zone
10. **Section 10: Validation Results** — Error cards, severity indicators, remediation boxes
11. **Section 11: Calendar** — Calendar grid, list view, timeline, deadline indicators
12. **Section 12: Template Browser** — Template cards, schema display, XML highlighting
13. **Section 13: Modals & Dialogs** — Modal system, confirmation dialogs
14. **Section 14: Toasts & Notifications** — Toast stack, notification dropdown, bell badge
15. **Section 15: Accordions & Tabs** — Collapsible panels, tab navigation
16. **Section 16: Buttons & Actions** — Button variants, icon buttons, button groups
17. **Section 17: Badges & Chips** — Status badges, category chips, count badges
18. **Section 18: Charts & Visualizations** — Ring charts, bar charts, progress bars
19. **Section 19: Help Center** — Documentation layout, code blocks, FAQ
20. **Section 20: Print Stylesheets** — All @media print rules, CBN letterhead
21. **Section 21: Responsive Breakpoints** — All @media queries
22. **Section 22: Accessibility** — Focus indicators, skip links, reduced motion, high contrast
23. **Section 23: Animations & Keyframes** — All @keyframes definitions
24. **Section 24: Utility Classes** — Spacing, text alignment, display, visibility helpers

### CBN Brand Application
- Primary color: Deep Green (#006B3F) for headers, buttons, active states, accents
- Gold (#C8A415) for highlights, premium features, success accents
- Red (#DC2626) for errors, overdue, critical status
- Blue (#0284C7) for information, links
- Background: White (#FFFFFF) with Gray (#F3F4F6) secondary backgrounds
- Typography: Plus Jakarta Sans for headings, Inter for body text

### Key Visual Elements
- Sidebar: CBN green background with white text, subtle green-on-green pattern
- Cards: White with `shadow-level-1`, 8px border radius, subtle hover elevation
- Tables: Clean with alternating row backgrounds, hover highlight
- Buttons: Filled (green), outline, ghost variants with 6px radius
- Status badges: Pill-shaped with icon + text, color-coded
- Form inputs: Subtle gray border, green focus ring, red error state
- Modals: Backdrop blur, gold accent line, slide-in animation

### Pattern Rules
- ALL class names use `portal-` prefix to avoid collision if Admin and Portal CSS ever coexist
- Follow BEM-like naming: `portal-card`, `portal-card-header`, `portal-card-body`
- Use CSS custom properties for ALL colors, spacing, and sizing — no hardcoded values
- Every interactive element has `:hover`, `:focus-visible`, and `:active` states
- Every component has a loading/skeleton state variant

### Verification
- Every page in the portal is fully styled
- No unstyled elements or browser defaults visible
- Consistent visual language across all pages
- Responsive at all breakpoints
- Print stylesheets produce professional documents
- All animations respect prefers-reduced-motion
- Total CSS file is well-organized with clear section comments

---

## Execution Order & Dependencies

```
Phase 1: Foundation (Prompts 1-3)
├── Prompt 1: Project Scaffolding       ← Start here
├── Prompt 2: Domain Extensions          ← Depends on 1
└── Prompt 3: Authentication Pages       ← Depends on 1, 2

Phase 2: Core Features (Prompts 4-6)
├── Prompt 4: Dashboard                  ← Depends on 3
├── Prompt 5: Submission Upload          ← Depends on 3
└── Prompt 6: Submission History         ← Depends on 5

Phase 3: Extended Features (Prompts 7-10)
├── Prompt 7: Reporting Calendar         ← Depends on 4
├── Prompt 8: Template Browser           ← Depends on 3
├── Prompt 9: Data Entry Form            ← Depends on 5
└── Prompt 10: Validation Deep-Dive      ← Depends on 6

Phase 4: Workflows & Management (Prompts 11-13)
├── Prompt 11: Maker-Checker             ← Depends on 5, 2
├── Prompt 12: Institution Management    ← Depends on 2, 3
└── Prompt 13: Notification System       ← Depends on 4

Phase 5: Advanced Features (Prompts 14-18)
├── Prompt 14: Help Center               ← Depends on 3
├── Prompt 15: Bulk Submission           ← Depends on 5
├── Prompt 16: Reports & Export          ← Depends on 6
├── Prompt 17: Cross-Sheet Dashboard     ← Depends on 4
└── Prompt 18: Real-Time Validation      ← Depends on 5, 9

Phase 6: Polish (Prompts 19-20)
├── Prompt 19: Responsive & A11y         ← After all features
└── Prompt 20: CSS Design System         ← Can run alongside any phase, finalized last
```

---

## Notes for AI Agents

1. **Shared Infrastructure**: The Portal reuses Domain, Application, and Infrastructure layers. Do NOT duplicate services, repositories, or entities that already exist.
2. **New Portal-Only Code**: Only create new code in `FC.Engine.Portal/` and extend shared layers (Domain entities, Application services) when the FI Portal needs new capabilities.
3. **Build Verification**: Every prompt must end with `dotnet build` passing with 0 errors and all existing tests passing.
4. **CSS Consistency**: Use the CBN design tokens for all styling. No inline styles, no external CSS libraries.
5. **Security**: All pages require authentication. Institution users can ONLY see their own institution's data. Always filter by `InstitutionId` from claims.
6. **Accessibility**: Every component must have proper ARIA attributes, keyboard support, and screen reader compatibility.
7. **No JavaScript Libraries**: Use CSS-only solutions for interactions. Minimal custom JS only for file download triggers and clipboard operations.
