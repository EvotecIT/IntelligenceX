using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.Rpc;
using IntelligenceX.Telemetry;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI.Transport;

internal sealed class AppServerTransport : IOpenAITransport {
    private readonly AppServerClient _client;

    public AppServerTransport(AppServerClient client) {
        _client = client;
        _client.NotificationReceived += OnNotificationReceived;
        _client.LoginStarted += OnLoginStarted;
        _client.LoginCompleted += OnLoginCompleted;
        _client.ProtocolLineReceived += OnProtocolLineReceived;
        _client.StandardErrorReceived += OnStandardErrorReceived;
        _client.RpcCallStarted += OnRpcCallStarted;
        _client.RpcCallCompleted += OnRpcCallCompleted;
    }

    public OpenAITransportKind Kind => OpenAITransportKind.AppServer;
    public AppServerClient? RawAppServerClient => _client;

    public event EventHandler<string>? DeltaReceived;
    public event EventHandler<LoginEventArgs>? LoginStarted;
    public event EventHandler<LoginEventArgs>? LoginCompleted;
    public event EventHandler<string>? ProtocolLineReceived;
    public event EventHandler<string>? StandardErrorReceived;
    public event EventHandler<RpcCallStartedEventArgs>? RpcCallStarted;
    public event EventHandler<RpcCallCompletedEventArgs>? RpcCallCompleted;

    public Task InitializeAsync(ClientInfo clientInfo, CancellationToken cancellationToken) {
        return _client.InitializeAsync(clientInfo, cancellationToken);
    }

    public Task<HealthCheckResult> HealthCheckAsync(string? method, TimeSpan? timeout, CancellationToken cancellationToken) {
        return _client.HealthCheckAsync(method, timeout, cancellationToken);
    }

    public Task<AccountInfo> GetAccountAsync(CancellationToken cancellationToken) {
        return _client.ReadAccountAsync(cancellationToken);
    }

    public Task LogoutAsync(CancellationToken cancellationToken) {
        return _client.LogoutAsync(cancellationToken);
    }

    public Task<ModelListResult> ListModelsAsync(CancellationToken cancellationToken) {
        return _client.ListModelsAsync(cancellationToken);
    }

    public async Task<ChatGptLoginStart> LoginChatGptAsync(Action<string>? onUrl, Func<string, Task<string>>? onPrompt,
        bool useLocalListener, TimeSpan timeout, CancellationToken cancellationToken) {
        var login = await _client.StartChatGptLoginAsync(cancellationToken).ConfigureAwait(false);
        onUrl?.Invoke(login.AuthUrl);
        await _client.WaitForLoginCompletionAsync(login.LoginId, cancellationToken).ConfigureAwait(false);
        return login;
    }

    public Task LoginApiKeyAsync(string apiKey, CancellationToken cancellationToken) {
        return _client.LoginWithApiKeyAsync(apiKey, cancellationToken);
    }

    public Task<ThreadInfo> StartThreadAsync(string model, string? currentDirectory, string? approvalPolicy,
        string? sandbox, CancellationToken cancellationToken) {
        return _client.StartThreadAsync(NormalizeModel(model), currentDirectory, approvalPolicy, sandbox, cancellationToken);
    }

    public Task<ThreadInfo> ResumeThreadAsync(string threadId, CancellationToken cancellationToken) {
        return _client.ResumeThreadAsync(threadId, cancellationToken);
    }

    public Task<TurnInfo> StartTurnAsync(string threadId, ChatInput input, ChatOptions? options, string? currentDirectory,
        string? approvalPolicy, SandboxPolicy? sandboxPolicy, CancellationToken cancellationToken) {
        var model = options?.Model;
        var normalizedInput = NormalizeAndFilterReplayInputItems(input.ToJson());
        return _client.StartTurnAsync(threadId, normalizedInput, NormalizeModel(model), currentDirectory, approvalPolicy, sandboxPolicy, cancellationToken);
    }

    private void OnNotificationReceived(object? sender, JsonRpcNotificationEventArgs args) {
        var delta = TryExtractDelta(args.Params);
        if (!string.IsNullOrWhiteSpace(delta)) {
            DeltaReceived?.Invoke(this, delta!);
        }
    }

