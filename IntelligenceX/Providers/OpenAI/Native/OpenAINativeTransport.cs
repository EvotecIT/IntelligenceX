using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.OpenAI.Transport;
using IntelligenceX.Telemetry;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI.Native;

internal sealed partial class OpenAINativeTransport : IOpenAITransport {
    private readonly OpenAINativeOptions _options;
    private readonly HttpClient _httpClient = new();
    private readonly OpenAINativeAuthManager _auth;
    private readonly OpenAINativeThreadStore _threads = new();

    public OpenAINativeTransport(OpenAINativeOptions options) {
        _options = options;
        _options.Validate();
        _auth = new OpenAINativeAuthManager(_options);
    }

    public OpenAITransportKind Kind => OpenAITransportKind.Native;
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
        // Native transport does not require a separate initialize step.
        return Task.CompletedTask;
    }

    public async Task<HealthCheckResult> HealthCheckAsync(string? method, TimeSpan? timeout, CancellationToken cancellationToken) {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try {
            await _auth.TryGetValidBundleAsync(cancellationToken).ConfigureAwait(false);
            return new HealthCheckResult(true, "native/auth", null, sw.Elapsed);
        } catch (Exception ex) {
            return new HealthCheckResult(false, "native/auth", ex, sw.Elapsed);
        }
    }

    public async Task<AccountInfo> GetAccountAsync(CancellationToken cancellationToken) {
        var bundle = await _auth.TryGetValidBundleAsync(cancellationToken).ConfigureAwait(false);
        if (bundle is null || string.IsNullOrWhiteSpace(bundle.AccessToken)) {
            throw new OpenAIAuthenticationRequiredException(OpenAIAuthenticationRequiredException.DefaultMessage);
        }
        bundle.AccountId ??= JwtDecoder.TryGetAccountId(bundle.AccessToken);
        if (string.IsNullOrWhiteSpace(bundle.AccountId)) {
            throw new InvalidOperationException("Failed to extract account id from access token.");
        }
        var raw = new JsonObject()
            .Add("id", bundle.AccountId);
        return AccountInfo.FromJson(raw);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken) {
        if (_options.AuthStore is FileAuthBundleStore fileStore) {
            try {
                fileStore.Delete();
            } catch {
                // Best-effort logout.
            }
        }
        await Task.CompletedTask;
    }

    public async Task<ModelListResult> ListModelsAsync(CancellationToken cancellationToken) {
        var bundle = await EnsureAuthAsync(cancellationToken).ConfigureAwait(false);
        var accountId = bundle.AccountId ?? JwtDecoder.TryGetAccountId(bundle.AccessToken);
        if (string.IsNullOrWhiteSpace(accountId)) {
            throw new InvalidOperationException("Failed to extract account id from access token.");
        }

        var errors = new List<string>();
        foreach (var url in _options.ModelUrls) {
            if (string.IsNullOrWhiteSpace(url)) {
                continue;
            }
            try {
                return await FetchModelsAsync(AddClientVersion(url), bundle.AccessToken, accountId!, cancellationToken)
                    .ConfigureAwait(false);
            } catch (Exception ex) {
                errors.Add($"{url}: {ex.Message}");
            }
        }

        var message = errors.Count == 0
            ? "No model endpoints configured."
            : "Failed to list models. " + string.Join(" | ", errors);
        throw new InvalidOperationException(message);
    }

    public async Task<ChatGptLoginStart> LoginChatGptAsync(Action<string>? onUrl, Func<string, Task<string>>? onPrompt,
        bool useLocalListener, TimeSpan timeout, CancellationToken cancellationToken) {
        var loginId = Guid.NewGuid().ToString("N");
        string? authUrl = null;

        LoginStarted?.Invoke(this, new LoginEventArgs("chatgpt", loginId));
        var bundle = await _auth.LoginAsync(url => {
            authUrl = url;
            onUrl?.Invoke(url);
            LoginStarted?.Invoke(this, new LoginEventArgs("chatgpt", loginId, url));
        }, onPrompt, useLocalListener, timeout, cancellationToken).ConfigureAwait(false);

        bundle.AccountId ??= JwtDecoder.TryGetAccountId(bundle.AccessToken);
        LoginCompleted?.Invoke(this, new LoginEventArgs("chatgpt", loginId, authUrl));

        var raw = new JsonObject()
            .Add("loginId", loginId)
            .Add("authUrl", authUrl ?? string.Empty);
        return ChatGptLoginStart.FromJson(raw);
    }

    public Task LoginApiKeyAsync(string apiKey, CancellationToken cancellationToken) {
        throw new NotSupportedException("API key login is not supported with the native ChatGPT transport.");
    }

    public Task<ThreadInfo> StartThreadAsync(string model, string? currentDirectory, string? approvalPolicy,
        string? sandbox, CancellationToken cancellationToken) {
        var state = _threads.StartNew(model);
        return Task.FromResult(state.ToThreadInfo());
    }

    public Task<ThreadInfo> ResumeThreadAsync(string threadId, CancellationToken cancellationToken) {
        var state = _threads.Resume(threadId, model: string.Empty);
        return Task.FromResult(state.ToThreadInfo());
    }

    public async Task<TurnInfo> StartTurnAsync(string threadId, ChatInput input, ChatOptions? options, string? currentDirectory,
        string? approvalPolicy, SandboxPolicy? sandboxPolicy, CancellationToken cancellationToken) {
        if (!_threads.TryGet(threadId, out var state)) {
            state = _threads.Resume(threadId, options?.Model ?? string.Empty);
        }

        options ??= new ChatOptions();
        var resolvedModel = NormalizeModelId(options.Model, state.Model);
        state.Touch(resolvedModel);

        var bundle = await EnsureAuthAsync(cancellationToken).ConfigureAwait(false);
        var accountId = bundle.AccountId ?? JwtDecoder.TryGetAccountId(bundle.AccessToken);
        if (string.IsNullOrWhiteSpace(accountId)) {
            throw new InvalidOperationException("Failed to extract account id from access token.");
        }

        var inputItems = await BuildInputItemsAsync(input, cancellationToken).ConfigureAwait(false);
        var trackMessages = options.PreviousResponseId is null;
        var requestMessages = trackMessages
            ? new List<JsonObject>(state.Messages.Count + inputItems.Count)
            : new List<JsonObject>(inputItems.Count);
        if (trackMessages) {
            requestMessages.AddRange(state.Messages);
        }
        requestMessages.AddRange(inputItems);

        var body = BuildRequestBody(resolvedModel, requestMessages, state.SessionId, options);
        var turnId = Guid.NewGuid().ToString("N");

        var rpcParameters = JsonValue.From(body);
        RpcCallStarted?.Invoke(this, new RpcCallStartedEventArgs("responses.create", rpcParameters));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try {
            var turn = await SendWithModelFallbackAsync(body, requestMessages, bundle.AccessToken, accountId!, state, inputItems, trackMessages,
                    resolvedModel, turnId, options, cancellationToken)
                .ConfigureAwait(false);
            RpcCallCompleted?.Invoke(this, new RpcCallCompletedEventArgs("responses.create", sw.Elapsed, true));
            return turn;
        } catch (Exception ex) {
            RpcCallCompleted?.Invoke(this, new RpcCallCompletedEventArgs("responses.create", sw.Elapsed, false, ex));
            throw;
        }
    }

    private async Task<AuthBundle> EnsureAuthAsync(CancellationToken cancellationToken) {
        var bundle = await _auth.TryGetValidBundleAsync(cancellationToken).ConfigureAwait(false);
        if (bundle is not null) {
            return bundle;
        }
        throw new OpenAIAuthenticationRequiredException(OpenAIAuthenticationRequiredException.DefaultMessage);
    }

    private async Task<HttpResponseMessage> SendAsync(JsonObject body, string accessToken, string accountId, string sessionId,
        CancellationToken cancellationToken) {
        var json = JsonLite.Serialize(JsonValue.From(body));
        var content = new StringContent(json, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, _options.ResponsesUrl) {
            Content = content
        };

        var headers = BuildHeaders(accessToken, accountId, sessionId);
        foreach (var header in headers) {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        TryDumpRequest(request, json);

        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ModelListResult> FetchModelsAsync(string url, string accessToken, string accountId, CancellationToken cancellationToken) {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var header in BuildHeaders(accessToken, accountId, Guid.NewGuid().ToString("N"))) {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        request.Headers.Remove("accept");
        request.Headers.TryAddWithoutValidation("accept", "application/json");

        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"Model list request failed ({(int)response.StatusCode}): {payload}");
        }
        var value = JsonLite.Parse(payload);
        var obj = value?.AsObject();
        if (obj is null) {
            var array = value?.AsArray();
            if (array is null) {
                throw new InvalidOperationException("Invalid model list response.");
            }
            obj = new JsonObject().Add("models", array);
        }
        return ModelListResult.FromJson(obj);
    }

    private string AddClientVersion(string url) {
        var version = _options.ClientVersion;
        if (string.IsNullOrWhiteSpace(version) || url.IndexOf("client_version=", StringComparison.OrdinalIgnoreCase) >= 0) {
            return url;
        }
        var separator = url.Contains("?") ? "&" : "?";
        return $"{url}{separator}client_version={Uri.EscapeDataString(version)}";
    }

    private async Task<TurnInfo> ProcessResponseAsync(HttpResponseMessage response, string turnId, string model,
        NativeThreadState state, IReadOnlyList<JsonObject> inputItems, bool trackMessages, CancellationToken cancellationToken) {
        if (!response.IsSuccessStatusCode) {
            var error = await ParseErrorResponseAsync(response, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) {
                var message = string.IsNullOrWhiteSpace(error.Message)
                    ? OpenAIAuthenticationRequiredException.DefaultMessage
                    : error.Message;
                throw new OpenAIAuthenticationRequiredException(message);
            }
            throw new OpenAINativeErrorResponseException(error.Message, error.Code, error.Param, response.StatusCode);
        }

        var delta = new StringBuilder();
        string? status = null;
        JsonObject? completedResponse = null;
        string? streamError = null;

        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await OpenAINativeSseParser.ParseAsync(stream, evt => {
            HandleStreamEvent(evt, delta, ref status, ref completedResponse, ref streamError);
            return Task.CompletedTask;
        }, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(streamError)) {
            throw new InvalidOperationException(streamError);
        }

        var outputs = completedResponse is not null
            ? ParseOutputsFromResponse(completedResponse)
            : BuildOutputsFromDelta(delta.ToString());
        if (outputs.Count == 0 && delta.Length > 0) {
            outputs.Add(new JsonObject().Add("type", "text").Add("text", delta.ToString()));
        }

        var assistantText = ExtractAssistantText(outputs);
        var assistantMessage = BuildAssistantMessage(assistantText, state);

        if (trackMessages) {
            if (inputItems.Count > 0) {
                state.Messages.AddRange(inputItems);
            }
            state.Messages.Add(assistantMessage);
        }
        state.UpdatePreview(TruncatePreview(assistantText));

        var outputArray = new JsonArray();
        foreach (var output in outputs) {
            outputArray.Add(output);
        }

        var responseId = completedResponse?.GetString("id");
        var rawTurn = new JsonObject()
            .Add("id", turnId)
            .Add("status", status ?? "completed")
            .Add("response", completedResponse ?? new JsonObject().Add("status", status ?? "completed"))
            .Add("output", outputArray);
        if (!string.IsNullOrWhiteSpace(responseId)) {
            rawTurn.Add("responseId", responseId);
        }
        return TurnInfo.FromJson(rawTurn);
    }

    private async Task<TurnInfo> SendWithModelFallbackAsync(JsonObject body, IReadOnlyList<JsonObject> requestMessages,
        string accessToken, string accountId, NativeThreadState state, IReadOnlyList<JsonObject> inputItems, bool trackMessages,
        string model, string turnId, ChatOptions options, CancellationToken cancellationToken) {
        try {
            return await SendWithToolSchemaFallbackAsync(body, requestMessages, accessToken, accountId, state, inputItems, trackMessages,
                    model, turnId, options, cancellationToken)
                .ConfigureAwait(false);
        } catch (InvalidOperationException ex) when (IsModelNotSupportedForChatGpt(ex)) {
            foreach (var fallback in GetChatGptFallbackModels(model)) {
                state.Touch(fallback);
                var retryBody = BuildRequestBody(fallback, requestMessages, state.SessionId, options);
                try {
                    return await SendWithToolSchemaFallbackAsync(retryBody, requestMessages, accessToken, accountId, state, inputItems, trackMessages,
                            fallback, turnId, options, cancellationToken)
                        .ConfigureAwait(false);
                } catch (InvalidOperationException retryEx) when (IsModelNotSupportedForChatGpt(retryEx)) {
                    continue;
                }
            }

            throw;
        }
    }

    private async Task<TurnInfo> SendWithToolSchemaFallbackAsync(JsonObject body, IReadOnlyList<JsonObject> requestMessages,
        string accessToken, string accountId, NativeThreadState state, IReadOnlyList<JsonObject> inputItems, bool trackMessages,
        string model, string turnId, ChatOptions options, CancellationToken cancellationToken) {
        if (options.Tools is null || options.Tools.Count == 0) {
            using var response = await SendAsync(body, accessToken, accountId, state.SessionId, cancellationToken)
                .ConfigureAwait(false);
            return await ProcessResponseAsync(response, turnId, model, state, inputItems, trackMessages, cancellationToken)
                .ConfigureAwait(false);
        }

        var attempted = new HashSet<ToolWireFormat>();
        ExceptionDispatchInfo? lastError = null;

        async Task<TurnInfo> SendWithWireFormatAsync(ToolWireFormat toolWireFormat, JsonObject? prebuiltBody = null) {
            cancellationToken.ThrowIfCancellationRequested();
            var requestBody = prebuiltBody ?? BuildRequestBody(model, requestMessages, state.SessionId, options, toolWireFormat);
            using var response = await SendAsync(requestBody, accessToken, accountId, state.SessionId, cancellationToken)
                .ConfigureAwait(false);
            return await ProcessResponseAsync(response, turnId, model, state, inputItems, trackMessages, cancellationToken)
                .ConfigureAwait(false);
        }

        ToolSchemaKey retryKey;
        attempted.Add(ToolWireFormat.CustomParameters);
        try {
            return await SendWithWireFormatAsync(ToolWireFormat.CustomParameters, body).ConfigureAwait(false);
        } catch (Exception ex) {
            lastError = ExceptionDispatchInfo.Capture(ex);
            if (!TryGetToolSchemaKeyFallback(ex, out retryKey)) {
                throw;
            }
        }

        // Retry with the alternate custom-tool schema key first.
        var retryFormat = retryKey == ToolSchemaKey.InputSchema ? ToolWireFormat.CustomInputSchema : ToolWireFormat.CustomParameters;
        if (!attempted.Add(retryFormat)) {
            lastError!.Throw();
        }

        ToolSchemaKey retryKey2;
        try {
            return await SendWithWireFormatAsync(retryFormat).ConfigureAwait(false);
        } catch (Exception ex) {
            lastError = ExceptionDispatchInfo.Capture(ex);
            if (!TryGetToolSchemaKeyFallback(ex, out retryKey2)) {
                throw;
            }
        }

        // Some ChatGPT native variants don't accept custom tool schema fields at all. Fall back to function-style tools.
        var initialFunctionFormat = retryKey2 == ToolSchemaKey.InputSchema
            ? ToolWireFormat.FunctionFlatInputSchema
            : ToolWireFormat.FunctionFlatParameters;
        if (!attempted.Add(initialFunctionFormat)) {
            lastError!.Throw();
        }

        ToolSchemaKey functionRetryKey;
        try {
            return await SendWithWireFormatAsync(initialFunctionFormat).ConfigureAwait(false);
        } catch (Exception ex) {
            lastError = ExceptionDispatchInfo.Capture(ex);
            if (!TryGetToolSchemaKeyFallback(ex, out functionRetryKey)) {
                throw;
            }
        }

        // Retry function-style request with the alternate key, ensuring we don't resend the same format.
        var functionRetryFormat = functionRetryKey == ToolSchemaKey.InputSchema
            ? ToolWireFormat.FunctionFlatInputSchema
            : ToolWireFormat.FunctionFlatParameters;
        if (functionRetryFormat == initialFunctionFormat) {
            functionRetryFormat = functionRetryFormat == ToolWireFormat.FunctionFlatInputSchema
                ? ToolWireFormat.FunctionFlatParameters
                : ToolWireFormat.FunctionFlatInputSchema;
        }

        if (!attempted.Add(functionRetryFormat)) {
            lastError!.Throw();
        }

        return await SendWithWireFormatAsync(functionRetryFormat).ConfigureAwait(false);
    }

    private void HandleStreamEvent(JsonObject evt, StringBuilder delta, ref string? status, ref JsonObject? completedResponse,
        ref string? streamError) {
        var type = evt.GetString("type");
        if (string.IsNullOrWhiteSpace(type)) {
            return;
        }

        if (string.Equals(type, "response.output_text.delta", StringComparison.Ordinal)) {
            var piece = evt.GetString("delta");
            if (!string.IsNullOrWhiteSpace(piece)) {
                delta.Append(piece);
                DeltaReceived?.Invoke(this, piece!);
            }
            return;
        }

        if (string.Equals(type, "response.refusal.delta", StringComparison.Ordinal)) {
            var piece = evt.GetString("delta");
            if (!string.IsNullOrWhiteSpace(piece)) {
                delta.Append(piece);
                DeltaReceived?.Invoke(this, piece!);
            }
            return;
        }

        if (string.Equals(type, "response.completed", StringComparison.Ordinal) ||
            string.Equals(type, "response.done", StringComparison.Ordinal)) {
            completedResponse = evt.GetObject("response");
            status = completedResponse?.GetString("status") ?? status;
            return;
        }

        if (string.Equals(type, "response.failed", StringComparison.Ordinal)) {
            var response = evt.GetObject("response");
            var error = response?.GetObject("error");
            streamError = error?.GetString("message") ?? "ChatGPT response failed.";
            return;
        }

        if (string.Equals(type, "error", StringComparison.Ordinal)) {
            streamError = evt.GetString("message") ?? evt.GetString("code") ?? "ChatGPT error.";
        }
    }

    // Request/headers/tool serialization moved to OpenAINativeTransport.Requests.cs (partial class) to keep file sizes reasonable.

    private static JsonObject BuildAssistantMessage(string text, NativeThreadState state) {
        var content = new JsonArray();
        if (!string.IsNullOrWhiteSpace(text)) {
            content.Add(new JsonObject().Add("type", "output_text").Add("text", text).Add("annotations", new JsonArray()));
        }
        return new JsonObject()
            .Add("type", "message")
            .Add("role", "assistant")
            .Add("content", content)
            .Add("status", "completed")
            .Add("id", state.NextMessageId());
    }

    private static bool IsModelNotSupportedForChatGpt(InvalidOperationException ex) {
        var message = ex.Message ?? string.Empty;
        return message.IndexOf("model is not supported", StringComparison.OrdinalIgnoreCase) >= 0 &&
               message.IndexOf("chatgpt account", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static IEnumerable<string> GetChatGptFallbackModels(string currentModel) {
        var candidates = new[] { "gpt-5.3-codex", "gpt-5.3", "gpt-5.2-codex", "gpt-5.2", "gpt-5.1-codex", "gpt-5.1" };
        foreach (var candidate in candidates) {
            if (!string.Equals(candidate, currentModel, StringComparison.OrdinalIgnoreCase)) {
                yield return candidate;
            }
        }
    }

    private static List<JsonObject> BuildOutputsFromDelta(string text) {
        var list = new List<JsonObject>();
        if (!string.IsNullOrWhiteSpace(text)) {
            list.Add(new JsonObject().Add("type", "text").Add("text", text));
        }
        return list;
    }

    private static List<JsonObject> ParseOutputsFromResponse(JsonObject response) {
        var outputs = new List<JsonObject>();
        var outputArray = response.GetArray("output");
        if (outputArray is null) {
            return outputs;
        }

        foreach (var itemValue in outputArray) {
            var item = itemValue.AsObject();
            if (item is null) {
                continue;
            }
            var type = item.GetString("type");
            if (string.Equals(type, "message", StringComparison.Ordinal)) {
                var content = item.GetArray("content");
                if (content is not null) {
                    ParseContentParts(content, outputs);
                }
                continue;
            }
            if (string.Equals(type, "output_image", StringComparison.Ordinal)) {
                AddImageOutput(item, outputs);
                continue;
            }
            if (string.Equals(type, "custom_tool_call", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "tool_call", StringComparison.OrdinalIgnoreCase)) {
                outputs.Add(item);
                continue;
            }
            var text = item.GetString("text");
            if (!string.IsNullOrWhiteSpace(text)) {
                outputs.Add(new JsonObject().Add("type", "text").Add("text", text));
            }
        }

        return outputs;
    }

    private static void ParseContentParts(JsonArray content, List<JsonObject> outputs) {
        foreach (var partValue in content) {
            var part = partValue.AsObject();
            if (part is null) {
                continue;
            }
            var partType = part.GetString("type");
            if (string.Equals(partType, "output_text", StringComparison.Ordinal)) {
                var text = part.GetString("text");
                if (!string.IsNullOrWhiteSpace(text)) {
                    outputs.Add(new JsonObject().Add("type", "text").Add("text", text));
                }
                continue;
            }
            if (string.Equals(partType, "refusal", StringComparison.Ordinal)) {
                var refusal = part.GetString("refusal") ?? part.GetString("text");
                if (!string.IsNullOrWhiteSpace(refusal)) {
                    outputs.Add(new JsonObject().Add("type", "text").Add("text", refusal));
                }
                continue;
            }
            if (string.Equals(partType, "output_image", StringComparison.Ordinal) ||
                string.Equals(partType, "image", StringComparison.Ordinal)) {
                AddImageOutput(part, outputs);
            }
        }
    }

    private static void AddImageOutput(JsonObject part, List<JsonObject> outputs) {
        var url = part.GetString("image_url") ?? part.GetString("url");
        if (string.IsNullOrWhiteSpace(url)) {
            var imageUrlObj = part.GetObject("image_url");
            url = imageUrlObj?.GetString("url");
        }
        if (!string.IsNullOrWhiteSpace(url)) {
            outputs.Add(new JsonObject().Add("type", "image").Add("url", url));
        }
    }

    private static string ExtractAssistantText(IReadOnlyList<JsonObject> outputs) {
        var sb = new StringBuilder();
        foreach (var output in outputs) {
            var type = output.GetString("type");
            if (!string.Equals(type, "text", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            var text = output.GetString("text");
            if (string.IsNullOrWhiteSpace(text)) {
                continue;
            }
            if (sb.Length > 0) {
                sb.AppendLine();
            }
            sb.Append(text);
        }
        return sb.ToString();
    }

    private static string TruncatePreview(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return string.Empty;
        }
        const int max = 120;
        return text.Length <= max ? text : text.Substring(0, max);
    }

    private static async Task<byte[]> ReadFileBytesAsync(string path, CancellationToken cancellationToken) {
#if NETSTANDARD2_0 || NET472
        cancellationToken.ThrowIfCancellationRequested();
        return File.ReadAllBytes(path);
#else
        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
#endif
    }

    private static string GuessMimeType(string path) {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(ext)) {
            return "application/octet-stream";
        }
        var value = ext!.TrimStart('.').ToLowerInvariant();
        return value switch {
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "bmp" => "image/bmp",
            "tif" or "tiff" => "image/tiff",
            _ => "application/octet-stream"
        };
    }

    public void Dispose() {
        _httpClient.Dispose();
    }
}
