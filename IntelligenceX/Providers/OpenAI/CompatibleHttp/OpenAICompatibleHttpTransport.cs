using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.OpenAI.Transport;
using IntelligenceX.Telemetry;
using IntelligenceX.Tools;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI.CompatibleHttp;

internal sealed class OpenAICompatibleHttpTransport : IOpenAITransport {
    private readonly OpenAICompatibleHttpOptions _options;
    private readonly HttpClient _http;
    private readonly Uri _apiBase;
    private readonly Uri _modelsUrl;
    private readonly Uri? _lmStudioModelsUrl;
    private readonly Uri _chatCompletionsUrl;

    private readonly object _threadsLock = new();
    private readonly Dictionary<string, CompatibleThreadState> _threads = new(StringComparer.Ordinal);

    internal OpenAICompatibleHttpTransport(OpenAICompatibleHttpOptions options)
        : this(options, httpClient: null) { }

    internal OpenAICompatibleHttpTransport(OpenAICompatibleHttpOptions options, HttpClient? httpClient) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();

        _apiBase = NormalizeBaseUrl(_options.BaseUrl!);
        _modelsUrl = new Uri(_apiBase, "models");
        _lmStudioModelsUrl = BuildLmStudioModelsUrl(_apiBase);
        _chatCompletionsUrl = new Uri(_apiBase, "chat/completions");

        _http = httpClient ?? new HttpClient();
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    public OpenAITransportKind Kind => OpenAITransportKind.CompatibleHttp;
    public AppServerClient? RawAppServerClient => null;

    public event EventHandler<string>? DeltaReceived;
    public event EventHandler<LoginEventArgs>? LoginStarted;
    public event EventHandler<LoginEventArgs>? LoginCompleted;
#pragma warning disable CS0067
    public event EventHandler<string>? ProtocolLineReceived;
    public event EventHandler<string>? StandardErrorReceived;
#pragma warning restore CS0067
    public event EventHandler<RpcCallStartedEventArgs>? RpcCallStarted;
    public event EventHandler<RpcCallCompletedEventArgs>? RpcCallCompleted;

    public Task InitializeAsync(ClientInfo clientInfo, CancellationToken cancellationToken) {
        // No explicit initialization required for OpenAI-compatible HTTP endpoints.
        return Task.CompletedTask;
    }

    public async Task<HealthCheckResult> HealthCheckAsync(string? method, TimeSpan? timeout, CancellationToken cancellationToken) {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        CancellationTokenSource? cts = null;
        var token = cancellationToken;
        if (timeout.HasValue) {
            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout.Value);
            token = cts.Token;
        }

