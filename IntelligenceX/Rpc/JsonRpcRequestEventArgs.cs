using System;

namespace IntelligenceX.Rpc;

/// <summary>
/// Event arguments for JSON-RPC requests.
/// </summary>
public sealed class JsonRpcRequestEventArgs : EventArgs {
    /// <summary>
    /// Initializes a new request event args instance.
    /// </summary>
    /// <param name="request">The JSON-RPC request.</param>
    public JsonRpcRequestEventArgs(JsonRpcRequest request) {
        Request = request;
    }

    /// <summary>
    /// Gets the JSON-RPC request.
    /// </summary>
    public JsonRpcRequest Request { get; }
}
