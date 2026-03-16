using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Linq;
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
                throw new ChatServiceRequestException(err.Error, err.Code);
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
            msg = TryDeserializeMessageLine(line);
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

    internal static ChatServiceMessage? TryDeserializeMessageLine(string line, Func<string, ChatServiceMessage?>? primaryDeserializer = null) {
        if (string.IsNullOrWhiteSpace(line)) {
            return null;
        }

        try {
            var deserialize = primaryDeserializer ?? (static input =>
                JsonSerializer.Deserialize(input, ChatServiceJsonContext.Default.ChatServiceMessage));
            return deserialize(line);
        } catch (Exception) {
            // Resilience path: preserve chat_result text when optional timeline metadata is malformed.
            return TryDeserializeChatResultWithoutTimeline(line);
        }
    }

    private static ChatServiceMessage? TryDeserializeChatResultWithoutTimeline(string line) {
        try {
            using var document = JsonDocument.Parse(line);
            if (!TryBuildChatResultWithoutTimelineJson(document, out var sanitizedJson)) {
                return null;
            }

            return JsonSerializer.Deserialize(sanitizedJson, ChatServiceJsonContext.Default.ChatServiceMessage);
        } catch {
            // Intentional fail-open behavior: if salvage parsing fails, drop only this frame
            // and let the read loop continue rather than faulting the entire client session.
            return null;
        }
    }

    private static bool TryBuildChatResultWithoutTimelineJson(JsonDocument document, out string sanitizedJson) {
        sanitizedJson = string.Empty;
        if (document.RootElement.ValueKind != JsonValueKind.Object) {
            return false;
        }

        if (!TryGetStringProperty(document.RootElement, "type", out var type)
            || !string.Equals(type, "chat_result", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (!ContainsPropertyCaseInsensitive(document.RootElement, "turnTimelineEvents")) {
            return false;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        foreach (var property in document.RootElement.EnumerateObject()) {
            if (string.Equals(property.Name, "turnTimelineEvents", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            property.WriteTo(writer);
        }
        writer.WriteEndObject();
        writer.Flush();

        sanitizedJson = System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
        return sanitizedJson.Length > 0;
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string value) {
        value = string.Empty;
        foreach (var property in element.EnumerateObject()) {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.String) {
                return false;
            }

            value = property.Value.GetString() ?? string.Empty;
            return value.Length > 0;
        }

        return false;
    }

    private static bool ContainsPropertyCaseInsensitive(JsonElement element, string propertyName) {
        foreach (var property in element.EnumerateObject()) {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
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
    /// Requests the current background scheduler status for the active session.
    /// </summary>
    public Task<BackgroundSchedulerStatusMessage> GetBackgroundSchedulerStatusAsync(
        string? threadId = null,
        bool includeRecentActivity = true,
        bool includeThreadSummaries = true,
        int? maxReadyThreadIds = null,
        int? maxRunningThreadIds = null,
        int? maxRecentActivity = null,
        int? maxThreadSummaries = null,
        CancellationToken cancellationToken = default) {
        ValidateBackgroundSchedulerStatusLimit(maxReadyThreadIds, nameof(maxReadyThreadIds));
        ValidateBackgroundSchedulerStatusLimit(maxRunningThreadIds, nameof(maxRunningThreadIds));
        ValidateBackgroundSchedulerStatusLimit(maxRecentActivity, nameof(maxRecentActivity));
        ValidateBackgroundSchedulerStatusLimit(maxThreadSummaries, nameof(maxThreadSummaries));

        return RequestAsync<BackgroundSchedulerStatusMessage>(
            new GetBackgroundSchedulerStatusRequest {
                RequestId = NewRequestId(),
                ThreadId = string.IsNullOrWhiteSpace(threadId) ? null : threadId.Trim(),
                IncludeRecentActivity = includeRecentActivity,
                IncludeThreadSummaries = includeThreadSummaries,
                MaxReadyThreadIds = maxReadyThreadIds,
                MaxRunningThreadIds = maxRunningThreadIds,
                MaxRecentActivity = maxRecentActivity,
                MaxThreadSummaries = maxThreadSummaries
            },
            cancellationToken);
    }

    /// <summary>
    /// Applies an operator pause/resume action to the background scheduler and returns the updated scheduler summary.
    /// </summary>
    public Task<BackgroundSchedulerStatusMessage> SetBackgroundSchedulerPausedAsync(
        bool paused,
        int? pauseSeconds = null,
        string? reason = null,
        CancellationToken cancellationToken = default) {
        ValidateBackgroundSchedulerPauseSeconds(paused, pauseSeconds);

        return RequestAsync<BackgroundSchedulerStatusMessage>(
            new SetBackgroundSchedulerStateRequest {
                RequestId = NewRequestId(),
                Paused = paused,
                PauseSeconds = pauseSeconds,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()
            },
            cancellationToken);
    }

    /// <summary>
    /// Updates background scheduler maintenance windows and returns the updated scheduler summary.
    /// </summary>
    public Task<BackgroundSchedulerStatusMessage> SetBackgroundSchedulerMaintenanceWindowsAsync(
        string operation,
        IReadOnlyList<string>? windows = null,
        CancellationToken cancellationToken = default) {
        ValidateBackgroundSchedulerMaintenanceWindowOperation(operation, windows);

        return RequestAsync<BackgroundSchedulerStatusMessage>(
            new SetBackgroundSchedulerMaintenanceWindowsRequest {
                RequestId = NewRequestId(),
                Operation = operation.Trim(),
                Windows = windows is { Count: > 0 }
                    ? windows
                        .Where(static window => !string.IsNullOrWhiteSpace(window))
                        .Select(static window => window.Trim())
                        .ToArray()
                    : null
            },
            cancellationToken);
    }

    /// <summary>
    /// Updates background scheduler blocked-thread policy and returns the updated scheduler summary.
    /// </summary>
    public Task<BackgroundSchedulerStatusMessage> SetBackgroundSchedulerBlockedThreadsAsync(
        string operation,
        IReadOnlyList<string>? threadIds = null,
        int? durationSeconds = null,
        bool untilNextMaintenanceWindow = false,
        bool untilNextMaintenanceWindowStart = false,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(operation)) {
            throw new ArgumentException("Operation must be provided.", nameof(operation));
        }

        if (durationSeconds is < ChatRequestOptionLimits.MinPositiveTimeoutSeconds or > ChatRequestOptionLimits.MaxTimeoutSeconds) {
            throw new ArgumentOutOfRangeException(
                nameof(durationSeconds),
                $"durationSeconds must be between {ChatRequestOptionLimits.MinPositiveTimeoutSeconds} and {ChatRequestOptionLimits.MaxTimeoutSeconds}.");
        }

        if (durationSeconds is not null && untilNextMaintenanceWindow) {
            throw new ArgumentException("durationSeconds and untilNextMaintenanceWindow cannot be set together.", nameof(untilNextMaintenanceWindow));
        }

        if (durationSeconds is not null && untilNextMaintenanceWindowStart) {
            throw new ArgumentException("durationSeconds and untilNextMaintenanceWindowStart cannot be set together.", nameof(untilNextMaintenanceWindowStart));
        }

        if (untilNextMaintenanceWindow && untilNextMaintenanceWindowStart) {
            throw new ArgumentException("untilNextMaintenanceWindow and untilNextMaintenanceWindowStart cannot be set together.", nameof(untilNextMaintenanceWindowStart));
        }

        string[]? normalizedThreadIds = null;
        if (threadIds is { Count: > 0 }) {
            normalizedThreadIds = threadIds
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        return RequestAsync<BackgroundSchedulerStatusMessage>(
            new SetBackgroundSchedulerBlockedThreadsRequest {
                RequestId = NewRequestId(),
                Operation = operation.Trim(),
                ThreadIds = normalizedThreadIds,
                DurationSeconds = durationSeconds,
                UntilNextMaintenanceWindow = untilNextMaintenanceWindow,
                UntilNextMaintenanceWindowStart = untilNextMaintenanceWindowStart
            },
            cancellationToken);
    }

    /// <summary>
    /// Updates background scheduler blocked-pack policy and returns the updated scheduler summary.
    /// </summary>
    public Task<BackgroundSchedulerStatusMessage> SetBackgroundSchedulerBlockedPacksAsync(
        string operation,
        IReadOnlyList<string>? packIds = null,
        int? durationSeconds = null,
        bool untilNextMaintenanceWindow = false,
        bool untilNextMaintenanceWindowStart = false,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(operation)) {
            throw new ArgumentException("Operation must be provided.", nameof(operation));
        }

        if (durationSeconds is < ChatRequestOptionLimits.MinPositiveTimeoutSeconds or > ChatRequestOptionLimits.MaxTimeoutSeconds) {
            throw new ArgumentOutOfRangeException(
                nameof(durationSeconds),
                $"durationSeconds must be between {ChatRequestOptionLimits.MinPositiveTimeoutSeconds} and {ChatRequestOptionLimits.MaxTimeoutSeconds}.");
        }

        if (durationSeconds is not null && untilNextMaintenanceWindow) {
            throw new ArgumentException("durationSeconds and untilNextMaintenanceWindow cannot be set together.", nameof(untilNextMaintenanceWindow));
        }

        if (durationSeconds is not null && untilNextMaintenanceWindowStart) {
            throw new ArgumentException("durationSeconds and untilNextMaintenanceWindowStart cannot be set together.", nameof(untilNextMaintenanceWindowStart));
        }

        if (untilNextMaintenanceWindow && untilNextMaintenanceWindowStart) {
            throw new ArgumentException("untilNextMaintenanceWindow and untilNextMaintenanceWindowStart cannot be set together.", nameof(untilNextMaintenanceWindowStart));
        }

        string[]? normalizedPackIds = null;
        if (packIds is { Count: > 0 }) {
            normalizedPackIds = packIds
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return RequestAsync<BackgroundSchedulerStatusMessage>(
            new SetBackgroundSchedulerBlockedPacksRequest {
                RequestId = NewRequestId(),
                Operation = operation.Trim(),
                PackIds = normalizedPackIds,
                DurationSeconds = durationSeconds,
                UntilNextMaintenanceWindow = untilNextMaintenanceWindow,
                UntilNextMaintenanceWindowStart = untilNextMaintenanceWindowStart
            },
            cancellationToken);
    }

    /// <summary>
    /// Updates background scheduler maintenance windows from structured descriptors and returns the updated scheduler summary.
    /// </summary>
    public Task<BackgroundSchedulerStatusMessage> SetBackgroundSchedulerMaintenanceWindowEntriesAsync(
        string operation,
        IReadOnlyList<SessionCapabilityBackgroundSchedulerMaintenanceWindowDto>? windows,
        CancellationToken cancellationToken = default) {
        var windowSpecs = windows is { Count: > 0 }
            ? windows.Select(BuildBackgroundSchedulerMaintenanceWindowSpec).ToArray()
            : null;
        return SetBackgroundSchedulerMaintenanceWindowsAsync(operation, windowSpecs, cancellationToken);
    }

    /// <summary>
    /// Builds a canonical maintenance-window spec from structured day/time/scope values.
    /// </summary>
    public static string BuildBackgroundSchedulerMaintenanceWindowSpec(
        string day,
        string startTimeLocal,
        int durationMinutes,
        string? packId = null,
        string? threadId = null) {
        if (durationMinutes < 1 || durationMinutes > 1440) {
            throw new ArgumentOutOfRangeException(nameof(durationMinutes), "durationMinutes must be between 1 and 1440.");
        }

        var normalizedDay = (day ?? string.Empty).Trim();
        var normalizedStartTime = (startTimeLocal ?? string.Empty).Trim();
        if (normalizedDay.Length == 0) {
            throw new ArgumentOutOfRangeException(nameof(day), "day must be provided.");
        }

        if (normalizedStartTime.Length == 0) {
            throw new ArgumentOutOfRangeException(nameof(startTimeLocal), "startTimeLocal must be provided.");
        }

        var spec = $"{NormalizeBackgroundSchedulerMaintenanceWindowDay(normalizedDay)}@{normalizedStartTime}/{durationMinutes}";
        var normalizedPackId = (packId ?? string.Empty).Trim();
        if (normalizedPackId.Length > 0) {
            spec += ";pack=" + normalizedPackId;
        }

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length > 0) {
            spec += ";thread=" + normalizedThreadId;
        }

        ValidateBackgroundSchedulerMaintenanceWindowSpecs(new[] { spec });
        return spec;
    }

    /// <summary>
    /// Builds a canonical maintenance-window spec from a structured descriptor.
    /// </summary>
    public static string BuildBackgroundSchedulerMaintenanceWindowSpec(SessionCapabilityBackgroundSchedulerMaintenanceWindowDto window) {
        ArgumentNullException.ThrowIfNull(window);

        if (!string.IsNullOrWhiteSpace(window.Spec)) {
            var explicitSpec = window.Spec.Trim();
            ValidateBackgroundSchedulerMaintenanceWindowSpecs(new[] { explicitSpec });
            return explicitSpec;
        }

        return BuildBackgroundSchedulerMaintenanceWindowSpec(
            window.Day,
            window.StartTimeLocal,
            window.DurationMinutes,
            window.PackId,
            window.ThreadId);
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

    private static void ValidateBackgroundSchedulerStatusLimit(int? value, string parameterName) {
        if (value is not int requested) {
            return;
        }

        if (requested < 0 || requested > ChatRequestOptionLimits.MaxBackgroundSchedulerStatusItems) {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"{parameterName} must be between 0 and {ChatRequestOptionLimits.MaxBackgroundSchedulerStatusItems}.");
        }
    }

    private static void ValidateBackgroundSchedulerPauseSeconds(bool paused, int? pauseSeconds) {
        if (!paused && pauseSeconds is not null) {
            throw new ArgumentOutOfRangeException(nameof(pauseSeconds), "pauseSeconds can only be set when paused is true.");
        }

        if (pauseSeconds is not int requested) {
            return;
        }

        if (requested < ChatRequestOptionLimits.MinPositiveTimeoutSeconds || requested > ChatRequestOptionLimits.MaxTimeoutSeconds) {
            throw new ArgumentOutOfRangeException(
                nameof(pauseSeconds),
                $"pauseSeconds must be between {ChatRequestOptionLimits.MinPositiveTimeoutSeconds} and {ChatRequestOptionLimits.MaxTimeoutSeconds}.");
        }
    }

    private static void ValidateBackgroundSchedulerMaintenanceWindowOperation(string operation, IReadOnlyList<string>? windows) {
        var normalizedOperation = (operation ?? string.Empty).Trim().ToLowerInvariant();
        var requiresWindows = normalizedOperation is "add" or "remove" or "replace";
        var forbidsWindows = normalizedOperation is "clear" or "reset";
        if (!requiresWindows && !forbidsWindows) {
            throw new ArgumentOutOfRangeException(nameof(operation), "operation must be one of: add, remove, replace, clear, reset.");
        }

        if (forbidsWindows) {
            if (windows is { Count: > 0 }) {
                throw new ArgumentOutOfRangeException(nameof(windows), "windows must be empty for clear/reset operations.");
            }

            return;
        }

        if (windows is not { Count: > 0 }) {
            throw new ArgumentOutOfRangeException(nameof(windows), "windows must be provided for add/remove/replace operations.");
        }

        ValidateBackgroundSchedulerMaintenanceWindowSpecs(windows);
    }

    private static void ValidateBackgroundSchedulerMaintenanceWindowSpecs(IReadOnlyList<string> windows) {
        for (var i = 0; i < windows.Count; i++) {
            var spec = (windows[i] ?? string.Empty).Trim();
            if (spec.Length == 0) {
                throw new ArgumentOutOfRangeException(nameof(windows), "windows must not contain empty values.");
            }

            if (!TryValidateBackgroundSchedulerMaintenanceWindowSpec(spec, out var error)) {
                throw new ArgumentOutOfRangeException(nameof(windows), error ?? "Invalid maintenance-window spec.");
            }
        }
    }

    private static bool TryValidateBackgroundSchedulerMaintenanceWindowSpec(string spec, out string? error) {
        error = null;
        var segments = spec.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) {
            error = "must use <day>@HH:mm/<minutes> optionally followed by ;pack=<id> and/or ;thread=<id>.";
            return false;
        }

        var schedule = segments[0];
        var atIndex = schedule.IndexOf('@');
        var slashIndex = schedule.IndexOf('/');
        if (atIndex <= 0 || slashIndex <= atIndex + 1 || slashIndex >= schedule.Length - 1) {
            error = "must use <day>@HH:mm/<minutes> optionally followed by ;pack=<id> and/or ;thread=<id>.";
            return false;
        }

        var day = schedule[..atIndex].Trim().ToLowerInvariant();
        if (day is not ("daily" or "everyday" or "day" or "mon" or "monday" or "tue" or "tuesday" or "wed" or "wednesday" or "thu" or "thursday" or "fri" or "friday" or "sat" or "saturday" or "sun" or "sunday")) {
            error = "day must be daily, mon, tue, wed, thu, fri, sat, or sun.";
            return false;
        }

        var time = schedule.Substring(atIndex + 1, slashIndex - atIndex - 1);
        var timeParts = time.Split(':', StringSplitOptions.TrimEntries);
        if (timeParts.Length != 2
            || !int.TryParse(timeParts[0], out var hour)
            || !int.TryParse(timeParts[1], out var minute)
            || hour is < 0 or > 23
            || minute is < 0 or > 59) {
            error = "time must use HH:mm in 24-hour local time.";
            return false;
        }

        var durationText = schedule[(slashIndex + 1)..];
        if (!int.TryParse(durationText, out var durationMinutes) || durationMinutes is < 1 or > 1440) {
            error = "duration minutes must be between 1 and 1440.";
            return false;
        }

        for (var i = 1; i < segments.Length; i++) {
            var segment = segments[i];
            var equalsIndex = segment.IndexOf('=');
            if (equalsIndex <= 0 || equalsIndex >= segment.Length - 1) {
                error = "scope segments must use pack=<id> or thread=<id>.";
                return false;
            }

            var key = segment[..equalsIndex].Trim().ToLowerInvariant();
            var value = segment[(equalsIndex + 1)..].Trim();
            if (value.Length == 0) {
                error = "scope segments must use a non-empty value.";
                return false;
            }

            if (key is not ("pack" or "thread")) {
                error = "only pack=<id> and thread=<id> scope segments are supported.";
                return false;
            }
        }

        return true;
    }

    private static string NormalizeBackgroundSchedulerMaintenanceWindowDay(string day) {
        return (day ?? string.Empty).Trim().ToLowerInvariant() switch {
            "everyday" => "daily",
            "day" => "daily",
            "monday" => "mon",
            "tuesday" => "tue",
            "wednesday" => "wed",
            "thursday" => "thu",
            "friday" => "fri",
            "saturday" => "sat",
            "sunday" => "sun",
            var value => value
        };
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
        IReadOnlyList<string>? enablePackIds = null,
        IReadOnlyList<string>? disablePackIds = null,
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
            EnablePackIds = enablePackIds is { Count: > 0 } ? enablePackIds.ToArray() : null,
            DisablePackIds = disablePackIds is { Count: > 0 } ? disablePackIds.ToArray() : null,
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
