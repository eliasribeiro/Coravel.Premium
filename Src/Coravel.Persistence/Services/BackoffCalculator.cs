namespace Coravel.Persistence.Services;

/// <summary>
/// Calculates retry delays using exponential backoff with jitter.
/// </summary>
public static class BackoffCalculator
{
    private static readonly Random _random = new();
    private static readonly TimeSpan _maxDelay = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Calculates the next retry time using exponential backoff with jitter.
    /// </summary>
    /// <param name="attempts">Number of attempts already made.</param>
    /// <param name="baseDelay">Base delay between retries.</param>
    /// <returns>The DateTime (UTC) when the next retry should occur.</returns>
    public static DateTime CalculateNextRetry(int attempts, TimeSpan baseDelay)
    {
        var delay = CalculateDelay(attempts, baseDelay);
        return DateTime.UtcNow.Add(delay);
    }

    /// <summary>
    /// Calculates the delay for the next retry using exponential backoff with jitter.
    /// </summary>
    /// <param name="attempts">Number of attempts already made.</param>
    /// <param name="baseDelay">Base delay between retries.</param>
    /// <returns>The TimeSpan to wait before the next retry.</returns>
    public static TimeSpan CalculateDelay(int attempts, TimeSpan baseDelay)
    {
        if (attempts <= 0)
            return baseDelay;

        // Exponential backoff: delay = baseDelay * 2^(attempts-1)
        var exponentialFactor = Math.Pow(2, attempts - 1);
        var exponentialDelay = TimeSpan.FromTicks((long)(baseDelay.Ticks * exponentialFactor));

        // Cap at maximum delay
        var actualDelay = exponentialDelay > _maxDelay ? _maxDelay : exponentialDelay;

        // Add jitter (±10%) to prevent thundering herd
        var jitterFactor = 1 + ((_random.NextDouble() * 0.2) - 0.1);
        var jitteredDelay = TimeSpan.FromTicks((long)(actualDelay.Ticks * jitterFactor));

        return jitteredDelay;
    }

    /// <summary>
    /// Calculates a random jitter delay for retry loops.
    /// </summary>
    /// <param name="minMs">Minimum delay in milliseconds.</param>
    /// <param name="maxMs">Maximum delay in milliseconds.</param>
    /// <returns>A random delay between min and max.</returns>
    public static TimeSpan GetJitter(int minMs = 5, int maxMs = 50)
    {
        return TimeSpan.FromMilliseconds(_random.Next(minMs, maxMs));
    }
}
