using System.ComponentModel.DataAnnotations;

namespace Coravel.Persistence.Entities;

/// <summary>
/// Represents a scheduled job definition.
/// Jobs are synced from code on application startup.
/// </summary>
public class CoravelScheduledJob
{
    /// <summary>
    /// Unique identifier for this scheduled job.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Unique name of the scheduled job.
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
    /// Cron expression for scheduling (null if using interval).
    /// </summary>
    [MaxLength(100)]
    public string? CronExpression { get; set; }

    /// <summary>
    /// Interval in seconds between executions (null if using cron).
    /// </summary>
    public int? IntervalSeconds { get; set; }

    /// <summary>
    /// Whether this scheduled job is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When this job was last executed.
    /// </summary>
    public DateTime? LastExecutionUtc { get; set; }

    /// <summary>
    /// When this job should next execute.
    /// </summary>
    public DateTime? NextExecutionUtc { get; set; }

    /// <summary>
    /// Maximum execution time (stored as ticks).
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
    /// Maximum number of retry attempts on failure.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts (stored as ticks).
    /// </summary>
    public long RetryDelayTicks { get; set; } = TimeSpan.FromSeconds(30).Ticks;

    /// <summary>
    /// Gets or sets the retry delay as a TimeSpan.
    /// </summary>
    public TimeSpan RetryDelay
    {
        get => TimeSpan.FromTicks(RetryDelayTicks);
        set => RetryDelayTicks = value.Ticks;
    }

    /// <summary>
    /// Whether to prevent overlapping executions.
    /// </summary>
    public bool PreventOverlapping { get; set; } = true;

    /// <summary>
    /// Timezone ID for cron expression evaluation.
    /// </summary>
    [MaxLength(100)]
    public string? TimeZoneId { get; set; }

    /// <summary>
    /// JSON-serialized constructor parameters.
    /// </summary>
    public string? ParametersJson { get; set; }

    /// <summary>
    /// Instance ID that currently holds the lock.
    /// </summary>
    [MaxLength(100)]
    public string? InstanceLockId { get; set; }

    /// <summary>
    /// When the current lock expires.
    /// </summary>
    public DateTime? LockedUntilUtc { get; set; }

    /// <summary>
    /// When this scheduled job was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// When this scheduled job was last updated.
    /// </summary>
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>
    /// Concurrency token for optimistic locking.
    /// </summary>
    [ConcurrencyCheck]
    public int Version { get; set; }

    /// <summary>
    /// Gets the TimeZoneInfo from TimeZoneId, or UTC if not set.
    /// </summary>
    public TimeZoneInfo GetTimeZone()
    {
        if (string.IsNullOrEmpty(TimeZoneId))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }
}
