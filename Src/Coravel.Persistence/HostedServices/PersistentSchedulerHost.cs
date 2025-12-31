using System.Text.Json;
using Coravel.Invocable;
using Coravel.Persistence.Configuration;
using Coravel.Persistence.Entities;
using Coravel.Persistence.Interfaces;
using Coravel.Persistence.Mutex;
using Coravel.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coravel.Persistence.HostedServices;

/// <summary>
/// Background service that executes scheduled jobs from the database.
/// </summary>
public class PersistentSchedulerHost : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PersistentScheduler _scheduler;
    private readonly ScheduleSyncService _syncService;
    private readonly DatabaseMutex _mutex;
    private readonly PersistentCoravelOptions _options;
    private readonly ILogger<PersistentSchedulerHost> _logger;

    public PersistentSchedulerHost(
        IServiceScopeFactory scopeFactory,
        IPersistentScheduler scheduler,
        ScheduleSyncService syncService,
        DatabaseMutex mutex,
        IOptions<PersistentCoravelOptions> options,
        ILogger<PersistentSchedulerHost> logger)
    {
        _scopeFactory = scopeFactory;
        _scheduler = (PersistentScheduler)scheduler;
        _syncService = syncService;
        _mutex = mutex;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Persistent Scheduler Host starting. Instance: {InstanceId}",
            _options.InstanceId);

        // Sync schedules to database on startup
        try
        {
            var schedules = _scheduler.GetRegisteredSchedules();
            await _syncService.SyncSchedulesAsync(schedules, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing schedules on startup");
        }

        _logger.LogInformation("Persistent Scheduler Host started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueSchedulesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in scheduler loop");
            }

            // Check every second for due schedules
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }

        _logger.LogInformation("Persistent Scheduler Host stopped");
    }

    private async Task ProcessDueSchedulesAsync(CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IPersistentCoravelDbContext>();

        var now = DateTime.UtcNow;

        // Find due schedules
        var dueJobs = await context.Coravel_ScheduledJobs
            .Where(j => j.IsActive
                && j.NextExecutionUtc != null
                && j.NextExecutionUtc <= now)
            .ToListAsync(stoppingToken);

        foreach (var job in dueJobs)
        {
            stoppingToken.ThrowIfCancellationRequested();

            // Try to acquire lock if prevent overlapping is enabled
            if (job.PreventOverlapping)
            {
                var lockAcquired = await _mutex.TryGetLockAsync(job.JobName, _options.DefaultLockTimeout);
                if (!lockAcquired)
                {
                    _logger.LogDebug("Could not acquire lock for {JobName}, skipping", job.JobName);
                    continue;
                }
            }

            // Execute in background
            _ = ExecuteScheduledJobAsync(job, stoppingToken);
        }
    }

    private async Task ExecuteScheduledJobAsync(CoravelScheduledJob job, CancellationToken stoppingToken)
    {
        var executionId = Guid.NewGuid();
        var startTime = DateTime.UtcNow;

        _logger.LogInformation(
            "Executing scheduled job {JobName} ({ExecutionId})",
            job.JobName, executionId);

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<IPersistentCoravelDbContext>();

            // Create history entry
            var history = new CoravelScheduledJobHistory
            {
                Id = executionId,
                ScheduledJobId = job.Id,
                ScheduledAtUtc = job.NextExecutionUtc ?? startTime,
                StartedAtUtc = startTime,
                Status = JobStatus.Running,
                Attempts = 1,
                InstanceId = _options.InstanceId,
                LastHeartbeatUtc = startTime
            };
            context.Coravel_ScheduledJobHistory.Add(history);

            // Update job's last execution and calculate next
            var dbJob = await context.Coravel_ScheduledJobs
                .FirstOrDefaultAsync(j => j.Id == job.Id, stoppingToken);

            if (dbJob != null)
            {
                dbJob.LastExecutionUtc = startTime;
                dbJob.NextExecutionUtc = ScheduleSyncService.CalculateNextExecution(dbJob);
                dbJob.Version++;
            }

            await context.SaveChangesAsync(stoppingToken);

            // Start heartbeat task
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var heartbeatTask = RunHeartbeatAsync(executionId, heartbeatCts.Token);

            try
            {
                // Create timeout cancellation
                using var timeoutCts = job.TimeoutTicks.HasValue
                    ? new CancellationTokenSource(TimeSpan.FromTicks(job.TimeoutTicks.Value))
                    : new CancellationTokenSource();
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);

                // Execute the job
                await ExecuteInvocableAsync(job, linkedCts.Token);

                // Mark as completed
                await UpdateHistoryAsync(executionId, JobStatus.Completed, null, stoppingToken);

                _logger.LogInformation(
                    "Scheduled job {JobName} completed successfully ({ExecutionId})",
                    job.JobName, executionId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // Timeout
                var ex = new TimeoutException($"Scheduled job {job.JobName} exceeded timeout");
                await UpdateHistoryAsync(executionId, JobStatus.Failed, ex, stoppingToken);
                InvokeErrorHandler(ex, job);
            }
            catch (Exception ex)
            {
                await UpdateHistoryAsync(executionId, JobStatus.Failed, ex, stoppingToken);
                InvokeErrorHandler(ex, job);

                _logger.LogError(ex, "Scheduled job {JobName} failed ({ExecutionId})", job.JobName, executionId);
            }
            finally
            {
                heartbeatCts.Cancel();
                try { await heartbeatTask; } catch { }
            }
        }
        finally
        {
            // Release lock
            if (job.PreventOverlapping)
            {
                await _mutex.ReleaseLockAsync(job.JobName);
            }
        }
    }

    private async Task ExecuteInvocableAsync(CoravelScheduledJob job, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        var jobType = Type.GetType(job.JobType);
        if (jobType == null)
        {
            throw new InvalidOperationException($"Could not resolve job type: {job.JobType}");
        }

        var invocable = scope.ServiceProvider.GetService(jobType) as IInvocable;
        if (invocable == null)
        {
            throw new InvalidOperationException($"Job type {job.JobType} is not registered or does not implement IInvocable");
        }

        // Set parameters if available
        if (!string.IsNullOrEmpty(job.ParametersJson))
        {
            // Parameters would be set here if we implement parameter injection
        }

        // Set cancellation token if applicable
        if (invocable is ICancellableInvocable cancellableInvocable)
        {
            cancellableInvocable.CancellationToken = cancellationToken;
        }

        await invocable.Invoke();
    }

    private async Task UpdateHistoryAsync(Guid executionId, JobStatus status, Exception? exception, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<IPersistentCoravelDbContext>();

            var history = await context.Coravel_ScheduledJobHistory
                .FirstOrDefaultAsync(h => h.Id == executionId, cancellationToken);

            if (history != null)
            {
                history.Status = status;
                history.CompletedAtUtc = DateTime.UtcNow;
                history.DurationTicks = (DateTime.UtcNow - (history.StartedAtUtc ?? DateTime.UtcNow)).Ticks;
                history.Version++;

                if (exception != null)
                {
                    history.ErrorMessage = exception.Message;
                    history.StackTrace = exception.StackTrace;
                }

                await context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating history for execution {ExecutionId}", executionId);
        }
    }

    private async Task RunHeartbeatAsync(Guid executionId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.HeartbeatInterval, cancellationToken);

                await using var scope = _scopeFactory.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<IPersistentCoravelDbContext>();

                var history = await context.Coravel_ScheduledJobHistory
                    .FirstOrDefaultAsync(h => h.Id == executionId && h.Status == JobStatus.Running, cancellationToken);

                if (history != null)
                {
                    history.LastHeartbeatUtc = DateTime.UtcNow;
                    history.Version++;
                    await context.SaveChangesAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error updating heartbeat for execution {ExecutionId}", executionId);
            }
        }
    }

    private void InvokeErrorHandler(Exception exception, CoravelScheduledJob job)
    {
        try
        {
            _scheduler.GetErrorHandler()?.Invoke(exception, job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in error handler for job {JobName}", job.JobName);
        }
    }
}
