using Coravel.Invocable;
using Coravel.Persistence.Entities;
using Coravel.Persistence.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coravel.Persistence.Services;

/// <summary>
/// Persistent scheduler implementation that stores schedules in the database.
/// </summary>
public class PersistentScheduler : IPersistentScheduler, IPersistentSchedulerConfiguration
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PersistentScheduler> _logger;
    private readonly List<ScheduleDefinition> _schedules = new();
    private Action<Exception, CoravelScheduledJob>? _errorHandler;

    public PersistentScheduler(
        IServiceScopeFactory scopeFactory,
        ILogger<PersistentScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public IPersistentScheduleInterval Schedule<T>() where T : IInvocable
    {
        return Schedule<T>(typeof(T).Name);
    }

    /// <inheritdoc />
    public IPersistentScheduleInterval Schedule<T>(string name) where T : IInvocable
    {
        var definition = new ScheduleDefinition
        {
            Name = name,
            InvocableType = typeof(T)
        };
        _schedules.Add(definition);
        return new ScheduleBuilder(definition);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CoravelScheduledJob>> GetScheduledJobsAsync()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IPersistentCoravelDbContext>();

        return await context.Coravel_ScheduledJobs
            .AsNoTracking()
            .OrderBy(j => j.JobName)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CoravelScheduledJobHistory>> GetJobHistoryAsync(Guid scheduledJobId, int limit = 100)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IPersistentCoravelDbContext>();

        return await context.Coravel_ScheduledJobHistory
            .AsNoTracking()
            .Where(h => h.ScheduledJobId == scheduledJobId)
            .OrderByDescending(h => h.ScheduledAtUtc)
            .Take(limit)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task SetJobActiveAsync(string jobName, bool isActive)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IPersistentCoravelDbContext>();

        var job = await context.Coravel_ScheduledJobs
            .FirstOrDefaultAsync(j => j.JobName == jobName);

        if (job != null)
        {
            job.IsActive = isActive;
            job.UpdatedAtUtc = DateTime.UtcNow;
            job.Version++;

            if (isActive)
            {
                job.NextExecutionUtc = ScheduleSyncService.CalculateNextExecution(job);
            }
            else
            {
                job.NextExecutionUtc = null;
            }

            await context.SaveChangesAsync();

            _logger.LogInformation(
                "Scheduled job {JobName} set to {Status}",
                jobName, isActive ? "active" : "inactive");
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ScheduleDefinition> GetRegisteredSchedules() => _schedules.AsReadOnly();

    /// <inheritdoc />
    public IPersistentSchedulerConfiguration OnError(Action<Exception, CoravelScheduledJob> errorHandler)
    {
        _errorHandler = errorHandler;
        return this;
    }

    internal Action<Exception, CoravelScheduledJob>? GetErrorHandler() => _errorHandler;

    /// <summary>
    /// Builder class for fluent schedule configuration.
    /// </summary>
    private class ScheduleBuilder : IPersistentScheduleInterval, IPersistentScheduleConfiguration
    {
        private readonly ScheduleDefinition _definition;

        public ScheduleBuilder(ScheduleDefinition definition)
        {
            _definition = definition;
        }

        public IPersistentScheduleConfiguration EverySecond()
        {
            _definition.IntervalSeconds = 1;
            return this;
        }

        public IPersistentScheduleConfiguration EverySeconds(int seconds)
        {
            _definition.IntervalSeconds = seconds;
            return this;
        }

        public IPersistentScheduleConfiguration EveryMinute()
        {
            _definition.CronExpression = "* * * * *";
            return this;
        }

        public IPersistentScheduleConfiguration EveryMinutes(int minutes)
        {
            _definition.CronExpression = $"*/{minutes} * * * *";
            return this;
        }

        public IPersistentScheduleConfiguration Hourly()
        {
            _definition.CronExpression = "0 * * * *";
            return this;
        }

        public IPersistentScheduleConfiguration HourlyAt(int minute)
        {
            _definition.CronExpression = $"{minute} * * * *";
            return this;
        }

        public IPersistentScheduleConfiguration Daily()
        {
            _definition.CronExpression = "0 0 * * *";
            return this;
        }

        public IPersistentScheduleConfiguration DailyAt(int hour, int minute)
        {
            _definition.CronExpression = $"{minute} {hour} * * *";
            return this;
        }

        public IPersistentScheduleConfiguration Weekly()
        {
            _definition.CronExpression = "0 0 * * 0";
            return this;
        }

        public IPersistentScheduleConfiguration Monthly()
        {
            _definition.CronExpression = "0 0 1 * *";
            return this;
        }

        public IPersistentScheduleConfiguration Cron(string cronExpression)
        {
            _definition.CronExpression = cronExpression;
            return this;
        }

        public IPersistentScheduleConfiguration PreventOverlapping()
        {
            _definition.PreventOverlapping = true;
            return this;
        }

        public IPersistentScheduleConfiguration Zoned(TimeZoneInfo timeZone)
        {
            _definition.TimeZone = timeZone;
            return this;
        }

        public IPersistentScheduleConfiguration WithRetries(int maxRetries)
        {
            _definition.MaxRetries = maxRetries;
            return this;
        }

        public IPersistentScheduleConfiguration WithRetryDelay(TimeSpan delay)
        {
            _definition.RetryDelay = delay;
            return this;
        }

        public IPersistentScheduleConfiguration WithTimeout(TimeSpan timeout)
        {
            _definition.Timeout = timeout;
            return this;
        }
    }
}
