using Coravel.Persistence.Configuration;
using Coravel.Persistence.Entities;
using Coravel.Persistence.Interfaces;
using Coravel.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coravel.Persistence.HostedServices;

/// <summary>
/// Background service that recovers stale/stuck jobs.
/// Jobs are considered stale if they are in Running status but haven't
/// updated their heartbeat within the configured timeout.
/// </summary>
public class StaleJobRecoveryHost : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PersistentCoravelOptions _options;
    private readonly ILogger<StaleJobRecoveryHost> _logger;

    public StaleJobRecoveryHost(
        IServiceScopeFactory scopeFactory,
        IOptions<PersistentCoravelOptions> options,
        ILogger<StaleJobRecoveryHost> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Stale Job Recovery Host started. Check interval: {Interval}s, Stale timeout: {Timeout}s",
            _options.StaleJobCheckInterval.TotalSeconds,
            _options.StaleJobTimeout.TotalSeconds);

        // Initial delay to let jobs start
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RecoverStaleJobsAsync(stoppingToken);
                await RecoverStaleScheduledJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in stale job recovery loop");
            }

            await Task.Delay(_options.StaleJobCheckInterval, stoppingToken);
        }

        _logger.LogInformation("Stale Job Recovery Host stopped");
    }

    private async Task RecoverStaleJobsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IPersistentCoravelDbContext>();

        var staleThreshold = DateTime.UtcNow.Subtract(_options.StaleJobTimeout);

        var staleJobs = await context.Coravel_JobHistory
            .Where(j => j.Status == JobStatus.Running
                && j.LastHeartbeatUtc != null
                && j.LastHeartbeatUtc < staleThreshold)
            .ToListAsync(cancellationToken);

        foreach (var job in staleJobs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (job.Attempts < job.MaxAttempts)
                {
                    // Schedule for retry
                    job.Status = JobStatus.Pending;
                    job.NextRetryAtUtc = DateTime.UtcNow;
                    job.InstanceId = null;
                    job.ErrorMessage = "Job was recovered after heartbeat timeout";
                    job.Version++;

                    _logger.LogWarning(
                        "Recovered stale job {JobId} ({JobName}). Last heartbeat: {LastHeartbeat}. Scheduling retry.",
                        job.Id, job.JobName, job.LastHeartbeatUtc);
                }
                else
                {
                    // No more retries, mark as failed
                    job.Status = JobStatus.Failed;
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.ErrorMessage = "Job exceeded heartbeat timeout after all retry attempts";
                    job.Version++;

                    _logger.LogError(
                        "Stale job {JobId} ({JobName}) failed after {Attempts} attempts. Last heartbeat: {LastHeartbeat}",
                        job.Id, job.JobName, job.Attempts, job.LastHeartbeatUtc);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recovering stale job {JobId}", job.Id);
            }
        }

        if (staleJobs.Any())
        {
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Recovered {Count} stale jobs", staleJobs.Count);
        }
    }

    private async Task RecoverStaleScheduledJobsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IPersistentCoravelDbContext>();

        var staleThreshold = DateTime.UtcNow.Subtract(_options.StaleJobTimeout);

        // Recover stale scheduled job history entries
        var staleExecutions = await context.Coravel_ScheduledJobHistory
            .Where(h => h.Status == JobStatus.Running
                && h.LastHeartbeatUtc != null
                && h.LastHeartbeatUtc < staleThreshold)
            .ToListAsync(cancellationToken);

        foreach (var execution in staleExecutions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            execution.Status = JobStatus.Failed;
            execution.CompletedAtUtc = DateTime.UtcNow;
            execution.ErrorMessage = "Execution exceeded heartbeat timeout";
            execution.Version++;

            _logger.LogWarning(
                "Recovered stale scheduled job execution {ExecutionId}. Last heartbeat: {LastHeartbeat}",
                execution.Id, execution.LastHeartbeatUtc);
        }

        // Release stale locks on scheduled jobs
        var staleLocks = await context.Coravel_ScheduledJobs
            .Where(j => j.LockedUntilUtc != null && j.LockedUntilUtc < DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var job in staleLocks)
        {
            job.InstanceLockId = null;
            job.LockedUntilUtc = null;
            job.Version++;

            _logger.LogDebug(
                "Released stale lock on scheduled job {JobName}. Lock expired at: {LockedUntil}",
                job.JobName, job.LockedUntilUtc);
        }

        if (staleExecutions.Any() || staleLocks.Any())
        {
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
