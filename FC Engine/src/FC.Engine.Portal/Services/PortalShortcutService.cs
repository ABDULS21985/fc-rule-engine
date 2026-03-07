namespace FC.Engine.Portal.Services;

/// <summary>
/// Brokers context-sensitive keyboard shortcuts between the global JS handler
/// (in PortalLayout) and individual page components.
///
/// Usage in a page/component:
///   [Inject] PortalShortcutService Shortcuts { get; set; } = default!;
///
///   protected override void OnInitialized()
///       => Shortcuts.Register(HandleShortcut);
///
///   private async Task HandleShortcut(string id)
///   {
///       if (id == "ctrl+s") await SaveDraftAsync();
///       if (id.StartsWith("f1:")) ShowFieldHelp(id["f1:".Length..]);
///   }
///
///   public void Dispose() => Shortcuts.Unregister(HandleShortcut);
///
/// Shortcut IDs fired by JS:
///   "ctrl+s"      — save draft (fired when Ctrl/⌘+S pressed outside a table)
///   "f1:{fieldId}"— field-level help (F1 pressed; fieldId may be empty)
/// </summary>
public class PortalShortcutService
{
    private event Func<string, Task>? _handlers;

    /// <summary>Register a handler. Call in OnInitialized; unregister in Dispose/DisposeAsync.</summary>
    public void Register(Func<string, Task> handler) => _handlers += handler;

    /// <summary>Unregister a previously registered handler.</summary>
    public void Unregister(Func<string, Task> handler) => _handlers -= handler;

    /// <summary>Called by PortalLayout's JSInvokable method when a shortcut fires.</summary>
    internal async Task FireAsync(string shortcutId)
    {
        if (_handlers is null) return;
        foreach (var handler in _handlers.GetInvocationList().Cast<Func<string, Task>>())
        {
            try { await handler(shortcutId); }
            catch { /* handler exceptions must not propagate */ }
        }
    }
}
