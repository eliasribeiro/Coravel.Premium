using Coravel.Invocable;

namespace Coravel.Persistence.Interfaces;

/// <summary>
/// Options for configuring a queued job.
/// </summary>
public class PersistentJobOptions
{
    /// <summary>
    /// Maximum number of retry attempts on failure.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Base delay between retry attempts. Actual delay uses exponential backoff.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum execution time before the job is considered stuck.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Job priority. Lower values = higher priority.
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// Unique key to prevent duplicate job execution.
    /// If set, only one job with this key can be pending/running at a time.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// Options for configuring a queued job with a payload.
/// </summary>
/// <typeparam name="TPayload">The type of the payload.</typeparam>
public class PersistentJobOptions<TPayload> : PersistentJobOptions
{
    /// <summary>
    /// The payload to pass to the invocable.
    /// </summary>
    public TPayload? Payload { get; set; }
}

/// <summary>
/// Persistent queue interface for background job processing.
/// Jobs are stored in the database and survive application restarts.
/// </summary>
public interface IPersistentQueue
{
    /// <summary>
    /// Queues an invocable for background execution.
    /// </summary>
    /// <typeparam name="T">The invocable type.</typeparam>
    /// <returns>The ID of the queued job.</returns>
    Task<Guid> QueueInvocableAsync<T>() where T : IInvocable;

    /// <summary>
    /// Queues an invocable for background execution with options.
    /// </summary>
    /// <typeparam name="T">The invocable type.</typeparam>
    /// <param name="configure">Action to configure job options.</param>
    /// <returns>The ID of the queued job.</returns>
    Task<Guid> QueueInvocableAsync<T>(Action<PersistentJobOptions> configure) where T : IInvocable;

    /// <summary>
    /// Queues an invocable with a payload for background execution.
    /// </summary>
    /// <typeparam name="T">The invocable type.</typeparam>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    /// <param name="payload">The payload to pass to the invocable.</param>
    /// <returns>The ID of the queued job.</returns>
    Task<Guid> QueueInvocableWithPayloadAsync<T, TPayload>(TPayload payload)
        where T : IInvocable, IInvocableWithPayload<TPayload>;

    /// <summary>
    /// Queues an invocable with a payload for background execution with options.
    /// </summary>
    /// <typeparam name="T">The invocable type.</typeparam>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    /// <param name="payload">The payload to pass to the invocable.</param>
    /// <param name="configure">Action to configure job options.</param>
    /// <returns>The ID of the queued job.</returns>
    Task<Guid> QueueInvocableWithPayloadAsync<T, TPayload>(TPayload payload, Action<PersistentJobOptions> configure)
        where T : IInvocable, IInvocableWithPayload<TPayload>;

    /// <summary>
    /// Gets the current status of a queued job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <returns>The job history record, or null if not found.</returns>
    Task<Entities.CoravelJobHistory?> GetJobStatusAsync(Guid jobId);

    /// <summary>
    /// Cancels a pending job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <returns>True if the job was cancelled, false if it was not found or already running.</returns>
    Task<bool> CancelJobAsync(Guid jobId);

    /// <summary>
    /// Gets metrics about the queue.
    /// </summary>
    /// <returns>Queue metrics.</returns>
    Task<PersistentQueueMetrics> GetMetricsAsync();
}

/// <summary>
/// Metrics about the persistent queue.
/// </summary>
public class PersistentQueueMetrics
{
    /// <summary>
    /// Number of jobs waiting to be processed.
    /// </summary>
    public int PendingCount { get; set; }

    /// <summary>
    /// Number of jobs currently being processed.
    /// </summary>
    public int RunningCount { get; set; }

    /// <summary>
    /// Number of jobs that completed successfully.
    /// </summary>
    public int CompletedCount { get; set; }

    /// <summary>
    /// Number of jobs that failed after all retry attempts.
    /// </summary>
    public int FailedCount { get; set; }
}

/// <summary>
/// Configuration interface for the persistent queue.
/// </summary>
public interface IPersistentQueueConfiguration
{
    /// <summary>
    /// Configures an error handler for failed jobs.
    /// </summary>
    /// <param name="errorHandler">The error handler.</param>
    /// <returns>The configuration for chaining.</returns>
    IPersistentQueueConfiguration OnError(Action<Exception, Entities.CoravelJobHistory> errorHandler);
}
