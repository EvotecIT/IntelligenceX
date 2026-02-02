using System;

namespace IntelligenceX.Utils;

/// <summary>
/// Represents a health check outcome.
/// </summary>
public sealed class HealthCheckResult {
    /// <summary>
    /// Initializes a new health check result.
    /// </summary>
    /// <param name="ok">Whether the check succeeded.</param>
    /// <param name="message">Optional message.</param>
    /// <param name="error">Optional exception.</param>
    /// <param name="duration">Optional duration.</param>
    public HealthCheckResult(bool ok, string? message = null, Exception? error = null, TimeSpan? duration = null) {
        Ok = ok;
        Message = message;
        Error = error;
        Duration = duration;
    }

    /// <summary>
    /// Gets a value indicating whether the check succeeded.
    /// </summary>
    public bool Ok { get; }
    /// <summary>
    /// Gets the optional message.
    /// </summary>
    public string? Message { get; }
    /// <summary>
    /// Gets the optional exception.
    /// </summary>
    public Exception? Error { get; }
    /// <summary>
    /// Gets the optional duration.
    /// </summary>
    public TimeSpan? Duration { get; }
}
