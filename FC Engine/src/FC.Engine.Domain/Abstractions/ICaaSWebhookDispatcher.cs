namespace FC.Engine.Domain.Abstractions;

public enum WebhookEventType
{
    FilingCompleted,
    ValidationFailed,
    DeadlineApproaching,
    ChangesDetected,
    ScoreUpdated,
    ExtractionCompleted,
    AutoFilingHeld
}

public interface ICaaSWebhookDispatcher
{
    /// <summary>
    /// Enqueues a webhook delivery for the given partner.
    /// Returns immediately — delivery is asynchronous.
    /// </summary>
    Task EnqueueAsync(
        int partnerId,
        WebhookEventType eventType,
        object payload,
        CancellationToken ct = default);

    /// <summary>
    /// Background worker calls this to dispatch all pending webhook deliveries.
    /// Handles at-least-once delivery with exponential back-off and dead-lettering.
    /// </summary>
    Task ProcessPendingAsync(CancellationToken ct = default);
}
