using IntelligenceX.Json;

namespace IntelligenceX.PowerShell;

/// <summary>
/// Represents a JSON-RPC notification record for PowerShell consumers.
/// </summary>
public sealed class RpcNotificationRecord {
    /// <summary>
    /// Initializes a new notification record.
    /// </summary>
    /// <param name="method">Notification method name.</param>
    /// <param name="params">Notification parameters.</param>
    public RpcNotificationRecord(string method, JsonValue? @params) {
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
