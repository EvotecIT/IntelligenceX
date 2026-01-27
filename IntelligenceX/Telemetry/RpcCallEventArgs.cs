using System;
using IntelligenceX.Json;

namespace IntelligenceX.Telemetry;

public sealed class RpcCallStartedEventArgs : EventArgs {
    public RpcCallStartedEventArgs(string method, JsonValue? parameters) {
        Method = method;
        Parameters = parameters;
        Timestamp = DateTimeOffset.UtcNow;
    }

    public string Method { get; }
    public JsonValue? Parameters { get; }
    public DateTimeOffset Timestamp { get; }
}

public sealed class RpcCallCompletedEventArgs : EventArgs {
    public RpcCallCompletedEventArgs(string method, TimeSpan duration, bool success, Exception? error = null) {
        Method = method;
        Duration = duration;
        Success = success;
        Error = error;
        Timestamp = DateTimeOffset.UtcNow;
    }

    public string Method { get; }
    public TimeSpan Duration { get; }
    public bool Success { get; }
    public Exception? Error { get; }
    public DateTimeOffset Timestamp { get; }
}