        try {
            // Prefer a cheap call that most OpenAI-compatible servers implement.
            _ = await ListModelsAsync(token).ConfigureAwait(false);
            return new HealthCheckResult(true, "compatible-http/models", null, sw.Elapsed);
        } catch (Exception ex) {
            return new HealthCheckResult(false, "compatible-http/models", ex, sw.Elapsed);
        } finally {
            cts?.Dispose();
        }
    }

    public Task<AccountInfo> GetAccountAsync(CancellationToken cancellationToken) {
        // Many OpenAI-compatible endpoints don't expose an account endpoint. Treat "reachable" as "authenticated".
        var obj = new JsonObject()
            .Add("id", "local")
            .Add("planType", "local");
        return Task.FromResult(AccountInfo.FromJson(obj));
    }

    public Task LogoutAsync(CancellationToken cancellationToken) {
        // No session state to clear for compatible HTTP endpoints.
        return Task.CompletedTask;
    }

    public async Task<ModelListResult> ListModelsAsync(CancellationToken cancellationToken) {
        using var request = new HttpRequestMessage(HttpMethod.Get, _modelsUrl);
        AddAuthHeader(request);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        RpcCallStarted?.Invoke(this, new RpcCallStartedEventArgs("models.list", JsonValue.From(new JsonObject().Add("url", _modelsUrl.ToString()))));
        try {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await ReadAsStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                throw new InvalidOperationException($"Model list request failed ({(int)response.StatusCode}): {payload}");
            }

            var value = JsonLite.Parse(payload);
            var obj = value?.AsObject() ?? new JsonObject();
            RpcCallCompleted?.Invoke(this, new RpcCallCompletedEventArgs("models.list", sw.Elapsed, true));
            var primary = ModelListResult.FromJson(obj);
            return await TryMergeLmStudioCatalogAsync(primary, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            RpcCallCompleted?.Invoke(this, new RpcCallCompletedEventArgs("models.list", sw.Elapsed, false, ex));
            throw;
        }
    }

    private async Task<ModelListResult> TryMergeLmStudioCatalogAsync(ModelListResult primary, CancellationToken cancellationToken) {
        if (_lmStudioModelsUrl is null) {
            return primary;
        }

        var lmStudioCatalog = await TryFetchLmStudioCatalogAsync(cancellationToken).ConfigureAwait(false);
        if (lmStudioCatalog is null || lmStudioCatalog.Models.Count == 0) {
            return primary;
        }

        var merged = MergeLmStudioCatalogModels(primary.Models, lmStudioCatalog.Models);
        return new ModelListResult(merged, primary.NextCursor, primary.Raw, primary.Additional);
    }

    private async Task<ModelListResult?> TryFetchLmStudioCatalogAsync(CancellationToken cancellationToken) {
        if (_lmStudioModelsUrl is null) {
            return null;
        }

        try {
            using var request = new HttpRequestMessage(HttpMethod.Get, _lmStudioModelsUrl);
            AddAuthHeader(request);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                return null;
            }

            var payload = await ReadAsStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
            var value = JsonLite.Parse(payload);
            var obj = value?.AsObject();
            return obj is null ? null : ModelListResult.FromJson(obj);
        } catch {
            return null;
        }
    }

    private static IReadOnlyList<ModelInfo> MergeLmStudioCatalogModels(IReadOnlyList<ModelInfo> primary, IReadOnlyList<ModelInfo> catalog) {
        if (primary.Count == 0) {
            return primary;
        }

        if (catalog.Count == 0) {
            return primary;
        }

        var catalogLookup = new Dictionary<string, ModelInfo>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < catalog.Count; i++) {
            var model = catalog[i];
            RegisterModelAliases(catalogLookup, model);
        }

        var merged = new List<ModelInfo>(primary.Count);
        for (var i = 0; i < primary.Count; i++) {
            var model = primary[i];
            var match = FindCatalogMatch(catalogLookup, model);
            merged.Add(match is null ? model : MergeModelInfo(model, match));
        }

        return merged;
    }

    private static ModelInfo? FindCatalogMatch(IReadOnlyDictionary<string, ModelInfo> lookup, ModelInfo model) {
        if (lookup.Count == 0 || model is null) {
            return null;
        }

        var id = NormalizeModelKey(model.Id);
        if (id.Length > 0 && lookup.TryGetValue(id, out var byId)) {
            return byId;
        }

        var name = NormalizeModelKey(model.Model);
        if (name.Length > 0 && lookup.TryGetValue(name, out var byModel)) {
            return byModel;
        }

        return null;
    }

    private static ModelInfo MergeModelInfo(ModelInfo primary, ModelInfo catalog) {
        var mergedCapabilities = MergeCapabilities(primary.Capabilities, catalog.Capabilities);
        return new ModelInfo(
            id: string.IsNullOrWhiteSpace(primary.Id) ? catalog.Id : primary.Id,
            model: string.IsNullOrWhiteSpace(primary.Model) ? catalog.Model : primary.Model,
            displayName: string.IsNullOrWhiteSpace(primary.DisplayName) ? catalog.DisplayName : primary.DisplayName,
            description: string.IsNullOrWhiteSpace(primary.Description) ? catalog.Description : primary.Description,
            supportedReasoningEfforts: primary.SupportedReasoningEfforts.Count == 0 ? catalog.SupportedReasoningEfforts : primary.SupportedReasoningEfforts,
            defaultReasoningEffort: string.IsNullOrWhiteSpace(primary.DefaultReasoningEffort)
                ? catalog.DefaultReasoningEffort
                : primary.DefaultReasoningEffort,
            isDefault: primary.IsDefault || catalog.IsDefault,
            raw: primary.Raw,
            additional: primary.Additional,
            ownedBy: string.IsNullOrWhiteSpace(primary.OwnedBy) ? catalog.OwnedBy : primary.OwnedBy,
            publisher: string.IsNullOrWhiteSpace(primary.Publisher) ? catalog.Publisher : primary.Publisher,
            architecture: string.IsNullOrWhiteSpace(primary.Architecture) ? catalog.Architecture : primary.Architecture,
            quantization: string.IsNullOrWhiteSpace(primary.Quantization) ? catalog.Quantization : primary.Quantization,
            compatibilityType: string.IsNullOrWhiteSpace(primary.CompatibilityType) ? catalog.CompatibilityType : primary.CompatibilityType,
            runtimeState: string.IsNullOrWhiteSpace(primary.RuntimeState) ? catalog.RuntimeState : primary.RuntimeState,
            modelType: string.IsNullOrWhiteSpace(primary.ModelType) ? catalog.ModelType : primary.ModelType,
            maxContextLength: primary.MaxContextLength ?? catalog.MaxContextLength,
            loadedContextLength: primary.LoadedContextLength ?? catalog.LoadedContextLength,
            capabilities: mergedCapabilities);
    }

    private static IReadOnlyList<string> MergeCapabilities(IReadOnlyList<string> primary, IReadOnlyList<string> catalog) {
        if (primary.Count == 0 && catalog.Count == 0) {
            return Array.Empty<string>();
        }

        if (catalog.Count == 0) {
            return primary;
        }

        if (primary.Count == 0) {
            return catalog;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<string>(primary.Count + catalog.Count);
        for (var i = 0; i < primary.Count; i++) {
            var item = primary[i];
            if (string.IsNullOrWhiteSpace(item) || !seen.Add(item.Trim())) {
                continue;
            }
            merged.Add(item.Trim());
        }

        for (var i = 0; i < catalog.Count; i++) {
            var item = catalog[i];
            if (string.IsNullOrWhiteSpace(item) || !seen.Add(item.Trim())) {
                continue;
            }
            merged.Add(item.Trim());
        }

        return merged;
    }

    private static void RegisterModelAliases(IDictionary<string, ModelInfo> lookup, ModelInfo model) {
        if (lookup is null || model is null) {
            return;
        }

        var id = NormalizeModelKey(model.Id);
        if (id.Length > 0 && !lookup.ContainsKey(id)) {
            lookup[id] = model;
        }

        var name = NormalizeModelKey(model.Model);
        if (name.Length > 0 && !lookup.ContainsKey(name)) {
            lookup[name] = model;
        }
    }

    private static string NormalizeModelKey(string? value) {
        if (value is null) {
            return string.Empty;
        }

        var normalized = value.Trim();
        return normalized.Length == 0 ? string.Empty : normalized;
    }

    public Task<ChatGptLoginStart> LoginChatGptAsync(Action<string>? onUrl, Func<string, Task<string>>? onPrompt, bool useLocalListener,
        TimeSpan timeout, CancellationToken cancellationToken) {
        throw new NotSupportedException("ChatGPT OAuth login is not supported with CompatibleHttp transport. Configure BaseUrl (and optionally ApiKey).");
    }

    public Task LoginApiKeyAsync(string apiKey, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(apiKey)) {
            throw new ArgumentException("API key cannot be empty.", nameof(apiKey));
        }
        // Keep the API key in-memory for this instance. This is primarily for callers reusing the same client.
        _options.ApiKey = apiKey.Trim();
        LoginStarted?.Invoke(this, new LoginEventArgs("apikey"));
        LoginCompleted?.Invoke(this, new LoginEventArgs("apikey"));
        return Task.CompletedTask;
    }

    public Task<ThreadInfo> StartThreadAsync(string model, string? currentDirectory, string? approvalPolicy, string? sandbox,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(model)) {
            throw new ArgumentException("Model cannot be empty.", nameof(model));
        }

        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        lock (_threadsLock) {
            _threads[id] = new CompatibleThreadState(model.Trim());
        }

        var raw = new JsonObject()
            .Add("id", id)
            .Add("modelProvider", "compatible-http")
            .Add("createdAt", now.ToUnixTimeSeconds())
            .Add("updatedAt", now.ToUnixTimeSeconds());
        return Task.FromResult(ThreadInfo.FromJson(raw));
    }

    public Task<ThreadInfo> ResumeThreadAsync(string threadId, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(threadId)) {
            throw new ArgumentException("Thread id cannot be empty.", nameof(threadId));
        }

        CompatibleThreadState? state;
        lock (_threadsLock) {
            _threads.TryGetValue(threadId.Trim(), out state);
        }

        if (state is null) {
            throw new InvalidOperationException($"Thread '{threadId}' not found in CompatibleHttp transport.");
        }

        var now = DateTimeOffset.UtcNow;
        var raw = new JsonObject()
            .Add("id", threadId.Trim())
            .Add("modelProvider", "compatible-http")
            .Add("updatedAt", now.ToUnixTimeSeconds());
        return Task.FromResult(ThreadInfo.FromJson(raw));
    }

    public async Task<TurnInfo> StartTurnAsync(string threadId, ChatInput input, ChatOptions? options, string? currentDirectory,
        string? approvalPolicy, SandboxPolicy? sandboxPolicy, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(threadId)) {
            throw new ArgumentException("Thread id cannot be empty.", nameof(threadId));
        }
        if (input is null) {
            throw new ArgumentNullException(nameof(input));
        }

        options ??= new ChatOptions();
        var model = string.IsNullOrWhiteSpace(options.Model) ? null : options.Model!.Trim();

        CompatibleThreadState state;
        lock (_threadsLock) {
            if (!_threads.TryGetValue(threadId.Trim(), out state!)) {
                // Be resilient: callers might resume a thread id from a prior run; treat it as a new thread.
                state = new CompatibleThreadState(model ?? string.Empty);
                _threads[threadId.Trim()] = state;
            }
        }

        if (!string.IsNullOrWhiteSpace(model)) {
            state.Model = model!;
        }

        if (string.IsNullOrWhiteSpace(state.Model)) {
            throw new InvalidOperationException("No model configured for CompatibleHttp transport. Set ChatOptions.Model or set a default model when creating the thread.");
        }

        if (!string.IsNullOrWhiteSpace(options.Instructions)) {
            lock (_threadsLock) {
                state.SetInstructions(options.Instructions!);
            }
        }

        var newMessages = BuildMessagesFromInput(input);
        if (newMessages.Count == 0) {
            throw new InvalidOperationException("Chat input produced no messages.");
        }

        List<JsonObject> requestMessages;
        lock (_threadsLock) {
            requestMessages = new List<JsonObject>(state.Messages.Count + newMessages.Count);
            requestMessages.AddRange(state.Messages);
            requestMessages.AddRange(newMessages);
        }

        var body = BuildChatCompletionsRequest(state.Model, requestMessages, options, streaming: _options.Streaming);
        var rpcParams = JsonValue.From(body);
        RpcCallStarted?.Invoke(this, new RpcCallStartedEventArgs("chat.completions.create", rpcParams));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try {
            var response = await SendChatCompletionsAsync(body, cancellationToken).ConfigureAwait(false);
            RpcCallCompleted?.Invoke(this, new RpcCallCompletedEventArgs("chat.completions.create", sw.Elapsed, true));

            // Update history with the messages we sent + the assistant message we received.
            lock (_threadsLock) {
                state.Messages.AddRange(newMessages);
                state.Messages.Add(response.AssistantMessageForHistory);
            }

            return response.Turn;
        } catch (Exception ex) {
            RpcCallCompleted?.Invoke(this, new RpcCallCompletedEventArgs("chat.completions.create", sw.Elapsed, false, ex));
            throw;
        }
    }

    private async Task<ChatCompletionResponse> SendChatCompletionsAsync(JsonObject body, CancellationToken cancellationToken) {
        var json = JsonLite.Serialize(JsonValue.From(body));
        using var request = new HttpRequestMessage(HttpMethod.Post, _chatCompletionsUrl);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        AddAuthHeader(request);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            var errorPayload = await ReadAsStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Chat request failed ({(int)response.StatusCode}): {errorPayload}");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var wantsStreaming = _options.Streaming && body.GetBoolean("stream");
        if (wantsStreaming && string.Equals(contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase)) {
            return await ReadChatCompletionsStreamAsync(response, cancellationToken).ConfigureAwait(false);
        }

        // Fallback: provider ignored stream and returned a normal JSON payload.
        var payload = await ReadAsStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
        var value = JsonLite.Parse(payload);
        var obj = value?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Invalid chat response (expected JSON object).");
        }
        return BuildTurnFromChatCompletions(obj);
    }

    private async Task<ChatCompletionResponse> ReadChatCompletionsStreamAsync(HttpResponseMessage response, CancellationToken cancellationToken) {
        using var stream = await ReadAsStreamAsync(response.Content, cancellationToken).ConfigureAwait(false);
        using var cancelRegistration = cancellationToken.Register(() => {
            try {
                stream.Dispose();
            } catch {
                // Ignore.
            }
        });
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 16 * 1024, leaveOpen: false);

        var content = new StringBuilder();
        var toolCalls = new Dictionary<int, ToolCallBuilder>();
        JsonObject? finalUsage = null;

        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            string? line;
            try {
                line = await reader.ReadLineAsync().ConfigureAwait(false);
            } catch (Exception) when (cancellationToken.IsCancellationRequested) {
                throw new OperationCanceledException(cancellationToken);
            }
            if (line is null) {
                break;
            }
            if (line.Length == 0) {
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var data = line.Substring("data:".Length).Trim();
            if (data.Length == 0) {
                continue;
            }
            if (string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase)) {
                break;
            }

            JsonValue? parsed;
            try {
                parsed = JsonLite.Parse(data);
            } catch {
                continue;
            }
            var obj = parsed?.AsObject();
            if (obj is null) {
                continue;
            }

            // Best-effort usage parsing: some servers include usage on the final chunk.
            finalUsage ??= obj.GetObject("usage");

            var choices = obj.GetArray("choices");
            var first = choices?.Count > 0 ? choices[0].AsObject() : null;
            var delta = first?.GetObject("delta");
            if (delta is null) {
                continue;
            }

            var deltaContent = delta.GetString("content");
            if (!string.IsNullOrEmpty(deltaContent)) {
                content.Append(deltaContent);
                DeltaReceived?.Invoke(this, deltaContent!);
            }

            var deltaToolCalls = delta.GetArray("tool_calls");
            if (deltaToolCalls is not null && deltaToolCalls.Count > 0) {
                for (var i = 0; i < deltaToolCalls.Count; i++) {
                    var toolObj = deltaToolCalls[i].AsObject();
                    if (toolObj is null) {
                        continue;
                    }

                    var index = (int?)(toolObj.GetInt64("index") ?? i);
                    if (!index.HasValue || index.Value < 0) {
                        continue;
                    }

                    if (!toolCalls.TryGetValue(index.Value, out var builder)) {
                        builder = new ToolCallBuilder();
                        toolCalls[index.Value] = builder;
                    }

                    var id = toolObj.GetString("id");
                    if (!string.IsNullOrWhiteSpace(id)) {
                        builder.Id = id!.Trim();
                    }

                    var function = toolObj.GetObject("function");
                    var name = function?.GetString("name");
                    if (!string.IsNullOrWhiteSpace(name)) {
                        builder.Name = name!.Trim();
                    }

                    var args = function?.GetString("arguments");
                    if (!string.IsNullOrEmpty(args)) {
                        builder.Arguments.Append(args);
                    }
                }
            }
        }

        var assistantMessage = BuildAssistantMessageForHistory(content.ToString(), toolCalls);
        var turn = BuildTurnFromAssistantMessage(assistantMessage, usageObj: finalUsage);
        return new ChatCompletionResponse(turn, assistantMessage);
    }

    private static ChatCompletionResponse BuildTurnFromChatCompletions(JsonObject responseObj) {
        // OpenAI-compatible chat completions response shape.
        var choices = responseObj.GetArray("choices");
        var first = choices?.Count > 0 ? choices[0].AsObject() : null;
        var message = first?.GetObject("message");
        if (message is null) {
            throw new InvalidOperationException("Invalid chat response (missing choices[0].message).");
        }

        var assistantMessage = new JsonObject()
            .Add("role", "assistant");

        var content = message.GetString("content");
        if (content is not null) {
            assistantMessage.Add("content", content);
        }

        var toolCalls = message.GetArray("tool_calls");
        if (toolCalls is not null) {
            assistantMessage.Add("tool_calls", toolCalls);
        }

        var usage = responseObj.GetObject("usage");
        var turn = BuildTurnFromAssistantMessage(assistantMessage, usage);
        return new ChatCompletionResponse(turn, assistantMessage);
    }

    private static TurnInfo BuildTurnFromAssistantMessage(JsonObject assistantMessageForHistory, JsonObject? usageObj) {
        var outputs = new List<TurnOutput>();
        var rawOutputs = new JsonArray();

        var toolCalls = assistantMessageForHistory.GetArray("tool_calls");
        if (toolCalls is not null && toolCalls.Count > 0) {
            for (var i = 0; i < toolCalls.Count; i++) {
                var tool = toolCalls[i].AsObject();
                if (tool is null) {
                    continue;
                }

                // Convert to the output item shape used across IntelligenceX (so ToolCallParser can extract).
                var outputObj = new JsonObject()
                    .Add("type", "tool_call");

                var id = tool.GetString("id");
                if (!string.IsNullOrWhiteSpace(id)) {
                    outputObj.Add("id", id!.Trim());
                    outputObj.Add("tool_call_id", id!.Trim());
                    outputObj.Add("call_id", id!.Trim());
                }

                var function = tool.GetObject("function");
                if (function is not null) {
                    outputObj.Add("function", function);

                    // Also include OpenAI-style fields at the root so ToolCall.FromJson can parse arguments reliably.
                    var name = function.GetString("name");
                    if (!string.IsNullOrWhiteSpace(name)) {
                        outputObj.Add("name", name!.Trim());
                    }
                    var args = function.GetString("arguments");
                    if (args is not null) {
                        outputObj.Add("arguments", args);
                    }
                }

                rawOutputs.Add(outputObj);
                outputs.Add(new TurnOutput(
                    type: "tool_call",
                    text: null,
                    imageUrl: null,
                    imagePath: null,
                    base64: null,
                    mimeType: null,
                    raw: outputObj,
                    additional: null));
            }
        }

        var content = assistantMessageForHistory.GetString("content");
        if (!string.IsNullOrWhiteSpace(content)) {
            var outputObj = new JsonObject()
                .Add("type", "text")
                .Add("text", content);
            rawOutputs.Add(outputObj);
            outputs.Add(new TurnOutput(
                type: "text",
                text: content,
                imageUrl: null,
                imagePath: null,
                base64: null,
                mimeType: null,
                raw: outputObj,
                additional: null));
        }

        var turnId = Guid.NewGuid().ToString("N");
        var turnRaw = new JsonObject()
            .Add("id", turnId)
            .Add("status", "completed")
            .Add("outputs", rawOutputs);

        TurnUsage? usage = null;
        if (usageObj is not null) {
            usage = TurnUsage.FromJson(usageObj);
            turnRaw.Add("usage", usageObj);
        }

        return new TurnInfo(turnId, responseId: null, status: "completed", outputs, Array.Empty<TurnOutput>(), turnRaw, additional: null, usage);
    }

    private static JsonObject BuildAssistantMessageForHistory(string content, Dictionary<int, ToolCallBuilder> toolCallsByIndex) {
        var msg = new JsonObject()
            .Add("role", "assistant");

        if (!string.IsNullOrEmpty(content)) {
            msg.Add("content", content);
        }

        if (toolCallsByIndex.Count > 0) {
            var arr = new JsonArray();
            foreach (var kvp in toolCallsByIndex.OrderBy(k => k.Key)) {
                var builder = kvp.Value;
                var id = string.IsNullOrWhiteSpace(builder.Id) ? $"call_{kvp.Key}" : builder.Id!.Trim();
                var name = string.IsNullOrWhiteSpace(builder.Name) ? "unknown_tool" : builder.Name!.Trim();
                var args = builder.Arguments.Length == 0 ? "{}" : builder.Arguments.ToString();

                var toolObj = new JsonObject()
                    .Add("id", id)
                    .Add("type", "function")
                    .Add("function", new JsonObject()
                        .Add("name", name)
                        .Add("arguments", args));
                arr.Add(toolObj);
            }
            msg.Add("tool_calls", arr);
        }

        return msg;
    }

    private static List<JsonObject> BuildMessagesFromInput(ChatInput input) {
        var items = input.ToJson();
        if (items is null || items.Count == 0) {
            return new List<JsonObject>();
        }

        var messages = new List<JsonObject>();
        var userText = new StringBuilder();

        void FlushUserText() {
            if (userText.Length == 0) {
                return;
            }
            messages.Add(new JsonObject()
                .Add("role", "user")
                .Add("content", userText.ToString()));
            userText.Clear();
        }

        for (var i = 0; i < items.Count; i++) {
            var item = items[i].AsObject();
            if (item is null) {
                continue;
            }

            var type = item.GetString("type") ?? string.Empty;
            switch (type) {
                case "text": {
                        var text = item.GetString("text");
                        if (string.IsNullOrWhiteSpace(text)) {
                            continue;
                        }

                        if (userText.Length > 0) {
                            userText.Append('\n');
                        }
                        userText.Append(text);
                        break;
                    }
                case "custom_tool_call_output": {
                        FlushUserText();
                        var callId = item.GetString("call_id");
                        if (string.IsNullOrWhiteSpace(callId)) {
                            throw new InvalidOperationException("Tool output item is missing call_id.");
                        }

                        var output = item.GetString("output") ?? string.Empty;
                        messages.Add(new JsonObject()
                            .Add("role", "tool")
                            .Add("tool_call_id", callId!.Trim())
                            .Add("content", output));
                        break;
                    }
                case "image":
                    throw new NotSupportedException("CompatibleHttp transport does not currently support image inputs.");
                default:
                    throw new NotSupportedException($"Unsupported chat input item type '{type}'.");
            }
        }

        FlushUserText();

        return messages;
    }

    private JsonObject BuildChatCompletionsRequest(string model, IReadOnlyList<JsonObject> messages, ChatOptions options, bool streaming) {
        var messageArray = new JsonArray();
        for (var i = 0; i < messages.Count; i++) {
            messageArray.Add(JsonValue.From(messages[i]));
        }

        var body = new JsonObject()
            .Add("model", model)
            .Add("messages", messageArray);

        body.Add("stream", streaming);

        if (options.Temperature.HasValue) {
            body.Add("temperature", options.Temperature.Value);
        }

        if (options.Tools is { Count: > 0 }) {
            var tools = new JsonArray();
            foreach (var def in options.Tools) {
                if (def is null) {
                    continue;
                }
                var fn = new JsonObject()
                    .Add("name", def.Name)
                    .Add("description", def.GetDescriptionWithTags() ?? string.Empty);
                if (def.Parameters is not null) {
                    fn.Add("parameters", def.Parameters);
                }
                tools.Add(new JsonObject()
                    .Add("type", "function")
                    .Add("function", fn));
            }
            if (tools.Count > 0) {
                body.Add("tools", tools);
            }

            if (options.ToolChoice is not null) {
                body.Add("tool_choice", BuildToolChoice(options.ToolChoice));
            }
        }

        if (options.ParallelToolCalls.HasValue) {
            // OpenAI-compatible servers may ignore this; it's still useful for those that support it.
            body.Add("parallel_tool_calls", options.ParallelToolCalls.Value);
        }

        if (options.ReasoningEffort.HasValue) {
            body.Add("reasoning_effort", options.ReasoningEffort.Value.ToString().ToLowerInvariant());
        }

        if (options.ReasoningSummary.HasValue) {
            body.Add("reasoning_summary", options.ReasoningSummary.Value.ToString().ToLowerInvariant());
        }

        return body;
    }

    private static JsonValue BuildToolChoice(ToolChoice toolChoice) {
        if (toolChoice is null) {
            return JsonValue.Null;
        }

        var type = (toolChoice.Type ?? string.Empty).Trim();
        if (type.Equals("auto", StringComparison.OrdinalIgnoreCase)) {
            return JsonValue.From("auto");
        }
        if (type.Equals("none", StringComparison.OrdinalIgnoreCase)) {
            return JsonValue.From("none");
        }
        if (type.Equals("custom", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(toolChoice.Name)) {
            return JsonValue.From(new JsonObject()
                .Add("type", "function")
                .Add("function", new JsonObject().Add("name", toolChoice.Name!.Trim())));
        }

        return JsonValue.From("auto");
    }

    private void AddAuthHeader(HttpRequestMessage request) {
        if (request is null) {
            return;
        }
        if (string.IsNullOrWhiteSpace(_options.ApiKey)) {
            return;
        }
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey!.Trim());
    }

    private static Uri NormalizeBaseUrl(string baseUrl) {
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) || uri is null) {
            throw new ArgumentException("BaseUrl must be an absolute URI.", nameof(baseUrl));
        }

        // Normalize so common local-provider forms work:
        // - http://localhost:11434          -> http://localhost:11434/v1/
        // - http://localhost:1234/v1       -> http://localhost:1234/v1/
        // - https://example.com/openai/v1  -> https://example.com/openai/v1/
        var builder = new UriBuilder(uri);
        var path = builder.Path ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path) || path == "/") {
            builder.Path = "/v1/";
            return builder.Uri;
        }

        path = path.TrimEnd('/');
        if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) {
            builder.Path = path + "/";
            return builder.Uri;
        }

        var finalPath = builder.Path ?? string.Empty;
        if (!finalPath.EndsWith("/", StringComparison.Ordinal)) {
            builder.Path = finalPath + "/";
        }
        return builder.Uri;
    }

    private static Uri? BuildLmStudioModelsUrl(Uri apiBase) {
        if (!IsLikelyLmStudioEndpoint(apiBase)) {
            return null;
        }

        var builder = new UriBuilder(apiBase) {
            Path = "/api/v0/models",
            Query = string.Empty
        };
        return builder.Uri;
    }

    private static bool IsLikelyLmStudioEndpoint(Uri apiBase) {
        if (apiBase is null) {
            return false;
        }

        if (apiBase.Port == 1234) {
            return true;
        }

        var host = apiBase.Host ?? string.Empty;
        return host.IndexOf("lmstudio", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Task<string> ReadAsStringAsync(HttpContent content, CancellationToken cancellationToken) {
#if NETSTANDARD2_0 || NET472
        cancellationToken.ThrowIfCancellationRequested();
        return content.ReadAsStringAsync();
#else
        return content.ReadAsStringAsync(cancellationToken);
#endif
    }

    private static Task<Stream> ReadAsStreamAsync(HttpContent content, CancellationToken cancellationToken) {
#if NETSTANDARD2_0 || NET472
        cancellationToken.ThrowIfCancellationRequested();
        return content.ReadAsStreamAsync();
#else
        return content.ReadAsStreamAsync(cancellationToken);
#endif
    }

    public void Dispose() {
        _http.Dispose();
    }

    private sealed class CompatibleThreadState {
        public CompatibleThreadState(string model) {
            Model = model;
        }

        public string Model { get; set; }
        public List<JsonObject> Messages { get; } = new();
        public string? Instructions { get; private set; }

        public void SetInstructions(string instructions) {
            if (string.IsNullOrWhiteSpace(instructions)) {
                return;
            }

            var normalized = instructions.Trim();
            if (string.Equals(Instructions, normalized, StringComparison.Ordinal)) {
                return;
            }

            Instructions = normalized;
            var sys = new JsonObject()
                .Add("role", "system")
                .Add("content", normalized);

            if (Messages.Count > 0 && string.Equals(Messages[0].GetString("role"), "system", StringComparison.OrdinalIgnoreCase)) {
                Messages[0] = sys;
            } else {
                Messages.Insert(0, sys);
            }
        }
    }

    private sealed class ToolCallBuilder {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StringBuilder Arguments { get; } = new();
    }

    private sealed class ChatCompletionResponse {
        public ChatCompletionResponse(TurnInfo turn, JsonObject assistantMessageForHistory) {
            Turn = turn;
            AssistantMessageForHistory = assistantMessageForHistory;
        }

        public TurnInfo Turn { get; }
        public JsonObject AssistantMessageForHistory { get; }
    }
}
