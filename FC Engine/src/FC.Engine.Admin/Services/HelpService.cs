namespace FC.Engine.Admin.Services;

public record HelpArticle(string Id, string Title, string Body, string? VideoUrl = null);
public record KeyboardShortcutGroup(string Category, List<KeyboardShortcut> Shortcuts);
public record KeyboardShortcut(string Keys, string Description);
public record TourStep(string TargetSelector, string Title, string Content, string Placement = "bottom");
public record ChangelogEntry(string Version, string Date, string Title, List<ChangelogItem> Items);
public record ChangelogItem(string Type, string Text); // Type: "new" | "improved" | "fixed"

/// <summary>
/// Manages the global help system: panel, keyboard shortcut overlay, feature tours, and what's new modal.
/// </summary>
public class HelpService
{
    // ── State ────────────────────────────────────────────────────────────────
    public bool   IsPanelOpen            { get; private set; }
    public bool   IsShortcutOverlayOpen  { get; private set; }
    public bool   IsTourActive           { get; private set; }
    public int    TourStepIndex          { get; private set; }
    public string? ActiveTourId          { get; private set; }
    public bool   IsWhatsNewOpen         { get; private set; }
    public string? HelpPage              { get; private set; }

    public event Action? OnChanged;

    // ── Panel ────────────────────────────────────────────────────────────────
    public void OpenPanel(string? page = null)  { IsPanelOpen = true;  HelpPage = page; Notify(); }
    public void ClosePanel()                    { IsPanelOpen = false; Notify(); }
    public void TogglePanel(string? page = null) { if (IsPanelOpen) ClosePanel(); else OpenPanel(page); }

    // ── Shortcut Overlay ─────────────────────────────────────────────────────
    public void OpenShortcutOverlay()  { IsShortcutOverlayOpen = true;  Notify(); }
    public void CloseShortcutOverlay() { IsShortcutOverlayOpen = false; Notify(); }

    // ── Tour ─────────────────────────────────────────────────────────────────
    public void StartTour(string tourId) { ActiveTourId = tourId; IsTourActive = true; TourStepIndex = 0; Notify(); }

    public void NextTourStep()
    {
        var steps = GetTourSteps(ActiveTourId);
        if (TourStepIndex < steps.Count - 1) TourStepIndex++;
        else EndTour();
        Notify();
    }

    public void PreviousTourStep() { if (TourStepIndex > 0) { TourStepIndex--; Notify(); } }
    public void EndTour()          { IsTourActive = false; ActiveTourId = null; TourStepIndex = 0; Notify(); }

    // ── What's New ───────────────────────────────────────────────────────────
    public void ShowWhatsNew()    { IsWhatsNewOpen = true;  Notify(); }
    public void DismissWhatsNew() { IsWhatsNewOpen = false; Notify(); }

    private void Notify() => OnChanged?.Invoke();

