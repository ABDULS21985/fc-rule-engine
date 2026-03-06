namespace FC.Engine.Admin.Components.Shared;

/// <summary>A single breadcrumb navigation item.</summary>
/// <param name="Label">Human-readable label shown in the breadcrumb trail.</param>
/// <param name="Href">Navigation URL. Null for the current (last) item — renders as plain text.</param>
public record BreadcrumbItem(string Label, string? Href = null);

/// <summary>A tab entry in the PageHeader tab bar.</summary>
/// <param name="Id">Unique identifier used for active-tab tracking.</param>
/// <param name="Label">Visible tab label text.</param>
/// <param name="Href">Optional navigation link; if null the tab fires OnTabChange instead.</param>
/// <param name="Icon">Optional SVG markup string for a tab icon.</param>
/// <param name="Count">Optional numeric badge on the tab.</param>
public record TabItem(string Id, string Label, string? Href = null, string? Icon = null, int? Count = null);
