namespace FC.Engine.Admin.Services;

public sealed class ConfirmRequest
{
    public string Title { get; init; } = "Confirm Action";
    public string Message { get; init; } = "Are you sure you want to proceed?";
    public string ConfirmText { get; init; } = "Confirm";
    public string CancelText { get; init; } = "Cancel";
    public bool IsDestructive { get; init; }
    public string? Icon { get; init; }
}

public sealed class DialogService
{
    private TaskCompletionSource<bool>? _tcs;

    public ConfirmRequest? CurrentRequest { get; private set; }
    public bool IsOpen => CurrentRequest is not null;

    public event Action? OnChange;

    public Task<bool> Confirm(ConfirmRequest request)
    {
        CurrentRequest = request;
        _tcs = new TaskCompletionSource<bool>();
        NotifyStateChanged();
        return _tcs.Task;
    }

    public Task<bool> ConfirmDelete(string itemName)
        => Confirm(new ConfirmRequest
        {
            Title = $"Delete {itemName}",
            Message = $"Are you sure you want to delete this {itemName.ToLowerInvariant()}? This action cannot be undone.",
            ConfirmText = "Delete",
            IsDestructive = true,
            Icon = "delete"
        });

    public Task<bool> ConfirmArchive(string itemName)
        => Confirm(new ConfirmRequest
        {
            Title = $"Archive {itemName}",
            Message = $"Archive this {itemName.ToLowerInvariant()} from the active workflow? You can restore it later if supported.",
            ConfirmText = "Archive",
            IsDestructive = true,
            Icon = "archive"
        });

    public Task<bool> ConfirmPublish(string itemName)
        => Confirm(new ConfirmRequest
        {
            Title = $"Publish {itemName}",
            Message = $"Publish this {itemName.ToLowerInvariant()} and make it the active record for future submissions?",
            ConfirmText = "Publish",
            IsDestructive = false,
            Icon = "publish"
        });

    public void Complete(bool result)
    {
        _tcs?.TrySetResult(result);
        _tcs = null;
        CurrentRequest = null;
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
