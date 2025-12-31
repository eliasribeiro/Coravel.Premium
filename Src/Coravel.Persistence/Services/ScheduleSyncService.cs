using Coravel.Persistence.Interfaces;
using Coravel.Scheduling.Schedule.Cron;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coravel.Persistence.Services;

/// <summary>
/// Service that synchronizes schedule definitions from code to the database.
/// </summary>
public class ScheduleSyncService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduleSyncService> _logger;

    public ScheduleSyncService(
        IServiceScopeFactory scopeFactory,
        ILogger<ScheduleSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Synchronizes the given schedule definitions to the database.
    /// New schedules are created, existing ones are updated.
    /// </summary>
    public async Task SyncSchedulesAsync(IEnumerable<ScheduleDefinition> codeSchedules, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IPersistentCoravelDbContext>();

        var scheduleList = codeSchedules.ToList();
        _logger.LogInformation("Syncing {Count} schedule definitions to database", scheduleList.Count);

        foreach (var schedule in scheduleList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var dbJob = await context.Coravel_ScheduledJobs
                    .FirstOrDefaultAsync(j => j.JobName == schedule.Name, cancellationToken);

                if (dbJob == null)
                {
                    // Create new schedule
                    dbJob = new Entities.CoravelScheduledJob
                    {
                        Id = Guid.NewGuid(),
                        JobName = schedule.Name,
                        JobType = schedule.InvocableType.AssemblyQualifiedName
                            ?? schedule.InvocableType.FullName
                            ?? schedule.InvocableType.Name,
                        CronExpression = schedule.CronExpression,
                        IntervalSeconds = schedule.IntervalSeconds,
                        IsActive = true,
                        MaxRetries = schedule.MaxRetries,
                        RetryDelayTicks = schedule.RetryDelay.Ticks,
                        PreventOverlapping = schedule.PreventOverlapping,
                        TimeZoneId = schedule.TimeZone?.Id,
                        TimeoutTicks = schedule.Timeout?.Ticks,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    dbJob.NextExecutionUtc = CalculateNextExecution(dbJob);
                    context.Coravel_ScheduledJobs.Add(dbJob);

                    _logger.LogInformation(
                        "Created scheduled job {JobName} ({JobType}). Next execution: {NextExecution}",
                        dbJob.JobName, schedule.InvocableType.Name, dbJob.NextExecutionUtc);
                }
                else
                {
                    // Update existing schedule
                    dbJob.JobType = schedule.InvocableType.AssemblyQualifiedName
                        ?? schedule.InvocableType.FullName
                        ?? schedule.InvocableType.Name;
                    dbJob.CronExpression = schedule.CronExpression;
                    dbJob.IntervalSeconds = schedule.IntervalSeconds;
                    dbJob.MaxRetries = schedule.MaxRetries;
                    dbJob.RetryDelayTicks = schedule.RetryDelay.Ticks;
                    dbJob.PreventOverlapping = schedule.PreventOverlapping;
                    dbJob.TimeZoneId = schedule.TimeZone?.Id;
                    dbJob.TimeoutTicks = schedule.Timeout?.Ticks;
                    dbJob.UpdatedAtUtc = DateTime.UtcNow;

                    // Recalculate next execution if not currently running
                    if (dbJob.InstanceLockId == null)
                    {
                        dbJob.NextExecutionUtc = CalculateNextExecution(dbJob);
                    }

                    _logger.LogDebug(
                        "Updated scheduled job {JobName}. Next execution: {NextExecution}",
                        dbJob.JobName, dbJob.NextExecutionUtc);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing schedule {ScheduleName}", schedule.Name);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Schedule sync completed");
    }

    /// <summary>
    /// Calculates the next execution time for a scheduled job.
    /// </summary>
    public static DateTime? CalculateNextExecution(Entities.CoravelScheduledJob job)
    {
        if (!job.IsActive)
            return null;

        var now = DateTime.UtcNow;
        var timeZone = job.GetTimeZone();

        if (!string.IsNullOrEmpty(job.CronExpression))
        {
            try
            {
                var cronExpression = new CronExpression(job.CronExpression);
                var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, timeZone);

                // Find the next occurrence
                // CronExpression works with local time, so we need to convert
                var nextLocal = FindNextCronOccurrence(cronExpression, localNow);
                if (nextLocal.HasValue)
                {
                    return TimeZoneInfo.ConvertTimeToUtc(nextLocal.Value, timeZone);
                }
            }
            catch (Exception)
            {
                // Invalid cron expression, return null
                return null;
            }
        }
        else if (job.IntervalSeconds.HasValue)
        {
            // For interval-based scheduling, next execution is now + interval
            // Unless there's a last execution, then use that as the base
            var baseTime = job.LastExecutionUtc ?? now;
            return baseTime.AddSeconds(job.IntervalSeconds.Value);
        }

        return null;
    }

    private static DateTime? FindNextCronOccurrence(CronExpression cron, DateTime fromTime)
    {
        // Start from the next minute
        var candidate = fromTime.AddMinutes(1);
        candidate = new DateTime(candidate.Year, candidate.Month, candidate.Day,
            candidate.Hour, candidate.Minute, 0);

        // Search for up to 1 year
        var maxTime = fromTime.AddYears(1);

        while (candidate < maxTime)
        {
            if (cron.IsDue(candidate))
            {
                return candidate;
            }
            candidate = candidate.AddMinutes(1);
        }

        return null;
    }
}
