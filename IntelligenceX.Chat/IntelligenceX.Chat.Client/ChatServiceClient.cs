using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;

namespace IntelligenceX.Chat.Client;

/// <summary>
/// Minimal named-pipe client for <c>IntelligenceX.Chat.Service</c>.
/// </summary>
public sealed class ChatServiceClient : IAsyncDisposable {
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ChatServiceMessage>> _pending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _disconnectSignaled;

    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _readLoopCts;
    private Task? _readLoop;

    /// <summary>
    /// Raised for every message received from the service (events and responses).
    /// </summary>
    public event Action<ChatServiceMessage>? MessageReceived;
    /// <summary>
    /// Raised when the read loop ends and the client is no longer connected.
    /// </summary>
    public event Action<ChatServiceClient>? Disconnected;

    /// <summary>
    /// Connects to the service pipe and starts a background read loop.
    /// </summary>
    public async Task ConnectAsync(string pipeName, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(pipeName)) {
            throw new ArgumentException("Pipe name cannot be empty.", nameof(pipeName));
        }
        if (_pipe is not null) {
            throw new InvalidOperationException("Client is already connected.");
        }

        var pipe = new NamedPipeClientStream(
            serverName: ".",
            pipeName: pipeName,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous);

        await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Exchange(ref _disconnectSignaled, 0);

        _pipe = pipe;
        _reader = new StreamReader(pipe, leaveOpen: true);
        _writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };

        // Keep the read loop lifetime independent from the short connect timeout token.
        // The caller may use a very small token for ConnectAsync; linking here would
        // cancel the session shortly after a successful connection.
        _readLoopCts = new CancellationTokenSource();
        _readLoop = Task.Run(() => ReadLoopAsync(_readLoopCts.Token), CancellationToken.None);
    }

    /// <summary>
    /// Sends a request and waits for the correlated response.
    /// </summary>
    public async Task<TResponse> RequestAsync<TResponse>(ChatServiceRequest request, CancellationToken cancellationToken)
        where TResponse : ChatServiceMessage {
        if (request is null) {
            throw new ArgumentNullException(nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.RequestId)) {
            throw new ArgumentException("RequestId is required.", nameof(request));
        }

        var tcs = new TaskCompletionSource<ChatServiceMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(request.RequestId, tcs)) {
            throw new InvalidOperationException($"A request with id '{request.RequestId}' is already in flight.");
        }

        try {
            using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            await SendAsync(request, cancellationToken).ConfigureAwait(false);
            var msg = await tcs.Task.ConfigureAwait(false);
            if (msg is ErrorMessage err) {
                throw new InvalidOperationException(err.Error);
            }
            if (msg is TResponse typed) {
                return typed;
            }
            throw new InvalidOperationException($"Expected response '{typeof(TResponse).Name}', got '{msg.GetType().Name}'.");
        } finally {
            _pending.TryRemove(request.RequestId, out _);
        }
    }

    /// <summary>
    /// Sends a request without waiting for a response.
    /// </summary>
    public async Task SendAsync(ChatServiceRequest request, CancellationToken cancellationToken) {
        if (request is null) {
            throw new ArgumentNullException(nameof(request));
        }

        var writer = _writer ?? throw new InvalidOperationException("Not connected.");
        var json = JsonSerializer.Serialize(request, ChatServiceJsonContext.Default.ChatServiceRequest);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        } finally {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken) {
        var reader = _reader!;
        while (!cancellationToken.IsCancellationRequested) {
            string? line;
            try {
                line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                break;
            } catch {
                break;
            }

            if (line is null) {
                break;
            }
            if (string.IsNullOrWhiteSpace(line)) {
                continue;
            }

            ChatServiceMessage? msg;
            try {
                msg = JsonSerializer.Deserialize(line, ChatServiceJsonContext.Default.ChatServiceMessage);
            } catch {
                continue;
            }
            if (msg is null) {
                continue;
            }

            MessageReceived?.Invoke(msg);

            // Only correlate response frames (events flow through MessageReceived).
            if (msg.Kind == ChatServiceMessageKind.Response && !string.IsNullOrWhiteSpace(msg.RequestId)) {
                if (_pending.TryGetValue(msg.RequestId!, out var tcs)) {
                    tcs.TrySetResult(msg);
                }
            }
        }

        // Fail any pending requests on disconnect.
        foreach (var item in _pending) {
            item.Value.TrySetException(new IOException("Disconnected."));
        }
        _pending.Clear();
        SignalDisconnected();
    }

    /// <summary>
    /// Disposes the client and closes the pipe connection.
    /// </summary>
    public async ValueTask DisposeAsync() {
        try {
            _readLoopCts?.Cancel();
        } catch {
            // Ignore.
        }

        if (_readLoop is not null) {
            try {
                await _readLoop.ConfigureAwait(false);
            } catch {
                // Ignore.
            }
        }

        try {
            _writer?.Dispose();
        } catch {
            // Ignore.
        }
        try {
            _reader?.Dispose();
        } catch {
            // Ignore.
        }
        try {
            _pipe?.Dispose();
        } catch {
            // Ignore.
        }

        _writeLock.Dispose();
        _readLoopCts?.Dispose();
        SignalDisconnected();
    }

    private void SignalDisconnected() {
        if (Interlocked.Exchange(ref _disconnectSignaled, 1) != 0) {
            return;
        }

        try {
            Disconnected?.Invoke(this);
        } catch {
            // Ignore.
        }
    }
}
