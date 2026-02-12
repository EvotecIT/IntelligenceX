using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly ServiceOptions _options;
    private readonly Stream _stream;
    private readonly ToolRegistry _registry;
    private readonly IReadOnlyList<IToolPack> _packs;

    private readonly JsonSerializerOptions _json;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private readonly object _loginLock = new();
    private LoginFlow? _login;

    public ChatServiceSession(ServiceOptions options, Stream stream) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
            AllowedRoots = _options.AllowedRoots.ToArray(),
            AdDomainController = _options.AdDomainController,
            AdDefaultSearchBaseDn = _options.AdDefaultSearchBaseDn,
            AdMaxResults = _options.AdMaxResults,
            EnablePowerShellPack = _options.EnablePowerShellPack,
            PowerShellAllowWrite = _options.PowerShellAllowWrite,
            EnableTestimoXPack = _options.EnableTestimoXPack
        });
        _registry = new ToolRegistry();
        ToolPackBootstrap.RegisterAll(_registry, _packs);

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

        IDisposable? deltaSubscription = null;
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
                            Policy = BuildSessionPolicy(_options, _packs)
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

                    case ChatRequest chat:
                        if (string.IsNullOrWhiteSpace(chat.Text)) {
                            await WriteAsync(writer, new ErrorMessage {
                                Kind = ChatServiceMessageKind.Response,
                                RequestId = request.RequestId,
                                Error = "Text cannot be empty.",
                                Code = "invalid_argument"
                            }, cancellationToken).ConfigureAwait(false);
                            break;
                        }

                        try {
                            // Determine the thread before subscribing to deltas.
                            activeThreadId = await EnsureThreadAsync(client, chat.ThreadId, activeThreadId, chat.Options?.Model, cancellationToken)
                                .ConfigureAwait(false);

                            // Correlate deltas to the active requestId. Only one in-flight chat is supported for now.
                            deltaSubscription?.Dispose();
                            var threadIdForDelta = activeThreadId ?? string.Empty;
                            deltaSubscription = client.SubscribeDelta(delta => {
                                _ = TryWriteDeltaAsync(writer, request.RequestId, threadIdForDelta, delta);
                            });

                            var result = await RunChatOnCurrentThreadAsync(client, writer, chat, activeThreadId!, cancellationToken).ConfigureAwait(false);
                            await WriteAsync(writer, result, cancellationToken).ConfigureAwait(false);
                        } catch (OpenAIAuthenticationRequiredException) {
                            await WriteAsync(writer, new ErrorMessage {
                                Kind = ChatServiceMessageKind.Response,
                                RequestId = request.RequestId,
                                Error = "Not authenticated. Run ChatGPT login in a client that can persist ~/.intelligencex/auth.json, then reconnect.",
                                Code = "not_authenticated"
                            }, cancellationToken).ConfigureAwait(false);
                        }
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
            deltaSubscription?.Dispose();
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
            tools[i] = new ToolDefinitionDto {
                Name = defs[i].Name,
                Description = defs[i].Description ?? string.Empty,
                ParametersJson = parametersJson,
                RequiredArguments = ExtractRequiredArguments(parametersJson)
            };
        }
        await WriteAsync(writer, new ToolListMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = requestId,
            Tools = tools
        }, cancellationToken).ConfigureAwait(false);
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

        var toolDefs = _registry.GetDefinitions();
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

        await TryWriteStatusAsync(writer, request.RequestId, threadId, status: "thinking").ConfigureAwait(false);
        TurnInfo turn = await client.ChatAsync(ChatInput.FromText(request.Text), options, turnToken).ConfigureAwait(false);

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
            foreach (var output in executed) {
                toolOutputs.Add(new ToolOutputDto { CallId = output.CallId, Output = output.Output });
            }

            var next = new ChatInput();
            foreach (var output in executed) {
                next.AddToolOutput(output.CallId, output.Output);
            }
            options.NewThread = false;
            await TryWriteStatusAsync(writer, request.RequestId, threadId, status: "thinking").ConfigureAwait(false);
            turn = await client.ChatAsync(next, options, turnToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException($"Tool runner exceeded max rounds ({maxRounds}).");
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

            var meta = TryExtractToolOutputMetadata(output);
            return new ToolOutputDto {
                CallId = call.CallId,
                Output = output,
                Ok = meta.Ok,
                ErrorCode = meta.ErrorCode,
                Error = meta.Error,
                Hints = meta.Hints,
                IsTransient = meta.IsTransient,
                SummaryMarkdown = meta.SummaryMarkdown,
                MetaJson = meta.MetaJson,
                RenderJson = meta.RenderJson
            };
        }
        using var toolCts = CreateTimeoutCts(cancellationToken, toolTimeoutSeconds);
        var toolToken = toolCts?.Token ?? cancellationToken;
        try {
            var output = await tool.InvokeAsync(call.Arguments, toolToken).ConfigureAwait(false);
            var text = output ?? string.Empty;
            if (_options.Redact) {
                text = RedactText(text);
            }
            var meta = TryExtractToolOutputMetadata(text);
            return new ToolOutputDto {
                CallId = call.CallId,
                Output = text,
                Ok = meta.Ok,
                ErrorCode = meta.ErrorCode,
                Error = meta.Error,
                Hints = meta.Hints,
                IsTransient = meta.IsTransient,
                SummaryMarkdown = meta.SummaryMarkdown,
                MetaJson = meta.MetaJson,
                RenderJson = meta.RenderJson
            };
        } catch (OperationCanceledException) when (toolCts is not null && toolCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
            var output = ToolOutputEnvelope.Error(
                errorCode: "tool_timeout",
                error: $"Tool '{call.Name}' timed out after {toolTimeoutSeconds}s.",
                hints: new[] { "Increase toolTimeoutSeconds, or narrow the query (OU scoping, tighter filters)." },
                isTransient: true);

            var meta = TryExtractToolOutputMetadata(output);
            return new ToolOutputDto {
                CallId = call.CallId,
                Output = output,
                Ok = meta.Ok,
                ErrorCode = meta.ErrorCode,
                Error = meta.Error,
                Hints = meta.Hints,
                IsTransient = meta.IsTransient,
                SummaryMarkdown = meta.SummaryMarkdown,
                MetaJson = meta.MetaJson,
                RenderJson = meta.RenderJson
            };
        } catch (Exception ex) {
            var output = ToolOutputEnvelope.Error(
                errorCode: "tool_exception",
                error: $"{ex.GetType().Name}: {ex.Message}",
                hints: new[] { "Try again. If it keeps failing, narrow the query and capture tool args/output." },
                isTransient: false);

            var meta = TryExtractToolOutputMetadata(output);
            return new ToolOutputDto {
                CallId = call.CallId,
                Output = output,
                Ok = meta.Ok,
                ErrorCode = meta.ErrorCode,
                Error = meta.Error,
                Hints = meta.Hints,
                IsTransient = meta.IsTransient,
                SummaryMarkdown = meta.SummaryMarkdown,
                MetaJson = meta.MetaJson,
                RenderJson = meta.RenderJson
            };
        }
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
                var arr = obj.GetArray("hints");
                if (arr is not null && arr.Count > 0) {
                    var list = new List<string>(arr.Count);
                    foreach (var item in arr) {
                        var s = item?.AsString();
                        if (!string.IsNullOrWhiteSpace(s)) {
                            list.Add(s!);
                        }
                    }
                    hints = list.Count > 0 ? list.ToArray() : null;
                }
            } catch {
                hints = null;
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

            if (ok is null && errorCode is null && error is null && hints is null && isTransient is null && summaryMarkdown is null && metaJson is null && renderJson is null) {
                return default;
            }

            return new ToolOutputMetadata(ok, errorCode, error, hints, isTransient, summaryMarkdown, metaJson, renderJson);
        } catch {
            return default;
        }
    }

    private readonly record struct ToolOutputMetadata(
        bool? Ok,
        string? ErrorCode,
        string? Error,
        string[]? Hints,
        bool? IsTransient,
        string? SummaryMarkdown,
        string? MetaJson,
        string? RenderJson);

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

    private static SessionPolicyDto BuildSessionPolicy(ServiceOptions options, IEnumerable<IToolPack> packs) {
        var roots = options.AllowedRoots.Count == 0 ? Array.Empty<string>() : options.AllowedRoots.ToArray();

        var packList = new List<ToolPackInfoDto>();
        foreach (var pack in ToolPackBootstrap.GetDescriptors(packs)) {
            packList.Add(new ToolPackInfoDto {
                Id = pack.Id,
                Name = pack.Name,
                Tier = MapTier(pack.Tier),
                Enabled = true,
                IsDangerous = pack.IsDangerous || pack.Tier == ToolCapabilityTier.DangerousWrite
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
            Redact = options.Redact
        };
    }

    private static CapabilityTier MapTier(ToolCapabilityTier tier) {
        return tier switch {
            ToolCapabilityTier.ReadOnly => CapabilityTier.ReadOnly,
            ToolCapabilityTier.SensitiveRead => CapabilityTier.SensitiveRead,
            ToolCapabilityTier.DangerousWrite => CapabilityTier.DangerousWrite,
            _ => CapabilityTier.SensitiveRead
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
