using System.Text.Json;
using Coravel.Invocable;
using Coravel.Persistence.Configuration;
using Coravel.Persistence.Entities;
using Coravel.Persistence.Interfaces;
using Coravel.Persistence.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coravel.Persistence.HostedServices;

/// <summary>
/// Background service that processes queued jobs from the database.
/// </summary>
public class PersistentJobHost : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JobClaimService _claimService;
    private readonly PersistentCoravelOptions _options;
    private readonly ILogger<PersistentJobHost> _logger;
    private readonly SemaphoreSlim _concurrencyLimiter;

    public PersistentJobHost(
        IServiceScopeFactory scopeFactory,
        JobClaimService claimService,
        IOptions<PersistentCoravelOptions> options,
        ILogger<PersistentJobHost> logger)
    {
        _scopeFactory = scopeFactory;
        _claimService = claimService;
        _options = options.Value;
        _logger = logger;
        _concurrencyLimiter = new SemaphoreSlim(_options.MaxConcurrentJobs);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Persistent Job Host started. Instance: {InstanceId}, Polling interval: {Interval}s, Max concurrent: {MaxConcurrent}",
            _options.InstanceId,
            _options.JobPollingInterval.TotalSeconds,
            _options.MaxConcurrentJobs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAvailableJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in job processing loop");
            }

            await Task.Delay(_options.JobPollingInterval, stoppingToken);
        }

        _logger.LogInformation("Persistent Job Host stopping. Waiting for running jobs to complete...");

        // Wait for all running jobs to complete
        for (int i = 0; i < _options.MaxConcurrentJobs; i++)
        {
            await _concurrencyLimiter.WaitAsync(TimeSpan.FromSeconds(30));
        }

        _logger.LogInformation("Persistent Job Host stopped");
    }

    private async Task ProcessAvailableJobsAsync(CancellationToken stoppingToken)
    {
        // Try to claim jobs up to the concurrency limit
        while (_concurrencyLimiter.CurrentCount > 0 && !stoppingToken.IsCancellationRequested)
        {
            var job = await _claimService.ClaimNextJobAsync(stoppingToken);
            if (job == null)
                break; // No more jobs available

            // Process job in background
            _ = ProcessJobAsync(job, stoppingToken);
        }
    }

    private async Task ProcessJobAsync(CoravelJobHistory job, CancellationToken stoppingToken)
    {
        await _concurrencyLimiter.WaitAsync(stoppingToken);

        try
        {
            _logger.LogInformation(
                "Processing job {JobId} ({JobName}) - Attempt {Attempt}/{MaxAttempts}",
                job.Id, job.JobName, job.Attempts, job.MaxAttempts);

            // Start heartbeat task
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var heartbeatTask = RunHeartbeatAsync(job.Id, heartbeatCts.Token);

            try
            {
                // Create cancellation token for job timeout
                using var timeoutCts = job.TimeoutTicks.HasValue
                    ? new CancellationTokenSource(TimeSpan.FromTicks(job.TimeoutTicks.Value))
                    : new CancellationTokenSource();
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);

                // Execute the job
                await ExecuteJobAsync(job, linkedCts.Token);

                // Mark as completed
                await _claimService.CompleteJobAsync(job.Id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Job {JobId} ({JobName}) was interrupted by shutdown", job.Id, job.JobName);
                throw;
            }
            catch (OperationCanceledException)
            {
                // Timeout
                var timeoutException = new TimeoutException($"Job {job.JobName} exceeded timeout");
                await _claimService.FailJobAsync(job.Id, timeoutException, TimeSpan.FromSeconds(30), stoppingToken);
                InvokeErrorHandler(timeoutException, job);
            }
            catch (Exception ex)
            {
                await _claimService.FailJobAsync(job.Id, ex, TimeSpan.FromSeconds(30), stoppingToken);
                InvokeErrorHandler(ex, job);
            }
            finally
            {
                // Stop heartbeat
                heartbeatCts.Cancel();
                try { await heartbeatTask; } catch { }
            }
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    private async Task ExecuteJobAsync(CoravelJobHistory job, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        // Resolve the invocable type
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

        // Set payload if applicable
        if (!string.IsNullOrEmpty(job.PayloadJson))
        {
            var payloadInterfaces = jobType.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IInvocableWithPayload<>))
                .ToList();

            if (payloadInterfaces.Any())
            {
                var payloadType = payloadInterfaces.First().GetGenericArguments()[0];
                var payload = JsonSerializer.Deserialize(job.PayloadJson, payloadType);

                var payloadProperty = jobType.GetProperty("Payload");
                payloadProperty?.SetValue(invocable, payload);
            }
        }

        // Set cancellation token if applicable
        if (invocable is ICancellableInvocable cancellableInvocable)
        {
            cancellableInvocable.CancellationToken = cancellationToken;
        }

        // Execute
        await invocable.Invoke();
    }

    private async Task RunHeartbeatAsync(Guid jobId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.HeartbeatInterval, cancellationToken);
                await _claimService.UpdateHeartbeatAsync(jobId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error updating heartbeat for job {JobId}", jobId);
            }
        }
    }

    private void InvokeErrorHandler(Exception exception, CoravelJobHistory job)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var queue = scope.ServiceProvider.GetService<IPersistentQueue>() as PersistentQueue;
            queue?.GetErrorHandler()?.Invoke(exception, job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in error handler for job {JobId}", job.Id);
        }
    }
}
