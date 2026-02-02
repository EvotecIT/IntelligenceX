using IntelligenceX.Json;

namespace IntelligenceX.Rpc;

/// <summary>
/// Represents a JSON-RPC error payload.
/// </summary>
public sealed class JsonRpcError {
    /// <summary>
    /// Initializes a new JSON-RPC error.
    /// </summary>
    /// <param name="code">Error code.</param>
    /// <param name="message">Error message.</param>
    /// <param name="data">Optional error data.</param>
    public JsonRpcError(int code, string message, JsonValue? data) {
        Code = code;
        Message = message;
        Data = data;
    }

    /// <summary>
    /// Gets the error code.
    /// </summary>
    public int Code { get; }
    /// <summary>
    /// Gets the error message.
    /// </summary>
    public string Message { get; }
    /// <summary>
    /// Gets optional error data.
    /// </summary>
    public JsonValue? Data { get; }

    /// <summary>
    /// Returns a human-readable error string.
    /// </summary>
    public override string ToString() => $"{Code}: {Message}";
}