    private void OnLoginStarted(object? sender, LoginEventArgs args) => LoginStarted?.Invoke(this, args);
    private void OnLoginCompleted(object? sender, LoginEventArgs args) => LoginCompleted?.Invoke(this, args);
    private void OnProtocolLineReceived(object? sender, string line) => ProtocolLineReceived?.Invoke(this, line);
    private void OnStandardErrorReceived(object? sender, string line) => StandardErrorReceived?.Invoke(this, line);
    private void OnRpcCallStarted(object? sender, RpcCallStartedEventArgs args) => RpcCallStarted?.Invoke(this, args);
    private void OnRpcCallCompleted(object? sender, RpcCallCompletedEventArgs args) => RpcCallCompleted?.Invoke(this, args);

    private static string? TryExtractDelta(JsonValue? value) {
        return value?.AsObject()?.GetObject("delta")?.GetString("text");
    }

    private static string NormalizeModel(string? model) {
        return string.IsNullOrWhiteSpace(model)
            ? string.Empty
            : OpenAIModelCatalog.NormalizeModelId(model);
    }

    private static JsonArray NormalizeAndFilterReplayInputItems(JsonArray items) {
        if (items is null || items.Count == 0) {
            return new JsonArray();
        }

        var normalized = new List<JsonObject>(items.Count);
        var callIndexesById = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var outputIndexesById = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < items.Count; i++) {
            var normalizedItem = NormalizeInputItem(items[i].AsObject() ?? new JsonObject());
            normalized.Add(normalizedItem);

            var type = (normalizedItem.GetString("type") ?? string.Empty).Trim();
            if (!IsToolCallInputType(type) && !IsToolOutputInputType(type)) {
                continue;
            }

            var callId = (normalizedItem.GetString("call_id") ?? normalizedItem.GetString("id") ?? string.Empty).Trim();
            if (callId.Length == 0) {
                continue;
            }

            if (IsToolCallInputType(type)) {
                AddReplayIndex(callIndexesById, callId, i);
            } else {
                AddReplayIndex(outputIndexesById, callId, i);
            }
        }

