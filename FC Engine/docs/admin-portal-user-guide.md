# RegOS™ Admin Portal — User Guide

**Central Bank of Nigeria (CBN) — DFIS Financial Returns Collection & Analysis System**

---

## Table of Contents

1. [Getting Started](#1-getting-started)
2. [Dashboard Overview](#2-dashboard-overview)
3. [Managing Templates](#3-managing-templates)
4. [Managing Fields](#4-managing-fields)
5. [Template Versioning & Publishing](#5-template-versioning--publishing)
6. [Intra-Sheet Formulas](#6-intra-sheet-formulas)
7. [Cross-Sheet Validation Rules](#7-cross-sheet-validation-rules)
8. [Business Rules](#8-business-rules)
9. [Viewing Submissions](#9-viewing-submissions)
10. [Impact Analysis](#10-impact-analysis)
11. [Audit Log](#11-audit-log)
12. [User Management](#12-user-management)
13. [User Roles & Permissions](#13-user-roles--permissions)
14. [Common Workflows](#14-common-workflows)
15. [Glossary](#15-glossary)

---

## 1. Getting Started

### 1.1 Accessing the Portal

Open your web browser and navigate to the Admin Portal URL (e.g., `http://localhost:5001` for local development).

### 1.2 Logging In

1. On the login page, enter your **Username** and **Password**.
2. Use the eye icon next to the password field to toggle password visibility if needed.
3. Click **Sign In**.
4. If your credentials are invalid, an error message will appear below the form.

> **Default admin credentials** (first-time setup): Username `admin`, Password `Admin@123`.
> Change this immediately after first login.

### 1.3 Portal Layout

After logging in, you will see:

- **Sidebar (left)** — Navigation menu with links to all portal sections. Your name and role are displayed at the bottom.
- **Main content area (right)** — The active page content.
- **Sign Out** — Located at the bottom of the sidebar.

### 1.4 Navigation Menu

| Menu Item | Description | Access |
|-----------|-------------|--------|
| Dashboard | System overview and statistics | All users |
| Templates | Create and manage return templates | All users |
| Formulas | Intra-sheet validation formulas | All users |
| Cross-Sheet Rules | Multi-template validation rules | All users |
| Business Rules | Expression-based validation rules | All users |
| Submissions | View submitted returns and results | All users |
| Impact Analysis | Analyze effects of template changes | Admin, Approver |
| Audit Log | Track all system changes | Admin, Approver |
| Users | Manage portal user accounts | Admin only |

---

## 2. Dashboard Overview

The Dashboard is the first page you see after logging in. It provides a summary of your RegOS™ system.

### 2.1 Statistics Cards

Four cards at the top show:

| Card | What It Shows |
|------|---------------|
| **Published Templates** | Total number of templates currently in Published status |
| **Total Fields** | Sum of all fields across all published templates |
| **Intra-Sheet Formulas** | Total number of validation formulas defined |
| **Template Categories** | Breakdown by FixedRow / MultiRow / ItemCoded |

### 2.2 Templates by Frequency

A table showing how many templates exist per reporting frequency:

- **Monthly** — Templates with return codes starting with `MFCR`
- **Quarterly** — Templates with return codes starting with `QFCR`
- **Semi-Annual** — Templates with return codes starting with `SFCR`
- **Computed** — Templates with return codes starting with `CFCR`

### 2.3 Templates by Category

A table showing template counts and total fields grouped by structural category:

- **FixedRow** — Single-row returns (one set of values per submission)
- **MultiRow** — Multiple rows identified by serial number
- **ItemCoded** — Multiple rows identified by predefined item codes

---

## 3. Managing Templates

Navigate to **Templates** from the sidebar.

### 3.1 Browsing Templates

The Templates page displays all published templates in a searchable, filterable table.

**Search and Filter Options:**

- **Search bar** — Type a return code or template name to filter results (e.g., `MFCR 300` or `Balance Sheet`)
- **Frequency filter** — Select a frequency to show only Monthly, Quarterly, Semi-Annual, or Computed templates
- **Category filter** — Select a category to show only FixedRow, MultiRow, or ItemCoded templates

**Template List Columns:**

| Column | Description |
|--------|-------------|
| Return Code | The unique CBN return code (e.g., `MFCR 300`) |
| Name | Descriptive name of the return |
| Category | Structural category badge |
| Fields | Number of data fields in the template |
| Formulas | Number of validation formulas |
| Table | Physical database table name |
| Action | Click **View** to open the template detail page |

### 3.2 Creating a New Template

> **Required role:** Admin or Approver

1. Click the **New Template** button at the top-right of the Templates page.
2. Fill in the template creation form:

| Field | Description | Example |
|-------|-------------|---------|
| **Return Code** | Unique CBN return code | `MFCR 999` |
| **Name** | Descriptive template name | `Miscellaneous Returns` |
| **Frequency** | Reporting frequency | Monthly, Quarterly, Semi-Annual, Annual, Computed |
| **Structural Category** | How data rows are organized | FixedRow, MultiRow, ItemCoded |
| **Description** | Optional description | `Monthly report for miscellaneous items` |

3. Click **Create**.
4. You will be redirected to the new template's detail page.

> **Tip:** The return code format should follow CBN conventions:
> - `MFCR` for Monthly returns
> - `QFCR` for Quarterly returns
> - `SFCR` for Semi-Annual returns
> - `CFCR` for Computed returns

### 3.3 Viewing Template Details

Click on any template's **Return Code** or **View** button to open its detail page.

The detail page shows:

- **Header** — Return code and template name
- **Stats cards** — Field count, formula count, category, and current version with status
- **Version management** — Version selector and action buttons
- **Template info** — Physical table name, XML root element, XML namespace, XSD schema
- **Fields table** — All fields defined in the selected version
- **Formulas** — Link to the Formulas page for managing formulas
- **Item Codes** — (ItemCoded templates only) List of predefined item codes

---

## 4. Managing Fields

Fields are managed from the Template Detail page. Fields can only be added to templates that have a **Draft** version.

### 4.1 Adding a Field

> **Required role:** Admin or Approver
> **Prerequisite:** Template must have a Draft version (see [Section 5](#5-template-versioning--publishing))

1. Navigate to the template's detail page.
2. Ensure you are viewing a **Draft** version (the version badge should show "Draft").
3. Click the **Add Field** button in the Fields section.
4. Fill in the field form:

| Field | Description | Example |
|-------|-------------|---------|
| **Field Name** | Database column name (lowercase, underscores) | `total_assets` |
| **Display Name** | Human-readable label | `Total Assets` |
| **XML Element Name** | Element name in XML submissions | `TotalAssets` |
| **Line Code** | CBN line code reference | `1.00` |
| **Data Type** | Type of data the field holds | Money, Integer, Text, Percentage, Decimal, Date, Boolean |
| **Field Order** | Display/processing order (number) | `1` |
| **Section Name** | Optional grouping section | `Assets` |
| **Required** | Whether the field is mandatory | Yes / No |

5. Click **Add** to save the field.
6. The field will appear in the fields table below.

### 4.2 Understanding Data Types

| Data Type | Description | SQL Type Generated |
|-----------|-------------|-------------------|
| **Money** | Currency values (2 decimal places) | DECIMAL(20,2) |
| **Decimal** | General decimal values (4 decimal places) | DECIMAL(20,4) |
| **Percentage** | Percentage values (4 decimal places) | DECIMAL(10,4) |
| **Integer** | Whole numbers | INT |
| **Text** | Text/string values | NVARCHAR(255) |
| **Date** | Date values | DATE |
| **Boolean** | True/false values | BIT |

### 4.3 Understanding the Fields Table

| Column | Description |
|--------|-------------|
| **#** | Display order of the field |
| **Field Name** | Database column name |
| **Display Name** | Human-readable label |
| **Line Code** | CBN line reference code |
| **Data Type** | The data type of the field |
| **SQL Type** | Actual SQL Server column type |
| **Required** | Whether the field must have a value |
| **Flags** | Special indicators: "Key" (key field), "Computed" (calculated field) |

---

## 5. Template Versioning & Publishing

Templates use a version lifecycle to ensure changes are reviewed before going live. Every template starts with a Draft version, goes through Review, and finally gets Published.

### 5.1 Version Lifecycle

```
Draft  →  Review  →  Published
  ↑                      |
  └──── New Draft ←──────┘
```

| Status | Meaning |
|--------|---------|
| **Draft** | Work in progress. Fields can be added or modified. |
| **Review** | Submitted for approval. No further edits allowed until published or reverted. |
| **Published** | Live and active. Physical database table is created/updated. XML submissions are accepted. |

### 5.2 Working with Versions

**Selecting a Version:**
- On the template detail page, use the version buttons to switch between versions.
- Each button shows the version number and its status badge.

**Creating a New Draft (from Published):**

> **Required role:** Admin or Approver

1. View the template detail page with the Published version selected.
2. Click **New Draft**.
3. A new version is created as a copy of the current Published version.
4. You can now add new fields to this Draft version.

**Submitting for Review:**

> **Required role:** Admin or Approver

1. View the template detail page with a Draft version selected.
2. Click **Submit for Review**.
3. The version status changes to "Review".

**Previewing DDL (SQL Changes):**

> **Required role:** Admin

1. View the template detail page with a version in Review status.
2. Click **Preview DDL**.
3. The SQL statements that will be executed are displayed below the button.
4. Review the SQL carefully — this shows exactly what database changes will be made.

**Publishing a Version:**

> **Required role:** Admin

1. View the template detail page with a version in Review status.
2. (Optional) Click **Preview DDL** to review the changes first.
3. Click **Publish**.
4. The system will:
   - Generate the necessary SQL (CREATE TABLE or ALTER TABLE)
   - Execute the SQL against the database
   - Update the template status to Published
   - Invalidate the metadata cache so the new version takes effect immediately
5. A success message confirms the publication.

> **Important:** Publishing is a significant action. For new templates, it creates a physical database table. For updates to existing templates, it adds new columns. Columns are never dropped to preserve existing data.

### 5.3 Viewing the XSD Schema

1. On the template detail page, click the **XSD Schema** button.
2. The generated XML Schema Definition (XSD) is displayed.
3. This XSD is automatically generated from the template's field definitions.
4. Institutions use this schema to validate their XML submissions before uploading.

---

## 6. Intra-Sheet Formulas

Navigate to **Formulas** from the sidebar.

Intra-sheet formulas define validation rules within a single template. For example: *"Total Cash must equal Notes plus Coins"*.

### 6.1 Browsing Formulas

The Formulas page shows all validation formulas across all templates.

**Filtering Options:**

- **Search** — Filter by rule code, field name, or description
- **Formula Type** — Filter by type (Sum, Difference, Custom, Equals, etc.)
- **Template** — Filter to show formulas for a specific template only

**Formula List Columns:**

| Column | Description |
|--------|-------------|
| Template | The return template this formula belongs to |
| Rule Code | Unique identifier for the formula (e.g., `MFCR300-SUM-001`) |
| Name | Description of what the formula validates |
| Type | Formula type (Sum, Difference, Custom, etc.) |
| Target | The field being validated and its line code |
| Formula/Operands | The fields or expression used in the calculation |
| Tolerance | Acceptable margin of error |
| Severity | Error, Warning, or Info |
| Actions | Edit and Delete buttons |

### 6.2 Adding a Formula

> **Required role:** Admin or Approver

1. Click the **Add Formula** button.
2. Fill in the formula form:

**Basic Information:**

| Field | Description | Example |
|-------|-------------|---------|
| **Template** | Select the template this formula applies to | `MFCR 300` |
| **Rule Code** | Unique identifier | `MFCR300-SUM-001` |
| **Rule Name / Description** | Human-readable explanation | `TOTAL CASH = Notes + Coins` |
| **Formula Type** | Type of validation | Sum, Difference, Custom, Equals |

**Target Definition:**

| Field | Description | Example |
|-------|-------------|---------|
| **Target Field** | The field being validated (select from dropdown) | `total_cash` |
| **Target Line Code** | Optional CBN line code | `10140` |

**Operands:**

| Field | Description | Example |
|-------|-------------|---------|
| **Operand Fields** | Comma-separated list of fields used in the formula | `cash_notes,cash_coins` |

**Custom Expression (only for Custom type):**

| Field | Description | Example |
|-------|-------------|---------|
| **Custom Expression** | Mathematical expression using field names or line codes | `10420+10430+10440-10510` |

**Validation Settings:**

| Field | Description | Example |
|-------|-------------|---------|
| **Tolerance** | Acceptable margin of error (decimal) | `0.01` |
| **Severity** | Impact level if validation fails | Error, Warning, Info |

3. Click **Add** to save the formula.

> **Tip:** When you select a template, the available fields are shown below the form for reference. Use these field names in the operand fields.

### 6.3 Understanding Formula Types

| Type | Description | Example |
|------|-------------|---------|
| **Sum** | Target field must equal the sum of operand fields | `total = field_a + field_b + field_c` |
| **Difference** | Target field must equal the first operand minus the rest | `net = gross - deductions` |
| **Equals** | Target field must equal a specific operand field | `field_a = field_b` |
| **Custom** | Free-form arithmetic expression | `10420 + 10430 - 10510` |

### 6.4 Editing a Formula

> **Required role:** Admin or Approver

1. In the formula list, click the **Edit** button on the formula you want to modify.
2. The form opens pre-filled with the formula's current values.
3. Make your changes.
4. Click **Update** to save, or **Cancel** to discard.

### 6.5 Deleting a Formula

> **Required role:** Admin or Approver

1. In the formula list, click the **Delete** button on the formula you want to remove.
2. The formula is immediately removed.

> **Caution:** Deletion is immediate. Ensure the formula is no longer needed before deleting.

---

## 7. Cross-Sheet Validation Rules

Navigate to **Cross-Sheet Rules** from the sidebar.

Cross-sheet rules validate relationships **between different return templates**. For example: *"Total Assets in MFCR 300 must equal Total Assets in MFCR 301"*.

### 7.1 Browsing Cross-Sheet Rules

The page displays:

- **Total Rules** — Count of all cross-sheet rules
- **Active Rules** — Count of currently active rules

**Rule List Columns:**

| Column | Description |
|--------|-------------|
| Rule Code | Unique identifier (e.g., `XS-001`) |
| Name | Descriptive name of the rule |
| Description | Detailed explanation |
| Operands | Template-field pairs with their aliases |
| Expression | Mathematical expression using aliases |
| Severity | Error, Warning, or Info |
| Active | Whether the rule is currently enforced |

### 7.2 Creating a Cross-Sheet Rule

> **Required role:** Admin

1. Click the **Add Rule** button.
2. Fill in the form:

**Basic Information:**

| Field | Description | Example |
|-------|-------------|---------|
| **Rule Code** | Unique identifier | `XS-100` |
| **Rule Name** | Descriptive name | `Loan Reconciliation` |
| **Description** | Detailed explanation of the rule | `Total loans in MFCR 300 must match MFCR 318 total` |

**Operands:**

Operands define which template fields are involved in the rule. Each operand has:

| Field | Description | Example |
|-------|-------------|---------|
| **Alias** | Letter reference used in the expression | `A` |
| **Template Return Code** | The template this operand refers to | `MFCR 300` |
| **Field Name** | The specific field in that template | `total_loans` |
| **Aggregate Function** | How to aggregate multi-row values | None, SUM, COUNT, MAX, MIN, AVG |

- Click **+ Add Operand** to add more operands (aliases auto-increment: A, B, C, ...).
- You need at least 2 operands to define a cross-sheet relationship.

**Expression:**

| Field | Description | Example |
|-------|-------------|---------|
| **Expression** | Mathematical expression using aliases | `A = B + C` |
| **Tolerance** | Acceptable margin of error | `0.01` |
| **Severity** | Impact level if validation fails | Error, Warning, Info |
| **Error Message** | Message shown when rule fails | `Balance sheet does not balance` |

3. Click **Create Rule** to save.

### 7.3 Example: Creating a Balance Sheet Rule

Scenario: *Total Assets in MFCR 300 must equal Total Liabilities in MFCR 300*

1. **Rule Code:** `XS-001`
2. **Rule Name:** `Balance Sheet Equation`
3. **Operand A:** Template = `MFCR 300`, Field = `total_assets`, Aggregate = None
4. **Operand B:** Template = `MFCR 300`, Field = `total_liabilities`, Aggregate = None
5. **Expression:** `A = B`
6. **Tolerance:** `0.01`
7. **Severity:** Error
8. **Error Message:** `Total Assets must equal Total Liabilities`

---

## 8. Business Rules

Navigate to **Business Rules** from the sidebar.

Business rules are expression-based validations that can apply across multiple templates. They are used for checks like "Total Assets must be greater than zero" or "Reporting date must be valid".

### 8.1 Browsing Business Rules

**Rule List Columns:**

| Column | Description |
|--------|-------------|
| Rule Code | Unique identifier (e.g., `BR-001`) |
| Name | Descriptive name |
| Type | Rule category (Completeness, DateCheck, ThresholdCheck, Custom) |
| Expression | Validation expression |
| Applies To | Templates this rule applies to |
| Severity | Error, Warning, or Info |
| Active | Whether the rule is enforced |

### 8.2 Creating a Business Rule

> **Required role:** Admin or Approver

1. Click the **Add Rule** button.
2. Fill in the form:

| Field | Description | Example |
|-------|-------------|---------|
| **Rule Code** | Unique identifier | `BR-001` |
| **Rule Name** | Descriptive name | `Positive Assets Check` |
| **Rule Type** | Category of rule | Completeness, DateCheck, ThresholdCheck, Custom |
| **Severity** | Impact level | Error, Warning, Info |
| **Expression** | Validation expression (optional) | `total_assets > 0` |
| **Applies To Templates** | Comma-separated return codes (optional) | `MFCR 300,MFCR 301` |

3. Click **Create** to save.

### 8.3 Understanding Rule Types

| Type | Description | Use Case |
|------|-------------|----------|
| **Completeness** | Ensures required fields are filled | All mandatory fields have values |
| **DateCheck** | Validates date fields | Reporting date is within the expected period |
| **ThresholdCheck** | Checks values against thresholds | Total assets exceeds minimum |
| **Custom** | Free-form expression-based rules | Any custom validation logic |

---

## 9. Viewing Submissions

Navigate to **Submissions** from the sidebar.

The Submissions page lets you view returns submitted by financial institutions and their validation results.

### 9.1 Searching for Submissions

1. Enter the **Institution ID** in the search field.
2. Click **Search**.
3. Submissions for that institution are displayed in the table.

### 9.2 Submissions List

| Column | Description |
|--------|-------------|
| ID | Unique submission identifier (clickable link) |
| Return Code | Template the submission is for (clickable link to template) |
| Institution | Name of the submitting institution |
| Period | Reporting period date |
| Status | Current status: Accepted, Rejected, Processing, etc. |
| Submitted | Date and time of submission |
| Errors | Number of validation errors |

**Status Meanings:**

| Status | Meaning | Badge Color |
|--------|---------|-------------|
| **Accepted** | Passed all validations | Green |
| **AcceptedWithWarnings** | Passed with non-critical issues | Yellow |
| **Rejected** | Failed one or more validation rules | Red |
| **Processing** | Currently being validated | Gray |
| **Draft** | Initial state before processing | Gray |

### 9.3 Viewing Submission Details

1. Click on a **Submission ID** in the list.
2. The detail page shows:

**Stats Cards:**
- Return Code (linked to template)
- Status
- Validation Issues count
- Submission timestamp

**Submission Info:**
- Institution ID
- Return Period ID
- Template Version ID
- Processing Time (in milliseconds)

**Validation Report:**
- Overall status (Valid / Invalid)
- Error count and Warning count

**Validation Errors Table** (if any errors exist):

| Column | Description |
|--------|-------------|
| Rule ID | The validation rule that failed |
| Category | Type of validation (IntraSheet, CrossSheet, XSD, etc.) |
| Severity | Error or Warning |
| Field | Which field the error relates to |
| Message | Description of the validation failure |
| Expected | What the value should be |
| Actual | What the value actually was |

---

## 10. Impact Analysis

Navigate to **Impact Analysis** from the sidebar.

> **Required role:** Admin or Approver

Impact Analysis helps you understand the effects of changing a template before you publish changes.

### 10.1 Running an Analysis

1. Select a template from the **dropdown** at the top of the page.
2. The analysis runs automatically when you select a template.

### 10.2 Understanding the Results

**Stats Cards:**

| Card | Description |
|------|-------------|
| **Fields** | Number of fields in the current version |
| **Intra-Sheet Formulas** | Number of validation formulas for this template |
| **Cross-Sheet Rules** | Number of cross-sheet rules involving this template |
| **Dependent Templates** | Number of other templates affected by changes to this one |

**Cross-Sheet Rules Section:**
Shows all cross-sheet validation rules that reference this template:
- Rule Code
- Rule Name
- Related Templates (other templates in the same rule)
- Expression

**Dependent Templates Section:**
Shows all templates that would be affected if this template changes:
- Return Code (linked to the template)
- Via Rule (which cross-sheet rule creates the dependency)

> **Use Case:** Before modifying MFCR 300, run Impact Analysis to see which cross-sheet rules and dependent templates might be affected. This helps you plan and communicate changes to stakeholders.

---

## 11. Audit Log

Navigate to **Audit Log** from the sidebar.

> **Required role:** Admin or Approver

The Audit Log records every change made to the metadata in the system, providing a complete audit trail.

### 11.1 Viewing the Audit Log

The log displays the most recent 100 entries (from the last 500 stored).

**Audit Log Columns:**

| Column | Description |
|--------|-------------|
| Timestamp | When the change was made |
| Action | Type of change: Created, Updated, or Deleted |
| Entity | What type of object was changed (Template, Field, Formula, etc.) |
| Entity ID | The ID of the changed object |
| User | Who made the change |
| Changes | Summary of the new values (first 100 characters) |

### 11.2 Filtering the Audit Log

- **Entity filter** — Type an entity name to show only changes to that type (e.g., `Template`, `Formula`)
- **User filter** — Type a username to show only changes made by that user
- Click **Refresh** to reload the latest entries

---

## 12. User Management

Navigate to **Users** from the sidebar.

> **Required role:** Admin only

### 12.1 Viewing Users

The Users page displays all portal user accounts:

| Column | Description |
|--------|-------------|
| Username | Login username |
| Display Name | Full name |
| Email | Email address |
| Role | User role (Admin, Approver, Viewer) |
| Status | Active or Inactive |
| Last Login | Most recent login timestamp |
| Created | Account creation date |

### 12.2 Creating a New User

1. Click the **Add User** button.
2. Fill in the form:

| Field | Description | Example |
|-------|-------------|---------|
| **Username** | Login username (unique) | `john.doe` |
| **Display Name** | Full name | `John Doe` |
| **Email** | Email address | `john.doe@cbn.gov.ng` |
| **Password** | Initial password | (secure password) |
| **Role** | User role | Admin, Approver, Viewer |

3. Click **Create**.
4. The new user will appear in the users table.

> **Important:** Share the initial password securely with the new user and advise them to change it after first login.

---

## 13. User Roles & Permissions

The portal has three roles with increasing levels of access:

### Permission Matrix

| Action | Viewer | Approver | Admin |
|--------|--------|----------|-------|
| View Dashboard | Yes | Yes | Yes |
| Browse Templates | Yes | Yes | Yes |
| View Template Details | Yes | Yes | Yes |
| Create Template | No | Yes | Yes |
| Add Fields to Draft | No | Yes | Yes |
| Create New Draft Version | No | Yes | Yes |
| Submit for Review | No | Yes | Yes |
| Preview DDL | No | No | Yes |
| Publish Version | No | No | Yes |
| View Formulas | Yes | Yes | Yes |
| Add/Edit/Delete Formulas | No | Yes | Yes |
| View Cross-Sheet Rules | Yes | Yes | Yes |
| Create Cross-Sheet Rules | No | No | Yes |
| View Business Rules | Yes | Yes | Yes |
| Add Business Rules | No | Yes | Yes |
| View Submissions | Yes | Yes | Yes |
| View Submission Details | Yes | Yes | Yes |
| Impact Analysis | No | Yes | Yes |
| Audit Log | No | Yes | Yes |
| User Management | No | No | Yes |

### Role Descriptions

- **Viewer** — Read-only access. Can view all templates, formulas, rules, and submissions but cannot make any changes. Ideal for reporting staff and stakeholders.

- **Approver** — Can create and modify templates, add fields, create formulas and business rules, and submit versions for review. Cannot publish templates or manage users. Ideal for senior analysts who design templates.

- **Admin** — Full access to all features, including publishing templates (which creates/modifies database tables), managing cross-sheet rules, viewing audit logs, and managing user accounts. Ideal for system administrators and team leads.

---

## 14. Common Workflows

### 14.1 Creating a Brand New Return Template (End to End)

This workflow walks through creating a completely new return template from scratch.

**Step 1: Create the Template**
1. Go to **Templates**.
2. Click **New Template**.
3. Enter: Return Code = `MFCR 999`, Name = `Test Return`, Frequency = Monthly, Category = FixedRow.
4. Click **Create**.

**Step 2: Add Fields**
1. On the template detail page, you are in Draft version.
2. Click **Add Field**.
3. Add your first field: Field Name = `total_assets`, Display Name = `Total Assets`, XML Element = `TotalAssets`, Line Code = `1.00`, Data Type = Money, Order = 1, Required = Yes.
4. Click **Add**.
5. Repeat for all required fields.

**Step 3: Add Validation Formulas**
1. Go to **Formulas**.
2. Click **Add Formula**.
3. Select Template = `MFCR 999`.
4. Fill in: Rule Code = `MFCR999-SUM-001`, Name = `Total Check`, Type = Sum, Target = `total_assets`, Operands = `field_a,field_b,field_c`.
5. Click **Add**.

**Step 4: Submit for Review**
1. Go back to **Templates** and open `MFCR 999`.
2. Click **Submit for Review**.

**Step 5: Review and Publish (Admin)**
1. Still on the template detail page (or ask an Admin to navigate there).
2. Click **Preview DDL** to review the SQL that will be executed.
3. Verify the CREATE TABLE statement looks correct.
4. Click **Publish**.
5. The physical database table is created and the template is now live.

**Step 6: Verify**
1. Check the Dashboard — the template count should increase by one.
2. Institutions can now submit XML data for `MFCR 999`.

---

### 14.2 Adding a New Field to an Existing Template

**Step 1: Create a New Draft**
1. Go to **Templates** and open the template (e.g., `MFCR 300`).
2. Ensure the Published version is selected.
3. Click **New Draft** — a new Draft version is created with all existing fields.

**Step 2: Add the Field**
1. Switch to the new Draft version (it should be selected automatically).
2. Click **Add Field**.
3. Fill in the new field details and click **Add**.

**Step 3: Review and Publish**
1. Click **Submit for Review**.
2. (Admin) Click **Preview DDL** — you should see an `ALTER TABLE ... ADD COLUMN` statement.
3. (Admin) Click **Publish**.
4. The physical table is altered to include the new column. Existing data is preserved (the new column will be NULL for old records).

---

### 14.3 Setting Up Cross-Sheet Validation

**Scenario:** Ensure that Total Loans in MFCR 300 matches the sum of Individual Loans in MFCR 318.

**Step 1: Define the Rule**
1. Go to **Cross-Sheet Rules**.
2. Click **Add Rule**.
3. Fill in:
   - Rule Code = `XS-050`
   - Rule Name = `Loan Portfolio Reconciliation`
   - Description = `Total loans in balance sheet must match loan schedule total`

**Step 2: Add Operands**
4. Operand A: Template = `MFCR 300`, Field = `total_loans`, Aggregate = None
5. Click **+ Add Operand**
6. Operand B: Template = `MFCR 318`, Field = `loan_amount`, Aggregate = SUM

**Step 3: Define Expression**
7. Expression = `A = B`
8. Tolerance = `0.01`
9. Severity = Error
10. Error Message = `Total loans in MFCR 300 must equal sum of loans in MFCR 318`

**Step 4: Save**
11. Click **Create Rule**.

---

### 14.4 Investigating a Rejected Submission

**Step 1: Find the Submission**
1. Go to **Submissions**.
2. Enter the Institution ID and click **Search**.
3. Find the submission with "Rejected" status.

**Step 2: Review Errors**
4. Click the **Submission ID** to open the detail page.
5. Review the Validation Errors table:
   - Check the **Rule ID** to identify which rule failed.
   - Read the **Message** for a description of the issue.
   - Compare **Expected** vs **Actual** values.

**Step 3: Communicate**
6. Share the specific error details with the institution so they can correct their data and resubmit.

---

### 14.5 Assessing Impact Before Making Changes

**Before** modifying any template, always check the impact:

1. Go to **Impact Analysis**.
2. Select the template you plan to modify.
3. Review:
   - How many cross-sheet rules reference this template?
   - Which other templates are dependent?
4. If dependencies exist, plan to communicate changes to affected teams.
5. After publishing, verify that cross-sheet rules still pass for existing submissions.

---

## 15. Glossary

| Term | Definition |
|------|-----------|
| **Return Template** | A definition of a regulatory return that institutions must submit (e.g., Balance Sheet, Income Statement) |
| **Return Code** | Unique CBN identifier for a template (e.g., MFCR 300) |
| **FixedRow** | Template with a single row of data per submission |
| **MultiRow** | Template with multiple rows, each identified by a serial number |
| **ItemCoded** | Template with multiple rows, each identified by a predefined item code |
| **Field** | A single data point within a template (e.g., Total Assets) |
| **Line Code** | CBN reference code for a specific data item |
| **Intra-Sheet Formula** | Validation rule within a single template (e.g., column A + B = C) |
| **Cross-Sheet Rule** | Validation rule between two or more templates |
| **Business Rule** | General validation rule applied across the system |
| **Draft** | Initial version status; fields can be added and modified |
| **Review** | Version awaiting approval; no further edits allowed |
| **Published** | Live version; physical database table created/updated |
| **DDL** | Data Definition Language — SQL statements that create or modify database tables |
| **XSD** | XML Schema Definition — validates the structure of XML submissions |
| **Tolerance** | Acceptable margin of error for formula validation (e.g., 0.01 means values within 0.01 are considered equal) |
| **Severity** | Impact level of a validation failure: Error (blocks acceptance), Warning (allows with caution), Info (informational only) |
| **Physical Table** | The actual SQL Server database table where submitted data is stored |
| **Operand** | A template-field reference used in a cross-sheet expression |
| **Expression** | A mathematical formula using aliases (e.g., A = B + C) |
| **Aggregate Function** | A function that summarizes multiple rows: SUM, COUNT, MAX, MIN, AVG |

---

*RegOS™ Admin Portal v1.0 — Central Bank of Nigeria, DFIS Department*
