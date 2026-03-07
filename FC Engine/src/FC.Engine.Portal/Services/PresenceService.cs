using System.Collections.Concurrent;

namespace FC.Engine.Portal.Services;

/// <summary>
/// Represents a user currently viewing or editing a resource (return/form/approval).
/// </summary>
public sealed class PresenceUser
{
    public int UserId { get; init; }
    public string DisplayName { get; init; } = "";
    public string Role { get; init; } = "";
    public string Initials { get; init; } = "";
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public string? ActiveFieldId { get; set; }
    public bool IsOnline => (DateTime.UtcNow - LastSeen).TotalSeconds < 60;
    public string Status => IsOnline ? "online" : "offline";
}

/// <summary>
/// Lightweight in-memory presence tracking. Each resource (keyed by string like "return:42" or "form:CBN001")
/// maintains a dictionary of active users.
/// </summary>
public interface IPresenceService
{
    /// <summary>Heartbeat: upserts the user's presence for a resource. Call every 15s.</summary>
    void Heartbeat(string resourceKey, PresenceUser user);

    /// <summary>Mark a specific field as being edited by this user (or null to clear).</summary>
    void SetActiveField(string resourceKey, int userId, string? fieldId);

    /// <summary>Remove user from a resource (e.g. on page leave).</summary>
    void Leave(string resourceKey, int userId);

    /// <summary>Get all users currently on a resource (includes recently-offline for grace period).</summary>
    IReadOnlyList<PresenceUser> GetViewers(string resourceKey, int? excludeUserId = null);

    /// <summary>Get the field lock map: fieldId → user who is editing it.</summary>
    IReadOnlyDictionary<string, PresenceUser> GetFieldLocks(string resourceKey, int? excludeUserId = null);
}

public sealed class InMemoryPresenceService : IPresenceService, IDisposable
{
    // resourceKey → (userId → PresenceUser)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, PresenceUser>> _store = new();
    private readonly Timer _cleanupTimer;
    private const int StaleThresholdSeconds = 90; // remove after 90s of no heartbeat

    public InMemoryPresenceService()
    {
        _cleanupTimer = new Timer(Cleanup, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public void Heartbeat(string resourceKey, PresenceUser user)
    {
        var users = _store.GetOrAdd(resourceKey, _ => new ConcurrentDictionary<int, PresenceUser>());
        if (users.TryGetValue(user.UserId, out var existing))
        {
            existing.LastSeen = DateTime.UtcNow;
            existing.ActiveFieldId = user.ActiveFieldId;
        }
        else
        {
            user.LastSeen = DateTime.UtcNow;
            users[user.UserId] = user;
        }
    }

    public void SetActiveField(string resourceKey, int userId, string? fieldId)
    {
        if (_store.TryGetValue(resourceKey, out var users) && users.TryGetValue(userId, out var user))
        {
            user.ActiveFieldId = fieldId;
        }
    }

    public void Leave(string resourceKey, int userId)
    {
        if (_store.TryGetValue(resourceKey, out var users))
        {
            users.TryRemove(userId, out _);
            if (users.IsEmpty) _store.TryRemove(resourceKey, out _);
        }
    }

    public IReadOnlyList<PresenceUser> GetViewers(string resourceKey, int? excludeUserId = null)
    {
        if (!_store.TryGetValue(resourceKey, out var users)) return [];
        return users.Values
            .Where(u => excludeUserId == null || u.UserId != excludeUserId.Value)
            .Where(u => (DateTime.UtcNow - u.LastSeen).TotalSeconds < StaleThresholdSeconds)
            .OrderByDescending(u => u.LastSeen)
            .ToList();
    }

    public IReadOnlyDictionary<string, PresenceUser> GetFieldLocks(string resourceKey, int? excludeUserId = null)
    {
        if (!_store.TryGetValue(resourceKey, out var users))
            return new Dictionary<string, PresenceUser>();

        return users.Values
            .Where(u => u.ActiveFieldId != null && u.IsOnline)
            .Where(u => excludeUserId == null || u.UserId != excludeUserId.Value)
            .ToDictionary(u => u.ActiveFieldId!, u => u);
    }

    private void Cleanup(object? _)
    {
        foreach (var (key, users) in _store)
        {
            foreach (var (userId, user) in users)
            {
                if ((DateTime.UtcNow - user.LastSeen).TotalSeconds > StaleThresholdSeconds)
                    users.TryRemove(userId, out _);
            }
            if (users.IsEmpty) _store.TryRemove(key, out _);
        }
    }

    public void Dispose() => _cleanupTimer.Dispose();
}
