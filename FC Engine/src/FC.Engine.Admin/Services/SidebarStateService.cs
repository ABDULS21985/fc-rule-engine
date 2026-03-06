using Microsoft.JSInterop;

namespace FC.Engine.Admin.Services;

/// <summary>
/// Manages intelligent sidebar state: collapsed mode, group expand/collapse,
/// favorites (pinned pages), and recent page history — all persisted to LocalStorage.
/// </summary>
public sealed class SidebarStateService : IAsyncDisposable
{
    private readonly IJSRuntime _js;

    private const string KeyCollapsed  = "fc-sidebar-collapsed";
    private const string KeyGroups     = "fc-sidebar-groups";
    private const string KeyFavorites  = "fc-sidebar-favorites";
    private const string KeyRecent     = "fc-sidebar-recent";
    private const int    MaxRecent     = 5;

    public bool IsCollapsed { get; private set; }
    public HashSet<string> CollapsedGroups { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<SidebarNavItem> Favorites { get; private set; } = [];
    public List<SidebarNavItem> RecentPages { get; private set; } = [];

    private bool _initialized;

    public event Action? OnStateChanged;

    public SidebarStateService(IJSRuntime js) => _js = js;

    // ------------------------------------------------------------------
    //  Initialization (call once from NavMenu OnAfterRenderAsync)
    // ------------------------------------------------------------------

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            IsCollapsed = await GetBool(KeyCollapsed);

            var groups = await GetString(KeyGroups);
            if (!string.IsNullOrEmpty(groups))
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(groups);
                CollapsedGroups = new HashSet<string>(list ?? [], StringComparer.OrdinalIgnoreCase);
            }

            var favJson = await GetString(KeyFavorites);
            if (!string.IsNullOrEmpty(favJson))
                Favorites = System.Text.Json.JsonSerializer.Deserialize<List<SidebarNavItem>>(favJson) ?? [];

            var recentJson = await GetString(KeyRecent);
            if (!string.IsNullOrEmpty(recentJson))
                RecentPages = System.Text.Json.JsonSerializer.Deserialize<List<SidebarNavItem>>(recentJson) ?? [];
        }
        catch
        {
            // JS not ready (prerender) — silently continue with defaults
        }
    }

    // ------------------------------------------------------------------
    //  Collapsed sidebar toggle
    // ------------------------------------------------------------------

    public async Task ToggleCollapsedAsync()
    {
        IsCollapsed = !IsCollapsed;
        await SetBool(KeyCollapsed, IsCollapsed);
        OnStateChanged?.Invoke();
    }

    public async Task SetCollapsedAsync(bool collapsed)
    {
        if (IsCollapsed == collapsed) return;
        IsCollapsed = collapsed;
        await SetBool(KeyCollapsed, IsCollapsed);
        OnStateChanged?.Invoke();
    }

    // ------------------------------------------------------------------
    //  Group collapse/expand
    // ------------------------------------------------------------------

    public async Task ToggleGroupAsync(string groupId)
    {
        if (CollapsedGroups.Contains(groupId))
            CollapsedGroups.Remove(groupId);
        else
            CollapsedGroups.Add(groupId);

        await SetString(KeyGroups,
            System.Text.Json.JsonSerializer.Serialize(CollapsedGroups.ToList()));
        OnStateChanged?.Invoke();
    }

    public bool IsGroupCollapsed(string groupId) => CollapsedGroups.Contains(groupId);

    // ------------------------------------------------------------------
    //  Favorites / Pinned
    // ------------------------------------------------------------------

    public async Task ToggleFavoriteAsync(SidebarNavItem item)
    {
        var existing = Favorites.FirstOrDefault(f => f.Href == item.Href);
        if (existing != null)
            Favorites.Remove(existing);
        else
            Favorites.Insert(0, item);

        await PersistFavoritesAsync();
        OnStateChanged?.Invoke();
    }

    public bool IsFavorite(string href) =>
        Favorites.Any(f => string.Equals(f.Href, href, StringComparison.OrdinalIgnoreCase));

    private Task PersistFavoritesAsync() =>
        SetString(KeyFavorites, System.Text.Json.JsonSerializer.Serialize(Favorites));

    // ------------------------------------------------------------------
    //  Recent pages
    // ------------------------------------------------------------------

    public async Task AddRecentPageAsync(SidebarNavItem item)
    {
        // Don't track Dashboard as "recent"
        if (item.Href == "/" || string.IsNullOrWhiteSpace(item.Label)) return;

        RecentPages.RemoveAll(r => string.Equals(r.Href, item.Href, StringComparison.OrdinalIgnoreCase));
        RecentPages.Insert(0, item);

        if (RecentPages.Count > MaxRecent)
            RecentPages = RecentPages.Take(MaxRecent).ToList();

        await SetString(KeyRecent, System.Text.Json.JsonSerializer.Serialize(RecentPages));
        OnStateChanged?.Invoke();
    }

    // ------------------------------------------------------------------
    //  LocalStorage helpers
    // ------------------------------------------------------------------

    private async Task<bool> GetBool(string key)
    {
        var val = await GetString(key);
        return val == "true";
    }

    private Task SetBool(string key, bool value) =>
        SetString(key, value ? "true" : "false");

    private async Task<string?> GetString(string key)
    {
        try
        {
            return await _js.InvokeAsync<string?>("FCSidebar.getItem", key);
        }
        catch
        {
            return null;
        }
    }

    private async Task SetString(string key, string value)
    {
        try
        {
            await _js.InvokeVoidAsync("FCSidebar.setItem", key, value);
        }
        catch { /* prerender / circuit not ready */ }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Lightweight nav item reference stored in LocalStorage.</summary>
public sealed record SidebarNavItem(string Label, string Href, string IconPath);
