using System;
using IntelligenceX.Json;

namespace IntelligenceX.Telemetry;

/// <summary>
/// Event arguments raised when a JSON-RPC call starts.
/// </summary>
public sealed class RpcCallStartedEventArgs : EventArgs {
    /// <summary>
    /// Initializes a new call-start event args instance.
    /// </summary>
    /// <param name="method">The RPC method name.</param>
    /// <param name="parameters">The RPC parameters.</param>
    /// <param name="requestId">The JSON-RPC request id, if available.</param>
    public RpcCallStartedEventArgs(string method, JsonValue? parameters, long? requestId = null) {
        Method = method;
        Parameters = parameters;
        RequestId = requestId;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the RPC method name.
    /// </summary>
    public string Method { get; }
    /// <summary>
    /// Gets the RPC parameters payload.
    /// </summary>
    public JsonValue? Parameters { get; }
    /// <summary>
    /// Gets the JSON-RPC request id, when available.
    /// </summary>
    public long? RequestId { get; }
    /// <summary>
    /// Gets the UTC timestamp when the call started.
    /// </summary>
    public DateTimeOffset Timestamp { get; }
}

/// <summary>
/// Event arguments raised when a JSON-RPC call completes.
/// </summary>
public sealed class RpcCallCompletedEventArgs : EventArgs {
    /// <summary>
    /// Initializes a new call-completed event args instance.
    /// </summary>
    /// <param name="method">The RPC method name.</param>
    /// <param name="duration">The call duration.</param>
    /// <param name="success">Whether the call succeeded.</param>
    /// <param name="error">The exception when the call fails.</param>
    /// <param name="requestId">The JSON-RPC request id, if available.</param>
    public RpcCallCompletedEventArgs(string method, TimeSpan duration, bool success, Exception? error = null, long? requestId = null) {
        Method = method;
        Duration = duration;
        Success = success;
        Error = error;
        RequestId = requestId;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the RPC method name.
    /// </summary>
    public string Method { get; }
    /// <summary>
    /// Gets the call duration.
    /// </summary>
    public TimeSpan Duration { get; }
    /// <summary>
    /// Gets a value indicating whether the call succeeded.
    /// </summary>
    public bool Success { get; }
    /// <summary>
    /// Gets the exception raised on failure, if any.
    /// </summary>
    public Exception? Error { get; }
    /// <summary>
    /// Gets the JSON-RPC request id, when available.
    /// </summary>
    public long? RequestId { get; }
    /// <summary>
    /// Gets the UTC timestamp when the call completed.
    /// </summary>
    public DateTimeOffset Timestamp { get; }
}
