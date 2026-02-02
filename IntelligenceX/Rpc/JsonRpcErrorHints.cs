namespace IntelligenceX.Rpc;

/// <summary>
/// Provides human-friendly hints for JSON-RPC error codes.
/// </summary>
public static class JsonRpcErrorHints {
    /// <summary>Gets the hint for a JSON-RPC error code.</summary>
    /// <param name="code">The JSON-RPC error code.</param>
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
