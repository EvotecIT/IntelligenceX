using System;
using System.IO;

namespace IntelligenceX.Utils;

/// <summary>
/// Configures retry behavior for JSON-RPC calls.
/// </summary>
public sealed class RpcRetryOptions {
    /// <summary>
    /// Gets or sets the number of retry attempts.
    /// </summary>
    public int RetryCount { get; set; }
    /// <summary>
    /// Gets or sets the initial retry delay.
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(250);
    /// <summary>
    /// Gets or sets the maximum retry delay.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(2);
    /// <summary>
    /// Gets or sets a custom retry predicate.
    /// </summary>
    public Func<Exception, bool>? ShouldRetry { get; set; }

    /// <summary>
    /// Gets a value indicating whether retries are enabled.
    /// </summary>
    public bool Enabled => RetryCount > 0;

    /// <summary>
    /// Determines whether the supplied exception is retryable.
    /// </summary>
    /// <param name="ex">Exception to evaluate.</param>
    /// <returns><c>true</c> when the exception should be retried.</returns>
    public bool IsRetryable(Exception ex) {
        if (ex is OperationCanceledException) {
            return false;
        }
        if (ShouldRetry is not null) {
            return ShouldRetry(ex);
        }
        return ex is IOException || ex is TimeoutException;
    }
}
