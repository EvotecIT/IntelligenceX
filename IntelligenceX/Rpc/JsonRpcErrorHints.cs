namespace IntelligenceX.Rpc;

/// <summary>
/// Provides friendly descriptions for JSON-RPC error codes.
/// </summary>
public static class JsonRpcErrorHints {
    /// <summary>
    /// Returns a human-readable hint for a JSON-RPC error code, when known.
    /// </summary>
    /// <param name="code">JSON-RPC error code.</param>
    /// <returns>A hint string or null when unknown.</returns>
    public static string? GetHint(int code) {
        return code switch {
            -32700 => "Parse error (malformed JSON)",
            -32600 => "Invalid request",
            -32601 => "Method not found",
            -32602 => "Invalid params",
            -32603 => "Internal error",
            -32000 => "Server error",
            _ => null
        };
    }
}
