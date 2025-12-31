namespace Coravel.Persistence.Entities;

/// <summary>
/// Represents the current status of a job execution.
/// </summary>
public enum JobStatus
{
    /// <summary>
    /// Job is waiting to be processed.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Job is currently being executed.
    /// </summary>
    Running = 1,

    /// <summary>
    /// Job completed successfully.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Job failed after exhausting all retry attempts.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// Job was cancelled before completion.
    /// </summary>
    Cancelled = 4
}
