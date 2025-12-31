using System.ComponentModel.DataAnnotations;

namespace Coravel.Persistence.Entities;

/// <summary>
/// Represents a queued job and its execution history.
/// Used for background jobs enqueued via IPersistentQueue.
/// </summary>
public class CoravelJobHistory
{
    /// <summary>
    /// Unique identifier for this job execution.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Human-readable name of the job.
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string JobName { get; set; } = string.Empty;

    /// <summary>
    /// Full type name of the IInvocable (AssemblyQualifiedName).
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string JobType { get; set; } = string.Empty;

    /// <summary>
    /// JSON-serialized payload for IInvocableWithPayload jobs.
    /// </summary>
    public string? PayloadJson { get; set; }

    /// <summary>
    /// Current status of the job.
    /// </summary>
    public JobStatus Status { get; set; } = JobStatus.Pending;

    /// <summary>
    /// Number of execution attempts made.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Maximum number of retry attempts allowed.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// When the job was enqueued.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// When execution started (null if not yet started).
    /// </summary>
    public DateTime? StartedAtUtc { get; set; }

    /// <summary>
    /// When execution completed (null if not yet completed).
    /// </summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>
    /// When the next retry should be attempted (null if no retry scheduled).
    /// Used for exponential backoff.
    /// </summary>
    public DateTime? NextRetryAtUtc { get; set; }

    /// <summary>
    /// Last heartbeat timestamp. Used to detect stale/stuck jobs.
    /// </summary>
    public DateTime? LastHeartbeatUtc { get; set; }

    /// <summary>
    /// Error message if the job failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Full stack trace if the job failed.
    /// </summary>
    public string? StackTrace { get; set; }

    /// <summary>
    /// Identifier of the application instance processing this job.
    /// </summary>
    [MaxLength(100)]
    public string? InstanceId { get; set; }

    /// <summary>
    /// Unique key to prevent duplicate job execution.
    /// If set, only one job with this key can be pending/running at a time.
    /// </summary>
    [MaxLength(255)]
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Job priority. Lower values = higher priority.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Maximum execution time before the job is considered stuck.
    /// Stored as ticks for database compatibility.
    /// </summary>
    public long? TimeoutTicks { get; set; }

    /// <summary>
    /// Gets or sets the timeout as a TimeSpan.
    /// </summary>
    public TimeSpan? Timeout
    {
        get => TimeoutTicks.HasValue ? TimeSpan.FromTicks(TimeoutTicks.Value) : null;
        set => TimeoutTicks = value?.Ticks;
    }

    /// <summary>
    /// Concurrency token for optimistic locking.
    /// </summary>
    [ConcurrencyCheck]
    public int Version { get; set; }
}
