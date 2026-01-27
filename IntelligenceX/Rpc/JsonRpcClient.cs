using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Telemetry;

namespace IntelligenceX.Rpc;

internal sealed class JsonRpcClient : IDisposable {
    private sealed class PendingCall {
        public PendingCall(string method, TaskCompletionSource<JsonValue?> tcs) {
            Method = method;
            Tcs = tcs;
        }

        public string Method { get; }
        public TaskCompletionSource<JsonValue?> Tcs { get; }
    }

    private readonly Func<string, Task> _sendLineAsync;
    private readonly ConcurrentDictionary<long, PendingCall> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private long _nextId;
    private bool _disposed;

    public JsonRpcClient(Func<string, Task> sendLineAsync) {
        _sendLineAsync = sendLineAsync;
    }

    public event EventHandler<JsonRpcNotificationEventArgs>? NotificationReceived;
    public event EventHandler<JsonRpcRequestEventArgs>? RequestReceived;
    public event EventHandler<Exception>? ProtocolError;
    public event EventHandler<RpcCallStartedEventArgs>? CallStarted;
    public event EventHandler<RpcCallCompletedEventArgs>? CallCompleted;

    public Task<JsonValue?> CallAsync(string method, JsonObject? @params, CancellationToken cancellationToken = default) {
        return CallAsync(method, @params is null ? null : JsonValue.From(@params), cancellationToken);
    }

    public async Task<JsonValue?> CallAsync(string method, JsonValue? @params, CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(method)) {
            throw new ArgumentException("Method cannot be null or whitespace.", nameof(method));
        }

        CallStarted?.Invoke(this, new RpcCallStartedEventArgs(method, @params));
        var started = DateTime.UtcNow;
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonValue?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending.TryAdd(id, new PendingCall(method, tcs));

        using var ctr = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        await SendRequestAsync(id, method, @params).ConfigureAwait(false);

        try {
            var result = await tcs.Task.ConfigureAwait(false);
            var duration = DateTime.UtcNow - started;
            CallCompleted?.Invoke(this, new RpcCallCompletedEventArgs(method, duration, true));
            return result;
        } catch (Exception ex) {
            var duration = DateTime.UtcNow - started;
            CallCompleted?.Invoke(this, new RpcCallCompletedEventArgs(method, duration, false, ex));
            throw;
        }
    }

    public Task NotifyAsync(string method, JsonObject? @params, CancellationToken cancellationToken = default) {
        return NotifyAsync(method, @params is null ? null : JsonValue.From(@params), cancellationToken);
    }

    public Task NotifyAsync(string method, JsonValue? @params, CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(method)) {
            throw new ArgumentException("Method cannot be null or whitespace.", nameof(method));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return SendNotificationAsync(method, @params);
    }

    public void HandleLine(string line) {
        if (string.IsNullOrWhiteSpace(line)) {
            return;
        }

        JsonValue root;
        try {
            root = JsonLite.Parse(line);
        } catch (Exception ex) {
            ProtocolError?.Invoke(this, ex);
            return;
        }

        var obj = root.AsObject();
        if (obj is null) {
            ProtocolError?.Invoke(this, new FormatException("JSON-RPC message root must be an object."));
            return;
        }

        var hasId = obj.TryGetValue("id", out var idValue);
        var hasMethod = obj.TryGetValue("method", out var methodValue);

        if (hasMethod && !hasId) {
            var method = methodValue?.AsString();
            if (!string.IsNullOrWhiteSpace(method)) {
                obj.TryGetValue("params", out var parameters);
                NotificationReceived?.Invoke(this, new JsonRpcNotificationEventArgs(method!, parameters));
                return;
            }
        }

        if (hasMethod && hasId && idValue?.AsInt64() is long requestId) {
            var method = methodValue?.AsString() ?? string.Empty;
            obj.TryGetValue("params", out var parameters);
            var request = new JsonRpcRequest(requestId, method, parameters, RespondAsync, RespondErrorAsync);
            RequestReceived?.Invoke(this, new JsonRpcRequestEventArgs(request));
            return;
        }

        if (hasId && idValue?.AsInt64() is long responseId) {
            if (_pending.TryRemove(responseId, out var pending)) {
                if (obj.TryGetValue("error", out var errorValue)) {
                    var error = ParseError(errorValue);
                    pending.Tcs.TrySetException(new JsonRpcException(pending.Method, error));
                    return;
                }

                obj.TryGetValue("result", out var resultValue);
                pending.Tcs.TrySetResult(resultValue);
                return;
            }
        }

        ProtocolError?.Invoke(this, new FormatException("Unknown JSON-RPC message shape."));
    }

    private JsonRpcError ParseError(JsonValue? errorValue) {
        var errorObj = errorValue?.AsObject();
        if (errorObj is null) {
            return new JsonRpcError(-1, "Unknown error", errorValue);
        }

        var code = (int)(errorObj.GetInt64("code") ?? -1);
        var message = errorObj.GetString("message") ?? "Unknown error";
        errorObj.TryGetValue("data", out var data);
        return new JsonRpcError(code, message, data);
    }

    private async Task SendRequestAsync(long id, string method, JsonValue? @params) {
        var obj = new JsonObject()
            .Add("id", id)
            .Add("method", method);
        if (@params is not null) {
            obj.Add("params", @params);
        }
        await SendLineAsync(JsonLite.Serialize(obj)).ConfigureAwait(false);
    }

    private async Task SendNotificationAsync(string method, JsonValue? @params) {
        var obj = new JsonObject()
            .Add("method", method);
        if (@params is not null) {
            obj.Add("params", @params);
        }
        await SendLineAsync(JsonLite.Serialize(obj)).ConfigureAwait(false);
    }

    private async Task RespondAsync(long id, JsonValue? result) {
        var obj = new JsonObject()
            .Add("id", id);
        if (result is null) {
            obj.Add("result", JsonValue.Null);
        } else {
            obj.Add("result", result);
        }
        await SendLineAsync(JsonLite.Serialize(obj)).ConfigureAwait(false);
    }

    private async Task RespondErrorAsync(long id, JsonRpcError error) {
        var errorObj = new JsonObject()
            .Add("code", error.Code)
            .Add("message", error.Message);
        if (error.Data is not null) {
            errorObj.Add("data", error.Data);
        }
        var obj = new JsonObject()
            .Add("id", id)
            .Add("error", errorObj);
        await SendLineAsync(JsonLite.Serialize(obj)).ConfigureAwait(false);
    }

    private async Task SendLineAsync(string line) {
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try {
            await _sendLineAsync(line).ConfigureAwait(false);
        } finally {
            _sendLock.Release();
        }
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _sendLock.Dispose();
        foreach (var pending in _pending.Values) {
            pending.Tcs.TrySetCanceled();
        }
        _pending.Clear();
    }
}
