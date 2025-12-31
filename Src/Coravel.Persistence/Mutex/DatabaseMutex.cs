using Coravel.Persistence.Configuration;
using Coravel.Persistence.Interfaces;
using Coravel.Persistence.Services;
using Coravel.Scheduling.Schedule.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coravel.Persistence.Mutex;

/// <summary>
/// Database-backed mutex implementation using optimistic concurrency.
/// Uses EF Core's concurrency tokens to detect conflicts.
/// </summary>
public class DatabaseMutex : IMutex
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PersistentCoravelOptions _options;
    private readonly ILogger<DatabaseMutex> _logger;

    public DatabaseMutex(
        IServiceScopeFactory scopeFactory,
        IOptions<PersistentCoravelOptions> options,
        ILogger<DatabaseMutex> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Attempts to acquire a lock for the given key.
    /// </summary>
    /// <param name="resourceId">The resource identifier (job name).</param>
    /// <param name="timeoutMinutes">Lock timeout in minutes.</param>
    /// <returns>True if the lock was acquired, false otherwise.</returns>
    public bool TryGetLock(string resourceId, int timeoutMinutes)
    {
        return TryGetLockAsync(resourceId, TimeSpan.FromMinutes(timeoutMinutes))
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Releases a lock for the given key.
    /// </summary>
    /// <param name="resourceId">The resource identifier (job name).</param>
    public void Release(string resourceId)
    {
        ReleaseLockAsync(resourceId)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Attempts to acquire a lock asynchronously.
    /// </summary>
    public async Task<bool> TryGetLockAsync(string resourceId, TimeSpan timeout)
    {
        for (int attempt = 0; attempt < _options.MaxClaimRetries; attempt++)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<IPersistentCoravelDbContext>();

                var job = await context.Coravel_ScheduledJobs
                    .FirstOrDefaultAsync(j => j.JobName == resourceId
                        && (j.LockedUntilUtc == null || j.LockedUntilUtc < DateTime.UtcNow));

                if (job == null)
                {
                    _logger.LogDebug("Lock not available for {ResourceId}: job not found or already locked", resourceId);
                    return false;
                }

                // Try to acquire the lock
                job.InstanceLockId = _options.InstanceId;
                job.LockedUntilUtc = DateTime.UtcNow.Add(timeout);
                job.Version++;

                await context.SaveChangesAsync();

                _logger.LogDebug("Lock acquired for {ResourceId} by instance {InstanceId}", resourceId, _options.InstanceId);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another instance acquired the lock first
                if (attempt < _options.MaxClaimRetries - 1)
                {
                    var jitter = BackoffCalculator.GetJitter(10, 50);
                    await Task.Delay(jitter);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error acquiring lock for {ResourceId}", resourceId);
                return false;
            }
        }

        _logger.LogDebug("Failed to acquire lock for {ResourceId} after {MaxRetries} attempts", resourceId, _options.MaxClaimRetries);
        return false;
    }

    /// <summary>
    /// Releases a lock asynchronously.
    /// </summary>
    public async Task ReleaseLockAsync(string resourceId)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<IPersistentCoravelDbContext>();

            var job = await context.Coravel_ScheduledJobs
                .FirstOrDefaultAsync(j => j.JobName == resourceId && j.InstanceLockId == _options.InstanceId);

            if (job != null)
            {
                job.InstanceLockId = null;
                job.LockedUntilUtc = null;
                job.Version++;

                await context.SaveChangesAsync();
                _logger.LogDebug("Lock released for {ResourceId} by instance {InstanceId}", resourceId, _options.InstanceId);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            // Lock was already released or taken by another instance, ignore
            _logger.LogDebug("Lock release conflict for {ResourceId}, ignoring", resourceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing lock for {ResourceId}", resourceId);
        }
    }
}