    // ── Articles ─────────────────────────────────────────────────────────────
    public List<HelpArticle> GetArticlesForPage(string path)
    {
        var key = "/" + path.Trim('/').ToLowerInvariant();
        // Match root
        if (key == "/" && _pageArticles.TryGetValue("/", out var root)) return root;
        // Prefix match (e.g. /templates/FORM01 → /templates)
        foreach (var (prefix, articles) in _pageArticles)
        {
            if (prefix != "/" && key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return articles;
        }
        return _generalArticles;
    }

    // ── Shortcuts ────────────────────────────────────────────────────────────
    public List<KeyboardShortcutGroup> GetShortcuts() => _shortcuts;

    // ── Tours ────────────────────────────────────────────────────────────────
    public List<TourStep> GetTourSteps(string? tourId) => tourId switch
    {
        "dashboard"   => _dashboardTour,
        "templates"   => _templatesTour,
        "submissions" => _submissionsTour,
        _             => new()
    };

    public string? GetTourIdForPath(string path)
    {
        var key = "/" + path.Trim('/').ToLowerInvariant();
        if (key == "/") return "dashboard";
        if (key.StartsWith("/templates")) return "templates";
        if (key.StartsWith("/submissions")) return "submissions";
        return null;
    }

    // ── Changelog ────────────────────────────────────────────────────────────
    public string CurrentVersion => "2.1.0";
    public List<ChangelogEntry> GetChangelog() => _changelog;

    // ═════════════════════════════════════════════════════════════════════════
    // DATA
    // ═════════════════════════════════════════════════════════════════════════

    private static readonly List<HelpArticle> _generalArticles = new()
    {
        new("kbd-shortcuts", "Keyboard Shortcuts",
            "Press <kbd>?</kbd> at any time to view all available keyboard shortcuts. Use <kbd>⌘K</kbd> (Mac) or <kbd>Ctrl+K</kbd> (Windows) to open the command palette for fast navigation."),
        new("help-panel", "Getting Contextual Help",
            "Click the <strong>?</strong> button in the top-right corner to open this help panel. Articles shown are relevant to the current page."),
        new("nav-tips", "Navigation Tips",
            "Use the sidebar to move between sections. Collapse it with the arrow button for more screen space. Sidebar items show a tooltip in collapsed mode — hover to see the label."),
    };

    private static readonly Dictionary<string, List<HelpArticle>> _pageArticles = new()
    {
        ["/"] = new()
        {
            new("dash-overview", "Dashboard Overview",
                "The dashboard provides a real-time snapshot of your compliance activity. Hero metrics show totals for templates, submissions, active formulas, and business rules."),
            new("dash-metrics", "Understanding Metrics",
                "Metrics refresh each time you load the dashboard. Use the date range picker to filter activity by time period. Click any card to navigate to that section."),
            new("dash-smart-nav", "Quick Navigation",
                "Press <kbd>⌘K</kbd> to open the command palette and jump to any page instantly. Recent pages also appear in the sidebar below your navigation."),
        },
        ["/templates"] = new()
        {
            new("templates-lifecycle", "Template Lifecycle",
                "Templates follow a strict lifecycle: <strong>Draft → Under Review → Published</strong>. Only Approvers can publish. Published templates lock their field structure to prevent breaking existing submissions."),
            new("templates-versions", "Versioning",
                "Each edit creates a new draft version. Previous published versions are preserved in Version History. You can view diffs between versions and revert if needed."),
            new("templates-import", "Importing Templates",
                "Use the Import button to upload a pre-defined template JSON. The system validates structure, cross-references formulas, and reports any issues before committing."),
        },
        ["/formulas"] = new()
        {
            new("formula-syntax", "Formula Syntax",
                "Formulas use a spreadsheet-like syntax: <code>SUM(A1:A10)</code>, <code>IF(condition, value, else)</code>. Field references use <code>SheetName.FieldCode</code> notation for cross-sheet calculations."),
            new("formula-scope", "Formula Scope",
                "Formulas can be scoped to a single template or shared across all templates. Shared formulas appear in the library and are referenced by code, so changes propagate automatically."),
        },
        ["/submissions"] = new()
        {
            new("sub-pipeline", "Submission Pipeline",
                "Submissions flow through: <strong>Draft → Submitted → Validating → Accepted / Rejected</strong>. Each stage triggers validation rules and sends notifications to relevant users."),
            new("sub-kanban", "Kanban View",
                "Switch to Kanban view to see submissions organised by status. Drag cards between columns to update status instantly. Use List view for sorting, filtering, and bulk operations."),
            new("sub-validation", "Validation Errors",
                "Validation runs automatically on submission. Errors must be resolved before Acceptance. Warnings are advisory — a submission can be accepted with warnings at Approver discretion."),
        },
        ["/audit"] = new()
        {
            new("audit-overview", "Audit Log Overview",
                "Every data modification, login event, and status change is recorded here with a timestamp and the acting user. Logs are immutable and cannot be edited or deleted."),
            new("audit-export", "Exporting Audit Data",
                "Use the Export button to download audit logs as CSV or Excel. For large date ranges exports are processed in the background and a download link is emailed to you."),
        },
        ["/users"] = new()
        {
            new("users-roles", "User Roles",
                "<strong>Admin</strong> — full access to all features. <strong>Approver</strong> — can review and publish templates. <strong>Submitter</strong> — can create and submit returns. <strong>Viewer</strong> — read-only access."),
            new("users-invite", "Inviting Users",
                "Click <em>Invite User</em> to send an email invitation. The user receives a link to set their password. You can set their role and restrict access to specific modules at invite time."),
        },
        ["/platform"] = new()
        {
            new("platform-overview", "Platform Administration",
                "The Platform section is only visible to Platform Admins. Here you manage tenants, billing, feature flags, and system health across all organisations on the platform."),
            new("platform-impersonation", "Tenant Impersonation",
                "Use the Impersonate button on a Tenant Detail page to view and act as that tenant. A yellow banner appears while impersonating. All actions are logged with your identity."),
        },
        ["/business-rules"] = new()
        {
            new("rules-overview", "Business Rules",
                "Business rules enforce data quality constraints beyond formula-level validation. Rules can compare values across sheets, apply conditional logic, and flag threshold breaches."),
        },
        ["/cross-sheet-rules"] = new()
        {
            new("cross-sheet-overview", "Cross-Sheet Rules",
                "Cross-sheet rules validate relationships between cells in different sheets within the same template. Common uses: ensuring balance sheet totals reconcile, or capital ratios are within regulatory limits."),
        },
    };

    private static readonly List<KeyboardShortcutGroup> _shortcuts = new()
    {
        new("Navigation", new()
        {
            new("⌘K / Ctrl+K", "Open command palette"),
            new("?",           "Show keyboard shortcuts"),
            new("G  D",        "Go to Dashboard"),
            new("G  T",        "Go to Templates"),
            new("G  S",        "Go to Submissions"),
            new("G  F",        "Go to Formulas"),
            new("G  A",        "Go to Audit Log"),
        }),
        new("Actions", new()
        {
            new("N",          "New item (context-sensitive)"),
            new("E",          "Edit focused item"),
            new("Del",        "Delete focused item"),
            new("⌘S / Ctrl+S","Save current form"),
            new("Esc",        "Close modal or cancel action"),
        }),
        new("Tables & Lists", new()
        {
            new("/ or F",    "Focus search / filter input"),
            new("↑ ↓",       "Navigate rows"),
            new("Enter",     "Open selected row"),
            new("⌘A / Ctrl+A","Select all visible rows"),
            new("X",         "Toggle row selection"),
        }),
        new("Drag & Drop", new()
        {
            new("Space",     "Pick up / drop item"),
            new("↑ ↓",       "Move item while held"),
            new("Esc",       "Cancel drag"),
        }),
        new("Help", new()
        {
            new("?",         "Show this overlay"),
            new("Shift+/",   "Open contextual help panel"),
        }),
    };

    private static readonly List<TourStep> _dashboardTour = new()
    {
        new(".fc-ph", "Welcome to your Dashboard",
            "This is your command centre. Here you'll see live metrics across all your compliance activities. Let's take a quick tour.", "bottom"),
        new(".fc-topbar__search-trigger", "Command Palette",
            "Press ⌘K (Mac) or Ctrl+K (Windows) to open the command palette. You can jump to any page or trigger any action without touching the mouse.", "bottom"),
        new(".fc-shell__sidebar", "Sidebar Navigation",
            "The sidebar gives you access to all sections. Collapse it with the chevron arrow to gain more working space — icons stay visible in collapsed mode.", "right"),
    };

    private static readonly List<TourStep> _templatesTour = new()
    {
        new(".fc-ph", "Template Management",
            "Templates define the structure of financial return submissions — sheets, fields, formulas, and validation rules.", "bottom"),
        new(".fc-ph__actions", "Template Actions",
            "Use the primary button to create a new template. The overflow menu gives access to import, bulk export, and archive actions.", "bottom"),
    };

    private static readonly List<TourStep> _submissionsTour = new()
    {
        new(".fc-ph", "Submission Management",
            "Track all financial return submissions from regulated institutions. Each submission goes through a validation pipeline before acceptance.", "bottom"),
        new(".fc-ph__actions", "View Options",
            "Toggle between List view (sortable, filterable table) and Kanban view (drag-and-drop status board) using the action buttons.", "bottom"),
    };

    private static readonly List<ChangelogEntry> _changelog = new()
    {
        new("2.1.0", "March 2026", "Help System, Drag & Drop, Kanban Board", new()
        {
            new("new", "Contextual help panel — page-specific articles, guided tours, and keyboard shortcut overlay"),
            new("new", "What's New changelog modal shown on first visit after updates"),
            new("new", "Glossary tooltips for regulatory terms (IFRS, GAAP, CAR, NPL, LCR …)"),
            new("new", "Kanban board view for submissions — drag cards between status columns"),
            new("new", "DragList component for reordering templates, modules, and form fields"),
            new("new", "FileDropZone component for file upload with drag-and-drop support"),
            new("new", "Universal PageHeader with breadcrumbs, compact scroll mode, and action slots"),
            new("improved", "Command palette now shows recent searches and an item preview panel"),
            new("improved", "Sidebar collapses to icon-only mode to save horizontal space"),
        }),
        new("2.0.0", "January 2026", "Command Palette and Navigation Overhaul", new()
        {
            new("new", "Spotlight-style command palette (⌘K) for instant page and action search"),
            new("new", "Sidebar context menu with favourites and recent pages"),
            new("new", "Data table component with sorting, filtering, export, and column toggles"),
            new("improved", "Dashboard redesigned with hero metrics and date range filtering"),
            new("fixed", "Module import now correctly validates cross-sheet formula references"),
        }),
        new("1.5.0", "November 2025", "Billing and Tenant Management", new()
        {
            new("new", "Revenue analytics with monthly breakdown charts"),
            new("new", "Partner onboarding workflow for Platform Admins"),
            new("new", "Tenant subscription management with plan tiers"),
            new("improved", "Audit log now shows diff previews for data changes"),
            new("fixed", "Fixed date range export in revenue report for multi-year spans"),
        }),
    };
}
