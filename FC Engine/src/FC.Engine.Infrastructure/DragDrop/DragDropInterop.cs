using Microsoft.JSInterop;

namespace FC.Engine.Infrastructure.DragDrop;

public class DragDropInterop
{
    private readonly IJSRuntime _js;

    public DragDropInterop(IJSRuntime js)
    {
        _js = js;
    }

    public Task InitSortable(string containerSelector, string itemSelector, object options)
        => _js.InvokeVoidAsync("FCDragDrop.sortable", containerSelector, itemSelector, options).AsTask();

    public Task InitKanban(string boardSelector, string columnSelector, string cardSelector, object options)
        => _js.InvokeVoidAsync("FCDragDrop.kanban", boardSelector, columnSelector, cardSelector, options).AsTask();

    public Task InitColumnReorder(string tableSelector, object options)
        => _js.InvokeVoidAsync("FCDragDrop.columnReorder", tableSelector, options).AsTask();

    public Task InitDropZone(string selector, object options)
        => _js.InvokeVoidAsync("FCDragDrop.dropZone", selector, options).AsTask();

    public ValueTask<string?> GetSavedOrder(string storageKey)
        => _js.InvokeAsync<string?>("FCDragDrop.getSavedOrder", storageKey);

    public Task Destroy(string containerId)
        => _js.InvokeVoidAsync("FCDragDrop.destroy", containerId).AsTask();
}
