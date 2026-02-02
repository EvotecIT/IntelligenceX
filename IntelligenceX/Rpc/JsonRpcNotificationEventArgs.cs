using System;
using IntelligenceX.Json;

namespace IntelligenceX.Rpc;

/// <summary>
/// Event arguments for JSON-RPC notifications.
/// </summary>
public sealed class JsonRpcNotificationEventArgs : EventArgs {
    /// <summary>
    /// Initializes a new notification event args instance.
    /// </summary>
    /// <param name="method">The notification method name.</param>
    /// <param name="params">Optional notification parameters.</param>
    public JsonRpcNotificationEventArgs(string method, JsonValue? @params) {
        Method = method;
        Params = @params;
    }

    /// <summary>
    /// Gets the notification method name.
    /// </summary>
    public string Method { get; }
    /// <summary>
    /// Gets the notification parameters.
    /// </summary>
    public JsonValue? Params { get; }
}
