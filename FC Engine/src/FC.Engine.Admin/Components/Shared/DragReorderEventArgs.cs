namespace FC.Engine.Admin.Components.Shared;

/// <summary>Event arguments emitted when a drag-and-drop reorder completes.</summary>
/// <typeparam name="TItem">The item type in the list.</typeparam>
public class DragReorderEventArgs<TItem>
{
    /// <summary>Full list in the new order after the drag.</summary>
    public List<TItem> NewOrder { get; init; } = new();

    /// <summary>The item that was moved.</summary>
    public TItem? MovedItem { get; init; }

    /// <summary>Index before the move (-1 if keyboard-initiated).</summary>
    public int OldIndex { get; init; }

    /// <summary>Index after the move.</summary>
    public int NewIndex { get; init; }
}
