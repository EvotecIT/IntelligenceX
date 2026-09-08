using IntelligenceX.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.Copilot;

/// <summary>
/// Represents an active Copilot session.
/// </summary>
public sealed class CopilotSession : IDisposable {
    private readonly CopilotClient _client;
    private readonly List<Action<CopilotSessionEvent>> _handlers = new();
    private readonly object _handlersLock = new();
    private bool _disposed;

    internal CopilotSession(string sessionId, CopilotClient client) {
        SessionId = sessionId;
        _client = client;
    }

    /// <summary>
    /// Gets the session id.
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// Subscribes to session events.
    /// </summary>
    /// <param name="handler">Event handler.</param>
    /// <returns>A subscription token that should be disposed to unsubscribe.</returns>
    public IDisposable OnEvent(Action<CopilotSessionEvent> handler) {
        if (handler is null) {
            throw new ArgumentNullException(nameof(handler));
        }
        lock (_handlersLock) _handlers.Add(handler);
        return new Subscription(() => { lock (_handlersLock) _handlers.Remove(handler); });
    }

    /// <summary>
    /// Sends a message to the session.
    /// </summary>
    /// <param name="options">Message options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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

        var result = await _client.CallAsync("session.send", JsonValue.From(request), cancellationToken).ConfigureAwait(false);
        var messageId = result?.AsObject()?.GetString("messageId");
        return messageId ?? string.Empty;
    }

    /// <summary>
    /// Sends a message and waits for the response.
    /// </summary>
    /// <param name="options">Message options.</param>
    /// <param name="timeout">Optional timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string?> SendAndWaitAsync(CopilotMessageOptions options, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (options.MaxResponseCharacters < 1 || options.MaxResponseCharacters > 16_000_000) throw new ArgumentOutOfRangeException(nameof(options));
        if (options.MaxResponseBytes is < 1 or > 268_435_456) throw new ArgumentOutOfRangeException(nameof(options));
        int maximumCharacters = options.MaxResponseCharacters;
        long? maximumBytes = options.MaxResponseBytes;
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = new StringBuilder();
        var deltaEncoder = Encoding.UTF8.GetEncoder();
        var encodingBuffer = new byte[512];
        long deltaBytes = 0;
        string? lastMessage = null;

        void Handler(CopilotSessionEvent evt) {
            if (tcs.Task.IsCompleted) return;
            if ((evt.Content?.Length ?? 0) > maximumCharacters
                || (long)builder.Length + (evt.DeltaContent?.Length ?? 0) > maximumCharacters) {
                tcs.TrySetException(new InvalidDataException("Copilot response exceeded its character limit.")); return;
            }
            if (maximumBytes.HasValue) {
                if (evt.Content is not null && Encoding.UTF8.GetByteCount(evt.Content) > maximumBytes.Value) {
                    tcs.TrySetException(new InvalidDataException("Copilot response exceeded its UTF-8 byte limit.")); return;
                }
                if (evt.DeltaContent is not null) {
                    deltaBytes += CountUtf8Bytes(deltaEncoder, evt.DeltaContent, encodingBuffer, flush: false);
                }
                if (evt.IsIdle) deltaBytes += CountUtf8Bytes(deltaEncoder, string.Empty, encodingBuffer, flush: true);
                if (deltaBytes > maximumBytes.Value) {
                    tcs.TrySetException(new InvalidDataException("Copilot response exceeded its UTF-8 byte limit.")); return;
                }
            }
            if (evt.Content is not null) {
                lastMessage = evt.Content;
            } else if (evt.DeltaContent is not null) {
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
        using var registration = cts.Token.Register(() => {
            if (cancellationToken.IsCancellationRequested) tcs.TrySetCanceled(cancellationToken);
            else tcs.TrySetException(new TimeoutException("Copilot response timed out."));
        });
        return await tcs.Task.ConfigureAwait(false);
    }

    private static long CountUtf8Bytes(Encoder encoder, string text, byte[] buffer, bool flush) {
        char[] characters = text.ToCharArray();
        int offset = 0;
        long count = 0;
        bool completed;
        do {
            encoder.Convert(characters, offset, characters.Length - offset, buffer, 0, buffer.Length, flush,
                out int consumed, out int written, out completed);
            offset += consumed; count += written;
        } while (!completed);
        return count;
    }

    internal void Dispatch(CopilotSessionEvent evt) {
        Action<CopilotSessionEvent>[] handlers;
        lock (_handlersLock) handlers = _handlers.ToArray();
        ObserverDispatcher.Dispatch(handlers, evt);
    }

    /// <summary>
    /// Disposes the session and clears handlers.
    /// </summary>
    public void Dispose() {
        _disposed = true;
        lock (_handlersLock) _handlers.Clear();
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
