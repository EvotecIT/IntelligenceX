using System;

namespace IntelligenceX.Rpc;

/// <summary>
/// Exception raised for JSON-RPC error responses.
/// </summary>
public sealed class JsonRpcException : Exception {
    /// <summary>
    /// Initializes a new instance from a JSON-RPC error.
    /// </summary>
    /// <param name="error">The JSON-RPC error payload.</param>
    public JsonRpcException(JsonRpcError error) : this(string.Empty, error, null) { }

    /// <summary>
    /// Initializes a new instance from a JSON-RPC error and inner exception.
    /// </summary>
    /// <param name="error">The JSON-RPC error payload.</param>
    /// <param name="innerException">The underlying exception.</param>
    public JsonRpcException(JsonRpcError error, Exception innerException) : this(string.Empty, error, innerException) { }

    /// <summary>
    /// Initializes a new instance for a specific JSON-RPC method.
    /// </summary>
    /// <param name="method">The method name.</param>
    /// <param name="error">The JSON-RPC error payload.</param>
    public JsonRpcException(string method, JsonRpcError error) : this(method, error, null) { }

    /// <summary>
    /// Initializes a new instance for a specific JSON-RPC method with an inner exception.
    /// </summary>
    /// <param name="method">The method name.</param>
    /// <param name="error">The JSON-RPC error payload.</param>
    /// <param name="innerException">The underlying exception.</param>
    public JsonRpcException(string method, JsonRpcError error, Exception? innerException)
        : base(BuildMessage(method, error), innerException) {
        Error = error;
        Method = method;
        Hint = JsonRpcErrorHints.GetHint(error.Code);
    }

    /// <summary>
    /// Gets the JSON-RPC error payload.
    /// </summary>
    public JsonRpcError Error { get; }
    /// <summary>
    /// Gets the JSON-RPC method name.
    /// </summary>
    public string Method { get; }
    /// <summary>
    /// Gets a human-readable hint for the error code when available.
    /// </summary>
    public string? Hint { get; }

    private static string BuildMessage(string method, JsonRpcError error) {
        var hint = JsonRpcErrorHints.GetHint(error.Code);
        var prefix = string.IsNullOrWhiteSpace(method) ? string.Empty : method + ": ";
        if (string.IsNullOrWhiteSpace(hint)) {
            return prefix + error;
        }
        return $"{prefix}{error} ({hint})";
    }
}
