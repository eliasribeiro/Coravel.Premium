using Coravel.Invocable;
using Coravel.Scheduling.Schedule.Interfaces;

namespace Coravel.Persistence.Interfaces;

/// <summary>
/// Options for configuring a scheduled job.
/// </summary>
public class PersistentScheduleOptions
{
    /// <summary>
    /// Maximum number of retry attempts on failure.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Base delay between retry attempts.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum execution time before the job is considered stuck.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Whether to prevent overlapping executions.
    /// </summary>
    public bool PreventOverlapping { get; set; } = true;

    /// <summary>
    /// Timezone for cron expression evaluation.
    /// </summary>
    public TimeZoneInfo? TimeZone { get; set; }
}

/// <summary>
/// Represents a schedule definition for syncing with the database.
/// </summary>
public class ScheduleDefinition
{
    /// <summary>
    /// Unique name of the scheduled job.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The invocable type to execute.
    /// </summary>
    public Type InvocableType { get; set; } = null!;

    /// <summary>
    /// Cron expression for scheduling (null if using interval).
    /// </summary>
    public string? CronExpression { get; set; }

    /// <summary>
    /// Interval in seconds between executions (null if using cron).
    /// </summary>
    public int? IntervalSeconds { get; set; }

    /// <summary>
    /// Maximum number of retry attempts.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether to prevent overlapping executions.
    /// </summary>
    public bool PreventOverlapping { get; set; } = true;

    /// <summary>
    /// Timezone for cron expression evaluation.
    /// </summary>
    public TimeZoneInfo? TimeZone { get; set; }

    /// <summary>
    /// Maximum execution time.
    /// </summary>
    public TimeSpan? Timeout { get; set; }
}

/// <summary>
/// Builder interface for configuring a scheduled job's timing.
/// </summary>
public interface IPersistentScheduleInterval
{
    /// <summary>
    /// Schedule to run every second.
    /// </summary>
    IPersistentScheduleConfiguration EverySecond();

    /// <summary>
    /// Schedule to run every N seconds.
    /// </summary>
    IPersistentScheduleConfiguration EverySeconds(int seconds);

    /// <summary>
    /// Schedule to run every minute.
    /// </summary>
    IPersistentScheduleConfiguration EveryMinute();

    /// <summary>
    /// Schedule to run every N minutes.
    /// </summary>
    IPersistentScheduleConfiguration EveryMinutes(int minutes);

    /// <summary>
    /// Schedule to run every hour.
    /// </summary>
    IPersistentScheduleConfiguration Hourly();

    /// <summary>
    /// Schedule to run at a specific minute each hour.
    /// </summary>
    IPersistentScheduleConfiguration HourlyAt(int minute);

    /// <summary>
    /// Schedule to run daily at midnight.
    /// </summary>
    IPersistentScheduleConfiguration Daily();

    /// <summary>
    /// Schedule to run daily at a specific time.
    /// </summary>
    IPersistentScheduleConfiguration DailyAt(int hour, int minute);

    /// <summary>
    /// Schedule to run weekly on Sunday at midnight.
    /// </summary>
    IPersistentScheduleConfiguration Weekly();

    /// <summary>
    /// Schedule to run monthly on the 1st at midnight.
    /// </summary>
    IPersistentScheduleConfiguration Monthly();

    /// <summary>
    /// Schedule using a cron expression.
    /// </summary>
    IPersistentScheduleConfiguration Cron(string cronExpression);
}

/// <summary>
/// Builder interface for additional schedule configuration.
/// </summary>
public interface IPersistentScheduleConfiguration
{
    /// <summary>
    /// Prevents overlapping executions.
    /// </summary>
    IPersistentScheduleConfiguration PreventOverlapping();

    /// <summary>
    /// Sets the timezone for schedule evaluation.
    /// </summary>
    IPersistentScheduleConfiguration Zoned(TimeZoneInfo timeZone);

    /// <summary>
    /// Sets the maximum number of retry attempts.
    /// </summary>
    IPersistentScheduleConfiguration WithRetries(int maxRetries);

    /// <summary>
    /// Sets the delay between retry attempts.
    /// </summary>
    IPersistentScheduleConfiguration WithRetryDelay(TimeSpan delay);

    /// <summary>
    /// Sets the maximum execution time.
    /// </summary>
    IPersistentScheduleConfiguration WithTimeout(TimeSpan timeout);
}

/// <summary>
/// Persistent scheduler interface for scheduled job management.
/// Scheduled jobs are synced to the database on application startup.
/// </summary>
public interface IPersistentScheduler
{
    /// <summary>
    /// Schedules an invocable for recurring execution.
    /// </summary>
    /// <typeparam name="T">The invocable type.</typeparam>
    /// <returns>A builder for configuring the schedule.</returns>
    IPersistentScheduleInterval Schedule<T>() where T : IInvocable;

    /// <summary>
    /// Schedules an invocable with a custom name for recurring execution.
    /// </summary>
    /// <typeparam name="T">The invocable type.</typeparam>
    /// <param name="name">Custom name for the scheduled job.</param>
    /// <returns>A builder for configuring the schedule.</returns>
    IPersistentScheduleInterval Schedule<T>(string name) where T : IInvocable;

    /// <summary>
    /// Gets all scheduled job definitions from the database.
    /// </summary>
    Task<IReadOnlyList<Entities.CoravelScheduledJob>> GetScheduledJobsAsync();

    /// <summary>
    /// Gets the execution history for a scheduled job.
    /// </summary>
    /// <param name="scheduledJobId">The scheduled job ID.</param>
    /// <param name="limit">Maximum number of records to return.</param>
    Task<IReadOnlyList<Entities.CoravelScheduledJobHistory>> GetJobHistoryAsync(Guid scheduledJobId, int limit = 100);

    /// <summary>
    /// Enables or disables a scheduled job.
    /// </summary>
    /// <param name="jobName">The job name.</param>
    /// <param name="isActive">Whether the job should be active.</param>
    Task SetJobActiveAsync(string jobName, bool isActive);

    /// <summary>
    /// Gets all schedule definitions registered in code.
    /// </summary>
    IReadOnlyList<ScheduleDefinition> GetRegisteredSchedules();
}

/// <summary>
/// Configuration interface for the persistent scheduler.
/// </summary>
public interface IPersistentSchedulerConfiguration
{
    /// <summary>
    /// Configures an error handler for failed scheduled jobs.
    /// </summary>
    /// <param name="errorHandler">The error handler.</param>
    /// <returns>The configuration for chaining.</returns>
    IPersistentSchedulerConfiguration OnError(Action<Exception, Entities.CoravelScheduledJob> errorHandler);
}