        var selectedCallIndexesById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var selectedOutputIndexesById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in callIndexesById) {
            if (!outputIndexesById.TryGetValue(pair.Key, out var outputIndexes)) {
                continue;
            }

            if (!TrySelectReplayPairIndexes(pair.Value, outputIndexes, out var selectedCallIndex, out var selectedOutputIndex)) {
                continue;
            }

            selectedCallIndexesById[pair.Key] = selectedCallIndex;
            selectedOutputIndexesById[pair.Key] = selectedOutputIndex;
        }

        var filtered = new JsonArray();
        for (var i = 0; i < normalized.Count; i++) {
            var item = normalized[i];
            var type = (item.GetString("type") ?? string.Empty).Trim();
            if (!IsToolCallInputType(type) && !IsToolOutputInputType(type)) {
                filtered.Add(item);
                continue;
            }

            var callId = (item.GetString("call_id") ?? item.GetString("id") ?? string.Empty).Trim();
            if (callId.Length == 0) {
                continue;
            }

            if (IsToolCallInputType(type)
                && selectedCallIndexesById.TryGetValue(callId, out var selectedCallIndex)
                && selectedCallIndex == i) {
                filtered.Add(item);
                continue;
            }

            if (IsToolOutputInputType(type)
                && selectedOutputIndexesById.TryGetValue(callId, out var selectedOutputIndex)
                && selectedOutputIndex == i) {
                filtered.Add(item);
            }
        }

        return filtered;
    }

    private static JsonObject NormalizeInputItem(JsonObject message) {
        var type = (message.GetString("type") ?? string.Empty).Trim();
        if (IsToolCallInputType(type) || LooksLikeToolCallInputShape(message)) {
            var callId = FirstNonEmpty(
                message.GetString("call_id"),
                message.GetString("tool_call_id"),
                message.GetString("id")) ?? string.Empty;
            var name = FirstNonEmpty(
                message.GetString("name"),
                message.GetObject("function")?.GetString("name")) ?? string.Empty;
            var input = FirstNonEmpty(
                message.GetString("input"),
                message.GetString("arguments"),
                message.GetObject("function")?.GetString("arguments")) ?? "{}";

            var normalized = new JsonObject()
                .Add("type", "custom_tool_call")
                .Add("id", callId.Trim().Length == 0 ? "call" : callId.Trim())
                .Add("input", string.IsNullOrWhiteSpace(input) ? "{}" : input.Trim());
            if (!string.IsNullOrWhiteSpace(callId)) {
                normalized.Add("call_id", callId.Trim());
            }
            if (!string.IsNullOrWhiteSpace(name)) {
                normalized.Add("name", name.Trim());
            }
            return normalized;
        }

        if (IsToolOutputInputType(type) || LooksLikeToolOutputInputShape(message)) {
            var callId = FirstNonEmpty(
                message.GetString("call_id"),
                message.GetString("tool_call_id"),
                message.GetString("id")) ?? string.Empty;
            var output = FirstNonEmpty(
                message.GetString("output"),
                message.GetString("result"),
                message.GetString("content")) ?? string.Empty;

            var normalized = new JsonObject()
                .Add("type", "custom_tool_call_output")
                .Add("output", output);
            if (!string.IsNullOrWhiteSpace(callId)) {
                normalized.Add("call_id", callId.Trim());
            }
            return normalized;
        }

        var sanitized = new JsonObject();
        foreach (var pair in message) {
            if (string.Equals(pair.Key, "arguments", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            sanitized.Add(pair.Key, pair.Value ?? JsonValue.Null);
        }
        return sanitized;
    }

    private static bool IsToolCallInputType(string type) {
        return string.Equals(type, "custom_tool_call", StringComparison.OrdinalIgnoreCase)
               || string.Equals(type, "tool_call", StringComparison.OrdinalIgnoreCase)
               || string.Equals(type, "function_call", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsToolOutputInputType(string type) {
        return string.Equals(type, "custom_tool_call_output", StringComparison.OrdinalIgnoreCase)
               || string.Equals(type, "tool_call_output", StringComparison.OrdinalIgnoreCase)
               || string.Equals(type, "function_call_output", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeToolCallInputShape(JsonObject message) {
        var callId = FirstNonEmpty(message.GetString("call_id"), message.GetString("tool_call_id"));
        if (string.IsNullOrWhiteSpace(callId)) {
            return false;
        }

        var hasName = !string.IsNullOrWhiteSpace(message.GetString("name"))
                      || message.GetObject("function") is not null;
        var hasInput = !string.IsNullOrWhiteSpace(message.GetString("input"))
                       || !string.IsNullOrWhiteSpace(message.GetString("arguments"));
        return hasName && hasInput;
    }

    private static bool LooksLikeToolOutputInputShape(JsonObject message) {
        var callId = FirstNonEmpty(message.GetString("call_id"), message.GetString("tool_call_id"));
        if (string.IsNullOrWhiteSpace(callId)) {
            return false;
        }

        return !string.IsNullOrWhiteSpace(message.GetString("output"))
               || !string.IsNullOrWhiteSpace(message.GetString("result"));
    }

    private static void AddReplayIndex(IDictionary<string, List<int>> indexesById, string callId, int index) {
        if (!indexesById.TryGetValue(callId, out var indexes)) {
            indexes = new List<int>();
            indexesById[callId] = indexes;
        }

        indexes.Add(index);
    }

    private static bool TrySelectReplayPairIndexes(
        IReadOnlyList<int> callIndexes,
        IReadOnlyList<int> outputIndexes,
        out int selectedCallIndex,
        out int selectedOutputIndex) {
        selectedCallIndex = -1;
        selectedOutputIndex = -1;
        if (callIndexes is null || outputIndexes is null || callIndexes.Count == 0 || outputIndexes.Count == 0) {
            return false;
        }

        var outputCursor = 0;
        for (var i = 0; i < callIndexes.Count; i++) {
            var callIndex = callIndexes[i];
            while (outputCursor < outputIndexes.Count && outputIndexes[outputCursor] <= callIndex) {
                outputCursor++;
            }

            if (outputCursor >= outputIndexes.Count) {
                break;
            }

            selectedCallIndex = callIndex;
            selectedOutputIndex = outputIndexes[outputCursor];
        }

        return selectedCallIndex >= 0 && selectedOutputIndex >= 0;
    }

    private static string? FirstNonEmpty(params string?[] values) {
        if (values is null || values.Length == 0) {
            return null;
        }

        for (var i = 0; i < values.Length; i++) {
            var value = values[i];
            if (!string.IsNullOrWhiteSpace(value)) {
                return value;
            }
        }

        return null;
    }

    public void Dispose() {
        _client.NotificationReceived -= OnNotificationReceived;
        _client.LoginStarted -= OnLoginStarted;
        _client.LoginCompleted -= OnLoginCompleted;
        _client.ProtocolLineReceived -= OnProtocolLineReceived;
        _client.StandardErrorReceived -= OnStandardErrorReceived;
        _client.RpcCallStarted -= OnRpcCallStarted;
        _client.RpcCallCompleted -= OnRpcCallCompleted;
        _client.Dispose();
    }
}
