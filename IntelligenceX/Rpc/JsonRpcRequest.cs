using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.Rpc;

/// <summary>
/// Represents an inbound JSON-RPC request.
/// </summary>
public sealed class JsonRpcRequest {
    private readonly Func<long, JsonValue?, Task> _respondAsync;
    private readonly Func<long, JsonRpcError, Task> _respondErrorAsync;

    internal JsonRpcRequest(long id, string method, JsonValue? @params,
        Func<long, JsonValue?, Task> respondAsync,
        Func<long, JsonRpcError, Task> respondErrorAsync) {
        Id = id;
        Method = method;
        Params = @params;
        _respondAsync = respondAsync;
        _respondErrorAsync = respondErrorAsync;
    }

    /// <summary>
    /// Gets the request id.
    /// </summary>
    public long Id { get; }
    /// <summary>
    /// Gets the JSON-RPC method name.
    /// </summary>
    public string Method { get; }
    /// <summary>
    /// Gets the request parameters.
    /// </summary>
    public JsonValue? Params { get; }

    /// <summary>
    /// Sends a successful response for this request.
    /// </summary>
    /// <param name="result">The response result value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task RespondAsync(JsonValue? result, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        return _respondAsync(Id, result);
    }

    /// <summary>
    /// Sends an error response for this request.
    /// </summary>
    /// <param name="code">JSON-RPC error code.</param>
    /// <param name="message">Error message.</param>
    /// <param name="data">Optional error data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task RespondErrorAsync(int code, string message, JsonValue? data = null, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        return _respondErrorAsync(Id, new JsonRpcError(code, message, data));
    }
}
