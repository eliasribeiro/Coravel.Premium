using System.ComponentModel.DataAnnotations;

namespace Coravel.Persistence.Entities;

/// <summary>
/// Represents a single execution of a scheduled job.
/// </summary>
public class CoravelScheduledJobHistory
{
    /// <summary>
    /// Unique identifier for this execution record.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Reference to the scheduled job that was executed.
    /// </summary>
    public Guid ScheduledJobId { get; set; }

    /// <summary>
    /// Navigation property to the scheduled job.
    /// </summary>
    public CoravelScheduledJob? ScheduledJob { get; set; }

    /// <summary>
    /// When this execution was scheduled to run.
    /// </summary>
    public DateTime ScheduledAtUtc { get; set; }

    /// <summary>
    /// When execution actually started.
    /// </summary>
    public DateTime? StartedAtUtc { get; set; }

    /// <summary>
    /// When execution completed.
    /// </summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>
    /// Execution duration (stored as ticks).
    /// </summary>
    public long? DurationTicks { get; set; }

    /// <summary>
    /// Gets or sets the duration as a TimeSpan.
    /// </summary>
    public TimeSpan? Duration
    {
        get => DurationTicks.HasValue ? TimeSpan.FromTicks(DurationTicks.Value) : null;
        set => DurationTicks = value?.Ticks;
    }

    /// <summary>
    /// Current status of this execution.
    /// </summary>
    public JobStatus Status { get; set; } = JobStatus.Pending;

    /// <summary>
    /// Number of attempts for this execution.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Error message if the execution failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Full stack trace if the execution failed.
    /// </summary>
    public string? StackTrace { get; set; }

    /// <summary>
    /// Instance ID that processed this execution.
    /// </summary>
    [MaxLength(100)]
    public string? InstanceId { get; set; }

    /// <summary>
    /// Last heartbeat timestamp for stale detection.
    /// </summary>
    public DateTime? LastHeartbeatUtc { get; set; }

    /// <summary>
    /// Concurrency token for optimistic locking.
    /// </summary>
    [ConcurrencyCheck]
    public int Version { get; set; }
}
