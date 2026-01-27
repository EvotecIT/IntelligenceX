using System;

namespace IntelligenceX.Utils;

public sealed class HealthCheckResult {
    public HealthCheckResult(bool ok, string? message = null, Exception? error = null, TimeSpan? duration = null) {
        Ok = ok;
        Message = message;
        Error = error;
        Duration = duration;
    }

    public bool Ok { get; }
    public string? Message { get; }
    public Exception? Error { get; }
    public TimeSpan? Duration { get; }
}
