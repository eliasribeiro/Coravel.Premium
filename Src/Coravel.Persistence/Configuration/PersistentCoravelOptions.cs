namespace Coravel.Persistence.Configuration;

/// <summary>
/// Configuration options for Coravel persistence.
/// </summary>
public class PersistentCoravelOptions
{
    /// <summary>
    /// How often to poll for pending jobs.
    /// Default: 5 seconds.
    /// </summary>
    public TimeSpan JobPollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How often to update the heartbeat for running jobs.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a job can go without a heartbeat before being considered stale.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan StaleJobTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How often to check for stale jobs.
    /// Default: 1 minute.
    /// </summary>
    public TimeSpan StaleJobCheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Maximum number of jobs to process concurrently per instance.
    /// Default: 5.
    /// </summary>
    public int MaxConcurrentJobs { get; set; } = 5;

    /// <summary>
    /// Default timeout for job lock acquisition.
    /// Default: 24 hours.
    /// </summary>
    public TimeSpan DefaultLockTimeout { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Maximum number of retries when claiming a job (optimistic concurrency conflicts).
    /// Default: 5.
    /// </summary>
    public int MaxClaimRetries { get; set; } = 5;

    /// <summary>
    /// Whether to automatically run migrations on startup.
    /// Default: false.
    /// </summary>
    public bool AutoMigrate { get; set; } = false;

    /// <summary>
    /// Unique identifier for this application instance.
    /// Default: auto-generated GUID.
    /// </summary>
    public string InstanceId { get; set; } = Guid.NewGuid().ToString();
}
