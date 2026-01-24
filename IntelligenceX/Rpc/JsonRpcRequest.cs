using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.Rpc;

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

    public long Id { get; }
    public string Method { get; }
    public JsonValue? Params { get; }

    public Task RespondAsync(JsonValue? result, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        return _respondAsync(Id, result);
    }

    public Task RespondErrorAsync(int code, string message, JsonValue? data = null, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        return _respondErrorAsync(Id, new JsonRpcError(code, message, data));
    }
}
