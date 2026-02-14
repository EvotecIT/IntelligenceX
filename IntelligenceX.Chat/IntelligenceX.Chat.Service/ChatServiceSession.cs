using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JsonValueKind = System.Text.Json.JsonValueKind;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed class ChatServiceSession {
    private const int MaxTrackedToolRoutingStats = 512;
    private const int MaxTrackedWeightedRoutingContexts = 256;
    private readonly ServiceOptions _options;
    private readonly Stream _stream;
    private readonly ToolRegistry _registry;
    private readonly IReadOnlyList<IToolPack> _packs;
    private readonly string[] _startupWarnings;
    private readonly string[] _pluginSearchPaths;
    private readonly Dictionary<string, string> _packDisplayNamesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _toolRoutingStatsLock = new();
    private readonly Dictionary<string, ToolRoutingStats> _toolRoutingStats = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _toolRoutingContextLock = new();
    private readonly Dictionary<string, string[]> _lastWeightedToolNamesByThreadId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _lastWeightedToolSubsetSeenUtcTicks = new(StringComparer.Ordinal);

    private readonly JsonSerializerOptions _json;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private readonly object _loginLock = new();
    private LoginFlow? _login;
    private readonly object _chatRunLock = new();
    private ChatRun? _activeChat;
    private static readonly Regex UserRequestSectionRegex =
        new(@"\bUser request:\s*(?<value>[\s\S]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ContinuationFollowUpRegex =
        new(@"\b(continue|go on|keep going|same|again|retry|more|next step)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
            | RegexOptions.Compiled);

    public ChatServiceSession(ServiceOptions options, Stream stream) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        var startupWarnings = new List<string>();
        var bootstrapOptions = new ToolPackBootstrapOptions {
            AllowedRoots = _options.AllowedRoots.ToArray(),
            AdDomainController = _options.AdDomainController,
            AdDefaultSearchBaseDn = _options.AdDefaultSearchBaseDn,
            AdMaxResults = _options.AdMaxResults,
            EnablePowerShellPack = _options.EnablePowerShellPack,
            PowerShellAllowWrite = _options.PowerShellAllowWrite,
            EnableTestimoXPack = _options.EnableTestimoXPack,
            EnableDefaultPluginPaths = _options.EnableDefaultPluginPaths,
            PluginPaths = _options.PluginPaths.ToArray(),
            OnBootstrapWarning = warning => RecordBootstrapWarning(startupWarnings, warning)
        };

        _packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(bootstrapOptions);
        _pluginSearchPaths = NormalizeDistinctStrings(ToolPackBootstrap.GetPluginSearchPaths(bootstrapOptions), maxItems: 32);
        _startupWarnings = NormalizeDistinctStrings(startupWarnings, maxItems: 64);
        _registry = new ToolRegistry();
        ToolPackBootstrap.RegisterAll(_registry, _packs);
        foreach (var descriptor in ToolPackBootstrap.GetDescriptors(_packs)) {
            var normalizedPackId = NormalizePackId(descriptor.Id);
            if (normalizedPackId.Length == 0) {
                continue;
            }

            _packDisplayNamesById[normalizedPackId] = ResolvePackDisplayName(descriptor.Id, descriptor.Name);
        }

        _json = new JsonSerializerOptions {
            TypeInfoResolver = ChatServiceJsonContext.Default
        };
    }

    public async Task RunAsync(CancellationToken cancellationToken) {
        var clientOptions = new IntelligenceXClientOptions {
            TransportKind = OpenAITransportKind.Native,
            DefaultModel = _options.Model
        };
        var instructions = LoadInstructions(_options);
        if (!string.IsNullOrWhiteSpace(instructions)) {
            clientOptions.NativeOptions.Instructions = instructions!;
        }

        await using var client = await IntelligenceXClient.ConnectAsync(clientOptions, cancellationToken).ConfigureAwait(false);

        using var reader = new StreamReader(_stream, leaveOpen: true);
        using var writer = new StreamWriter(_stream, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        string? activeThreadId = null;

        try {
            while (!cancellationToken.IsCancellationRequested) {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) {
                    break;
                }
                if (string.IsNullOrWhiteSpace(line)) {
                    continue;
                }

                ChatServiceRequest? request;
                try {
                    request = JsonSerializer.Deserialize(line, ChatServiceJsonContext.Default.ChatServiceRequest);
                } catch (Exception ex) {
                    await WriteAsync(writer, new ErrorMessage {
                        Kind = ChatServiceMessageKind.Response,
                        RequestId = null,
                        Error = $"Invalid request JSON: {ex.Message}",
                        Code = "invalid_json"
                    }, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (request is null) {
                    continue;
                }

                switch (request) {
                    case HelloRequest:
                        await WriteAsync(writer, new HelloMessage {
                            Kind = ChatServiceMessageKind.Response,
                            RequestId = request.RequestId,
                            Name = "IntelligenceX.Chat.Service",
                            Version = typeof(ChatServiceSession).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                            ProcessId = Environment.ProcessId.ToString(),
                            Policy = BuildSessionPolicy(_options, _packs, _startupWarnings, _pluginSearchPaths)
                        }, cancellationToken).ConfigureAwait(false);
                        break;

                    case EnsureLoginRequest login:
                        await HandleEnsureLoginAsync(client, writer, login, cancellationToken).ConfigureAwait(false);
                        break;

                    case StartChatGptLoginRequest startLogin:
                        await HandleStartChatGptLoginAsync(client, writer, startLogin, cancellationToken).ConfigureAwait(false);
                        break;

                    case ChatGptLoginPromptResponseRequest promptResponse:
                        await HandleChatGptLoginPromptResponseAsync(writer, promptResponse, cancellationToken).ConfigureAwait(false);
                        break;

                    case CancelChatGptLoginRequest cancelLogin:
                        await HandleCancelChatGptLoginAsync(writer, cancelLogin, cancellationToken).ConfigureAwait(false);
                        break;

                    case ListToolsRequest:
                        await HandleListToolsAsync(writer, request.RequestId, cancellationToken).ConfigureAwait(false);
                        break;

                    case InvokeToolRequest invokeTool:
                        await HandleInvokeToolAsync(writer, invokeTool, cancellationToken).ConfigureAwait(false);
                        break;

                    case CancelChatRequest cancelChat:
                        await HandleCancelChatAsync(writer, cancelChat, cancellationToken).ConfigureAwait(false);
                        break;

                    case ChatRequest chat:
                        activeThreadId = await HandleChatRequestAsync(client, writer, chat, activeThreadId, cancellationToken).ConfigureAwait(false);
                        break;

                    default:
                        await WriteAsync(writer, new ErrorMessage {
                            Kind = ChatServiceMessageKind.Response,
                            RequestId = request.RequestId,
                            Error = $"Unsupported request type: {request.GetType().Name}",
                            Code = "unsupported"
                        }, cancellationToken).ConfigureAwait(false);
                        break;
                }
            }
        } finally {
            await CancelActiveChatIfAnyAsync().ConfigureAwait(false);
            CancelLoginIfActive();
        }
    }

    private async Task TryWriteDeltaAsync(StreamWriter writer, string requestId, string threadId, string delta) {
        try {
            await WriteAsync(writer, new ChatDeltaMessage {
                Kind = ChatServiceMessageKind.Event,
                RequestId = requestId,
                ThreadId = threadId,
                Text = delta
            }, CancellationToken.None).ConfigureAwait(false);
        } catch {
            // Best-effort streaming; ignore pipe failures.
        }
    }

    private async Task TryWriteStatusAsync(StreamWriter writer, string requestId, string threadId, string status, string? toolName = null,
        string? toolCallId = null, long? durationMs = null, string? message = null) {
        try {
            await WriteAsync(writer, new ChatStatusMessage {
                Kind = ChatServiceMessageKind.Event,
                RequestId = requestId,
                ThreadId = threadId,
                Status = status,
                ToolName = toolName,
                ToolCallId = toolCallId,
                DurationMs = durationMs,
                Message = message
            }, CancellationToken.None).ConfigureAwait(false);
        } catch {
            // Best-effort; ignore pipe failures.
        }
    }

    private async Task HandleEnsureLoginAsync(IntelligenceXClient client, StreamWriter writer, EnsureLoginRequest request, CancellationToken cancellationToken) {
        if (request.ForceLogin) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "Use chatgpt_login_start to run an interactive ChatGPT OAuth login in the service.",
                Code = "use_chatgpt_login_start"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }
        try {
            var account = await client.GetAccountAsync(cancellationToken).ConfigureAwait(false);
            var accountId = account.AccountId;
            await WriteAsync(writer, new LoginStatusMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                IsAuthenticated = true,
                AccountId = accountId
            }, cancellationToken).ConfigureAwait(false);
        } catch (OpenAIAuthenticationRequiredException) {
            await WriteAsync(writer, new LoginStatusMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                IsAuthenticated = false,
                AccountId = null
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleStartChatGptLoginAsync(IntelligenceXClient client, StreamWriter writer, StartChatGptLoginRequest request,
        CancellationToken cancellationToken) {
        if (request.TimeoutSeconds <= 0 || request.TimeoutSeconds > 3600) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "timeoutSeconds must be between 1 and 3600.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        LoginFlow flow;
        lock (_loginLock) {
            if (_login is not null) {
                flow = _login;
                if (!flow.IsCompleted) {
                    // One login per connection/session for now.
                    _ = WriteAsync(writer, new ErrorMessage {
                        Kind = ChatServiceMessageKind.Response,
                        RequestId = request.RequestId,
                        Error = $"A login flow is already in progress (loginId={flow.LoginId}).",
                        Code = "login_in_progress"
                    }, cancellationToken);
                    return;
                }
                _login = null;
            }

            flow = new LoginFlow(Guid.NewGuid().ToString("N"), request.RequestId,
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
            _login = flow;
        }

        await WriteAsync(writer, new ChatGptLoginStartedMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = request.RequestId,
            LoginId = flow.LoginId
        }, cancellationToken).ConfigureAwait(false);

        // Run in the background so the session can continue to process prompt responses.
        flow.Task = Task.Run(async () => {
            try {
                await client.LoginChatGptAndWaitAsync(
                        onUrl: url => {
                            _ = WriteAsync(writer, new ChatGptLoginUrlMessage {
                                Kind = ChatServiceMessageKind.Event,
                                RequestId = flow.RequestId,
                                LoginId = flow.LoginId,
                                Url = url
                            }, CancellationToken.None);
                        },
                        onPrompt: prompt => OnLoginPromptAsync(writer, flow, prompt),
                        useLocalListener: request.UseLocalListener,
                        timeout: TimeSpan.FromSeconds(request.TimeoutSeconds),
                        cancellationToken: flow.Cts.Token)
                    .ConfigureAwait(false);

                await WriteAsync(writer, new ChatGptLoginCompletedMessage {
                    Kind = ChatServiceMessageKind.Event,
                    RequestId = flow.RequestId,
                    LoginId = flow.LoginId,
                    Ok = true,
                    Error = null
                }, CancellationToken.None).ConfigureAwait(false);
            } catch (OpenAIUserCanceledLoginException) {
                await WriteAsync(writer, new ChatGptLoginCompletedMessage {
                    Kind = ChatServiceMessageKind.Event,
                    RequestId = flow.RequestId,
                    LoginId = flow.LoginId,
                    Ok = false,
                    Error = "Canceled."
                }, CancellationToken.None).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                await WriteAsync(writer, new ChatGptLoginCompletedMessage {
                    Kind = ChatServiceMessageKind.Event,
                    RequestId = flow.RequestId,
                    LoginId = flow.LoginId,
                    Ok = false,
                    Error = "Canceled."
                }, CancellationToken.None).ConfigureAwait(false);
            } catch (Exception ex) {
                await WriteAsync(writer, new ChatGptLoginCompletedMessage {
                    Kind = ChatServiceMessageKind.Event,
                    RequestId = flow.RequestId,
                    LoginId = flow.LoginId,
                    Ok = false,
                    Error = ex.Message
                }, CancellationToken.None).ConfigureAwait(false);
            } finally {
                lock (_loginLock) {
                    if (ReferenceEquals(_login, flow)) {
                        _login = null;
                    }
                }
                flow.MarkCompleted();
            }
        }, CancellationToken.None);
    }

    private async Task<string> OnLoginPromptAsync(StreamWriter writer, LoginFlow flow, string prompt) {
        var promptId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        flow.SetPendingPrompt(promptId, tcs);

        await WriteAsync(writer, new ChatGptLoginPromptMessage {
            Kind = ChatServiceMessageKind.Event,
            RequestId = flow.RequestId,
            LoginId = flow.LoginId,
            PromptId = promptId,
            Prompt = prompt
        }, CancellationToken.None).ConfigureAwait(false);

        try {
            using var reg = flow.Cts.Token.Register(() => tcs.TrySetCanceled(flow.Cts.Token));
            var input = await tcs.Task.ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(input)) {
                throw new OpenAIUserCanceledLoginException();
            }
            return input;
        } finally {
            flow.ClearPendingPrompt(promptId);
        }
    }

    private async Task HandleChatGptLoginPromptResponseAsync(StreamWriter writer, ChatGptLoginPromptResponseRequest request,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(request.LoginId) || string.IsNullOrWhiteSpace(request.PromptId)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "loginId and promptId are required.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        LoginFlow? flow;
        lock (_loginLock) {
            flow = _login;
        }

        if (flow is null || !string.Equals(flow.LoginId, request.LoginId, StringComparison.Ordinal)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "Login flow not found.",
                Code = "login_not_found"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!flow.TryCompletePrompt(request.PromptId, request.Input)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "Prompt not found or already completed.",
                Code = "prompt_not_found"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteAsync(writer, new AckMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = request.RequestId,
            Ok = true,
            Message = "Accepted."
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleCancelChatGptLoginAsync(StreamWriter writer, CancelChatGptLoginRequest request, CancellationToken cancellationToken) {
        LoginFlow? flow;
        lock (_loginLock) {
            flow = _login;
        }

        if (flow is null || !string.Equals(flow.LoginId, request.LoginId, StringComparison.Ordinal)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "Login flow not found.",
                Code = "login_not_found"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        flow.Cancel();
        await WriteAsync(writer, new AckMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = request.RequestId,
            Ok = true,
            Message = "Canceled."
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleListToolsAsync(StreamWriter writer, string requestId, CancellationToken cancellationToken) {
        var defs = _registry.GetDefinitions();
        var tools = new ToolDefinitionDto[defs.Count];
        for (var i = 0; i < defs.Count; i++) {
            var parametersJson = defs[i].Parameters is null ? "{}" : JsonLite.Serialize(defs[i].Parameters);
            var required = ExtractRequiredArguments(parametersJson);
            var parameters = ExtractToolParameters(parametersJson, required);
            var packId = NormalizePackId(InferPackIdFromToolName(defs[i].Name, defs[i].Tags));
            string? packName = null;
            if (packId.Length > 0 && _packDisplayNamesById.TryGetValue(packId, out var resolvedPackName)) {
                packName = resolvedPackName;
            }

            tools[i] = new ToolDefinitionDto {
                Name = defs[i].Name,
                Description = defs[i].Description ?? string.Empty,
                DisplayName = FormatToolDisplayName(defs[i].Name),
                Category = InferToolCategory(defs[i].Name, defs[i].Tags),
                Tags = defs[i].Tags.Count == 0 ? null : defs[i].Tags.ToArray(),
                PackId = packId.Length == 0 ? null : packId,
                PackName = string.IsNullOrWhiteSpace(packName) ? null : packName,
                ParametersJson = parametersJson,
                RequiredArguments = required,
                Parameters = parameters
            };
        }
        await WriteAsync(writer, new ToolListMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = requestId,
            Tools = tools
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleInvokeToolAsync(StreamWriter writer, InvokeToolRequest request, CancellationToken cancellationToken) {
        var toolName = (request.ToolName ?? string.Empty).Trim();
        if (toolName.Length == 0) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "toolName is required.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        JsonObject? arguments = null;
        if (!string.IsNullOrWhiteSpace(request.ArgumentsJson)) {
            try {
                var parsed = JsonLite.Parse(request.ArgumentsJson!);
                arguments = parsed?.AsObject();
                if (parsed is not null && arguments is null) {
                    await WriteAsync(writer, new ErrorMessage {
                        Kind = ChatServiceMessageKind.Response,
                        RequestId = request.RequestId,
                        Error = "argumentsJson must be a JSON object.",
                        Code = "invalid_argument"
                    }, cancellationToken).ConfigureAwait(false);
                    return;
                }
            } catch (Exception ex) {
                await WriteAsync(writer, new ErrorMessage {
                    Kind = ChatServiceMessageKind.Response,
                    RequestId = request.RequestId,
                    Error = $"Invalid argumentsJson: {ex.Message}",
                    Code = "invalid_json"
                }, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        var timeoutSeconds = request.ToolTimeoutSeconds ?? _options.ToolTimeoutSeconds;
        if (timeoutSeconds < 0) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "toolTimeoutSeconds must be a non-negative integer.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var call = new ToolCall(
            callId: request.RequestId + ":invoke",
            name: toolName,
            input: request.ArgumentsJson,
            arguments: arguments,
            raw: new JsonObject());
        var output = await ExecuteToolAsync(call, timeoutSeconds, cancellationToken).ConfigureAwait(false);

        await WriteAsync(writer, new InvokeToolResultMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = request.RequestId,
            ToolName = toolName,
            Output = output
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> HandleChatRequestAsync(IntelligenceXClient client, StreamWriter writer, ChatRequest request, string? activeThreadId,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(request.Text)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "Text cannot be empty.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return activeThreadId;
        }

        ChatRun? existingRun;
        lock (_chatRunLock) {
            existingRun = _activeChat;
            if (existingRun is not null && !existingRun.IsCompleted) {
                existingRun = _activeChat;
            } else {
                existingRun = null;
            }
        }

        if (existingRun is not null) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = $"A chat request is already running (requestId={existingRun.ChatRequestId}).",
                Code = "chat_in_progress"
            }, cancellationToken).ConfigureAwait(false);
            return activeThreadId;
        }

        try {
            activeThreadId = await EnsureThreadAsync(client, request.ThreadId, activeThreadId, request.Options?.Model, cancellationToken)
                .ConfigureAwait(false);
        } catch (OpenAIAuthenticationRequiredException) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "Not authenticated. Run ChatGPT login in a client that can persist ~/.intelligencex/auth.json, then reconnect.",
                Code = "not_authenticated"
            }, cancellationToken).ConfigureAwait(false);
            return activeThreadId;
        } catch (Exception ex) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = $"Chat failed: {ex.Message}",
                Code = "chat_failed"
            }, cancellationToken).ConfigureAwait(false);
            return activeThreadId;
        }

        var run = new ChatRun(request.RequestId, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
            ThreadId = activeThreadId
        };
        lock (_chatRunLock) {
            _activeChat = run;
        }

        run.Task = Task.Run(async () => {
            IDisposable? deltaSubscription = null;
            try {
                var threadIdForDelta = run.ThreadId ?? string.Empty;
                deltaSubscription = client.SubscribeDelta(delta => { _ = TryWriteDeltaAsync(writer, request.RequestId, threadIdForDelta, delta); });

                var result = await RunChatOnCurrentThreadAsync(client, writer, request, threadIdForDelta, run.Cts.Token).ConfigureAwait(false);
                await WriteAsync(writer, result, CancellationToken.None).ConfigureAwait(false);
            } catch (OpenAIAuthenticationRequiredException) {
                await WriteAsync(writer, new ErrorMessage {
                    Kind = ChatServiceMessageKind.Response,
                    RequestId = request.RequestId,
                    Error = "Not authenticated. Run ChatGPT login in a client that can persist ~/.intelligencex/auth.json, then reconnect.",
                    Code = "not_authenticated"
                }, CancellationToken.None).ConfigureAwait(false);
            } catch (OperationCanceledException) when (run.Cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
                await WriteAsync(writer, new ErrorMessage {
                    Kind = ChatServiceMessageKind.Response,
                    RequestId = request.RequestId,
                    Error = "Chat canceled by client.",
                    Code = "chat_canceled"
                }, CancellationToken.None).ConfigureAwait(false);
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                // Session shutting down.
            } catch (Exception ex) {
                await WriteAsync(writer, new ErrorMessage {
                    Kind = ChatServiceMessageKind.Response,
                    RequestId = request.RequestId,
                    Error = $"Chat failed: {ex.Message}",
                    Code = "chat_failed"
                }, CancellationToken.None).ConfigureAwait(false);
            } finally {
                deltaSubscription?.Dispose();
                run.MarkCompleted();
                lock (_chatRunLock) {
                    if (ReferenceEquals(_activeChat, run)) {
                        _activeChat = null;
                    }
                }
            }
        }, CancellationToken.None);

        return activeThreadId;
    }

    private async Task HandleCancelChatAsync(StreamWriter writer, CancelChatRequest request, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(request.ChatRequestId)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "chatRequestId is required.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        ChatRun? active;
        lock (_chatRunLock) {
            active = _activeChat;
        }

        if (active is null || active.IsCompleted
            || !string.Equals(active.ChatRequestId, request.ChatRequestId, StringComparison.Ordinal)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = $"Active chat request '{request.ChatRequestId}' not found.",
                Code = "chat_not_found"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        active.Cancel();
        await WriteAsync(writer, new AckMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = request.RequestId,
            Ok = true,
            Message = "Cancel requested."
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task CancelActiveChatIfAnyAsync() {
        ChatRun? active;
        lock (_chatRunLock) {
            active = _activeChat;
            _activeChat = null;
        }

        if (active is null) {
            return;
        }

        active.Cancel();
        if (active.Task is not null) {
            try {
                await active.Task.ConfigureAwait(false);
            } catch {
                // Ignore.
            }
        }
    }

    private static string[] ExtractRequiredArguments(string parametersJson) {
        if (string.IsNullOrWhiteSpace(parametersJson)) {
            return Array.Empty<string>();
        }

        try {
            using var doc = JsonDocument.Parse(parametersJson);
            if (!doc.RootElement.TryGetProperty("required", out var required) || required.ValueKind != System.Text.Json.JsonValueKind.Array) {
                return Array.Empty<string>();
            }

            var list = new List<string>();
            foreach (var item in required.EnumerateArray()) {
                if (item.ValueKind != System.Text.Json.JsonValueKind.String) {
                    continue;
                }
                var value = item.GetString();
                if (string.IsNullOrWhiteSpace(value)) {
                    continue;
                }
                list.Add(value.Trim());
            }
            return list.ToArray();
        } catch {
            return Array.Empty<string>();
        }
    }

    private static ToolParameterDto[] ExtractToolParameters(string parametersJson, IReadOnlyCollection<string> requiredArguments) {
        if (string.IsNullOrWhiteSpace(parametersJson)) {
            return Array.Empty<ToolParameterDto>();
        }

        try {
            using var doc = JsonDocument.Parse(parametersJson);
            if (!doc.RootElement.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object) {
                return Array.Empty<ToolParameterDto>();
            }

            var required = new HashSet<string>(requiredArguments ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var list = new List<ToolParameterDto>();
            foreach (var property in properties.EnumerateObject()) {
                var parameterName = (property.Name ?? string.Empty).Trim();
                if (parameterName.Length == 0) {
                    continue;
                }

                var node = property.Value;
                var enumValues = TryReadEnumValues(node);
                var defaultJson = node.TryGetProperty("default", out var defaultValue)
                    ? NormalizeSchemaJsonSnippet(defaultValue.GetRawText())
                    : null;
                var exampleJson = node.TryGetProperty("example", out var exampleValue)
                    ? NormalizeSchemaJsonSnippet(exampleValue.GetRawText())
                    : (node.TryGetProperty("examples", out var examples) && examples.ValueKind == JsonValueKind.Array && examples.GetArrayLength() > 0
                        ? NormalizeSchemaJsonSnippet(examples[0].GetRawText())
                        : null);

                list.Add(new ToolParameterDto {
                    Name = parameterName,
                    Type = ReadSchemaType(node),
                    Description = node.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String
                        ? description.GetString()
                        : null,
                    Required = required.Contains(parameterName),
                    EnumValues = enumValues,
                    DefaultJson = defaultJson,
                    ExampleJson = exampleJson
                });
            }

            return list.Count == 0
                ? Array.Empty<ToolParameterDto>()
                : list
                    .OrderBy(static p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        } catch {
            return Array.Empty<ToolParameterDto>();
        }
    }

    private static string ReadSchemaType(JsonElement node) {
        if (node.TryGetProperty("type", out var typeNode)) {
            if (typeNode.ValueKind == JsonValueKind.String) {
                var value = (typeNode.GetString() ?? string.Empty).Trim();
                if (value.Length > 0) {
                    return value;
                }
            }

            if (typeNode.ValueKind == JsonValueKind.Array) {
                var values = new List<string>();
                foreach (var item in typeNode.EnumerateArray()) {
                    if (item.ValueKind != JsonValueKind.String) {
                        continue;
                    }

                    var value = (item.GetString() ?? string.Empty).Trim();
                    if (value.Length == 0 || string.Equals(value, "null", StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    values.Add(value);
                }

                if (values.Count > 0) {
                    return string.Join("|", values);
                }
            }
        }

        if (node.TryGetProperty("anyOf", out var anyOf) && anyOf.ValueKind == JsonValueKind.Array) {
            foreach (var candidate in anyOf.EnumerateArray()) {
                var resolved = ReadSchemaType(candidate);
                if (!string.Equals(resolved, "any", StringComparison.OrdinalIgnoreCase)) {
                    return resolved;
                }
            }
        }

        if (node.TryGetProperty("oneOf", out var oneOf) && oneOf.ValueKind == JsonValueKind.Array) {
            foreach (var candidate in oneOf.EnumerateArray()) {
                var resolved = ReadSchemaType(candidate);
                if (!string.Equals(resolved, "any", StringComparison.OrdinalIgnoreCase)) {
                    return resolved;
                }
            }
        }

        return "any";
    }

    private static string[]? TryReadEnumValues(JsonElement node) {
        if (!node.TryGetProperty("enum", out var enumNode) || enumNode.ValueKind != JsonValueKind.Array || enumNode.GetArrayLength() == 0) {
            return null;
        }

        var values = new List<string>();
        foreach (var enumValue in enumNode.EnumerateArray()) {
            var text = enumValue.ValueKind switch {
                JsonValueKind.String => enumValue.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => enumValue.GetRawText(),
                _ => enumValue.GetRawText()
            };

            if (string.IsNullOrWhiteSpace(text)) {
                continue;
            }

            values.Add(text.Trim());
        }

        return values.Count == 0 ? null : values.ToArray();
    }

    private static string? NormalizeSchemaJsonSnippet(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string FormatToolDisplayName(string toolName) {
        var normalized = (toolName ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return "Tool";
        }

        var parts = normalized.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) {
            return normalized;
        }

        for (var i = 0; i < parts.Length; i++) {
            var part = parts[i];
            if (part.Length <= 1) {
                parts[i] = part.ToUpperInvariant();
                continue;
            }

            parts[i] = char.ToUpperInvariant(part[0]) + part[1..];
        }

        return string.Join(' ', parts);
    }

    private static string InferToolCategory(string toolName, IReadOnlyList<string> tags) {
        var packId = InferPackIdFromToolName(toolName, tags);
        return packId switch {
            "ad" => "active-directory",
            "eventlog" => "event-log",
            "fs" => "file-system",
            "system" => "system",
            "powershell" => "powershell",
            "email" => "email",
            "testimox" => "testimox",
            "reviewersetup" => "reviewer-setup",
            "reviewer-setup" => "reviewer-setup",
            _ => "other"
        };
    }

    private static string InferPackIdFromToolName(string? toolName, IReadOnlyList<string>? tags) {
        var normalized = (toolName ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        if (normalized.StartsWith("ad_", StringComparison.Ordinal)
            || normalized.StartsWith("adplayground_", StringComparison.Ordinal)) {
            return "ad";
        }
        if (normalized.StartsWith("eventlog_", StringComparison.Ordinal)) {
            return "eventlog";
        }
        if (normalized.StartsWith("fs_", StringComparison.Ordinal)) {
            return "fs";
        }
        if (normalized.StartsWith("system_", StringComparison.Ordinal) || normalized.StartsWith("wsl_", StringComparison.Ordinal)) {
            return "system";
        }
        if (normalized.StartsWith("powershell_", StringComparison.Ordinal)) {
            return "powershell";
        }
        if (normalized.StartsWith("email_", StringComparison.Ordinal)) {
            return "email";
        }
        if (normalized.StartsWith("testimox_", StringComparison.Ordinal)) {
            return "testimox";
        }
        if (normalized.StartsWith("reviewer_setup_", StringComparison.Ordinal)) {
            return "reviewer-setup";
        }
        if (normalized.StartsWith("export_", StringComparison.Ordinal)) {
            return "export";
        }

        if (tags is { Count: > 0 }) {
            foreach (var tag in tags) {
                var normalizedTag = (tag ?? string.Empty).Trim().ToLowerInvariant();
                if (normalizedTag.Length == 0) {
                    continue;
                }

                if (normalizedTag.Contains("active-directory", StringComparison.Ordinal)
                    || normalizedTag.Equals("ad", StringComparison.Ordinal)) {
                    return "ad";
                }

                if (normalizedTag.Contains("eventlog", StringComparison.Ordinal)
                    || normalizedTag.Contains("event-log", StringComparison.Ordinal)) {
                    return "eventlog";
                }

                if (normalizedTag.Contains("filesystem", StringComparison.Ordinal)
                    || normalizedTag.Contains("file-system", StringComparison.Ordinal)
                    || normalizedTag.Equals("fs", StringComparison.Ordinal)) {
                    return "fs";
                }

                if (normalizedTag.Contains("powershell", StringComparison.Ordinal)) {
                    return "powershell";
                }
            }
        }

        return "other";
    }

    private static async Task<string> EnsureThreadAsync(IntelligenceXClient client, string? requestThreadId, string? activeThreadId, string? model,
        CancellationToken cancellationToken) {
        if (!string.IsNullOrWhiteSpace(requestThreadId)) {
            await client.UseThreadAsync(requestThreadId!, cancellationToken).ConfigureAwait(false);
            return requestThreadId!;
        }
        if (string.IsNullOrWhiteSpace(activeThreadId)) {
            var thread = await client.StartNewThreadAsync(model, cancellationToken: cancellationToken).ConfigureAwait(false);
            return thread.Id;
        }
        await client.UseThreadAsync(activeThreadId!, cancellationToken).ConfigureAwait(false);
        return activeThreadId!;
    }

    private async Task<ChatResultMessage> RunChatOnCurrentThreadAsync(IntelligenceXClient client, StreamWriter writer, ChatRequest request, string threadId,
        CancellationToken cancellationToken) {
        var toolCalls = new List<ToolCallDto>();
        var toolOutputs = new List<ToolOutputDto>();

        IReadOnlyList<ToolDefinition> toolDefs = _registry.GetDefinitions();
        if (request.Options?.DisabledTools is { Length: > 0 } disabledTools && toolDefs.Count > 0) {
            var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < disabledTools.Length; i++) {
                if (!string.IsNullOrWhiteSpace(disabledTools[i])) {
                    disabled.Add(disabledTools[i].Trim());
                }
            }

            if (disabled.Count > 0) {
                var filtered = new List<ToolDefinition>(toolDefs.Count);
                for (var i = 0; i < toolDefs.Count; i++) {
                    if (!disabled.Contains(toolDefs[i].Name)) {
                        filtered.Add(toolDefs[i]);
                    }
                }
                toolDefs = filtered;
            }
        }
        toolDefs = SanitizeToolDefinitions(toolDefs);
        var originalToolCount = toolDefs.Count;
        var routingInsights = new List<ToolRoutingInsight>();
        var weightedToolRouting = request.Options?.WeightedToolRouting ?? true;
        var maxCandidateTools = request.Options?.MaxCandidateTools;
        if (weightedToolRouting && toolDefs.Count > 0) {
            var userRequest = ExtractPrimaryUserRequest(request.Text);
            if (!TryGetContinuationToolSubset(threadId, userRequest, toolDefs, out var continuationSubset)) {
                toolDefs = SelectWeightedToolSubset(toolDefs, userRequest, maxCandidateTools, out routingInsights);
            } else {
                toolDefs = continuationSubset;
                routingInsights = BuildContinuationRoutingInsights(toolDefs);
            }
            RememberWeightedToolSubset(threadId, toolDefs, originalToolCount);
        }

        var parallelTools = request.Options?.ParallelTools ?? _options.ParallelTools;
        var maxRounds = request.Options?.MaxToolRounds ?? _options.MaxToolRounds;
        var turnTimeoutSeconds = request.Options?.TurnTimeoutSeconds ?? _options.TurnTimeoutSeconds;
        var toolTimeoutSeconds = request.Options?.ToolTimeoutSeconds ?? _options.ToolTimeoutSeconds;
        using var turnCts = CreateTimeoutCts(cancellationToken, turnTimeoutSeconds);
        var turnToken = turnCts?.Token ?? cancellationToken;

        var options = new ChatOptions {
            Model = request.Options?.Model ?? _options.Model,
            ParallelToolCalls = parallelTools,
            Tools = toolDefs.Count == 0 ? null : toolDefs,
            ToolChoice = toolDefs.Count == 0 ? null : ToolChoice.Auto
        };

        if (weightedToolRouting && originalToolCount > 0 && toolDefs.Count > 0 && toolDefs.Count < originalToolCount) {
            await TryWriteStatusAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    status: "routing",
                    message: $"Tool routing selected {toolDefs.Count} of {originalToolCount} tools for this turn.")
                .ConfigureAwait(false);
            await EmitRoutingInsightsAsync(writer, request.RequestId, threadId, routingInsights).ConfigureAwait(false);
        }

        await TryWriteStatusAsync(writer, request.RequestId, threadId, status: "thinking").ConfigureAwait(false);
        TurnInfo turn = await ChatWithToolSchemaRecoveryAsync(client, ChatInput.FromText(request.Text), options, turnToken).ConfigureAwait(false);

        for (var round = 0; round < Math.Max(1, maxRounds); round++) {
            var extracted = ToolCallParser.Extract(turn);
            if (extracted.Count == 0) {
                var text = EasyChatResult.FromTurn(turn).Text ?? string.Empty;
                if (_options.Redact) {
                    text = RedactText(text);
                }
                return new ChatResultMessage {
                    Kind = ChatServiceMessageKind.Response,
                    RequestId = request.RequestId,
                    ThreadId = threadId,
                    Text = text,
                    Tools = toolCalls.Count == 0 && toolOutputs.Count == 0
                        ? null
                        : new ToolRunDto { Calls = toolCalls.ToArray(), Outputs = toolOutputs.ToArray() }
                };
            }

            foreach (var call in extracted) {
                await TryWriteStatusAsync(writer, request.RequestId, threadId, status: "tool_call", toolName: call.Name, toolCallId: call.CallId)
                    .ConfigureAwait(false);
                toolCalls.Add(new ToolCallDto {
                    CallId = call.CallId,
                    Name = call.Name,
                    ArgumentsJson = call.Arguments is null ? "{}" : JsonLite.Serialize(call.Arguments)
                });
            }

            var executed = await ExecuteToolsAsync(writer, request.RequestId, threadId, extracted, parallelTools, toolTimeoutSeconds, turnToken)
                .ConfigureAwait(false);
            UpdateToolRoutingStats(extracted, executed);
            foreach (var output in executed) {
                toolOutputs.Add(new ToolOutputDto {
                    CallId = output.CallId,
                    Output = output.Output,
                    Ok = output.Ok,
                    ErrorCode = output.ErrorCode,
                    Error = output.Error,
                    Hints = output.Hints,
                    IsTransient = output.IsTransient,
                    SummaryMarkdown = output.SummaryMarkdown,
                    MetaJson = output.MetaJson,
                    RenderJson = output.RenderJson,
                    FailureJson = output.FailureJson
                });
            }

            var next = new ChatInput();
            foreach (var output in executed) {
                next.AddToolOutput(output.CallId, output.Output);
            }
            options.NewThread = false;
            await TryWriteStatusAsync(writer, request.RequestId, threadId, status: "thinking").ConfigureAwait(false);
            turn = await ChatWithToolSchemaRecoveryAsync(client, next, options, turnToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException($"Tool runner exceeded max rounds ({maxRounds}).");
    }

    private static IReadOnlyList<ToolDefinition> SanitizeToolDefinitions(IReadOnlyList<ToolDefinition> definitions) {
        if (definitions.Count == 0) {
            return Array.Empty<ToolDefinition>();
        }

        var sanitized = new List<ToolDefinition>(definitions.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null) {
                continue;
            }

            var normalizedName = (definition.Name ?? string.Empty).Trim();
            if (normalizedName.Length == 0 || !seen.Add(normalizedName)) {
                continue;
            }

            sanitized.Add(definition);
        }

        return sanitized.Count == 0 ? Array.Empty<ToolDefinition>() : sanitized;
    }

    private IReadOnlyList<ToolDefinition> SelectWeightedToolSubset(IReadOnlyList<ToolDefinition> definitions, string requestText, int? maxCandidateTools,
        out List<ToolRoutingInsight> insights) {
        insights = new List<ToolRoutingInsight>();
        if (definitions.Count <= 12) {
            return definitions;
        }

        var userRequest = ExtractPrimaryUserRequest(requestText);
        if (ShouldSkipWeightedRouting(userRequest)) {
            return definitions;
        }

        var tokens = TokenizeForToolRouting(userRequest);
        if (tokens.Count == 0) {
            return definitions;
        }

        var limit = ResolveMaxCandidateToolsLimit(maxCandidateTools, definitions.Count);
        if (limit >= definitions.Count) {
            return definitions;
        }

        var scored = new List<ToolScore>(definitions.Count);
        var hasSignal = false;
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            var searchText = BuildToolSearchText(definition);
            var score = 0d;
            var directNameMatch = userRequest.IndexOf(definition.Name, StringComparison.OrdinalIgnoreCase) >= 0;
            if (directNameMatch) {
                score += 6d;
            }

            var packId = InferPackIdFromToolName(definition.Name, definition.Tags);
            var packMatch = IsPackMentioned(userRequest, packId);
            if (packMatch) {
                score += 2.5d;
            }

            var tokenHits = 0;
            for (var t = 0; t < tokens.Count; t++) {
                var token = tokens[t];
                if (searchText.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                    score += 0.9d;
                    tokenHits++;
                }

                if (definition.Name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                    score += 0.9d;
                }
            }

            var adjustment = ReadToolRoutingAdjustment(definition.Name);
            score += adjustment;
            if (score > 0.01d) {
                hasSignal = true;
            }

            scored.Add(new ToolScore(
                Definition: definition,
                Score: score,
                TokenHits: tokenHits,
                DirectNameMatch: directNameMatch,
                PackMatch: packMatch,
                Adjustment: adjustment));
        }

        if (!hasSignal) {
            return definitions;
        }

        scored.Sort(static (a, b) => {
            var scoreCompare = b.Score.CompareTo(a.Score);
            if (scoreCompare != 0) {
                return scoreCompare;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(a.Definition.Name, b.Definition.Name);
        });

        if (scored[0].Score < 1d) {
            return definitions;
        }

        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedDefs = new List<ToolDefinition>(Math.Min(limit, definitions.Count));
        for (var i = 0; i < scored.Count && selectedDefs.Count < limit; i++) {
            var definition = scored[i].Definition;
            if (!selected.Add(definition.Name)) {
                continue;
            }
            selectedDefs.Add(definition);
        }

        if (selectedDefs.Count == 0) {
            return definitions;
        }

        var minSelection = Math.Min(definitions.Count, Math.Max(8, Math.Min(limit, 12)));
        if (selectedDefs.Count < minSelection) {
            for (var i = selectedDefs.Count; i < scored.Count && selectedDefs.Count < minSelection; i++) {
                var definition = scored[i].Definition;
                if (!selected.Add(definition.Name)) {
                    continue;
                }
                selectedDefs.Add(definition);
            }
        }

        if (selectedDefs.Count >= definitions.Count) {
            return definitions;
        }

        insights = BuildRoutingInsights(scored, selectedDefs);
        return selectedDefs;
    }

    private static List<ToolRoutingInsight> BuildRoutingInsights(IReadOnlyList<ToolScore> scored, IReadOnlyList<ToolDefinition> selectedDefs) {
        if (selectedDefs.Count == 0 || scored.Count == 0) {
            return new List<ToolRoutingInsight>();
        }

        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < selectedDefs.Count; i++) {
            selectedNames.Add(selectedDefs[i].Name);
        }

        var maxScore = scored[0].Score <= 0 ? 1d : scored[0].Score;
        var insights = new List<ToolRoutingInsight>();
        for (var i = 0; i < scored.Count; i++) {
            var toolScore = scored[i];
            if (!selectedNames.Contains(toolScore.Definition.Name)) {
                continue;
            }

            var confidenceValue = Math.Clamp(toolScore.Score / maxScore, 0d, 1d);
            var confidence = confidenceValue >= 0.72d ? "high" : confidenceValue >= 0.45d ? "medium" : "low";
            var reasons = new List<string>();
            if (toolScore.DirectNameMatch) {
                reasons.Add("direct name match");
            }
            if (toolScore.PackMatch) {
                reasons.Add("pack intent match");
            }
            if (toolScore.TokenHits > 0) {
                reasons.Add($"{toolScore.TokenHits} token matches");
            }
            if (toolScore.Adjustment > 0.2d) {
                reasons.Add("recent tool success");
            } else if (toolScore.Adjustment < -0.2d) {
                reasons.Add("recent tool failures");
            }

            if (reasons.Count == 0) {
                reasons.Add("general relevance");
            }

            insights.Add(new ToolRoutingInsight(
                ToolName: toolScore.Definition.Name,
                Confidence: confidence,
                Score: Math.Round(toolScore.Score, 3),
                Reason: string.Join(", ", reasons)));
        }

        insights.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        if (insights.Count > 12) {
            insights.RemoveRange(12, insights.Count - 12);
        }

        return insights;
    }

    private static List<ToolRoutingInsight> BuildContinuationRoutingInsights(IReadOnlyList<ToolDefinition> selectedDefs) {
        var list = new List<ToolRoutingInsight>(selectedDefs.Count);
        for (var i = 0; i < selectedDefs.Count && i < 12; i++) {
            var name = selectedDefs[i].Name;
            if (string.IsNullOrWhiteSpace(name)) {
                continue;
            }

            list.Add(new ToolRoutingInsight(
                ToolName: name.Trim(),
                Confidence: "high",
                Score: 1d,
                Reason: "continuation follow-up reuse"));
        }

        return list;
    }

    private async Task EmitRoutingInsightsAsync(StreamWriter writer, string requestId, string threadId, IReadOnlyList<ToolRoutingInsight> insights) {
        if (insights.Count == 0) {
            return;
        }

        for (var i = 0; i < insights.Count; i++) {
            var insight = insights[i];
            var payload = JsonSerializer.Serialize(new {
                confidence = insight.Confidence,
                score = insight.Score,
                reason = insight.Reason
            });
            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    threadId,
                    status: "routing_tool",
                    toolName: insight.ToolName,
                    message: payload)
                .ConfigureAwait(false);
        }
    }

    private bool TryGetContinuationToolSubset(string threadId, string userRequest, IReadOnlyList<ToolDefinition> allDefinitions,
        out IReadOnlyList<ToolDefinition> subset) {
        subset = Array.Empty<ToolDefinition>();
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || !LooksLikeContinuationFollowUp(userRequest)) {
            return false;
        }

        string[]? previousNames;
        lock (_toolRoutingContextLock) {
            if (!_lastWeightedToolNamesByThreadId.TryGetValue(normalizedThreadId, out previousNames) || previousNames.Length == 0) {
                return false;
            }

            _lastWeightedToolSubsetSeenUtcTicks[normalizedThreadId] = DateTime.UtcNow.Ticks;
            TrimWeightedRoutingContextsNoLock();
        }

        var preferred = new HashSet<string>(previousNames!, StringComparer.OrdinalIgnoreCase);
        var selected = new List<ToolDefinition>();
        for (var i = 0; i < allDefinitions.Count; i++) {
            var definition = allDefinitions[i];
            if (preferred.Contains(definition.Name)) {
                selected.Add(definition);
            }
        }

        if (selected.Count < 2) {
            return false;
        }

        subset = selected;
        return true;
    }

    private void RememberWeightedToolSubset(string threadId, IReadOnlyList<ToolDefinition> selectedDefinitions, int allToolCount) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        lock (_toolRoutingContextLock) {
            if (selectedDefinitions.Count == 0 || selectedDefinitions.Count >= allToolCount) {
                _lastWeightedToolNamesByThreadId.Remove(normalizedThreadId);
                _lastWeightedToolSubsetSeenUtcTicks.Remove(normalizedThreadId);
                return;
            }

            var names = new List<string>(selectedDefinitions.Count);
            for (var i = 0; i < selectedDefinitions.Count && i < 64; i++) {
                var name = (selectedDefinitions[i].Name ?? string.Empty).Trim();
                if (name.Length > 0) {
                    names.Add(name);
                }
            }

            _lastWeightedToolNamesByThreadId[normalizedThreadId] = names.Count == 0 ? Array.Empty<string>() : names.ToArray();
            _lastWeightedToolSubsetSeenUtcTicks[normalizedThreadId] = DateTime.UtcNow.Ticks;
            TrimWeightedRoutingContextsNoLock();
        }
    }

    private double ReadToolRoutingAdjustment(string toolName) {
        lock (_toolRoutingStatsLock) {
            if (!_toolRoutingStats.TryGetValue(toolName, out var stats)) {
                return 0d;
            }

            var score = 0d;
            if (stats.Successes > 0) {
                score += Math.Min(2.4d, stats.Successes * 0.2d);
            }
            if (stats.Failures > 0) {
                score -= Math.Min(2.4d, stats.Failures * 0.28d);
            }
            if (stats.LastSuccessUtcTicks > 0) {
                var sinceSuccess = DateTime.UtcNow - new DateTime(stats.LastSuccessUtcTicks, DateTimeKind.Utc);
                if (sinceSuccess <= TimeSpan.FromMinutes(20)) {
                    score += 0.35d;
                }
            }

            return score;
        }
    }

    private void UpdateToolRoutingStats(IReadOnlyList<ToolCall> calls, IReadOnlyList<ToolOutputDto> outputs) {
        if (calls.Count == 0 || outputs.Count == 0) {
            return;
        }

        var nameByCallId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < calls.Count; i++) {
            var call = calls[i];
            if (string.IsNullOrWhiteSpace(call.CallId) || string.IsNullOrWhiteSpace(call.Name)) {
                continue;
            }

            nameByCallId[call.CallId.Trim()] = call.Name.Trim();
        }

        if (nameByCallId.Count == 0) {
            return;
        }

        var nowTicks = DateTime.UtcNow.Ticks;
        lock (_toolRoutingStatsLock) {
            foreach (var output in outputs) {
                if (string.IsNullOrWhiteSpace(output.CallId) || !nameByCallId.TryGetValue(output.CallId, out var toolName)) {
                    continue;
                }

                if (!_toolRoutingStats.TryGetValue(toolName, out var stats)) {
                    stats = new ToolRoutingStats();
                    _toolRoutingStats[toolName] = stats;
                }

                stats.Invocations++;
                stats.LastUsedUtcTicks = nowTicks;
                var success = output.Ok != false
                              && string.IsNullOrWhiteSpace(output.ErrorCode)
                              && string.IsNullOrWhiteSpace(output.Error);
                if (success) {
                    stats.Successes++;
                    stats.LastSuccessUtcTicks = nowTicks;
                } else {
                    stats.Failures++;
                }
            }
            TrimToolRoutingStatsNoLock();
        }
    }

    private void TrimWeightedRoutingContextsNoLock() {
        var removeCount = _lastWeightedToolNamesByThreadId.Count - MaxTrackedWeightedRoutingContexts;
        if (removeCount <= 0) {
            return;
        }

        var threadIdsToRemove = _lastWeightedToolNamesByThreadId.Keys
            .Select(threadId => {
                var ticks = _lastWeightedToolSubsetSeenUtcTicks.TryGetValue(threadId, out var value) && value > 0
                    ? value
                    : long.MinValue;
                return (ThreadId: threadId, Ticks: ticks);
            })
            .OrderBy(item => item.Ticks)
            .ThenBy(item => item.ThreadId, StringComparer.Ordinal)
            .Take(removeCount)
            .Select(item => item.ThreadId)
            .ToArray();

        foreach (var threadId in threadIdsToRemove) {
            _lastWeightedToolNamesByThreadId.Remove(threadId);
            _lastWeightedToolSubsetSeenUtcTicks.Remove(threadId);
        }
    }

    private void TrimToolRoutingStatsNoLock() {
        var removeCount = _toolRoutingStats.Count - MaxTrackedToolRoutingStats;
        if (removeCount <= 0) {
            return;
        }

        var toolNamesToRemove = _toolRoutingStats
            .Select(pair => {
                var stats = pair.Value;
                var ticks = stats.LastUsedUtcTicks > 0
                    ? stats.LastUsedUtcTicks
                    : (stats.LastSuccessUtcTicks > 0 ? stats.LastSuccessUtcTicks : long.MinValue);
                return (ToolName: pair.Key, Ticks: ticks);
            })
            .OrderBy(item => item.Ticks)
            .ThenBy(item => item.ToolName, StringComparer.Ordinal)
            .Take(removeCount)
            .Select(item => item.ToolName)
            .ToArray();

        foreach (var toolName in toolNamesToRemove) {
            _toolRoutingStats.Remove(toolName);
        }
    }

    internal void SetToolRoutingStatsForTesting(IReadOnlyDictionary<string, (long LastUsedUtcTicks, long LastSuccessUtcTicks)> statsByToolName) {
        ArgumentNullException.ThrowIfNull(statsByToolName);

        lock (_toolRoutingStatsLock) {
            _toolRoutingStats.Clear();
            foreach (var pair in statsByToolName) {
                var name = (pair.Key ?? string.Empty).Trim();
                if (name.Length == 0) {
                    continue;
                }

                _toolRoutingStats[name] = new ToolRoutingStats {
                    LastUsedUtcTicks = pair.Value.LastUsedUtcTicks,
                    LastSuccessUtcTicks = pair.Value.LastSuccessUtcTicks
                };
            }
        }
    }

    internal void SetWeightedRoutingContextsForTesting(IReadOnlyDictionary<string, string[]> namesByThreadId, IReadOnlyDictionary<string, long> seenTicksByThreadId) {
        ArgumentNullException.ThrowIfNull(namesByThreadId);
        ArgumentNullException.ThrowIfNull(seenTicksByThreadId);

        lock (_toolRoutingContextLock) {
            _lastWeightedToolNamesByThreadId.Clear();
            _lastWeightedToolSubsetSeenUtcTicks.Clear();

            foreach (var pair in namesByThreadId) {
                var threadId = (pair.Key ?? string.Empty).Trim();
                if (threadId.Length == 0) {
                    continue;
                }

                var names = pair.Value ?? Array.Empty<string>();
                var namesClone = new string[names.Length];
                if (names.Length > 0) {
                    Array.Copy(names, namesClone, names.Length);
                }

                _lastWeightedToolNamesByThreadId[threadId] = namesClone;
            }

            foreach (var pair in seenTicksByThreadId) {
                var threadId = (pair.Key ?? string.Empty).Trim();
                if (threadId.Length == 0 || !_lastWeightedToolNamesByThreadId.ContainsKey(threadId)) {
                    continue;
                }

                _lastWeightedToolSubsetSeenUtcTicks[threadId] = pair.Value;
            }
        }
    }

    internal IReadOnlyCollection<string> GetTrackedToolRoutingStatNamesForTesting() {
        lock (_toolRoutingStatsLock) {
            return _toolRoutingStats.Keys.ToArray();
        }
    }

    internal IReadOnlyCollection<string> GetTrackedWeightedRoutingContextThreadIdsForTesting() {
        lock (_toolRoutingContextLock) {
            return _lastWeightedToolNamesByThreadId.Keys.ToArray();
        }
    }

    internal void TrimToolRoutingStatsForTesting() {
        lock (_toolRoutingStatsLock) {
            TrimToolRoutingStatsNoLock();
        }
    }

    internal void TrimWeightedRoutingContextsForTesting() {
        lock (_toolRoutingContextLock) {
            TrimWeightedRoutingContextsNoLock();
        }
    }

    private static int ResolveMaxCandidateToolsLimit(int? requestedLimit, int totalToolCount) {
        var candidate = requestedLimit.GetValueOrDefault(0);
        if (candidate <= 0) {
            candidate = Math.Clamp((int)Math.Ceiling(totalToolCount * 0.45d), 10, 28);
        }

        return Math.Clamp(candidate, 4, Math.Max(4, totalToolCount));
    }

    private static string ExtractPrimaryUserRequest(string requestText) {
        var text = (requestText ?? string.Empty).Trim();
        if (text.Length == 0) {
            return string.Empty;
        }

        var match = UserRequestSectionRegex.Match(text);
        if (match.Success && match.Groups.Count > 1) {
            var value = match.Groups["value"].Value;
            if (!string.IsNullOrWhiteSpace(value)) {
                return value.Trim();
            }
        }

        return text;
    }

    private static bool ShouldSkipWeightedRouting(string userRequest) {
        if (string.IsNullOrWhiteSpace(userRequest)) {
            return true;
        }

        var normalized = userRequest.Trim().ToLowerInvariant();
        return normalized.Contains("what tools", StringComparison.Ordinal)
               || normalized.Contains("list tools", StringComparison.Ordinal)
               || normalized.Contains("available tools", StringComparison.Ordinal)
               || normalized.Contains("tool catalog", StringComparison.Ordinal)
               || normalized.Contains("which tool", StringComparison.Ordinal)
               || normalized.Contains("all tools", StringComparison.Ordinal)
               || normalized.Contains("anything you can", StringComparison.Ordinal);
    }

    private static bool LooksLikeContinuationFollowUp(string userRequest) {
        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (ContinuationFollowUpRegex.IsMatch(normalized)) {
            return true;
        }

        return normalized.Equals("continue", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("same", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("again", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> TokenizeForToolRouting(string userRequest) {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = Regex.Split((userRequest ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9_]+");
        for (var i = 0; i < parts.Length; i++) {
            var token = (parts[i] ?? string.Empty).Trim();
            if (token.Length < 3) {
                continue;
            }

            if (token is "the" or "and" or "with" or "from" or "that" or "this" or "for" or "you" or "your" or "have" or "show"
                or "give" or "list" or "check" or "please" or "about" or "into" or "just" or "today") {
                continue;
            }

            if (seen.Add(token)) {
                result.Add(token);
            }
        }

        return result;
    }

    private static string BuildToolSearchText(ToolDefinition definition) {
        var tags = definition.Tags.Count == 0 ? string.Empty : string.Join(' ', definition.Tags);
        return (definition.Name + " " + (definition.Description ?? string.Empty) + " " + tags).ToLowerInvariant();
    }

    private static bool IsPackMentioned(string userRequest, string packId) {
        if (string.IsNullOrWhiteSpace(userRequest) || string.IsNullOrWhiteSpace(packId)) {
            return false;
        }

        var normalized = userRequest.ToLowerInvariant();
        return packId switch {
            "ad" => normalized.Contains("active directory", StringComparison.Ordinal)
                    || normalized.Contains("domain", StringComparison.Ordinal)
                    || normalized.Contains("replication", StringComparison.Ordinal)
                    || normalized.Contains("ou ", StringComparison.Ordinal),
            "eventlog" => normalized.Contains("event log", StringComparison.Ordinal)
                          || normalized.Contains("evtx", StringComparison.Ordinal)
                          || normalized.Contains("lockout", StringComparison.Ordinal),
            "system" => normalized.Contains("system", StringComparison.Ordinal)
                        || normalized.Contains("host", StringComparison.Ordinal)
                        || normalized.Contains("computer", StringComparison.Ordinal),
            "fs" => normalized.Contains("file", StringComparison.Ordinal)
                    || normalized.Contains("folder", StringComparison.Ordinal)
                    || normalized.Contains("path", StringComparison.Ordinal),
            "powershell" => normalized.Contains("powershell", StringComparison.Ordinal)
                            || normalized.Contains("script", StringComparison.Ordinal),
            "email" => normalized.Contains("email", StringComparison.Ordinal)
                       || normalized.Contains("mail", StringComparison.Ordinal),
            "testimox" => normalized.Contains("testimox", StringComparison.Ordinal)
                          || normalized.Contains("rule", StringComparison.Ordinal),
            _ => false
        };
    }

    private sealed class ToolRoutingStats {
        public int Invocations { get; set; }
        public int Successes { get; set; }
        public int Failures { get; set; }
        public long LastUsedUtcTicks { get; set; }
        public long LastSuccessUtcTicks { get; set; }
    }

    private readonly record struct ToolScore(
        ToolDefinition Definition,
        double Score,
        int TokenHits,
        bool DirectNameMatch,
        bool PackMatch,
        double Adjustment);

    private readonly record struct ToolRoutingInsight(
        string ToolName,
        string Confidence,
        double Score,
        string Reason);

    private readonly record struct ToolRetryProfile(
        int MaxAttempts,
        int DelayBaseMs,
        bool RetryOnTimeout,
        bool RetryOnTransport);

    private static async Task<TurnInfo> ChatWithToolSchemaRecoveryAsync(IntelligenceXClient client, ChatInput input, ChatOptions options,
        CancellationToken cancellationToken) {
        try {
            return await client.ChatAsync(input, options, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) when (ShouldRetryWithoutTools(ex, options)) {
            options.Tools = null;
            options.ToolChoice = null;
            return await client.ChatAsync(input, options, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool ShouldRetryWithoutTools(Exception ex, ChatOptions options) {
        if (options.Tools is not { Count: > 0 }) {
            return false;
        }

        var message = ex.Message ?? string.Empty;
        if (message.Length == 0) {
            return false;
        }

        return message.IndexOf("missing required parameter", StringComparison.OrdinalIgnoreCase) >= 0
               && message.IndexOf("tools", StringComparison.OrdinalIgnoreCase) >= 0
               && message.IndexOf(".name", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private async Task<IReadOnlyList<ToolOutputDto>> ExecuteToolsAsync(StreamWriter writer, string requestId, string threadId, IReadOnlyList<ToolCall> calls,
        bool parallel, int toolTimeoutSeconds, CancellationToken cancellationToken) {
        if (!parallel || calls.Count <= 1) {
            var outputs = new List<ToolOutputDto>(calls.Count);
            foreach (var call in calls) {
                await TryWriteStatusAsync(writer, requestId, threadId, status: "tool_running", toolName: call.Name, toolCallId: call.CallId)
                    .ConfigureAwait(false);
                var sw = Stopwatch.StartNew();
                var output = await ExecuteToolAsync(call, toolTimeoutSeconds, cancellationToken).ConfigureAwait(false);
                sw.Stop();
                await TryWriteStatusAsync(writer, requestId, threadId, status: "tool_completed", toolName: call.Name, toolCallId: call.CallId,
                        durationMs: sw.ElapsedMilliseconds)
                    .ConfigureAwait(false);
                outputs.Add(output);
            }
            return outputs;
        }

        var tasks = new Task<ToolOutputDto>[calls.Count];
        for (var i = 0; i < calls.Count; i++) {
            var call = calls[i];
            tasks[i] = ExecuteToolWithStatusAsync(writer, requestId, threadId, call, toolTimeoutSeconds, cancellationToken);
        }
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<ToolOutputDto> ExecuteToolWithStatusAsync(StreamWriter writer, string requestId, string threadId, ToolCall call,
        int toolTimeoutSeconds, CancellationToken cancellationToken) {
        await TryWriteStatusAsync(writer, requestId, threadId, status: "tool_running", toolName: call.Name, toolCallId: call.CallId)
            .ConfigureAwait(false);
        var sw = Stopwatch.StartNew();
        var output = await ExecuteToolAsync(call, toolTimeoutSeconds, cancellationToken).ConfigureAwait(false);
        sw.Stop();
        await TryWriteStatusAsync(writer, requestId, threadId, status: "tool_completed", toolName: call.Name, toolCallId: call.CallId,
                durationMs: sw.ElapsedMilliseconds)
            .ConfigureAwait(false);
        return output;
    }

    private async Task<ToolOutputDto> ExecuteToolAsync(ToolCall call, int toolTimeoutSeconds, CancellationToken cancellationToken) {
        if (!_registry.TryGet(call.Name, out var tool)) {
            var output = ToolOutputEnvelope.Error(
                errorCode: "tool_not_registered",
                error: $"Tool '{call.Name}' is not registered.",
                hints: new[] { "Call list_tools to list available tools.", "Check that the correct packs are enabled." },
                isTransient: false);

            return BuildToolOutputDto(call.CallId, output);
        }

        // Retry profile wiring is enforced in this execution loop.
        var profile = ResolveRetryProfile(call.Name);
        ToolOutputDto? lastFailure = null;
        for (var attemptIndex = 0; attemptIndex < profile.MaxAttempts; attemptIndex++) {
            var output = await ExecuteToolAttemptAsync(tool, call, toolTimeoutSeconds, cancellationToken).ConfigureAwait(false);
            if (!ShouldRetryToolCall(output, profile, attemptIndex)) {
                return output;
            }

            lastFailure = output;
            if (profile.DelayBaseMs > 0) {
                var delayMs = Math.Min(800, profile.DelayBaseMs * (attemptIndex + 1));
                try {
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                } catch (OperationCanceledException) {
                    return output;
                }
            }
        }

        return lastFailure ?? await ExecuteToolAttemptAsync(tool, call, toolTimeoutSeconds, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ToolOutputDto> ExecuteToolAttemptAsync(ITool tool, ToolCall call, int toolTimeoutSeconds, CancellationToken cancellationToken) {
        using var toolCts = CreateTimeoutCts(cancellationToken, toolTimeoutSeconds);
        var toolToken = toolCts?.Token ?? cancellationToken;
        try {
            var output = await tool.InvokeAsync(call.Arguments, toolToken).ConfigureAwait(false);
            var text = output ?? string.Empty;
            if (_options.Redact) {
                text = RedactText(text);
            }
            return BuildToolOutputDto(call.CallId, text);
        } catch (OperationCanceledException) when (toolCts is not null && toolCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
            var output = ToolOutputEnvelope.Error(
                errorCode: "tool_timeout",
                error: $"Tool '{call.Name}' timed out after {toolTimeoutSeconds}s.",
                hints: new[] { "Increase toolTimeoutSeconds, or narrow the query (OU scoping, tighter filters)." },
                isTransient: true);
            return BuildToolOutputDto(call.CallId, output);
        } catch (Exception ex) {
            var isTransient = IsLikelyTransientToolException(ex);
            var output = ToolOutputEnvelope.Error(
                errorCode: "tool_exception",
                error: $"{ex.GetType().Name}: {ex.Message}",
                hints: new[] {
                    "Try again. If it keeps failing, narrow the query and capture tool args/output.",
                    "Check tool parameter names and value types in the tool details panel."
                },
                isTransient: isTransient);
            return BuildToolOutputDto(call.CallId, output);
        }
    }

    private static ToolOutputDto BuildToolOutputDto(string callId, string output) {
        var meta = TryExtractToolOutputMetadata(output);
        return new ToolOutputDto {
            CallId = callId,
            Output = output,
            Ok = meta.Ok,
            ErrorCode = meta.ErrorCode,
            Error = meta.Error,
            Hints = meta.Hints,
            IsTransient = meta.IsTransient,
            SummaryMarkdown = meta.SummaryMarkdown,
            MetaJson = meta.MetaJson,
            RenderJson = meta.RenderJson,
            FailureJson = meta.FailureJson
        };
    }

    private static bool ShouldRetryToolCall(ToolOutputDto output, ToolRetryProfile profile, int attemptIndex) {
        // attemptIndex is zero-based current attempt. We can only retry when there is another slot left.
        if (attemptIndex + 1 >= profile.MaxAttempts) {
            return false;
        }
        if (output.Ok is true) {
            return false;
        }

        if (string.Equals(output.ErrorCode, "tool_not_registered", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        if (string.Equals(output.ErrorCode, "tool_timeout", StringComparison.OrdinalIgnoreCase) && profile.RetryOnTimeout) {
            return true;
        }
        if (!string.IsNullOrWhiteSpace(output.ErrorCode)) {
            var code = output.ErrorCode.Trim();
            var transientTransportCode = code.Contains("transport", StringComparison.OrdinalIgnoreCase)
                                         || code.Contains("transient", StringComparison.OrdinalIgnoreCase)
                                         || code.Contains("unavailable", StringComparison.OrdinalIgnoreCase);
            if (transientTransportCode && profile.RetryOnTransport) {
                return true;
            }
        }
        if (IsLikelyPermanentToolFailure(output)) {
            return false;
        }
        if (output.IsTransient is true) {
            return true;
        }

        var text = BuildToolFailureSearchText(output);
        if (text.Length == 0) {
            return false;
        }

        var timeoutSignal = text.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                            || text.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                            || text.Contains("rpc server unavailable", StringComparison.OrdinalIgnoreCase)
                            || text.Contains("server unavailable", StringComparison.OrdinalIgnoreCase);
        if (timeoutSignal && profile.RetryOnTimeout) {
            return true;
        }

        var transportSignal = text.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("connection reset", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("connection closed", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("name resolution", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("dns", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("remote host closed", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("service unavailable", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("gateway timeout", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("econnreset", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("etimedout", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("network", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("try again", StringComparison.OrdinalIgnoreCase)
                              || text.Contains("throttl", StringComparison.OrdinalIgnoreCase);
        return transportSignal && profile.RetryOnTransport;
    }

    private static bool IsLikelyPermanentToolFailure(ToolOutputDto output) {
        var text = BuildToolFailureSearchText(output);
        if (text.Length == 0) {
            return false;
        }

        return text.Contains("unsupported columns", StringComparison.OrdinalIgnoreCase)
               || text.Contains("unknown projection", StringComparison.OrdinalIgnoreCase)
               || text.Contains("invalid parameter", StringComparison.OrdinalIgnoreCase)
               || text.Contains("invalid argument", StringComparison.OrdinalIgnoreCase)
               || text.Contains("missing required", StringComparison.OrdinalIgnoreCase)
               || text.Contains("cannot bind parameter", StringComparison.OrdinalIgnoreCase)
               || text.Contains("access denied", StringComparison.OrdinalIgnoreCase)
               || text.Contains("permission denied", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildToolFailureSearchText(ToolOutputDto output) {
        var parts = new List<string>(8);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddFailureSearchPart(parts, seen, output.ErrorCode);
        AddFailureSearchPart(parts, seen, output.Error);
        AppendFailureSearchContext(parts, seen, output.FailureJson);
        AppendFailureSearchContext(parts, seen, output.MetaJson);
        AppendFailureSearchContext(parts, seen, output.Output, includeRawFallback: false);
        return parts.Count == 0 ? string.Empty : string.Join(" ", parts);
    }

    private static void AppendFailureSearchContext(List<string> parts, HashSet<string> seen, string? rawText, bool includeRawFallback = true) {
        if (string.IsNullOrWhiteSpace(rawText)) {
            return;
        }

        if (TryAppendFailureJsonSignals(parts, seen, rawText!)) {
            return;
        }

        if (includeRawFallback) {
            AddFailureSearchPart(parts, seen, rawText);
        }
    }

    private static bool TryAppendFailureJsonSignals(List<string> parts, HashSet<string> seen, string rawText) {
        try {
            var parsed = JsonLite.Parse(rawText);
            var obj = parsed?.AsObject();
            if (obj is null) {
                return false;
            }

            var before = parts.Count;
            AppendFailureSignalsFromObject(parts, seen, obj);
            return parts.Count > before;
        } catch {
            return false;
        }
    }

    private static void AppendFailureSignalsFromObject(List<string> parts, HashSet<string> seen, JsonObject obj) {
        AddFailureSearchPart(parts, seen, obj.GetString("error_code"));
        AddFailureSearchPart(parts, seen, obj.GetString("code"));
        AddFailureSearchPart(parts, seen, obj.GetString("error"));
        AddFailureSearchPart(parts, seen, obj.GetString("message"));
        AddFailureSearchPart(parts, seen, obj.GetString("reason"));
        AddFailureSearchPart(parts, seen, obj.GetString("exception"));
        AddFailureSearchPart(parts, seen, obj.GetString("exception_type"));
        AddFailureSearchPart(parts, seen, obj.GetString("exceptionType"));
        AddFailureSearchPart(parts, seen, obj.GetString("details"));

        try {
            if (obj.GetObject("failure") is JsonObject failureObj) {
                AddFailureSearchPart(parts, seen, failureObj.GetString("code"));
                AddFailureSearchPart(parts, seen, failureObj.GetString("error"));
                AddFailureSearchPart(parts, seen, failureObj.GetString("message"));
                AddFailureSearchPart(parts, seen, failureObj.GetString("reason"));
            }
        } catch {
            // best-effort extraction only
        }

        try {
            if (obj.GetObject("meta") is JsonObject metaObj) {
                AddFailureSearchPart(parts, seen, metaObj.GetString("error_code"));
                AddFailureSearchPart(parts, seen, metaObj.GetString("error"));
                AddFailureSearchPart(parts, seen, metaObj.GetString("message"));
                AddFailureSearchPart(parts, seen, metaObj.GetString("reason"));
            }
        } catch {
            // best-effort extraction only
        }
    }

    private static void AddFailureSearchPart(List<string> parts, HashSet<string> seen, string? rawText) {
        var compact = CompactFailureText(rawText);
        if (compact.Length == 0) {
            return;
        }

        if (seen.Add(compact)) {
            parts.Add(compact);
        }
    }

    private static string CompactFailureText(string? rawText) {
        if (string.IsNullOrWhiteSpace(rawText)) {
            return string.Empty;
        }

        var compact = Regex.Replace(rawText.Trim(), @"\s+", " ");
        const int maxLength = 768;
        return compact.Length <= maxLength ? compact : compact[..maxLength];
    }

    private static bool IsLikelyTransientToolException(Exception ex) {
        if (ex is OperationCanceledException) {
            return false;
        }
        if (HasLikelyPermanentExceptionSignal(ex)) {
            return false;
        }

        if (HasKnownTransientExceptionInChain(ex)) {
            return true;
        }

        var message = ex.Message ?? string.Empty;
        return message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0
               || message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
               || message.IndexOf("temporarily", StringComparison.OrdinalIgnoreCase) >= 0
               || message.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0
               || message.IndexOf("try again", StringComparison.OrdinalIgnoreCase) >= 0
               || message.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0
               || message.IndexOf("throttl", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool HasLikelyPermanentExceptionSignal(Exception ex) {
        var depth = 0;
        for (Exception? current = ex; current is not null && depth < 8; current = current.InnerException, depth++) {
            if (current is UnauthorizedAccessException) {
                return true;
            }

            var message = current.Message ?? string.Empty;
            if (message.IndexOf("access denied", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("permission denied", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("unauthorized", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("forbidden", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("invalid credential", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("authentication failed", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("invalid parameter", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("invalid argument", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("missing required", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("cannot bind parameter", StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
    }

    private static bool HasKnownTransientExceptionInChain(Exception ex) {
        var depth = 0;
        for (Exception? current = ex; current is not null && depth < 8; current = current.InnerException, depth++) {
            if (current is OperationCanceledException) {
                return false;
            }
            if (current is TimeoutException || current is IOException) {
                return true;
            }

            var name = current.GetType().FullName ?? current.GetType().Name;
            if (name.IndexOf("SocketException", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("HttpRequestException", StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
    }

    private static ToolRetryProfile ResolveRetryProfile(string? toolName) {
        var normalized = (toolName ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.StartsWith("ad_", StringComparison.Ordinal)) {
            return new ToolRetryProfile(MaxAttempts: 2, DelayBaseMs: 200, RetryOnTimeout: true, RetryOnTransport: true);
        }
        if (normalized.StartsWith("eventlog_", StringComparison.Ordinal)) {
            return new ToolRetryProfile(MaxAttempts: 2, DelayBaseMs: 150, RetryOnTimeout: true, RetryOnTransport: true);
        }
        if (normalized.StartsWith("system_", StringComparison.Ordinal)
            || normalized.StartsWith("wsl_", StringComparison.Ordinal)) {
            return new ToolRetryProfile(MaxAttempts: 2, DelayBaseMs: 120, RetryOnTimeout: true, RetryOnTransport: true);
        }
        if (normalized.StartsWith("fs_", StringComparison.Ordinal)) {
            return new ToolRetryProfile(MaxAttempts: 2, DelayBaseMs: 90, RetryOnTimeout: true, RetryOnTransport: false);
        }

        return new ToolRetryProfile(MaxAttempts: 1, DelayBaseMs: 0, RetryOnTimeout: false, RetryOnTransport: false);
    }

    private static ToolOutputMetadata TryExtractToolOutputMetadata(string output) {
        if (string.IsNullOrWhiteSpace(output)) {
            return default;
        }

        // Tool outputs are free-form strings. When they happen to be JSON envelopes,
        // extract fields so the UI can render errors/tables/markdown consistently.
        try {
            var parsed = JsonLite.Parse(output);
            var obj = parsed?.AsObject();
            if (obj is null) {
                return default;
            }

            bool? ok = null;
            try {
                ok = obj.GetBoolean("ok");
            } catch {
                ok = null;
            }

            var errorCode = obj.GetString("error_code");
            var error = obj.GetString("error");

            bool? isTransient = null;
            try {
                isTransient = obj.GetBoolean("is_transient");
            } catch {
                isTransient = null;
            }

            string[]? hints = null;
            try {
                hints = ParseHintsArray(obj.GetArray("hints"));
            } catch {
                hints = null;
            }

            string? failureJson = null;
            try {
                if (obj.GetObject("failure") is JsonObject failureObj) {
                    failureJson = JsonLite.Serialize(failureObj);

                    if (string.IsNullOrWhiteSpace(errorCode)) {
                        errorCode = failureObj.GetString("code");
                    }
                    if (string.IsNullOrWhiteSpace(error)) {
                        error = failureObj.GetString("message");
                    }
                    if (!isTransient.HasValue) {
                        try {
                            isTransient = failureObj.GetBoolean("is_transient");
                        } catch {
                            isTransient = null;
                        }
                    }
                    if (hints is null || hints.Length == 0) {
                        hints = ParseHintsArray(failureObj.GetArray("hints"));
                    }
                }
            } catch {
                failureJson = null;
            }

            var summaryMarkdown = obj.GetString("summary_markdown");
            string? metaJson = null;
            try {
                if (obj.GetObject("meta") is JsonObject metaObj) {
                    metaJson = JsonLite.Serialize(metaObj);
                }
            } catch {
                metaJson = null;
            }

            string? renderJson = null;
            try {
                if (obj.GetObject("render") is JsonObject renderObj) {
                    renderJson = JsonLite.Serialize(renderObj);
                } else if (obj.GetArray("render") is JsonArray renderArr) {
                    renderJson = JsonLite.Serialize(renderArr);
                }
            } catch {
                renderJson = null;
            }

            if (ok is null && errorCode is null && error is null && hints is null && isTransient is null && summaryMarkdown is null
                && metaJson is null && renderJson is null && failureJson is null) {
                return default;
            }

            return new ToolOutputMetadata(ok, errorCode, error, hints, isTransient, summaryMarkdown, metaJson, renderJson, failureJson);
        } catch {
            return default;
        }
    }

    private static string[]? ParseHintsArray(JsonArray? arr) {
        if (arr is null || arr.Count == 0) {
            return null;
        }

        var list = new List<string>(arr.Count);
        foreach (var item in arr) {
            var s = item?.AsString();
            if (!string.IsNullOrWhiteSpace(s)) {
                list.Add(s!);
            }
        }

        return list.Count > 0 ? list.ToArray() : null;
    }

    private readonly record struct ToolOutputMetadata(
        bool? Ok,
        string? ErrorCode,
        string? Error,
        string[]? Hints,
        bool? IsTransient,
        string? SummaryMarkdown,
        string? MetaJson,
        string? RenderJson,
        string? FailureJson);

    private async Task WriteAsync(StreamWriter writer, ChatServiceMessage message, CancellationToken cancellationToken) {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            var json = JsonSerializer.Serialize(message, ChatServiceJsonContext.Default.ChatServiceMessage);
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        } finally {
            _writeLock.Release();
        }
    }

    private void CancelLoginIfActive() {
        LoginFlow? flow;
        lock (_loginLock) {
            flow = _login;
            _login = null;
        }
        flow?.Cancel();
    }

    private static CancellationTokenSource? CreateTimeoutCts(CancellationToken ct, int seconds) {
        if (seconds <= 0) {
            return null;
        }
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        return cts;
    }

    // Tool errors are returned as JSON strings to the model. Use the shared contract helper so
    // tool packs and hosts converge on the same envelope over time.

    private static SessionPolicyDto BuildSessionPolicy(ServiceOptions options, IEnumerable<IToolPack> packs, IReadOnlyList<string> startupWarnings,
        IReadOnlyList<string> pluginSearchPaths) {
        var roots = options.AllowedRoots.Count == 0 ? Array.Empty<string>() : options.AllowedRoots.ToArray();

        var packList = new List<ToolPackInfoDto>();
        foreach (var pack in ToolPackBootstrap.GetDescriptors(packs)) {
            packList.Add(new ToolPackInfoDto {
                Id = pack.Id,
                Name = ResolvePackDisplayName(pack.Id, pack.Name),
                Tier = MapTier(pack.Tier),
                Enabled = true,
                IsDangerous = pack.IsDangerous || pack.Tier == ToolCapabilityTier.DangerousWrite,
                SourceKind = MapSourceKind(pack.SourceKind, pack.Id)
            });
        }

        var dangerousEnabled = packList.Exists(static p => p.IsDangerous || p.Tier == CapabilityTier.DangerousWrite);

        return new SessionPolicyDto {
            ReadOnly = !dangerousEnabled,
            AllowedRoots = roots,
            Packs = packList.ToArray(),
            DangerousToolsEnabled = dangerousEnabled,
            ToolTimeoutSeconds = options.ToolTimeoutSeconds <= 0 ? null : options.ToolTimeoutSeconds,
            TurnTimeoutSeconds = options.TurnTimeoutSeconds <= 0 ? null : options.TurnTimeoutSeconds,
            MaxToolRounds = options.MaxToolRounds,
            ParallelTools = options.ParallelTools,
            MaxTableRows = options.MaxTableRows <= 0 ? null : options.MaxTableRows,
            MaxSample = options.MaxSample <= 0 ? null : options.MaxSample,
            Redact = options.Redact,
            StartupWarnings = startupWarnings.Count == 0 ? Array.Empty<string>() : startupWarnings.ToArray(),
            PluginSearchPaths = pluginSearchPaths.Count == 0 ? Array.Empty<string>() : pluginSearchPaths.ToArray()
        };
    }

    private static void RecordBootstrapWarning(ICollection<string> sink, string? warning) {
        var normalized = (warning ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return;
        }

        sink.Add(normalized);
        Console.Error.WriteLine($"[pack warning] {normalized}");
    }

    private static string[] NormalizeDistinctStrings(IEnumerable<string> values, int maxItems) {
        if (values is null) {
            return Array.Empty<string>();
        }

        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var value in values) {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0) {
                continue;
            }

            if (!dedupe.Add(normalized)) {
                continue;
            }

            list.Add(normalized);
            if (maxItems > 0 && list.Count >= maxItems) {
                break;
            }
        }

        return list.Count == 0 ? Array.Empty<string>() : list.ToArray();
    }

    private static CapabilityTier MapTier(ToolCapabilityTier tier) {
        return tier switch {
            ToolCapabilityTier.ReadOnly => CapabilityTier.ReadOnly,
            ToolCapabilityTier.SensitiveRead => CapabilityTier.SensitiveRead,
            ToolCapabilityTier.DangerousWrite => CapabilityTier.DangerousWrite,
            _ => CapabilityTier.SensitiveRead
        };
    }

    private static string ResolvePackDisplayName(string? descriptorId, string? fallbackName) {
        var packId = NormalizePackId(descriptorId);
        return packId switch {
            "system" => "ComputerX",
            "ad" => "ADPlayground",
            "testimox" => "TestimoX",
            _ => string.IsNullOrWhiteSpace(fallbackName) ? string.Empty : fallbackName.Trim()
        };
    }

    private static ToolPackSourceKind MapSourceKind(string? sourceKind, string descriptorId) {
        var normalized = (sourceKind ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "builtin") {
            return ToolPackSourceKind.Builtin;
        }
        if (normalized is "closed_source" or "closed" or "private" or "internal") {
            return ToolPackSourceKind.ClosedSource;
        }
        if (normalized is "open_source" or "open" or "opensource" or "public") {
            return ToolPackSourceKind.OpenSource;
        }

        var packId = NormalizePackId(descriptorId);
        return packId switch {
            "eventlog" => ToolPackSourceKind.Builtin,
            "fs" => ToolPackSourceKind.Builtin,
            "powershell" => ToolPackSourceKind.Builtin,
            "reviewersetup" => ToolPackSourceKind.Builtin,
            "email" => ToolPackSourceKind.Builtin,
            "system" => ToolPackSourceKind.ClosedSource,
            "ad" => ToolPackSourceKind.ClosedSource,
            "testimox" => ToolPackSourceKind.ClosedSource,
            _ => ToolPackSourceKind.OpenSource
        };
    }

    private static string NormalizePackId(string? descriptorId) {
        var normalized = (descriptorId ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        normalized = normalized.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal);

        if (normalized.StartsWith("ix", StringComparison.Ordinal)) {
            normalized = normalized[2..];
        } else if (normalized.StartsWith("intelligencex", StringComparison.Ordinal)) {
            normalized = normalized["intelligencex".Length..];
        }

        return normalized switch {
            "computerx" => "system",
            "adplayground" => "ad",
            "activedirectory" => "ad",
            "filesystem" => "fs",
            _ => normalized
        };
    }

    private static string? LoadInstructions(ServiceOptions options) {
        string? path = null;
        if (!string.IsNullOrWhiteSpace(options.InstructionsFile)) {
            path = options.InstructionsFile.Trim();
        } else {
            path = Path.Combine(AppContext.BaseDirectory, "HostSystemPrompt.md");
        }

        string? instructions = null;
        try {
            if (File.Exists(path)) {
                var text = File.ReadAllText(path);
                instructions = string.IsNullOrWhiteSpace(text) ? null : text;
            }
        } catch {
            instructions = null;
        }

        var shaping = BuildShapingInstructions(options);
        if (string.IsNullOrWhiteSpace(shaping)) {
            return instructions;
        }
        if (string.IsNullOrWhiteSpace(instructions)) {
            return shaping;
        }
        return instructions + Environment.NewLine + Environment.NewLine + shaping;
    }

    private static string? BuildShapingInstructions(ServiceOptions options) {
        var maxTableRows = options.MaxTableRows;
        var maxSample = options.MaxSample;
        var redact = options.Redact;

        if (maxTableRows <= 0 && maxSample <= 0 && !redact) {
            return null;
        }

        var lines = new List<string> {
            "## Session Response Shaping",
            "Follow these display constraints for all assistant responses:"
        };

        if (maxTableRows > 0) {
            lines.Add($"- Max table rows: {maxTableRows} (show a preview, then offer to paginate/refine).");
        }
        if (maxSample > 0) {
            lines.Add($"- Max sample items: {maxSample} (for long lists, show a sample and counts).");
        }
        if (redact) {
            lines.Add("- Redaction: redact emails/UPNs in assistant output. Prefer summaries over raw identifiers.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static readonly Regex EmailRegex = new(@"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string RedactText(string text) {
        if (string.IsNullOrEmpty(text)) {
            return string.Empty;
        }
        // Best-effort: redact email/UPN-like tokens.
        return EmailRegex.Replace(text, "[redacted_email]");
    }

    private sealed class ChatRun {
        public ChatRun(string chatRequestId, CancellationTokenSource cts) {
            ChatRequestId = chatRequestId;
            Cts = cts;
        }

        public string ChatRequestId { get; }
        public string? ThreadId { get; set; }
        public CancellationTokenSource Cts { get; }
        public Task? Task { get; set; }
        public bool IsCompleted { get; private set; }

        public void Cancel() {
            try {
                Cts.Cancel();
            } catch {
                // Ignore.
            }
        }

        public void MarkCompleted() {
            IsCompleted = true;
            try {
                Cts.Dispose();
            } catch {
                // Ignore.
            }
        }
    }

    private sealed class LoginFlow {
        private readonly object _lock = new();
        private PendingPrompt? _pending;

        public LoginFlow(string loginId, string requestId, CancellationTokenSource cts) {
            LoginId = loginId;
            RequestId = requestId;
            Cts = cts;
        }

        public string LoginId { get; }
        public string RequestId { get; }
        public CancellationTokenSource Cts { get; }
        public Task? Task { get; set; }
        public bool IsCompleted { get; private set; }

        public void Cancel() {
            try {
                Cts.Cancel();
            } catch {
                // Ignore.
            }
            lock (_lock) {
                _pending?.Tcs.TrySetCanceled();
            }
        }

        public void MarkCompleted() {
            IsCompleted = true;
            try {
                Cts.Dispose();
            } catch {
                // Ignore.
            }
        }

        public void SetPendingPrompt(string promptId, TaskCompletionSource<string> tcs) {
            lock (_lock) {
                _pending = new PendingPrompt(promptId, tcs);
            }
        }

        public void ClearPendingPrompt(string promptId) {
            lock (_lock) {
                if (_pending is not null && string.Equals(_pending.PromptId, promptId, StringComparison.Ordinal)) {
                    _pending = null;
                }
            }
        }

        public bool TryCompletePrompt(string promptId, string input) {
            lock (_lock) {
                if (_pending is null || !string.Equals(_pending.PromptId, promptId, StringComparison.Ordinal)) {
                    return false;
                }
                return _pending.Tcs.TrySetResult(input);
            }
        }

        private sealed record PendingPrompt(string PromptId, TaskCompletionSource<string> Tcs);
    }
}
