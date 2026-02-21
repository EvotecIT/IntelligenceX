using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
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

    /// <summary>
    /// Generates a new request id suitable for <see cref="ChatServiceRequest.RequestId"/>.
    /// </summary>
    public static string NewRequestId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Requests the list of available saved profiles from the service.
    /// </summary>
    public Task<ProfileListMessage> ListProfilesAsync(CancellationToken cancellationToken = default) {
        return RequestAsync<ProfileListMessage>(new ListProfilesRequest { RequestId = NewRequestId() }, cancellationToken);
    }

    /// <summary>
    /// Runs service-side <c>*_pack_info</c> health probes and returns per-pack status.
    /// </summary>
    public Task<ToolHealthMessage> CheckToolHealthAsync(int? toolTimeoutSeconds = null, IReadOnlyList<string>? packIds = null,
        IReadOnlyList<ToolPackSourceKind>? sourceKinds = null, CancellationToken cancellationToken = default) {
        if (toolTimeoutSeconds is < ChatRequestOptionLimits.MinTimeoutSeconds or > ChatRequestOptionLimits.MaxTimeoutSeconds) {
            throw new ArgumentOutOfRangeException(
                nameof(toolTimeoutSeconds),
                $"toolTimeoutSeconds must be between {ChatRequestOptionLimits.MinTimeoutSeconds} and {ChatRequestOptionLimits.MaxTimeoutSeconds}.");
        }

        return RequestAsync<ToolHealthMessage>(new CheckToolHealthRequest {
            RequestId = NewRequestId(),
            ToolTimeoutSeconds = toolTimeoutSeconds,
            PackIds = packIds is { Count: > 0 } ? packIds.ToArray() : null,
            SourceKinds = sourceKinds is { Count: > 0 } ? sourceKinds.ToArray() : null
        }, cancellationToken);
    }

    /// <summary>
    /// Runs health probes only for packs classified as open-source.
    /// </summary>
    public Task<ToolHealthMessage> CheckOpenSourceToolHealthAsync(int? toolTimeoutSeconds = null, CancellationToken cancellationToken = default) {
        return CheckToolHealthAsync(
            toolTimeoutSeconds: toolTimeoutSeconds,
            sourceKinds: new[] { ToolPackSourceKind.OpenSource },
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Runs health probes only for packs classified as closed-source.
    /// </summary>
    public Task<ToolHealthMessage> CheckClosedSourceToolHealthAsync(int? toolTimeoutSeconds = null, CancellationToken cancellationToken = default) {
        return CheckToolHealthAsync(
            toolTimeoutSeconds: toolTimeoutSeconds,
            sourceKinds: new[] { ToolPackSourceKind.ClosedSource },
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Switches the active service profile for this session.
    /// </summary>
    public Task<AckMessage> SetProfileAsync(string profileName, bool newThread = true, CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(profileName)) {
            throw new ArgumentException("Profile name cannot be empty.", nameof(profileName));
        }
        return RequestAsync<AckMessage>(new SetProfileRequest {
            RequestId = NewRequestId(),
            ProfileName = profileName.Trim(),
            NewThread = newThread
        }, cancellationToken);
    }

    /// <summary>
    /// Applies runtime/provider settings live in the connected service session.
    /// </summary>
    public Task<AckMessage> ApplyRuntimeSettingsAsync(
        string? model = null,
        string? openAITransport = null,
        string? openAIBaseUrl = null,
        string? openAIApiKey = null,
        string? openAIAuthMode = null,
        string? openAIBasicUsername = null,
        string? openAIBasicPassword = null,
        string? openAIAccountId = null,
        bool clearOpenAIApiKey = false,
        bool clearOpenAIBasicAuth = false,
        bool? openAIStreaming = null,
        bool? openAIAllowInsecureHttp = null,
        string? reasoningEffort = null,
        string? reasoningSummary = null,
        string? textVerbosity = null,
        double? temperature = null,
        bool? enablePowerShellPack = null,
        bool? enableTestimoXPack = null,
        bool? enableOfficeImoPack = null,
        string? profileName = null,
        CancellationToken cancellationToken = default) {
        return RequestAsync<AckMessage>(new ApplyRuntimeSettingsRequest {
            RequestId = NewRequestId(),
            Model = model,
            OpenAITransport = openAITransport,
            OpenAIBaseUrl = openAIBaseUrl,
            OpenAIApiKey = openAIApiKey,
            OpenAIAuthMode = openAIAuthMode,
            OpenAIBasicUsername = openAIBasicUsername,
            OpenAIBasicPassword = openAIBasicPassword,
            OpenAIAccountId = openAIAccountId,
            ClearOpenAIApiKey = clearOpenAIApiKey,
            ClearOpenAIBasicAuth = clearOpenAIBasicAuth,
            OpenAIStreaming = openAIStreaming,
            OpenAIAllowInsecureHttp = openAIAllowInsecureHttp,
            ReasoningEffort = reasoningEffort,
            ReasoningSummary = reasoningSummary,
            TextVerbosity = textVerbosity,
            Temperature = temperature,
            EnablePowerShellPack = enablePowerShellPack,
            EnableTestimoXPack = enableTestimoXPack,
            EnableOfficeImoPack = enableOfficeImoPack,
            ProfileName = profileName
        }, cancellationToken);
    }

    /// <summary>
    /// Requests the list of models from the active provider/transport.
    /// </summary>
    public Task<ModelListMessage> ListModelsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default) {
        return RequestAsync<ModelListMessage>(new ListModelsRequest { RequestId = NewRequestId(), ForceRefresh = forceRefresh }, cancellationToken);
    }

    /// <summary>
    /// Requests the list of favorite models for the active profile.
    /// </summary>
    public Task<ModelFavoritesMessage> ListModelFavoritesAsync(CancellationToken cancellationToken = default) {
        return RequestAsync<ModelFavoritesMessage>(new ListModelFavoritesRequest { RequestId = NewRequestId() }, cancellationToken);
    }

    /// <summary>
    /// Adds or removes a model from favorites for the active profile.
    /// </summary>
    public Task<AckMessage> SetModelFavoriteAsync(string model, bool isFavorite, CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(model)) {
            throw new ArgumentException("Model cannot be empty.", nameof(model));
        }
        return RequestAsync<AckMessage>(new SetModelFavoriteRequest {
            RequestId = NewRequestId(),
            Model = model.Trim(),
            IsFavorite = isFavorite
        }, cancellationToken);
    }
}
