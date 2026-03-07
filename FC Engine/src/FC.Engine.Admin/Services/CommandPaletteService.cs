namespace FC.Engine.Admin.Services;

public sealed class CommandPaletteService
{
    private readonly List<CommandItem> _commands = [];
    private readonly List<RecentSearch> _recentSearches = [];
    private const int MaxRecent = 5;
    private const int MaxResults = 12;

    public bool IsOpen { get; private set; }
    public event Action? OnToggled;

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        OnToggled?.Invoke();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        OnToggled?.Invoke();
    }

    public void Toggle()
    {
        IsOpen = !IsOpen;
        OnToggled?.Invoke();
    }

    public void RegisterCommands(bool isPlatformAdmin, bool hasModules)
    {
        _commands.Clear();

        // Always available
        _commands.Add(Nav("Dashboard", "/", "Main dashboard overview", "home,overview,start",
            """<rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/>"""));

        if (hasModules || isPlatformAdmin)
        {
            _commands.Add(Nav("Templates", "/templates", "Manage regulatory return templates", "template,return,form,report",
                """<path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"/><polyline points="14 2 14 8 20 8"/>"""));
            _commands.Add(Nav("Formulas", "/formulas", "Manage calculation formulas", "formula,calculation,compute,math",
                """<line x1="4" y1="9" x2="20" y2="9"/><line x1="4" y1="15" x2="20" y2="15"/><line x1="10" y1="3" x2="8" y2="21"/><line x1="16" y1="3" x2="14" y2="21"/>"""));
            _commands.Add(Nav("Cross-Sheet Rules", "/cross-sheet-rules", "Cross-sheet validation rules", "cross,sheet,rule,validation",
                """<polyline points="22 12 18 12 15 21 9 3 6 12 2 12"/>"""));
            _commands.Add(Nav("Business Rules", "/business-rules", "Business validation rules", "business,rule,validation,compliance",
                """<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>"""));
            _commands.Add(Nav("Submissions", "/submissions", "View filing submissions", "submission,filing,submit,return",
                """<polyline points="22 12 16 12 14 15 10 15 8 12 2 12"/><path d="M5.45 5.11L2 12v6a2 2 0 002 2h16a2 2 0 002-2v-6l-3.45-6.89A2 2 0 0016.76 4H7.24a2 2 0 00-1.79 1.11z"/>"""));
        }

        // Administration (Admin/Approver)
        _commands.Add(Nav("Impact Analysis", "/impact-analysis", "Analyse template change impacts", "impact,analysis,change",
            """<line x1="18" y1="20" x2="18" y2="10"/><line x1="12" y1="20" x2="12" y2="4"/><line x1="6" y1="20" x2="6" y2="14"/>"""));
        _commands.Add(Nav("Audit Log", "/audit", "View audit trail and activity", "audit,log,activity,trail,history",
            """<path d="M16 4h2a2 2 0 012 2v14a2 2 0 01-2 2H6a2 2 0 01-2-2V6a2 2 0 012-2h2"/><rect x="8" y="2" width="8" height="4" rx="1" ry="1"/>"""));
        _commands.Add(Nav("DPO Dashboard", "/privacy/dpo", "Data protection officer dashboard", "dpo,privacy,gdpr,ndpr,data,protection",
            """<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/><path d="M9 12l2 2 4-4"/>"""));
        _commands.Add(Nav("Users", "/users", "Manage user accounts and roles", "user,account,role,permission,people",
            """<path d="M17 21v-2a4 4 0 00-4-4H5a4 4 0 00-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 00-3-3.87"/><path d="M16 3.13a4 4 0 010 7.75"/>"""));

        if (isPlatformAdmin)
        {
            _commands.Add(Nav("Platform Dashboard", "/dashboard/platform", "Platform-wide metrics and overview", "platform,dashboard,metrics,overview",
                """<line x1="12" y1="20" x2="12" y2="10"/><line x1="18" y1="20" x2="18" y2="4"/><line x1="6" y1="20" x2="6" y2="14"/>"""));
            _commands.Add(Nav("Tenant List", "/platform/tenants", "Browse and manage all tenants", "tenant,institution,client,organisation",
                """<path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/>"""));
            _commands.Add(Nav("Template Console", "/platform/templates", "Platform template management console", "template,console,platform",
                """<path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/>"""));
            _commands.Add(Nav("Billing Operations", "/platform/billing-ops", "Platform billing operations", "billing,ops,operations,payment",
                """<line x1="12" y1="20" x2="12" y2="10"/><line x1="18" y1="20" x2="18" y2="4"/><line x1="6" y1="20" x2="6" y2="14"/>"""));
            _commands.Add(Nav("Platform Health", "/platform/health", "System health and monitoring", "health,monitoring,status,uptime",
                """<polyline points="22 12 18 12 15 21 9 3 6 12 2 12"/>"""));
            _commands.Add(Nav("Module Analytics", "/platform/module-analytics", "Module usage analytics", "module,analytics,usage,statistics",
                """<rect x="2" y="2" width="8" height="8" rx="1"/><rect x="14" y="2" width="8" height="8" rx="1"/><rect x="2" y="14" width="8" height="8" rx="1"/><rect x="14" y="14" width="8" height="8" rx="1"/>"""));
            _commands.Add(Nav("Feature Flags", "/platform/feature-flags", "Toggle platform feature flags", "feature,flag,toggle,switch",
                """<path d="M6 3v18"/><path d="M6 4h10l-2 4 2 4H6"/>"""));
            _commands.Add(Nav("Partner Onboarding", "/platform/partners/onboard", "Onboard new partner organisations", "partner,onboarding,onboard",
                """<path d="M17 21v-2a4 4 0 00-4-4H5a4 4 0 00-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 11h-6"/><path d="M20 8v6"/>"""));
            _commands.Add(Nav("Module Registry", "/modules", "Manage regulatory modules", "module,registry,regulatory",
                """<rect x="2" y="2" width="8" height="8" rx="1"/><rect x="14" y="2" width="8" height="8" rx="1"/><rect x="2" y="14" width="8" height="8" rx="1"/><rect x="14" y="14" width="8" height="8" rx="1"/>"""));
            _commands.Add(Nav("Jurisdictions", "/jurisdictions", "Manage jurisdictions and regulators", "jurisdiction,regulator,country,region",
                """<circle cx="12" cy="12" r="10"/><line x1="2" y1="12" x2="22" y2="12"/><path d="M12 2a15.3 15.3 0 014 10 15.3 15.3 0 01-4 10 15.3 15.3 0 01-4-10 15.3 15.3 0 014-10z"/>"""));
            _commands.Add(Nav("Licence Types", "/licence-types", "Manage licence type definitions", "licence,license,type",
                """<path d="M9 21H5a2 2 0 01-2-2V5a2 2 0 012-2h4"/><polyline points="16 17 21 12 16 7"/><line x1="21" y1="12" x2="9" y2="12"/>"""));
            _commands.Add(Nav("Billing Plans", "/billing/plans", "Configure billing plans and pricing", "billing,plan,pricing,subscription",
                """<rect x="2" y="6" width="20" height="12" rx="2"/><line x1="2" y1="10" x2="22" y2="10"/>"""));
            _commands.Add(Nav("Subscriptions", "/billing/subscriptions", "Manage tenant subscriptions", "subscription,billing,tenant",
                """<path d="M16 3h5v5"/><path d="M4 20L21 3"/><path d="M21 16v5h-5"/><path d="M15 21L3 9"/>"""));
            _commands.Add(Nav("Invoices", "/billing/invoices", "View and manage invoices", "invoice,billing,payment",
                """<path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="8" y1="13" x2="16" y2="13"/>"""));
            _commands.Add(Nav("Revenue", "/billing/revenue", "Revenue reports and analytics", "revenue,income,billing,money",
                """<line x1="12" y1="20" x2="12" y2="10"/><line x1="18" y1="20" x2="18" y2="4"/><line x1="6" y1="20" x2="6" y2="14"/>"""));

            // Platform actions
            _commands.Add(Action("Export Tenants", "Export tenant list to Excel", "export,tenants,excel,download",
                """<path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/>""",
                "/platform/tenants"));
        }

        // General actions
        _commands.Add(Action("View Audit Log", "Open the audit trail", "audit,log,trail",
            """<path d="M16 4h2a2 2 0 012 2v14a2 2 0 01-2 2H6a2 2 0 01-2-2V6a2 2 0 012-2h2"/><rect x="8" y="2" width="8" height="4" rx="1" ry="1"/>""",
            "/audit"));
        _commands.Add(Action("Manage Users", "Go to user management", "user,manage,people",
            """<path d="M17 21v-2a4 4 0 00-4-4H5a4 4 0 00-4 4v2"/><circle cx="9" cy="7" r="4"/>""",
            "/users"));
    }

    public List<CommandGroup> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            // Return recent searches + all nav items grouped
            var groups = new List<CommandGroup>();

            if (_recentSearches.Count > 0)
            {
                groups.Add(new CommandGroup("Recent", _recentSearches.Select(r =>
                    new CommandItem(r.ItemId, r.Label, CommandCategory.Recent, r.Description, r.Url, r.Icon ?? string.Empty, [])
                ).ToList()));
            }

            var navItems = _commands.Where(c => c.Category == CommandCategory.Navigation).ToList();
            if (navItems.Count > 0)
                groups.Add(new CommandGroup("Navigation", navItems));

            var actions = _commands.Where(c => c.Category == CommandCategory.Action).ToList();
            if (actions.Count > 0)
                groups.Add(new CommandGroup("Actions", actions));

            return groups;
        }

        var q = query.Trim();
        var scored = new List<(CommandItem Item, int Score)>();

        foreach (var cmd in _commands)
        {
            var score = ScoreMatch(cmd, q);
            if (score > 0)
                scored.Add((cmd, score));
        }

        var results = scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Item.Label)
            .Take(MaxResults)
            .Select(s => s.Item)
            .ToList();

        var grouped = new List<CommandGroup>();
        var navResults = results.Where(r => r.Category == CommandCategory.Navigation).ToList();
        if (navResults.Count > 0)
            grouped.Add(new CommandGroup("Navigation", navResults));

        var actionResults = results.Where(r => r.Category == CommandCategory.Action).ToList();
        if (actionResults.Count > 0)
            grouped.Add(new CommandGroup("Actions", actionResults));

        return grouped;
    }

    public void AddRecentSearch(CommandItem item)
    {
        _recentSearches.RemoveAll(r => r.ItemId == item.Id);
        _recentSearches.Insert(0, new RecentSearch(item.Id, item.Label, item.Description, item.Url, item.IconPath, DateTime.UtcNow));
        if (_recentSearches.Count > MaxRecent)
            _recentSearches.RemoveAt(_recentSearches.Count - 1);
    }

    private static int ScoreMatch(CommandItem cmd, string query)
    {
        var label = cmd.Label;
        var score = 0;

        if (label.Equals(query, StringComparison.OrdinalIgnoreCase))
            score = 100;
        else if (label.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            score = 80;
        else if (label.Contains(query, StringComparison.OrdinalIgnoreCase))
            score = 60;

        if (score == 0 && !string.IsNullOrEmpty(cmd.Description) &&
            cmd.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            score = 50;

        if (score == 0)
        {
            foreach (var kw in cmd.Keywords)
            {
                if (kw.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                {
                    score = 45;
                    break;
                }
                if (kw.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    score = 40;
                    break;
                }
            }
        }

        // Bonus for URL match
        if (score > 0 && !string.IsNullOrEmpty(cmd.Url) &&
            cmd.Url.Contains(query, StringComparison.OrdinalIgnoreCase))
            score += 5;

        return score;
    }

    private static CommandItem Nav(string label, string url, string description, string keywords, string iconPath) =>
        new($"nav-{url}", label, CommandCategory.Navigation, description, url, iconPath,
            keywords.Split(',', StringSplitOptions.RemoveEmptyEntries));

    private static CommandItem Action(string label, string description, string keywords, string iconPath, string? url = null) =>
        new($"act-{label.ToLowerInvariant().Replace(' ', '-')}", label, CommandCategory.Action, description, url, iconPath,
            keywords.Split(',', StringSplitOptions.RemoveEmptyEntries));
}

public enum CommandCategory
{
    Navigation,
    Action,
    Recent
}

public record CommandItem(
    string Id,
    string Label,
    CommandCategory Category,
    string Description,
    string? Url,
    string IconPath,
    string[] Keywords);

public record CommandGroup(string Title, List<CommandItem> Items);

public record RecentSearch(
    string ItemId,
    string Label,
    string Description,
    string? Url,
    string? Icon,
    DateTime SearchedAt);
