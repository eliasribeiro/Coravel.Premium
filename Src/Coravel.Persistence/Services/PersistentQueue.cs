using System.Text.Json;
using Coravel.Invocable;
using Coravel.Persistence.Configuration;
using Coravel.Persistence.Entities;
using Coravel.Persistence.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coravel.Persistence.Services;

/// <summary>
/// Persistent queue implementation that stores jobs in the database.
/// </summary>
public class PersistentQueue : IPersistentQueue, IPersistentQueueConfiguration
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PersistentCoravelOptions _options;
    private readonly ILogger<PersistentQueue> _logger;
    private Action<Exception, CoravelJobHistory>? _errorHandler;

    public PersistentQueue(
        IServiceScopeFactory scopeFactory,
        IOptions<PersistentCoravelOptions> options,
        ILogger<PersistentQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Guid> QueueInvocableAsync<T>() where T : IInvocable
    {
        return await QueueInvocableAsync<T>(_ => { });
    }

    /// <inheritdoc />
    public async Task<Guid> QueueInvocableAsync<T>(Action<PersistentJobOptions> configure) where T : IInvocable
    {
        var jobOptions = new PersistentJobOptions();
        configure(jobOptions);

        return await EnqueueJobAsync(typeof(T), null, jobOptions);
    }

    /// <inheritdoc />
    public async Task<Guid> QueueInvocableWithPayloadAsync<T, TPayload>(TPayload payload)
        where T : IInvocable, IInvocableWithPayload<TPayload>
    {
        return await QueueInvocableWithPayloadAsync<T, TPayload>(payload, _ => { });
    }

    /// <inheritdoc />
    public async Task<Guid> QueueInvocableWithPayloadAsync<T, TPayload>(TPayload payload, Action<PersistentJobOptions> configure)
        where T : IInvocable, IInvocableWithPayload<TPayload>
    {
        var jobOptions = new PersistentJobOptions();
        configure(jobOptions);

        var payloadJson = JsonSerializer.Serialize(payload);
        return await EnqueueJobAsync(typeof(T), payloadJson, jobOptions);
    }

    /// <inheritdoc />
    public async Task<CoravelJobHistory?> GetJobStatusAsync(Guid jobId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IPersistentCoravelDbContext>();

        return await context.Coravel_JobHistory
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId);
    }

    /// <inheritdoc />
    public async Task<bool> CancelJobAsync(Guid jobId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IPersistentCoravelDbContext>();

        var job = await context.Coravel_JobHistory
            .FirstOrDefaultAsync(j => j.Id == jobId && j.Status == JobStatus.Pending);

        if (job == null)
            return false;

        job.Status = JobStatus.Cancelled;
        job.CompletedAtUtc = DateTime.UtcNow;
        job.Version++;

        await context.SaveChangesAsync();

        _logger.LogInformation("Job {JobId} ({JobName}) was cancelled", job.Id, job.JobName);
        return true;
    }

    /// <inheritdoc />
    public async Task<PersistentQueueMetrics> GetMetricsAsync()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IPersistentCoravelDbContext>();

        var metrics = await context.Coravel_JobHistory
            .GroupBy(_ => 1)
            .Select(g => new PersistentQueueMetrics
            {
                PendingCount = g.Count(j => j.Status == JobStatus.Pending),
                RunningCount = g.Count(j => j.Status == JobStatus.Running),
                CompletedCount = g.Count(j => j.Status == JobStatus.Completed),
                FailedCount = g.Count(j => j.Status == JobStatus.Failed)
            })
            .FirstOrDefaultAsync();

        return metrics ?? new PersistentQueueMetrics();
    }

    /// <inheritdoc />
    public IPersistentQueueConfiguration OnError(Action<Exception, CoravelJobHistory> errorHandler)
    {
        _errorHandler = errorHandler;
        return this;
    }

    internal Action<Exception, CoravelJobHistory>? GetErrorHandler() => _errorHandler;

    private async Task<Guid> EnqueueJobAsync(Type invocableType, string? payloadJson, PersistentJobOptions options)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IPersistentCoravelDbContext>();

        // Check for existing job with same idempotency key
        if (!string.IsNullOrEmpty(options.IdempotencyKey))
        {
            var existingJob = await context.Coravel_JobHistory
                .Where(j => j.IdempotencyKey == options.IdempotencyKey
                    && j.Status != JobStatus.Failed
                    && j.Status != JobStatus.Cancelled
                    && j.CreatedAtUtc > DateTime.UtcNow.AddHours(-24))
                .FirstOrDefaultAsync();

            if (existingJob != null)
            {
                _logger.LogDebug(
                    "Job with idempotency key {IdempotencyKey} already exists: {JobId}",
                    options.IdempotencyKey, existingJob.Id);
                return existingJob.Id;
            }
        }

        var job = new CoravelJobHistory
        {
            Id = Guid.NewGuid(),
            JobName = invocableType.Name,
            JobType = invocableType.AssemblyQualifiedName ?? invocableType.FullName ?? invocableType.Name,
            PayloadJson = payloadJson,
            Status = JobStatus.Pending,
            Attempts = 0,
            MaxAttempts = options.MaxRetries + 1, // +1 because first attempt is not a "retry"
            Priority = options.Priority,
            IdempotencyKey = options.IdempotencyKey,
            TimeoutTicks = options.Timeout?.Ticks,
            CreatedAtUtc = DateTime.UtcNow
        };

        context.Coravel_JobHistory.Add(job);
        await context.SaveChangesAsync();

        _logger.LogDebug(
            "Enqueued job {JobId} ({JobName}) with priority {Priority}",
            job.Id, job.JobName, job.Priority);

        return job.Id;
    }
}
