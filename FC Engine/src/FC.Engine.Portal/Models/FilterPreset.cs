namespace FC.Engine.Portal.Models;

/// <summary>
/// Represents a named, saved set of filter values for a specific list page.
/// Persisted to browser localStorage keyed by PageKey.
/// </summary>
public sealed class FilterPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    /// <summary>Key → value map of filter param names (q, status, template, sort, role, …).</summary>
    public Dictionary<string, string> Filters { get; set; } = new();
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
}
