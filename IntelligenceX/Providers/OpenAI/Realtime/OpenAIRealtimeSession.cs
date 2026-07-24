using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Realtime;

/// <summary>
/// Owns an active OpenAI Realtime WebSocket session.
/// </summary>
public sealed class OpenAIRealtimeSession : IDisposable {
    private const int ReceiveBufferBytes = 16 * 1024;

    private readonly ClientWebSocket _webSocket;
    private readonly int _maxEventBytes;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _receiveLock = new(1, 1);
    private int _disposeState;

    internal OpenAIRealtimeSession(ClientWebSocket webSocket, string model, int maxEventBytes) {
        _webSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
        Model = model;
        _maxEventBytes = maxEventBytes;
    }

    /// <summary>
    /// Gets the connected Realtime model.
    /// </summary>
    public string Model { get; }

    /// <summary>
    /// Gets the current WebSocket state.
    /// </summary>
    public WebSocketState State => IsDisposed ? WebSocketState.Closed : _webSocket.State;

    /// <summary>
    /// Sends a user text item and optionally asks the model to respond.
    /// </summary>
    public async Task SendTextAsync(string text, bool requestResponse = true, CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(text)) {
            throw new ArgumentException("Text cannot be null or whitespace.", nameof(text));
        }

        var content = new JsonArray().Add(new JsonObject()
            .Add("type", "input_text")
            .Add("text", text));
        var item = JsonLite.Serialize(JsonValue.From(new JsonObject()
            .Add("type", "conversation.item.create")
            .Add("item", new JsonObject()
                .Add("type", "message")
                .Add("role", "user")
                .Add("content", content))));
        await SendEventAsync(item, cancellationToken).ConfigureAwait(false);
        if (requestResponse) {
            await RequestResponseAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sends an image URL or data URL as a user input item and optionally asks the model to respond.
    /// </summary>
    public async Task SendImageAsync(
        string imageUrl,
        bool requestResponse = true,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(imageUrl)) {
            throw new ArgumentException("Image URL cannot be null or whitespace.", nameof(imageUrl));
        }

        var content = new JsonArray().Add(new JsonObject()
            .Add("type", "input_image")
            .Add("image_url", imageUrl.Trim()));
        var item = JsonLite.Serialize(JsonValue.From(new JsonObject()
            .Add("type", "conversation.item.create")
            .Add("item", new JsonObject()
                .Add("type", "message")
                .Add("role", "user")
                .Add("content", content))));
        await SendEventAsync(item, cancellationToken).ConfigureAwait(false);
        if (requestResponse) {
            await RequestResponseAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Appends base64-encoded PCM audio bytes to the session input buffer.
    /// </summary>
    public Task AppendAudioAsync(byte[] audioBytes, CancellationToken cancellationToken = default) {
        if (audioBytes is null || audioBytes.Length == 0) {
            throw new ArgumentException("Audio bytes cannot be null or empty.", nameof(audioBytes));
        }

        var payload = new JsonObject()
            .Add("type", "input_audio_buffer.append")
            .Add("audio", Convert.ToBase64String(audioBytes));
        return SendEventAsync(JsonLite.Serialize(JsonValue.From(payload)), cancellationToken);
    }

    /// <summary>
    /// Commits the current input audio buffer and optionally asks the model to respond.
    /// </summary>
    public async Task CommitAudioAsync(bool requestResponse = true, CancellationToken cancellationToken = default) {
        await SendEventAsync("{\"type\":\"input_audio_buffer.commit\"}", cancellationToken).ConfigureAwait(false);
        if (requestResponse) {
            await RequestResponseAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Requests a model response for the current conversation state.
    /// </summary>
    public Task RequestResponseAsync(CancellationToken cancellationToken = default) {
        return SendEventAsync("{\"type\":\"response.create\"}", cancellationToken);
    }

    /// <summary>
    /// Sends a raw Realtime client event.
    /// </summary>
    public async Task SendEventAsync(string eventJson, CancellationToken cancellationToken = default) {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(eventJson)) {
            throw new ArgumentException("Event JSON cannot be empty.", nameof(eventJson));
        }

        var bytes = Encoding.UTF8.GetBytes(eventJson);
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            ThrowIfDisposed();
            await _webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken)
                .ConfigureAwait(false);
        } finally {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Receives the next complete Realtime server event. Only one receive operation may run at a time.
    /// </summary>
    public async Task<OpenAIRealtimeEvent?> ReceiveEventAsync(CancellationToken cancellationToken = default) {
        ThrowIfDisposed();
        await _receiveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            ThrowIfDisposed();
            var buffer = new byte[ReceiveBufferBytes];
            using var stream = new MemoryStream();

            while (true) {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                    .ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) {
                    return null;
                }
                if (result.MessageType != WebSocketMessageType.Text) {
                    throw new InvalidDataException("OpenAI Realtime returned a non-text WebSocket event.");
                }

                if (stream.Length + result.Count > _maxEventBytes) {
                    throw new InvalidDataException("OpenAI Realtime event exceeded the configured size limit.");
                }
                stream.Write(buffer, 0, result.Count);
                if (result.EndOfMessage) {
                    break;
                }
            }

            return OpenAIRealtimeEvent.Parse(Encoding.UTF8.GetString(stream.ToArray()));
        } finally {
            _receiveLock.Release();
        }
    }

    /// <summary>
    /// Closes the Realtime WebSocket when possible.
    /// </summary>
    public async Task CloseAsync(CancellationToken cancellationToken = default) {
        if (IsDisposed) {
            return;
        }

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            if (IsDisposed ||
                _webSocket.State is WebSocketState.Closed or WebSocketState.Aborted or WebSocketState.None) {
                return;
            }
            if (_webSocket.State == WebSocketState.Open || _webSocket.State == WebSocketState.CloseReceived) {
                await _webSocket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Client closed",
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        } catch (ObjectDisposedException) when (IsDisposed) {
            // A concurrent Dispose owns shutdown.
        } finally {
            _sendLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) {
            return;
        }
        _webSocket.Dispose();
    }

    private bool IsDisposed => Volatile.Read(ref _disposeState) != 0;

    private void ThrowIfDisposed() {
        if (IsDisposed) {
            throw new ObjectDisposedException(nameof(OpenAIRealtimeSession));
        }
    }
}
