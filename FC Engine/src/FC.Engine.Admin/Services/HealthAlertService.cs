namespace FC.Engine.Admin.Services;

public enum HealthAlertSeverity { Warning, Critical }

public sealed class HealthAlert
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string Metric { get; init; } = "";
    public string Message { get; init; } = "";
    public HealthAlertSeverity Severity { get; init; }
    public bool IsNew { get; set; } = true;
}

public sealed class HealthAlertService
{
    private readonly List<HealthAlert> _alerts = [];
    private readonly Dictionary<string, DateTime> _suppressUntil = [];

    public IReadOnlyList<HealthAlert> Alerts => _alerts;
    public int UnreadCount => _alerts.Count(a => a.IsNew);

    public event Action? OnChange;

    public void Raise(string metric, string message, HealthAlertSeverity severity)
    {
        var now = DateTime.UtcNow;
        if (_suppressUntil.TryGetValue(metric, out var until) && now < until)
            return;

        _suppressUntil[metric] = now.AddMinutes(5);

        _alerts.Insert(0, new HealthAlert
        {
            Metric = metric,
            Message = message,
            Severity = severity
        });

        // Cap list at 100 entries
        while (_alerts.Count > 100)
            _alerts.RemoveAt(_alerts.Count - 1);

        OnChange?.Invoke();
    }

    public void ClearAll()
    {
        _alerts.Clear();
        OnChange?.Invoke();
    }

    public void MarkAllRead()
    {
        foreach (var a in _alerts)
            a.IsNew = false;
        OnChange?.Invoke();
    }
}
