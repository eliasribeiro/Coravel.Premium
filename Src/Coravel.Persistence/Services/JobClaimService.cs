using Coravel.Persistence.Configuration;
using Coravel.Persistence.Entities;
using Coravel.Persistence.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coravel.Persistence.Services;

/// <summary>
/// Service for claiming jobs using optimistic concurrency.
/// Implements the claim pattern with retry loop and jitter.
/// </summary>
public class JobClaimService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PersistentCoravelOptions _options;
    private readonly ILogger<JobClaimService> _logger;

    public JobClaimService(
        IServiceScopeFactory scopeFactory,
        IOptions<PersistentCoravelOptions> options,
        ILogger<JobClaimService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Attempts to claim the next available job from the queue.
    /// Uses optimistic concurrency with retry loop.
    /// </summary>
    /// <returns>The claimed job, or null if no jobs are available.</returns>
    public async Task<CoravelJobHistory?> ClaimNextJobAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        for (int attempt = 0; attempt < _options.MaxClaimRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<IPersistentCoravelDbContext>();

                // Find the next available job
                var job = await context.Coravel_JobHistory
                    .Where(j => j.Status == JobStatus.Pending
                        && (j.NextRetryAtUtc == null || j.NextRetryAtUtc <= now))
                    .OrderBy(j => j.Priority)
                    .ThenBy(j => j.CreatedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);

                if (job == null)
                {
                    return null; // No jobs available
                }

                // Try to claim the job (optimistic concurrency)
                job.Status = JobStatus.Running;
                job.InstanceId = _options.InstanceId;
                job.StartedAtUtc = now;
                job.LastHeartbeatUtc = now;
                job.Attempts++;
                job.Version++;

                await context.SaveChangesAsync(cancellationToken);

                _logger.LogDebug(
                    "Claimed job {JobId} ({JobName}) - Attempt {Attempt}",
                    job.Id, job.JobName, job.Attempts);

                return job;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another instance claimed this job, try again
                if (attempt < _options.MaxClaimRetries - 1)
                {
                    var jitter = BackoffCalculator.GetJitter(5, 25);
                    await Task.Delay(jitter, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error claiming job on attempt {Attempt}", attempt + 1);
                if (attempt == _options.MaxClaimRetries - 1)
                    throw;
            }
        }

        return null;
    }

    /// <summary>
    /// Marks a job as completed successfully.
    /// </summary>
    public async Task CompleteJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IPersistentCoravelDbContext>();

        var job = await context.Coravel_JobHistory
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job != null)
        {
            job.Status = JobStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.Version++;

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Job {JobId} ({JobName}) completed successfully",
                job.Id, job.JobName);
        }
    }

    /// <summary>
    /// Marks a job as failed and schedules a retry if attempts remain.
    /// </summary>
    public async Task FailJobAsync(
        Guid jobId,
        Exception exception,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IPersistentCoravelDbContext>();

        var job = await context.Coravel_JobHistory
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job == null)
            return;

        job.ErrorMessage = exception.Message;
        job.StackTrace = exception.StackTrace;
        job.Version++;

        if (job.Attempts < job.MaxAttempts)
        {
            // Schedule retry with exponential backoff
            job.Status = JobStatus.Pending;
            job.NextRetryAtUtc = BackoffCalculator.CalculateNextRetry(job.Attempts, retryDelay);
            job.InstanceId = null;

            _logger.LogWarning(
                exception,
                "Job {JobId} ({JobName}) failed on attempt {Attempt}/{MaxAttempts}. Retry scheduled for {NextRetry}",
                job.Id, job.JobName, job.Attempts, job.MaxAttempts, job.NextRetryAtUtc);
        }
        else
        {
            // No more retries, mark as failed
            job.Status = JobStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;

            _logger.LogError(
                exception,
                "Job {JobId} ({JobName}) failed after {Attempts} attempts",
                job.Id, job.JobName, job.Attempts);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Updates the heartbeat timestamp for a running job.
    /// </summary>
    public async Task UpdateHeartbeatAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<IPersistentCoravelDbContext>();

            var job = await context.Coravel_JobHistory
                .FirstOrDefaultAsync(j => j.Id == jobId && j.Status == JobStatus.Running, cancellationToken);

            if (job != null)
            {
                job.LastHeartbeatUtc = DateTime.UtcNow;
                job.Version++;
                await context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            // Ignore concurrency conflicts for heartbeat updates
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update heartbeat for job {JobId}", jobId);
        }
    }

    /// <summary>
    /// Cancels a pending job.
    /// </summary>
    public async Task<bool> CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IPersistentCoravelDbContext>();

        var job = await context.Coravel_JobHistory
            .FirstOrDefaultAsync(j => j.Id == jobId && j.Status == JobStatus.Pending, cancellationToken);

        if (job == null)
            return false;

        job.Status = JobStatus.Cancelled;
        job.CompletedAtUtc = DateTime.UtcNow;
        job.Version++;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Job {JobId} ({JobName}) was cancelled", job.Id, job.JobName);
        return true;
    }
}
