using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.Copilot;

public sealed class CopilotSession : IDisposable {
    private readonly CopilotClient _client;
    private readonly List<Action<CopilotSessionEvent>> _handlers = new();
    private bool _disposed;

    internal CopilotSession(string sessionId, CopilotClient client) {
        SessionId = sessionId;
        _client = client;
    }

    public string SessionId { get; }

    public IDisposable OnEvent(Action<CopilotSessionEvent> handler) {
        if (handler is null) {
            throw new ArgumentNullException(nameof(handler));
        }
        _handlers.Add(handler);
        return new Subscription(() => _handlers.Remove(handler));
    }

    public async Task<string> SendAsync(CopilotMessageOptions options, CancellationToken cancellationToken = default) {
        if (_disposed) {
            throw new ObjectDisposedException(nameof(CopilotSession));
        }
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var request = new JsonObject()
            .Add("sessionId", SessionId)
            .Add("prompt", options.Prompt ?? string.Empty);

        if (options.Attachments is { Count: > 0 }) {
            var attachments = new JsonArray();
            foreach (var attachment in options.Attachments) {
                var obj = new JsonObject()
                    .Add("type", attachment.Type ?? "file")
                    .Add("path", attachment.Path ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(attachment.DisplayName)) {
                    obj.Add("displayName", attachment.DisplayName);
                }
                attachments.Add(obj);
            }
            request.Add("attachments", attachments);
        }

        if (!string.IsNullOrWhiteSpace(options.Mode)) {
            request.Add("mode", options.Mode);
        }

        var parameters = new JsonArray().Add(request);
        var result = await _client.CallAsync("session.send", JsonValue.From(parameters), cancellationToken).ConfigureAwait(false);
        var messageId = result?.AsObject()?.GetString("messageId");
        return messageId ?? string.Empty;
    }

    public async Task<string?> SendAndWaitAsync(CopilotMessageOptions options, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = new StringBuilder();
        string? lastMessage = null;

        void Handler(CopilotSessionEvent evt) {
            if (!string.IsNullOrWhiteSpace(evt.Content)) {
                lastMessage = evt.Content;
            } else if (!string.IsNullOrWhiteSpace(evt.DeltaContent)) {
                builder.Append(evt.DeltaContent);
            } else if (!string.IsNullOrWhiteSpace(evt.ErrorMessage)) {
                tcs.TrySetException(new InvalidOperationException(evt.ErrorMessage));
            }

            if (evt.IsIdle) {
                if (lastMessage is not null) {
                    tcs.TrySetResult(lastMessage);
                } else if (builder.Length > 0) {
                    tcs.TrySetResult(builder.ToString());
                } else {
                    tcs.TrySetResult(null);
                }
            }
        }

        using var subscription = OnEvent(Handler);
        await SendAsync(options, cancellationToken).ConfigureAwait(false);

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(effectiveTimeout);
        using var registration = cts.Token.Register(() =>
            tcs.TrySetException(new TimeoutException($"SendAndWaitAsync timed out after {effectiveTimeout}")));
        return await tcs.Task.ConfigureAwait(false);
    }

    internal void Dispatch(CopilotSessionEvent evt) {
        foreach (var handler in _handlers.ToArray()) {
            handler(evt);
        }
    }

    public void Dispose() {
        _disposed = true;
        _handlers.Clear();
    }

    private sealed class Subscription : IDisposable {
        private readonly Action _onDispose;
        private bool _disposed;

        public Subscription(Action onDispose) {
            _onDispose = onDispose;
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }
            _disposed = true;
            _onDispose();
        }
    }
}
