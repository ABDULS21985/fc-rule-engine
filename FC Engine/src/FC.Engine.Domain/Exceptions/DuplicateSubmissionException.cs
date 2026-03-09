namespace FC.Engine.Domain.Exceptions;

/// <summary>
/// Thrown when a submission for a given return already exists in an active batch,
/// preventing duplicate regulatory submissions per R-06 (idempotency).
/// </summary>
public sealed class DuplicateSubmissionException : Exception
{
    public long ExistingBatchId { get; }
    public int ReturnInstanceId { get; }

    public DuplicateSubmissionException(int returnInstanceId, long existingBatchId)
        : base($"Return {returnInstanceId} is already queued in batch {existingBatchId}.")
    {
        ReturnInstanceId = returnInstanceId;
        ExistingBatchId = existingBatchId;
    }
}
