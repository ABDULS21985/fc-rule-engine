namespace FC.Engine.Admin.Services;

public enum ToastVariant
{
    Success,
    Warning,
    Error,
    Info
}

public sealed class ToastItem
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public ToastVariant Variant { get; init; }
    public string? Title { get; init; }
    public string Message { get; init; } = "";
    public int DurationMs { get; init; } = 5000;
    public bool Dismissible { get; init; } = true;
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
}

public sealed class ToastService : IDisposable
{
    private readonly List<ToastItem> _toasts = new();
    private readonly Dictionary<string, Timer> _timers = new();
    private const int MaxToasts = 5;

    public IReadOnlyList<ToastItem> Toasts => _toasts;

    public event Action? OnChange;

    public void Show(ToastItem toast)
    {
        // Enforce stack limit
        while (_toasts.Count >= MaxToasts)
        {
            RemoveToast(_toasts[0].Id);
        }

        _toasts.Add(toast);
        NotifyStateChanged();

        if (toast.DurationMs > 0)
        {
            var timer = new Timer(_ => RemoveToast(toast.Id), null, toast.DurationMs, Timeout.Infinite);
            _timers[toast.Id] = timer;
        }
    }

    public void Success(string message, string? title = null)
        => Show(new ToastItem { Variant = ToastVariant.Success, Message = message, Title = title });

    public void Warning(string message, string? title = null)
        => Show(new ToastItem { Variant = ToastVariant.Warning, Message = message, Title = title });

    public void Error(string message, string? title = null)
        => Show(new ToastItem { Variant = ToastVariant.Error, Message = message, Title = title, DurationMs = 8000 });

    public void Info(string message, string? title = null)
        => Show(new ToastItem { Variant = ToastVariant.Info, Message = message, Title = title });

    public void Dismiss(string toastId) => RemoveToast(toastId);

    private void RemoveToast(string id)
    {
        var removed = _toasts.RemoveAll(t => t.Id == id) > 0;
        if (_timers.Remove(id, out var timer))
        {
            timer.Dispose();
        }
        if (removed)
        {
            NotifyStateChanged();
        }
    }

    private void NotifyStateChanged() => OnChange?.Invoke();

    public void Dispose()
    {
        foreach (var timer in _timers.Values)
        {
            timer.Dispose();
        }
        _timers.Clear();
    }
}
