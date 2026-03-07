using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace FC.Engine.Portal.Services;

/// <summary>
/// Tracks progressive onboarding state persisted to localStorage.
/// Manages: welcome splash, page-first-visits, dismissed tips, submission count, celebration flag.
/// </summary>
public sealed class OnboardingStateService(IJSRuntime js)
{
    private const string StorageKey = "fc_onboarding_v1";
    private OnboardingState? _cache;

    /// <summary>Raised when a first submission is recorded — lets ConfettiOverlay react immediately.</summary>
    public event Action? OnCelebrationTriggered;

    // ── Welcome splash ──────────────────────────────────────────────────────

    public async Task<bool> HasSeenWelcomeAsync()
    {
        var s = await GetStateAsync();
        return s.WelcomeSeen;
    }

    public async Task MarkWelcomeSeenAsync()
    {
        var s = await GetStateAsync();
        s.WelcomeSeen = true;
        await SaveAsync(s);
    }

    // ── Page spotlights ─────────────────────────────────────────────────────

    public async Task<bool> HasVisitedPageAsync(string pageKey)
    {
        var s = await GetStateAsync();
        return s.VisitedPages.Contains(pageKey);
    }

    public async Task MarkPageVisitedAsync(string pageKey)
    {
        var s = await GetStateAsync();
        if (s.VisitedPages.Add(pageKey))
            await SaveAsync(s);
    }

    // ── Contextual tips ─────────────────────────────────────────────────────

    public async Task<bool> HasDismissedTipAsync(string tipKey)
    {
        var s = await GetStateAsync();
        return s.DismissedTips.Contains(tipKey);
    }

    public async Task DismissTipAsync(string tipKey)
    {
        var s = await GetStateAsync();
        if (s.DismissedTips.Add(tipKey))
            await SaveAsync(s);
    }

    // ── Submission milestone ────────────────────────────────────────────────

    public async Task<int> GetCompletedSubmissionsAsync()
    {
        var s = await GetStateAsync();
        return s.CompletedSubmissions;
    }

    /// <summary>
    /// Call this after a successful submission. Returns true if it was the first one
    /// and fires <see cref="OnCelebrationTriggered"/> so the confetti overlay reacts.
    /// </summary>
    public async Task<bool> RecordSubmissionAsync()
    {
        var s = await GetStateAsync();
        s.CompletedSubmissions++;
        var isFirst = s.CompletedSubmissions == 1;
        if (isFirst) s.CelebrationPending = true;
        await SaveAsync(s);
        if (isFirst) OnCelebrationTriggered?.Invoke();
        return isFirst;
    }

    public async Task<bool> IsCelebrationPendingAsync()
    {
        var s = await GetStateAsync();
        return s.CelebrationPending;
    }

    public async Task ClearCelebrationAsync()
    {
        var s = await GetStateAsync();
        if (!s.CelebrationPending) return;
        s.CelebrationPending = false;
        await SaveAsync(s);
    }

    // ── Checklist progress ──────────────────────────────────────────────────

    public async Task<int> GetChecklistProgressAsync()
    {
        var s = await GetStateAsync();
        var done = 0;
        const int total = 5;
        if (s.WelcomeSeen) done++;
        if (s.CompletedSubmissions >= 1) done++;
        if (s.VisitedPages.Contains("calendar")) done++;
        if (s.VisitedPages.Contains("reports")) done++;
        if (s.DismissedTips.Count >= 1) done++;
        return (int)Math.Round(done * 100.0 / total);
    }

    public async Task<OnboardingChecklistItems> GetChecklistItemsAsync()
    {
        var s = await GetStateAsync();
        return new OnboardingChecklistItems
        {
            WelcomeSeen = s.WelcomeSeen,
            FirstSubmission = s.CompletedSubmissions >= 1,
            CalendarVisited = s.VisitedPages.Contains("calendar"),
            ReportsVisited = s.VisitedPages.Contains("reports"),
            TipDismissed = s.DismissedTips.Count >= 1
        };
    }

    // ── Internal ────────────────────────────────────────────────────────────

    private async Task<OnboardingState> GetStateAsync()
    {
        if (_cache is not null) return _cache;
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrEmpty(json))
            {
                _cache = JsonSerializer.Deserialize<OnboardingState>(json, JsonOpts) ?? new();
                return _cache;
            }
        }
        catch { /* prerender or JS unavailable */ }
        _cache = new OnboardingState();
        return _cache;
    }

    private async Task SaveAsync(OnboardingState s)
    {
        _cache = s;
        try
        {
            var json = JsonSerializer.Serialize(s, JsonOpts);
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
        }
        catch { /* prerender */ }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };

    private sealed class OnboardingState
    {
        public bool WelcomeSeen { get; set; }
        public bool CelebrationPending { get; set; }
        public int CompletedSubmissions { get; set; }
        public HashSet<string> VisitedPages { get; set; } = [];
        public HashSet<string> DismissedTips { get; set; } = [];
    }
}

public sealed class OnboardingChecklistItems
{
    public bool WelcomeSeen { get; set; }
    public bool FirstSubmission { get; set; }
    public bool CalendarVisited { get; set; }
    public bool ReportsVisited { get; set; }
    public bool TipDismissed { get; set; }
}
